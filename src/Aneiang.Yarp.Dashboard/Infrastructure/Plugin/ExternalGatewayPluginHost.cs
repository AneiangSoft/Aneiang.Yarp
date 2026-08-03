using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Plugin;

/// <summary>Registration state of an external plugin discovered from plugin.json.</summary>
public enum ExternalPluginRegistrationStatus
{
    Discovered,
    Disabled,
    Loaded,
    InvalidManifest,
    DependencyUnsatisfied,
    LoadFailed,
    UnloadPending,
    Unloaded
}

/// <summary>External plugin discovery and load result. Discovery never loads the entry assembly.</summary>
public sealed record ExternalPluginRegistration(
    PluginManifest Manifest,
    string ManifestPath,
    ExternalPluginRegistrationStatus Status,
    string? Error = null,
    bool IsCollectible = false,
    bool? IsLoadContextAlive = null);

/// <summary>Discovers plugin.json files without loading plugin assemblies and activates enabled plugins in collectible contexts.</summary>
public sealed class ExternalGatewayPluginHost : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly ILogger<ExternalGatewayPluginHost> _logger;
    private readonly string _pluginRoot;
    private readonly Dictionary<string, ExternalPluginRegistration> _registrations = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, LoadedExternalPlugin> _loaded = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, WeakReference> _unloadingContexts = new(StringComparer.OrdinalIgnoreCase);

    public ExternalGatewayPluginHost(
        IHostEnvironment environment,
        IConfiguration configuration,
        ILogger<ExternalGatewayPluginHost> logger)
    {
        _logger = logger;
        var configuredRoot = configuration["Gateway:Dashboard:PluginDirectory"];
        _pluginRoot = Path.GetFullPath(string.IsNullOrWhiteSpace(configuredRoot)
            ? Path.Combine(environment.ContentRootPath, "plugins")
            : Path.IsPathRooted(configuredRoot) ? configuredRoot : Path.Combine(environment.ContentRootPath, configuredRoot));

        Discover();
    }

    public IReadOnlyList<ExternalPluginRegistration> Registrations
    {
        get
        {
            RefreshUnloadStatuses();
            return _registrations.Values.OrderBy(x => x.Manifest.Order).ThenBy(x => x.Manifest.Id, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    public IReadOnlyList<PluginManifest> Manifests => Registrations.Select(x => x.Manifest).ToList();

    public IReadOnlyList<IGatewayPlugin> LoadedPlugins => _loaded.Values.Select(x => x.Plugin).ToList();

    public void ActivateEnabled(
        Func<string, bool> isEnabled,
        IReadOnlyDictionary<string, PluginManifest> availableManifests)
    {
        foreach (var registration in Registrations)
        {
            var pluginId = registration.Manifest.Id;
            if (registration.Status == ExternalPluginRegistrationStatus.InvalidManifest)
                continue;

            if (!isEnabled(pluginId))
            {
                UpdateStatus(pluginId, ExternalPluginRegistrationStatus.Disabled);
                continue;
            }

            var dependencyErrors = PluginDependencyValidator.Validate(registration.Manifest, availableManifests);
            if (dependencyErrors.Count > 0)
            {
                UpdateStatus(pluginId, ExternalPluginRegistrationStatus.DependencyUnsatisfied, string.Join("; ", dependencyErrors));
                continue;
            }

            Load(registration);
        }
    }

    public bool TryActivate(
        string pluginId,
        IReadOnlyDictionary<string, PluginManifest> availableManifests,
        out string? error)
    {
        error = null;
        if (!_registrations.TryGetValue(pluginId, out var registration))
        {
            error = $"External plugin '{pluginId}' was not discovered.";
            return false;
        }

        if (_loaded.ContainsKey(pluginId))
            return true;

        var dependencyErrors = PluginDependencyValidator.Validate(registration.Manifest, availableManifests);
        if (dependencyErrors.Count > 0)
        {
            error = string.Join("; ", dependencyErrors);
            UpdateStatus(pluginId, ExternalPluginRegistrationStatus.DependencyUnsatisfied, error);
            return false;
        }

        return Load(registration, out error);
    }

    public void Deactivate(string pluginId)
    {
        if (_loaded.Remove(pluginId, out var loaded))
        {
            var contextReference = new WeakReference(loaded.Context, trackResurrection: false);
            _unloadingContexts[pluginId] = contextReference;
            loaded.Context.Unload();
            loaded = null;
            UpdateStatus(pluginId, ExternalPluginRegistrationStatus.UnloadPending, isCollectible: true, isLoadContextAlive: contextReference.IsAlive);
            return;
        }

        if (_registrations.ContainsKey(pluginId))
            UpdateStatus(pluginId, ExternalPluginRegistrationStatus.Disabled);
    }

    private void Discover()
    {
        if (!Directory.Exists(_pluginRoot))
            return;

        foreach (var manifestPath in Directory.EnumerateFiles(_pluginRoot, "plugin.json", SearchOption.AllDirectories))
        {
            try
            {
                using var stream = File.OpenRead(manifestPath);
                var manifest = JsonSerializer.Deserialize<PluginManifest>(stream, JsonOptions)
                    ?? throw new InvalidDataException("Manifest is empty.");
                ValidateManifest(manifest, manifestPath);
                if (_registrations.ContainsKey(manifest.Id))
                    throw new InvalidDataException($"Duplicate plugin id '{manifest.Id}'.");

                _registrations[manifest.Id] = new ExternalPluginRegistration(
                    manifest, manifestPath, ExternalPluginRegistrationStatus.Discovered);
            }
            catch (Exception ex)
            {
                var fallbackId = $"invalid:{Path.GetRelativePath(_pluginRoot, manifestPath)}";
                var invalid = new PluginManifest(
                    fallbackId,
                    Path.GetFileName(Path.GetDirectoryName(manifestPath)) ?? fallbackId,
                    "0.0.0",
                    [],
                    [],
                    0,
                    new PluginResourceRequirements(),
                    [],
                    "Invalid external plugin manifest.");
                _registrations[fallbackId] = new ExternalPluginRegistration(
                    invalid, manifestPath, ExternalPluginRegistrationStatus.InvalidManifest, ex.Message);
                _logger.LogWarning(ex, "External plugin manifest {ManifestPath} is invalid", manifestPath);
            }
        }
    }

    public ExternalPluginRuntimeLoad LoadRuntime(string pluginId)
    {
        if (!_registrations.TryGetValue(pluginId, out var registration))
            throw new KeyNotFoundException($"External plugin manifest '{pluginId}' was not discovered.");

        CollectiblePluginLoadContext? context = null;
        try
        {
            var manifest = registration.Manifest;
            var pluginDirectory = Path.GetDirectoryName(registration.ManifestPath)!;
            var assemblyPath = Path.GetFullPath(Path.Combine(pluginDirectory, manifest.EntryAssembly!));
            if (!assemblyPath.StartsWith(Path.GetFullPath(pluginDirectory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("entryAssembly must stay inside the plugin directory.");
            if (!File.Exists(assemblyPath))
                throw new FileNotFoundException("Plugin entry assembly was not found.", assemblyPath);

            context = new CollectiblePluginLoadContext(assemblyPath);
            var assembly = context.LoadFromAssemblyPath(assemblyPath);
            var entryType = assembly.GetType(manifest.EntryType!, throwOnError: true, ignoreCase: false)!;
            if (!typeof(IGatewayPlugin).IsAssignableFrom(entryType))
                throw new InvalidCastException($"Entry type '{manifest.EntryType}' does not implement {nameof(IGatewayPlugin)}.");

            var plugin = (IGatewayPlugin)(Activator.CreateInstance(entryType)
                ?? throw new InvalidOperationException($"Could not create plugin entry type '{manifest.EntryType}'."));
            if (!string.Equals(plugin.PluginId, manifest.Id, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Entry plugin id '{plugin.PluginId}' does not match manifest id '{manifest.Id}'.");

            UpdateStatus(manifest.Id, ExternalPluginRegistrationStatus.Loaded, isCollectible: true, isLoadContextAlive: true);
            _logger.LogInformation("External plugin {PluginId} v{Version} loaded into a candidate runtime domain from {AssemblyPath}", manifest.Id, manifest.Version, assemblyPath);
            return new ExternalPluginRuntimeLoad(plugin, context, reference => TrackRuntimeUnload(manifest.Id, reference));
        }
        catch (Exception ex)
        {
            context?.Unload();
            UpdateStatus(registration.Manifest.Id, ExternalPluginRegistrationStatus.LoadFailed, ex.Message);
            _logger.LogError(ex, "External plugin {PluginId} failed to load", registration.Manifest.Id);
            throw;
        }
    }

    private void TrackRuntimeUnload(string pluginId, WeakReference contextReference)
    {
        _unloadingContexts[pluginId] = contextReference;
        UpdateStatus(pluginId, ExternalPluginRegistrationStatus.UnloadPending, isCollectible: true, isLoadContextAlive: true);
    }

    private bool Load(ExternalPluginRegistration registration) => Load(registration, out _);

    private bool Load(ExternalPluginRegistration registration, out string? error)
    {
        try
        {
            var runtime = LoadRuntime(registration.Manifest.Id);
            _unloadingContexts.Remove(registration.Manifest.Id);
            _loaded[registration.Manifest.Id] = new LoadedExternalPlugin(runtime.Plugin, runtime.DetachContext());
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static void ValidateManifest(PluginManifest manifest, string manifestPath)
    {
        if (string.IsNullOrWhiteSpace(manifest.Id)) throw new InvalidDataException("id is required.");
        if (string.IsNullOrWhiteSpace(manifest.Name)) throw new InvalidDataException("name is required.");
        if (!SemanticVersion.TryParse(manifest.Version, out _)) throw new InvalidDataException($"version '{manifest.Version}' is not semantic versioning compatible.");
        if (string.IsNullOrWhiteSpace(manifest.EntryAssembly)) throw new InvalidDataException("entryAssembly is required for external plugins.");
        if (string.IsNullOrWhiteSpace(manifest.EntryType)) throw new InvalidDataException("entryType is required for external plugins.");
        if (Path.IsPathRooted(manifest.EntryAssembly)) throw new InvalidDataException("entryAssembly must be relative to plugin.json.");
        _ = manifestPath;
    }

    private void UpdateStatus(
        string pluginId,
        ExternalPluginRegistrationStatus status,
        string? error = null,
        bool isCollectible = false,
        bool? isLoadContextAlive = null)
    {
        var current = _registrations[pluginId];
        _registrations[pluginId] = current with
        {
            Status = status,
            Error = error,
            IsCollectible = isCollectible,
            IsLoadContextAlive = isLoadContextAlive
        };
    }

    private void RefreshUnloadStatuses()
    {
        foreach (var (pluginId, contextReference) in _unloadingContexts.ToArray())
        {
            if (contextReference.IsAlive)
            {
                UpdateStatus(pluginId, ExternalPluginRegistrationStatus.UnloadPending, isCollectible: true, isLoadContextAlive: true);
                continue;
            }

            UpdateStatus(pluginId, ExternalPluginRegistrationStatus.Unloaded, isCollectible: true, isLoadContextAlive: false);
            _unloadingContexts.Remove(pluginId);
        }
    }

    public void Dispose()
    {
        foreach (var pluginId in _loaded.Keys.ToArray())
            Deactivate(pluginId);
        _loaded.Clear();
    }

    private sealed record LoadedExternalPlugin(IGatewayPlugin Plugin, AssemblyLoadContext Context);

    private sealed class CollectiblePluginLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;
        private readonly string _contractAssemblyName = typeof(IGatewayPlugin).Assembly.GetName().Name!;

        public CollectiblePluginLoadContext(string entryAssemblyPath)
            : base($"GatewayPlugin:{Path.GetFileNameWithoutExtension(entryAssemblyPath)}", isCollectible: true)
        {
            _resolver = new AssemblyDependencyResolver(entryAssemblyPath);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (string.Equals(assemblyName.Name, _contractAssemblyName, StringComparison.OrdinalIgnoreCase))
                return null;

            var path = _resolver.ResolveAssemblyToPath(assemblyName);
            return path == null ? null : LoadFromAssemblyPath(path);
        }

        protected override nint LoadUnmanagedDll(string unmanagedDllName)
        {
            var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            return path == null ? nint.Zero : LoadUnmanagedDllFromPath(path);
        }
    }
}

public sealed class ExternalPluginRuntimeLoad : IAsyncDisposable
{
    private AssemblyLoadContext? _context;
    private readonly Action<WeakReference> _onUnload;

    internal ExternalPluginRuntimeLoad(IGatewayPlugin plugin, AssemblyLoadContext context, Action<WeakReference> onUnload)
    {
        Plugin = plugin;
        _context = context;
        _onUnload = onUnload;
    }

    public IGatewayPlugin Plugin { get; }

    internal AssemblyLoadContext DetachContext() =>
        Interlocked.Exchange(ref _context, null)
        ?? throw new ObjectDisposedException(nameof(ExternalPluginRuntimeLoad));

    public ValueTask DisposeAsync()
    {
        if (Plugin is IPluginRuntimeResource runtimeResource)
        {
            try { runtimeResource.StopResourceAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult(); }
            catch { }
        }
        if (Plugin is IDisposable disposable)
            disposable.Dispose();
        var context = Interlocked.Exchange(ref _context, null);
        if (context != null)
        {
            var reference = new WeakReference(context, trackResurrection: false);
            context.Unload();
            _onUnload(reference);
        }
        return ValueTask.CompletedTask;
    }
}

/// <summary>Semantic-version dependency validation used before an external assembly is loaded.</summary>
public static class PluginDependencyValidator
{
    public static IReadOnlyList<string> Validate(PluginManifest manifest, IReadOnlyDictionary<string, PluginManifest> available)
    {
        var errors = new List<string>();
        foreach (var dependency in manifest.Dependencies ?? [])
        {
            if (!available.TryGetValue(dependency.PluginId, out var dependencyManifest))
            {
                errors.Add($"Missing dependency '{dependency.PluginId}'.");
                continue;
            }

            if (!SemanticVersion.TryParse(dependencyManifest.Version, out var actual))
            {
                errors.Add($"Dependency '{dependency.PluginId}' has invalid version '{dependencyManifest.Version}'.");
                continue;
            }

            if (!SemanticVersionConstraint.IsSatisfied(actual, dependency.MinimumVersion, out var constraintError))
                errors.Add($"Dependency '{dependency.PluginId}' version {dependencyManifest.Version} does not satisfy '{dependency.MinimumVersion}': {constraintError}");
        }
        return errors;
    }
}

internal readonly record struct SemanticVersion(int Major, int Minor, int Patch, string? PreRelease) : IComparable<SemanticVersion>
{
    public static bool TryParse(string? value, out SemanticVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var coreAndMetadata = value.Trim().TrimStart('v', 'V').Split('+', 2)[0];
        var parts = coreAndMetadata.Split('-', 2);
        var numbers = parts[0].Split('.');
        if (numbers.Length is < 1 or > 3 || !TryParseCoreNumber(numbers[0], out var major)) return false;
        var minor = 0;
        var patch = 0;
        if (numbers.Length > 1 && !TryParseCoreNumber(numbers[1], out minor)) return false;
        if (numbers.Length > 2 && !TryParseCoreNumber(numbers[2], out patch)) return false;
        var preRelease = parts.Length == 2 ? parts[1] : null;
        if (preRelease != null && !IsValidPreRelease(preRelease)) return false;
        version = new SemanticVersion(major, minor, patch, preRelease);
        return true;
    }

    private static bool TryParseCoreNumber(string value, out int number) =>
        int.TryParse(value, out number) && number >= 0 && (value.Length == 1 || value[0] != '0');

    private static bool IsValidPreRelease(string value) =>
        value.Length > 0 && value.Split('.').All(identifier =>
            identifier.Length > 0
            && identifier.All(character => char.IsAsciiLetterOrDigit(character) || character == '-')
            && (!identifier.All(char.IsDigit) || identifier.Length == 1 || identifier[0] != '0'));

    public int CompareTo(SemanticVersion other)
    {
        var result = Major.CompareTo(other.Major);
        if (result != 0) return result;
        result = Minor.CompareTo(other.Minor);
        if (result != 0) return result;
        result = Patch.CompareTo(other.Patch);
        if (result != 0) return result;
        if (PreRelease == null && other.PreRelease != null) return 1;
        if (PreRelease != null && other.PreRelease == null) return -1;
        if (PreRelease == null) return 0;

        var identifiers = PreRelease.Split('.');
        var otherIdentifiers = other.PreRelease!.Split('.');
        for (var index = 0; index < Math.Min(identifiers.Length, otherIdentifiers.Length); index++)
        {
            var identifier = identifiers[index];
            var otherIdentifier = otherIdentifiers[index];
            var numeric = int.TryParse(identifier, out var numericValue);
            var otherNumeric = int.TryParse(otherIdentifier, out var otherNumericValue);
            if (numeric && otherNumeric)
            {
                result = numericValue.CompareTo(otherNumericValue);
            }
            else if (numeric != otherNumeric)
            {
                result = numeric ? -1 : 1;
            }
            else
            {
                result = string.Compare(identifier, otherIdentifier, StringComparison.Ordinal);
            }

            if (result != 0) return result;
        }

        return identifiers.Length.CompareTo(otherIdentifiers.Length);
    }
}

internal static class SemanticVersionConstraint
{
    public static bool IsSatisfied(SemanticVersion actual, string? constraint, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(constraint)) return true;

        var comparators = constraint.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var comparator in comparators)
        {
            var op = comparator.StartsWith(">=", StringComparison.Ordinal) ? ">="
                : comparator.StartsWith("<=", StringComparison.Ordinal) ? "<="
                : comparator.StartsWith(">", StringComparison.Ordinal) ? ">"
                : comparator.StartsWith("<", StringComparison.Ordinal) ? "<"
                : comparator.StartsWith("=", StringComparison.Ordinal) ? "=" : ">=";
            var versionText = op == ">=" && !comparator.StartsWith(">=", StringComparison.Ordinal)
                ? comparator
                : comparator[op.Length..];
            if (!SemanticVersion.TryParse(versionText, out var required))
            {
                error = $"invalid semantic version comparator '{comparator}'";
                return false;
            }

            var comparison = actual.CompareTo(required);
            var satisfied = op switch
            {
                ">=" => comparison >= 0,
                "<=" => comparison <= 0,
                ">" => comparison > 0,
                "<" => comparison < 0,
                "=" => comparison == 0,
                _ => false
            };
            if (!satisfied) return false;
        }

        return true;
    }
}
