param(
    [Parameter(Mandatory = $true)]
    [string]$RuntimePath
)

$ErrorActionPreference = 'Stop'

$runtime = (Resolve-Path -LiteralPath $RuntimePath).Path
$providersPath = Join-Path $runtime 'Providers'
if (-not (Test-Path -LiteralPath $providersPath -PathType Container)) {
    throw "Final runtime Providers directory is missing: $providersPath"
}

# PowerShell's location stack is not guaranteed to update the process-wide .NET current directory.
# IOWrapper resolves .\Providers through System.IO, so reproduce UCR startup exactly by setting
# the process current directory before constructing IOController.
$previousCurrentDirectory = [System.Environment]::CurrentDirectory
Push-Location $runtime
$controller = $null
try {
    [System.IO.Directory]::SetCurrentDirectory($runtime)

    # Load the exact assemblies from the deployed runtime, then let IOController perform the same
    # MEF provider discovery UCR performs at startup from .\Providers.
    [void][System.Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath '.\lib\IOWrapper.DTOs.dll').Path)
    [void][System.Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath '.\lib\IOWrapper.IProvider.dll').Path)
    $coreAssembly = [System.Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath '.\lib\IOWrapper.Core.dll').Path)
    $controllerType = $coreAssembly.GetType('HidWizards.IOWrapper.Core.IOController', $true)
    $controller = [System.Activator]::CreateInstance($controllerType)

    # Composition is the CI contract here; hardware enumeration is not. A clean GitHub runner
    # does not have the user's device drivers/hardware, so inspect the providers IOController
    # successfully constructed instead of calling GetInputList/GetOutputList.
    $flags = [System.Reflection.BindingFlags]::Instance -bor [System.Reflection.BindingFlags]::NonPublic
    $providersField = $controllerType.GetField('_providers', $flags)
    if ($null -eq $providersField) {
        throw 'Unable to inspect IOWrapper provider composition: _providers field was not found.'
    }

    $providers = $providersField.GetValue($controller)
    $providerNames = @($providers.Keys)
    Write-Host "Composed providers: $($providerNames -join ', ')"

    if ($providerNames -notcontains 'Core_Interception') {
        throw 'Final runtime could not compose Core_Interception through IOWrapper.'
    }
    if ($providerNames -notcontains 'Core_ViGEm') {
        throw 'Final runtime could not compose Core_ViGEm through IOWrapper.'
    }
}
finally {
    if ($null -ne $controller) {
        $controller.Dispose()
    }
    [System.IO.Directory]::SetCurrentDirectory($previousCurrentDirectory)
    Pop-Location
}

Write-Host 'Verified final deployed IOWrapper can compose Core_Interception and Core_ViGEm.'
