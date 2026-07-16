namespace Aneiang.Yarp.Models;

public class GatewayApiAuthOptions
{
    public const string SectionName = "Gateway:ApiAuth";

    public GatewayApiAuthMode Mode { get; set; } = GatewayApiAuthMode.None;

    public string? Username { get; set; }

    public string? Password { get; set; }

    public string? ApiKey { get; set; }

    public string ApiKeyHeaderName { get; set; } = "X-Api-Key";

    public bool AllowApiKeyInQuery { get; set; } = false;
}
