# Copy to config.local.psd1 (gitignored) and adjust. Every key is optional; missing keys use the
# defaults in scripts\Config.ps1.
@{
    # Package identity name: letters, digits, '.' and '-' (3-50 chars). Pick something unique to you.
    IdentityName         = 'yourname.TailShare'

    # Must equal the subject of the signing certificate that scripts\cert.ps1 creates.
    Publisher            = 'CN=yourname'

    # Shown as the publisher in Windows settings.
    PublisherDisplayName = 'yourname'

    # External location: where build.ps1 publishes TailShare.exe and register.ps1 points the package.
    InstallDir           = 'C:\Tools\TailShare'
}
