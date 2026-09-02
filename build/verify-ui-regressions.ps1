$ErrorActionPreference = 'Stop'

function Read-RepoText([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Required UI regression file is missing: $Path"
    }
    return Get-Content -LiteralPath $Path -Raw
}

function Require-Text([string]$Text, [string]$Needle, [string]$Message) {
    if (-not $Text.Contains($Needle)) { throw $Message }
}

function Forbid-Regex([string]$Text, [string]$Pattern, [string]$Message) {
    if ($Text -match $Pattern) { throw $Message }
}

$deviceModel = Read-RepoText '.\UCR.Core\Models\Device.cs'
$deviceVisuals = Read-RepoText '.\UCR\ViewModels\Presentation\DeviceVisualDescriptor.cs'
$deviceManagerVm = Read-RepoText '.\UCR\ViewModels\Dashboard\DeviceManagerViewModel.cs'
$devicesManager = Read-RepoText '.\UCR.Core\Managers\DevicesManager.cs'
$appCode = Read-RepoText '.\UCR\App.xaml.cs'
$runtimePathManager = Read-RepoText '.\UCR.Core\Utilities\RuntimePathManager.cs'
$testProject = Read-RepoText '.\UCR.Tests\UCR.Tests.csproj'

$providerCompositionSmoke = Read-RepoText '.\build\verify-provider-composition.ps1'
Require-Text $providerCompositionSmoke "Core_Interception" `
    'Provider-composition smoke test no longer requires Core_Interception.'
Require-Text $providerCompositionSmoke "Core_ViGEm" `
    'Provider-composition smoke test no longer requires Core_ViGEm.'

Forbid-Regex $deviceModel 'GenerateUniqueDefault|StableHash|HslToHex' `
    'Generated/random default device colours must not return. Default means the original semantic device colour.'
Forbid-Regex $deviceManagerVm 'AssignUniqueDefaultOutlineColors|GetDeviceDefaultOutlineColor' `
    'Device Manager must not manufacture per-device default colours.'
Require-Text $deviceVisuals 'if (choice == DeviceOutlineColor.Default) return;' `
    'Default outline no longer explicitly preserves the original semantic device colour.'
Require-Text $deviceVisuals 'descriptor.OutlineBrush = brush;' `
    'User outline overrides are no longer isolated to OutlineBrush.'
Require-Text $testProject '<Reference Include="PresentationCore" />' `
    'UCR.Tests uses WPF Brush-backed visual descriptors but is missing its explicit PresentationCore reference.'
Require-Text $testProject '<Reference Include="PresentationFramework" />' `
    'UCR.Tests UI smoke test is missing its explicit PresentationFramework reference.'
Require-Text $testProject '<Reference Include="WindowsBase" />' `
    'UCR.Tests WPF dependency closure is missing the explicit WindowsBase reference.'

$uiSmokeTest = Read-RepoText '.\UCR.Tests\UiTests\DeviceManagerPageSmokeTests.cs'
Require-Text $testProject '<Compile Include="UiTests\DeviceManagerPageSmokeTests.cs" />' `
    'The real WPF Devices-page row-materialization smoke test is no longer compiled.'
Require-Text $uiSmokeTest 'DeviceManagerPageMaterializesRealDeviceRowAndColourButton' `
    'The Devices-page runtime smoke test no longer exercises a real DeviceManagerItemViewModel.'
Require-Text $uiSmokeTest 'new DeviceManagerItemViewModel' `
    'The Devices-page smoke test has regressed to a fake row object and can miss real binding/template failures.'
Require-Text $uiSmokeTest '[Apartment(ApartmentState.STA)]' `
    'The WPF Devices-page smoke test must run on an STA thread.'
Require-Text $uiSmokeTest 'AvailableOutlineColors.Length, Is.EqualTo(10)' `
    'The WPF smoke test no longer proves the real row has its full outline-colour model.'

$appXaml = Read-RepoText '.\UCR\App.xaml'
[xml]$null = $appXaml
Forbid-Regex $appXaml 'DeviceOutlinePicker|DeviceOutlineSwatchList|DeviceOutlineSwatchItem|DeviceOutlineButton|DeviceOutlineMenuItem' `
    'Device outline picker templates must not live in Application resources; a picker failure must not block row materialisation.'

Require-Text $deviceManagerVm 'Devices = new ObservableCollection<DeviceManagerItemViewModel>();' `
    'Device Manager collection initialization is missing.'
