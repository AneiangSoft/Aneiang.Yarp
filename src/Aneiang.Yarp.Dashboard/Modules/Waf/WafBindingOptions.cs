using System.Text.Json.Serialization;

namespace Aneiang.Yarp.Dashboard.Modules.Waf;

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
