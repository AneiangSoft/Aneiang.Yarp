
Add-Type -AssemblyName System.Net.Http
$c = [System.Net.Http.HttpClient]::new()
$content = [System.Net.Http.StringContent]::new('{"username":"admin","password":"demo123"}', [System.Text.Encoding]::UTF8, 'application/json')
$body = $c.PostAsync('http://localhost:5200/aneiang/login', $content).GetAwaiter().GetResult().Content.ReadAsStringAsync().GetAwaiter().GetResult()
$token = ($body | ConvertFrom-Json).token
$c.DefaultRequestHeaders.Authorization = [System.Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $token)
$r = $c.GetAsync('http://localhost:5200/aneiang/api/plugin-resources').GetAwaiter().GetResult().Content.ReadAsStringAsync().GetAwaiter().GetResult()
$j = $r | ConvertFrom-Json
Write-Output ('totals JSON: ' + ($j.totals | ConvertTo-Json -Compress))