Require-Text $deviceManagerVm '            Populate();' `
    'Device Manager must populate from the already-enumerated provider state on construction.'
Forbid-Regex $deviceManagerVm 'Devices = new ObservableCollection<DeviceManagerItemViewModel>\(\);\s*Refresh\(\);' `
    'Do not eagerly refresh providers while constructing Devices; that regression produced an empty real device list.'
Require-Text $deviceManagerVm '_devicesManager.RefreshDeviceList();' `
    'Explicit Device Manager refresh no longer refreshes the provider list when actually requested.'
Require-Text $deviceManagerVm 'public Brush CurrentOutlineBrush' `
    'The compact colour button no longer exposes the currently selected outline brush.'

Require-Text $devicesManager 'public List<Device> GetManagementDeviceList(DeviceIoType type)' `
    'The Devices page has lost its management-specific resilient inventory.'
Require-Text $devicesManager 'var configured = GetConfiguredManagementDevices(type).ToList();' `
    'Persisted profile devices are no longer collected for the management fallback.'
Require-Text $devicesManager 'AddManagementCandidates(candidates, configured);' `
    'Persisted profile devices are no longer merged when live enumeration fails.'
Require-Text $devicesManager 'var cached = LoadAllDeviceCaches().ToList();' `
    'Persisted input cache devices are no longer collected for the management fallback.'
Require-Text $devicesManager 'AddManagementCandidates(candidates, cached);' `
    'Persisted input cache devices are no longer merged into the management fallback.'
Require-Text $deviceManagerVm '_devicesManager.GetManagementDeviceList(DeviceIoType.Input)' `
    'Device Manager no longer computes removed-input state from the resilient management inventory.'
Require-Text $deviceManagerVm '_devicesManager.GetManagementDeviceList(type)' `
    'Device Manager rows no longer come from the resilient management inventory.'
Require-Text $devicesManager 'public bool HasLoadedProviderReports()' `
    'Runtime health can no longer distinguish mapping-plugin success from missing IOWrapper providers.'
Require-Text $appCode 'RuntimePathManager.NormalizeWorkingDirectory();' `
    'UCR must normalize relative Providers/Plugins/Cache/context paths to the executable directory at startup.'
Require-Text $runtimePathManager 'Directory.SetCurrentDirectory(applicationDirectory);' `
    'Runtime path normalization no longer anchors the process working directory to the executable directory.'
Require-Text $appCode 'context.DevicesManager.HasLoadedProviderReports()' `
    'Blocked-DLL recovery no longer validates IOWrapper device-provider availability.'

$normalizeIndex = $appCode.IndexOf('RuntimePathManager.NormalizeWorkingDirectory();', [System.StringComparison]::Ordinal)
$loggerIndex = $appCode.IndexOf('Logger.InitializeSession();', [System.StringComparison]::Ordinal)
if ($normalizeIndex -lt 0 -or $loggerIndex -lt 0 -or $normalizeIndex -gt $loggerIndex) {
    throw 'Runtime working-directory normalization must happen before logging and all provider/plugin/context initialization.'
}

$inventoryTests = Read-RepoText '.\UCR.Tests\ModelTests\DeviceManagementInventoryTests.cs'
Require-Text $testProject '<Compile Include="ModelTests\DeviceManagementInventoryTests.cs" />' `
    'The resilient management-inventory regression tests are no longer compiled.'
foreach ($testName in @(
    'ManagementInventoryKeepsConfiguredDevicesWhenIoControllerIsUnavailable',
    'ManagementInventoryIncludesNestedProfileAndShadowDevicesWithoutDuplicatingPrimaryDevice',
    'ManagementInventoryKeepsDistinctLogicalOutputSlots',
    'DeviceManagerViewModelUsesConfiguredFallbackWhenIoControllerIsUnavailable',
    'DeviceManagerViewModelExplainsEmptyInventoryWhenProvidersUnavailable',
    'ProviderHealthCheckReportsUnavailableControllerInsteadOfPretendingRuntimeIsHealthy',
    'RuntimePathManagerNormalizesRelativeRuntimePathsToExecutableDirectory'
)) {
    Require-Text $inventoryTests $testName "Required Devices-management regression test is missing: $testName"
}

