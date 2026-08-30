<#
.SYNOPSIS
    Build and/or push Aneiang.Yarp NuGet packages to a NuGet source.
.DESCRIPTION
    Builds all packable projects under src/ and optionally pushes the generated
    .nupkg (and optionally .snupkg) files to a NuGet server.

    Projects packed (dependencies first):
      - Aneiang.Yarp.Plugin.Abstractions
      - Aneiang.Yarp.Storage.Abstractions
      - Aneiang.Yarp.Storage.Sqlite
      - Aneiang.Yarp.Client
      - Aneiang.Yarp.Grpc
      - Aneiang.Yarp
      - Aneiang.Yarp.Plugin.Waf / Retry / CircuitBreaker / RateLimit /
        RateLimit.Redis / ProxyLog / Cache / Compression / ServiceDiscovery / Metrics
      - Aneiang.Yarp.Dashboard

    Base version comes from src/Directory.Build.props (<Version>). Use
    -VersionSuffix or -Version to override at pack time (passed as -p:Version,
    because --version-suffix is ignored when a static <Version> is declared).

    Push behaviour:
      - Packages are pushed one by one; a failed push does NOT abort the
        remaining packages (failures are collected and reported at the end).
      - Transient failures are retried (-RetryCount).
      - Local folder sources (non-http) do not require an API key.
      - -SkipDuplicate tolerates "version already exists" (409) responses,
        useful for re-pushing preview versions.

.PARAMETER Push
    Switch. When specified, pushes the generated .nupkg files to NuGet source.
.PARAMETER Source
    NuGet server URL or local folder path. Default: https://api.nuget.org/v3/index.json
.PARAMETER ApiKey
    NuGet API key (required when -Push is used with an http(s) source).
.PARAMETER Symbols
    Also push generated .snupkg symbol packages. By default they are built
    (IncludeSymbols=true in Directory.Build.props) but not pushed.
.PARAMETER SymbolSource
    Separate symbol server URL. Defaults to -Source. Ignored without -Symbols.
.PARAMETER SymbolApiKey
    API key for the symbol source. Defaults to -ApiKey.
.PARAMETER Configuration
    Build configuration. Default: Release
.PARAMETER Version
    Full version override, e.g. "2.3.0.30-preview.2". Takes precedence over
    -VersionSuffix.
