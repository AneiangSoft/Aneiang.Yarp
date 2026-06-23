<#
.SYNOPSIS
    Build and/or push Aneiang.Yarp NuGet packages to a NuGet source.
.DESCRIPTION
    Builds all packable projects under src/ and optionally pushes the generated
    .nupkg files to a NuGet server.

    Projects packed:
      - Aneiang.Yarp
      - Aneiang.Yarp.Client
      - Aneiang.Yarp.Grpc
      - Aneiang.Yarp.Dashboard
      - Aneiang.Yarp.Storage.Abstractions
      - Aneiang.Yarp.Storage.Sqlite
.PARAMETER Push
    Switch. When specified, pushes the generated .nupkg files to NuGet source.
.PARAMETER Source
    NuGet server URL. Default: https://api.nuget.org/v3/index.json
.PARAMETER ApiKey
    NuGet API key (required when -Push is used).
.PARAMETER Configuration
    Build configuration. Default: Release
.PARAMETER VersionSuffix
    Optional version suffix (e.g. "beta1" -> 2.3.0-beta1).
.PARAMETER SkipRestore
    Skip dotnet restore before pack (use if already restored).
.EXAMPLE
    # Build only
    ./push-nuget.ps1

    # Build + push to nuget.org
    ./push-nuget.ps1 -Push -ApiKey "your-api-key"

    # Build + push to custom source with version suffix
    ./push-nuget.ps1 -Push -Source "http://nuget.local:5000/v3/index.json" -ApiKey "local" -VersionSuffix "preview1"

    # Build without restore (faster, if already restored)
    ./push-nuget.ps1 -SkipRestore
#>

param(
    [switch]$Push,
    [string]$Source = "https://api.nuget.org/v3/index.json",
    [string]$ApiKey = "",
    [string]$Configuration = "Release",
    [string]$VersionSuffix = "",
    [switch]$SkipRestore
)

$ErrorActionPreference = "Stop"

# Resolve repo root
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ArtifactsDir = Join-Path (Join-Path $ScriptDir "artifacts") "nupkg"

# Projects to pack (order matters: dependencies first)
$Projects = @(
    "src\Aneiang.Yarp.Storage.Abstractions\Aneiang.Yarp.Storage.Abstractions.csproj",
    "src\Aneiang.Yarp.Storage.Sqlite\Aneiang.Yarp.Storage.Sqlite.csproj",
    "src\Aneiang.Yarp.Client\Aneiang.Yarp.Client.csproj",
    "src\Aneiang.Yarp.Grpc\Aneiang.Yarp.Grpc.csproj",
    "src\Aneiang.Yarp\Aneiang.Yarp.csproj",
    "src\Aneiang.Yarp.Dashboard\Aneiang.Yarp.Dashboard.csproj"
)

# ── Restore ──

if (-not $SkipRestore) {
    Write-Host "=== Restoring packages ===" -ForegroundColor Cyan
    & dotnet restore (Join-Path $ScriptDir "Aneiang.Yarp.sln")
    if ($LASTEXITCODE -ne 0) {
        Write-Host "!!! Restore failed" -ForegroundColor Red
        exit $LASTEXITCODE
    }
}

# ── Build ──

Write-Host "`n=== Building Aneiang.Yarp packages ===" -ForegroundColor Cyan
Write-Host "  Configuration : $Configuration" -ForegroundColor Gray
if ($VersionSuffix) {
    Write-Host "  VersionSuffix : $VersionSuffix" -ForegroundColor Gray
    $VersionSuffixArg = "--version-suffix", $VersionSuffix
} else {
    $VersionSuffixArg = @()
}

# Clean previous packages
if (Test-Path $ArtifactsDir) {
    Remove-Item "$ArtifactsDir\*.nupkg", "$ArtifactsDir\*.snupkg" -ErrorAction SilentlyContinue
}

$PackArgs = @("--configuration", $Configuration, "--output", $ArtifactsDir)
if ($SkipRestore) { $PackArgs += "--no-restore" }
$PackArgs += $VersionSuffixArg

foreach ($proj in $Projects) {
    $projPath = Join-Path $ScriptDir $proj
    if (-not (Test-Path $projPath)) {
        Write-Host "  Skip (not found): $proj" -ForegroundColor DarkGray
        continue
    }
    Write-Host "`n>>> dotnet pack $proj" -ForegroundColor Yellow
    & dotnet pack $projPath @PackArgs

    if ($LASTEXITCODE -ne 0) {
        Write-Host "!!! Build failed: $proj" -ForegroundColor Red
        exit $LASTEXITCODE
    }
}

Write-Host "`n=== Build completed ===" -ForegroundColor Green

# ── Push ──

if (-not $Push) {
    Write-Host "`nPackages output: $ArtifactsDir" -ForegroundColor Cyan
    Get-ChildItem $ArtifactsDir -Filter "*.nupkg" | ForEach-Object { Write-Host "  $($_.Name)" -ForegroundColor Gray }
    Write-Host "`nTip: Use -Push to push packages to NuGet source." -ForegroundColor Yellow
    exit 0
}

if ([string]::IsNullOrWhiteSpace($ApiKey)) {
    Write-Host "!!! -ApiKey is required when -Push is specified" -ForegroundColor Red
    exit 1
}

Write-Host "`n=== Pushing packages to $Source ===" -ForegroundColor Cyan

$pushedCount = 0
Get-ChildItem $ArtifactsDir -Filter "*.nupkg" | ForEach-Object {
    Write-Host ">>> Pushing $($_.Name) ..." -ForegroundColor Yellow
    & dotnet nuget push $_.FullName `
        --source $Source `
        --api-key $ApiKey

    if ($LASTEXITCODE -ne 0) {
        Write-Host "!!! Push failed: $($_.Name)" -ForegroundColor Red
        exit $LASTEXITCODE
    }
    $pushedCount++
}

Write-Host "`n=== $pushedCount package(s) pushed successfully ===" -ForegroundColor Green
