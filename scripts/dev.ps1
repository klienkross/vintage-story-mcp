param(
    [string]$VintageStoryPath,
    [switch]$Install,
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$RepoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $RepoRoot

function Require-Command([string]$Name) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' was not found on PATH."
    }
}

function Resolve-VintageStoryPath {
    param([string]$ExplicitPath)

    $candidates = @()
    if ($ExplicitPath) { $candidates += $ExplicitPath }
    if ($env:VINTAGE_STORY) { $candidates += $env:VINTAGE_STORY }
    if ($env:APPDATA) { $candidates += (Join-Path $env:APPDATA "Vintagestory") }

    foreach ($candidate in $candidates | Select-Object -Unique) {
        if ($candidate -and (Test-Path (Join-Path $candidate "VintagestoryAPI.dll"))) {
            return (Resolve-Path $candidate).Path
        }
    }

    throw @"
Vintage Story installation was not found.
Pass it explicitly:
  .\scripts\dev.ps1 -VintageStoryPath 'C:\path\to\Vintagestory'

Or set VINTAGE_STORY to the game install directory containing VintagestoryAPI.dll.
"@
}

Require-Command "dotnet"
$ResolvedVintageStoryPath = Resolve-VintageStoryPath $VintageStoryPath
$env:VINTAGE_STORY = $ResolvedVintageStoryPath

Write-Host "Vintage Story: $ResolvedVintageStoryPath"
Write-Host "Repository:    $RepoRoot"

if (-not $SkipTests) {
    Write-Host "`n== C# transport tests =="
    dotnet run --project tests/VintageStoryAgent.Tests/VintageStoryAgent.Tests.csproj
    if ($LASTEXITCODE -ne 0) { throw "C# transport tests failed." }

    $python = Get-Command python -ErrorAction SilentlyContinue
    if (-not $python) { $python = Get-Command py -ErrorAction SilentlyContinue }
    if ($python) {
        Write-Host "`n== Python probe tests =="
        Push-Location client
        try {
            if ($python.Name -eq "py.exe" -or $python.Name -eq "py") {
                & $python.Source -3 -m unittest test_probe.py
            } else {
                & $python.Source -m unittest test_probe.py
            }
            if ($LASTEXITCODE -ne 0) { throw "Python probe tests failed." }
        }
        finally {
            Pop-Location
        }
    }
    else {
        Write-Warning "Python was not found; skipping probe tests."
    }
}

Write-Host "`n== Build mod =="
dotnet build mod/VintageStoryAgent.csproj -c Debug
if ($LASTEXITCODE -ne 0) { throw "Vintage Story mod build failed." }

$BuiltMod = Join-Path $RepoRoot "mod/bin/Debug/Mods/vintagestoryagent"
if (-not (Test-Path $BuiltMod)) {
    throw "Build succeeded but expected mod output was not found at $BuiltMod"
}

Write-Host "Built mod: $BuiltMod"

if ($Install) {
    if (-not $env:APPDATA) {
        throw "APPDATA is unavailable; cannot determine the Vintage Story user Mods directory."
    }

    $ModsDir = Join-Path $env:APPDATA "VintagestoryData/Mods"
    $Destination = Join-Path $ModsDir "vintagestoryagent"
    New-Item -ItemType Directory -Force -Path $ModsDir | Out-Null

    if (Test-Path $Destination) {
        Remove-Item -Recurse -Force $Destination
    }
    Copy-Item -Recurse -Force $BuiltMod $Destination
    Write-Host "Installed mod: $Destination"
}

Write-Host "`nReady. Start Vintage Story, then run:"
Write-Host "  python client/probe.py state"
