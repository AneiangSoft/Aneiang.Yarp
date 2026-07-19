using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Application;

/// <summary>
/// RAG-style configuration knowledge service.
/// Loads embedded JSON knowledge files and provides keyword search.
/// </summary>
public class ConfigKnowledgeService : IConfigKnowledgeService
{
    private const string ResourcePrefix = "Aneiang.Yarp.Dashboard.Infrastructure.Knowledge.";
    private const string ResourceSuffix = ".json";

    private readonly ILogger<ConfigKnowledgeService> _logger;
    private readonly ConcurrentDictionary<string, KnowledgeEntry> _cache = new();
    private volatile bool _loaded;
    private readonly object _loadLock = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ConfigKnowledgeService(ILogger<ConfigKnowledgeService> logger)
    {
        _logger = logger;
    }

    public Task<List<KnowledgeEntry>> GetAllTopicsAsync(CancellationToken ct = default)
    {
        EnsureLoaded();
        return Task.FromResult(_cache.Values.ToList());
    }

    public Task<KnowledgeEntry?> GetTopicAsync(string topicId, CancellationToken ct = default)
    {
        EnsureLoaded();
        _cache.TryGetValue(topicId, out var entry);
        return Task.FromResult(entry);
    }

    public Task<KnowledgeResult?> SearchAsync(string query, CancellationToken ct = default)
    {
        EnsureLoaded();

        if (string.IsNullOrWhiteSpace(query))
            return Task.FromResult<KnowledgeResult?>(null);

        var queryLower = query.ToLowerInvariant();
        var terms = queryLower.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var hits = new List<KnowledgeSearchHit>();

        foreach (var entry in _cache.Values)
        {
            var score = CalculateRelevance(entry, terms);
            if (score > 0)
            {
                hits.Add(new KnowledgeSearchHit
                {
                    TopicId = entry.TopicId,
                    Title = entry.Title,
                    Summary = entry.Summary,
                    RelevanceScore = Math.Round(score, 2),
                    Snippet = ExtractSnippet(entry, terms)
                });
            }
        }

        hits.Sort((a, b) => b.RelevanceScore.CompareTo(a.RelevanceScore));

        return Task.FromResult<KnowledgeResult?>(new KnowledgeResult
        {
            Query = query,
            Results = hits.Take(10).ToList()
        });
    }

    private static double CalculateRelevance(KnowledgeEntry entry, string[] terms)
    {
        var titleLower = entry.Title.ToLowerInvariant();
        var summaryLower = entry.Summary.ToLowerInvariant();
        var contentLower = entry.Content.ToLowerInvariant();
        var keyPointsLower = string.Join(' ', entry.KeyPoints).ToLowerInvariant();
        var practicesLower = string.Join(' ', entry.BestPractices).ToLowerInvariant();

        double score = 0;

        foreach (var term in terms)
        {
            if (titleLower.Contains(term)) score += 3.0;
            if (summaryLower.Contains(term)) score += 2.0;
            if (keyPointsLower.Contains(term)) score += 1.5;
            if (practicesLower.Contains(term)) score += 1.0;
            if (contentLower.Contains(term)) score += 0.5;
        }

        return score;
    }

    private static string ExtractSnippet(KnowledgeEntry entry, string[] terms)
    {
        var content = entry.Content;
        if (string.IsNullOrEmpty(content))
            return entry.Summary;

        // Find the first occurrence of any term and extract surrounding text
        var contentLower = content.ToLowerInvariant();
        var bestPos = -1;

        foreach (var term in terms)
        {
            var pos = contentLower.IndexOf(term, StringComparison.OrdinalIgnoreCase);
            if (pos >= 0 && (bestPos < 0 || pos < bestPos))
                bestPos = pos;
        }

        if (bestPos < 0)
            return content.Length > 200 ? content[..200] + "..." : content;

        var start = Math.Max(0, bestPos - 50);
        var end = Math.Min(content.Length, bestPos + 150);
        var snippet = (start > 0 ? "..." : "") + content[start..end] + (end < content.Length ? "..." : "");

        return snippet;
    }

    private void EnsureLoaded()
    {
        if (_loaded) return;

        lock (_loadLock)
        {
            if (_loaded) return;

            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var resourceNames = assembly.GetManifestResourceNames()
                    .Where(n => n.StartsWith(ResourcePrefix, StringComparison.OrdinalIgnoreCase) &&
                                n.EndsWith(ResourceSuffix, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var resourceName in resourceNames)
                {
                    try
                    {
                        using var stream = assembly.GetManifestResourceStream(resourceName);
                        if (stream == null) continue;

                        using var reader = new StreamReader(stream);
                        var json = reader.ReadToEnd();
                        var entry = JsonSerializer.Deserialize<KnowledgeEntry>(json, JsonOpts);

                        if (entry != null && !string.IsNullOrEmpty(entry.TopicId))
                        {
                            _cache[entry.TopicId] = entry;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to load knowledge resource: {Resource}", resourceName);
                    }
                }

                _loaded = true;
                _logger.LogInformation("[ConfigKnowledge] Loaded {Count} knowledge entries", _cache.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ConfigKnowledge] Failed to load knowledge base");
                _loaded = true; // Mark as loaded to avoid retry loops
            }
        }
    }
}
