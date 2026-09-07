param(
    [Parameter(Mandatory = $true)]
    [string]$IOWrapperRoot
)

$ErrorActionPreference = 'Stop'
$path = Join-Path $IOWrapperRoot 'Source\Core Providers\Core_ViGEm\DS4Handler.cs'
if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
    throw "Core_ViGEm DS4 source not found: $path"
}

$source = (Get-Content -LiteralPath $path -Raw).Replace("`r`n", "`n")
$lockField = '            private readonly object _reportSync = new object();'

if (-not $source.Contains($lockField)) {
    $fieldAnchor = '            private readonly DualShock4Report _report = new DualShock4Report();'
    if (-not $source.Contains($fieldAnchor)) {
        throw 'Expected DS4 report field was not found. Refusing to patch an unexpected provider source.'
    }
    $source = $source.Replace($fieldAnchor, $fieldAnchor + "`n" + $lockField)
}

$replacements = @(
    @{
        Old = @'
            protected override void SetAxisState(BindingDescriptor bindingDescriptor, int state)
            {
                var inputId = bindingDescriptor.Index;
                _report.SetAxis(AxisIndexes[inputId], (byte)((state + 32768) / 256));
                SendReport();
            }
'@
        New = @'
            protected override void SetAxisState(BindingDescriptor bindingDescriptor, int state)
            {
                lock (_reportSync)
                {
                    var inputId = bindingDescriptor.Index;
                    _report.SetAxis(AxisIndexes[inputId], (byte)((state + 32768) / 256));
                    SendReport();
                }
            }
'@
    },
    @{
        Old = @'
            protected override void SetButtonState(BindingDescriptor bindingDescriptor, int state)
            {
                var inputId = bindingDescriptor.Index;
                if (inputId >= ButtonIndexes.Count)
                {
                    _report.SetSpecialButtonState(SpecialButtonIndexes[inputId - ButtonIndexes.Count], state != 0);
                }
                else
                {
                    _report.SetButtonState(ButtonIndexes[inputId], state != 0);
                }
                SendReport();
            }
'@
        New = @'
            protected override void SetButtonState(BindingDescriptor bindingDescriptor, int state)
            {
                lock (_reportSync)
                {
                    var inputId = bindingDescriptor.Index;
                    if (inputId >= ButtonIndexes.Count)
                    {
                        _report.SetSpecialButtonState(SpecialButtonIndexes[inputId - ButtonIndexes.Count], state != 0);
                    }
                    else
                    {
                        _report.SetButtonState(ButtonIndexes[inputId], state != 0);
                    }
                    SendReport();
                }
            }
'@
    },
    @{
        Old = @'
            protected override void SetPovState(BindingDescriptor bindingDescriptor, int state)
            {
                var inputId = bindingDescriptor.Index;
                var mapping = IndexToVector[inputId];
                var axisState = _povAxisStates[mapping.Axis];
                var newState = state == 1 ? mapping.Direction : 0;
                if (axisState == newState) return;
                _povAxisStates[mapping.Axis] = newState;

                var buttons = (int)_report.Buttons;
                buttons &= ~15; // Clear all the Dpad bits

                buttons |= (int)AxisStatesToDpadValue[(_povAxisStates["x"], _povAxisStates["y"])];
                _report.Buttons = (ushort) buttons;
                SendReport();
            }
'@
        New = @'
            protected override void SetPovState(BindingDescriptor bindingDescriptor, int state)
            {
                lock (_reportSync)
                {
                    var inputId = bindingDescriptor.Index;
                    var mapping = IndexToVector[inputId];
                    var axisState = _povAxisStates[mapping.Axis];
                    var newState = state == 1 ? mapping.Direction : 0;
                    if (axisState == newState) return;
                    _povAxisStates[mapping.Axis] = newState;

                    var buttons = (int)_report.Buttons;
                    buttons &= ~15; // Clear all the Dpad bits

                    buttons |= (int)AxisStatesToDpadValue[(_povAxisStates["x"], _povAxisStates["y"])];
                    _report.Buttons = (ushort) buttons;
                    SendReport();
                }
            }
'@
    }
)

foreach ($replacement in $replacements) {
    $oldText = $replacement.Old.Replace("`r`n", "`n")
    $newText = $replacement.New.Replace("`r`n", "`n")
    if ($source.Contains($newText)) {
        continue
    }
    if (-not $source.Contains($oldText)) {
        throw 'Expected DS4 handler method body was not found. Refusing to apply a partial patch.'
    }
    $source = $source.Replace($oldText, $newText)
}

Set-Content -LiteralPath $path -Value $source -Encoding UTF8
$verified = Get-Content -LiteralPath $path -Raw

if (-not $verified.Contains($lockField)) {
    throw 'DS4 serialization patch verification failed: lock field missing.'
}
if (([regex]::Matches($verified, [regex]::Escape('lock (_reportSync)'))).Count -ne 3) {
    throw 'DS4 serialization patch verification failed: expected exactly three serialized report mutation paths.'
}
if ($verified.Contains('Task.Factory.StartNew') -or $verified.Contains('Task.Run(')) {
    throw 'DS4 serialization patch verification failed: asynchronous task scheduling remains in DS4Handler.'
}

Write-Host 'Applied Core_ViGEm DS4 fix: report mutation and transmission are synchronous and serialized.'
