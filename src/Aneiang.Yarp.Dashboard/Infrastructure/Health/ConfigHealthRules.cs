namespace Aneiang.Yarp.Dashboard.Infrastructure.Health;

/// <summary>
/// All 12 built-in configuration health rules.
/// Organized by category: Security (SEC), Reliability (REL), Performance (PER), BestPractice (BP).
/// </summary>
public static class ConfigHealthRules
{
    // ───────────── Security Rules ─────────────

    public class WafEnabledRule : IConfigHealthRule
    {
        public string Id => "SEC001";
        public string Category => "Security";
        public Severity Level => Severity.Critical;
        public string Title => "WAF 未启用";
        public string Description => "WAF 防火墙当前处于禁用状态";
        public string Recommendation => "启用 SQL注入/XSS/路径遍历检测";
        public string ConfigPageUrl => "/apigateway/waf";

        public Task<HealthRuleResult> EvaluateAsync(ConfigHealthContext ctx, CancellationToken ct = default)
        {
            var result = Build(Id, Category, Level, Title, Recommendation, ConfigPageUrl);
            result.Triggered = !ctx.WafEnabled;
            result.Detail = result.Triggered ? "WAF 全局禁用，网关暴露在 SQL 注入、XSS 等攻击风险下" : "WAF 已启用";
            return Task.FromResult(result);
        }
    }

    public class IpBlacklistRule : IConfigHealthRule
    {
        public string Id => "SEC002";
        public string Category => "Security";
        public Severity Level => Severity.Warning;
        public string Title => "未配置 IP 黑名单";
        public string Description => "IP 黑名单为空";
        public string Recommendation => "配置已知恶意 IP 到黑名单";
        public string ConfigPageUrl => "/apigateway/waf";

        public Task<HealthRuleResult> EvaluateAsync(ConfigHealthContext ctx, CancellationToken ct = default)
        {
            var result = Build(Id, Category, Level, Title, Recommendation, ConfigPageUrl);
            result.Triggered = ctx.WafIpBlacklistCount == 0;
            result.Detail = result.Triggered ? "IP 黑名单为空，无法主动拦截恶意 IP" : $"已配置 {ctx.WafIpBlacklistCount} 个黑名单 IP";
            return Task.FromResult(result);
        }
    }

    public class RequestBodySizeRule : IConfigHealthRule
    {
        public string Id => "SEC003";
        public string Category => "Security";
        public Severity Level => Severity.Warning;
        public string Title => "请求体大小限制过大";
        public string Description => "MaxRequestBodySize 超过 50MB";
        public string Recommendation => "设置合理的 MaxRequestBodySize";
        public string ConfigPageUrl => "/apigateway/waf";

        public Task<HealthRuleResult> EvaluateAsync(ConfigHealthContext ctx, CancellationToken ct = default)
        {
            var result = Build(Id, Category, Level, Title, Recommendation, ConfigPageUrl);
            var fiftyMb = 50L * 1024 * 1024;
            result.Triggered = ctx.WafMaxRequestBodySize > fiftyMb;
            result.Detail = result.Triggered
                ? $"MaxRequestBodySize = {ctx.WafMaxRequestBodySize / 1024 / 1024}MB，过大可能增加 DoS 风险"
                : $"MaxRequestBodySize = {ctx.WafMaxRequestBodySize / 1024 / 1024}MB";
            return Task.FromResult(result);
        }
    }

    // ───────────── Reliability Rules ─────────────

    public class HealthCheckRule : IConfigHealthRule
    {
        public string Id => "REL001";
        public string Category => "Reliability";
        public Severity Level => Severity.Critical;
        public string Title => "集群缺少健康检查";
        public string Description => "部分集群未配置健康检查";
        public string Recommendation => "为每个集群添加 Active Health Check";
        public string ConfigPageUrl => "/apigateway/clusters";

        public Task<HealthRuleResult> EvaluateAsync(ConfigHealthContext ctx, CancellationToken ct = default)
        {
            var result = Build(Id, Category, Level, Title, Recommendation, ConfigPageUrl);
            var noHealthCheck = ctx.Clusters.Where(c => !c.HasHealthCheck).Select(c => c.ClusterId).ToList();
            result.Triggered = noHealthCheck.Count > 0;
            result.Detail = result.Triggered
                ? $"{noHealthCheck.Count} 个集群缺少健康检查: {string.Join(", ", noHealthCheck.Take(5))}"
                : "所有集群已配置健康检查";
            return Task.FromResult(result);
        }
    }

