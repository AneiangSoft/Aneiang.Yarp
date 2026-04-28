<#
.SYNOPSIS
    Build and/or push Aneiang.Yarp NuGet packages to a NuGet source.
.DESCRIPTION
    Builds src/Aneiang.Yarp and src/Aneiang.Yarp.Dashboard projects.
    Supports two modes:
      - Build only (default)
      - Build + Push (specify -Push with -ApiKey)
.PARAMETER Push
    Switch. When specified, pushes the generated .nupkg files to NuGet source.
.PARAMETER Source
    NuGet server URL. Default: https://api.nuget.org/v3/index.json
.PARAMETER ApiKey
    NuGet API key (required when -Push is used).
.PARAMETER Configuration
    Build configuration. Default: Release
.PARAMETER VersionSuffix
    Optional version suffix (e.g. "beta1" -> 2.0.0-beta1).
.EXAMPLE
    # Build only
    ./push-nuget.ps1

    # Build + push to nuget.org
    ./push-nuget.ps1 -Push -ApiKey "your-api-key"

    # Build + push to custom source with version suffix
    ./push-nuget.ps1 -Push -Source "http://nuget.local:5000/v3/index.json" -ApiKey "local" -VersionSuffix "preview1"
#>

param(
    [switch]$Push,
    [string]$Source = "https://api.nuget.org/v3/index.json",
    [string]$ApiKey = "",
    [string]$Configuration = "Release",
    [string]$VersionSuffix = ""
)

$ErrorActionPreference = "Stop"

# Resolve repo root
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$SrcDir = Join-Path $ScriptDir "src"
$ArtifactsDir = Join-Path (Join-Path $ScriptDir "artifacts") "nupkg"

# ── Build ──

Write-Host "=== Building Aneiang.Yarp packages ===" -ForegroundColor Cyan
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

# Build src projects (pack generates nupkg directly)
$Projects = @(
    "src\Aneiang.Yarp\Aneiang.Yarp.csproj",
    "src\Aneiang.Yarp.Dashboard\Aneiang.Yarp.Dashboard.csproj"
)

foreach ($proj in $Projects) {
    $projPath = Join-Path $ScriptDir $proj
    Write-Host "`n>>> dotnet pack $proj" -ForegroundColor Yellow
    & dotnet pack $projPath `
        --configuration $Configuration `
        --output $ArtifactsDir `
        --no-restore `
        @VersionSuffixArg

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

Get-ChildItem $ArtifactsDir -Filter "*.nupkg" | ForEach-Object {
    Write-Host ">>> Pushing $($_.Name) ..." -ForegroundColor Yellow
    & dotnet nuget push $_.FullName `
        --source $Source `
        --api-key $ApiKey

    if ($LASTEXITCODE -ne 0) {
        Write-Host "!!! Push failed: $($_.Name)" -ForegroundColor Red
        exit $LASTEXITCODE
    }
}

Write-Host "`n=== All packages pushed successfully ===" -ForegroundColor Green
