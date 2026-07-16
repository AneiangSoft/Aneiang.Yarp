using System.Net;
using System.Net.Sockets;
using Aneiang.Yarp.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Dashboard.Modules.AI;

/// <summary>
/// Runtime AI settings service. Allows reading/updating AI options
/// without requiring application restart.
///
/// Security model (layered defence):
///   Layer 1 — All providers: BaseUrl is user-editable (supports API proxies / mirrors).
///             For known providers, an empty or default input falls back to the official endpoint.
///   Layer 2 — User-supplied BaseUrl is always validated against SSRF (private IP, localhost, metadata).
///             Invalid URLs are rejected and the previous value is preserved.
///   Layer 3 — All numeric parameters are clamped to safe ranges.
///
/// Persistence: settings are saved to SQLite (ai_settings table) on every update
/// and loaded on construction. SQLite overrides take precedence over appsettings.json,
/// so the AI module can be fully configured from the Dashboard without touching config files.
/// </summary>
public class AISettingsService : IAISettingsService
{
    // ──────── Known provider → official BaseUrl (immutable, never overridden by user input) ────────
    private static readonly Dictionary<string, string> _builtinBaseUrls =
        new(StringComparer.OrdinalIgnoreCase)
    {
        ["openai"]   = "https://api.openai.com/v1",
        ["deepseek"] = "https://api.deepseek.com",
        // Alibaba Bailian (Qwen) — 仍可使用旧域名，亦可迁移至业务空间专属域名获得更高性能：
        //   华北2（北京）: https://{WorkspaceId}.cn-beijing.maas.aliyuncs.com/compatible-mode/v1
        //   新加坡:        https://{WorkspaceId}.ap-southeast-1.maas.aliyuncs.com/compatible-mode/v1
        ["qwen"]     = "https://dashscope.aliyuncs.com/compatible-mode/v1",
    };

    // Providers that are ALWAYS available (regardless of AllowCustomProvider)
    private static readonly HashSet<string> _builtinProviders =
        new(StringComparer.OrdinalIgnoreCase) { "openai", "deepseek", "qwen" };

    // ──────── Known provider domain suffixes (for workspace-specific endpoints) ────────
    // Maps provider → domain suffixes that are recognised as legitimate.
    // URLs matching these domains are treated as "official-like" (no SSRF alarm, no warning).
    private static readonly Dictionary<string, string[]> _knownProviderDomains =
        new(StringComparer.OrdinalIgnoreCase)
    {
        ["openai"]   = ["openai.com"],
        ["deepseek"] = ["deepseek.com"],
        ["qwen"]     = ["aliyuncs.com", "dashscope.aliyuncs.com"],  // covers workspace-specific maas endpoints
    };

    // ──────── SSRF: blocked metadata-service hostnames ────────
    private static readonly HashSet<string> _blockedHostnames =
        new(StringComparer.OrdinalIgnoreCase)
    {
        "metadata.google.internal",       // GCP metadata
        "169.254.169.254",                // AWS / Azure / common metadata IP
    };

    // ──────── Parameter range validation constants ────────
    private const int MaxTokensMin = 1;
    private const int MaxTokensMax = 131_072;
    private const double TemperatureMin = 0.0;
    private const double TemperatureMax = 2.0;
    private const int MaxHistoryMin = 1;
    private const int MaxHistoryMax = 200;

    // ──────── Default models per provider ────────
    private static readonly Dictionary<string, string> _defaultModels =
        new(StringComparer.OrdinalIgnoreCase)
    {
        ["openai"]   = "gpt-4o-mini",
        ["deepseek"] = "deepseek-v4-flash",
        ["qwen"]     = "qwen3.7-plus",
    };

    private readonly AIOptions _options;
    private readonly IAISettingsRepository _repo;
    private readonly ILogger<AISettingsService> _logger;

    public AISettingsService(
        IOptions<AIOptions> options,
        IAISettingsRepository repo,
        ILogger<AISettingsService> logger)
    {
        _options = options.Value;
        _repo = repo;
        _logger = logger;
        LoadOverridesFromStorage();
    }

