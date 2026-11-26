<#
.SYNOPSIS
  Registers the signed sparse package with C:\Tools\TailShare as its external location.
  Re-running replaces any existing registration (the package version stays 1.0.0.0).
#>
[CmdletBinding()]
param(
    [string]$InstallDir = 'C:\Tools\TailShare'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$msix = Join-Path $root 'out\TailShare.msix'

if (-not (Test-Path $msix)) { throw "$msix not found. Run scripts\build.ps1 first." }
if (-not (Test-Path (Join-Path $InstallDir 'TailShare.exe'))) { throw "TailShare.exe not found in $InstallDir. Run scripts\build.ps1 first." }

$existing = Get-AppxPackage -Name 'nx.TailShare'
if ($existing) {
    Write-Host "Removing $($existing.PackageFullName)"
    Remove-AppxPackage -Package $existing.PackageFullName
}

Add-AppxPackage -Path $msix -ExternalLocation $InstallDir
Get-AppxPackage -Name 'nx.TailShare' | Select-Object Name, Version, PackageFullName, InstallLocation | Format-List
Write-Host 'Registered. In File Explorer: select files > Share > Tailscale.' -ForegroundColor Green
