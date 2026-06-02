$DashBase = "http://localhost:5200/aneiang"
$LoginUrl = "$DashBase/login"
$cookies = New-Object System.Net.CookieContainer

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
$token = ($sr.ReadToEnd() | ConvertFrom-Json).token
$sr.Close()
$resp.Close()

Write-Host "=== Routes from Dashboard API ===" -ForegroundColor Cyan
$req = [Net.HttpWebRequest]::Create("$DashBase/api/routes")
$req.Method = "GET"
$req.CookieContainer = $cookies
$req.Headers.Add("Authorization", "Bearer $token")
try {
    $resp = $req.GetResponse()
    $sr = New-Object System.IO.StreamReader($resp.GetResponseStream())
    $json = $sr.ReadToEnd() | ConvertFrom-Json
    $sr.Close()
    $resp.Close()
    foreach ($r in $json.data) {
        Write-Host "  $($r.routeId): $($r.match.path) -> ClusterId=$($r.clusterId)" -ForegroundColor Yellow
    }
} catch {
    Write-Host "Error: $_" -ForegroundColor Red
}

Write-Host ""
Write-Host "=== Clusters from Dashboard API ===" -ForegroundColor Cyan
$req2 = [Net.HttpWebRequest]::Create("$DashBase/api/clusters")
$req2.Method = "GET"
$req2.CookieContainer = $cookies
$req2.Headers.Add("Authorization", "Bearer $token")
try {
    $resp = $req2.GetResponse()
    $sr = New-Object System.IO.StreamReader($resp.GetResponseStream())
    $json = $sr.ReadToEnd() | ConvertFrom-Json
    $sr.Close()
    $resp.Close()
    foreach ($c in $json.data) {
        $dests = ($c.destinations.PSObject.Properties | ForEach-Object { "$($_.Name)=$($_.Value.address)" }) -join ", "
        Write-Host "  $($c.clusterId): $dests" -ForegroundColor Green
    }
} catch {
    Write-Host "Error: $_" -ForegroundColor Red
}
