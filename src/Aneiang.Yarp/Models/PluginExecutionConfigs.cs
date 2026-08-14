using System.Text.Json.Serialization;

namespace Aneiang.Yarp.Models;

// ──────────────── Route-scoped plugin binding configs ────────────────

/// <summary>Route-scoped WAF configuration stored in a plugin binding.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class WafBindingOptions
{
    public bool Enabled { get; set; } = true;
    public List<string> IpWhitelist { get; set; } = [];
    public List<string> IpBlacklist { get; set; } = [];
    public long MaxRequestBodySize { get; set; } = 10 * 1024 * 1024;
    public int MaxHeaderCount { get; set; } = 64;
    public int MaxHeaderSize { get; set; } = 8192;
    public bool EnableSqlInjectionDetection { get; set; } = true;
    public bool EnableXssDetection { get; set; } = true;
    public bool EnablePathTraversalDetection { get; set; } = true;
    public bool EnableIpCheck { get; set; } = true;
    public bool EnableRequestSizeValidation { get; set; } = true;
}

/// <summary>Route-scoped request retry configuration stored in a plugin binding.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class RequestRetryBindingOptions
{
    public bool Enabled { get; set; } = true;
    public int MaxRetries { get; set; } = 3;
    public int BackoffBaseMs { get; set; } = 100;
    public int BackoffJitterMs { get; set; } = 50;
    public int TimeoutSeconds { get; set; } = 30;
    public bool RetryOnExceptions { get; set; } = true;
    public bool UseDifferentDestination { get; set; }
    public bool RetryNonIdempotent { get; set; }
    public List<int> RetryOnStatusCodes { get; set; } = [502, 503, 504];
}

// ──────────────── Compiled execution configs ────────────────

public sealed record RateLimitExecutionConfig
{
    public bool Enabled { get; init; }
    public RateLimitAlgorithm Algorithm { get; init; } = RateLimitAlgorithm.FixedWindow;
    public int PermitLimit { get; init; } = 100;
    public string Window { get; init; } = "1m";
    public int QueueLimit { get; init; }
    public string PartitionKey { get; init; } = "IpAddress";
    public int SegmentsPerWindow { get; init; } = 4;
    public int TokenLimit { get; init; } = 100;
    public int TokensPerPeriod { get; init; } = 100;
    public string ReplenishmentPeriod { get; init; } = "1s";
    public string RouteUid { get; init; } = string.Empty;
}

/// <summary>Compiled route-scoped configuration for the Redis distributed rate-limit plugin.</summary>
public sealed record RedisRateLimitExecutionConfig
{
    public bool Enabled { get; init; } = true;
    public string? RedisConnectionString { get; init; }
    public string Algorithm { get; init; } = "FixedWindow";
    public int Limit { get; init; } = 100;
    public int WindowSeconds { get; init; } = 60;
    public string KeyPrefix { get; init; } = "aneiang:rl";
    public int BurstBalance { get; init; }
}

public sealed record ProxyLogBindingExecutionConfig
{
    public bool? CaptureRequestHeaders { get; init; }
    public bool? CaptureResponseHeaders { get; init; }
    public bool? RequestBodyCaptureEnabled { get; init; }
    public bool? EnableRequestBodyCapture { get; init; }
    public bool? EnableProxyRequestBodyCapture { get; init; }
    public bool? ResponseBodyCaptureEnabled { get; init; }
    public bool? EnableResponseBodyCapture { get; init; }
    public bool? EnableProxyResponseBodyCapture { get; init; }
    public int? MaxBodyLength { get; init; }
    public int? LogMaxBodyLength { get; init; }
    public int? MaxBodyBufferBytes { get; init; }
    public int? LogMaxBodyBufferBytes { get; init; }
    public bool? ErrorsOnly { get; init; }
    public bool? LogErrorsOnly { get; init; }
    public bool? SamplingEnabled { get; init; }
    public bool? EnableSampling { get; init; }
    public bool? EnableLogSampling { get; init; }
    public double? SamplingRate { get; init; }
    public double? LogSamplingRate { get; init; }
}

public sealed record ResponseCacheExecutionConfig
{
    public bool Enabled { get; init; } = true;
    public int TtlSeconds { get; init; } = 60;
    public int MaxBodyBytes { get; init; } = 1_048_576;
    public bool VaryByQuery { get; init; } = true;
    public string[] VaryHeaders { get; init; } = [];
    public int[] CacheStatusCodes { get; init; } = [200];
}

public sealed record MetricsExecutionConfig
{
    public bool Enabled { get; init; } = true;
    public bool IncludeRequestBytes { get; init; } = true;
    public bool IncludeResponseBytes { get; init; } = true;
    public bool IncludeDestination { get; init; } = true;
}

/// <summary>Route-scoped response compression configuration stored in a plugin binding.</summary>
public sealed record CompressionExecutionConfig
{
    public bool Enabled { get; init; } = true;
    public int MinResponseSize { get; init; } = 1024;
    public string CompressionLevel { get; init; } = "Optimal";
    public string[] MimeTypes { get; init; } =
    [
        "text/plain",
        "text/css",
        "text/javascript",
        "application/json",
        "application/xml",
        "text/xml",
        "application/javascript",
        "image/svg+xml"
    ];
}

public sealed record ServiceDiscoveryExecutionConfig
{
    public bool Enabled { get; init; } = true;
    public string Mode { get; init; } = "Static";
    public string[] StaticEndpoints { get; init; } = [];
    public string? Endpoint { get; init; }
    public string? ServiceName { get; init; }
    public string Namespace { get; init; } = "default";
    public string Scheme { get; init; } = "http";
    public int RefreshSeconds { get; init; } = 30;
    public int RequestTimeoutSeconds { get; init; } = 5;
}
