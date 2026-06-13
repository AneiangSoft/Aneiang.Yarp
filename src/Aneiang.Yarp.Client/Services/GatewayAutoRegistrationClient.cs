using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using GatewayGrpc = Aneiang.Yarp.GatewayRegistry.GatewayRegistry;
using Aneiang.Yarp.GatewayRegistry;
using Aneiang.Yarp.Models;
using Grpc.Core;
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
    private readonly KestrelAutoConfigService _kestrelConfig;
    private readonly GatewayGrpc.GatewayRegistryClient _grpcClient;

    /// <summary>Initializes a new instance of GatewayAutoRegistrationClient.</summary>
    /// <param name="httpClientFactory">HTTP client factory.</param>
    /// <param name="options">Gateway registration options.</param>
    /// <param name="serviceProvider">Service provider.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="kestrelConfig">Kestrel auto-configuration service.</param>
    /// <param name="grpcClient">gRPC gateway registry client.</param>
    public GatewayAutoRegistrationClient(
        IHttpClientFactory httpClientFactory,
        IOptions<GatewayRegistrationOptions> options,
        IServiceProvider serviceProvider,
        ILogger<GatewayAutoRegistrationClient> logger,
        KestrelAutoConfigService kestrelConfig,
        GatewayGrpc.GatewayRegistryClient grpcClient)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _kestrelConfig = kestrelConfig;
        _grpcClient = grpcClient;
    }

    /// <summary>Register this service's route with the gateway.</summary>
    public async Task<bool> RegisterAsync(CancellationToken ct = default)
    {
        if (!RegistrationOptionsResolver.IsEnabled(_options))
        {
            _logger.LogDebug("Auto-registration disabled (no GatewayUrl configured)");
            return false;
        }

        var gatewayUrl = _options.GatewayUrl;
        var routeName = RegistrationOptionsResolver.GetRouteName(_options);
        var clusterName = RegistrationOptionsResolver.GetClusterName(_options);
        var matchPath = RegistrationOptionsResolver.GetMatchPath(_options);
        var destinationAddress = RegistrationOptionsResolver.GetDestinationAddress(_options, _serviceProvider);
        var order = RegistrationOptionsResolver.GetOrder(_options);
        var transforms = RegistrationOptionsResolver.GetTransforms(_options);
        var useIpIsolation = RegistrationOptionsResolver.UseIpIsolation(_options);
        var useGrpcRegistration = _options.UseGrpcRegistration == true;

        if (string.IsNullOrWhiteSpace(gatewayUrl) || string.IsNullOrWhiteSpace(destinationAddress))
        {
            _logger.LogWarning("Registration info incomplete, skipping");
            return false;
        }

        _logger.LogDebug("Registering: Route={RouteName}, Match={MatchPath}, Dest={DestAddress}, GW={GatewayUrl}",
            routeName, matchPath, destinationAddress, gatewayUrl);

        if (transforms != null && transforms.Count > 0)
            _logger.LogDebug("Transforms: {Transforms}", JsonSerializer.Serialize(transforms));

        if (useIpIsolation)
        {
            _logger.LogDebug("IP-based isolation enabled. Gateway will route based on client IP address.");
        }

        if (RegistrationOptionsResolver.GetAutoResolveIp(_options))
        {
            var (resolvedAddress, warning) = ResolveLocalAddressWithCheck(destinationAddress);
            destinationAddress = resolvedAddress;

            if (!string.IsNullOrWhiteSpace(warning))
            {
                _logger.LogWarning(warning);
            }

            try
            {
                var uri = new Uri(destinationAddress);
                _kestrelConfig.EnsureListeningOnAny(uri.Port);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to auto-configure Kestrel");
            }
        }

        try
        {
            if (useGrpcRegistration)
            {
                return await RegisterWithGrpcAsync(routeName, clusterName, matchPath, destinationAddress, ct).ConfigureAwait(false);
            }

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
                transforms,
                useIpIsolation,
                clientIp = useIpIsolation ? GetLocalIpv4() : null
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
        var useGrpcRegistration = _options.UseGrpcRegistration == true;

        if (string.IsNullOrWhiteSpace(gatewayUrl))
        {
            _logger.LogWarning("GatewayUrl not configured, skipping unregistration");
            return false;
        }

        try
        {
            if (useGrpcRegistration)
            {
                try
                {
                    var callOptions = BuildGrpcCallOptions(ct);
                    var grpcResponse = await _grpcClient.UnregisterServiceAsync(new UnregisterServiceRequest
                    {
                        ServiceId = routeName
                    }, callOptions).ConfigureAwait(false);

                    if (grpcResponse.Success)
                    {
                        _logger.LogInformation("gRPC unregistration OK: {Message}", grpcResponse.Message);
                        return true;
                    }

                    _logger.LogWarning("gRPC unregistration failed: {Message}", grpcResponse.Message);
                    return false;
                }
                catch (RpcException ex)
                {
                    _logger.LogWarning(ex, "gRPC unregistration RPC error: {Status}", ex.Status);
                    return false;
                }
            }

            using var http = _httpClientFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(RegistrationOptionsResolver.GetTimeoutSeconds(_options));
            ApplyAuthHeaders(http);

            var clientIp = RegistrationOptionsResolver.UseIpIsolation(_options) ? GetLocalIpv4() : null;
            var url = $"{gatewayUrl.TrimEnd('/')}/api/gateway/{routeName}";
            if (!string.IsNullOrWhiteSpace(clientIp))
                url += $"?clientIp={Uri.EscapeDataString(clientIp)}";

            _logger.LogInformation("Unregistering: [{RouteName}] (ClientIp: {ClientIp})", routeName, clientIp ?? "N/A");

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

    private async Task<bool> RegisterWithGrpcAsync(
        string routeName,
        string clusterName,
        string matchPath,
        string destinationAddress,
        CancellationToken ct)
    {
        try
        {
            var callOptions = BuildGrpcCallOptions(ct);
            var response = await _grpcClient.RegisterServiceAsync(new RegisterServiceRequest
            {
                ServiceId = routeName,
                ServiceName = clusterName,
                Paths = { matchPath },
                Destinations =
                {
                    new Destination
                    {
                        DestinationId = "d1",
                        Address = destinationAddress,
                        Enabled = true
                    }
                }
            }, callOptions).ConfigureAwait(false);

            if (response.Success)
            {
                _logger.LogInformation("gRPC registration OK: {Message}", response.Message);
                return true;
            }

            _logger.LogWarning("gRPC registration failed: {Message}", response.Message);
            return false;
        }
        catch (RpcException ex)
        {
            _logger.LogWarning(ex, "gRPC registration RPC error: {Status}", ex.Status);
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

    /// <summary>
    /// Build gRPC call options with auth metadata from <see cref="GatewayRegistrationOptions"/>.
    /// Supports Bearer token, Basic auth, and API Key.
    /// </summary>
    private CallOptions BuildGrpcCallOptions(CancellationToken ct = default)
    {
        var metadata = new Metadata();

        if (!string.IsNullOrWhiteSpace(_options.AuthToken))
        {
            metadata.Add("Authorization", $"Bearer {_options.AuthToken}");
        }
        else if (!string.IsNullOrWhiteSpace(_options.BasicAuthUsername) && !string.IsNullOrWhiteSpace(_options.BasicAuthPassword))
        {
            var creds = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{_options.BasicAuthUsername}:{_options.BasicAuthPassword}"));
            metadata.Add("Authorization", $"Basic {creds}");
        }
        else if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            metadata.Add("x-api-key", _options.ApiKey);
        }

        return new CallOptions(headers: metadata, cancellationToken: ct);
    }

    /// <summary>Resolve localhost/127.0.0.1/0.0.0.0 in destination to LAN IPv4 with listening check.</summary>
    /// <returns>Tuple of (resolved address, warning message if any).</returns>
    private (string address, string? warning) ResolveLocalAddressWithCheck(string address)
    {
        if (string.IsNullOrWhiteSpace(address)) return (address, null);

        try
        {
            var uri = new Uri(address);
            var host = uri.Host;

            if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                host.Equals("127.0.0.1") || host.Equals("0.0.0.0"))
            {
                bool isListeningOnAny = IsListeningOnAnyAddress(uri.Port);
                var ip = GetLocalIpv4();
                if (ip != null)
                {
                    var resolved = $"{uri.Scheme}://{ip}:{uri.Port}{uri.PathAndQuery}";

                    if (!isListeningOnAny)
                    {
                        var warning = $"Service is listening on localhost only (port {uri.Port}), but registered with LAN IP {ip}. " +
                            $"Cross-machine access will fail until you configure Kestrel to listen on 0.0.0.0:\n" +
                            $"  - launchSettings.json: \"applicationUrl\": \"http://0.0.0.0:{uri.Port}\"\n" +
                            $"  - Or Program.cs: .UseUrls(\"http://0.0.0.0:{uri.Port}\")";

                        return (resolved, warning);
                    }

                    return (resolved, null);
                }
            }
        }
        catch (UriFormatException ex)
        {
            _logger.LogWarning(ex, "Invalid URI format in address: {Address}", address);
        }

        return (address, null);
    }

    /// <summary>Check if the service is listening on 0.0.0.0 (all interfaces) for the given port.</summary>
    private static bool IsListeningOnAnyAddress(int port)
    {
        try
        {
            var listeners = System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties()
                .GetActiveTcpListeners()
                .Where(l => l.Port == port);

            foreach (var listener in listeners)
            {
                if (listener.Address.Equals(IPAddress.Any) ||
                    listener.Address.Equals(IPAddress.IPv6Any))
                {
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return true;
        }
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
            return null;
        }
    }

    /// <summary>
    /// Send a heartbeat to the gateway to keep the registration alive.
    /// Gateway uses heartbeat to detect stale registrations.
    /// </summary>
    public async Task<bool> HeartbeatAsync(CancellationToken ct = default)
    {
        var gatewayUrl = _options.GatewayUrl;
        var routeName = RegistrationOptionsResolver.GetRouteName(_options);
        var useGrpcRegistration = _options.UseGrpcRegistration == true;

        if (string.IsNullOrWhiteSpace(gatewayUrl))
            return false;

        try
        {
            if (useGrpcRegistration)
            {
                try
                {
                    var callOptions = BuildGrpcCallOptions(ct);
                    var grpcResponse = await _grpcClient.HeartbeatAsync(new HeartbeatRequest
                    {
                        ServiceId = routeName
                    }, callOptions).ConfigureAwait(false);

                    return grpcResponse.Success;
                }
                catch (RpcException)
                {
                    return false;
                }
            }

            using var http = _httpClientFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(10);
            ApplyAuthHeaders(http);

            var clientIp = RegistrationOptionsResolver.UseIpIsolation(_options) ? GetLocalIpv4() : null;
            var url = $"{gatewayUrl.TrimEnd('/')}/api/gateway/{routeName}/heartbeat";
            if (!string.IsNullOrWhiteSpace(clientIp))
                url += $"?clientIp={Uri.EscapeDataString(clientIp)}";

            var response = await http.PostAsync(url, null, ct).ConfigureAwait(false);

            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
