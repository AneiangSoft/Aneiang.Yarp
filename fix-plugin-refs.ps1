$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

# Fix DashboardOptions -> GatewayMiddlewareOptions in middleware constructors
$middlewareFiles = @(
    'src/Aneiang.Yarp.Plugin.Waf/Middleware/WafMiddleware.cs',
    'src/Aneiang.Yarp.Plugin.Retry/Middleware/RequestRetryMiddleware.cs',
    'src/Aneiang.Yarp.Plugin.RateLimit/Middleware/RateLimitMiddleware.cs'
)

foreach ($file in $middlewareFiles) {
    $path = Join-Path $PSScriptRoot $file
    if (Test-Path $path) {
        $content = [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
        $content = $content.Replace('IOptions<DashboardOptions>', 'IOptions<GatewayMiddlewareOptions>')
        $content = $content.Replace('DashboardOptions', 'GatewayMiddlewareOptions')
        [System.IO.File]::WriteAllText($path, $content, $utf8NoBom)
        Write-Host "Fixed: $file"
    }
}

# Fix InMemoryCircuitStateStore references in Retry middleware
$retryFile = Join-Path $PSScriptRoot 'src/Aneiang.Yarp.Plugin.Retry/Middleware/RequestRetryMiddleware.cs'
if (Test-Path $retryFile) {
    $content = [System.IO.File]::ReadAllText($retryFile, [System.Text.Encoding]::UTF8)
    $content = $content.Replace('InMemoryCircuitStateStore.BuildCircuitKey', 'CircuitKeyHelper.BuildCircuitKey')
    $content = $content.Replace('InMemoryCircuitStateStore.ResolveDestinationUid', 'CircuitKeyHelper.ResolveDestinationUid')
    $content = $content.Replace('InMemoryCircuitStateStore.ParseCircuitKey', 'CircuitKeyHelper.ParseCircuitKey')
    $content = $content.Replace('InMemoryCircuitStateStore', 'CircuitKeyHelper')
    [System.IO.File]::WriteAllText($retryFile, $content, $utf8NoBom)
    Write-Host "Fixed Retry references"
}

# Remove duplicate using statements in all plugin files
$pluginDirs = @(
    'src/Aneiang.Yarp.Plugin.Waf',
    'src/Aneiang.Yarp.Plugin.Retry',
    'src/Aneiang.Yarp.Plugin.CircuitBreaker',
    'src/Aneiang.Yarp.Plugin.RateLimit',
    'src/Aneiang.Yarp.Plugin.ProxyLog'
)

foreach ($dir in $pluginDirs) {
    $fullDir = Join-Path $PSScriptRoot $dir
    if (Test-Path $fullDir) {
        Get-ChildItem -Path $fullDir -Recurse -Filter '*.cs' | ForEach-Object {
            $content = [System.IO.File]::ReadAllText($_.FullName, [System.Text.Encoding]::UTF8)
            $lines = $content -split "`r?`n"
            $seen = @{}
            $newLines = @()
            $changed = $false
            foreach ($line in $lines) {
                $trimmed = $line.Trim()
                if ($trimmed.StartsWith('using ') -and $trimmed.EndsWith(';') -and -not $trimmed.Contains('=')) {
                    if ($seen.ContainsKey($trimmed)) {
                        $changed = $true
                        continue
                    }
                    $seen[$trimmed] = $true
                }
                $newLines += $line
            }
            if ($changed) {
                $newContent = $newLines -join "`r`n"
                [System.IO.File]::WriteAllText($_.FullName, $newContent, $utf8NoBom)
                Write-Host "Removed duplicate usings: $($_.Name)"
            }
        }
    }
}

# Fix namespace declarations - remove .Middleware suffix where it shouldn't be
$namespaceFixes = @(
    @{File='src/Aneiang.Yarp.Plugin.Waf/Middleware/WafMiddleware.cs'; Old='namespace Aneiang.Yarp.Plugin.Waf.Middleware;'; New='namespace Aneiang.Yarp.Plugin.Waf;'},
    @{File='src/Aneiang.Yarp.Plugin.Retry/Middleware/RequestRetryMiddleware.cs'; Old='namespace Aneiang.Yarp.Plugin.Retry.Middleware;'; New='namespace Aneiang.Yarp.Plugin.Retry;'},
    @{File='src/Aneiang.Yarp.Plugin.RateLimit/Middleware/RateLimitMiddleware.cs'; Old='namespace Aneiang.Yarp.Plugin.RateLimit.Middleware;'; New='namespace Aneiang.Yarp.Plugin.RateLimit;'}
)

foreach ($fix in $namespaceFixes) {
    $path = Join-Path $PSScriptRoot $fix.File
    if (Test-Path $path) {
        $content = [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
        $content = $content.Replace($fix.Old, $fix.New)
        [System.IO.File]::WriteAllText($path, $content, $utf8NoBom)
        Write-Host "Fixed namespace: $($fix.File)"
    }
}

Write-Host "All fixes applied."
