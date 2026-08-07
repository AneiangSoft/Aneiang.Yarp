param([string]$Dir, [string]$ProjectNamespace)

$replacements = @{
    'Aneiang.Yarp.Dashboard.Infrastructure.Middleware' = 'Aneiang.Yarp.Infrastructure.Middleware'
    'Aneiang.Yarp.Dashboard.Infrastructure.Plugin' = 'Aneiang.Yarp.Plugins'
    'Aneiang.Yarp.Dashboard.Infrastructure.State' = 'Aneiang.Yarp.Infrastructure.State'
    'Aneiang.Yarp.Dashboard.Infrastructure.Resilience' = 'Aneiang.Yarp.Infrastructure.Resilience'
    'Aneiang.Yarp.Dashboard.Infrastructure.Yarp' = $ProjectNamespace
    'using Aneiang.Yarp.Dashboard.Infrastructure;' = 'using Aneiang.Yarp.Infrastructure.Middleware;'
}

# Add project-specific namespace replacement
$oldModuleNs = "Aneiang.Yarp.Dashboard.Modules.$($ProjectNamespace.Split('.')[-1])"
if ($oldModuleNs -ne $ProjectNamespace) {
    $replacements[$oldModuleNs] = $ProjectNamespace
}

# Also handle CircuitBreaker.Middleware and RateLimit.Middleware sub-namespaces
$replacements['Aneiang.Yarp.Dashboard.Modules.CircuitBreaker.Middleware'] = $ProjectNamespace
$replacements['Aneiang.Yarp.Dashboard.Modules.RateLimit.Middleware'] = $ProjectNamespace

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

Get-ChildItem -Path $Dir -Recurse -Filter '*.cs' | ForEach-Object {
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

Write-Host "Done processing $Dir"
