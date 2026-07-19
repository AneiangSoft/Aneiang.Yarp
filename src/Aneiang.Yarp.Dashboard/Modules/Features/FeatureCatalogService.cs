using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Modules.Features;

/// <summary>
/// Loads feature catalog from embedded JSON resource.
/// </summary>
public class FeatureCatalogService : IFeatureCatalogService
{
    private const string ResourceName = "Aneiang.Yarp.Dashboard.Infrastructure.Features.features.json";
    private readonly ILogger<FeatureCatalogService> _logger;
    private List<FeatureInfo>? _cache;
    private readonly object _lock = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public FeatureCatalogService(ILogger<FeatureCatalogService> logger)
    {
        _logger = logger;
    }

    public Task<List<FeatureInfo>> GetAllAsync(CancellationToken ct = default)
    {
        EnsureLoaded();
        return Task.FromResult(_cache ?? []);
    }

    public Task<FeatureInfo?> GetAsync(string featureId, CancellationToken ct = default)
    {
        EnsureLoaded();
        return Task.FromResult(_cache?.FirstOrDefault(f => f.Id == featureId));
    }

    private void EnsureLoaded()
    {
        if (_cache != null) return;

        lock (_lock)
        {
            if (_cache != null) return;

            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                using var stream = assembly.GetManifestResourceStream(ResourceName);
                if (stream == null)
                {
                    _logger.LogWarning("[FeatureCatalog] Resource not found: {Resource}", ResourceName);
                    _cache = [];
                    return;
                }

                using var reader = new StreamReader(stream);
                var json = reader.ReadToEnd();
                _cache = JsonSerializer.Deserialize<List<FeatureInfo>>(json, JsonOpts) ?? [];
                _logger.LogInformation("[FeatureCatalog] Loaded {Count} features", _cache.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[FeatureCatalog] Failed to load features");
                _cache = [];
            }
        }
    }
}
