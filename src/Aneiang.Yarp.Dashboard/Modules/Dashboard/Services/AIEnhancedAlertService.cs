using Aneiang.Yarp.Dashboard.Infrastructure.Alert;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Modules.Dashboard.Services;

/// <summary>
/// Decorator around <see cref="IGatewayAlertService"/> that, when <c>AI.EnhanceNotif</c>
/// is enabled, asynchronously asks the LLM to add context and remediation suggestions for
/// each alert and emits an additional enriched notification through the inner service.
/// The raw alert is always forwarded first so no alert is ever lost.
/// </summary>
public class AIEnhancedAlertService : IGatewayAlertService
{
    private readonly IGatewayAlertService _inner;
    private readonly AIConfigStore _config;
    private readonly IServiceProvider _services;
    private readonly ILogger<AIEnhancedAlertService> _logger;

    public AIEnhancedAlertService(
        IGatewayAlertService inner,
        AIConfigStore config,
        IServiceProvider services,
        ILogger<AIEnhancedAlertService> logger)
    {
        _inner = inner;
        _config = config;
        _services = services;
        _logger = logger;
    }

    public void AlertCircuitBreakerOpen(string clusterId, string? destinationId = null)
    {
        _inner.AlertCircuitBreakerOpen(clusterId, destinationId);
        Enhance("CircuitBreakerOpen", $"熔断器已打开 cluster={clusterId}, destination={destinationId}");
    }

    public void AlertRetryExhausted(string clusterId, string routeId, int attempts, int statusCode)
    {
        _inner.AlertRetryExhausted(clusterId, routeId, attempts, statusCode);
        Enhance("RetryExhausted", $"重试耗尽 cluster={clusterId}, route={routeId}, attempts={attempts}, status={statusCode}");
    }

    public void AlertWafBlock(string clientIp, string blockReason, string? uri = null)
    {
        _inner.AlertWafBlock(clientIp, blockReason, uri);
        Enhance("WafBlock", $"WAF 拦截 clientIp={clientIp}, reason={blockReason}, uri={uri}");
    }

    public void AlertProxyError(string clusterId, string? destinationId, string errorMessage, string? stackTrace = null)
    {
        _inner.AlertProxyError(clusterId, destinationId, errorMessage, stackTrace);
        Enhance("ProxyError", $"代理错误 cluster={clusterId}, destination={destinationId}, error={errorMessage}");
    }

    public void AlertRateLimitExceeded(string clientIp, string? routeId = null)
    {
        _inner.AlertRateLimitExceeded(clientIp, routeId);
        Enhance("RateLimitExceeded", $"触发限流 clientIp={clientIp}, route={routeId}");
    }

    public void AlertCustom(string alertType, string title, string message, string severity = "Info")
    {
        _inner.AlertCustom(alertType, title, message, severity);
        Enhance(alertType, $"{title} - {message}");
    }

    private void Enhance(string type, string context)
    {
        if (!_config.Current.EnhanceNotif || !_config.Current.IsConfigured) return;

        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _services.CreateScope();
                var ai = scope.ServiceProvider.GetRequiredService<AIService>();
                const string systemPrompt =
                    "你是网关运维助手。根据给定的告警信息，用中文输出一段简短分析（1~3 句）：说明可能的原因，并给出 1~2 条可操作的处置建议。不要使用列表符号或 Markdown。";
                var (ok, content) = await ai.CompleteAsync(systemPrompt, $"告警类型: {type}\n告警详情: {context}");
                if (ok && !string.IsNullOrWhiteSpace(content))
                {
                    _inner.AlertCustom(type, "AI 增强分析", content, "Info");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AI notification enhancement failed for alert {Type}", type);
            }
        });
    }
}