    public class CircuitBreakerRule : IConfigHealthRule
    {
        public string Id => "REL002";
        public string Category => "Reliability";
        public Severity Level => Severity.Critical;
        public string Title => "集群缺少熔断器";
        public string Description => "部分集群未关联熔断器策略";
        public string Recommendation => "创建熔断器策略并应用到集群";
        public string ConfigPageUrl => "/apigateway/policies";

        public Task<HealthRuleResult> EvaluateAsync(ConfigHealthContext ctx, CancellationToken ct = default)
        {
            var result = Build(Id, Category, Level, Title, Recommendation, ConfigPageUrl);
            var noCb = ctx.Clusters.Where(c => !ctx.ClustersWithCircuitBreaker.Contains(c.ClusterId))
                .Select(c => c.ClusterId).ToList();
            result.Triggered = noCb.Count > 0;
            result.Detail = result.Triggered
                ? $"{noCb.Count} 个集群未关联熔断器: {string.Join(", ", noCb.Take(5))}"
                : "所有集群已关联熔断器";
            return Task.FromResult(result);
        }
    }

    public class SingleBackendRule : IConfigHealthRule
    {
        public string Id => "REL003";
        public string Category => "Reliability";
        public Severity Level => Severity.Warning;
        public string Title => "集群仅有单个后端";
        public string Description => "单后端集群无高可用能力";
        public string Recommendation => "添加至少 2 个后端地址";
        public string ConfigPageUrl => "/apigateway/clusters";

        public Task<HealthRuleResult> EvaluateAsync(ConfigHealthContext ctx, CancellationToken ct = default)
        {
            var result = Build(Id, Category, Level, Title, Recommendation, ConfigPageUrl);
            var single = ctx.Clusters.Where(c => c.DestinationCount <= 1).Select(c => c.ClusterId).ToList();
            result.Triggered = single.Count > 0;
            result.Detail = result.Triggered
                ? $"{single.Count} 个集群仅有单个后端: {string.Join(", ", single.Take(5))}"
                : "所有集群至少有 2 个后端";
            return Task.FromResult(result);
        }
    }

    public class RetryRule : IConfigHealthRule
    {
        public string Id => "REL004";
        public string Category => "Reliability";
        public Severity Level => Severity.Warning;
        public string Title => "未启用请求重试";
        public string Description => "没有路由配置重试策略";
        public string Recommendation => "为关键路由创建重试策略";
        public string ConfigPageUrl => "/apigateway/policies";

        public Task<HealthRuleResult> EvaluateAsync(ConfigHealthContext ctx, CancellationToken ct = default)
        {
            var result = Build(Id, Category, Level, Title, Recommendation, ConfigPageUrl);
            result.Triggered = ctx.RoutesWithRetry.Count == 0 && ctx.Routes.Count > 0;
            result.Detail = result.Triggered
                ? "没有路由配置重试策略，临时故障时无法自动恢复"
                : $"{ctx.RoutesWithRetry.Count} 个路由已配置重试";
            return Task.FromResult(result);
        }
    }

    // ───────────── Performance Rules ─────────────

    public class LoadBalancingRule : IConfigHealthRule
    {
        public string Id => "PER001";
        public string Category => "Performance";
        public Severity Level => Severity.Info;
        public string Title => "负载均衡策略建议优化";
        public string Description => "部分集群未使用 PowerOfTwoChoices";
        public string Recommendation => "建议使用 PowerOfTwoChoices 策略";
        public string ConfigPageUrl => "/apigateway/clusters";

        public Task<HealthRuleResult> EvaluateAsync(ConfigHealthContext ctx, CancellationToken ct = default)
        {
            var result = Build(Id, Category, Level, Title, Recommendation, ConfigPageUrl);
            var multiDestClusters = ctx.Clusters.Where(c => c.DestinationCount > 1).ToList();
            var notOptimal = multiDestClusters.Where(c => c.LoadBalancingPolicy != "PowerOfTwoChoices")
                .Select(c => c.ClusterId).ToList();
            result.Triggered = notOptimal.Count > 0;
            result.Detail = result.Triggered
                ? $"{notOptimal.Count} 个多后端集群未使用 PowerOfTwoChoices: {string.Join(", ", notOptimal.Take(5))}"
                : "所有多后端集群使用 PowerOfTwoChoices";
            return Task.FromResult(result);
        }
    }

    public class Http2Rule : IConfigHealthRule
    {
        public string Id => "PER002";
        public string Category => "Performance";
        public Severity Level => Severity.Warning;
        public string Title => "未启用 HTTP/2 多路复用";
        public string Description => "部分集群未启用 EnableMultipleHttp2Connections";
        public string Recommendation => "启用 EnableMultipleHttp2Connections 提升高并发性能";
        public string ConfigPageUrl => "/apigateway/clusters";

