namespace Aneiang.Yarp.Storage;

public class WafSettingsEntity
{
    public int Id { get; set; } = 1;
    public bool Enabled { get; set; }
    public bool EnableIpCheck { get; set; } = true;
    public string? IpWhitelistJson { get; set; }
    public string? IpBlacklistJson { get; set; }
    public bool EnableRequestSizeValidation { get; set; } = true;
    public long MaxRequestBodySize { get; set; } = 10 * 1024 * 1024;
    public int MaxHeaderCount { get; set; } = 64;
    public int MaxHeaderSize { get; set; } = 8192;
    public bool EnableSqlInjectionDetection { get; set; } = true;
    public bool EnableXssDetection { get; set; } = true;
    public bool EnablePathTraversalDetection { get; set; } = true;
    public string? ExtraScriptSources { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
