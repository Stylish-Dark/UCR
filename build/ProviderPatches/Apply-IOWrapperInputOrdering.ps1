param(
    [Parameter(Mandatory = $true)]
    [string]$IOWrapperRoot
)

$ErrorActionPreference = 'Stop'
$path = Join-Path $IOWrapperRoot 'Source\Provider Libraries\Device Update Handling\DeviceHandlers\Devices\DeviceHandlerBase.cs'
if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
    throw "IOWrapper device handler source not found: $path"
}

$source = Get-Content -LiteralPath $path -Raw
$scheduled = 'Task.Factory.StartNew(() => subreq.Callback(value));'
$synchronous = 'subreq.Callback(value);'

if ($source.Contains($scheduled)) {
    $occurrences = ([regex]::Matches($source, [regex]::Escape($scheduled))).Count
    if ($occurrences -ne 1) {
        throw "Expected exactly one scheduled subscription callback site; found $occurrences."
    }
    $source = $source.Replace($scheduled, $synchronous)
    Set-Content -LiteralPath $path -Value $source -Encoding UTF8
}

$verified = Get-Content -LiteralPath $path -Raw
if ($verified.Contains($scheduled) -or -not $verified.Contains($synchronous)) {
    throw 'IOWrapper input-ordering patch verification failed.'
}

Write-Host 'Applied IOWrapper input-ordering fix: subscription callbacks execute synchronously in physical event order.'
