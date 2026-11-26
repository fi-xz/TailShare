# Dot-sourced by the other scripts. Package identity and install location come from
# config.local.psd1 at the repo root (gitignored; see config.example.psd1), falling back to these defaults.

function Get-TailShareConfig {
    $config = [ordered]@{
        IdentityName         = 'TailShare'
        Publisher            = 'CN=TailShare'
        PublisherDisplayName = 'TailShare'
        InstallDir           = 'C:\Tools\TailShare'
    }

    $local = Join-Path (Split-Path $PSScriptRoot -Parent) 'config.local.psd1'
    if (Test-Path $local) {
        $overrides = Import-PowerShellDataFile $local
        foreach ($key in $overrides.Keys) {
            if (-not $config.Contains($key)) { throw "Unknown key '$key' in $local" }
            $config[$key] = $overrides[$key]
        }
    }

    if ($config.IdentityName -notmatch '^[A-Za-z0-9.\-]{3,50}$') {
        throw "IdentityName '$($config.IdentityName)' must be 3-50 letters, digits, '.' or '-'."
    }
    [pscustomobject]$config
}
