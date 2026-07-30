using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

var root = FindRepositoryRoot();
var resultsDirectory = Path.Combine(root, "tests", "Aneiang.Yarp.Performance", "results");
Directory.CreateDirectory(resultsDirectory);

var durationSeconds = ReadIntArgument(args, "--duration", 10);
var warmupSeconds = ReadIntArgument(args, "--warmup", 3);
var concurrency = ReadIntArgument(args, "--concurrency", 64);
var repetitions = ReadIntArgument(args, "--repetitions", 3);
var suite = ReadStringArgument(args, "--suite", "baseline");
var soakSeconds = ReadIntArgument(args, "--soak-duration", 30);
var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
var results = new List<BenchmarkResult>();
var processes = new List<Process>();

if (suite.Equals("first", StringComparison.OrdinalIgnoreCase))
{
    await RunFirstBatchAsync(root, resultsDirectory, timestamp, durationSeconds, warmupSeconds, soakSeconds);
    return;
}

try
{
    CleanupDatabases(root);
    var backend = StartService("Backend", Path.Combine(root, "tests", "Aneiang.Yarp.Performance", "Backend", "bin", "Release", "net8.0", "Backend.dll"), root);
    processes.Add(backend);
    await WaitForHealthyAsync("http://127.0.0.1:5300/health", backend);

    var gateways = new[]
    {
        new GatewayCase("Native YARP", 5301, "NativeYarp", Array.Empty<string>()),
        new GatewayCase("Same host native", 5302, "EnhancedYarp", ["--mode=host-native"]),
        new GatewayCase("Aneiang core", 5302, "EnhancedYarp", ["--mode=core"]),
        new GatewayCase("Aneiang storage", 5302, "EnhancedYarp", ["--mode=storage"]),
        new GatewayCase("Aneiang services", 5302, "EnhancedYarp", ["--mode=services"]),
        new GatewayCase("Aneiang minimal", 5302, "EnhancedYarp", ["--mode=minimal"]),
        new GatewayCase("Aneiang full", 5302, "EnhancedYarp", ["--mode=full"]),
        new GatewayCase("Aneiang WAF", 5302, "EnhancedYarp", ["--mode=waf"])
    };

    foreach (var gateway in gateways)
    {
        var dll = Path.Combine(root, "tests", "Aneiang.Yarp.Performance", gateway.Project, "bin", "Release", "net8.0", gateway.Project + ".dll");
        var process = StartService(gateway.Name, dll, root, gateway.Arguments);
        processes.Add(process);
        await WaitForHealthyAsync($"http://127.0.0.1:{gateway.Port}/health", process);

        var scenarios = gateway.Name == "Aneiang WAF"
            ? new[] { Scenario.Get("plain", "/api/perf/plain"), Scenario.Get("json-small", "/api/perf/json-small") }
            : new[]
            {
                Scenario.Get("plain", "/api/perf/plain"),
                Scenario.Get("json-small", "/api/perf/json-small"),
                Scenario.Post("post-1kb", "/api/perf/echo", new string('x', 1024))
            };

        foreach (var scenario in scenarios)
        {
            Console.WriteLine($"[{gateway.Name}] {scenario.Name}: warmup {warmupSeconds}s, test {durationSeconds}s x {repetitions}, concurrency {concurrency}");
            await RunLoadAsync(gateway.Port, scenario, concurrency, TimeSpan.FromSeconds(warmupSeconds), null);
            for (var repetition = 1; repetition <= repetitions; repetition++)
            {
                var result = await RunLoadAsync(gateway.Port, scenario, concurrency, TimeSpan.FromSeconds(durationSeconds), process);
                results.Add(result with { Gateway = gateway.Name, Scenario = scenario.Name, Repetition = repetition });
                Console.WriteLine($"  #{repetition}: {result.RequestsPerSecond:N0} RPS, P95 {result.P95Ms:N2} ms, P99 {result.P99Ms:N2} ms, errors {result.ErrorRatePercent:N3}%");
            }
        }

        StopProcess(process);
        processes.Remove(process);
        await Task.Delay(1000);
    }

    var summary = BuildSummary(results);
    var csvPath = Path.Combine(resultsDirectory, $"performance-{timestamp}.csv");
    var markdownPath = Path.Combine(resultsDirectory, $"performance-{timestamp}.md");
    await File.WriteAllTextAsync(csvPath, BuildCsv(results, summary), Encoding.UTF8);
    await File.WriteAllTextAsync(markdownPath, BuildMarkdown(summary, durationSeconds, warmupSeconds, concurrency, repetitions), Encoding.UTF8);

    Console.WriteLine();
    Console.WriteLine("Median results:");
    foreach (var item in summary)
        Console.WriteLine($"{item.Gateway,-18} {item.Scenario,-12} {item.RequestsPerSecond,10:N0} RPS  P95 {item.P95Ms,7:N2} ms  P99 {item.P99Ms,7:N2} ms  CPU {item.CpuPercent,6:N1}%  Memory {item.WorkingSetMb,7:N1} MB");
    Console.WriteLine($"CSV: {csvPath}");
    Console.WriteLine($"Report: {markdownPath}");
}
finally
{
    foreach (var process in processes.ToArray()) StopProcess(process);
    CleanupDatabases(root);
}

