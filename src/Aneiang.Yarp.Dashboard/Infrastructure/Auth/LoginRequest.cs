namespace Aneiang.Yarp.Dashboard.Infrastructure.Auth;

/// <summary>Login credentials for dashboard authentication.</summary>
public class LoginRequest
{
    /// <summary>Username.</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>Password.</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>TOTP 2FA code (6 digits). Required when 2FA is enabled.</summary>
    public string? TwoFactorCode { get; set; }
}
