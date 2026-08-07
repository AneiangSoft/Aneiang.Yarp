using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Plugin.ProxyLog;
using Aneiang.Yarp.Plugin.ProxyLog.Services;
using Aneiang.Yarp.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.ComponentModel.DataAnnotations;

namespace Aneiang.Yarp.Dashboard.Modules.Dashboard.Controllers;

/// <summary>
/// System settings API: view and update Dashboard and ProxyLog settings.
/// </summary>
[ApiController]
[Route("api/settings")]
public sealed class DashboardSettingsController(
    IOptions<DashboardOptions> dashboardOptions,
    IOptions<ProxyLogOptions> proxyLogOptions,
    IOptions<StorageOptions> storageOptions,
    ProxyLogRuntimeSettings runtimeSettings) : ControllerBase
{
    [HttpGet]
    public IActionResult GetSettings()
    {
        var dash = dashboardOptions.Value;
        var log = proxyLogOptions.Value;
        var storage = storageOptions.Value;

        return Ok(new
        {
            locale = dash.Locale,
            routePrefix = dash.RoutePrefix,
            auth = new
            {
                authMode = dash.Auth.AuthMode.ToString(),
                hasApiKey = !string.IsNullOrEmpty(dash.Auth.ApiKey),
                apiKeyHeaderName = dash.Auth.ApiKeyHeaderName,
                jwtUsername = dash.Auth.JwtUsername,
                enableTwoFactor = dash.Auth.EnableTwoFactor,
                minPasswordLength = dash.Auth.MinPasswordLength
            },
            proxyLog = new
            {
                logPersistenceEnabled = log.LogPersistenceEnabled,
                logBufferCapacity = log.LogBufferCapacity,
                logMetaRetentionDays = log.LogMetaRetentionDays,
                logBodyRetentionDays = log.LogBodyRetentionDays,
                enableProxyRequestBodyCapture = log.EnableProxyRequestBodyCapture,
                enableProxyResponseBodyCapture = log.EnableProxyResponseBodyCapture,
                logMaxBodyLength = log.LogMaxBodyLength,
                enableLogSampling = log.EnableLogSampling,
                logSamplingRate = log.LogSamplingRate,
                logErrorsOnly = log.LogErrorsOnly,
                minLogLevel = log.MinLogLevel
            },
            storage = new
            {
                provider = "SQLite",
                sqliteConnectionString = MaskConnectionString(storage.Sqlite?.ConnectionString)
            }
        });
    }

    [HttpPost("locale")]
    public IActionResult SetLocale([FromBody] LocaleUpdateRequest request)
    {
        if (request?.Locale != "zh-CN" && request?.Locale != "en-US")
            return BadRequest(new { message = "Invalid locale. Must be 'zh-CN' or 'en-US'." });

        Response.Cookies.Append("dashboard_locale", request.Locale, new CookieOptions
        {
            Expires = DateTimeOffset.UtcNow.AddYears(1),
            HttpOnly = true,
            SameSite = SameSiteMode.Lax
        });

        return Ok(new { message = "Locale updated", locale = request.Locale });
    }

    /// <summary>
    /// Hot-reload ProxyLog settings without restarting the application.
    /// Only fields that are read at runtime via ProxyLogRuntimeSettings can be updated.
    /// </summary>
    [HttpPut("proxy-log")]
    public IActionResult UpdateProxyLogSettings([FromBody] UpdateProxyLogSettingsRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var current = runtimeSettings.Current;

        var snapshot = new ProxyLogRuntimeSnapshot(
            PersistenceEnabled: request.LogPersistenceEnabled ?? current.PersistenceEnabled,
            MetaRetentionDays: ValidateRange(request.LogMetaRetentionDays, current.MetaRetentionDays, 1, 365),
            BodyRetentionDays: ValidateRange(request.LogBodyRetentionDays, current.BodyRetentionDays, 1, 365),
            RequestBodyCaptureEnabled: request.EnableProxyRequestBodyCapture ?? current.RequestBodyCaptureEnabled,
            ResponseBodyCaptureEnabled: request.EnableProxyResponseBodyCapture ?? current.ResponseBodyCaptureEnabled,
            MaxBodyLength: ValidateRange(request.LogMaxBodyLength, current.MaxBodyLength, 0, 1_048_576),
            MaxBodyBufferBytes: ValidateRange(request.LogMaxBodyBufferBytes, current.MaxBodyBufferBytes, 0, 1_048_576),
            SamplingEnabled: request.EnableLogSampling ?? current.SamplingEnabled,
            SamplingRate: ValidateRange(request.LogSamplingRate, current.SamplingRate, 0.0, 1.0),
            ErrorsOnly: request.LogErrorsOnly ?? current.ErrorsOnly,
            MinLogLevelNumeric: ParseLogLevelNumeric(request.MinLogLevel, current.MinLogLevelNumeric));

        runtimeSettings.Update(snapshot);

        return Ok(new
        {
            message = "ProxyLog settings updated (hot-reload)",
            settings = new
            {
                logPersistenceEnabled = snapshot.PersistenceEnabled,
                logMetaRetentionDays = snapshot.MetaRetentionDays,
                logBodyRetentionDays = snapshot.BodyRetentionDays,
                enableProxyRequestBodyCapture = snapshot.RequestBodyCaptureEnabled,
                enableProxyResponseBodyCapture = snapshot.ResponseBodyCaptureEnabled,
                logMaxBodyLength = snapshot.MaxBodyLength,
                logMaxBodyBufferBytes = snapshot.MaxBodyBufferBytes,
                enableLogSampling = snapshot.SamplingEnabled,
                logSamplingRate = snapshot.SamplingRate,
                logErrorsOnly = snapshot.ErrorsOnly,
                minLogLevel = NumericToLogLevelString(snapshot.MinLogLevelNumeric)
            }
        });
    }

    private static int ValidateRange(int? value, int fallback, int min, int max) =>
        value.HasValue ? Math.Clamp(value.Value, min, max) : fallback;

    private static double ValidateRange(double? value, double fallback, double min, double max) =>
        value.HasValue ? Math.Clamp(value.Value, min, max) : fallback;

    private static int ParseLogLevelNumeric(string? level, int fallback) => level switch
    {
        "Critical" => 4,
        "Error" => 3,
        "Warning" => 2,
        "Information" => 1,
        "Debug" or "Trace" => 0,
        _ => fallback
    };

    private static string NumericToLogLevelString(int numeric) => numeric switch
    {
        4 => "Critical",
        3 => "Error",
        2 => "Warning",
        1 => "Information",
        _ => "Debug"
    };

    private static string MaskConnectionString(string? cs)
    {
        if (string.IsNullOrEmpty(cs)) return "(not configured)";
        // Show only the file path part, mask password if any
        var idx = cs.IndexOf("Password=", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            var end = cs.IndexOf(';', idx);
            if (end < 0) end = cs.Length;
            return cs[..idx] + "Password=****" + cs[end..];
        }
        return cs;
    }
}

public sealed record LocaleUpdateRequest(string Locale);

/// <summary>
/// Request to hot-reload ProxyLog settings. All fields optional — null means "keep current".
/// </summary>
public sealed class UpdateProxyLogSettingsRequest
{
    public bool? LogPersistenceEnabled { get; set; }
    [Range(1, 365)] public int? LogMetaRetentionDays { get; set; }
    [Range(1, 365)] public int? LogBodyRetentionDays { get; set; }
    public bool? EnableProxyRequestBodyCapture { get; set; }
    public bool? EnableProxyResponseBodyCapture { get; set; }
    [Range(0, 1048576)] public int? LogMaxBodyLength { get; set; }
    [Range(0, 1048576)] public int? LogMaxBodyBufferBytes { get; set; }
    public bool? EnableLogSampling { get; set; }
    [Range(0.0, 1.0)] public double? LogSamplingRate { get; set; }
    public bool? LogErrorsOnly { get; set; }
    public string? MinLogLevel { get; set; }
}
