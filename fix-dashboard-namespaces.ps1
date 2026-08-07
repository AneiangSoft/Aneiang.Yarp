$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

$dashboardDir = Join-Path $PSScriptRoot 'src/Aneiang.Yarp.Dashboard'

$replacements = [ordered]@{
    'Aneiang.Yarp.Dashboard.Modules.ProxyLog.Models' = 'Aneiang.Yarp.Plugin.ProxyLog.Models'
    'Aneiang.Yarp.Dashboard.Modules.ProxyLog.Services' = 'Aneiang.Yarp.Plugin.ProxyLog.Services'
    'Aneiang.Yarp.Dashboard.Modules.Waf' = 'Aneiang.Yarp.Plugin.Waf'
    'Aneiang.Yarp.Dashboard.Modules.Retry' = 'Aneiang.Yarp.Plugin.Retry'
    'Aneiang.Yarp.Dashboard.Modules.CircuitBreaker.Middleware' = 'Aneiang.Yarp.Plugin.CircuitBreaker'
    'Aneiang.Yarp.Dashboard.Modules.RateLimit.Middleware' = 'Aneiang.Yarp.Plugin.RateLimit'
    'Aneiang.Yarp.Dashboard.Infrastructure.Plugin.GatewayPluginExecutionPlanProvider' = 'Aneiang.Yarp.Services.GatewayPluginExecutionPlanProvider'
    'Aneiang.Yarp.Dashboard.Infrastructure.Plugin.PluginRuntimeResources' = 'Aneiang.Yarp.Plugins'
    'Aneiang.Yarp.Dashboard.Infrastructure.Plugin.PluginResourceHealthStatus' = 'Aneiang.Yarp.Plugins.PluginResourceHealthStatus'
    'Aneiang.Yarp.Dashboard.Infrastructure.Plugin.PluginRuntimeResourceSnapshot' = 'Aneiang.Yarp.Plugins.PluginRuntimeResourceSnapshot'
    'Aneiang.Yarp.Dashboard.Infrastructure.Plugin.IPluginRuntimeResource' = 'Aneiang.Yarp.Plugins.IPluginRuntimeResource'
    'Aneiang.Yarp.Dashboard.Infrastructure.Plugin.IPluginResourceLifecycleCoordinator' = 'Aneiang.Yarp.Plugins.IPluginResourceLifecycleCoordinator'
    'Aneiang.Yarp.Dashboard.Infrastructure.Middleware.GatewayMiddlewareBase' = 'Aneiang.Yarp.Infrastructure.Middleware.GatewayMiddlewareBase'
    'Aneiang.Yarp.Dashboard.Infrastructure.State.ICircuitStateStore' = 'Aneiang.Yarp.Infrastructure.State.ICircuitStateStore'
    'Aneiang.Yarp.Dashboard.Infrastructure.State.IRateLimiterStore' = 'Aneiang.Yarp.Infrastructure.State.IRateLimiterStore'
    'Aneiang.Yarp.Dashboard.Infrastructure.State.RateLimiterEntry' = 'Aneiang.Yarp.Infrastructure.State.RateLimiterEntry'
    'Aneiang.Yarp.Dashboard.Infrastructure.Resilience.IDestinationCandidateCoordinator' = 'Aneiang.Yarp.Infrastructure.Resilience.IDestinationCandidateCoordinator'
    'Aneiang.Yarp.Dashboard.Infrastructure.Performance.LockFreeStatistics' = 'Aneiang.Yarp.Infrastructure.Performance.LockFreeStatistics'
    'Aneiang.Yarp.Dashboard.Infrastructure.Performance.StatisticsSnapshot' = 'Aneiang.Yarp.Infrastructure.Performance.StatisticsSnapshot'
}

Get-ChildItem -Path $dashboardDir -Recurse -Filter '*.cs' | ForEach-Object {
    $content = [System.IO.File]::ReadAllText($_.FullName, [System.Text.Encoding]::UTF8)
    $changed = $false
    foreach ($key in $replacements.Keys) {
        if ($content.Contains($key)) {
            $content = $content.Replace($key, $replacements[$key])
            $changed = $true
        }
    }
    if ($changed) {
        [System.IO.File]::WriteAllText($_.FullName, $content, $utf8NoBom)
        Write-Host "Updated: $($_.Name)"
    }
}

Write-Host "Dashboard namespace update complete."