foreach ($path in @('.\UCR\Views\Dialogs\DeviceManagerDialog.xaml', '.\UCR\Views\Dialogs\DeviceManagerPage.xaml')) {
    $text = Read-RepoText $path
    [xml]$null = $text
    Forbid-Regex $text '<ComboBox[^>]+AvailableOutlineColors|DeviceOutlineSwatchList|DeviceOutlinePicker|<Button.ContextMenu>|<ListBox[^>]+AvailableOutlineColors' `
        "$path must not put picker infrastructure or a permanent palette into every device row."
    Require-Text $text 'Click="OutlineColorButton_OnClick"' `
        "$path is missing the compact current-colour button."
    Require-Text $text 'Background="{Binding CurrentOutlineBrush}"' `
        "$path no longer shows the currently selected outline colour on the closed button."
}

foreach ($path in @('.\UCR\Views\Dialogs\DeviceManagerDialog.xaml.cs', '.\UCR\Views\Dialogs\DeviceManagerPage.xaml.cs')) {
    $text = Read-RepoText $path
    Require-Text $text 'new System.Windows.Controls.Primitives.Popup' `
        "$path must build the outline palette only after the user clicks the colour button."
    Require-Text $text 'device.AvailableOutlineColors' `
        "$path no longer builds the requested ten-colour palette from the device model."
    Require-Text $text 'StaysOpen = false' `
        "$path outline palette must close on click-away."
}

foreach ($path in @('.\UCR\Views\ProfileViews\ProfilePage.xaml', '.\UCR\Views\ProfileViews\ProfileWindow.xaml')) {
    $text = Read-RepoText $path
    [xml]$null = $text
    Forbid-Regex $text 'x:Name="SidebarScrollViewer"|MaxHeight="110"' `
        "$path has reintroduced the old fixed-height sidebar layout."
    Forbid-Regex $text '<RowDefinition Height="Auto"\s*/>\s*<RowDefinition Height="\*"\s*/>\s*<RowDefinition Height="Auto"\s*/>\s*<RowDefinition Height="\*"\s*/>' `
        "$path is again distributing spare Devices-panel height between INPUT and OUTPUT."
    Require-Text $text 'x:Name="ProfileDevicesScrollViewer"' `
        "$path must keep INPUT/OUTPUT content top-aligned in a single scrolling surface."
    Require-Text $text 'VerticalContentAlignment="Stretch"' `
        "$path Devices expander must give the single scroll surface the available panel height."
}

$deviceConfigDialog = Read-RepoText '.\UCR\Views\Dialogs\ManageDeviceConfigurationDialog.xaml'
$deviceAddRemove = Read-RepoText '.\UCR\Views\Controls\DeviceAddRemoveControl.xaml'
[xml]$null = $deviceConfigDialog
[xml]$null = $deviceAddRemove
Require-Text $deviceConfigDialog 'x:Name="DeviceConfigurationShell"' `
    'Device configuration must use the rebuilt dark UCR shell.'
Forbid-Regex $deviceConfigDialog 'MaterialDesignPaper|MaterialDesignFloatingHintTextBox' `
    'Legacy light MaterialDesign surfaces must not return to Device Configuration.'
Require-Text $deviceAddRemove 'x:Name="AvailableDevicesPanel"' `
    'The rebuilt shadow-device selector is missing its available-devices panel.'
Require-Text $deviceAddRemove 'x:Name="SelectedDevicesPanel"' `
    'The rebuilt shadow-device selector is missing its selected-devices panel.'
Forbid-Regex $deviceAddRemove 'MaterialDesignCardGroupBox|<GroupBox' `
    'The legacy light card/group-box device selector must not return.'

$mainWindowXaml = Read-RepoText '.\UCR\Views\MainWindow.xaml'
$mainWindowCode = Read-RepoText '.\UCR\Views\MainWindow.xaml.cs'
$appearanceCode = Read-RepoText '.\UCR\Views\Dialogs\AppearanceDialog.xaml.cs'
[xml]$null = $mainWindowXaml
Require-Text $mainWindowXaml 'x:Name="AppearancePopup"' 'The main appearance picker is no longer a lightweight popup.'
Require-Text $mainWindowXaml 'StaysOpen="False"' 'Click-away cancellation for the appearance picker is not enabled.'
Forbid-Regex $mainWindowCode 'DialogHost\.Show\(new AppearanceDialog' `
    'The old modal appearance DialogHost flow has returned.'
Require-Text $appearanceCode 'e.Key == Key.Escape' 'Escape cancellation is missing from the appearance picker.'
Require-Text $appearanceCode 'CancelRequested?.Invoke' 'Escape no longer closes the appearance picker through its cancel path.'

Write-Host 'Verified resilient Devices inventory/provider health, executable-root runtime paths, population-first Devices page, real-row WPF smoke coverage, isolated on-demand colour popup, top-aligned Devices layout, rebuilt dark device configuration, and appearance-cancel UI guardrails.'
