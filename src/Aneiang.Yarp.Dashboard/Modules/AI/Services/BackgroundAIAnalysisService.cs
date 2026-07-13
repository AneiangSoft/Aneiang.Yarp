using Aneiang.Yarp.Services;
using Aneiang.Yarp.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Dashboard.Modules.AI.Services;

/// <summary>
/// Background service that periodically analyzes proxy logs using AI.
/// Detects anomalies (error rate spikes, latency surges, WAF attack surges),
/// generates log summaries, and produces configuration suggestions.
/// Results are stored in the ai_analysis table and severe anomalies trigger notifications.
/// </summary>
public class BackgroundAIAnalysisService : BackgroundService
{
    private readonly IAIProvider _provider;
    private readonly IProxyLogRepository _logRepo;
    private readonly IAIAnalysisRepository _analysisRepo;
    private readonly GatewayContextProvider _contextProvider;
    private readonly INotificationService _notificationService;
    private readonly AIOptions _options;
    private readonly ILogger<BackgroundAIAnalysisService> _logger;

    // Track the last analysis time to avoid duplicate runs
    private DateTime _lastAnalysisRun = DateTime.MinValue;
    // Track the previous 5xx count for anomaly detection baseline
    private int _previous5xxCount = -1;

    public BackgroundAIAnalysisService(
        IAIProvider provider,
        IProxyLogRepository logRepo,
        IAIAnalysisRepository analysisRepo,
        GatewayContextProvider contextProvider,
        INotificationService notificationService,
        IOptions<AIOptions> options,
        ILogger<BackgroundAIAnalysisService> logger)
    {
        _provider = provider;
        _logRepo = logRepo;
        _analysisRepo = analysisRepo;
        _contextProvider = contextProvider;
        _notificationService = notificationService;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.EnableBackgroundAnalysis || !_provider.IsAvailable)
        {
            _logger.LogInformation("[AI-Analysis] Background analysis disabled or AI provider unavailable. Service idle.");
            return;
        }

        _logger.LogInformation("[AI-Analysis] Background analysis started. Interval: {Interval}", _options.AnalysisInterval);

