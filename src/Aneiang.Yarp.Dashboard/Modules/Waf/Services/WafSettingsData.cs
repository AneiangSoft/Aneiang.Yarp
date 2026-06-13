namespace Aneiang.Yarp.Dashboard.Modules.Waf.Services;

/// <summary>Data model for persisted WAF settings.</summary>
public class WafSettingsData
{
    public bool Enabled { get; set; }
    public bool EnableIpCheck { get; set; } = true;
    public List<string> IpWhitelist { get; set; } = [];
    public List<string> IpBlacklist { get; set; } = [];
    public bool EnableRequestSizeValidation { get; set; } = true;
    public long MaxRequestBodySize { get; set; } = 10 * 1024 * 1024;
    public int MaxHeaderCount { get; set; } = 64;
    public int MaxHeaderSize { get; set; } = 8192;
    public bool EnableSqlInjectionDetection { get; set; } = true;
    public bool EnableXssDetection { get; set; } = true;
    public bool EnablePathTraversalDetection { get; set; } = true;
}
