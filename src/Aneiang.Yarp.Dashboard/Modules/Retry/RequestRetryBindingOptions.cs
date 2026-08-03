using System.Text.Json.Serialization;

namespace Aneiang.Yarp.Dashboard.Modules.Retry;

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
