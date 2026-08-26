using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using HidWizards.UCR.Core.Annotations;
using HidWizards.UCR.Core.Models;
using HidWizards.UCR.Views.Dialogs;
using MaterialDesignThemes.Wpf;

namespace HidWizards.UCR.ViewModels.Dashboard
{
    public class ProfileDeviceListControlViewModel : INotifyPropertyChanged
    {
        public ObservableCollection<DeviceItem> Devices { get; set; }
        public bool IsRemoveEnabled => CanRemoveDevice();
        public bool IsConfigurationEnabled => CanManageDeviceConfiguration();
        private DeviceItem _deviceConfiguration;
        public DeviceItem SelectedDeviceConfiguration
        {
            get => _deviceConfiguration;
            set
            {
                _deviceConfiguration = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsRemoveEnabled));
                OnPropertyChanged(nameof(IsConfigurationEnabled));
            }
        }

        private readonly Profile _profile;
        private readonly DeviceIoType _deviceIoType;
        private readonly Action _presentationChanged;

        public ProfileDeviceListControlViewModel()
        {
        }

        public ProfileDeviceListControlViewModel(Profile profile, List<DeviceConfiguration> devices, DeviceIoType deviceIoType, Action presentationChanged = null)
        {
            _profile = profile;
            _deviceIoType = deviceIoType;
            _presentationChanged = presentationChanged;
            _profile.Context.DeviceAliasesChangedEvent += ContextOnDeviceAliasesChanged;
            Devices = new ObservableCollection<DeviceItem>();

            var primary = _profile.GetPrimaryDeviceConfiguration(_deviceIoType);
            foreach (var device in (devices ?? new List<DeviceConfiguration>())
                .OrderBy(configuration => primary != null && configuration.Guid == primary.Guid ? 0 : 1))
            {
                Devices.Add(new DeviceItem(device, profile, deviceIoType));
            }

            RefreshPrimaryState();
        }

        private void ContextOnDeviceAliasesChanged()
        {
            if (Devices == null) return;
            foreach (var device in Devices) device.TitleChanged();
            OnPropertyChanged(nameof(Devices));
        }

        private bool CanRemoveDevice()
        {
            if (SelectedDeviceConfiguration == null) return false;
            return _profile.CanRemoveDeviceConfiguration(SelectedDeviceConfiguration.DeviceConfiguration);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public async void RemoveDevice(DeviceItem deviceItem)
        {
            if (deviceItem == null) return;
            var wasPrimary = deviceItem.IsPrimary;
            var dialog = new BoolDialog("Remove device", $"Are you sure you want to remove {deviceItem.Title} from {deviceItem.DeviceConfiguration.Device.Profile.Title}?");
            var result = (bool?)await DialogHost.Show(dialog, "RootDialog");
            if (result == null || !result.Value) return;

            _profile.RemoveDeviceConfiguration(deviceItem.DeviceConfiguration);
            Devices.Remove(deviceItem);

            if (wasPrimary)
            {
                _profile.SetPrimaryDeviceConfiguration(_deviceIoType, Guid.Empty);
            }

            RefreshPrimaryState();
            _presentationChanged?.Invoke();
            OnPropertyChanged(nameof(Devices));
        }

        public async void AddDevices()
        {
            var deviceList = _profile.GetMissingDeviceList(_deviceIoType);
            var dialog = new AddDevicesDialog(deviceList, _deviceIoType);
            var result = (AddDevicesDialogViewModel)await DialogHost.Show(dialog, "RootDialog");
            if (result?.Devices == null) return;

            var deviceConfigurations = result.Devices.GetSelectedDevices().Select(d => new DeviceConfiguration(d.Device)).ToList();
            _profile.AddDeviceConfigurations(deviceConfigurations, _deviceIoType);
            foreach (var deviceConfiguration in deviceConfigurations)
            {
                Devices.Add(new DeviceItem(deviceConfiguration, _profile, _deviceIoType));
            }
            RefreshPrimaryState();
            _presentationChanged?.Invoke();
            OnPropertyChanged(nameof(Devices));
        }

        public void SetPrimaryDevice(DeviceItem deviceItem)
        {
            if (deviceItem == null) return;
            if (!_profile.SetPrimaryDeviceConfiguration(_deviceIoType, deviceItem.DeviceConfiguration.Guid)) return;

            var currentIndex = Devices.IndexOf(deviceItem);
            if (currentIndex > 0) Devices.Move(currentIndex, 0);

            RefreshPrimaryState();
            SelectedDeviceConfiguration = deviceItem;
            _presentationChanged?.Invoke();
        }

        private void RefreshPrimaryState()
        {
            if (Devices == null) return;

            var primary = _profile.GetPrimaryDeviceConfiguration(_deviceIoType);
            if (primary != null)
            {
                var primaryItem = Devices.FirstOrDefault(device => device.DeviceConfiguration.Guid == primary.Guid);
                var primaryIndex = primaryItem != null ? Devices.IndexOf(primaryItem) : -1;
                if (primaryIndex > 0) Devices.Move(primaryIndex, 0);
            }

            foreach (var device in Devices) device.PrimaryChanged();
        }

        public async void ManageDeviceConfiguration()
        {
            var dialog = new ManageDeviceConfigurationDialog(SelectedDeviceConfiguration.DeviceConfiguration, _deviceIoType);
            var result = (ManageDeviceConfigurationViewModel)await DialogHost.Show(dialog, "RootDialog");
            if (result == null || !result.HasChanged) return;

            var configuration = SelectedDeviceConfiguration.DeviceConfiguration;
            string aliasError;
            if (!configuration.Device.Profile.Context.DevicesManager.TrySetDeviceAlias(
                    configuration.Device, _deviceIoType, result.DeviceAlias, out aliasError))
            {
                HidWizards.UCR.Utilities.DarkMessageBox.Show(aliasError, "Device name not changed",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }

            configuration.ChangeConfigurationName(result.DeviceConfigurationName);
            configuration.ChangeShadowDevices(result.GetSelectedShadowDevices());

            SelectedDeviceConfiguration.TitleChanged();
            OnPropertyChanged(nameof(Devices));
        }

        private bool CanManageDeviceConfiguration()
        {
            if (SelectedDeviceConfiguration == null) return false;
            return true;
        }
    }
}
