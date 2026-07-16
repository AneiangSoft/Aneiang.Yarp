namespace Aneiang.Yarp.Storage;

public class NotificationGlobalSettings
{
    public bool Enabled { get; set; } = true;
    public int MaxHistoryRecords { get; set; } = 500;
    public int DefaultTimeoutSeconds { get; set; } = 10;
    public int DefaultRetryCount { get; set; } = 1;
    public string Locale { get; set; } = "zh-CN";
}
