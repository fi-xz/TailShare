<#
.SYNOPSIS
  Publishes TailShare into the external location and builds + signs the sparse package.

.DESCRIPTION
  1. dotnet publish (self-contained, incl. Windows App SDK) -> out\publish, mirrored to InstallDir
  2. package\AppxManifest.template.xml (filled from config) + Assets -> out\layout
     -> makepri -> makeappx pack /nv -> out\TailShare.msix
  3. signtool sign with the code-signing cert (subject = Publisher) from Cert:\CurrentUser\My

  Identity, publisher and InstallDir come from config.local.psd1 (see config.example.psd1).
  makepri/makeappx/signtool come from the Microsoft.Windows.SDK.BuildTools NuGet package restored
  by the project, falling back to an installed Windows SDK.

.EXAMPLE
  .\scripts\build.ps1
  .\scripts\build.ps1 -Arch arm64
#>
[CmdletBinding()]
param(
    [ValidateSet('x64', 'arm64')]
    [string]$Arch = $(if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'arm64' } else { 'x64' }),
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Config.ps1')
$cfg        = Get-TailShareConfig
$InstallDir = $cfg.InstallDir
$root    = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src\TailShare\TailShare.csproj'
$out     = Join-Path $root 'out'
$publish = Join-Path $out 'publish'
$layout  = Join-Path $out 'layout'
$msix    = Join-Path $out 'TailShare.msix'

function Find-SdkTool([string]$Name) {
    $hostArch = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'arm64' } else { 'x64' }
    $nuget = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }
    $roots = @(
        Get-ChildItem (Join-Path $nuget 'microsoft.windows.sdk.buildtools') -Directory -ErrorAction SilentlyContinue |
            Sort-Object { [version]($_.Name -replace '-.*$') } -Descending |
            ForEach-Object { Join-Path $_.FullName 'bin' }
        Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    )
    foreach ($r in $roots) {
        $hit = Get-ChildItem -Path (Join-Path $r "*\$hostArch\$Name") -ErrorAction SilentlyContinue |
            Sort-Object { [version]$_.Directory.Parent.Name } -Descending |
            Select-Object -First 1
        if ($hit) { return $hit.FullName }
    }
    throw "$Name not found. Restore the project (dotnet restore) or install the Windows SDK."
}

function Find-SigningCert([string]$Subject) {
    Get-ChildItem Cert:\CurrentUser\My |
        Where-Object {
            $_.Subject -eq $Subject -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date) -and
            ($_.EnhancedKeyUsageList.ObjectId -contains '1.3.6.1.5.5.7.3.3')
        } |
        Sort-Object NotAfter -Descending |
        Select-Object -First 1
}

# Fail early, before a long publish, if signing can't happen.
$cert = Find-SigningCert $cfg.Publisher
if (-not $cert) {
    throw "No code-signing cert '$($cfg.Publisher)' in Cert:\CurrentUser\My. Run scripts\cert.ps1 (as administrator) first."
}

# MSBuild -p: splits values on ';' and ','; escape them.
function ConvertTo-MSBuildValue([string]$Value) { $Value -replace '%', '%25' -replace ';', '%3B' -replace ',', '%2C' }

# Running instances lock files in the install dir.
Get-Process TailShare -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

Write-Host "==> dotnet publish ($Configuration, win-$Arch)" -ForegroundColor Cyan
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish $project -c $Configuration -r "win-$Arch" -p:Platform=$Arch --self-contained true -o $publish `
    "-p:TailSharePackageName=$(ConvertTo-MSBuildValue $cfg.IdentityName)" `
    "-p:TailSharePublisher=$(ConvertTo-MSBuildValue $cfg.Publisher)"
if ($LASTEXITCODE) { throw "dotnet publish failed ($LASTEXITCODE)" }

Write-Host "==> Mirror to $InstallDir" -ForegroundColor Cyan
New-Item -ItemType Directory -Force $InstallDir | Out-Null
robocopy $publish $InstallDir /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy failed ($LASTEXITCODE)" }
# Assets next to the exe: the .ico files used at runtime (title bar, context menu), plus the logos
# in case the shell resolves them against the external location.
robocopy (Join-Path $root 'package\Assets') (Join-Path $InstallDir 'Assets') /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy Assets failed ($LASTEXITCODE)" }

Write-Host '==> makepri (theme/targetsize logo variants)' -ForegroundColor Cyan
if (Test-Path $layout) { Remove-Item $layout -Recurse -Force }
New-Item -ItemType Directory -Force $layout | Out-Null
$manifest = Get-Content (Join-Path $root 'package\AppxManifest.template.xml') -Raw
foreach ($key in 'IdentityName', 'Publisher', 'PublisherDisplayName') {
    $manifest = $manifest.Replace("{{$key}}", [System.Security.SecurityElement]::Escape($cfg.$key))
}
if ($manifest -match '\{\{\w+\}\}') { throw "Unfilled placeholder $($Matches[0]) in AppxManifest.template.xml" }
[IO.File]::WriteAllText((Join-Path $layout 'AppxManifest.xml'), $manifest, [Text.UTF8Encoding]::new($false))
Copy-Item (Join-Path $root 'package\Assets') $layout -Recurse
Remove-Item (Join-Path $layout 'Assets\*.ico')
$makepri = Find-SdkTool 'makepri.exe'
$priConfig = Join-Path $out 'priconfig.xml'
$log = & $makepri createconfig /cf $priConfig /dq en-US /pv 10.0.0 /o 2>&1
if ($LASTEXITCODE) { $log | Write-Host; throw "makepri createconfig failed ($LASTEXITCODE)" }
$log = & $makepri new /pr $layout /cf $priConfig /mn (Join-Path $layout 'AppxManifest.xml') /of (Join-Path $layout 'resources.pri') /o 2>&1
if ($LASTEXITCODE) { $log | Write-Host; throw "makepri new failed ($LASTEXITCODE)" }
# The shell resolves the package's logos against the external location, so the index goes there too.
Copy-Item (Join-Path $layout 'resources.pri') $InstallDir -Force

Write-Host '==> makeappx pack /nv' -ForegroundColor Cyan
$makeappx = Find-SdkTool 'makeappx.exe'
$log = & $makeappx pack /o /nv /d $layout /p $msix 2>&1
if ($LASTEXITCODE) { $log | Write-Host; throw "makeappx failed ($LASTEXITCODE)" }

Write-Host "==> signtool sign ($($cert.Subject), $($cert.Thumbprint))" -ForegroundColor Cyan
$signtool = Find-SdkTool 'signtool.exe'
& $signtool sign /q /fd SHA256 /s My /sha1 $cert.Thumbprint $msix
if ($LASTEXITCODE) { throw "signtool failed ($LASTEXITCODE)" }

Write-Host "`nBuilt $msix" -ForegroundColor Green
Write-Host "Next: .\scripts\register.ps1"