static async Task<BenchmarkResult> RunLoadAsync(int port, Scenario scenario, int concurrency, TimeSpan duration, Process? measuredProcess)
{
    using var handler = new SocketsHttpHandler
    {
        MaxConnectionsPerServer = concurrency,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        AutomaticDecompression = DecompressionMethods.None,
        UseProxy = false
    };
    using var client = new HttpClient(handler) { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(10) };
    client.DefaultRequestHeaders.ConnectionClose = false;

    var latencies = new ConcurrentBag<double>();
    long completed = 0;
    long failed = 0;
    var startedAt = Stopwatch.GetTimestamp();
    var cpuStart = measuredProcess?.TotalProcessorTime ?? TimeSpan.Zero;
    var stopAt = Stopwatch.GetTimestamp() + (long)(duration.TotalSeconds * Stopwatch.Frequency);

    var workers = Enumerable.Range(0, concurrency).Select(async _ =>
    {
        while (Stopwatch.GetTimestamp() < stopAt)
        {
            var requestStart = Stopwatch.GetTimestamp();
            try
            {
                using var request = new HttpRequestMessage(scenario.Method, scenario.Path);
                if (scenario.Body != null)
                {
                    request.Content = new StringContent(scenario.Body, Encoding.UTF8, "application/json");
                    request.Content.Headers.ContentLength = Encoding.UTF8.GetByteCount(scenario.Body);
                }
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                await response.Content.CopyToAsync(Stream.Null);
                if (!response.IsSuccessStatusCode) Interlocked.Increment(ref failed);
            }
            catch
            {
                Interlocked.Increment(ref failed);
            }
            finally
            {
                var elapsedMs = Stopwatch.GetElapsedTime(requestStart).TotalMilliseconds;
                latencies.Add(elapsedMs);
                Interlocked.Increment(ref completed);
            }
        }
    }).ToArray();

    await Task.WhenAll(workers);
    var elapsed = Stopwatch.GetElapsedTime(startedAt);
    measuredProcess?.Refresh();
    var cpuEnd = measuredProcess?.TotalProcessorTime ?? cpuStart;
    var cpuPercent = measuredProcess == null ? 0 : (cpuEnd - cpuStart).TotalMilliseconds / elapsed.TotalMilliseconds / Environment.ProcessorCount * 100;
    var memoryMb = measuredProcess?.WorkingSet64 / 1024d / 1024d ?? 0;
    var ordered = latencies.OrderBy(x => x).ToArray();

    return new BenchmarkResult(
        "", "", 0, completed / elapsed.TotalSeconds,
        Percentile(ordered, 0.50), Percentile(ordered, 0.95), Percentile(ordered, 0.99),
        completed == 0 ? 100 : failed * 100d / completed,
        cpuPercent, memoryMb, completed, failed);
}

static double Percentile(double[] values, double percentile)
{
    if (values.Length == 0) return 0;
    return values[(int)Math.Clamp(Math.Ceiling(percentile * values.Length) - 1, 0, values.Length - 1)];
}

