using Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Services;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Health;

/// <summary>
/// Evaluates configuration health by running all registered rules and computing a score.
/// Score = 100 - Σ(deductions). Critical: -15, Warning: -5, Info: -1. Min: 0.
/// Cache is automatically invalidated when configuration changes.
/// </summary>
public class ConfigHealthService
{
    private readonly IEnumerable<IConfigHealthRule> _rules;
    private readonly ILogger<ConfigHealthService> _logger;
    private volatile HealthReport? _cached;
    private DateTime _cacheExpiry;

    public ConfigHealthService(
        IEnumerable<IConfigHealthRule> rules,
        ILogger<ConfigHealthService> logger,
        ConfigChangeAuditLog? configChangeAuditLog = null)
    {
        _rules = rules;
        _logger = logger;

        // Subscribe to config change events to invalidate cache
        if (configChangeAuditLog != null)
        {
            configChangeAuditLog.OnConfigChanged += (eventType, target, operatorName, details) =>
            {
                InvalidateCache();
                _logger.LogDebug("[ConfigHealth] Cache invalidated due to config change: {EventType} on {Target}", eventType, target);
            };
        }
    }

    /// <summary>Evaluate all rules and produce a health report.</summary>
    public async Task<HealthReport> EvaluateAsync(ConfigHealthContext context, bool forceRefresh = false, CancellationToken ct = default)
    {
        if (!forceRefresh && _cached != null && DateTime.UtcNow < _cacheExpiry)
            return _cached;

        var results = new List<HealthRuleResult>();
        var ruleList = _rules.ToList();

        foreach (var rule in ruleList)
        {
            try
            {
                var result = await rule.EvaluateAsync(context, ct);
                if (result.IsApplicable)
                    results.Add(result);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[ConfigHealth] Rule {RuleId} evaluation failed", rule.Id);
            }
        }

        var triggered = results.Where(r => r.Triggered).ToList();
        var score = CalculateScore(triggered);

        var report = new HealthReport
        {
            Score = score,
            Grade = GetGrade(score),
            TotalRules = results.Count,
            TriggeredRules = triggered.Count,
            Issues = triggered.Select(r => new HealthIssue
            {
                RuleId = r.RuleId,
                Category = r.Category,
                Level = r.Level.ToString(),
                Title = r.Title,
                Description = r.Detail,
                Recommendation = r.Recommendation,
                ConfigPageUrl = r.ConfigPageUrl
            }).ToList(),
            EvaluatedAt = DateTime.UtcNow
        };

        _cached = report;
        _cacheExpiry = DateTime.UtcNow.AddSeconds(60);

        return report;
    }

    private static int CalculateScore(List<HealthRuleResult> triggered)
    {
        var deduction = triggered.Sum(r => r.Level switch
        {
            Severity.Critical => 15,
            Severity.Warning => 5,
            Severity.Info => 1,
            _ => 0
        });

        return Math.Max(0, 100 - deduction);
    }

    private static string GetGrade(int score) => score switch
    {
        >= 90 => "A",
        >= 80 => "B",
        >= 70 => "C",
        >= 60 => "D",
        _ => "F"
    };

    /// <summary>Invalidate the cached report.</summary>
    public void InvalidateCache()
    {
        _cached = null;
    }
}

/// <summary>Full health report.</summary>
public class HealthReport
{
    public int Score { get; set; }
    public string Grade { get; set; } = "";
    public int TotalRules { get; set; }
    public int TriggeredRules { get; set; }
    public List<HealthIssue> Issues { get; set; } = [];
    public DateTime EvaluatedAt { get; set; }
}

/// <summary>A single health issue found during evaluation.</summary>
public class HealthIssue
{
    public string RuleId { get; set; } = "";
    public string Category { get; set; } = "";
    public string Level { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Recommendation { get; set; } = "";
    public string ConfigPageUrl { get; set; } = "";
}
