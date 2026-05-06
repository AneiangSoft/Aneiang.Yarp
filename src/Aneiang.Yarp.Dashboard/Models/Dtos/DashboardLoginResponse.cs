using System.Text.Json.Serialization;

namespace Aneiang.Yarp.Dashboard.Models.Dtos;

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

/// <summary>
/// Log response with log entries snapshot.
/// </summary>
public class DashboardLogResponse
{
    /// <summary>Response code.</summary>
    [JsonPropertyName("code")]
    public int Code { get; set; }

    /// <summary>Log snapshot data.</summary>
    [JsonPropertyName("data")]
    public ProxyLogStoreSnapshot? Data { get; set; }
}

/// <summary>
/// Generic API response wrapper.
/// </summary>
/// <typeparam name="T">Data type.</typeparam>
public class DashboardApiResponse<T>
{
    /// <summary>Response code.</summary>
    [JsonPropertyName("code")]
    public int Code { get; set; }

    /// <summary>Response data.</summary>
    [JsonPropertyName("data")]
    public T? Data { get; set; }

    /// <summary>Response message.</summary>
    [JsonPropertyName("message")]
    public string? Message { get; set; }
}
