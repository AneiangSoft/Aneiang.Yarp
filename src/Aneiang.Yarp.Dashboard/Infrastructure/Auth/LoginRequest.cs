namespace Aneiang.Yarp.Dashboard.Infrastructure.Auth;

/// <summary>Login credentials for dashboard authentication.</summary>
public class LoginRequest
{
    /// <summary>Username.</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>Password.</summary>
    public string Password { get; set; } = string.Empty;
}