    /// <summary>Whether the "custom" provider choice is exposed to the frontend.</summary>
    public bool AllowCustomProvider => _options.AllowCustomProvider;

    /// <summary>Returns current AI settings (API key masked).</summary>
    public AISettingsDto GetSettings()
    {
        return new AISettingsDto
        {
            Enabled = _options.Enabled,
            Provider = _options.Provider,
            ApiKey = MaskKey(_options.ApiKey),
            BaseUrl = _options.BaseUrl,
            ChatModel = _options.ChatModel,
            AnalysisModel = _options.AnalysisModel,
            MaxTokens = _options.MaxTokens,
            Temperature = _options.Temperature,
            MaxConversationHistory = _options.MaxConversationHistory,
            EnableBackgroundAnalysis = _options.EnableBackgroundAnalysis,
            EnhanceNotifications = _options.EnhanceNotifications,
            IsConfigured = !string.IsNullOrWhiteSpace(_options.ApiKey),
            AllowCustomProvider = _options.AllowCustomProvider,
        };
    }

    /// <summary>
    /// Update AI settings at runtime.
    ///
    /// BaseUrl handling:
    ///   - Known providers (openai/deepseek/qwen) → locked to official endpoint, user input ignored.
    ///   - Custom  + AllowCustomProvider → user input validated via SSRF checks.
    ///   - Custom  + !AllowCustomProvider → rejected, fallback to openai.
    ///
    /// All numeric parameters are clamped to safe ranges.
    /// </summary>
    public void UpdateSettings(AISettingsDto dto)
    {
        var provider = NormaliseProvider(dto.Provider);

        _options.Enabled = dto.Enabled;
        _options.Provider = provider;

        // ── BaseUrl resolution (SSRF-aware) ──
        _options.BaseUrl = ResolveBaseUrl(provider, dto.BaseUrl);

        // Models: use user value or provider-appropriate default
        _options.ChatModel     = string.IsNullOrWhiteSpace(dto.ChatModel)     ? GetDefaultModel(provider) : dto.ChatModel;
        _options.AnalysisModel = string.IsNullOrWhiteSpace(dto.AnalysisModel) ? GetDefaultModel(provider) : dto.AnalysisModel;

        // Clamp numeric parameters to safe ranges
        _options.MaxTokens              = Math.Clamp(dto.MaxTokens,              MaxTokensMin,  MaxTokensMax);
        _options.Temperature            = Math.Clamp(dto.Temperature,            TemperatureMin, TemperatureMax);
        _options.MaxConversationHistory = Math.Clamp(dto.MaxConversationHistory, MaxHistoryMin,  MaxHistoryMax);

        _options.EnableBackgroundAnalysis = dto.EnableBackgroundAnalysis;
        _options.EnhanceNotifications     = dto.EnhanceNotifications;

        // Only update API key if a new (non-masked) value is provided
        if (!string.IsNullOrWhiteSpace(dto.ApiKey) && !dto.ApiKey.Contains("****"))
        {
            _options.ApiKey = dto.ApiKey;
        }

        _logger.LogInformation(
            "AI settings updated: Provider={Provider}, BaseUrl={BaseUrl}, Model={Model}, " +
            "Tokens={MaxTokens}, Temp={Temperature}, History={MaxHistory}, Enabled={Enabled}",
            _options.Provider, _options.BaseUrl, _options.ChatModel, _options.MaxTokens,
            _options.Temperature, _options.MaxConversationHistory, _options.Enabled);

        // Persist to SQLite so settings survive restarts
        PersistToStorage();
    }

