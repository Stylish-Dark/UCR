using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using HidWizards.UCR.Core.Annotations;
using HidWizards.UCR.Core.Managers;
using HidWizards.UCR.Core.Models;
using HidWizards.UCR.Core.Models.Binding;
using HidWizards.UCR.Core.Utilities;

namespace HidWizards.UCR.ViewModels.ProfileViewModels
{
    public class DeviceBindingViewModel : INotifyPropertyChanged, IDisposable
    {
        public string DeviceBindingName { get; set; }
        public string IoTypeName => DeviceBinding.DeviceIoType.Equals(DeviceIoType.Input) ? "Input" : "Output";
        public DeviceBindingCategory DeviceBindingCategory { get; set; }
        public ObservableCollection<ComboBoxItemViewModel> Devices { get; set; }
        public ComboBoxItemViewModel SelectedDevice { get; set; }
        public Visibility ShowPreview => DeviceBinding.IsInBindMode ? Visibility.Hidden : Visibility.Visible;
        public Visibility ShowBindMode => ShowPreview.Equals(Visibility.Visible) ? Visibility.Hidden : Visibility.Visible;
        public Visibility ShowPropertyList => PluginPropertyGroup == null ? Visibility.Collapsed : Visibility.Visible;
        public Visibility ShowBlock => DeviceBinding.DeviceIoType == DeviceIoType.Input && DeviceBinding.IsBlockable()
            ? Visibility.Visible
            : Visibility.Collapsed;
        public Visibility ShowInvertInput => DeviceBinding.DeviceIoType == DeviceIoType.Input &&
                                             DeviceBindingCategory == DeviceBindingCategory.Range
            ? Visibility.Visible
            : Visibility.Collapsed;
        public PluginPropertyGroupViewModel PluginPropertyGroup { get; set; }
        public double PreviewValue => GetPreviewValue();
        public bool ShowButtonPreview => DeviceBinding.IsInBindMode || DeviceBinding.Profile.IsActive();

