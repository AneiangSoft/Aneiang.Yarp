using System.Text.Json.Serialization;

namespace Aneiang.Yarp.Dashboard.Modules.Dashboard.Models;

/// <summary>
/// Login response with JWT token.
/// </summary>
public class DashboardLoginResponse
{
    /// <summary>Response code.</summary>
    [JsonPropertyName("code")]
    public int Code { get; set; }

    /// <summary>Response message.</summary>
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    /// <summary>JWT token.</summary>
    [JsonPropertyName("token")]
    public string? Token { get; set; }
}
