# Aneiang.Yarp - Dashboard Mock Server
# Used by Verify-DashboardData.ps1 when no real backend is available.
param(
    [int]$Port = 5201,
    [int]$DelayMs = 50
)

$ErrorActionPreference = "Stop"
$BaseUrl = "http://localhost:$Port"

Write-Host "Mock Server starting on port $Port..." -ForegroundColor Cyan

$listener = New-Object System.Net.HttpListener
$listener.Prefixes.Add($BaseUrl + "/")
try {
    $listener.Start()
} catch {
    Write-Host "[ERROR] Could not start on port $Port - already in use?" -ForegroundColor Red
    exit 1
}
Write-Host "Mock Server listening: $BaseUrl" -ForegroundColor Green

$reqCount = 0
$running = $true

while ($running) {
    try {
        $ctx = $listener.GetContext()
        $path = $ctx.Request.Url.AbsolutePath
        $method = $ctx.Request.HttpMethod
        $reqCount++

        if ($DelayMs -gt 0) {
            Start-Sleep -Milliseconds $DelayMs
        }

        $body = switch -Regex ($path) {
            "^/api/mock/users"   { '{"id":1,"name":"Alice","role":"admin"}' }
            "^/api/mock/products" { '{"products":[{"id":101,"name":"Widget"},{"id":102,"name":"Gadget"}],"total":2}' }
            "^/api/mock/orders" { '{"orderId":"ORD-999","status":"shipped"}' }
            "^/api/mock/search" { '{"query":"test","results":10}' }
            "^/api/mock/health" { '{"status":"healthy"}' }
            default { '{"path":"' + $path + '","method":"' + $method + '"}' }
        }

        $statusCode = 200
        if ($path -match "/error|/fail") { $statusCode = 500 }

        $resp = $ctx.Response
        $resp.StatusCode = $statusCode
        $resp.Headers.Add("Content-Type", "application/json; charset=utf-8")
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($body)
        $resp.ContentLength64 = $bytes.Length
        $resp.OutputStream.Write($bytes, 0, $bytes.Length)
        $resp.Close()

        $col = if ($statusCode -eq 200) { "Green" } else { "Red" }
        $delayStr = [Math]::Round($DelayMs)
        Write-Host "[$reqCount] $method $path -> $statusCode (${delayStr}ms)" -ForegroundColor $col
    }
    catch [System.Net.HttpListenerException] {
        $running = $false
    }
    catch {
        Write-Host "[ERROR] $_" -ForegroundColor Red
    }
}

$listener.Stop()
$listener.Close()
Write-Host "Mock Server stopped." -ForegroundColor Yellow
