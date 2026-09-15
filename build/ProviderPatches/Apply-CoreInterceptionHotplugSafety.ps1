param(
    [Parameter(Mandatory = $true)]
    [string]$IOWrapperRoot
)

$ErrorActionPreference = 'Stop'

$corePath = Join-Path $IOWrapperRoot 'Source\Core Providers\Core_Interception\Core_Interception.cs'
$libraryPath = Join-Path $IOWrapperRoot 'Source\Core Providers\Core_Interception\DeviceLibrary\IceptDeviceLibrary.cs'

foreach ($path in @($corePath, $libraryPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required Core_Interception source not found: $path"
    }
}

$core = Get-Content -LiteralPath $corePath -Raw
$nl = if ($core.Contains("`r`n")) { "`r`n" } else { "`n" }

$refreshMarker = 'Serialize hotplug re-enumeration against the multimedia poll callback.'
if (-not $core.Contains($refreshMarker)) {
    $refreshPattern = '(?ms)^        public void RefreshDevices\(\)\r?\n        \{\r?\n            _deviceLibrary\.RefreshConnectedDevices\(\);\r?\n        \}'
    if (([regex]::Matches($core, $refreshPattern)).Count -ne 1) {
        throw 'Expected exactly one Core_Interception.RefreshDevices method body.'
    }

    $refreshReplacement = @(
        '        public void RefreshDevices()',
        '        {',
        '            lock (_lockObj)',
        '            {',
        '                // Serialize hotplug re-enumeration against the multimedia poll callback.',
        '                SetPollThreadState(false);',
        '                try',
        '                {',
        '                    _deviceLibrary.RefreshConnectedDevices();',
        '                }',
        '                finally',
        '                {',
        '                    StartPollingIfNeeded();',
        '                }',
        '            }',
        '        }'
    ) -join $nl

    $core = [regex]::Replace($core, $refreshPattern, $refreshReplacement, 1)
}

$providerLogMarker = 'UCR-provider-errors.log'
if (-not $core.Contains($providerLogMarker)) {
    $anchor = '        #region PollThread'
    $anchorIndex = $core.IndexOf($anchor, [System.StringComparison]::Ordinal)
    if ($anchorIndex -lt 0) {
        throw 'Could not find Core_Interception PollThread insertion point.'
    }

    $providerLogger = @(
        '        private static void LogProviderException(string message, Exception exception)',
        '        {',
        '            HelperFunctions.Log("{0}: {1}", message, exception);',
        '',
        '            var directories = new[]',
        '            {',
        '                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs"),',
        '                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HidWizards", "UCR", "logs")',
        '            };',
        '',
        '            foreach (var directory in directories)',
        '            {',
        '                try',
        '                {',
        '                    Directory.CreateDirectory(directory);',
        '                    var line = DateTime.Now.ToString("O") + " " + message + Environment.NewLine +',
        '                               exception + Environment.NewLine + Environment.NewLine;',
        '                    File.AppendAllText(Path.Combine(directory, "UCR-provider-errors.log"), line);',
        '                    return;',
        '                }',
        '                catch',
        '                {',
        '                    // Try the next diagnostics location. Logging must never create a second failure.',
        '                }',
        '            }',
        '        }',
        '',
        ''
    ) -join $nl

    $core = $core.Insert($anchorIndex, $providerLogger)
}