        // Wait 2 minutes after startup before first analysis
        try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunAnalysisCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AI-Analysis] Error during analysis cycle: {Message}", ex.Message);
            }

            _lastAnalysisRun = DateTime.UtcNow;

            try
            {
                await Task.Delay(_options.AnalysisInterval, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
        }

        _logger.LogInformation("[AI-Analysis] Background analysis stopped.");
    }

    /// <summary>
    /// Executes one full analysis cycle: log summary → anomaly detection → suggestions.
    /// </summary>
    private async Task RunAnalysisCycleAsync(CancellationToken ct)
    {
        _logger.LogInformation("[AI-Analysis] Starting analysis cycle at {Time:yyyy-MM-dd HH:mm:ss}", DateTime.UtcNow);

        // 1. Collect statistics
        var recentStats = await _logRepo.GetStatsAsync(60, ct); // last hour
        var previousStats = await _logRepo.GetStatsAsync(120, ct); // last 2 hours (includes recent)
        var topIssues = await _logRepo.GetTopIssuesAsync(DateTime.UtcNow.AddHours(1), 10, ct);
        var recent5xx = await _logRepo.GetRecent5xxCountAsync(60, ct);
        var gatewayContext = await _contextProvider.BuildContextAsync(ct);

        // 2. Generate log summary
        await GenerateLogSummaryAsync(recentStats, topIssues, recent5xx, ct);

        // 3. Anomaly detection
        await DetectAnomaliesAsync(recentStats, previousStats, topIssues, recent5xx, gatewayContext, ct);

        // 4. Configuration suggestions (only every 4 hours to avoid noise)
        if ((DateTime.UtcNow - _lastAnalysisRun).TotalHours >= 4)
        {
            await GenerateSuggestionsAsync(recentStats, topIssues, gatewayContext, ct);
        }

        // 5. Purge old analysis results (keep 7 days)
        await _analysisRepo.PurgeOlderThanAsync(DateTime.UtcNow.AddDays(-7), ct);

        _previous5xxCount = recent5xx;

        _logger.LogInformation("[AI-Analysis] Analysis cycle completed.");
    }

    /// <summary>
    /// Generate a structured log summary using AI.
    /// </summary>
    private async Task GenerateLogSummaryAsync(
        ProxyLogStatsResult stats,
        List<ProxyLogRouteIssue> topIssues,
        int recent5xx,
        CancellationToken ct)
    {
        try
        {
            var prompt = $"""
                Analyze the following gateway statistics from the last hour and provide a brief operational summary.
                Focus on: overall health, error rate trends, latency concerns, and notable patterns.

                Statistics:
                - Total requests: {stats.TotalRequests}
                - Success: {stats.SuccessCount}, Errors: {stats.ErrorCount}
                - Error rate: {(stats.TotalRequests > 0 ? (double)stats.ErrorCount / stats.TotalRequests * 100 : 0):F2}%
                - Average latency: {stats.AvgLatencyMs:F1}ms
                - P50/P90/P99 latency: {stats.P50LatencyMs:F1}ms / {stats.P90LatencyMs:F1}ms / {stats.P99LatencyMs:F1}ms
                - Requests per minute: {stats.RequestsPerMinute}
                - 5xx errors (last hour): {recent5xx}

                Top error routes:
                {string.Join("\n", topIssues.Select(i => $"  - {i.RouteId}: {i.ErrorCount}/{i.TotalCount} errors"))}

                Respond in 3-5 sentences. Be concise. Use markdown.
                """;

            var request = new AIChatRequest
            {
                SystemPrompt = "You are a gateway operations analyst. Provide concise, actionable summaries.",
                Messages = [new() { Role = "user", Content = prompt }],
                Model = _options.AnalysisModel,
                Temperature = 0.2,
                MaxTokens = 512
            };

            var response = await _provider.ChatAsync(request, ct);
            if (response?.Content is { Length: > 0 })
            {
                await _analysisRepo.SaveAnalysisAsync(new AIAnalysisEntry
                {
                    AnalysisType = "log_summary",
                    Content = response.Content,
                    Severity = 0, // info level
                    CreatedAt = DateTime.UtcNow
                }, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AI-Analysis] Log summary generation failed");
        }
    }

    /// <summary>
    /// Detect anomalies by comparing current stats with historical baseline.
    /// Uses AI for contextual analysis when thresholds are breached.
    /// </summary>
    private async Task DetectAnomaliesAsync(
        ProxyLogStatsResult recent,
        ProxyLogStatsResult previous,
        List<ProxyLogRouteIssue> topIssues,
        int recent5xx,
        string gatewayContext,
        CancellationToken ct)
    {
        var anomalies = new List<string>();

        // Error rate spike detection (>3x baseline)
        var currentErrorRate = recent.TotalRequests > 0 ? (double)recent.ErrorCount / recent.TotalRequests : 0;
        var baselineErrorRate = previous.TotalRequests > 0 ? (double)previous.ErrorCount / previous.TotalRequests : 0;
        if (baselineErrorRate > 0 && currentErrorRate > baselineErrorRate * 3)
        {
            anomalies.Add($"Error rate spike: {currentErrorRate:P1} (baseline: {baselineErrorRate:P1}, {currentErrorRate / baselineErrorRate:F1}x increase)");
        }

        // 5xx surge detection
        if (_previous5xxCount >= 0 && recent5xx > _previous5xxCount * 3 && recent5xx > 10)
        {
            anomalies.Add($"5xx surge: {recent5xx} in last hour (previous: {_previous5xxCount})");
        }

        // Latency spike detection
        if (recent.P99LatencyMs > 0 && previous.P99LatencyMs > 0 && recent.P99LatencyMs > previous.P99LatencyMs * 3)
        {
            anomalies.Add($"P99 latency spike: {recent.P99LatencyMs:F0}ms (baseline: {previous.P99LatencyMs:F0}ms)");
        }

        // Route-level error concentration
        foreach (var issue in topIssues)
        {
            if (issue.TotalCount > 0 && (double)issue.ErrorCount / issue.TotalCount > 0.5 && issue.ErrorCount > 20)
            {
                anomalies.Add($"Route '{issue.RouteId}' has {(double)issue.ErrorCount / issue.TotalCount:P0} error rate ({issue.ErrorCount} errors)");
            }
        }

        if (anomalies.Count == 0)
        {
            _logger.LogDebug("[AI-Analysis] No anomalies detected.");
            return;
        }

        _logger.LogWarning("[AI-Analysis] {Count} anomalies detected: {Anomalies}",
            anomalies.Count, string.Join("; ", anomalies));

        // Use AI to generate contextual analysis of anomalies
        try
        {
            var prompt = $"""
                The following anomalies were detected in the gateway. Analyze them and provide:
                1. Likely root cause(s)
                2. Recommended actions (prioritized)

                Anomalies:
                {string.Join("\n", anomalies.Select(a => $"- {a}"))}

                Gateway context:
                {gatewayContext}

                Respond in markdown. Be specific and actionable. Max 200 words.
                """;

            var request = new AIChatRequest
            {
                SystemPrompt = "You are a senior SRE engineer analyzing gateway anomalies. Provide precise root cause analysis and remediation steps.",
                Messages = [new() { Role = "user", Content = prompt }],
                Model = _options.AnalysisModel,
                Temperature = 0.2,
                MaxTokens = 1024
            };

            var response = await _provider.ChatAsync(request, ct);
            if (response?.Content is { Length: > 0 })
            {
                var relatedRoutes = string.Join(",", topIssues.Where(i => i.ErrorCount > 0).Select(i => i.RouteId ?? ""));
                var severity = anomalies.Count >= 3 ? 2 : 1; // 2 = critical, 1 = warning

                await _analysisRepo.SaveAnalysisAsync(new AIAnalysisEntry
                {
                    AnalysisType = "anomaly",
                    Content = $"**Anomalies Detected:**\n{string.Join("\n", anomalies.Select(a => $"- {a}"))}\n\n{response.Content}",
                    Severity = severity,
                    RelatedRoutes = relatedRoutes,
                    CreatedAt = DateTime.UtcNow
                }, ct);

                // Push critical anomalies as notifications
                if (severity >= 2)
                {
                    _notificationService.NotifyCustom(
                        "AIAnomalyAlert",
                        "🔍 AI Anomaly Alert",
                        $"Detected {anomalies.Count} anomalies. Top issue: {anomalies[0]}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AI-Analysis] Anomaly AI analysis failed, saving raw anomalies");

            // Still save raw anomalies even if AI call fails
            await _analysisRepo.SaveAnalysisAsync(new AIAnalysisEntry
            {
                AnalysisType = "anomaly",
                Content = $"**Anomalies Detected (raw):**\n{string.Join("\n", anomalies.Select(a => $"- {a}"))}",
                Severity = 1,
                CreatedAt = DateTime.UtcNow
            }, ct);
        }
    }

    /// <summary>
    /// Generate configuration suggestions based on traffic patterns.
    /// </summary>
    private async Task GenerateSuggestionsAsync(
        ProxyLogStatsResult stats,
        List<ProxyLogRouteIssue> topIssues,
        string gatewayContext,
        CancellationToken ct)
    {
        try
        {
            var prompt = $"""
                Based on the following gateway statistics and configuration, suggest up to 3 improvements
                for rate limiting, circuit breaker, or routing configuration.

                Statistics (last hour):
                - Total: {stats.TotalRequests}, Errors: {stats.ErrorCount}
                - Avg latency: {stats.AvgLatencyMs:F0}ms, P99: {stats.P99LatencyMs:F0}ms
                - RPM: {stats.RequestsPerMinute}

                Top error routes:
                {string.Join("\n", topIssues.Select(i => $"  - {i.RouteId}: {i.ErrorCount}/{i.TotalCount}"))}

                {gatewayContext}

                Only suggest changes that would have measurable impact. Format as a numbered list.
                Each suggestion: what to change, why, and the expected benefit. Max 150 words.
                """;

            var request = new AIChatRequest
            {
                SystemPrompt = "You are a gateway optimization expert. Suggest practical configuration improvements.",
                Messages = [new() { Role = "user", Content = prompt }],
                Model = _options.AnalysisModel,
                Temperature = 0.3,
                MaxTokens = 768
            };

            var response = await _provider.ChatAsync(request, ct);
            if (response?.Content is { Length: > 0 })
            {
                await _analysisRepo.SaveAnalysisAsync(new AIAnalysisEntry
                {
                    AnalysisType = "suggestion",
                    Content = response.Content,
                    Severity = 0,
                    CreatedAt = DateTime.UtcNow
                }, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AI-Analysis] Suggestion generation failed");
        }
    }
}
