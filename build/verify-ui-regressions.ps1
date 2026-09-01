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
$testProject = Read-RepoText '.\UCR.Tests\UCR.Tests.csproj'

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
Require-Text $uiSmokeTest 'DeviceManagerPageMaterializesARowWhenItemsSourceHasADevice' `
    'The Devices-page runtime smoke test has been removed or renamed unexpectedly.'
Require-Text $uiSmokeTest '[Apartment(ApartmentState.STA)]' `
    'The WPF Devices-page smoke test must run on an STA thread.'

$appXaml = Read-RepoText '.\UCR\App.xaml'
[xml]$null = $appXaml
Require-Text $appXaml 'x:Key="DeviceOutlineButton"' `
    'The device outline selector must be one compact current-colour button, not an always-visible palette.'
Require-Text $appXaml 'x:Key="DeviceOutlineMenuItem"' `
    'The compact device outline popup is missing its swatch item style.'
Forbid-Regex $appXaml 'x:Key="DeviceOutlinePicker"|DeviceOutlineSwatchList|DeviceOutlineSwatchItem|TargetType="\{x:Type ComboBoxItem\}"' `
    'The custom ComboBox outline picker must not return to the device-row rendering path.'

Require-Text $deviceManagerVm 'Devices = new ObservableCollection<DeviceManagerItemViewModel>();' `
    'Device Manager collection initialization is missing.'
Require-Text $deviceManagerVm '            Refresh();' `
    'Device Manager must refresh live providers before its first population; otherwise the Devices page can open blank.'
Require-Text $deviceManagerVm '_devicesManager.RefreshDeviceList();' `
    'Device Manager no longer refreshes the live provider list before populating.'
Require-Text $deviceManagerVm 'public Brush CurrentOutlineBrush' `
    'The compact colour button no longer exposes the currently selected outline brush.'

foreach ($path in @('.\UCR\Views\Dialogs\DeviceManagerDialog.xaml', '.\UCR\Views\Dialogs\DeviceManagerPage.xaml')) {
    $text = Read-RepoText $path
    [xml]$null = $text
    Forbid-Regex $text '<ComboBox[^>]+AvailableOutlineColors|DeviceOutlineSwatchList|DeviceOutlinePicker' `
        "$path must not put a ComboBox or permanent palette back into every device row."
    Require-Text $text 'Style="{StaticResource DeviceOutlineButton}"' `
        "$path is missing the compact current-colour button."
    Require-Text $text 'Background="{Binding CurrentOutlineBrush}"' `
        "$path no longer shows the currently selected outline colour on the closed button."
    Require-Text $text 'ItemsSource="{Binding PlacementTarget.DataContext.AvailableOutlineColors, RelativeSource={RelativeSource Self}}"' `
        "$path is missing the on-demand swatch popup."
    Require-Text $text 'Background="#252525"' `
        "$path colour popup must use an opaque dark surface."
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

Write-Host 'Verified live-refreshing Devices page, compact on-demand colour menu, top-aligned Devices layout, rebuilt dark device configuration, and appearance-cancel UI guardrails.'
