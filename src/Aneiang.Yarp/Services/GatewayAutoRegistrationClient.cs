using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Aneiang.Yarp.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Services;

/// <summary>
/// Gateway auto-registration client. Local services use this to register routes with a remote gateway,
/// so traffic goes through the gateway pipeline before forwarding to the local service.
///
/// Config priority (high to low): code > env vars > appsettings.json.
/// </summary>
public class GatewayAutoRegistrationClient
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GatewayRegistrationOptions _options;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<GatewayAutoRegistrationClient> _logger;

    /// <summary>Initializes a new instance of GatewayAutoRegistrationClient.</summary>
    /// <param name="httpClientFactory">HTTP client factory.</param>
    /// <param name="options">Gateway registration options.</param>
    /// <param name="serviceProvider">Service provider.</param>
    /// <param name="logger">Logger instance.</param>
    public GatewayAutoRegistrationClient(
        IHttpClientFactory httpClientFactory,
        IOptions<GatewayRegistrationOptions> options,
        IServiceProvider serviceProvider,
        ILogger<GatewayAutoRegistrationClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>Register this service's route with the gateway.</summary>
    public async Task<bool> RegisterAsync(CancellationToken ct = default)
    {
        if (!RegistrationOptionsResolver.IsEnabled(_options))
        {
            _logger.LogInformation("Auto-registration disabled (no GatewayUrl configured)");
            return false;
        }

        var gatewayUrl = _options.GatewayUrl;
        var routeName = RegistrationOptionsResolver.GetRouteName(_options);
        var clusterName = RegistrationOptionsResolver.GetClusterName(_options);
        var matchPath = RegistrationOptionsResolver.GetMatchPath(_options);
        var destinationAddress = RegistrationOptionsResolver.GetDestinationAddress(_options, _serviceProvider);
        var order = RegistrationOptionsResolver.GetOrder(_options);
        var transforms = RegistrationOptionsResolver.GetTransforms(_options);

        if (string.IsNullOrWhiteSpace(gatewayUrl) || string.IsNullOrWhiteSpace(destinationAddress))
        {
            _logger.LogWarning("Registration info incomplete, skipping");
            return false;
        }

        _logger.LogInformation("Registering: Route={RouteName}, Match={MatchPath}, Dest={DestAddress}, GW={GatewayUrl}",
            routeName, matchPath, destinationAddress, gatewayUrl);

        if (transforms != null && transforms.Count > 0)
            _logger.LogDebug("Transforms: {Transforms}", JsonSerializer.Serialize(transforms));

        // Auto-resolve localhost to LAN IP
        if (RegistrationOptionsResolver.GetAutoResolveIp(_options))
            destinationAddress = ResolveLocalAddress(destinationAddress);

        try
        {
            using var http = _httpClientFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(RegistrationOptionsResolver.GetTimeoutSeconds(_options));
            ApplyAuthHeaders(http);

            var json = JsonSerializer.Serialize(new
            {
                routeName,
                clusterName,
                matchPath,
                destinationAddress,
                order,
                transforms
            }, _jsonOptions);

            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var url = $"{gatewayUrl.TrimEnd('/')}/api/gateway/register-route";

            var response = await http.PostAsync(url, content, ct).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Registration OK: {Body}", body);
                return true;
            }

            _logger.LogWarning("Registration failed ({StatusCode}): {Body}", (int)response.StatusCode, body);
            return false;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Gateway unreachable ({Url}), skipping", gatewayUrl);
            return false;
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("Registration timed out ({Url}), skipping", gatewayUrl);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected registration error");
            return false;
        }
    }

    /// <summary>Unregister this service's route from the gateway.</summary>
    public async Task<bool> UnregisterAsync(CancellationToken ct = default)
    {
        var gatewayUrl = _options.GatewayUrl;
        var routeName = RegistrationOptionsResolver.GetRouteName(_options);

        if (string.IsNullOrWhiteSpace(gatewayUrl))
        {
            _logger.LogWarning("GatewayUrl not configured, skipping unregistration");
            return false;
        }

        try
        {
            using var http = _httpClientFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(RegistrationOptionsResolver.GetTimeoutSeconds(_options));
            ApplyAuthHeaders(http);

            var url = $"{gatewayUrl.TrimEnd('/')}/api/gateway/{routeName}";
            _logger.LogInformation("Unregistering: [{RouteName}]", routeName);

            var response = await http.DeleteAsync(url, ct).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                _logger.LogInformation("Unregistration OK: {Body}", body);
                return true;
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogInformation("Route [{RouteName}] no longer exists, ignoring", routeName);
                return true;
            }

            _logger.LogWarning("Unregistration failed ({StatusCode})", (int)response.StatusCode);
            return false;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Gateway unreachable, skipping unregistration");
            return false;
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("Unregistration timed out, skipping");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected unregistration error");
            return false;
        }
    }

    private void ApplyAuthHeaders(HttpClient http)
    {
        if (!string.IsNullOrWhiteSpace(_options.AuthToken))
        {
            http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.AuthToken);
            return;
        }

        if (!string.IsNullOrWhiteSpace(_options.BasicAuthUsername) && !string.IsNullOrWhiteSpace(_options.BasicAuthPassword))
        {
            var creds = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{_options.BasicAuthUsername}:{_options.BasicAuthPassword}"));
            http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", creds);
            return;
        }

        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
            http.DefaultRequestHeaders.TryAddWithoutValidation("X-Api-Key", _options.ApiKey);
    }

    /// <summary>Resolve localhost/127.0.0.1/0.0.0.0 in destination to LAN IPv4.</summary>
    private static string ResolveLocalAddress(string address)
    {
        if (string.IsNullOrWhiteSpace(address)) return address;
        try
        {
            var uri = new Uri(address);
            var host = uri.Host;
            if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                host.Equals("127.0.0.1") || host.Equals("0.0.0.0"))
            {
                var ip = GetLocalIpv4();
                if (ip != null) return $"{uri.Scheme}://{ip}:{uri.Port}{uri.PathAndQuery}";
            }
        }
        catch (UriFormatException) { }
        return address;
    }

    private static string? GetLocalIpv4()
    {
        try
        {
            return Dns.GetHostEntry(Dns.GetHostName()).AddressList
                .FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                ?.ToString();
        }
        catch (Exception)
        {
            // DNS resolution failed, return null to skip IP resolution
            return null;
        }
    }
}
