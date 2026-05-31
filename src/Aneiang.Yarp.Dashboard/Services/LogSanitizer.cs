using Aneiang.Yarp.Dashboard.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Aneiang.Yarp.Dashboard.Services;

/// <summary>
/// Service for sanitizing sensitive information from log entries.
/// Handles header blacklisting, query parameter filtering, and JSON field sanitization.
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

    private readonly DashboardOptions _options;
    private readonly HashSet<string> _headerBlacklist;
    private readonly HashSet<string> _jsonFieldSanitizeList;

    /// <summary>
    /// Initializes a new instance of LogSanitizer.
    /// </summary>
    /// <param name="options">Dashboard options.</param>
    public LogSanitizer(IOptions<DashboardOptions> options)
    {
        _options = options.Value;

        _headerBlacklist = new HashSet<string>(
            _options.LogHeaderBlacklist ?? (IEnumerable<string>)_defaultHeaderBlacklist,
            StringComparer.OrdinalIgnoreCase);

        _jsonFieldSanitizeList = new HashSet<string>(
            _options.LogJsonFieldSanitizeList ?? (IEnumerable<string>)_defaultJsonFieldSanitizeList,
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Sanitizes request/response headers by removing blacklisted headers.
    /// </summary>
    /// <param name="headers">Original headers.</param>
    /// <returns>Sanitized headers.</returns>
    public Dictionary<string, string>? SanitizeHeaders(IHeaderDictionary? headers)
    {
        if (headers == null)
            return null;

        var sanitized = new Dictionary<string, string>();

        foreach (var header in headers)
        {
            if (!_headerBlacklist.Contains(header.Key))
            {
                sanitized[header.Key] = header.Value.ToString();
            }
            else
            {
                sanitized[header.Key] = "***REDACTED***";
            }
        }

        return sanitized;
    }

    /// <summary>
    /// Sanitizes query string by removing blacklisted parameters.
    /// </summary>
    /// <param name="queryString">Original query string.</param>
    /// <returns>Sanitized query string.</returns>
    public string? SanitizeQueryString(string? queryString)
    {
        if (string.IsNullOrEmpty(queryString))
            return queryString;

        if (_options.LogQueryBlacklist == null || _options.LogQueryBlacklist.Count == 0)
            return queryString;

        var blacklist = new HashSet<string>(_options.LogQueryBlacklist, StringComparer.OrdinalIgnoreCase);
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(queryString);

        var sanitized = new Dictionary<string, string>();
        foreach (var param in query)
        {
            if (blacklist.Contains(param.Key))
            {
                sanitized[param.Key] = "***REDACTED***";
            }
            else
            {
                sanitized[param.Key] = param.Value.ToString();
            }
        }

        return "?" + string.Join("&", sanitized.Select(kvp => $"{kvp.Key}={kvp.Value}"));
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

        if (text.Length <= _options.LogMaxBodyLength)
            return text;

        truncated = true;
        return text.Substring(0, _options.LogMaxBodyLength) + "\n... [TRUNCATED]";
    }

    /// <summary>
    /// Recursively sanitizes JSON element.
    /// </summary>
    private object? SanitizeJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => SanitizeJsonObject(element),
            JsonValueKind.Array => element.EnumerateArray().Select(SanitizeJsonElement).ToList(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetDecimal(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => element.GetRawText()
        };
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
