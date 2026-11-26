#Requires -RunAsAdministrator
<#
.SYNOPSIS
  One-time setup: creates a self-signed code-signing cert for the sparse package and trusts it.

.DESCRIPTION
  - Creates (or reuses) a cert with Subject = package Publisher ("CN=nx") in Cert:\CurrentUser\My.
    build.ps1 signs with it from there by thumbprint; no PFX or password on disk.
  - Imports the public part into Cert:\LocalMachine\TrustedPeople so Add-AppxPackage accepts the package.

  Run from an elevated prompt of the SAME Windows account that runs build.ps1
  (CurrentUser refers to the account the elevated shell runs as).
#>
[CmdletBinding()]
param(
    [string]$Subject = 'CN=nx',
    [int]$Years = 5
)

$ErrorActionPreference = 'Stop'
$codeSigningEku = '1.3.6.1.5.5.7.3.3'

$cert = Get-ChildItem Cert:\CurrentUser\My |
    Where-Object {
        $_.Subject -eq $Subject -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date).AddDays(30) -and
        ($_.EnhancedKeyUsageList.ObjectId -contains $codeSigningEku)
    } |
    Sort-Object NotAfter -Descending |
    Select-Object -First 1

if ($cert) {
    Write-Host "Reusing $($cert.Thumbprint) (expires $($cert.NotAfter.ToString('yyyy-MM-dd')))"
} else {
    $cert = New-SelfSignedCertificate `
        -Type Custom `
        -Subject $Subject `
        -FriendlyName 'TailShare sparse package signing' `
        -KeyUsage DigitalSignature `
        -KeyAlgorithm RSA -KeyLength 2048 -HashAlgorithm SHA256 `
        -TextExtension @("2.5.29.37={text}$codeSigningEku", '2.5.29.19={text}') `
        -NotAfter (Get-Date).AddYears($Years) `
        -CertStoreLocation Cert:\CurrentUser\My
    Write-Host "Created $($cert.Thumbprint) (expires $($cert.NotAfter.ToString('yyyy-MM-dd')))"
}

$trusted = Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object Thumbprint -eq $cert.Thumbprint
if ($trusted) {
    Write-Host 'Already in LocalMachine\TrustedPeople'
} else {
    $cer = Join-Path ([IO.Path]::GetTempPath()) "TailShare-$($cert.Thumbprint).cer"
    try {
        Export-Certificate -Cert $cert -FilePath $cer | Out-Null
        Import-Certificate -FilePath $cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
    } finally {
        Remove-Item $cer -ErrorAction SilentlyContinue
    }
    Write-Host 'Imported into LocalMachine\TrustedPeople'
}

Write-Host 'Done. Next: .\scripts\build.ps1' -ForegroundColor Green
