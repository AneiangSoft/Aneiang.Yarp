using System.Diagnostics;
using System.Reflection;
using Aneiang.Yarp.Dashboard.Modules.Dashboard.Models;
using Microsoft.AspNetCore.Hosting;

namespace Aneiang.Yarp.Dashboard.Modules.Dashboard.Services;

/// <summary>
/// Implementation of dashboard info query service.
/// </summary>
internal sealed class DashboardInfoQueryService : IDashboardInfoQueryService
{
    private readonly IWebHostEnvironment _env;
    private static readonly DateTime _startTime = DateTime.Now;
    private static readonly string _fileVersion;

    static DashboardInfoQueryService()
    {
        var location = Assembly.GetExecutingAssembly().Location;
        _fileVersion = !string.IsNullOrEmpty(location)
            ? FileVersionInfo.GetVersionInfo(location).ProductVersion
              ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown"
            : Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown";
    }

    /// <summary>
    /// Initializes a new instance of DashboardInfoQueryService.
    /// </summary>
    /// <param name="env">Web host environment.</param>
    public DashboardInfoQueryService(IWebHostEnvironment env)
    {
        _env = env;
    }

    /// <inheritdoc />
    public DashboardInfoResponse GetInfo()
    {
        var process = Process.GetCurrentProcess();
        process.Refresh();
        var uptime = DateTime.Now - _startTime;
        var memoryMb = Math.Round(process.WorkingSet64 / 1024.0 / 1024.0, 1);

        // CPU usage: total processor time / uptime * 100, capped at 100
        var totalCpuMs = process.TotalProcessorTime.TotalMilliseconds;
        var cpuPct = uptime.TotalMilliseconds > 0
            ? Math.Round(Math.Min(100, (totalCpuMs / uptime.TotalMilliseconds) * 100 / Environment.ProcessorCount), 1)
            : 0;

        return new DashboardInfoResponse
        {
            Version = _fileVersion,
            Environment = _env.EnvironmentName,
            StartTime = _startTime.ToString("yyyy-MM-dd HH:mm:ss"),
            Uptime = $"{(int)uptime.TotalHours}h {uptime.Minutes}m {uptime.Seconds}s",
            MemoryMb = memoryMb,
            MachineName = Environment.MachineName,
            ProcessId = process.Id,
            CpuUsage = cpuPct,
            TotalMemory = process.WorkingSet64 + GC.GetTotalMemory(false),
            MemoryWorkingSet = process.WorkingSet64,
            GcCount = GC.CollectionCount(0) + GC.CollectionCount(1) + GC.CollectionCount(2),
            ThreadCount = process.Threads.Count
        };
    }
}