static async Task RunFirstBatchAsync(string root, string resultsDirectory, string timestamp, int durationSeconds, int warmupSeconds, int soakSeconds)
{
    var records = new List<ExtendedResult>();
    var processes = new List<Process>();
    try
    {
        CleanupDatabases(root);
        var backendDll = Path.Combine(root, "tests", "Aneiang.Yarp.Performance", "Backend", "bin", "Release", "net8.0", "Backend.dll");
        var enhancedDll = Path.Combine(root, "tests", "Aneiang.Yarp.Performance", "EnhancedYarp", "bin", "Release", "net8.0", "EnhancedYarp.dll");
        var backend = StartService("Backend", backendDll, root);
        processes.Add(backend);
        await WaitForHealthyAsync("http://127.0.0.1:5300/health", backend);

        foreach (var concurrency in new[] { 1, 16, 64, 128 })
        {
            await RunExtendedCaseAsync("Aneiang full", "full", Scenario.Get($"concurrency-{concurrency}", "/api/perf/plain"), concurrency, durationSeconds, warmupSeconds, records, processes, enhancedDll, root);
        }
        foreach (var size in new[] { 1024, 64 * 1024, 1024 * 1024 })
        {
            await RunExtendedCaseAsync("Aneiang full", "full", Scenario.Get($"response-{size}", $"/api/perf/payload/{size}"), 32, durationSeconds, warmupSeconds, records, processes, enhancedDll, root);
            await RunExtendedCaseAsync("Aneiang full", "full", Scenario.Post($"request-{size}", "/api/perf/echo", new string('x', size)), 32, durationSeconds, warmupSeconds, records, processes, enhancedDll, root);
        }

        foreach (var item in new[]
        {
            (Name: "log-meta", Mode: "log-meta", Scenario: Scenario.Get("plain", "/api/perf/plain")),
            (Name: "log-request", Mode: "log-request", Scenario: Scenario.Post("post-64kb", "/api/perf/echo", new string('x', 64 * 1024))),
            (Name: "log-response", Mode: "log-response", Scenario: Scenario.Get("response-64kb", "/api/perf/payload/65536")),
            (Name: "log-sqlite", Mode: "log-sqlite", Scenario: Scenario.Post("post-64kb", "/api/perf/echo", new string('x', 64 * 1024))),
            (Name: "waf-normal", Mode: "waf", Scenario: Scenario.Get("normal", "/api/perf/plain"))
        })
        {
            await RunExtendedCaseAsync(item.Name, item.Mode, item.Scenario, 32, durationSeconds, warmupSeconds, records, processes, enhancedDll, root);
        }

        await RunFunctionalCaseAsync("waf-attack", "waf-attack", "/api/perf/plain?q=%27%20OR%201%3D1--", HttpStatusCode.Forbidden, 0, records, processes, enhancedDll, root);
        foreach (var mode in new[] { "rate-fixed", "rate-sliding", "rate-token", "rate-concurrency" })
        {
            await RunExtendedCaseAsync(mode, mode, Scenario.Get("plain", "/api/perf/plain"), 64, durationSeconds, warmupSeconds, records, processes, enhancedDll, root, allowNonSuccess: true);
        }
        await RunFunctionalCaseAsync("retry", "retry", "/api/perf/flaky/2", HttpStatusCode.OK, 3, records, processes, enhancedDll, root);
        await RunCircuitCaseAsync(records, processes, enhancedDll, root);
        await RunExtendedCaseAsync("soak", "full", Scenario.Get($"plain-{soakSeconds}s", "/api/perf/plain"), 64, soakSeconds, warmupSeconds, records, processes, enhancedDll, root);

        var csvPath = Path.Combine(resultsDirectory, $"performance-first-{timestamp}.csv");
        var markdownPath = Path.Combine(resultsDirectory, $"performance-first-{timestamp}.md");
        await File.WriteAllTextAsync(csvPath, BuildExtendedCsv(records), Encoding.UTF8);
        await File.WriteAllTextAsync(markdownPath, BuildExtendedMarkdown(records, durationSeconds, soakSeconds), Encoding.UTF8);
        Console.WriteLine($"CSV: {csvPath}");
        Console.WriteLine($"Report: {markdownPath}");
    }
    finally
    {
        foreach (var process in processes.ToArray()) StopProcess(process);
        CleanupDatabases(root);
    }
}

static async Task RunExtendedCaseAsync(string group, string mode, Scenario scenario, int concurrency, int durationSeconds, int warmupSeconds, List<ExtendedResult> records, List<Process> processes, string dll, string root, bool allowNonSuccess = false)
{
    var process = StartService(group, dll, root, [$"--mode={mode}"]);
    processes.Add(process);
    await WaitForHealthyAsync("http://127.0.0.1:5302/health", process);
    await RunLoadAsync(5302, scenario, concurrency, TimeSpan.FromSeconds(warmupSeconds), null);
    await ResetBackendAsync();
    var result = await RunLoadAsync(5302, scenario, concurrency, TimeSpan.FromSeconds(durationSeconds), process);
    var counters = await ReadBackendCountersAsync();
    var passed = allowNonSuccess || result.Failures == 0;
    records.Add(ExtendedResult.FromLoad(group, scenario.Name, concurrency, result, counters.Total, passed, allowNonSuccess ? "Expected policy rejections included" : "All responses successful"));
    Console.WriteLine($"[{group}] {scenario.Name}: {result.RequestsPerSecond:N0} RPS, P95 {result.P95Ms:N2} ms, errors {result.ErrorRatePercent:N2}%");
    StopProcess(process);
    processes.Remove(process);
    await Task.Delay(500);
}

