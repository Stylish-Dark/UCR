param(
    [switch]$Check
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$source = Join-Path $repoRoot 'artwork\device-glyphs\masters'
$destination = Join-Path $repoRoot 'UCR\Assets\DeviceGlyphs'

$glyphs = @(
    'ps1.png',
    'ps2.png',
    'ps3.png',
    'dualshock4.png',
    'dualsense.png',
    'xbox-original.png',
    'xbox360.png',
    'xboxone.png',
    'xboxseries.png',
    'n64.png',
    'gamecube.png',
    'wiiremote.png',
    'wiiclassic.png',
    'switchpro.png',
    'joycon.png',
    'keyboard.png',
    'vjoy.png',
    'device-generic.png'
)

foreach ($name in $glyphs) {
    $src = Join-Path $source $name
    $dst = Join-Path $destination $name

    if (-not (Test-Path -LiteralPath $src -PathType Leaf)) {
        throw "Missing glyph master: $src"
    }

    if ($Check) {
        if (-not (Test-Path -LiteralPath $dst -PathType Leaf)) {
            throw "Missing runtime glyph: $dst"
        }

        $sourceHash = (Get-FileHash -LiteralPath $src -Algorithm SHA256).Hash
        $runtimeHash = (Get-FileHash -LiteralPath $dst -Algorithm SHA256).Hash
        if ($sourceHash -ne $runtimeHash) {
            throw "Glyph is out of sync: $name"
        }
    }
    else {
        Copy-Item -LiteralPath $src -Destination $dst -Force
        Write-Host "Synced $name"
    }
}

if ($Check) {
    Write-Host "All $($glyphs.Count) device glyphs are in sync."
}
