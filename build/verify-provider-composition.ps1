param(
    [Parameter(Mandatory = $true)]
    [string]$RuntimePath
)

$ErrorActionPreference = 'Stop'

$runtime = (Resolve-Path -LiteralPath $RuntimePath).Path
Push-Location $runtime
$controller = $null
try {
    # Load the exact assemblies from the deployed runtime, then let IOController perform the same
    # MEF provider discovery UCR performs at startup from .\Providers.
    [void][System.Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath '.\lib\IOWrapper.DTOs.dll').Path)
    [void][System.Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath '.\lib\IOWrapper.IProvider.dll').Path)
    $coreAssembly = [System.Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath '.\lib\IOWrapper.Core.dll').Path)
    $controllerType = $coreAssembly.GetType('HidWizards.IOWrapper.Core.IOController', $true)
    $controller = [System.Activator]::CreateInstance($controllerType)

    $inputReports = $controller.GetInputList()
    $outputReports = $controller.GetOutputList()
    $inputProviders = @($inputReports.Keys)
    $outputProviders = @($outputReports.Keys)

    Write-Host "Composed input providers:  $($inputProviders -join ', ')"
    Write-Host "Composed output providers: $($outputProviders -join ', ')"

    if ($inputProviders -notcontains 'Core_Interception') {
        throw 'Final runtime could not compose Core_Interception through IOWrapper.'
    }
    if ($outputProviders -notcontains 'Core_ViGEm') {
        throw 'Final runtime could not compose Core_ViGEm through IOWrapper.'
    }
}
finally {
    if ($null -ne $controller) {
        $controller.Dispose()
    }
    Pop-Location
}

Write-Host 'Verified final deployed IOWrapper can compose Core_Interception and Core_ViGEm.'