static async Task RunFunctionalCaseAsync(string group, string mode, string path, HttpStatusCode expectedStatus, long expectedBackendRequests, List<ExtendedResult> records, List<Process> processes, string dll, string root)
{
    var process = StartService(group, dll, root, [$"--mode={mode}"]);
    processes.Add(process);
    await WaitForHealthyAsync("http://127.0.0.1:5302/health", process);
    await ResetBackendAsync();
    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    var started = Stopwatch.GetTimestamp();
    using var response = await client.GetAsync("http://127.0.0.1:5302" + path);
    await response.Content.CopyToAsync(Stream.Null);
    var latency = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    var counters = await ReadBackendCountersAsync();
    var passed = response.StatusCode == expectedStatus && counters.Total == expectedBackendRequests;
    records.Add(new ExtendedResult(group, path, 1, 1, latency, latency, latency, response.IsSuccessStatusCode ? 0 : 100, counters.Total, response.StatusCode.ToString(), passed, $"Expected status {(int)expectedStatus}, backend calls {expectedBackendRequests}"));
    Console.WriteLine($"[{group}] status {(int)response.StatusCode}, backend calls {counters.Total}: {(passed ? "PASS" : "FAIL")}");
    StopProcess(process);
    processes.Remove(process);
    await Task.Delay(500);
}

static async Task RunCircuitCaseAsync(List<ExtendedResult> records, List<Process> processes, string dll, string root)
{
    var process = StartService("circuit", dll, root, ["--mode=circuit"]);
    processes.Add(process);
    await WaitForHealthyAsync("http://127.0.0.1:5302/health", process);
    await ResetBackendAsync();
    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    var statuses = new List<int>();
    for (var i = 0; i < 6; i++)
    {
        using var response = await client.GetAsync("http://127.0.0.1:5302/api/perf/status/503");
        statuses.Add((int)response.StatusCode);
        await response.Content.CopyToAsync(Stream.Null);
    }
    var openCounters = await ReadBackendCountersAsync();
    await Task.Delay(2200);
    using var probe = await client.GetAsync("http://127.0.0.1:5302/api/perf/plain");
    await probe.Content.CopyToAsync(Stream.Null);
    var recoveredCounters = await ReadBackendCountersAsync();
    var passed = statuses.All(x => x == 503) && openCounters.Total == 3 && probe.IsSuccessStatusCode && recoveredCounters.Total == 4;
    records.Add(new ExtendedResult("circuit", "open-half-open-closed", 1, 7, 0, 0, 0, 0, recoveredCounters.Total, string.Join('/', statuses) + $"/{(int)probe.StatusCode}", passed, "3 backend failures, 3 open rejections, successful half-open probe"));
    Console.WriteLine($"[circuit] backend before recovery {openCounters.Total}, after {recoveredCounters.Total}: {(passed ? "PASS" : "FAIL")}");
    StopProcess(process);
    processes.Remove(process);
    await Task.Delay(500);
}

static async Task ResetBackendAsync()
{
    using var client = new HttpClient();
    using var response = await client.PostAsync("http://127.0.0.1:5300/api/perf/control/reset", null);
    response.EnsureSuccessStatusCode();
}

static async Task<BackendCounters> ReadBackendCountersAsync()
{
    using var client = new HttpClient();
    var json = await client.GetStringAsync("http://127.0.0.1:5300/api/perf/control/counters");
    return JsonSerializer.Deserialize<BackendCounters>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new BackendCounters();
}

static string BuildExtendedCsv(IEnumerable<ExtendedResult> records)
{
    var sb = new StringBuilder("Group,Scenario,Concurrency,RPS,P50Ms,P95Ms,P99Ms,NonSuccessPercent,BackendRequests,Statuses,Passed,Notes\n");
    foreach (var x in records)
        sb.AppendLine(string.Join(',', Csv(x.Group), Csv(x.Scenario), x.Concurrency, F(x.RequestsPerSecond), F(x.P50Ms), F(x.P95Ms), F(x.P99Ms), F(x.NonSuccessPercent), x.BackendRequests, Csv(x.Statuses), x.Passed, Csv(x.Notes)));
    return sb.ToString();
}