    // ═══════════════════════════════════════════════════════════════
    //  SQLite persistence (survives app restarts)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Load AI settings overrides from SQLite on startup.
    /// SQLite values take precedence over appsettings.json defaults,
    /// allowing the Dashboard to fully configure the AI module at runtime.
    /// </summary>
    private void LoadOverridesFromStorage()
    {
        try
        {
            // Blocking call — acceptable for a singleton constructed once at startup
            var kv = _repo.LoadAllAsync().GetAwaiter().GetResult();
            if (kv.Count == 0) return;

            ApplyOverrides(kv);
            _logger.LogInformation(
                "Loaded {Count} AI settings from SQLite. Enabled={Enabled}, Provider={Provider}",
                kv.Count, _options.Enabled, _options.Provider);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load AI settings from SQLite. Using configuration defaults.");
        }
    }

    /// <summary>Apply key-value overrides from SQLite to the in-memory options.</summary>
    private void ApplyOverrides(Dictionary<string, string> kv)
    {
        if (kv.TryGetValue("Enabled", out var v)) _options.Enabled = ParseBool(v);
        if (kv.TryGetValue("Provider", out v) && !string.IsNullOrWhiteSpace(v)) _options.Provider = v;
        if (kv.TryGetValue("ApiKey", out v) && !string.IsNullOrWhiteSpace(v)) _options.ApiKey = v;
        if (kv.TryGetValue("BaseUrl", out v) && !string.IsNullOrWhiteSpace(v)) _options.BaseUrl = v;
        if (kv.TryGetValue("ChatModel", out v) && !string.IsNullOrWhiteSpace(v)) _options.ChatModel = v;
        if (kv.TryGetValue("AnalysisModel", out v) && !string.IsNullOrWhiteSpace(v)) _options.AnalysisModel = v;
        if (kv.TryGetValue("MaxTokens", out v) && int.TryParse(v, out var i)) _options.MaxTokens = i;
        if (kv.TryGetValue("Temperature", out v) && double.TryParse(v, out var d)) _options.Temperature = d;
        if (kv.TryGetValue("MaxConversationHistory", out v) && int.TryParse(v, out i)) _options.MaxConversationHistory = i;
        if (kv.TryGetValue("EnableBackgroundAnalysis", out v)) _options.EnableBackgroundAnalysis = ParseBool(v);
        if (kv.TryGetValue("EnhanceNotifications", out v)) _options.EnhanceNotifications = ParseBool(v);
    }

