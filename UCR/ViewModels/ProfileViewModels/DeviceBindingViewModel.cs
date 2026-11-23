using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using HidWizards.UCR.Core;
using HidWizards.UCR.Core.Annotations;
using HidWizards.UCR.Core.Managers;
using HidWizards.UCR.Core.Models;
using HidWizards.UCR.Core.Models.Binding;
using HidWizards.UCR.Core.Utilities;
using HidWizards.UCR.ViewModels.Presentation;

namespace HidWizards.UCR.ViewModels.ProfileViewModels
{
    public class DeviceBindingViewModel : INotifyPropertyChanged, IDisposable
    {
        // A UI-only selector choice; never a physical device configuration ID.
        public static readonly Guid FilterOutputChoiceGuid =
            new Guid("FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF");
        private string _deviceBindingName;
        public string DeviceBindingName
        {
            get => _deviceBindingName;
            set
            {
                if (_deviceBindingName == value) return;
                _deviceBindingName = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DirectionalGlyph));
                OnPropertyChanged(nameof(AxisPolarityLabel));
            }
        }
        // Spatial binding buttons must carry their own visible direction.
        // The previous XAML tried to read an ancestor ContentControl.Tag from
        // inside a nested button template; that binding rendered no glyph.
        public string DirectionalGlyph
        {
            get
            {
                switch (DeviceBindingName)
                {
                    case "Up": return "↑";
                    case "Down": return "↓";
                    case "Left": return "←";
                    case "Right": return "→";
                    case "−": return "−";
                    case "+": return "+";
                    default: return string.Empty;
                }
            }
        }
        public string AxisPolarityLabel
        {
            get
            {
                var polarity = DeviceBinding.InputDirectionSlot == 1 ? "Negative (−)" : "Positive (+)";
                return DeviceBindingName == "Left" || DeviceBindingName == "Right" ||
                       DeviceBindingName == "Up" || DeviceBindingName == "Down"
                    ? polarity + "  " + DirectionalGlyph
                    : polarity;
            }
        }

        public string IoTypeName => DeviceBinding.DeviceIoType.Equals(DeviceIoType.Input) ? "Input" : "Output";
        public DeviceBindingCategory DeviceBindingCategory { get; set; }
        public Mapping ExpressionMapping { get; set; }
        public Visibility LegacyInputTypeVisibility =>
            ExpressionMapping == null && DeviceBinding.DeviceIoType == DeviceIoType.Input &&
            (DeviceBindingCategory == DeviceBindingCategory.Momentary ||
             DeviceBindingCategory == DeviceBindingCategory.Range)
                ? Visibility.Visible : Visibility.Collapsed;
        public int InputExpressionInputKind
        {
            get => DeviceBinding.InputExpressionInputKind;
            set
            {
                // WPF briefly reports -1 before ComboBox items have been realised.
                // That is not a user type change and must never clear a saved binding.
                if (value < 0 || value > 3) return;
                if (DeviceBinding.InputExpressionInputKind == value) return;
                if (ExpressionMapping != null && !ExpressionMapping.EnableInputExpression()) return;
                DeviceBinding.SetInputExpressionInputKind(value);
                DeviceBindingCategory = value == 0 ? DeviceBindingCategory.Momentary : DeviceBindingCategory.Range;
                SelectCompatibleInputDeviceAfterTypeChange();
                OnPropertyChanged(nameof(DeviceBindingCategory));
                OnPropertyChanged(nameof(BindingActionToolTip));
                OnPropertyChanged(nameof(ShowInvertInput));
                OnPropertyChanged(nameof(CanBindCurrentDevice));
                RefreshDirectionalSettingsVisibility();
                OnPropertyChanged(nameof(InputTypeDetailsVisibility));
                OnPropertyChanged();
                OnPropertyChanged(nameof(BindButtonText));
                OnPropertyChanged(nameof(CompactBindButtonText));
                OnPropertyChanged(nameof(AxisConditionVisibility));
                OnPropertyChanged(nameof(ThresholdConditionVisibility));
            }
        }
        // Logical NOT applies to digital directional channels, not continuous axes.
        // Hiding this for analogue channels avoids offering a switch the runtime ignores.
        public Visibility DirectionalNegationVisibility =>
            InputExpressionInputKind == 0 && (InputExpressionNegated || !IsPhysicalDPadDirection)
                ? Visibility.Visible : Visibility.Collapsed;

        internal bool IsPhysicalDPadDirection => DeviceBinding.IsBound &&
            DeviceVisualCatalog.DescribeBinding(DeviceBinding, DeviceBindingCategory.Momentary,
                DeviceBinding.Profile).ControlKind == ControlVisualKind.DPad;

        public Visibility DirectionalSettingsRowVisibility =>
            ShowInvertInput == Visibility.Visible || DirectionalNegationVisibility == Visibility.Visible
                ? Visibility.Visible : Visibility.Collapsed;

        private void RefreshDirectionalSettingsVisibility()
        {
            OnPropertyChanged(nameof(DirectionalNegationVisibility));
            OnPropertyChanged(nameof(DirectionalSettingsRowVisibility));
        }

        // Default button inputs need no extra row; advanced axis controls appear only
        // when that individual input uses analogue data.
        public Visibility InputTypeDetailsVisibility =>
            DeviceBinding.InputExpressionInputKind == 0 ? Visibility.Collapsed : Visibility.Visible;
        public Visibility AxisConditionVisibility =>
            DeviceBinding.InputExpressionInputKind == 0 ? Visibility.Collapsed : Visibility.Visible;
        public Visibility ThresholdConditionVisibility =>
            DeviceBinding.InputExpressionInputKind == 1 || DeviceBinding.InputExpressionInputKind == 2
                ? Visibility.Visible : Visibility.Collapsed;
        public int InputExpressionAxisThreshold
        {
            get => DeviceBinding.InputExpressionAxisThreshold;
            set
            {
                DeviceBinding.SetInputExpressionAxisThreshold(value);
                OnPropertyChanged();
                OnPropertyChanged(nameof(AxisThresholdPercent));
            }
        }

        public int AxisThresholdPercent
        {
            get => (int)Math.Round(DeviceBinding.InputExpressionAxisThreshold * 100.0 / short.MaxValue);
            set
            {
                var percent = Math.Max(1, Math.Min(100, value));
                InputExpressionAxisThreshold = (int)Math.Round(percent * (double)short.MaxValue / 100.0);
                OnPropertyChanged();
            }
        }
        public int DirectionalButtonThresholdPercent
        {
            get => (int)Math.Round(DeviceBinding.DirectionalButtonThreshold * 100.0 / short.MaxValue);
            set
            {
                var percent = Math.Max(0, Math.Min(100, value));
                DeviceBinding.SetDirectionalButtonThreshold((int)Math.Round(percent * (double)short.MaxValue / 100));
                OnPropertyChanged();
            }
        }

        public ObservableCollection<ComboBoxItemViewModel> Devices { get; set; }
        private ComboBoxItemViewModel _selectedDevice;
        public ComboBoxItemViewModel SelectedDevice
        {
            get => _selectedDevice;
            set
            {
                if (ReferenceEquals(_selectedDevice, value)) return;
                _selectedDevice = value;
                OnPropertyChanged(nameof(SelectedDevice));
            }
        }
        public Visibility ShowPreview => IsCapturing ? Visibility.Hidden : Visibility.Visible;
        public Visibility ShowBindMode => ShowPreview.Equals(Visibility.Visible) ? Visibility.Hidden : Visibility.Visible;
        public Visibility ShowPropertyList => PluginPropertyGroup == null ? Visibility.Collapsed : Visibility.Visible;
        internal bool SupportsBlock => DeviceBinding.IsBound && DeviceBinding.IsBlockable();
        public Visibility ShowBlock => SupportsBlock ? Visibility.Visible : Visibility.Collapsed;
        public bool CanBlock => BindingEnabled && SupportsBlock;
        internal DeviceConfiguration ResolveOutputConfiguration()
        {
            var profile = DeviceBinding?.Profile;
            if (profile == null) return null;
            var guid = DeviceBinding.DeviceConfigurationGuid;
            if (guid != Guid.Empty) return profile.GetDeviceConfiguration(DeviceIoType.Output, guid);

            // Keep the primary accessible if no device offers this category. A
            // single-output picker can still change type by selecting a control.
            return FindCompatibleOutputConfiguration(profile, DeviceBindingCategory) ??
                   profile.GetPrimaryDeviceConfiguration(DeviceIoType.Output);
        }

        internal static DeviceConfiguration FindCompatibleOutputConfiguration(Profile profile,
            DeviceBindingCategory category, Guid preferred = default(Guid))
        {
            var preference = preferred == Guid.Empty ? null : profile.GetDeviceConfiguration(DeviceIoType.Output, preferred);
            if (SupportsOutputCategory(preference, category, profile.Context)) return preference;

            var primary = profile.GetPrimaryDeviceConfiguration(DeviceIoType.Output);
            if (!ReferenceEquals(primary, preference) && SupportsOutputCategory(primary, category, profile.Context))
                return primary;

            return profile.GetDeviceConfigurationList(DeviceIoType.Output)
                .FirstOrDefault(device => !ReferenceEquals(device, primary) && !ReferenceEquals(device, preference) &&
                    SupportsOutputCategory(device, category, profile.Context));
        }

        public bool CanBindCurrentDevice
        {
            get
            {
                // A blank input-device selection is permitted, but an active profile
                // must not bypass the editor's disabled state to start detection.
                return DeviceBinding != null && BindingEnabled;
            }
        }

        public string BlockAvailability => !SupportsBlock
            ? "This input cannot be blocked."
            : BindingEnabled
                ? "Block this keyboard key from reaching other apps while the mapping runs."
                : "Stop the active profile to change blocking for this keyboard key.";
        public Visibility ShowInvertInput => DeviceBinding.DeviceIoType == DeviceIoType.Input &&
                                             DeviceBindingCategory == DeviceBindingCategory.Range
            ? Visibility.Visible
            : Visibility.Collapsed;
        public PluginPropertyGroupViewModel PluginPropertyGroup { get; set; }
        public double PreviewValue => GetPreviewValue();
        public bool ShowButtonPreview => IsCapturing || DeviceBinding.Profile.IsActive();

        private bool GuiInvalidated { get; set; }
        internal event Action<DeviceBindingViewModel> UiValueInvalidated;
        private bool _deviceListLoaded;
        private bool _selectedPreviewLoaded;
        public bool IsRefreshingDeviceChoices { get; private set; }
        private bool _deviceListDirty;
        private bool _disposed;

        private double GetPreviewValue()
        {
            if (IsCapturing)
            {
                return BindModeProgress;
            } else if (DeviceBinding.Profile.IsActive())
            {
                switch (DeviceBindingCategory)
                {
                    case DeviceBindingCategory.Momentary:
                        return 100 * CurrentValue;
                    case DeviceBindingCategory.Range:
                        return (long) (50.0 + ((double) CurrentValue / Constants.AxisMaxValue) * 50);
                    case DeviceBindingCategory.Event:
                    case DeviceBindingCategory.Delta:
                    default:
                        return 0;
                }
            }

            return 0;
        }

        private bool _bindingEnabled;
        public bool BindingEnabled
        {
            get => !_disposed && _bindingEnabled;
            set
            {
                if (_bindingEnabled == value) return;
                _bindingEnabled = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PreviewValue));
                OnPropertyChanged(nameof(ShowButtonPreview));
                OnPropertyChanged(nameof(CanBlock));
                OnPropertyChanged(nameof(CanBindCurrentDevice));
            }
        }

        public bool Block
        {
            // An unbound input cannot block anything. Do not show a misleading
            // active toggle while the physical control has not been selected.
            get => DeviceBinding.Block && SupportsBlock;
            set
            {
                if (!value || CanBlock) DeviceBinding.SetBlock(value);
            }
        }

        public bool InvertInput
        {
            get => DeviceBinding.InvertInput;
            set => DeviceBinding.SetInvertInput(value);
        }

        public bool InputExpressionNegated
        {
            get => DeviceBinding.InputExpressionNegated;
            set => DeviceBinding.SetInputExpressionNegated(value);
        }

        public string InputExpressionGroupLabel => "Group " + (DeviceBinding.InputExpressionGroup + 1);

        private Visibility _inputExpressionControlsVisibility = Visibility.Collapsed;
        public Visibility InputExpressionControlsVisibility
        {
            get => _inputExpressionControlsVisibility;
            set
            {
                if (_inputExpressionControlsVisibility == value) return;
                _inputExpressionControlsVisibility = value;
                OnPropertyChanged();
            }
        }

        private bool _canRemoveExpressionInput;
        public bool CanRemoveExpressionInput
        {
            get => _canRemoveExpressionInput;
            set
            {
                if (_canRemoveExpressionInput == value) return;
                _canRemoveExpressionInput = value;
                OnPropertyChanged();
            }
        }

        public string BindingActionToolTip => IsCapturing
            ? "Waiting for input. Click again or right-click to cancel."
            : DeviceBinding?.DeviceIoType == DeviceIoType.Output
                ? "Choose an output control. Right-click or press Shift+F10 for options."
                : DeviceBindingCategory == DeviceBindingCategory.Range
                    ? "Click to bind an input axis. Right-click or press Shift+F10 for options."
                    : "Click to bind an input control. Right-click or press Shift+F10 for options.";

        public bool MenuOnLeft => DeviceBinding?.InputDirectionSlot == 1 ||
            DeviceBinding?.InputDirectionSlot == 3;
        public bool MenuOnRight => !MenuOnLeft;

        public string DirectionalBindLabel
        {
            get
            {
                var slot = DeviceBinding?.InputDirectionSlot ?? 0;
                var direction = slot == 1 ? "←" : slot == 2 ? "→" :
                    slot == 3 ? "↑" : slot == 4 ? "↓" : "";
                if (!DeviceBinding.IsBound) return direction + " Bind";
                var label = CompactBindButtonText;
                if (label.Length > 9) label = label.Substring(0, 8) + "…";
                return direction + " " + label;
            }
        }

        private bool _isOutputCapturing;
        public bool IsCapturing => _isOutputCapturing || DeviceBinding?.IsInBindMode == true;

        internal bool IsUpdatingOutputCaptureState { get; private set; }

        internal void SetOutputCaptureState(bool capturing)
        {
            if (_isOutputCapturing == capturing) return;
            _isOutputCapturing = capturing;
            IsUpdatingOutputCaptureState = true;
            try
            {
                OnPropertyChanged(nameof(IsCapturing));
                OnPropertyChanged(nameof(BindButtonText));
                OnPropertyChanged(nameof(CompactBindButtonText));
                OnPropertyChanged(nameof(BindingActionToolTip));
                OnPropertyChanged(nameof(ShowPreview));
                OnPropertyChanged(nameof(ShowBindMode));
                OnPropertyChanged(nameof(ShowButtonPreview));
                OnPropertyChanged(nameof(PreviewValue));
            }
            finally { IsUpdatingOutputCaptureState = false; }
        }

        private string _cachedBoundName;
        private Guid _cachedBoundDevice;
        private int _cachedBoundType;
        private int _cachedBoundValue;
        private int _cachedBoundSubValue;

        private string BoundNameForDisplay()
        {
            var binding = DeviceBinding;
            if (_cachedBoundName != null && _cachedBoundDevice == binding.DeviceConfigurationGuid &&
                _cachedBoundType == binding.KeyType && _cachedBoundValue == binding.KeyValue &&
                _cachedBoundSubValue == binding.KeySubValue)
                return _cachedBoundName;
            _cachedBoundDevice = binding.DeviceConfigurationGuid;
            _cachedBoundType = binding.KeyType;
            _cachedBoundValue = binding.KeyValue;
            _cachedBoundSubValue = binding.KeySubValue;
            return _cachedBoundName = binding.BoundName();
        }

        public string BindButtonText
        {
            get
            {
                if (IsCapturing)
                    return "●  PRESS INPUT";
                if (DeviceBinding.IsBound)
                {
                    var name = BoundNameForDisplay();
                    // A disconnected/removed provider is not a control name.
                    // Keep the stored assignment for reconnection, but make the
                    // editor actionable instead of labelling the bind button
                    // "Device unavailable".
                    if (string.IsNullOrWhiteSpace(name) ||
                        name.IndexOf("device unavailable", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("unavailable device", StringComparison.OrdinalIgnoreCase) >= 0)
                        return "Bind";
                    return name;
                }
                return "Bind";
            }
        }

        // Compact workspace rows already identify the device. Provider group labels
        // such as "Keys,Backspace" and "Buttons,Back" are redundant here.
        public string CompactBindButtonText
        {
            get
            {
                var label = BindButtonText;
                if (IsCapturing)
                    return "●  PRESS INPUT";
                if (!DeviceBinding.IsBound && DeviceBinding.DeviceIoType == DeviceIoType.Output)
                    return "Output";
                // A new mapping has no configured physical input type yet.
                // Never present the axis-specific instruction before binding.
                if (!DeviceBinding.IsBound && DeviceBinding.DeviceIoType == DeviceIoType.Input)
                    return "Bind";
                if (string.IsNullOrWhiteSpace(label)) return label;
                var directInputLabel = DeviceVisualCatalog.GetBindingDisplayLabel(DeviceBinding, label);
                if (directInputLabel != null) return directInputLabel;
                // The device picker already identifies the device and the binding
                // menu owns category names. In a compact slot, show the control:
                // "Axes, LS X+" -> "LS X+", "Buttons, A" -> "A". Preserve
                // meaningful multi-part control names such as "POV 1, Up".
                foreach (var group in new[] { "Keys", "Buttons", "Axes", "Axis", "Buttons / Axes", "POVs" })
                {
                    if (!label.StartsWith(group, StringComparison.OrdinalIgnoreCase)) continue;
                    var suffix = label.Substring(group.Length);
                    if (suffix.Length > 1 && (suffix[0] == ',' || suffix[0] == ':'))
                        return suffix.Substring(1).Trim();
                }
                return label;
            }
        }

        private DeviceBinding _deviceBinding;
        public DeviceBinding DeviceBinding
        {
            get => _deviceBinding;
            set
            {
                if (ReferenceEquals(_deviceBinding, value)) return;
                if (_deviceBinding != null) _deviceBinding.PropertyChanged -= DeviceBindingOnPropertyChanged;
                _deviceBinding = value;
                _cachedBoundName = null;
                if (_deviceBinding == null) return;
                _deviceBinding.PropertyChanged += DeviceBindingOnPropertyChanged;
                CurrentValue = _deviceBinding.CurrentValue;
            }
        }

        private long _currentValue;
        public long CurrentValue
        {
            get => _currentValue;
            set
            {
                if (_currentValue == value) return;
                _currentValue = value;
                if (GuiInvalidated) return;
                GuiInvalidated = true;
                UiValueInvalidated?.Invoke(this);
            }
        }

        private double _bindModeProgress;
        public double BindModeProgress
        {
            get => _bindModeProgress;
            set
            {
                if (Math.Abs(_bindModeProgress - value) < 0.001) return;
                _bindModeProgress = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PreviewValue));
            }
        }

        public DeviceBindingViewModel(DeviceBinding deviceBinding)
        {
            DeviceBinding = deviceBinding;
            Devices = new ObservableCollection<ComboBoxItemViewModel>();
            BindingEnabled = !DeviceBinding.Profile.IsActive();
        }

        // The closed selector only needs its current choice. Building all compatible
        // device choices is deferred until the user actually opens the dropdown.
        public void EnsureSelectedDevicePreviewLoaded()
        {
            if (_disposed || DeviceBinding?.Profile == null || _deviceListLoaded) return;
            if (_selectedPreviewLoaded && !_deviceListDirty) return;
            _selectedPreviewLoaded = true;
            LoadSelectedDevicePreview();
        }

        private void LoadSelectedDevicePreview()
        {
            if (_disposed || _deviceListLoaded || DeviceBinding?.Profile == null) return;
            IsRefreshingDeviceChoices = true;
            try
            {
                var guid = DeviceBinding.DeviceConfigurationGuid;
                var placeholder = new ComboBoxItemViewModel(
                    DeviceBinding.DeviceIoType == DeviceIoType.Input ? "Any input device" : "Choose output device",
                    Guid.Empty);
                var choices = new ObservableCollection<ComboBoxItemViewModel> { placeholder };
                var chosen = guid == Guid.Empty ? placeholder : CreateSavedDeviceChoice(guid);
                if (guid != Guid.Empty) choices.Add(chosen);
                Devices = choices;
                _deviceListDirty = false;
                OnPropertyChanged(nameof(Devices));
                SelectedDevice = chosen;
            }
            finally
            {
                IsRefreshingDeviceChoices = false;
            }
        }

        public void EnsureDeviceListLoaded()
        {
            if (_disposed || DeviceBinding?.Profile == null) return;
            if (_deviceListLoaded && !_deviceListDirty) return;

            IsRefreshingDeviceChoices = true;
            try
            {
                LoadDeviceInputs();
                OnPropertyChanged(nameof(Devices));
                OnPropertyChanged(nameof(SelectedDevice));
            }
            finally
            {
                IsRefreshingDeviceChoices = false;
            }
        }
        
        // Output devices must actually expose a control of the selected output category.
        // In particular, a keyboard cannot be silently selected as an axis device.
        public static bool SupportsOutputCategory(DeviceConfiguration configuration,
            DeviceBindingCategory category, Context context)
        {
            if (configuration?.Device == null || context == null) return false;
            try
            {
                return MenuHasCategory(configuration.Device.GetDeviceBindingMenu(
                    context, DeviceIoType.Output), category);
            }
            catch (Exception exception)
            {
                Logger.Warn("Unable to check output device binding categories.", exception);
                return false;
            }
        }

        private static bool MenuHasCategory(IEnumerable<DeviceBindingNode> nodes,
            DeviceBindingCategory category)
        {
            if (nodes == null) return false;
            foreach (var node in nodes)
            {
                if (node == null) continue;
                if (node.DeviceBindingInfo?.DeviceBindingCategory == category) return true;
                if (MenuHasCategory(node.ChildrenNodes, category)) return true;
            }
            return false;
        }

        // Physical input categories are validated against the provider's actual
        // supported controls. A keyboard must never be offered as an axis source.
        // Button input providers may expose their keys only when connected or after
        // provider initialization. Preserve those choices; only range inputs require
        // a positively reported analogue control.
        private static bool IsCompatibleInputDevice(DeviceConfiguration configuration,
            DeviceBindingCategory category, Context context)
        {
            // Capability menus are not authoritative for physical input devices:
            // some providers expose axes only after initialization or connection.
            // Keep configured devices selectable; binding capture validates the
            // actual control rather than hiding the entire device.
            return configuration?.Device != null;
        }

        public static bool SupportsInputCategory(DeviceConfiguration configuration,
            DeviceBindingCategory category, Context context)
        {
            if (configuration?.Device == null || context == null) return false;
            try
            {
                return MenuHasCategory(configuration.Device.GetDeviceBindingMenu(
                    context, DeviceIoType.Input), category);
            }
            catch (Exception exception)
            {
                Logger.Warn("Unable to inspect input device binding categories.", exception);
                return false;
            }
        }

        private void SelectCompatibleInputDeviceAfterTypeChange()
        {
            if (DeviceBinding?.DeviceIoType != DeviceIoType.Input ||
                DeviceBinding.Profile == null) return;

            var profile = DeviceBinding.Profile;
            var context = profile.Context;
            var candidates = profile.GetDeviceConfigurationList(DeviceIoType.Input)
                .Where(configuration => IsCompatibleInputDevice(configuration, DeviceBindingCategory, context))
                .ToList();
            var currentGuid = DeviceBinding.DeviceConfigurationGuid;
            // An incompatible explicit selection returns to listen-to-all. Never
            // silently lock a new input type to an unrelated primary device.
            if (currentGuid != Guid.Empty &&
                !candidates.Any(configuration => configuration.Guid == currentGuid))
                DeviceBinding.SetDeviceConfigurationGuid(Guid.Empty, false);
            RefreshDeviceList();
        }

        private void LoadDeviceInputs()
        {
            var devicesManager = DeviceBinding.Profile.Context.DevicesManager;
            var deviceConfigurationList = DeviceBinding.Profile.GetDeviceConfigurationList(DeviceBinding.DeviceIoType)
                .Where(configuration => configuration?.Device != null &&
                    (DeviceBinding.DeviceIoType == DeviceIoType.Input ||
                     SupportsOutputCategory(configuration, DeviceBindingCategory, DeviceBinding.Profile.Context) ||
                     devicesManager.ResolveDevice(configuration.Device, DeviceIoType.Output) == null))
                .Select((configuration, index) => new
                {
                    Configuration = configuration,
                    OriginalIndex = index,
                    SortOrder = devicesManager.GetDeviceSortOrder(configuration.Device)
                })
                .OrderBy(item => item.SortOrder)
                .ThenBy(item => item.OriginalIndex)
                .Select(item => item.Configuration)
                .ToList();

            Devices = new ObservableCollection<ComboBoxItemViewModel>();
            if (DeviceBinding.DeviceIoType == DeviceIoType.Input)
                Devices.Add(new ComboBoxItemViewModel("Any input device", Guid.Empty));
            else
                Devices.Add(new ComboBoxItemViewModel("Choose output device", Guid.Empty));
            foreach (var deviceConfiguration in deviceConfigurationList)
            {
                Devices.Add(new ComboBoxItemViewModel(
                    deviceConfiguration.GetFullTitleForProfile(DeviceBinding.Profile),
                    deviceConfiguration.Guid,
                    DeviceVisualCatalog.Describe(deviceConfiguration, DeviceBinding.Profile, DeviceBinding.DeviceIoType)));
            }
            _deviceListLoaded = true;
            _deviceListDirty = false;
            SetSelectDevice();
        }

        public void RefreshDeviceList()
        {
            if (_disposed || DeviceBinding == null || DeviceBinding.Profile == null) return;
            // Device capabilities/aliases may have changed even if the GUID hasn't.
            _cachedBoundName = null;
            if (!_deviceListLoaded)
            {
                _deviceListDirty = true;
                if (_selectedPreviewLoaded)
                    LoadSelectedDevicePreview();
                // The collapsed mapping header depends on binding presentation,
                // not on discovering all possible devices.
                OnPropertyChanged(nameof(SelectedDevice));
                OnPropertyChanged(nameof(BindButtonText));
                OnPropertyChanged(nameof(CompactBindButtonText));
                return;
            }

            IsRefreshingDeviceChoices = true;
            try
            {
                LoadDeviceInputs();
                OnPropertyChanged(nameof(Devices));
                OnPropertyChanged(nameof(SelectedDevice));
                OnPropertyChanged(nameof(BindButtonText));
                OnPropertyChanged(nameof(CompactBindButtonText));
                OnPropertyChanged(nameof(ShowBlock));
                OnPropertyChanged(nameof(CanBlock));
                OnPropertyChanged(nameof(ShowInvertInput));
                OnPropertyChanged(nameof(CanBindCurrentDevice));
            }
            finally
            {
                IsRefreshingDeviceChoices = false;
            }
        }

        private ComboBoxItemViewModel CreateSavedDeviceChoice(Guid savedGuid)
        {
            var profile = DeviceBinding.Profile;
            var type = DeviceBinding.DeviceIoType;
            var saved = profile.GetDeviceConfiguration(type, savedGuid);
            if (saved?.Device == null)
                return new ComboBoxItemViewModel(type == DeviceIoType.Output
                    ? "Unavailable output device" : "Unavailable input device", savedGuid);

            return new ComboBoxItemViewModel(saved.GetFullTitleForProfile(profile), savedGuid,
                DeviceVisualCatalog.Describe(saved, profile, type));
        }

        private void SetSelectDevice()
        {
            if (!_deviceListLoaded || Devices == null) return;
            var guid = DeviceBinding.DeviceConfigurationGuid;
            var selected = Devices.FirstOrDefault(choice => choice.Value == guid);
            if (selected == null && guid != Guid.Empty)
            {
                // Rebuilding the picker is a read of the assignment. Missing
                // metadata must never clear a key stored in the profile.
                selected = CreateSavedDeviceChoice(guid);
                Devices.Add(selected);
            }
            SelectedDevice = selected;
        }

        public DeviceBindingTransferCompatibility ChangeDeviceConfiguration(Guid selectedDeviceConfigurationGuid)
        {
            if (DeviceBinding.DeviceIoType == DeviceIoType.Input &&
                selectedDeviceConfigurationGuid == Guid.Empty)
            {
                // Revert to unrestricted detection and discard the old device's
                // control key; no specific device may remain implicitly selected.
                DeviceBinding.SetDeviceConfigurationGuid(Guid.Empty, false);
                if (_deviceListLoaded)
                {
                    SetSelectDevice();
                    OnPropertyChanged(nameof(SelectedDevice));
                }
                OnPropertyChanged(nameof(CompactBindButtonText));
                OnPropertyChanged(nameof(CanBindCurrentDevice));
                return DeviceBindingTransferCompatibility.Unknown;
            }
            var selectedDeviceConfiguration = DeviceBinding.Profile.GetDeviceConfiguration(
                DeviceBinding.DeviceIoType, selectedDeviceConfigurationGuid);
            if (selectedDeviceConfiguration == null) return DeviceBindingTransferCompatibility.Unknown;
            if (DeviceBinding.DeviceConfigurationGuid == selectedDeviceConfiguration.Guid)
                return DeviceBinding.IsBound ? DeviceBindingTransferCompatibility.Compatible : DeviceBindingTransferCompatibility.Unknown;

            if (DeviceBinding.DeviceIoType == DeviceIoType.Input &&
                !IsCompatibleInputDevice(selectedDeviceConfiguration, DeviceBindingCategory,
                    DeviceBinding.Profile.Context))
                return DeviceBindingTransferCompatibility.Incompatible;

            var previousDeviceConfiguration = DeviceBinding.Profile.GetDeviceConfiguration(
                DeviceBinding.DeviceIoType, DeviceBinding.DeviceConfigurationGuid);

            var transfer = DeviceBindingTransferResult.For(
                DeviceBindingTransferCompatibility.Unknown,
                DeviceBinding);

            if (DeviceBinding.IsBound && previousDeviceConfiguration != null)
            {
                transfer = DeviceBindingCompatibility.EvaluateTransfer(
                    previousDeviceConfiguration.Device,
                    selectedDeviceConfiguration.Device,
                    DeviceBinding.Profile.Context,
                    DeviceBinding.DeviceIoType,
                    DeviceBinding,
                    DeviceBindingCategory);
            }

            if (transfer.Compatibility == DeviceBindingTransferCompatibility.Compatible)
            {
                DeviceBinding.SetDeviceConfigurationGuid(
                    selectedDeviceConfiguration.Guid,
                    true,
                    transfer.KeyType,
                    transfer.KeyValue,
                    transfer.KeySubValue);
            }
            else
            {
                // Keep the user's new destination, but do not carry an unverified physical key to it.
                DeviceBinding.SetDeviceConfigurationGuid(selectedDeviceConfiguration.Guid, false);
            }

            if (_deviceListLoaded)
            {
                SetSelectDevice();
                OnPropertyChanged(nameof(SelectedDevice));
            }
            OnPropertyChanged(nameof(BindButtonText));
            OnPropertyChanged(nameof(CompactBindButtonText));
            OnPropertyChanged(nameof(ShowBlock));
            OnPropertyChanged(nameof(CanBlock));
            OnPropertyChanged(nameof(Block));
            OnPropertyChanged(nameof(ShowInvertInput));
            OnPropertyChanged(nameof(InvertInput));
            OnPropertyChanged(nameof(CanBindCurrentDevice));
            Logger.Info("Binding device changed. io=" + DeviceBinding.DeviceIoType +
                        "; category=" + DeviceBindingCategory +
                        "; from=" + (previousDeviceConfiguration?.GetFullTitleForProfile(DeviceBinding.Profile) ?? "unavailable") +
                        "; to=" + selectedDeviceConfiguration.GetFullTitleForProfile(DeviceBinding.Profile) +
                        "; compatibility=" + transfer.Compatibility +
                        "; preserved=" + DeviceBinding.IsBound);
            return transfer.Compatibility;
        }

        internal void RequestUiValueRefresh()
        {
            if (_disposed) return;
            GuiInvalidated = true;
            UiValueInvalidated?.Invoke(this);
        }

        public void CurrentValueChanged()
        {
            if (!GuiInvalidated) return;
            GuiInvalidated = false;
            OnPropertyChanged(nameof(CurrentValue));
            OnPropertyChanged(nameof(PreviewValue));
        }

        private void DeviceBindingOnPropertyChanged(object sender, PropertyChangedEventArgs propertyChangedEventArgs)
        {
            var deviceBinding = (DeviceBinding) sender;
            if (!deviceBinding.Guid.Equals(DeviceBinding.Guid)) return;

            CurrentValue = deviceBinding.CurrentValue;

            if (string.Equals(propertyChangedEventArgs.PropertyName,
                nameof(DeviceBinding.InputExpressionInputKind), StringComparison.Ordinal))
            {
                // Automatic capture can change the physical input category without
                // going through the editor's type selector. Synchronise both views.
                DeviceBindingCategory = deviceBinding.InputExpressionInputKind == 0
                    ? DeviceBindingCategory.Momentary : DeviceBindingCategory.Range;
                RefreshDeviceList();
                OnPropertyChanged(nameof(InputExpressionInputKind));
                OnPropertyChanged(nameof(DeviceBindingCategory));
                OnPropertyChanged(nameof(BindingActionToolTip));
                OnPropertyChanged(nameof(InputTypeDetailsVisibility));
            }

            if (string.Equals(propertyChangedEventArgs.PropertyName, nameof(DeviceBinding.IsInBindMode), StringComparison.Ordinal))
            {
                var bindingManager = deviceBinding.Profile?.Context?.BindingManager;
                if (bindingManager != null)
                {
                    // Only the binding currently waiting for input needs the high-frequency countdown.
                    // Keeping every mapping subscribed caused thousands of needless WPF updates per second.
                    bindingManager.PropertyChanged -= BindingManagerOnPropertyChanged;
                    if (deviceBinding.IsInBindMode) bindingManager.PropertyChanged += BindingManagerOnPropertyChanged;
                }

                OnPropertyChanged(nameof(ShowButtonPreview));
                OnPropertyChanged(nameof(IsCapturing));
                OnPropertyChanged(nameof(BindingActionToolTip));
            }

            if (string.Equals(propertyChangedEventArgs.PropertyName, nameof(DeviceBinding.IsBound), StringComparison.Ordinal)
                || string.Equals(propertyChangedEventArgs.PropertyName, nameof(DeviceBinding.IsInBindMode), StringComparison.Ordinal))
            {
                _cachedBoundName = null;
                BindModeProgress = 0;
                OnPropertyChanged(nameof(BindButtonText));
                OnPropertyChanged(nameof(CompactBindButtonText));
                OnPropertyChanged(nameof(DirectionalBindLabel));
                OnPropertyChanged(nameof(ShowPreview));
                OnPropertyChanged(nameof(ShowBindMode));
            }

            if (string.Equals(propertyChangedEventArgs.PropertyName, nameof(DeviceBinding.IsBound), StringComparison.Ordinal))
            {
                if (_deviceListLoaded)
                {
                    SetSelectDevice();
                    OnPropertyChanged(nameof(SelectedDevice));
                }
                OnPropertyChanged(nameof(ShowBlock));
            OnPropertyChanged(nameof(CanBlock));
                OnPropertyChanged(nameof(Block));
                OnPropertyChanged(nameof(ShowInvertInput));
                OnPropertyChanged(nameof(InvertInput));
                RefreshDirectionalSettingsVisibility();
            }
            if (string.Equals(propertyChangedEventArgs.PropertyName, nameof(DeviceBinding.DeviceConfigurationGuid), StringComparison.Ordinal))
            {
                OnPropertyChanged(nameof(CanBindCurrentDevice));
                if (_deviceListLoaded)
                    SetSelectDevice();
                else if (_selectedPreviewLoaded)
                    LoadSelectedDevicePreview();
                OnPropertyChanged(nameof(BindButtonText));
                OnPropertyChanged(nameof(CompactBindButtonText));
                OnPropertyChanged(nameof(ShowBlock));
            OnPropertyChanged(nameof(CanBlock));
                OnPropertyChanged(nameof(Block));
                OnPropertyChanged(nameof(ShowInvertInput));
                OnPropertyChanged(nameof(InvertInput));
                RefreshDirectionalSettingsVisibility();
            }
            if (string.Equals(propertyChangedEventArgs.PropertyName, nameof(DeviceBinding.Block), StringComparison.Ordinal))
            {
                OnPropertyChanged(nameof(Block));
            }
            if (string.Equals(propertyChangedEventArgs.PropertyName, nameof(DeviceBinding.InvertInput), StringComparison.Ordinal))
            {
                OnPropertyChanged(nameof(InvertInput));
            }
            if (string.Equals(propertyChangedEventArgs.PropertyName, nameof(DeviceBinding.InputExpressionNegated), StringComparison.Ordinal))
            {
                OnPropertyChanged(nameof(InputExpressionNegated));
                RefreshDirectionalSettingsVisibility();
            }
            if (string.Equals(propertyChangedEventArgs.PropertyName, nameof(DeviceBinding.InputExpressionGroup), StringComparison.Ordinal))
            {
                OnPropertyChanged(nameof(InputExpressionGroupLabel));
            }
        }
        
        private void BindingManagerOnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (!string.Equals(e.PropertyName, nameof(BindingManager.BindModeProgress), StringComparison.Ordinal)) return;
            if (!DeviceBinding.IsInBindMode) return;

            var bindingManager = sender as BindingManager;
            if (bindingManager == null) return;
            BindModeProgress = bindingManager.BindModeProgress;
        }

        internal void RefreshActiveState()
        {
            if (_disposed || DeviceBinding?.Profile == null) return;
            BindingEnabled = !DeviceBinding.Profile.IsActive();
        }


        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            var binding = _deviceBinding;
            if (binding != null)
            {
                // Removing or rebuilding a slot must end its provider capture;
                // a queued press must never populate a detached binding.
                binding.CancelBindMode();
                binding.PropertyChanged -= DeviceBindingOnPropertyChanged;
                var context = binding.Profile?.Context;
                if (context != null)
                {
                    context.BindingManager.PropertyChanged -= BindingManagerOnPropertyChanged;
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

