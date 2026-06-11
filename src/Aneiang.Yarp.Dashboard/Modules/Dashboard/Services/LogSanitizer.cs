using Aneiang.Yarp.Dashboard.Infrastructure.Performance;
using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Dashboard.Infrastructure.Auth;
using Aneiang.Yarp.Dashboard.Modules.ProxyLog.Models;
using Aneiang.Yarp.Dashboard.Modules.Waf.Models;
using Aneiang.Yarp.Dashboard.Modules.Policy.Models;
using Aneiang.Yarp.Dashboard.Modules.Alert.Models;
using Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Models;
using Aneiang.Yarp.Dashboard.Modules.Webhook.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Aneiang.Yarp.Dashboard.Modules.Dashboard.Services;

/// <summary>
/// Service for sanitizing sensitive information from log entries.
/// Handles header blacklisting, query parameter filtering, and JSON field sanitization.
/// Optimized with cached HashSets and pre-allocated collections.
/// </summary>
public sealed class LogSanitizer
{
    private static readonly HashSet<string> _defaultHeaderBlacklist = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization", "Cookie", "Set-Cookie", "X-Api-Key", "X-Auth-Token"
    };

    private static readonly HashSet<string> _defaultJsonFieldSanitizeList = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "token", "secret", "apikey", "api-key", "access_token", "refresh_token"
    };

    // Reuse JsonSerializerOptions instance to avoid allocations
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = false
    };

    // Cached options values to avoid repeated property access
    private readonly int _maxBodyLength;
    private readonly HashSet<string> _headerBlacklist;
    private readonly HashSet<string> _jsonFieldSanitizeList;
    private readonly HashSet<string>? _queryBlacklist; // Cached to avoid creating on each call
    private readonly bool _hasQueryBlacklist;

    /// <summary>
    /// Initializes a new instance of LogSanitizer.
    /// Pre-computes and caches all hash sets for optimal performance.
    /// </summary>
    /// <param name="options">Dashboard options.</param>
    public LogSanitizer(IOptions<DashboardOptions> options)
    {
        var opt = options.Value;
        _maxBodyLength = opt.LogMaxBodyLength;

        // Build header blacklist - merged default + custom
        _headerBlacklist = new HashSet<string>(
            opt.LogHeaderBlacklist ?? (IEnumerable<string>)_defaultHeaderBlacklist,
            StringComparer.OrdinalIgnoreCase);

        // Build JSON field sanitize list - merged default + custom
        _jsonFieldSanitizeList = new HashSet<string>(
            opt.LogJsonFieldSanitizeList ?? (IEnumerable<string>)_defaultJsonFieldSanitizeList,
            StringComparer.OrdinalIgnoreCase);

        // Cache query blacklist to avoid rebuilding on every SanitizeQueryString call
        if (opt.LogQueryBlacklist != null && opt.LogQueryBlacklist.Count > 0)
        {
            _queryBlacklist = new HashSet<string>(opt.LogQueryBlacklist, StringComparer.OrdinalIgnoreCase);
            _hasQueryBlacklist = true;
        }
    }

    /// <summary>
    /// Sanitizes request/response headers by removing blacklisted headers.
    /// Uses pre-allocated capacity for efficiency.
    /// </summary>
    /// <param name="headers">Original headers.</param>
    /// <returns>Sanitized headers.</returns>
    public Dictionary<string, string>? SanitizeHeaders(IHeaderDictionary? headers)
    {
        if (headers == null)
            return null;

        // Pre-allocate with expected capacity
        var sanitized = new Dictionary<string, string>(headers.Count);

        foreach (var header in headers)
        {
            sanitized[header.Key] = _headerBlacklist.Contains(header.Key)
                ? "***REDACTED***"
                : header.Value.ToString();
        }

        return sanitized;
    }

    /// <summary>
    /// Sanitizes query string by removing blacklisted parameters.
    /// Uses cached blacklist for optimal performance.
    /// </summary>
    /// <param name="queryString">Original query string.</param>
    /// <returns>Sanitized query string.</returns>
    public string? SanitizeQueryString(string? queryString)
    {
        if (string.IsNullOrEmpty(queryString))
            return queryString;

        if (!_hasQueryBlacklist)
            return queryString;

        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(queryString);
        if (query.Count == 0)
            return queryString;

        // Pre-allocate with expected capacity
        var sanitized = new Dictionary<string, string>(query.Count);
        foreach (var param in query)
        {
            sanitized[param.Key] = _queryBlacklist!.Contains(param.Key)
                ? "***REDACTED***"
                : param.Value.ToString();
        }

        // Use pooled StringBuilder for efficient string concatenation
        var sb = PooledStringBuilder.Rent();
        try
        {
            sb.Append('?');
            var first = true;
            foreach (var kvp in sanitized)
            {
                if (!first) sb.Append('&');
                sb.Append(kvp.Key).Append('=').Append(kvp.Value);
                first = false;
            }
            return sb.ToString();
        }
        finally
        {
            PooledStringBuilder.Return(sb);
        }
    }

    /// <summary>
    /// Sanitizes JSON body by masking sensitive fields.
    /// </summary>
    /// <param name="body">Original JSON body.</param>
    /// <returns>Sanitized JSON body.</returns>
    public string? SanitizeJsonBody(string? body)
    {
        if (string.IsNullOrEmpty(body))
            return body;

        try
        {
            using var doc = JsonDocument.Parse(body);
            var sanitized = SanitizeJsonElement(doc.RootElement);
            return JsonSerializer.Serialize(sanitized, _jsonOptions);
        }
        catch (JsonException)
        {
            // If not valid JSON, return as-is
            return body;
        }
    }

    /// <summary>
    /// Truncates text to maximum allowed length.
    /// </summary>
    /// <param name="text">Original text.</param>
    /// <param name="truncated">Output indicating if text was truncated.</param>
    /// <returns>Truncated text.</returns>
    public string? TruncateText(string? text, out bool truncated)
    {
        truncated = false;

        if (string.IsNullOrEmpty(text))
            return text;

        if (text.Length <= _maxBodyLength)
            return text;

        truncated = true;
        return text.Substring(0, _maxBodyLength) + "\n... [TRUNCATED]";
    }

    /// <summary>
    /// Recursively sanitizes JSON element.
    /// </summary>
    private object? SanitizeJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => SanitizeJsonObject(element),
            JsonValueKind.Array => SanitizeJsonArray(element),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetDecimal(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => element.GetRawText()
        };
    }

    /// <summary>
    /// Sanitizes JSON array elements.
    /// </summary>
    private List<object?> SanitizeJsonArray(JsonElement element)
    {
        var array = element.EnumerateArray();
        var result = new List<object?>();
        foreach (var item in array)
        {
            result.Add(SanitizeJsonElement(item));
        }
        return result;
    }

    /// <summary>
    /// Sanitizes JSON object by masking sensitive fields.
    /// </summary>
    private Dictionary<string, object?> SanitizeJsonObject(JsonElement element)
    {
        var result = new Dictionary<string, object?>();

        foreach (var property in element.EnumerateObject())
        {
            if (_jsonFieldSanitizeList.Contains(property.Name))
            {
                result[property.Name] = "***REDACTED***";
            }
            else
            {
                result[property.Name] = SanitizeJsonElement(property.Value);
            }
        }

        return result;
    }
}
