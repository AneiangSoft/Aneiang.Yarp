$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$dashboardDir = Join-Path $PSScriptRoot 'src/Aneiang.Yarp.Dashboard'

# Map of types to their new using namespaces
$typeUsingMap = [ordered]@{
    'ICircuitStateStore' = 'Aneiang.Yarp.Infrastructure.State'
    'IRateLimiterStore' = 'Aneiang.Yarp.Infrastructure.State'
    'CircuitKeyHelper' = 'Aneiang.Yarp.Infrastructure.State'
    'IDestinationCandidateCoordinator' = 'Aneiang.Yarp.Infrastructure.Resilience'
    'GatewayMiddlewareBase' = 'Aneiang.Yarp.Infrastructure.Middleware'
    'GatewayMiddlewareOptions' = 'Aneiang.Yarp.Infrastructure.Middleware'
    'GatewayPluginExecutionPlanProvider' = 'Aneiang.Yarp.Services'
    'GatewayPluginExecutionPlan' = 'Aneiang.Yarp.Services'
    'IGatewayPluginManager' = 'Aneiang.Yarp.Plugins'
    'IPluginManifestCatalog' = 'Aneiang.Yarp.Plugins'
    'PluginStateChangeResult' = 'Aneiang.Yarp.Plugins'
    'PluginRuntimeState' = 'Aneiang.Yarp.Plugins'
    'ExternalPluginRegistrationStatus' = 'Aneiang.Yarp.Plugins'
    'IPluginRuntimeResource' = 'Aneiang.Yarp.Plugins'
    'IPluginResourceLifecycleCoordinator' = 'Aneiang.Yarp.Plugins'
    'PluginResourceHealthStatus' = 'Aneiang.Yarp.Plugins'
    'PluginRuntimeResourceSnapshot' = 'Aneiang.Yarp.Plugins'
    'WafBindingOptions' = 'Aneiang.Yarp.Models'
    'RequestRetryBindingOptions' = 'Aneiang.Yarp.Models'
    'CircuitBreakerConfig' = 'Aneiang.Yarp.Models'
    'RateLimitAlgorithm' = 'Aneiang.Yarp.Models'
    'CircuitState' = 'Aneiang.Yarp.Models'
    'CircuitStateInfo' = 'Aneiang.Yarp.Models'
    'CircuitStatus' = 'Aneiang.Yarp.Models'
}

# Also fix .cshtml views
$viewReplacements = [ordered]@{
    'Aneiang.Yarp.Dashboard.Modules.ProxyLog.Models' = 'Aneiang.Yarp.Plugin.ProxyLog.Models'
    'Aneiang.Yarp.Dashboard.Modules.ProxyLog.Services' = 'Aneiang.Yarp.Plugin.ProxyLog.Services'
    'Aneiang.Yarp.Dashboard.Modules.Waf' = 'Aneiang.Yarp.Plugin.Waf'
    'Aneiang.Yarp.Dashboard.Modules.Retry' = 'Aneiang.Yarp.Plugin.Retry'
    'Aneiang.Yarp.Dashboard.Modules.CircuitBreaker.Middleware' = 'Aneiang.Yarp.Plugin.CircuitBreaker'
    'Aneiang.Yarp.Dashboard.Modules.RateLimit.Middleware' = 'Aneiang.Yarp.Plugin.RateLimit'
}

# Process .cs files
Get-ChildItem -Path $dashboardDir -Recurse -Filter '*.cs' -ErrorAction SilentlyContinue | ForEach-Object {
    $content = [System.IO.File]::ReadAllText($_.FullName, [System.Text.Encoding]::UTF8)
    $changed = $false
    $usingsToAdd = [System.Collections.Generic.HashSet[string]]::new()

    foreach ($typeName in $typeUsingMap.Keys) {
        $namespace = $typeUsingMap[$typeName]
        # Check if the type is used (as a word boundary) and the using is not already present
        if ($content -match "\b$typeName\b" -and $content -notmatch "using $namespace;") {
            $usingsToAdd.Add($namespace) | Out-Null
        }
    }

    if ($usingsToAdd.Count -gt 0) {
        # Find the last using line and insert after it
        $lines = $content -split "`r?`n"
        $lastUsingIndex = -1
        for ($i = 0; $i -lt $lines.Count; $i++) {
            if ($lines[$i] -match '^using ') { $lastUsingIndex = $i }
        }

        if ($lastUsingIndex -ge 0) {
            $newUsings = $usingsToAdd | ForEach-Object { "using $_;" }
            $newLines = @()
            $newLines += $lines[0..$lastUsingIndex]
            $newLines += $newUsings
            if ($lastUsingIndex + 1 -lt $lines.Count) {
                $newLines += $lines[($lastUsingIndex + 1)..($lines.Count - 1)]
            }
            $content = $newLines -join "`r`n"
            $changed = $true
        }
    }

    if ($changed) {
        [System.IO.File]::WriteAllText($_.FullName, $content, $utf8NoBom)
        Write-Host "Added usings: $($_.Name)"
    }
}

# Process .cshtml files
Get-ChildItem -Path $dashboardDir -Recurse -Filter '*.cshtml' -ErrorAction SilentlyContinue | ForEach-Object {
    $content = [System.IO.File]::ReadAllText($_.FullName, [System.Text.Encoding]::UTF8)
    $changed = $false
    foreach ($key in $viewReplacements.Keys) {
        if ($content.Contains($key)) {
            $content = $content.Replace($key, $viewReplacements[$key])
            $changed = $true
        }
    }
    if ($changed) {
        [System.IO.File]::WriteAllText($_.FullName, $content, $utf8NoBom)
        Write-Host "Fixed view: $($_.Name)"
    }
}

Write-Host "Done."