.PARAMETER VersionSuffix
    Version suffix appended to the base version from Directory.Build.props
    (e.g. "preview.2" -> 2.3.0.30-preview.2 when base is 2.3.0.30-preview.1,
    the base's own suffix is stripped first).
.PARAMETER SkipRestore
    Skip dotnet restore before pack (use if already restored).
.PARAMETER SkipBuild
    Push existing artifacts only (skip restore + pack).
.PARAMETER SkipDuplicate
    Pass --skip-duplicate to dotnet nuget push (tolerate 409 conflicts).
.PARAMETER RetryCount
    Retries per package on transient push failure. Default: 2.
.PARAMETER DryRun
    Do everything except the actual push; print the commands instead.
.EXAMPLE
    # Build only
    ./push-nuget.ps1

    # Build + push to nuget.org
    ./push-nuget.ps1 -Push -ApiKey "your-api-key"

    # Build with custom version + push to local folder source (no key needed)
    ./push-nuget.ps1 -Push -Source "D:\nuget-feed" -VersionSuffix "local1"

    # Push preview re-release, ignoring "already exists" conflicts
    ./push-nuget.ps1 -Push -ApiKey "key" -VersionSuffix "preview.2" -SkipDuplicate

    # Push existing artifacts including symbols, no rebuild
    ./push-nuget.ps1 -Push -SkipBuild -Symbols -ApiKey "key"

    # See what would be pushed without pushing
    ./push-nuget.ps1 -Push -DryRun -SkipBuild
#>

param(
    [switch]$Push,
    [string]$Source = "https://api.nuget.org/v3/index.json",
    [string]$ApiKey = "",
    [switch]$Symbols,
    [string]$SymbolSource = "",
    [string]$SymbolApiKey = "",
    [string]$Configuration = "Release",
    [string]$Version = "",
    [string]$VersionSuffix = "",
    [switch]$SkipRestore,
    [switch]$SkipBuild,
    [switch]$SkipDuplicate,
    [int]$RetryCount = 2,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
$Script:TotalStopwatch = [System.Diagnostics.Stopwatch]::StartNew()

# Resolve repo root
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ArtifactsDir = Join-Path (Join-Path $ScriptDir "artifacts") "nupkg"
$BuildPropsPath = Join-Path $ScriptDir "src\Directory.Build.props"

# Projects to pack (order matters: dependencies first)
$Projects = @(
    "src\Aneiang.Yarp.Plugin.Abstractions\Aneiang.Yarp.Plugin.Abstractions.csproj",
    "src\Aneiang.Yarp.Storage.Abstractions\Aneiang.Yarp.Storage.Abstractions.csproj",
    "src\Aneiang.Yarp.Storage.Sqlite\Aneiang.Yarp.Storage.Sqlite.csproj",
    "src\Aneiang.Yarp.Client\Aneiang.Yarp.Client.csproj",
    "src\Aneiang.Yarp.Grpc\Aneiang.Yarp.Grpc.csproj",
    "src\Aneiang.Yarp\Aneiang.Yarp.csproj",
    "src\Aneiang.Yarp.Plugin.Waf\Aneiang.Yarp.Plugin.Waf.csproj",
    "src\Aneiang.Yarp.Plugin.Retry\Aneiang.Yarp.Plugin.Retry.csproj",
    "src\Aneiang.Yarp.Plugin.CircuitBreaker\Aneiang.Yarp.Plugin.CircuitBreaker.csproj",
    "src\Aneiang.Yarp.Plugin.RateLimit\Aneiang.Yarp.Plugin.RateLimit.csproj",
    "src\Aneiang.Yarp.Plugin.RateLimit.Redis\Aneiang.Yarp.Plugin.RateLimit.Redis.csproj",
    "src\Aneiang.Yarp.Plugin.ProxyLog\Aneiang.Yarp.Plugin.ProxyLog.csproj",
    "src\Aneiang.Yarp.Plugin.Cache\Aneiang.Yarp.Plugin.Cache.csproj",
    "src\Aneiang.Yarp.Plugin.Compression\Aneiang.Yarp.Plugin.Compression.csproj",
    "src\Aneiang.Yarp.Plugin.ServiceDiscovery\Aneiang.Yarp.Plugin.ServiceDiscovery.csproj",
    "src\Aneiang.Yarp.Plugin.Metrics\Aneiang.Yarp.Plugin.Metrics.csproj",
    "src\Aneiang.Yarp.Dashboard\Aneiang.Yarp.Dashboard.csproj"
)

# ── Helpers ──

function Write-Step { param([string]$Message) Write-Host "`n=== $Message ===" -ForegroundColor Cyan }
function Write-Ok   { param([string]$Message) Write-Host "    [OK] $Message" -ForegroundColor Green }
function Write-Warn { param([string]$Message) Write-Host "    [!]  $Message" -ForegroundColor Yellow }
function Write-Fail { param([string]$Message) Write-Host "    [X]  $Message" -ForegroundColor Red }

function Get-BaseVersion {
    # Extract <Version> from src/Directory.Build.props; strip any existing suffix.
    if (-not (Test-Path $BuildPropsPath)) { return $null }
    $raw = Select-String -Path $BuildPropsPath -Pattern '<Version>([^<]+)</Version>' | Select-Object -First 1
    if (-not $raw) { return $null }
    $v = $raw.Matches[0].Groups[1].Value.Trim()
    return ($v -split '-')[0]
}

function Test-HttpSource { param([string]$Url) $Url -match '^https?://' }

function Invoke-DotNet {
    # Runs dotnet with argument list; returns $true/$false, sets script:LASTDOTNET.
    param([string[]]$ArgumentList, [string]$Activity)
    & dotnet @ArgumentList 2>&1 | ForEach-Object { "$_" } | Write-Host
    return ($LASTEXITCODE -eq 0)
}

function Invoke-Push {
    # Push a single package with retries. Returns $true on success.
    param([string]$PackagePath, [string[]]$ExtraArgs, [string]$Label)
    $attempts = $RetryCount + 1
    for ($i = 1; $i -le $attempts; $i++) {
        if ($DryRun) {
            Write-Host "    [dry-run] dotnet nuget push $(Split-Path -Leaf $PackagePath) $($ExtraArgs -join ' ')"
            return $true
        }
        Write-Host "    >>> $Label (attempt $i/$attempts)" -ForegroundColor Yellow
        & dotnet nuget push $PackagePath @ExtraArgs 2>&1 | ForEach-Object { "$_" } | Write-Host
        if ($LASTEXITCODE -eq 0) { return $true }
        if ($i -lt $attempts) { Start-Sleep -Seconds (3 * $i) }  # linear backoff
    }
    return $false
}

# ── Validate dotnet availability ──

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Fail "dotnet CLI not found in PATH."
    exit 1
}

# ── Resolve effective version ──

$EffectiveVersion = $Version
if (-not $EffectiveVersion -and $VersionSuffix) {
    $BaseVersion = Get-BaseVersion
    if ($BaseVersion) {
        $EffectiveVersion = "$BaseVersion-$VersionSuffix"
    } else {
        Write-Fail "-VersionSuffix given but no <Version> found in src\Directory.Build.props (and -Version not set)."
        exit 1
    }
}

# ── Restore + Build ──

if (-not $SkipBuild) {

    if (-not $SkipRestore) {
        Write-Step "Restoring packages"
        if (-not (Invoke-DotNet @("restore", (Join-Path $ScriptDir "Aneiang.Yarp.sln")) "restore")) {
            Write-Fail "Restore failed."
            exit 1
        }
    }

    Write-Step "Building Aneiang.Yarp packages"
    Write-Host "  Configuration : $Configuration" -ForegroundColor Gray
    if ($EffectiveVersion) {
        Write-Host "  Version       : $EffectiveVersion (override)" -ForegroundColor Gray
    } else {
        Write-Host "  Version       : from src\Directory.Build.props" -ForegroundColor Gray
    }

    # Clean previous packages
    if (Test-Path $ArtifactsDir) {
        Remove-Item "$ArtifactsDir\*.nupkg", "$ArtifactsDir\*.snupkg" -ErrorAction SilentlyContinue
    } else {
        New-Item -ItemType Directory -Path $ArtifactsDir -Force | Out-Null
    }

    $PackArgs = @("pack", "", "--configuration", $Configuration, "--output", $ArtifactsDir)
    if ($SkipRestore) { $PackArgs += "--no-restore" }
    if ($EffectiveVersion) { $PackArgs += @("-p:Version=$EffectiveVersion") }

    $failedProjects = @()
    foreach ($proj in $Projects) {
        $projPath = Join-Path $ScriptDir $proj
        if (-not (Test-Path $projPath)) {
            Write-Warn "Skip (not found): $proj"
            continue
        }
        Write-Host "`n>>> dotnet pack $proj" -ForegroundColor Yellow
        $PackArgs[1] = $projPath
        if (-not (Invoke-DotNet $PackArgs "pack $proj")) {
            Write-Fail "Build failed: $proj"
            $failedProjects += $proj
        }
    }
    if ($failedProjects.Count -gt 0) {
        Write-Fail "Build failed for: $($failedProjects -join ', ')"
        exit 1
    }

    # Verify artifacts were actually produced
    $builtPackages = @(Get-ChildItem $ArtifactsDir -Filter "*.nupkg" -ErrorAction SilentlyContinue)
    if ($builtPackages.Count -eq 0) {
        Write-Fail "No .nupkg files produced in $ArtifactsDir."
        exit 1
    }

    Write-Step "Build completed"
    $totalSize = ($builtPackages | Measure-Object -Property Length -Sum).Sum
    Write-Ok "$($builtPackages.Count) nupkg ($( [math]::Round($totalSize / 1MB, 2) ) MB) in $ArtifactsDir"
}

# ── Push ──

if (-not $Push) {
    Write-Step "Done (build only)"
    Get-ChildItem $ArtifactsDir -Filter "*.nupkg" -ErrorAction SilentlyContinue |
        ForEach-Object { Write-Host "  $($_.Name)" -ForegroundColor Gray }
    Write-Host "`nTip: Use -Push to push packages to NuGet source." -ForegroundColor Yellow
    exit 0
}

# API key required only for http(s) sources (local folder feeds need none)
$IsHttp = Test-HttpSource $Source
if ($IsHttp -and [string]::IsNullOrWhiteSpace($ApiKey)) {
    Write-Fail "-ApiKey is required when -Push targets an http(s) source (use a local folder Source to skip auth)."
    exit 1
}
if (-not (Test-Path $ArtifactsDir)) {
    Write-Fail "Artifacts directory not found: $ArtifactsDir (run without -SkipBuild first)."
    exit 1
}

$packages = @(Get-ChildItem $ArtifactsDir -Filter "*.nupkg" -ErrorAction SilentlyContinue)
if ($packages.Count -eq 0) {
    Write-Fail "No .nupkg files in $ArtifactsDir."
    exit 1
}

Write-Step "Pushing $($packages.Count) package(s) to $Source"

$SourceArgs = @("--source", $Source)
if ($IsHttp)    { $SourceArgs += @("--api-key", $ApiKey) }
if ($SkipDuplicate) { $SourceArgs += "--skip-duplicate" }

$SymbolArgs = $null
if ($Symbols) {
    $symSource = if ($SymbolSource) { $SymbolSource } else { $Source }
    $symKey = if ($SymbolApiKey) { $SymbolApiKey } else { $ApiKey }
    $SymbolArgs = @("--source", $symSource)
    if (Test-HttpSource $symSource) { $SymbolArgs += @("--api-key", $symKey) }
    if ($SkipDuplicate) { $SymbolArgs += "--skip-duplicate" }
}

$pushed = New-Object System.Collections.Generic.List[string]
$failed = New-Object System.Collections.Generic.List[string]

foreach ($pkg in $packages) {
    if (Invoke-Push -PackagePath $pkg.FullName -ExtraArgs $SourceArgs -Label "push $($pkg.Name)") {
        $pushed.Add($pkg.Name)
    } else {
        $failed.Add($pkg.Name)
    }
}

# Symbol packages (snupkg): push after the nupkg set
if ($Symbols -and $SymbolArgs) {
    $symbolPackages = @(Get-ChildItem $ArtifactsDir -Filter "*.snupkg" -ErrorAction SilentlyContinue)
    if ($symbolPackages.Count -eq 0) {
        Write-Warn "-Symbols given but no .snupkg files found in $ArtifactsDir."
    }
    foreach ($spkg in $symbolPackages) {
        if (Invoke-Push -PackagePath $spkg.FullName -ExtraArgs $SymbolArgs -Label "push $($spkg.Name) [symbols]") {
            $pushed.Add($spkg.Name)
        } else {
            $failed.Add($spkg.Name)
        }
    }
}

$Script:TotalStopwatch.Stop()
$elapsed = [math]::Round($Script:TotalStopwatch.Elapsed.TotalSeconds, 1)

Write-Host ""
if ($failed.Count -gt 0) {
    Write-Host "=== $($pushed.Count) pushed, $($failed.Count) FAILED (total ${elapsed}s) ===" -ForegroundColor Red
    $failed | ForEach-Object { Write-Fail $_ }
    exit 1
}

Write-Host "=== $($pushed.Count) package(s) pushed successfully (total ${elapsed}s) ===" -ForegroundColor Green
if ($DryRun) { Write-Host "(dry run: nothing was actually pushed)" -ForegroundColor Yellow }
elseif ($IsHttp -and $Source -match 'nuget\.org') {
    Write-Host "Note: packages may take a few minutes to be indexed and appear on nuget.org." -ForegroundColor Yellow
}