static string BuildExtendedMarkdown(IEnumerable<ExtendedResult> records, int durationSeconds, int soakSeconds)
{
    var rows = records.ToList();
    var sb = new StringBuilder("# Aneiang.Yarp First Batch Performance Results\n\n");
    sb.AppendLine($"- Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
    sb.AppendLine($"- Runtime: {Environment.Version}; processors: {Environment.ProcessorCount}");
    sb.AppendLine($"- Regular duration: {durationSeconds}s; soak duration: {soakSeconds}s").AppendLine();
    sb.AppendLine("| Group | Scenario | C | RPS | P95 ms | P99 ms | Non-success % | Backend calls | Check |");
    sb.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---|");
    foreach (var x in rows)
        sb.AppendLine($"| {x.Group} | {x.Scenario} | {x.Concurrency} | {x.RequestsPerSecond:N0} | {x.P95Ms:N2} | {x.P99Ms:N2} | {x.NonSuccessPercent:N2} | {x.BackendRequests} | {(x.Passed ? "PASS" : "FAIL")} |");
    sb.AppendLine().AppendLine("## Functional Assertions").AppendLine();
    foreach (var x in rows.Where(x => !string.IsNullOrEmpty(x.Notes))) sb.AppendLine($"- {x.Group}/{x.Scenario}: {(x.Passed ? "PASS" : "FAIL")} - {x.Notes}; observed `{x.Statuses}`.");
    sb.AppendLine().AppendLine("> Same-machine closed-loop benchmark. Policy rejection percentages are expected for rate-limit and WAF scenarios and are not transport failures.");
    return sb.ToString();
}

static Process StartService(string name, string dll, string workingDirectory, IReadOnlyList<string>? arguments = null)
{
    if (!File.Exists(dll)) throw new FileNotFoundException($"Build output for {name} was not found.", dll);
    var argumentText = $"\"{dll}\"" + (arguments is { Count: > 0 } ? " " + string.Join(' ', arguments) : "");
    var process = Process.Start(new ProcessStartInfo("dotnet", argumentText)
    {
        WorkingDirectory = workingDirectory,
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true,
        Environment = { ["DOTNET_ENVIRONMENT"] = "Production" }
    }) ?? throw new InvalidOperationException($"Unable to start {name}.");
    _ = DrainAsync(process.StandardOutput);
    _ = DrainAsync(process.StandardError);
    return process;
}

static async Task DrainAsync(StreamReader reader)
{
    while (await reader.ReadLineAsync() != null) { }
}

static async Task WaitForHealthyAsync(string url, Process process)
{
    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
    for (var i = 0; i < 60; i++)
    {
        if (process.HasExited) throw new InvalidOperationException($"Service exited during startup with code {process.ExitCode}.");
        try
        {
            using var response = await client.GetAsync(url);
            if (response.IsSuccessStatusCode) return;
        }
        catch { }
        await Task.Delay(250);
    }
    throw new TimeoutException($"Service did not become healthy: {url}");
}

static void StopProcess(Process process)
{
    try
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
        }
    }
    catch { }
    process.Dispose();
}

static List<BenchmarkResult> BuildSummary(List<BenchmarkResult> results) => results
    .GroupBy(x => new { x.Gateway, x.Scenario })
    .Select(group =>
    {
        var ordered = group.OrderBy(x => x.RequestsPerSecond).ToArray();
        return ordered[ordered.Length / 2] with { Repetition = 0 };
    })
    .OrderBy(x => x.Scenario).ThenBy(x => x.Gateway).ToList();

static string BuildCsv(List<BenchmarkResult> results, List<BenchmarkResult> summary)
{
    var sb = new StringBuilder("Kind,Gateway,Scenario,Repetition,RPS,P50Ms,P95Ms,P99Ms,ErrorRatePercent,CpuPercent,WorkingSetMb,Requests,Failures\n");
    foreach (var item in results.Select(x => (Kind: "run", Item: x)).Concat(summary.Select(x => (Kind: "median", Item: x))))
        sb.AppendLine(string.Join(',', item.Kind, Csv(item.Item.Gateway), Csv(item.Item.Scenario), item.Item.Repetition, F(item.Item.RequestsPerSecond), F(item.Item.P50Ms), F(item.Item.P95Ms), F(item.Item.P99Ms), F(item.Item.ErrorRatePercent), F(item.Item.CpuPercent), F(item.Item.WorkingSetMb), item.Item.Requests, item.Item.Failures));
    return sb.ToString();
}

