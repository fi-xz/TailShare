<#
.SYNOPSIS
  Regenerates package\Assets from the source art. Run after changing art\*.png; the output is checked in.

.DESCRIPTION
  art\icon.png        plated tile (dark rounded square)   -> tiles, StoreLogo, plated Square44x44Logo
  art\icon-dark.png   light glyph on transparent          -> dark-theme variants (altform-unplated, TailShare-dark.ico)
  art\icon-light.png  dark glyph on transparent           -> light-theme variants (altform-lightunplated, TailShare-light.ico)

  The shell picks Square44x44Logo.targetsize-N[_altform-(light)unplated].png through resources.pri,
  which build.ps1 generates with makepri. The .ico files are for the window title bar and the
  Explorer context-menu command, which choose one at runtime from the current theme.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root   = Split-Path $PSScriptRoot -Parent
$art    = Join-Path $root 'art'
$assets = Join-Path $root 'package\Assets'

function Get-Resized([System.Drawing.Image]$src, [int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CompositingMode = 'SourceCopy'
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.SmoothingMode = 'HighQuality'
    $g.PixelOffsetMode = 'HighQuality'
    $attr = New-Object System.Drawing.Imaging.ImageAttributes
    $attr.SetWrapMode([System.Drawing.Drawing2D.WrapMode]::TileFlipXY)
    $g.DrawImage($src, (New-Object System.Drawing.Rectangle 0, 0, $size, $size), 0, 0, $src.Width, $src.Height, 'Pixel', $attr)
    $g.Dispose()
    $bmp
}

function Save-Png([System.Drawing.Image]$src, [int]$size, [string]$name) {
    $bmp = Get-Resized $src $size
    $bmp.Save((Join-Path $assets $name), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

# Multi-size .ico with PNG-compressed entries (supported since Vista).
function Save-Ico([System.Drawing.Image]$src, [int[]]$sizes, [string]$name) {
    $images = foreach ($s in $sizes) {
        $bmp = Get-Resized $src $s
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        , $ms.ToArray()
    }
    $out = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter $out
    $w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $s = $sizes[$i]
        $dim = if ($s -ge 256) { 0 } else { $s }
        $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
        $w.Write([uint16]1); $w.Write([uint16]32)
        $w.Write([uint32]$images[$i].Length); $w.Write([uint32]$offset)
        $offset += $images[$i].Length
    }
    foreach ($img in $images) { $w.Write([byte[]]$img) }
    $w.Flush()
    [IO.File]::WriteAllBytes((Join-Path $assets $name), $out.ToArray())
}

$plated = [System.Drawing.Image]::FromFile((Join-Path $art 'icon.png'))
$dark   = [System.Drawing.Image]::FromFile((Join-Path $art 'icon-dark.png'))
$light  = [System.Drawing.Image]::FromFile((Join-Path $art 'icon-light.png'))
try {
    Remove-Item (Join-Path $assets '*') -Include *.png, *.ico -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force $assets | Out-Null

    Save-Png $plated 150 'Square150x150Logo.png'
    Save-Png $plated 50  'StoreLogo.png'
    Save-Png $plated 44  'Square44x44Logo.png'

    foreach ($t in 16, 20, 24, 32, 40, 48, 64, 256) {
        Save-Png $plated $t "Square44x44Logo.targetsize-$t.png"
        Save-Png $dark   $t "Square44x44Logo.targetsize-${t}_altform-unplated.png"
        Save-Png $light  $t "Square44x44Logo.targetsize-${t}_altform-lightunplated.png"
    }

    $icoSizes = 16, 20, 24, 32, 40, 48, 64, 256
    Save-Ico $dark  $icoSizes 'TailShare-dark.ico'
    Save-Ico $light $icoSizes 'TailShare-light.ico'
} finally {
    $plated.Dispose(); $dark.Dispose(); $light.Dispose()
}

Get-ChildItem $assets | Measure-Object | ForEach-Object { "Wrote $($_.Count) files to $assets" }