        public Task<HealthRuleResult> EvaluateAsync(ConfigHealthContext ctx, CancellationToken ct = default)
        {
            var result = Build(Id, Category, Level, Title, Recommendation, ConfigPageUrl);
            var noHttp2 = ctx.Clusters.Where(c => !c.EnableMultipleHttp2Connections).Select(c => c.ClusterId).ToList();
            result.Triggered = noHttp2.Count > 0;
            result.Detail = result.Triggered
                ? $"{noHttp2.Count} 个集群未启用 HTTP/2 多路复用: {string.Join(", ", noHttp2.Take(5))}"
                : "所有集群已启用 HTTP/2 多路复用";
            return Task.FromResult(result);
        }
    }

    // ───────────── Best Practice Rules ─────────────

    public class RouteOrderRule : IConfigHealthRule
    {
        public string Id => "BP001";
        public string Category => "BestPractice";
        public Severity Level => Severity.Info;
        public string Title => "路由未设置 Order";
        public string Description => "部分路由未配置 Order 值";
        public string Recommendation => "为每条路由设置 Order 避免匹配歧义";
        public string ConfigPageUrl => "/apigateway/routes";

        public Task<HealthRuleResult> EvaluateAsync(ConfigHealthContext ctx, CancellationToken ct = default)
        {
            var result = Build(Id, Category, Level, Title, Recommendation, ConfigPageUrl);
            var noOrder = ctx.Routes.Where(r => !r.Order.HasValue).Select(r => r.RouteId).ToList();
            result.Triggered = noOrder.Count > 0;
            result.Detail = result.Triggered
                ? $"{noOrder.Count} 个路由未设置 Order: {string.Join(", ", noOrder.Take(5))}"
                : "所有路由已设置 Order";
            return Task.FromResult(result);
        }
    }

    public class ConfigSnapshotRule : IConfigHealthRule
    {
        public string Id => "BP002";
        public string Category => "BestPractice";
        public Severity Level => Severity.Info;
        public string Title => "无配置快照";
        public string Description => "没有配置快照可用于回滚";
        public string Recommendation => "创建配置快照以便回滚";
        public string ConfigPageUrl => "/apigateway/history";

        public Task<HealthRuleResult> EvaluateAsync(ConfigHealthContext ctx, CancellationToken ct = default)
        {
            var result = Build(Id, Category, Level, Title, Recommendation, ConfigPageUrl);
            result.Triggered = ctx.SnapshotCount == 0;
            result.Detail = result.Triggered
                ? "没有任何配置快照，配置变更后无法回滚"
                : $"已有 {ctx.SnapshotCount} 个配置快照";
            return Task.FromResult(result);
        }
    }

    public class TransformPathPatternRule : IConfigHealthRule
    {
        public string Id => "BP003";
        public string Category => "BestPractice";
        public Severity Level => Severity.Warning;
        public string Title => "Transform 建议使用 PathPattern";
        public string Description => "部分路由使用 PathRemovePrefix 而非 PathPattern";
        public string Recommendation => "使用 PathPattern 替代 PathRemovePrefix+PathPrefix";
        public string ConfigPageUrl => "/apigateway/routes";

        public Task<HealthRuleResult> EvaluateAsync(ConfigHealthContext ctx, CancellationToken ct = default)
        {
            var result = Build(Id, Category, Level, Title, Recommendation, ConfigPageUrl);
            var usingOldTransform = ctx.Routes.Where(r => r.UsesPathRemovePrefix && !r.UsesPathPattern)
                .Select(r => r.RouteId).ToList();
            result.Triggered = usingOldTransform.Count > 0;
            result.Detail = result.Triggered
                ? $"{usingOldTransform.Count} 个路由使用 PathRemovePrefix，建议迁移到 PathPattern: {string.Join(", ", usingOldTransform.Take(5))}"
                : "所有路由使用 PathPattern 或无路径转换";
            return Task.FromResult(result);
        }
    }

    // ───────────── Helper ─────────────

    private static HealthRuleResult Build(string id, string category, Severity level,
        string title, string recommendation, string configPageUrl)
    {
        return new HealthRuleResult
        {
            RuleId = id,
            Category = category,
            Level = level,
            Title = title,
            Recommendation = recommendation,
            ConfigPageUrl = configPageUrl,
            IsApplicable = true,
            Triggered = false,
            Detail = ""
        };
    }
}