static string BuildMarkdown(List<BenchmarkResult> summary, int duration, int warmup, int concurrency, int repetitions)
{
    var sb = new StringBuilder();
    sb.AppendLine("# Aneiang.Yarp Performance Results").AppendLine();
    sb.AppendLine($"- Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
    sb.AppendLine($"- OS: {Environment.OSVersion}");
    sb.AppendLine($"- Logical processors: {Environment.ProcessorCount}");
    sb.AppendLine($"- Runtime: {Environment.Version}");
    sb.AppendLine($"- Concurrency: {concurrency}");
    sb.AppendLine($"- Warmup: {warmup}s; duration: {duration}s; repetitions: {repetitions}; result: median RPS run").AppendLine();
    sb.AppendLine("| Gateway | Scenario | RPS | P50 ms | P95 ms | P99 ms | Errors % | CPU % | Memory MB |");
    sb.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|");
    foreach (var item in summary)
        sb.AppendLine($"| {item.Gateway} | {item.Scenario} | {item.RequestsPerSecond:N0} | {item.P50Ms:N2} | {item.P95Ms:N2} | {item.P99Ms:N2} | {item.ErrorRatePercent:N3} | {item.CpuPercent:N1} | {item.WorkingSetMb:N1} |");
    sb.AppendLine().AppendLine("## Relative throughput versus Native YARP").AppendLine();
    foreach (var scenario in summary.Select(x => x.Scenario).Distinct())
    {
        var baseline = summary.FirstOrDefault(x => x.Scenario == scenario && x.Gateway == "Native YARP");
        if (baseline == null) continue;
        foreach (var item in summary.Where(x => x.Scenario == scenario && x.Gateway != "Native YARP"))
            sb.AppendLine($"- {item.Gateway} / {scenario}: {(item.RequestsPerSecond / baseline.RequestsPerSecond - 1) * 100:N1}% RPS, {(item.P99Ms / baseline.P99Ms - 1) * 100:N1}% P99 latency.");
    }
    sb.AppendLine().AppendLine("> Same-machine closed-loop benchmark. Use the relative differences to assess middleware overhead; do not treat these numbers as production capacity planning without cross-machine load generation.");
    return sb.ToString();
}

static int ReadIntArgument(string[] args, string name, int fallback)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length && int.TryParse(args[index + 1], out var value) ? value : fallback;
}

static string ReadStringArgument(string[] args, string name, string fallback)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
}

static string FindRepositoryRoot()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Aneiang.Yarp.sln"))) directory = directory.Parent;
    return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
}

static void CleanupDatabases(string root)
{
    foreach (var path in Directory.EnumerateFiles(root, "performance-*.db*", SearchOption.TopDirectoryOnly))
    {
        try { File.Delete(path); } catch { }
    }
}

static string Csv(string value) => '"' + value.Replace("\"", "\"\"") + '"';
static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

sealed record GatewayCase(string Name, int Port, string Project, IReadOnlyList<string> Arguments);
sealed record Scenario(string Name, HttpMethod Method, string Path, string? Body)
{
    public static Scenario Get(string name, string path) => new(name, HttpMethod.Get, path, null);
    public static Scenario Post(string name, string path, string body) => new(name, HttpMethod.Post, path, body);
}
sealed record BenchmarkResult(string Gateway, string Scenario, int Repetition, double RequestsPerSecond, double P50Ms, double P95Ms, double P99Ms, double ErrorRatePercent, double CpuPercent, double WorkingSetMb, long Requests, long Failures);
sealed record ExtendedResult(string Group, string Scenario, int Concurrency, double RequestsPerSecond, double P50Ms, double P95Ms, double P99Ms, double NonSuccessPercent, long BackendRequests, string Statuses, bool Passed, string Notes)
{
    public static ExtendedResult FromLoad(string group, string scenario, int concurrency, BenchmarkResult result, long backendRequests, bool passed, string notes) =>
        new(group, scenario, concurrency, result.RequestsPerSecond, result.P50Ms, result.P95Ms, result.P99Ms, result.ErrorRatePercent, backendRequests, "load", passed, notes);
}
sealed class BackendCounters
{
    public long Total { get; set; }
    public long FlakyAttempts { get; set; }
}
