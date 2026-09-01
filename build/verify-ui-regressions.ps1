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
Require-Text $testProject '<Reference Include="WindowsBase" />' `
    'UCR.Tests WPF dependency closure is missing the explicit WindowsBase reference.'

foreach ($path in @('.\UCR\Views\Dialogs\DeviceManagerDialog.xaml', '.\UCR\Views\Dialogs\DeviceManagerPage.xaml')) {
    $text = Read-RepoText $path
    [xml]$null = $text
    Forbid-Regex $text '<ComboBox[^>]*(?:AvailableOutlineColors)|AvailableOutlineColors[^>]*</ComboBox>' `
        "$path regressed to the text-heavy outline colour ComboBox."
    Require-Text $text 'Style="{StaticResource DeviceOutlineSwatchList}"' `
        "$path is missing the compact visual outline swatches."
    Require-Text $text 'SelectedValue="{Binding OutlineColor, Mode=TwoWay}"' `
        "$path no longer binds the selected swatch to the persisted outline choice."
}

foreach ($path in @('.\UCR\Views\ProfileViews\ProfilePage.xaml', '.\UCR\Views\ProfileViews\ProfileWindow.xaml')) {
    $text = Read-RepoText $path
    [xml]$null = $text
    Forbid-Regex $text 'x:Name="SidebarScrollViewer"|MaxHeight="110"' `
        "$path has reintroduced the fixed-height/ScrollViewer layout that prevented Devices from filling available height."
    Require-Text $text 'x:Name="SidebarGrid"' "$path is missing the stretchable sidebar grid."
    Require-Text $text 'VerticalContentAlignment="Stretch"' "$path Devices expander is no longer stretching its content."
}

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

Write-Host 'Verified device-colour, sidebar-stretch, and appearance-cancel UI regression guardrails.'
