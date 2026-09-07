param(
    [Parameter(Mandatory = $true)]
    [string]$IOWrapperRoot
)

$ErrorActionPreference = 'Stop'
$sourcePath = Join-Path $IOWrapperRoot 'Source\Core Providers\Core_ViGEm\DS4Handler.cs'

if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
    throw "Core_ViGEm DS4 source not found: $sourcePath"
}

$source = Get-Content -LiteralPath $sourcePath -Raw
$lockField = 'private readonly object _reportSync = new object();'
$lockStatement = 'lock (_reportSync)'

if ($source.Contains($lockField)) {
    if (([regex]::Matches($source, [regex]::Escape($lockStatement))).Count -ne 3) {
        throw 'DS4 serialization appears partially applied. Refusing to continue.'
    }
    Write-Host 'Core_ViGEm DS4 serialization fix is already applied.'
    exit 0
}

$nl = if ($source.Contains("`r`n")) { "`r`n" } else { "`n" }

$fieldPattern = [regex]::Escape('private readonly DualShock4Report _report = new DualShock4Report();')
if (([regex]::Matches($source, $fieldPattern)).Count -ne 1) {
    throw 'Expected exactly one DS4 report field declaration.'
}
$source = [regex]::Replace(
    $source,
    $fieldPattern,
    'private readonly DualShock4Report _report = new DualShock4Report();' + $nl + '            ' + $lockField,
    1
)

$axisPattern = '(?ms)^            protected override void SetAxisState\(BindingDescriptor bindingDescriptor, int state\)\r?\n            \{\r?\n.*?^            \}\r?\n(?=\r?\n            protected override void SetButtonState)'
$buttonPattern = '(?ms)^            protected override void SetButtonState\(BindingDescriptor bindingDescriptor, int state\)\r?\n            \{\r?\n.*?^            \}\r?\n(?=\r?\n            protected override void SetPovState)'
$povPattern = '(?ms)^            protected override void SetPovState\(BindingDescriptor bindingDescriptor, int state\)\r?\n            \{\r?\n.*?^            \}\r?\n(?=\r?\n            private void SendReport)'

$axisReplacement = @(
    '            protected override void SetAxisState(BindingDescriptor bindingDescriptor, int state)',
    '            {',
    '                lock (_reportSync)',
    '                {',
    '                    var inputId = bindingDescriptor.Index;',
    '                    _report.SetAxis(AxisIndexes[inputId], (byte)((state + 32768) / 256));',
    '                    SendReport();',
    '                }',
    '            }'
) -join $nl

$buttonReplacement = @(
    '            protected override void SetButtonState(BindingDescriptor bindingDescriptor, int state)',
    '            {',
    '                lock (_reportSync)',
    '                {',
    '                    var inputId = bindingDescriptor.Index;',
    '                    if (inputId >= ButtonIndexes.Count)',
    '                    {',
    '                        _report.SetSpecialButtonState(SpecialButtonIndexes[inputId - ButtonIndexes.Count], state != 0);',
    '                    }',
    '                    else',
    '                    {',
    '                        _report.SetButtonState(ButtonIndexes[inputId], state != 0);',
    '                    }',
    '                    SendReport();',
    '                }',
    '            }'
) -join $nl

$povReplacement = @(
    '            protected override void SetPovState(BindingDescriptor bindingDescriptor, int state)',
    '            {',
    '                lock (_reportSync)',
    '                {',
    '                    var inputId = bindingDescriptor.Index;',
    '                    var mapping = IndexToVector[inputId];',
    '                    var axisState = _povAxisStates[mapping.Axis];',
    '                    var newState = state == 1 ? mapping.Direction : 0;',
    '                    if (axisState == newState) return;',
    '                    _povAxisStates[mapping.Axis] = newState;',
    '',
    '                    var buttons = (int)_report.Buttons;',
    '                    buttons &= ~15; // Clear all the Dpad bits',
    '',
    '                    buttons |= (int)AxisStatesToDpadValue[(_povAxisStates["x"], _povAxisStates["y"])];',
    '                    _report.Buttons = (ushort) buttons;',
    '                    SendReport();',
    '                }',
    '            }'
) -join $nl

$replacements = @(
    @{ Name = 'SetAxisState'; Pattern = $axisPattern; Replacement = $axisReplacement },
    @{ Name = 'SetButtonState'; Pattern = $buttonPattern; Replacement = $buttonReplacement },
    @{ Name = 'SetPovState'; Pattern = $povPattern; Replacement = $povReplacement }
)

foreach ($item in $replacements) {
    $count = ([regex]::Matches($source, $item.Pattern)).Count
    if ($count -ne 1) {
        throw "Expected exactly one $($item.Name) method body; found $count. Refusing a partial patch."
    }
    $source = [regex]::Replace($source, $item.Pattern, $item.Replacement, 1)
}

Set-Content -LiteralPath $sourcePath -Value $source -Encoding UTF8

$verified = Get-Content -LiteralPath $sourcePath -Raw
if (-not $verified.Contains($lockField)) {
    throw 'DS4 serialization verification failed: lock field missing.'
}
if (([regex]::Matches($verified, [regex]::Escape($lockStatement))).Count -ne 3) {
    throw 'DS4 serialization verification failed: expected exactly three serialized report mutation paths.'
}
if ($verified.Contains('Task.Factory.StartNew') -or $verified.Contains('Task.Run(')) {
    throw 'DS4 serialization verification failed: asynchronous task scheduling remains in DS4Handler.'
}

Write-Host 'Applied Core_ViGEm DS4 fix: report mutation and transmission are synchronous and serialized.'