$pollMarker = 'Core_Interception poll callback failed'
if (-not $core.Contains($pollMarker)) {
    $pollPattern = '(?ms)^        private void DoPoll\(object sender, EventArgs e\)\r?\n        \{\r?\n.*?^        \}\r?\n(?=\r?\n        /\*)'
    if (([regex]::Matches($core, $pollPattern)).Count -ne 1) {
        throw 'Expected exactly one Core_Interception.DoPoll method body.'
    }

    $pollReplacement = @(
        '        private void DoPoll(object sender, EventArgs e)',
        '        {',
        '            _pollThreadRunning = true;',
        '            try',
        '            {',
        '                var stroke = new ManagedWrapper.Stroke();',
        '                // Process Keyboard input',
        '                for (var i = 1; i < 11; i++)',
        '                {',
        '                    var isMonitoredKeyboard = _monitoredKeyboards.ContainsKey(i);',
        '',
        '                    while (ManagedWrapper.Receive(_deviceContext, i, ref stroke, 1) > 0)',
        '                    {',
        '                        if (isMonitoredKeyboard)',
        '                        {',
        '                            var blockingRequestedByUi = _monitoredKeyboards[i].ProcessUpdate(stroke);',
        '',
        '                            if (_blockingEnabled && blockingRequestedByUi)',
        '                            {',
        '                                continue;',
        '                            }',
        '                        }',
        '',
        '                        if (_fireStrokeOnThread)',
        '                        {',
        '                            var threadStroke = stroke;',
        '                            var deviceId = i;',
        '                            ThreadPool.QueueUserWorkItem(cb => ManagedWrapper.Send(_deviceContext, deviceId, ref threadStroke, 1));',
        '                        }',
        '                        else',
        '                        {',
        '                            ManagedWrapper.Send(_deviceContext, i, ref stroke, 1);',
        '                        }',
        '                    }',
        '                }',
        '',
        '                // Process Mouse input',
        '                for (var i = 11; i < 21; i++)',
        '                {',
        '                    var isMonitoredMouse = _monitoredMice.ContainsKey(i);',
        '',
        '                    while (ManagedWrapper.Receive(_deviceContext, i, ref stroke, 1) > 0)',
        '                    {',
        '                        if (isMonitoredMouse)',
        '                        {',
        '                            stroke = _monitoredMice[i].ProcessUpdate(stroke);',
        '                            if (stroke.mouse.x == 0 && stroke.mouse.y == 0 && stroke.mouse.state == 0) continue;',
        '                        }',
        '',
        '                        if (_fireStrokeOnThread)',
        '                        {',
        '                            var threadStroke = stroke;',
        '                            var deviceId = i;',
        '                            ThreadPool.QueueUserWorkItem(cb => ManagedWrapper.Send(_deviceContext, deviceId, ref threadStroke, 1));',
        '                        }',
        '                        else',
        '                        {',
        '                            ManagedWrapper.Send(_deviceContext, i, ref stroke, 1);',
        '                        }',
        '                    }',
        '                }',
        '            }',
        '            catch (Exception exception)',
        '            {',
        '                // Exceptions escaping a winmm callback can terminate UCR before WPF/AppDomain handlers run.',
        '                LogProviderException("Core_Interception poll callback failed", exception);',
        '            }',
        '            finally',
        '            {',
        '                _pollThreadRunning = false;',
        '            }',
        '        }'
    ) -join $nl

    $core = [regex]::Replace($core, $pollPattern, $pollReplacement, 1)
}

Set-Content -LiteralPath $corePath -Value $core -Encoding UTF8

$library = Get-Content -LiteralPath $libraryPath -Raw
$badBounds = 'if (_deviceHandleToId[deviceDescriptor.DeviceHandle].Count >= deviceDescriptor.DeviceInstance)'
$goodBoundsCs = 'if (deviceDescriptor.DeviceInstance >= 0 && _deviceHandleToId[deviceDescriptor.DeviceHandle].Count > deviceDescriptor.DeviceInstance)'

if ($library.Contains($badBounds)) {
    if (([regex]::Matches($library, [regex]::Escape($badBounds))).Count -ne 1) {
        throw 'Expected exactly one Core_Interception device-instance bounds check.'
    }
    $library = $library.Replace($badBounds, $goodBoundsCs)
    Set-Content -LiteralPath $libraryPath -Value $library -Encoding UTF8
}
elseif ($library.Contains($goodBoundsCs)) {
    Write-Host 'Core_Interception device-instance bounds fix is already applied.'
}
else {
    throw 'Core_Interception device-instance bounds check did not match the pinned source or the fixed form.'
}

$verifiedCore = Get-Content -LiteralPath $corePath -Raw
$verifiedLibrary = Get-Content -LiteralPath $libraryPath -Raw

if (-not $verifiedCore.Contains($refreshMarker)) {
    throw 'Core_Interception hotplug patch verification failed: synchronized refresh marker missing.'
}
if (-not $verifiedCore.Contains($pollMarker) -or -not $verifiedCore.Contains($providerLogMarker)) {
    throw 'Core_Interception hotplug patch verification failed: durable callback diagnostics missing.'
}
if (-not $verifiedCore.Contains('finally')) {
    throw 'Core_Interception hotplug patch verification failed: poll finally barrier missing.'
}
if (-not $verifiedLibrary.Contains($goodBoundsCs) -or $verifiedLibrary.Contains($badBounds)) {
    throw 'Core_Interception hotplug patch verification failed: instance bounds check is not fixed.'
}

Write-Host 'Applied Core_Interception hotplug safety: synchronized refresh, durable callback diagnostics, exception barrier, and instance bounds fix.'
