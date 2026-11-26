<#
.SYNOPSIS
  Removes the TailShare sparse package registration. Files in InstallDir are kept
  unless -RemoveFiles is given.
#>
[CmdletBinding()]
param(
    [switch]$RemoveFiles
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Config.ps1')
$cfg = Get-TailShareConfig

$existing = Get-AppxPackage -Name $cfg.IdentityName
if ($existing) {
    Remove-AppxPackage -Package $existing.PackageFullName
    Write-Host "Removed $($existing.PackageFullName)"
} else {
    Write-Host "$($cfg.IdentityName) is not registered."
}

if ($RemoveFiles -and (Test-Path $cfg.InstallDir)) {
    Get-Process TailShare -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Remove-Item $cfg.InstallDir -Recurse -Force
    Write-Host "Deleted $($cfg.InstallDir)"
}
