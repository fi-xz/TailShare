<#
.SYNOPSIS
  Removes the TailShare sparse package registration. Files in the install dir are kept
  unless -RemoveFiles is given.
#>
[CmdletBinding()]
param(
    [string]$InstallDir = 'C:\Tools\TailShare',
    [switch]$RemoveFiles
)

$ErrorActionPreference = 'Stop'

$existing = Get-AppxPackage -Name 'nx.TailShare'
if ($existing) {
    Remove-AppxPackage -Package $existing.PackageFullName
    Write-Host "Removed $($existing.PackageFullName)"
} else {
    Write-Host 'nx.TailShare is not registered.'
}

if ($RemoveFiles -and (Test-Path $InstallDir)) {
    Get-Process TailShare -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Remove-Item $InstallDir -Recurse -Force
    Write-Host "Deleted $InstallDir"
}
