using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Yarp.ReverseProxy;

namespace Aneiang.Yarp.Dashboard.Controllers
{
    /// <summary>
    /// 网关维护仪表盘
    /// </summary>
    [Route("apigateway")]
    public class DashboardController : Controller
    {
        private readonly IProxyStateLookup _proxyState;
        private readonly IConfiguration _configuration;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IWebHostEnvironment _env;

        // 记录进程启动时间
        private static readonly DateTime _startTime = DateTime.Now;

        /// <summary>
        /// 创建仪表盘控制器
        /// </summary>
        public DashboardController(
            IProxyStateLookup proxyState,
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory,
            IWebHostEnvironment env)
        {
            _proxyState = proxyState;
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;
            _env = env;
        }

        /// <summary>
        /// 仪表盘首页
        /// </summary>
        [HttpGet("")]
        public IActionResult Index()
        {
            ViewBag.SeqServerUrl = _configuration["Dashboard:SeqServerUrl"] ?? "";
            ViewBag.SeqDefaultFilter = _configuration["Dashboard:SeqDefaultFilter"] ?? "";
            return View();
        }

        /// <summary>
        /// 获取网关基本信息
        /// </summary>
        [HttpGet("/apigateway/info")]
        public IActionResult GetInfo()
        {
            var process = Process.GetCurrentProcess();
            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "未知";
            var uptime = DateTime.Now - _startTime;
            var memoryMb = Math.Round(process.WorkingSet64 / 1024.0 / 1024.0, 1);

            return Json(new
            {
                code = 200,
                data = new
                {
                    version,
                    environment = _env.EnvironmentName,
                    startTime = _startTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    uptime = $"{(int)uptime.TotalHours}h {uptime.Minutes}m {uptime.Seconds}s",
                    memoryMb,
                    machineName = Environment.MachineName,
                    processId = process.Id
                }
            });
        }

        /// <summary>
        /// 获取 YARP 集群状态
        /// </summary>
        [HttpGet("/apigateway/clusters")]
        public IActionResult GetClusters()
        {
            var clusters = _proxyState.GetClusters().Select(cluster =>
            {
                var destinations = cluster.Destinations.Select(d => new
                {
                    name = d.Key,
                    address = d.Value.Model?.Config?.Address,
                    health = d.Value.Health.Active.ToString(),
                    passive = d.Value.Health.Passive.ToString()
                }).ToList();

                return new
                {
                    clusterId = cluster.ClusterId,
                    loadBalancingPolicy = cluster.Model?.Config?.LoadBalancingPolicy ?? "Default",
                    destinations,
                    healthyCount   = destinations.Count(d => d.health == "Healthy"),
                    unknownCount   = destinations.Count(d => d.health == "Unknown"),
                    unhealthyCount = destinations.Count(d => d.health == "Unhealthy"),
                    totalCount     = destinations.Count
                };
            }).ToList();

            return Json(new { code = 200, data = clusters });
        }

        /// <summary>
        /// 获取 YARP 路由配置
        /// </summary>
        [HttpGet("/apigateway/routes")]
        public IActionResult GetRoutes()
        {
            var routes = _proxyState.GetRoutes().Select(route => new
            {
                routeId = route.Config.RouteId,
                clusterId = route.Config.ClusterId,
                path = route.Config.Match.Path,
                methods = route.Config.Match.Methods,
                order = route.Config.Order
            }).OrderBy(r => r.order).ToList();

            return Json(new { code = 200, data = routes });
        }

        /// <summary>
        /// 获取 Seq 日志摘要
        /// </summary>
        [HttpGet("/apigateway/seq-summary")]
        public async Task<IActionResult> GetSeqSummary()
        {
            var seqUrl = _configuration["Dashboard:SeqServerUrl"];
            var seqApiKey = _configuration["Dashboard:SeqApiKey"];
            var defaultFilter = _configuration["Dashboard:SeqDefaultFilter"] ?? "";

            if (string.IsNullOrWhiteSpace(seqUrl))
            {
                return Json(new { code = 400, info = "Seq 服务地址未配置" });
            }

            try
            {
                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(10);
                if (!string.IsNullOrWhiteSpace(seqApiKey))
                {
                    client.DefaultRequestHeaders.Add("X-Seq-ApiKey", seqApiKey);
                }

                var baseSeq = seqUrl.TrimEnd('/');
                var errorFilter = Uri.EscapeDataString($"@Level = 'Error' and {defaultFilter}");

                // 拉 5 条最近错误用于展示详情
                var detailUrl = $"{baseSeq}/api/events?count=5&filter={errorFilter}";
                // 拉 100 条统计总数（近期真实错误量）
                var countUrl  = $"{baseSeq}/api/events?count=100&filter={errorFilter}";

                var detailTask = client.GetAsync(detailUrl);
                var countTask  = client.GetAsync(countUrl);
                await Task.WhenAll(detailTask, countTask);

                var errors = new List<object>();
                if (detailTask.Result.IsSuccessStatusCode)
                {
                    var json = await detailTask.Result.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    // Seq 返回裸数组或包含 Events 字段
                    var arr = root.ValueKind == JsonValueKind.Array
                        ? root
                        : (root.TryGetProperty("Events", out var ev) ? ev : root);
                    foreach (var item in arr.EnumerateArray())
                    {
                        // 兼容 CLEF 短名（@t/@mt/@m/@l）与旧版长名（Timestamp/RenderedMessage/Level）
                        var timestamp = GetStr(item, "@t", "Timestamp");
                        var message   = RenderSeqMessage(item);
                        var level     = GetStr(item, "@l", "Level");
                        errors.Add(new { timestamp, message, level });
                    }
                }

                var totalErrorCount = 0;
                if (countTask.Result.IsSuccessStatusCode)
                {
                    var json = await countTask.Result.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    var arr = root.ValueKind == JsonValueKind.Array
                        ? root
                        : (root.TryGetProperty("Events", out var ev) ? ev : root);
                    totalErrorCount = arr.GetArrayLength();
                }

                return Json(new
                {
                    code = 200,
                    data = new
                    {
                        recentErrors = errors,
                        errorCount   = totalErrorCount,
                        seqUrl,
                        defaultFilter
                    }
                });
            }
            catch (Exception ex)
            {
                return Json(new { code = 500, info = $"Seq 请求失败: {ex.Message}" });
            }
        }

