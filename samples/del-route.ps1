
Add-Type -AssemblyName System.Net.Http
$c = [System.Net.Http.HttpClient]::new()
$del = $c.DeleteAsync('http://localhost:5200/api/gateway/sample-local-service').GetAwaiter().GetResult()
$b = $del.Content.ReadAsStringAsync().GetAwaiter().GetResult()
Write-Output ('DELETE (no prefix): ' + [int]$del.StatusCode + ' ' + $b.Substring(0, [Math]::Min(200, $b.Length)))
