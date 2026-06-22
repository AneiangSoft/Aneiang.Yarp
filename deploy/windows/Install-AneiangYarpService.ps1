param(
    [string]$ServiceName = "AneiangYarp",
    [string]$AppDirectory = "C:\\Services\\AneiangYarp",
    [string]$DllName = "SampleGateway.dll"
)

$dotnet = (Get-Command dotnet -ErrorAction Stop).Source
$binaryPath = "`"$dotnet`" `"$AppDirectory\\$DllName`""

if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    Stop-Service -Name $ServiceName -ErrorAction SilentlyContinue
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

New-Service `
    -Name $ServiceName `
    -BinaryPathName $binaryPath `
    -DisplayName "Aneiang YARP Gateway" `
    -Description "Aneiang.Yarp reverse proxy gateway" `
    -StartupType Automatic

[Environment]::SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production", "Machine")
[Environment]::SetEnvironmentVariable("Gateway__Deployment__Mode", "Split", "Machine")
[Environment]::SetEnvironmentVariable("Gateway__Deployment__EndpointRoles__Proxy", "Proxy", "Machine")
[Environment]::SetEnvironmentVariable("Gateway__Deployment__EndpointRoles__Dashboard", "Dashboard", "Machine")
[Environment]::SetEnvironmentVariable("Gateway__Dashboard__AuthMode", "DefaultJwt", "Machine")
[Environment]::SetEnvironmentVariable("Gateway__Dashboard__JwtPassword", "CHANGE_ME_STRONG_PASSWORD", "Machine")
[Environment]::SetEnvironmentVariable("Kestrel__Endpoints__Proxy__Url", "http://0.0.0.0:8080", "Machine")
[Environment]::SetEnvironmentVariable("Kestrel__Endpoints__Dashboard__Url", "http://127.0.0.1:5000", "Machine")

Start-Service -Name $ServiceName
Get-Service -Name $ServiceName
