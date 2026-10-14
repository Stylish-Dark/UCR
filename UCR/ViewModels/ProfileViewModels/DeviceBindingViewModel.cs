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
        private string _deviceBindingName;
        public string DeviceBindingName
        {
            get => _deviceBindingName;
            set
            {
                if (_deviceBindingName == value) return;
                _deviceBindingName = value;
                OnPropertyChanged();
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
                OnPropertyChanged(nameof(ShowInvertInput));
                OnPropertyChanged(nameof(CanBindCurrentDevice));
                OnPropertyChanged(nameof(InputTypeDetailsVisibility));
                OnPropertyChanged();
                OnPropertyChanged(nameof(BindButtonText));
                OnPropertyChanged(nameof(CompactBindButtonText));
                OnPropertyChanged(nameof(AxisConditionVisibility));
                OnPropertyChanged(nameof(ThresholdConditionVisibility));
            }
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
        public ObservableCollection<ComboBoxItemViewModel> Devices { get; set; }
        public ComboBoxItemViewModel SelectedDevice { get; set; }
        public Visibility ShowPreview => DeviceBinding.IsInBindMode ? Visibility.Hidden : Visibility.Visible;
        public Visibility ShowBindMode => ShowPreview.Equals(Visibility.Visible) ? Visibility.Hidden : Visibility.Visible;
        public Visibility ShowPropertyList => PluginPropertyGroup == null ? Visibility.Collapsed : Visibility.Visible;
        public Visibility ShowBlock => DeviceBinding.DeviceIoType == DeviceIoType.Input
            ? Visibility.Visible
            : Visibility.Collapsed;
        public bool CanBlock => BindingEnabled && DeviceBinding.IsBound && DeviceBinding.IsBlockable();
        public bool CanBindCurrentDevice
        {
            get
            {
                // Detection is available before choosing a device. Guid.Empty means
                // listen to every input device added to the profile; a nonempty GUID
                // limits detection to that device in BindingManager.
                if (DeviceBinding == null) return false;
                return DeviceBinding.DeviceIoType == DeviceIoType.Input || BindingEnabled;
            }
        }

        public string BlockAvailability => CanBlock
            ? "Prevent the original device input from reaching the application"
            : "This input provider or control does not support blocking";
        public Visibility ShowInvertInput => DeviceBinding.DeviceIoType == DeviceIoType.Input &&
                                             DeviceBindingCategory == DeviceBindingCategory.Range
            ? Visibility.Visible
            : Visibility.Collapsed;
        public PluginPropertyGroupViewModel PluginPropertyGroup { get; set; }
        public double PreviewValue => GetPreviewValue();
        public bool ShowButtonPreview => DeviceBinding.IsInBindMode || DeviceBinding.Profile.IsActive();

        private bool GuiInvalidated { get; set; }
        internal event Action<DeviceBindingViewModel> UiValueInvalidated;
        private bool _deviceListLoaded;
        private bool _deviceListDirty;
        private bool _disposed;

        private double GetPreviewValue()
        {
            if (DeviceBinding.IsInBindMode)
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
            get => _bindingEnabled;
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
            get => DeviceBinding.IsBound && DeviceBinding.Block;
            set => DeviceBinding.SetBlock(value);
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

        public string BindingActionToolTip => DeviceBinding.DeviceIoType == DeviceIoType.Output
            ? "Choose a supported output control from this device."
            : DeviceBindingCategory == DeviceBindingCategory.Range
                ? "Move the joystick or squeeze a trigger to detect its axis. Press Esc to cancel."
                : "Press an input control to bind it. Press Esc to cancel.";

        public string BindButtonText
        {
            get
            {
                if (DeviceBinding.IsInBindMode)
                    return DeviceBindingCategory == DeviceBindingCategory.Range
                        ? "Move axis to detect" : "Press input device";
                if (DeviceBinding.IsBound) return DeviceBinding.BoundName();
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
                if (!DeviceBinding.IsBound && DeviceBinding.DeviceIoType == DeviceIoType.Output)
                    return "Choose output";
                if (!DeviceBinding.IsBound && DeviceBinding.DeviceIoType == DeviceIoType.Input &&
                    DeviceBindingCategory == DeviceBindingCategory.Range)
                    return "Move axis to bind";
                if (string.IsNullOrWhiteSpace(label)) return label;
                foreach (var group in new[] { "Keys", "Buttons" })
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

        public void EnsureDeviceListLoaded()
        {
            if (_disposed || DeviceBinding?.Profile == null) return;
            if (_deviceListLoaded && !_deviceListDirty) return;

            LoadDeviceInputs();
            OnPropertyChanged(nameof(Devices));
            OnPropertyChanged(nameof(SelectedDevice));
        }
        
        // Output devices must actually expose a control of the selected output category.
        // In particular, a keyboard cannot be silently selected as an axis device.
        public static bool SupportsOutputCategory(DeviceConfiguration configuration,
            DeviceBindingCategory category, Context context)
        {
            if (configuration?.Device == null || context == null) return false;
            try
            {
                return MenuHasCategory(context.DevicesManager.GetDeviceBindingMenu(
                    configuration.Device, DeviceIoType.Output), category);
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
            return configuration?.Device != null &&
                   (category != DeviceBindingCategory.Range ||
                    SupportsInputCategory(configuration, category, context));
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
                    (DeviceBinding.DeviceIoType == DeviceIoType.Output
                        ? SupportsOutputCategory(configuration, DeviceBindingCategory, DeviceBinding.Profile.Context)
                        : IsCompatibleInputDevice(configuration, DeviceBindingCategory, DeviceBinding.Profile.Context)))
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
            if (!_deviceListLoaded)
            {
                _deviceListDirty = true;
                // The collapsed mapping header still depends on this view-model's presentation.
                // Signal a lightweight presentation change without paying to build the dropdown.
                OnPropertyChanged(nameof(SelectedDevice));
                OnPropertyChanged(nameof(BindButtonText));
                OnPropertyChanged(nameof(CompactBindButtonText));
                return;
            }

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

        private void SetSelectDevice()
        {
            if (!_deviceListLoaded || Devices == null) return;
            ComboBoxItemViewModel selectedDevice = null;

            foreach (var comboBoxItem in Devices)
            {
                if (comboBoxItem.Value == DeviceBinding.DeviceConfigurationGuid)
                {
                    selectedDevice = comboBoxItem;
                    break;
                }
            }

            if (Devices.Count == 0)
            {
                Devices.Add(new ComboBoxItemViewModel(
                    DeviceBinding.DeviceIoType == DeviceIoType.Output ? "No compatible output devices" :
                        DeviceBindingCategory == DeviceBindingCategory.Range
                            ? "No compatible axis devices" : "No compatible button devices",
                    Guid.Empty));
                selectedDevice = Devices[0];
            }
            else if (selectedDevice == null && DeviceBinding.DeviceConfigurationGuid != Guid.Empty)
            {
                if (DeviceBinding.DeviceIoType == DeviceIoType.Input)
                {
                    var savedGuid = DeviceBinding.DeviceConfigurationGuid;
                    var saved = DeviceBinding.Profile.GetDeviceConfiguration(DeviceIoType.Input, savedGuid);

                    if (saved == null ||
                        (saved.Device != null &&
                         !IsCompatibleInputDevice(saved, DeviceBindingCategory, DeviceBinding.Profile.Context)))
                    {
                        // A removed profile device or a category-incompatible control is
                        // not a selectable input. Reset to unrestricted detection rather
                        // than manufacturing a phantom "Unavailable device" choice.
                        DeviceBinding.SetDeviceConfigurationGuid(Guid.Empty, false);
                        selectedDevice = Devices.FirstOrDefault(item => item.Value == Guid.Empty);
                    }
                    else
                    {
                        // Preserve references to a configured but temporarily disconnected
                        // device without inventing a generic unavailable-device entry.
                        // This prevents losing saved mappings when hardware is unplugged.
                        var title = saved.ConfigurationName ?? saved.Device?.DisplayTitle
                            ?? saved.Device?.Title ?? "Disconnected input device";
                        selectedDevice = new ComboBoxItemViewModel(title, savedGuid,
                            DeviceVisualCatalog.Describe(saved, DeviceBinding.Profile, DeviceIoType.Input));
                        Devices.Add(selectedDevice);
                    }
                }
                else
                {
                    // Output selections are explicit destinations: never silently
                    // redirect their saved bindings to another output device.
                    selectedDevice = new ComboBoxItemViewModel("Unavailable device", DeviceBinding.DeviceConfigurationGuid);
                    Devices.Insert(0, selectedDevice);
                }
            }
            else if (selectedDevice == null)
            {
                if (DeviceBinding.DeviceIoType == DeviceIoType.Input)
                {
                    // A blank selection is intentional: Bind listens to all profile
                    // devices until an input is detected or a device is chosen.
                    selectedDevice = Devices.FirstOrDefault(item => item.Value == Guid.Empty);
                }
                else
                {
                    var primaryGuid = DeviceBinding.Profile.GetPrimaryDeviceConfiguration(DeviceBinding.DeviceIoType)?.Guid;
                    selectedDevice = Devices.FirstOrDefault(item => item.Value == primaryGuid) ?? Devices[0];
                    if (!DeviceBinding.IsBound && selectedDevice.Value != Guid.Empty)
                        DeviceBinding.SetDeviceConfigurationGuid(selectedDevice.Value, false);
                }
            }

            SelectedDevice = selectedDevice;
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

            if (DeviceBinding.DeviceIoType == DeviceIoType.Input &&
                !IsCompatibleInputDevice(selectedDeviceConfiguration, DeviceBindingCategory,
                    DeviceBinding.Profile.Context))
                return DeviceBindingTransferCompatibility.Incompatible;

            var previousDeviceConfiguration = DeviceBinding.Profile.GetDeviceConfiguration(
                DeviceBinding.DeviceIoType, DeviceBinding.DeviceConfigurationGuid);

            var transfer = DeviceBindingTransferResult.For(
                DeviceBindingTransferCompatibility.Unknown,
                DeviceBinding);

            if (DeviceBinding.IsBound && previousDeviceConfiguration != null &&
                previousDeviceConfiguration.Guid != selectedDeviceConfiguration.Guid)
            {
                transfer = DeviceBindingCompatibility.EvaluateTransfer(
                    previousDeviceConfiguration.Device,
                    selectedDeviceConfiguration.Device,
                    DeviceBinding.Profile.Context,
                    DeviceBinding.DeviceIoType,
                    DeviceBinding,
                    DeviceBindingCategory);
            }

            if (transfer.Compatibility == DeviceBindingTransferCompatibility.Incompatible)
            {
                DeviceBinding.SetDeviceConfigurationGuid(selectedDeviceConfiguration.Guid, false);
            }
            else if (transfer.Compatibility == DeviceBindingTransferCompatibility.Compatible)
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
                // Unknown remains deliberately non-destructive. Preserve the existing semantic key
                // unless the compatibility layer can positively prove that it is incompatible.
                DeviceBinding.SetDeviceConfigurationGuid(selectedDeviceConfiguration.Guid, true);
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
            }

            if (string.Equals(propertyChangedEventArgs.PropertyName, nameof(DeviceBinding.IsBound), StringComparison.Ordinal)
                || string.Equals(propertyChangedEventArgs.PropertyName, nameof(DeviceBinding.IsInBindMode), StringComparison.Ordinal))
            {
                BindModeProgress = 0;
                OnPropertyChanged(nameof(BindButtonText));
                OnPropertyChanged(nameof(CompactBindButtonText));
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
            }
            if (string.Equals(propertyChangedEventArgs.PropertyName, nameof(DeviceBinding.DeviceConfigurationGuid), StringComparison.Ordinal))
            {
                OnPropertyChanged(nameof(CanBindCurrentDevice));
                OnPropertyChanged(nameof(CompactBindButtonText));
                OnPropertyChanged(nameof(BindButtonText));
                OnPropertyChanged(nameof(CompactBindButtonText));
                OnPropertyChanged(nameof(ShowBlock));
            OnPropertyChanged(nameof(CanBlock));
                OnPropertyChanged(nameof(Block));
                OnPropertyChanged(nameof(ShowInvertInput));
                OnPropertyChanged(nameof(InvertInput));
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
