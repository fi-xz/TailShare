<#
.SYNOPSIS
  Registers the signed sparse package with InstallDir (config.local.psd1) as its external location.
  Re-running replaces any existing registration (the package version stays 1.0.0.0).
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Config.ps1')
$cfg  = Get-TailShareConfig
$root = Split-Path $PSScriptRoot -Parent
$msix = Join-Path $root 'out\TailShare.msix'

if (-not (Test-Path $msix)) { throw "$msix not found. Run scripts\build.ps1 first." }
if (-not (Test-Path (Join-Path $cfg.InstallDir 'TailShare.exe'))) { throw "TailShare.exe not found in $($cfg.InstallDir). Run scripts\build.ps1 first." }

$existing = Get-AppxPackage -Name $cfg.IdentityName
if ($existing) {
    Write-Host "Removing $($existing.PackageFullName)"
    Remove-AppxPackage -Package $existing.PackageFullName
}

Add-AppxPackage -Path $msix -ExternalLocation $cfg.InstallDir
Get-AppxPackage -Name $cfg.IdentityName | Select-Object Name, Version, PackageFullName, InstallLocation | Format-List
Write-Host 'Registered. In File Explorer: select files > Share > Tailscale.' -ForegroundColor Green
