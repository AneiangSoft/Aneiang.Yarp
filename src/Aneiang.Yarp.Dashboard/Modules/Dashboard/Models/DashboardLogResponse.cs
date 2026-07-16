using System.Text.Json.Serialization;
using Aneiang.Yarp.Dashboard.Modules.ProxyLog.Models;

namespace Aneiang.Yarp.Dashboard.Modules.Dashboard.Models;

/// <summary>
/// Log response with log entries snapshot.
/// </summary>
public class DashboardLogResponse
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("data")]
    public ProxyLogStoreSnapshot? Data { get; set; }
}