        /// <summary>
        /// 诊断：返回 Seq 第一条事件的原始字段（排查 message 为空问题）
        /// </summary>
        [HttpGet("/apigateway/seq-raw")]
        public async Task<IActionResult> GetSeqRaw()
        {
            var seqUrl    = _configuration["Dashboard:SeqServerUrl"];
            var seqApiKey = _configuration["Dashboard:SeqApiKey"];
            var filter    = _configuration["Dashboard:SeqDefaultFilter"] ?? "";

            if (string.IsNullOrWhiteSpace(seqUrl))
                return Json(new { code = 400, info = "Seq 未配置" });

            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            if (!string.IsNullOrWhiteSpace(seqApiKey))
                client.DefaultRequestHeaders.Add("X-Seq-ApiKey", seqApiKey);

            var url = $"{seqUrl.TrimEnd('/')}/api/events?count=1&filter={Uri.EscapeDataString($"@Level = 'Error' and {filter}")}";
            var resp = await client.GetAsync(url);
            var raw  = await resp.Content.ReadAsStringAsync();

            // 解析后列出第一个事件的所有 key
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            var arr  = root.ValueKind == JsonValueKind.Array
                ? root
                : (root.TryGetProperty("Events", out var ev) ? ev : root);

            var keys = new List<object>();
            if (arr.GetArrayLength() > 0)
            {
                foreach (var prop in arr[0].EnumerateObject())
                    keys.Add(new { key = prop.Name, value = prop.Value.ToString()[..Math.Min(200, prop.Value.ToString().Length)] });
            }

            return Json(new { code = 200, url, statusCode = (int)resp.StatusCode, keys, rawFirst500 = raw[..Math.Min(500, raw.Length)] });
        }

        /// <summary>
        /// 按优先级依次尝试读取 JsonElement 中的字符串属性
        /// </summary>
        private static string GetStr(JsonElement el, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (el.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.String)
                    return prop.GetString() ?? "";
            }
            return "";
        }

        /// <summary>
        /// 将 Seq 旧版 REST API 返回的 MessageTemplateTokens + Properties 还原为可读消息
        /// </summary>
        private static string RenderSeqMessage(JsonElement item)
        {
            // 先尝试 CLEF 短名
            var clefMsg = GetStr(item, "@m", "@mt");
            if (!string.IsNullOrEmpty(clefMsg)) return clefMsg;

            // 读取 Properties 数组 → 字典
            var props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (item.TryGetProperty("Properties", out var propsArr) && propsArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var p in propsArr.EnumerateArray())
                {
                    var name = p.TryGetProperty("Name", out var n) ? n.GetString() ?? "" : "";
                    var val  = p.TryGetProperty("Value", out var v)
                        ? (v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ToString())
                        : "";
                    if (!string.IsNullOrEmpty(name)) props[name] = val;
                }
            }

            // 根据 MessageTemplateTokens 还原消息
            if (item.TryGetProperty("MessageTemplateTokens", out var tokens) && tokens.ValueKind == JsonValueKind.Array)
            {
                var sb = new System.Text.StringBuilder();
                foreach (var token in tokens.EnumerateArray())
                {
                    if (token.TryGetProperty("Text", out var text))
                    {
                        sb.Append(text.GetString());
                    }
                    else if (token.TryGetProperty("PropertyName", out var pName))
                    {
                        var pn = pName.GetString() ?? "";
                        sb.Append(props.TryGetValue(pn, out var pv) ? pv : $"{{{pn}}}");
                    }
                }
                var result = sb.ToString();
                if (!string.IsNullOrEmpty(result)) return result;
            }

            // 最后兜底：旧版 RenderedMessage / MessageTemplate
            return GetStr(item, "RenderedMessage", "MessageTemplate");
        }
    }
}
