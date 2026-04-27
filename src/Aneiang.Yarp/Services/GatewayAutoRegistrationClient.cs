using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Aneiang.Yarp.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Services
{
    /// <summary>
    /// Gateway auto-registration client.
    /// Used by local services (e.g. a monolith running locally for debugging) to register routes
    /// with a remote gateway, so traffic flows through the gateway's middleware pipeline before
    /// being forwarded to the local service.
    /// <para>
    /// Configuration priority (high to low):
    /// <list type="number">
    /// <item>Code: <c>AddAneiangYarpClient(o => o.GatewayUrl = "...")</c></item>
    /// <item>Environment variables: <c>GatewayRegistration__GatewayUrl</c></item>
    /// <item>Config file: <c>appsettings.json</c></item>
    /// </list>
    /// </para>
    /// </summary>
    public class GatewayAutoRegistrationClient
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly GatewayRegistrationOptions _options;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<GatewayAutoRegistrationClient> _logger;

        /// <summary>
        /// Creates a new GatewayAutoRegistrationClient.
        /// </summary>
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

        /// <summary>
        /// Register the current service route with the gateway.
        /// </summary>
        public async Task<bool> RegisterAsync(CancellationToken cancellationToken = default)
        {
            if (!_options.IsEnabled)
            {
                _logger.LogInformation("Gateway auto-registration disabled (GatewayUrl not configured)");
                return false;
            }

            var gatewayUrl = _options.GatewayUrl;
            var routeName = _options.GetRouteName();
            var clusterName = _options.GetClusterName();
            var matchPath = _options.GetMatchPath();
            var destinationAddress = _options.GetDestinationAddress(_serviceProvider);
            var order = _options.GetOrder();

            if (string.IsNullOrWhiteSpace(gatewayUrl) ||
                string.IsNullOrWhiteSpace(destinationAddress))
            {
                _logger.LogWarning("Registration info incomplete (missing GatewayUrl/DestinationAddress), skipping");
                return false;
            }

            _logger.LogInformation("Registration info: Route={RouteName}, Match={MatchPath}, Dest={DestAddress}, Gateway={GatewayUrl}",
                routeName, matchPath, destinationAddress, gatewayUrl);

            // Automatically resolve localhost to local LAN IP
            if (_options.GetAutoResolveIp())
                destinationAddress = ResolveLocalAddress(destinationAddress);

            try
            {
                using var httpClient = _httpClientFactory.CreateClient();
                httpClient.Timeout = TimeSpan.FromSeconds(_options.GetTimeoutSeconds());

                var requestBody = new
                {
                    routeName,
                    clusterName,
                    matchPath,
                    destinationAddress,
                    order
                };

                var json = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var registerUrl = $"{gatewayUrl.TrimEnd('/')}/api/gateway/register-route";

                _logger.LogInformation("Registering route [{RouteName}] {MatchPath} \u2192 {Address}",
                    routeName, matchPath, destinationAddress);

                var response = await httpClient.PostAsync(registerUrl, content, cancellationToken)
                    .ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Registration successful {Body}", body);
                    return true;
                }

                _logger.LogWarning("Registration failed ({StatusCode}): {Body}", (int)response.StatusCode, body);
                return false;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Gateway unreachable ({GatewayUrl}), skipping registration", gatewayUrl);
                return false;
            }
            catch (TaskCanceledException)
            {
                _logger.LogWarning("Gateway request timed out ({GatewayUrl}), skipping registration", gatewayUrl);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during registration");
                return false;
            }
        }

        /// <summary>
        /// Unregister the current route from the gateway.
        /// </summary>
        public async Task<bool> UnregisterAsync(CancellationToken cancellationToken = default)
        {
            var gatewayUrl = _options.GatewayUrl;
            var routeName = _options.GetRouteName();

            if (string.IsNullOrWhiteSpace(gatewayUrl))
            {
                _logger.LogWarning("GatewayUrl not configured, skipping unregistration");
                return false;
            }

            try
            {
                using var httpClient = _httpClientFactory.CreateClient();
                httpClient.Timeout = TimeSpan.FromSeconds(_options.GetTimeoutSeconds());

                var deleteUrl = $"{gatewayUrl.TrimEnd('/')}/api/gateway/{routeName}";

                _logger.LogInformation("Unregistering route [{RouteName}]", routeName);
                var response = await httpClient.DeleteAsync(deleteUrl, cancellationToken)
                    .ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken)
                        .ConfigureAwait(false);
                    _logger.LogInformation("Unregistration successful {Body}", body);
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
                _logger.LogWarning("Unregistration request timed out, skipping");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during unregistration");
                return false;
            }
        }

        /// <summary>
        /// Resolve localhost / 127.0.0.1 / 0.0.0.0 in the destination address
        /// to the local LAN IPv4 address (so the gateway can reach this machine).
        /// </summary>
        private static string ResolveLocalAddress(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
                return address;

            try
            {
                var uri = new Uri(address);
                var host = uri.Host;

                if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                    host.Equals("127.0.0.1") ||
                    host.Equals("0.0.0.0"))
                {
                    var localIp = GetLocalIpv4();
                    if (localIp != null)
                    {
                        return $"{uri.Scheme}://{localIp}:{uri.Port}{uri.PathAndQuery}";
                    }
                }
            }
            catch (UriFormatException)
            {
                // If address is not a valid URI, return as-is
            }

            return address;
        }

        private static string? GetLocalIpv4()
        {
            try
            {
                var hostEntry = Dns.GetHostEntry(Dns.GetHostName());
                return hostEntry.AddressList
                    .FirstOrDefault(ip =>
                        ip.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(ip))
                    ?.ToString();
            }
            catch
            {
                return null;
            }
        }
    }
}
