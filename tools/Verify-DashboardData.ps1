param(
    [int]$Requests = 30,
    [string]$GatewayUrl = "http://localhost:5200",
    [switch]$SkipLogin
)

$ErrorActionPreference = "Stop"
$DashBase = "$GatewayUrl/aneiang"
$LoginUrl = "$DashBase/login"
$MockBase = "http://localhost:5201"

function Test-Url($url) {
    $req = [Net.HttpWebRequest]::Create($url)
    $req.Timeout = 5000
    $req.Method = "GET"
    try {
        $resp = $req.GetResponse()
        $resp.Close()
        return $true
    } catch {
        $e = $_.Exception
        while ($e) {
            if ($e.Response) {
                try { $e.Response.GetResponseStream().Close() } catch { }
                return $true
            }
            $e = $e.InnerException
        }
        return $false
    }
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Aneiang.Yarp Dashboard Verification" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

Write-Host "[1/8] Checking gateway..." -ForegroundColor Yellow
if (-not (Test-Url $GatewayUrl)) {
    if (-not (Test-Url "$DashBase/api/info")) {
        Write-Host "[ERROR] Cannot reach gateway at $GatewayUrl" -ForegroundColor Red
        Write-Host "Run: dotnet run --project samples/SampleGateway --environment DashboardTest" -ForegroundColor Yellow
        exit 1
    }
}
Write-Host "[OK] Gateway is running: $GatewayUrl" -ForegroundColor Green

Write-Host ""
Write-Host "[2/8] Checking mock backend..." -ForegroundColor Yellow
$mockRunning = Test-Url $MockBase
if (-not $mockRunning) {
    $mockPath = Join-Path $PSScriptRoot "Mock-Server.ps1"
    if (Test-Path $mockPath) {
        Write-Host "  Starting mock server on port 5201..." -ForegroundColor Yellow
        $scriptContent = Get-Content $mockPath -Raw -Encoding UTF8
        $tempFile = [System.IO.Path]::GetTempFileName() + ".ps1"
        Set-Content -Path $tempFile -Value $scriptContent -Encoding UTF8
        $mockJob = Start-Job -ScriptBlock {
            param($path, $port)
            & $path -Port $port -DelayMs 30
        } -ArgumentList $tempFile, 5201
        Start-Sleep -Seconds 3
        if (Test-Url $MockBase) {
            Write-Host "[OK] Mock backend started: $MockBase" -ForegroundColor Green
        } else {
            Write-Host "[WARN] Mock backend did not respond in time" -ForegroundColor Yellow
        }
    } else {
        Write-Host "[WARN] Mock-Server.ps1 not found" -ForegroundColor Yellow
    }
} else {
    Write-Host "[OK] Mock backend already running: $MockBase" -ForegroundColor Green
}

Write-Host ""
Write-Host "[3/8] Logging in..." -ForegroundColor Yellow
$token = $null
$cookies = New-Object System.Net.CookieContainer
if (-not $SkipLogin) {
    try {
        $body = @{ username = "admin"; password = "demo123" } | ConvertTo-Json
        $loginReq = [Net.HttpWebRequest]::Create($LoginUrl)
        $loginReq.Method = "POST"
        $loginReq.ContentType = "application/json"
        $loginReq.CookieContainer = $cookies
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($body)
        $loginReq.ContentLength = $bytes.Length
        $sw = $loginReq.GetRequestStream()
        $sw.Write($bytes, 0, $bytes.Length)
        $sw.Close()
        $resp = $loginReq.GetResponse()
        $sr = New-Object System.IO.StreamReader($resp.GetResponseStream())
        $json = $sr.ReadToEnd()
        $sr.Close()
        $resp.Close()
        $result = $json | ConvertFrom-Json
        if ($result.code -eq 200) {
            $token = $result.token
            Write-Host "[OK] Login successful" -ForegroundColor Green
        } else {
            Write-Host "[ERROR] Login failed: $($result.message)" -ForegroundColor Red
            exit 1
        }
    } catch {
        Write-Host "[ERROR] Login failed: $_" -ForegroundColor Red
        exit 1
    }
}

function Invoke-Api($endpoint, $method = "GET", $bodyContent = $null) {
    $url = "$DashBase/api/$endpoint"
    try {
        $req = [Net.HttpWebRequest]::Create($url)
        $req.Method = $method
        $req.ContentType = "application/json"
        $req.CookieContainer = $cookies
        $req.Headers.Add("Authorization", "Bearer $token")
        if ($bodyContent) {
            $bytes = [System.Text.Encoding]::UTF8.GetBytes($bodyContent)
            $req.ContentLength = $bytes.Length
            $sw = $req.GetRequestStream()
            $sw.Write($bytes, 0, $bytes.Length)
            $sw.Close()
        }
        $resp = $req.GetResponse()
        $sr = New-Object System.IO.StreamReader($resp.GetResponseStream())
        $result = $sr.ReadToEnd()
        $sr.Close()
        $resp.Close()
        return $result | ConvertFrom-Json
    } catch {
        return $null
    }
}

Write-Host ""
Write-Host "[4/8] Collecting gateway routes..." -ForegroundColor Yellow
$routesData = Invoke-Api "routes"
if ($routesData -and $routesData.code -eq 200) {
    $allPaths = @()
    foreach ($r in $routesData.data) {
        $p = $r.match.path
        if ($p -notmatch "{\*\*") { $p = $p + "/{**catchAll}" }
        $allPaths += @{ Path = $p; Method = "GET"; Desc = "$($r.routeId): $p" }
    }
    $routes = $routesData.data
    $routeCount = $routes.Count
    Write-Host "[OK] Found $routeCount routes" -ForegroundColor Green
} else {
    Write-Host "[WARN] Could not fetch routes, using fallback paths" -ForegroundColor Yellow
    $allPaths = @(
        @{ Path = "/test/{**catchAll}"; Method = "GET"; Desc = "MockTestRoute" },
        @{ Path = "/health"; Method = "GET"; Desc = "MockHealthRoute" }
    )
    $routeCount = 0
}

Write-Host ""
Write-Host "[5/8] Sending $Requests test requests (Logs + Stats)..." -ForegroundColor Yellow
if ($allPaths.Count -eq 0) {
    $allPaths = @(@{ Path = "/api/{**catchAll}"; Method = "GET"; Desc = "catch-all" })
}
$success = 0
$errors = 0
for ($i = 1; $i -le $Requests; $i++) {
    $t = $allPaths[$i % $allPaths.Count]
    $url = "$GatewayUrl$($t.Path)"
    $url = $url.Replace("{**catchAll}", "test")
    try {
        $req = [Net.HttpWebRequest]::Create($url)
        $req.Method = $t.Method
        $req.CookieContainer = $cookies
        $req.Headers.Add("Authorization", "Bearer $token")
        $resp = $req.GetResponse()
        $sc = $resp.StatusCode
        $resp.Close()
        $success++
        Write-Host "." -NoNewline -ForegroundColor Green
    } catch {
        $e = $_.Exception
        if ($e.InnerException.Response) {
            try { $e.InnerException.Response.GetResponseStream().Close() } catch { }
        }
        $errors++
        Write-Host "." -NoNewline -ForegroundColor Red
    }
    if ($i % 10 -eq 0) { Write-Host " $i/$Requests" -NoNewline -ForegroundColor Gray }
    Start-Sleep -Milliseconds 50
}
Write-Host " Done ($success ok, $errors fail)" -ForegroundColor Gray

Write-Host ""
Write-Host "[6/8] Sending attack requests (Security Events)..." -ForegroundColor Yellow
$attackTests = @(
    @{ Path = "/test/users?q=SELECT%20*%20FROM%20users"; Desc = "SQL Injection" },
    @{ Path = "/test/users?q=%3Cscript%3Ealert(1)%3C/script%3E"; Desc = "XSS" },
    @{ Path = "/test/..%2F..%2Fetc%2Fpasswd"; Desc = "Path Traversal" },
    @{ Path = "/test/users?q=%27%20OR%20%271%27%3D%271"; Desc = "SQL String Escape" }
)
foreach ($a in $attackTests) {
    $url = "$GatewayUrl$($a.Path)"
    try {
        $req = [Net.HttpWebRequest]::Create($url)
        $req.CookieContainer = $cookies
        $req.Headers.Add("Authorization", "Bearer $token")
        $resp = $req.GetResponse()
        $sc = $resp.StatusCode
        $resp.Close()
        $col = if ($sc -eq 403) { "Green" } else { "Yellow" }
        Write-Host "  $($a.Desc): $sc" -ForegroundColor $col
    } catch {
        $e = $_.Exception
        $sc = 0
        if ($e.InnerException.Response) {
            $sc = $e.InnerException.Response.StatusCode
            try { $e.InnerException.Response.GetResponseStream().Close() } catch { }
        }
        $col = if ($sc -eq 403) { "Green" } else { "Yellow" }
        Write-Host "  $($a.Desc): $sc (WAF)" -ForegroundColor $col
    }
}
Start-Sleep -Seconds 1

Write-Host ""
Write-Host "[7/8] Firing test alerts..." -ForegroundColor Yellow
$alertTests = @(
    @{ Ep = "test"; Body = @{ alertType = "TestAlert"; title = "Test Alert"; message = "This is a test."; severity = "Info" }; Desc = "Generic Test Alert" },
    @{ Ep = "test/circuit-breaker"; Body = @{ clusterId = "TestCluster"; destinationId = "d1" }; Desc = "Circuit Breaker Alert" },
    @{ Ep = "test/waf-block"; Body = @{ clientIp = "192.168.1.99"; reason = "SqlInjectionBlocked"; uri = "/test/users?q=SELECT" }; Desc = "WAF Block Alert" },
    @{ Ep = "test/retry-exhausted"; Body = @{ routeId = "TestRoute"; clusterId = "TestCluster"; attempts = 3; statusCode = 502 }; Desc = "Retry Exhausted Alert" }
)
foreach ($a in $alertTests) {
    $bodyJson = $a.Body | ConvertTo-Json -Compress
    $result = Invoke-Api "alerts/$($a.Ep)" -method "POST" -bodyContent $bodyJson
    if ($result -and $result.code -eq 200) {
        Write-Host "  $($a.Desc): OK" -ForegroundColor Green
    } else {
        Write-Host "  $($a.Desc): FAIL" -ForegroundColor Yellow
    }
}

Write-Host ""
Write-Host "[8/8] Collecting dashboard data..." -ForegroundColor Yellow
Start-Sleep -Seconds 2

$routes = Invoke-Api "routes"
$clusters = Invoke-Api "clusters"
$stats = Invoke-Api "stats"
$logs = Invoke-Api "logs?limit=10"
$secEvents = Invoke-Api "security-events?count=20"
$secSummary = Invoke-Api "security-events/summary"
$alerts = Invoke-Api "alerts?count=20"
$alertSummary = Invoke-Api "alerts/summary"
$cbStatus = Invoke-Api "circuit-breaker/status"

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Dashboard Data Report" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

Write-Host ""
Write-Host "  [Logs / Stats]" -ForegroundColor White
Write-Host "    Requests:    $success ok, $errors fail"
Write-Host "    Routes:      $($routes.data.Count) items" -ForegroundColor $(if ($routes.data.Count -gt 0) { "Green" } else { "Yellow" })
Write-Host "    Clusters:    $($clusters.data.Count) items" -ForegroundColor $(if ($clusters.data.Count -gt 0) { "Green" } else { "Yellow" })
Write-Host "    TotalReqs:   $($stats.data.totalRequests)" -ForegroundColor $(if ($stats.data.totalRequests -gt 0) { "Green" } else { "Yellow" })
Write-Host "    AvgLatency:  $($stats.data.averageLatencyMs) ms" -ForegroundColor Cyan
Write-Host "    Logs:        $($logs.data.Count) entries" -ForegroundColor $(if ($logs.data.Count -gt 0) { "Green" } else { "Yellow" })

Write-Host ""
Write-Host "  [Security Events]" -ForegroundColor White
Write-Host "    Total:       $($secEvents.data.total)" -ForegroundColor $(if ($secEvents.data.total -gt 0) { "Green" } else { "Yellow" })
if ($secSummary.data.typeCounts) {
    Write-Host "    Type distribution:"
    foreach ($prop in $secSummary.data.typeCounts.PSObject.Properties) {
        Write-Host "      - $($prop.Name): $($prop.Value)" -ForegroundColor DarkGray
    }
}
if ($secSummary.data.topIps) {
    Write-Host "    Top IPs:"
    foreach ($prop in ($secSummary.data.topIps.PSObject.Properties | Select-Object -First 3)) {
        Write-Host "      - $($prop.Name): $($prop.Value) times" -ForegroundColor DarkGray
    }
} else {
    Write-Host "    Top IPs: (none)" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "  [Alerts]" -ForegroundColor White
Write-Host "    Total:       $($alerts.data.total)" -ForegroundColor $(if ($alerts.data.total -gt 0) { "Green" } else { "Yellow" })
if ($alertSummary.data.severityCounts) {
    Write-Host "    By Severity:"
    foreach ($prop in $alertSummary.data.severityCounts.PSObject.Properties) {
        $color = if ($prop.Name -eq "Error") { "Red" } elseif ($prop.Name -eq "Warning") { "Yellow" } else { "Green" }
        Write-Host "      - $($prop.Name): $($prop.Value)" -ForegroundColor $color
    }
}
if ($alertSummary.data.typeCounts) {
    Write-Host "    By Type:"
    foreach ($prop in $alertSummary.data.typeCounts.PSObject.Properties) {
        Write-Host "      - $($prop.Name): $($prop.Value)" -ForegroundColor DarkGray
    }
}

Write-Host ""
Write-Host "  [Circuit Breaker]" -ForegroundColor White
$cbCount = $cbStatus.data.PSObject.Properties.Count
Write-Host "    Circuits:    $cbCount" -ForegroundColor $(if ($cbCount -gt 0) { "Green" } else { "Yellow" })
if ($cbCount -gt 0) {
    foreach ($prop in ($cbStatus.data.PSObject.Properties | Select-Object -First 5)) {
        $s = $prop.Value.Status
        $c = if ($s -eq "Open") { "Red" } elseif ($s -eq "HalfOpen") { "Yellow" } else { "Green" }
        Write-Host "      - $($prop.Name): $s" -ForegroundColor $c
    }
} else {
    Write-Host "    (No active circuits - normal, only triggers on 5xx)" -ForegroundColor Gray
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
$hasLogs = $stats.data.totalRequests -gt 0
$hasSecEvents = $secEvents.data.total -gt 0
$hasAlerts = $alerts.data.total -gt 0
$hasCb = $cbCount -gt 0

if ($hasLogs -and $hasSecEvents -and $hasAlerts) {
    Write-Host "  Status: ALL DATA GENERATED!" -ForegroundColor Green
} elseif ($hasLogs -and -not $hasSecEvents -and -not $hasAlerts) {
    Write-Host "  Status: Logs OK, Security Events / Alerts are empty." -ForegroundColor Yellow
    Write-Host "  Reason: WAF may not be enabled (check Gateway.Dashboard.Waf.Enabled=true)" -ForegroundColor Yellow
} elseif (-not $hasLogs) {
    Write-Host "  Status: No traffic data. Gateway routes may differ from test config." -ForegroundColor Red
    Write-Host "  Gateway routes found: $routeCount (check if they match your appsettings)" -ForegroundColor Red
} else {
    Write-Host "  Status: Partial data - check above report." -ForegroundColor Yellow
}
Write-Host ""
Write-Host "  Refresh Dashboard pages to see live data." -ForegroundColor Cyan
Write-Host "  Login: http://localhost:5200/aneiang  (admin/demo123)" -ForegroundColor Gray
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
