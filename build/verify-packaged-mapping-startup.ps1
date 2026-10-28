param(
    [Parameter(Mandatory = $true)][string]$RuntimePath
)
$ErrorActionPreference = 'Stop'
$runtime = (Resolve-Path -LiteralPath $RuntimePath).Path
$pluginDll = Join-Path $runtime 'Plugins\UCR.Plugins\UCR.Plugins.dll'
$coreDll = Join-Path $runtime 'lib\UCR.Core.dll'
if (-not (Test-Path -LiteralPath $pluginDll -PathType Leaf)) {
    throw "Release build is missing the mandatory plugin DLL: $pluginDll"
}
if (-not (Test-Path -LiteralPath $coreDll -PathType Leaf)) {
    throw "Release build is missing UCR.Core.dll: $coreDll"
}

# Run in Windows PowerShell 5.1, not PowerShell 7: UCR targets .NET Framework.
# Register the library directory for any transitive dependencies.
$handler = [System.ResolveEventHandler]{
    param($sender, $args)
    $name = (New-Object System.Reflection.AssemblyName($args.Name)).Name + '.dll'
    $candidate = Join-Path (Join-Path $runtime 'lib') $name
    if (Test-Path -LiteralPath $candidate) {
        return [System.Reflection.Assembly]::LoadFrom($candidate)
    }
    return $null
}
[AppDomain]::CurrentDomain.add_AssemblyResolve($handler)
try {
    [void][System.Reflection.Assembly]::LoadFrom($coreDll)
    [void][System.Reflection.Assembly]::LoadFrom($pluginDll)
    $manager = New-Object -TypeName HidWizards.UCR.Core.Managers.PluginsManager -ArgumentList (Join-Path $runtime 'Plugins')
    $button = @($manager.Plugins | Where-Object {
        $_.GetType().FullName -eq 'HidWizards.UCR.Plugins.Remapper.ButtonToButton'
    } | Select-Object -First 1)
    if ($button.Count -ne 1) {
        throw 'The packaged mapping plugin DLL was present but ButtonToButton could not be registered.'
    }
    Write-Host "Packaged mapping plugin imported: $($button[0].GetType().FullName)"

    # Materialise an ordinary profile through the production persistence path.
    # The following actual UCR.exe launch must load this populated profile,
    # not an empty virgin Documents directory.
    $context = New-Object HidWizards.UCR.Core.Context
    $profile = $context.ProfilesManager.CreateProfile(
        'CI persisted profile', $null, $null)
    [void]$context.ProfilesManager.AddProfile($profile)
    $mapping = $profile.AddMapping('Saved ButtonToButton mapping')
    [void]$profile.AddPlugin($mapping, $button[0])
    $types = New-Object 'System.Collections.Generic.List[System.Type]'
    $types.Add($button[0].GetType())
    [void]$context.SaveContext($types)
    $file = Join-Path (Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'UCR\Profiles') ($profile.Guid.ToString('D') + '.json')
    if (-not (Test-Path -LiteralPath $file)) {
        throw "Persisted startup profile was not written: $file"
    }
    if (-not (Select-String -LiteralPath $file -Pattern 'HidWizards.UCR.Plugins.Remapper.ButtonToButton' -Quiet)) {
        throw 'Persisted startup profile does not contain the required plugin type.'
    }
    Write-Host "Persisted profile fixture ready for real UCR.exe startup: $file"
}
finally {
    [AppDomain]::CurrentDomain.remove_AssemblyResolve($handler)
}
