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
        var uptime = DateTime.Now - _startTime;
        var memoryMb = Math.Round(process.WorkingSet64 / 1024.0 / 1024.0, 1);

        return new DashboardInfoResponse
        {
            Version = _fileVersion,
            Environment = _env.EnvironmentName,
            StartTime = _startTime.ToString("yyyy-MM-dd HH:mm:ss"),
            Uptime = $"{(int)uptime.TotalHours}h {uptime.Minutes}m {uptime.Seconds}s",
            MemoryMb = memoryMb,
            MachineName = Environment.MachineName,
            ProcessId = process.Id
        };
    }
}