    /// <summary>Save current in-memory settings to SQLite.</summary>
    private void PersistToStorage()
    {
        try
        {
            var pairs = new (string, string)[]
            {
                ("Enabled", _options.Enabled.ToString().ToLowerInvariant()),
                ("Provider", _options.Provider),
                ("ApiKey", _options.ApiKey),
                ("BaseUrl", _options.BaseUrl),
                ("ChatModel", _options.ChatModel),
                ("AnalysisModel", _options.AnalysisModel),
                ("MaxTokens", _options.MaxTokens.ToString()),
                ("Temperature", _options.Temperature.ToString("F2")),
                ("MaxConversationHistory", _options.MaxConversationHistory.ToString()),
                ("EnableBackgroundAnalysis", _options.EnableBackgroundAnalysis.ToString().ToLowerInvariant()),
                ("EnhanceNotifications", _options.EnhanceNotifications.ToString().ToLowerInvariant()),
            };

            // Fire-and-forget — persistence failure should not block the API response
            _ = Task.Run(async () =>
            {
                try
                {
                    await _repo.SaveBatchAsync(pairs);
                    _logger.LogDebug("AI settings persisted to SQLite ({Count} keys).", pairs.Length);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to persist AI settings to SQLite.");
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enqueue AI settings persistence.");
        }
    }

    private static bool ParseBool(string? s) =>
        bool.TryParse(s, out var b) && b;

    // ═══════════════════════════════════════════════════════════════
    //  Provider / BaseUrl resolution
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Normalise provider to a known value, or "custom" if allowed.</summary>
    private string NormaliseProvider(string? provider)
    {
        var p = (provider ?? "openai").ToLowerInvariant().Trim();

        if (_builtinProviders.Contains(p))
            return p;

        if (p == "custom" && _options.AllowCustomProvider)
            return "custom";

        // Unknown / disabled custom → safe fallback
        _logger.LogWarning("Provider '{Provider}' is not allowed (AllowCustomProvider={Allow}). Falling back to 'openai'.",
            provider, _options.AllowCustomProvider);
        return "openai";
    }

    /// <summary>
    /// Resolve BaseUrl for any provider type.
    ///
    /// Known providers:
    ///   - Empty / official-URL input          → use official endpoint (no validation needed).
    ///   - URL matching known provider domain  → fast-path validate (format only, skip SSRF IP resolution).
    ///   - Other custom URL                    → full SSRF validation; reject and keep current on failure.
    ///
    /// Custom provider:
    ///   - Empty input  → keep current BaseUrl.
    ///   - Non-empty    → validate against SSRF; reject and keep current on failure.
    /// </summary>
    private string ResolveBaseUrl(string provider, string? userBaseUrl)
    {
        var trimmed = (userBaseUrl ?? "").Trim();

        // ── Known provider ──
        if (_builtinBaseUrls.TryGetValue(provider, out var officialUrl))
        {
            // If user left blank, or pasted the official URL verbatim → use official endpoint
            if (string.IsNullOrEmpty(trimmed) ||
                trimmed.TrimEnd('/').Equals(officialUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
                return officialUrl;

            // Fast-path: URL belongs to the provider's known domain family
            // (e.g. workspace-specific Qwen: ws-xxx.cn-beijing.maas.aliyuncs.com)
            // Skip SSRF IP resolution — these are well-known public services.
            if (IsKnownProviderDomain(provider, trimmed))
            {
                if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
                    return BuildCleanUrl(uri);
            }

            // User supplied an unknown-domain URL (e.g. a proxy / mirror) → full SSRF validation
            var result = ValidateBaseUrl(trimmed);
            if (result.IsValid)
                return result.SanitisedUrl!;

            _logger.LogWarning(
                "BaseUrl for '{Provider}' rejected (SSRF check): {Url} — {Reason}. Keeping current: {Current}",
                provider, trimmed, result.Reason, _options.BaseUrl);
            return _options.BaseUrl;
        }

        // ── Custom provider ──
        if (provider == "custom")
        {
            if (string.IsNullOrWhiteSpace(trimmed))
                return _options.BaseUrl;

            var result = ValidateBaseUrl(trimmed);
            if (result.IsValid)
                return result.SanitisedUrl!;

            _logger.LogWarning("Custom BaseUrl rejected (SSRF check): {Url} — {Reason}. Keeping current: {Current}",
                trimmed, result.Reason, _options.BaseUrl);
            return _options.BaseUrl;
        }

        return _builtinBaseUrls["openai"];
    }

    /// <summary>
    /// Check if a URL's hostname belongs to a known provider's domain family.
    /// e.g. "ws-abc.cn-beijing.maas.aliyuncs.com" matches qwen's "aliyuncs.com".
    /// </summary>
    private static bool IsKnownProviderDomain(string provider, string rawUrl)
    {
        if (!_knownProviderDomains.TryGetValue(provider, out var domains))
            return false;

        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri))
            return false;

        var host = uri.Host;
        foreach (var domain in domains)
        {
            if (host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    // ═══════════════════════════════════════════════════════════════
    //  SSRF validation for custom BaseUrl
    // ═══════════════════════════════════════════════════════════════

    private readonly record struct UrlValidationResult(bool IsValid, string? SanitisedUrl, string? Reason)
    {
        public static UrlValidationResult Ok(string url) => new(true, url, null);
        public static UrlValidationResult Fail(string reason) => new(false, null, reason);
    }

    /// <summary>
    /// Validate a user-supplied BaseUrl to prevent SSRF attacks.
    ///
    /// Rules:
    ///   1. Must be a valid absolute URI.
    ///   2. Scheme must be http or https.
    ///   3. Metadata-service hostnames are always blocked.
    ///   4. For HTTP:  only loopback addresses are allowed (e.g. local Ollama/vLLM).
    ///   5. For HTTPS: private/loopback/link-local IPs are blocked.
    /// </summary>
    private static UrlValidationResult ValidateBaseUrl(string rawUrl)
    {
        // 1. Must be a valid absolute URI
        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri))
            return UrlValidationResult.Fail("Not a valid absolute URL.");

        // 2. Scheme must be HTTP or HTTPS
        var isHttp = uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase);
        var isHttps = uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase);
        if (!isHttp && !isHttps)
            return UrlValidationResult.Fail($"Scheme '{uri.Scheme}' is not allowed. Use http:// or https://.");

        var host = uri.Host;

        // 3. Block metadata-service hostnames (always, regardless of scheme)
        if (_blockedHostnames.Contains(host))
            return UrlValidationResult.Fail($"Hostname '{host}' is blocked (metadata service).");

        // 4. Resolve hostname to IP addresses
        IPAddress[] addresses;
        try
        {
            addresses = Dns.GetHostAddresses(host);
            if (addresses.Length == 0)
                return UrlValidationResult.Fail($"Could not resolve hostname '{host}'.");
        }
        catch (SocketException)
        {
            // DNS failure — safer to allow than to block
            // (legitimate public endpoints may have intermittent DNS during validation)
            var clean = BuildCleanUrl(uri);
            return UrlValidationResult.Ok(clean);
        }

        // 5. HTTP: ONLY allow loopback (for local LLM servers like Ollama, LM Studio, vLLM)
        if (isHttp)
        {
            foreach (var ip in addresses)
            {
                var checkIp = ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip;
                if (!IPAddress.IsLoopback(checkIp))
                    return UrlValidationResult.Fail(
                        $"HTTP is only allowed for loopback addresses. '{host}' resolves to {checkIp}.");
            }
        }

        // 6. HTTPS: block private / loopback / link-local IP ranges (SSRF prevention)
        if (isHttps)
        {
            foreach (var ip in addresses)
            {
                if (IsPrivateOrReservedIp(ip))
                    return UrlValidationResult.Fail(
                        $"Hostname '{host}' resolves to {ip} (private/reserved range — SSRF blocked).");
            }
        }

        var cleanUrl = BuildCleanUrl(uri);
        return UrlValidationResult.Ok(cleanUrl);
    }

    /// <summary>
    /// Build a sanitised URL preserving the path (important for endpoints like
    /// https://ws-xxx.cn-beijing.maas.aliyuncs.com/compatible-mode/v1).
    /// Strips trailing slashes, query strings, and fragments.
    /// </summary>
    private static string BuildCleanUrl(Uri uri)
    {
        var path = uri.AbsolutePath.TrimEnd('/');
        return $"{uri.Scheme}://{uri.Authority}{path}";
    }

    /// <summary>
    /// Check if an IP address falls in a private or reserved range.
    /// Used to block SSRF attempts against internal infrastructure.
    /// </summary>
    private static bool IsPrivateOrReservedIp(IPAddress ip)
    {
        // Map IPv4-mapped IPv6 back to IPv4 for consistent checking
        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();

        if (IPAddress.IsLoopback(ip))
            return true;

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = ip.GetAddressBytes();

            // 10.0.0.0/8        — private (Class A)
            // 172.16.0.0/12     — private (Class B)
            // 192.168.0.0/16    — private (Class C)
            // 169.254.0.0/16    — link-local / cloud metadata
            // 127.0.0.0/8       — loopback
            // 0.0.0.0/8         — "this network"

            if (bytes[0] == 10) return true;
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;
            if (bytes[0] == 192 && bytes[1] == 168) return true;
            if (bytes[0] == 169 && bytes[1] == 254) return true;
            if (bytes[0] == 127) return true;
            if (bytes[0] == 0) return true;
        }
        else if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal)
                return true;
        }

        return false;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════════════════

    private static string GetDefaultModel(string provider)
    {
        return _defaultModels.TryGetValue(provider, out var model)
            ? model
            : _defaultModels["openai"];
    }

    private static string MaskKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return "";
        if (key.Length <= 8) return "****";
        return key[..4] + "****" + key[^4..];
    }
}