        private bool GuiInvalidated { get; set; }
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
                _bindingEnabled = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PreviewValue));
                OnPropertyChanged(nameof(ShowButtonPreview));
            }
        }

        public bool Block
        {
            get => DeviceBinding.Block;
            set => DeviceBinding.SetBlock(value);
        }

        public bool InvertInput
        {
            get => DeviceBinding.InvertInput;
            set => DeviceBinding.SetInvertInput(value);
        }

        public string BindButtonText
        {
            get
            {
                if (DeviceBinding.IsInBindMode) return "Press input device";
                if (DeviceBinding.IsBound) return DeviceBinding.BoundName();
                return "Click to bind";
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
                GuiInvalidated = true;
                OnPropertyChanged(nameof(ShowButtonPreview));
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
                OnPropertyChanged(nameof(ShowButtonPreview));
            }
        }

        public DeviceBindingViewModel(DeviceBinding deviceBinding)
        {
            DeviceBinding = deviceBinding;
            deviceBinding.Profile.Context.SubscriptionsManager.PropertyChanged += SubscriptionsManagerOnPropertyChanged;
            deviceBinding.Profile.Context.DeviceAliasesChangedEvent += ContextOnDeviceAliasesChanged;
            BindingEnabled = !DeviceBinding.Profile.Context.SubscriptionsManager.ProfileActive;

            LoadDeviceInputs();
        }
        
        public void LoadDeviceInputs()
        {
            var devicesManager = DeviceBinding.Profile.Context.DevicesManager;
            var deviceConfigurationList = DeviceBinding.Profile.GetDeviceConfigurationList(DeviceBinding.DeviceIoType)
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
            foreach (var deviceConfiguration in deviceConfigurationList)
            {
                Devices.Add(new ComboBoxItemViewModel(deviceConfiguration.GetFullTitleForProfile(DeviceBinding.Profile), deviceConfiguration.Guid));
            }

            SetSelectDevice();
        }

        public void RefreshDeviceList()
        {
            if (_disposed || DeviceBinding == null || DeviceBinding.Profile == null) return;
            LoadDeviceInputs();
            OnPropertyChanged(nameof(Devices));
            OnPropertyChanged(nameof(SelectedDevice));
            OnPropertyChanged(nameof(BindButtonText));
            OnPropertyChanged(nameof(ShowBlock));
            OnPropertyChanged(nameof(ShowInvertInput));
        }

        private void ContextOnDeviceAliasesChanged()
        {
            RefreshDeviceList();
        }

        private void SetSelectDevice()
        {
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
                Devices.Add(new ComboBoxItemViewModel("No devices", Guid.Empty));
                selectedDevice = Devices[0];
            }
            else if (selectedDevice == null && DeviceBinding.DeviceConfigurationGuid != Guid.Empty)
            {
                selectedDevice = new ComboBoxItemViewModel("Unavailable device", DeviceBinding.DeviceConfigurationGuid);
                Devices.Insert(0, selectedDevice);
            }
            else if (selectedDevice == null)
            {
                selectedDevice = Devices[0];
            }

            SelectedDevice = selectedDevice;
        }

        public DeviceBindingTransferCompatibility ChangeDeviceConfiguration(Guid selectedDeviceConfigurationGuid)
        {
            var selectedDeviceConfiguration = DeviceBinding.Profile.GetDeviceConfiguration(
                DeviceBinding.DeviceIoType, selectedDeviceConfigurationGuid);
            if (selectedDeviceConfiguration == null) return DeviceBindingTransferCompatibility.Unknown;

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

            SetSelectDevice();
            OnPropertyChanged(nameof(SelectedDevice));
            OnPropertyChanged(nameof(BindButtonText));
            OnPropertyChanged(nameof(ShowBlock));
            OnPropertyChanged(nameof(Block));
            OnPropertyChanged(nameof(ShowInvertInput));
            OnPropertyChanged(nameof(InvertInput));
            Logger.Info("Binding device changed. io=" + DeviceBinding.DeviceIoType +
                        "; category=" + DeviceBindingCategory +
                        "; from=" + (previousDeviceConfiguration?.GetFullTitleForProfile(DeviceBinding.Profile) ?? "unavailable") +
                        "; to=" + selectedDeviceConfiguration.GetFullTitleForProfile(DeviceBinding.Profile) +
                        "; compatibility=" + transfer.Compatibility +
                        "; preserved=" + DeviceBinding.IsBound);
            return transfer.Compatibility;
        }

        public void CurrentValueChanged()
        {
            if (!GuiInvalidated) return;
            OnPropertyChanged(nameof(CurrentValue));
            OnPropertyChanged(nameof(PreviewValue));
        }

        private void DeviceBindingOnPropertyChanged(object sender, PropertyChangedEventArgs propertyChangedEventArgs)
        {
            var deviceBinding = (DeviceBinding) sender;
            if (!deviceBinding.Guid.Equals(DeviceBinding.Guid)) return;

            CurrentValue = deviceBinding.CurrentValue;

            if (propertyChangedEventArgs.PropertyName.Equals(nameof(DeviceBinding.IsInBindMode)))
            {
                var bindingManager = deviceBinding.Profile?.Context?.BindingManager;
                if (bindingManager != null)
                {
                    // Only the binding currently waiting for input needs the high-frequency countdown.
                    // Keeping every mapping subscribed caused thousands of needless WPF updates per second.
                    bindingManager.PropertyChanged -= BindingManagerOnPropertyChanged;
                    if (deviceBinding.IsInBindMode) bindingManager.PropertyChanged += BindingManagerOnPropertyChanged;
                }
            }

            if (propertyChangedEventArgs.PropertyName.Equals(nameof(DeviceBinding.IsBound))
                || propertyChangedEventArgs.PropertyName.Equals(nameof(DeviceBinding.IsInBindMode)))
            {
                BindModeProgress = 0;
                OnPropertyChanged(nameof(BindButtonText));
                OnPropertyChanged(nameof(ShowPreview));
                OnPropertyChanged(nameof(ShowBindMode));
            }

            if (propertyChangedEventArgs.PropertyName.Equals(nameof(DeviceBinding.IsBound)))
            {
                SetSelectDevice();
                OnPropertyChanged(nameof(SelectedDevice));
                OnPropertyChanged(nameof(ShowBlock));
                OnPropertyChanged(nameof(Block));
                OnPropertyChanged(nameof(ShowInvertInput));
                OnPropertyChanged(nameof(InvertInput));
            }
            if (propertyChangedEventArgs.PropertyName.Equals(nameof(DeviceBinding.DeviceConfigurationGuid)))
            {
                OnPropertyChanged(nameof(BindButtonText));
                OnPropertyChanged(nameof(ShowBlock));
                OnPropertyChanged(nameof(Block));
                OnPropertyChanged(nameof(ShowInvertInput));
                OnPropertyChanged(nameof(InvertInput));
            }
            if (propertyChangedEventArgs.PropertyName.Equals(nameof(DeviceBinding.InvertInput)))
            {
                OnPropertyChanged(nameof(InvertInput));
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

        private void SubscriptionsManagerOnPropertyChanged(object sender, PropertyChangedEventArgs propertyChangedEventArgs)
        {
            if (propertyChangedEventArgs.PropertyName.Equals("ProfileActive"))
            {
                BindingEnabled = !DeviceBinding.Profile.Context.SubscriptionsManager.ProfileActive;
            }
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
                    context.SubscriptionsManager.PropertyChanged -= SubscriptionsManagerOnPropertyChanged;
                    context.DeviceAliasesChangedEvent -= ContextOnDeviceAliasesChanged;
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
