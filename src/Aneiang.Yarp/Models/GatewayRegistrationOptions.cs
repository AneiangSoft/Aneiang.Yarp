using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Aneiang.Yarp.Models
{
    /// <summary>
    /// Gateway auto-registration configuration options.
    /// <para>
    /// Supports three configuration sources (priority high to low):
    /// <list type="number">
    /// <item>Code: <c>builder.Services.AddAneiangYarpClient(o => o.GatewayUrl = "...")</c></item>
    /// <item>Environment variables: <c>GatewayRegistration__GatewayUrl</c></item>
    /// <item>Config file: <c>appsettings.json -> GatewayRegistration</c></item>
    /// </list>
    /// </para>
    /// </summary>
    public class GatewayRegistrationOptions
    {
        /// <summary>
        /// JSON config section name.
        /// </summary>
        public const string SectionName = "GatewayRegistration";

        /// <summary>
        /// Whether auto-registration is enabled.
        /// <para>Smart default: auto-enabled when <see cref="GatewayUrl"/> is set; otherwise disabled.</para>
        /// </summary>
        public bool? Enabled { get; set; }

        /// <summary>
        /// Gateway service URL, e.g. http://192.168.1.100:5000
        /// <para><b>Required (the only mandatory field).</b></para>
        /// </summary>
        public string? GatewayUrl { get; set; }

        /// <summary>
        /// Route name (unique identifier on the gateway).
        /// <para>Default: entry assembly name (e.g. "MyService").</para>
        /// </summary>
        public string? RouteName { get; set; }

        /// <summary>
        /// Cluster name.
        /// <para>Default: same as <see cref="RouteName"/>.</para>
        /// </summary>
        public string? ClusterName { get; set; }

        /// <summary>
        /// Match path template, e.g. /api/my-service/{**catch-all}
        /// <para>Default: "/{**catch-all}" (forwards all paths).</para>
        /// </summary>
        public string? MatchPath { get; set; }

        /// <summary>
        /// Local destination address, e.g. http://localhost:5001
        /// <para>
        /// Default: auto-detected from the first Kestrel binding address.
        /// If the value contains localhost/127.0.0.1, it is <b>automatically resolved to the local LAN IP</b> during registration.
        /// </para>
        /// </summary>
        public string? DestinationAddress { get; set; }

        /// <summary>
        /// Route priority — lower values take precedence.
        /// <para>Default: 50.</para>
        /// </summary>
        public int? Order { get; set; }

        /// <summary>
        /// Automatically resolve localhost/127.0.0.1/0.0.0.0 to the local LAN IPv4 before registration.
        /// <para>Default: true.</para>
        /// </summary>
        public bool? AutoResolveIp { get; set; }

        /// <summary>
        /// HTTP timeout (seconds).<para>Default: 5.</para>
        /// </summary>
        public int? TimeoutSeconds { get; set; }

        /// <summary>
        /// Enable instance isolation mode (for multi-developer debugging of the same service).
        /// <para>
        /// When enabled, a machine-specific identifier is embedded into the route name and path,
        /// so multiple instances do not interfere with each other:
        /// </para>
        /// <list type="bullet">
        /// <item>routeName -> "{routeName}-{instanceId}"</item>
        /// <item>clusterName -> "{clusterName}-{instanceId}"</item>
        /// <item>matchPath -> "/{instanceId}{matchPath}" (path-level isolation)</item>
        /// </list>
        /// <para><b>Default: true</b></para>
        /// </summary>
        public bool? InstanceIsolation { get; set; }

        /// <summary>
        /// Custom instance identifier (only effective when <see cref="InstanceIsolation"/>=true).
        /// <para>Default: local machine name (Environment.MachineName).</para>
        /// </summary>
        public string? InstanceId { get; set; }

        /// <summary>
        /// Format template for the instance isolation path prefix.
        /// <para>Available placeholders: {instanceId}, {machineName}, {userName}</para>
        /// <para>Default: "{instanceId}"</para>
        /// </summary>
        public string? InstancePrefixFormat { get; set; }

        // ── Smart defaults ──

        internal bool IsEnabled => Enabled ?? !string.IsNullOrWhiteSpace(GatewayUrl);

        private bool IsInstanceIsolation() => InstanceIsolation != false;

        private string ResolveInstanceId()
        {
            if (!string.IsNullOrWhiteSpace(InstanceId))
                return InstanceId;
            return Environment.MachineName;
        }

        private string GetPrefix()
        {
            if (!IsInstanceIsolation())
                return string.Empty;

            var format = !string.IsNullOrWhiteSpace(InstancePrefixFormat)
                ? InstancePrefixFormat
                : "{instanceId}";

            var instanceId = ResolveInstanceId();
            return format
                .Replace("{instanceId}", instanceId)
                .Replace("{machineName}", Environment.MachineName)
                .Replace("{userName}", Environment.UserName);
        }

        internal string GetRouteName()
        {
            var name = RouteName;
            if (string.IsNullOrWhiteSpace(name))
                name = Assembly.GetEntryAssembly()?.GetName().Name ?? "my-service";

            var prefix = GetPrefix();
            return !string.IsNullOrEmpty(prefix) ? $"{name}-{prefix}" : name;
        }

        internal string GetClusterName()
        {
            var name = ClusterName;
            if (string.IsNullOrWhiteSpace(name))
                return GetRouteName();

            var prefix = GetPrefix();
            return !string.IsNullOrEmpty(prefix) ? $"{name}-{prefix}" : name;
        }

        internal string GetMatchPath()
        {
            var path = MatchPath;
            if (string.IsNullOrWhiteSpace(path))
                path = "/{**catch-all}";

            var prefix = GetPrefix();
            if (string.IsNullOrEmpty(prefix))
                return path;

            // Ensure path starts with /
            if (!path.StartsWith("/"))
                path = "/" + path;

            return $"/{prefix}{path}";
        }

        internal int GetOrder() => Order ?? 50;

        internal bool GetAutoResolveIp() => AutoResolveIp ?? true;

        internal int GetTimeoutSeconds() => TimeoutSeconds ?? 5;

        internal string? GetDestinationAddress(IServiceProvider sp)
        {
            if (!string.IsNullOrWhiteSpace(DestinationAddress))
                return DestinationAddress;

            // Auto-detect from Kestrel binding addresses
            var config = sp.GetRequiredService<IConfiguration>();
            var urls = config["urls"] ?? config["Urls"];

            if (!string.IsNullOrWhiteSpace(urls))
            {
                // Take the first address (strip semicolon-separated multiple addresses)
                var firstUrl = urls.Split(';')[0].Trim();
                if (firstUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    return firstUrl;
            }

            // Fallback: common ASP.NET Core default addresses
            var env = sp.GetRequiredService<IHostEnvironment>();
            var defaultPort = env.IsDevelopment() ? "5001" : "5000";
            return $"http://localhost:{defaultPort}";
        }
    }
}
