using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using HidWizards.UCR.Core.Annotations;
using HidWizards.UCR.Core.Managers;
using HidWizards.UCR.Core.Models;
using HidWizards.UCR.Core.Utilities;
using HidWizards.UCR.Views.Dialogs;
using MaterialDesignThemes.Wpf;

namespace HidWizards.UCR.ViewModels.Dashboard
{
    public class ProfileDeviceListControlViewModel : INotifyPropertyChanged, IDisposable
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
        private readonly string _dialogIdentifier;
        private CancellationTokenSource _detectionCancellation;
        private DispatcherTimer _detectionUiTimer;
        private DateTime _detectionDeadlineUtc;
        private bool _isDetecting;
        private double _detectionSecondsRemaining;
        private double _detectionProgress;
        private bool _disposed;

        public bool IsDetecting
        {
            get => _isDetecting;
            private set
            {
                if (_isDetecting == value) return;
                _isDetecting = value;
                OnPropertyChanged();
            }
        }

        public double DetectionSecondsRemaining
        {
            get => _detectionSecondsRemaining;
            private set
            {
                if (Math.Abs(_detectionSecondsRemaining - value) < 0.01) return;
                _detectionSecondsRemaining = value;
                OnPropertyChanged();
            }
        }

        public double DetectionProgress
        {
            get => _detectionProgress;
            private set
            {
                if (Math.Abs(_detectionProgress - value) < 0.01) return;
                _detectionProgress = value;
                OnPropertyChanged();
            }
        }

        private string _detectionStatus;
        public string DetectionStatus
        {
            get => _detectionStatus;
            private set
            {
                if (_detectionStatus == value) return;
                _detectionStatus = value;
                OnPropertyChanged();
            }
        }

        public ProfileDeviceListControlViewModel()
        {
        }

        public ProfileDeviceListControlViewModel(Profile profile, List<DeviceConfiguration> devices, DeviceIoType deviceIoType,
            Action presentationChanged = null, string dialogIdentifier = "RootDialog")
        {
            _profile = profile;
            _deviceIoType = deviceIoType;
            _presentationChanged = presentationChanged;
            _dialogIdentifier = string.IsNullOrWhiteSpace(dialogIdentifier) ? "RootDialog" : dialogIdentifier;
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

        public void RemoveDevice(DeviceItem deviceItem)
        {
            if (deviceItem == null) return;
            var wasPrimary = deviceItem.IsPrimary;
            var result = HidWizards.UCR.Utilities.DarkMessageBox.Show(
                "Remove " + deviceItem.Title + " from this profile? Existing mappings that use it may become unbound.",
                "Remove device", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

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
            var dialog = new AddDevicesDialog(deviceList, _deviceIoType, _profile.Context.DevicesManager);
            var result = (AddDevicesDialogViewModel)await DialogHost.Show(dialog, _dialogIdentifier);
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

        public async Task<DeviceItem> DetectAndAddInputDeviceAsync()
        {
            if (_disposed || _profile == null || _deviceIoType != DeviceIoType.Input) return null;

            if (IsDetecting)
            {
                _detectionCancellation?.Cancel();
                DetectionStatus = "Detection cancelled.";
                return null;
            }

            var timeout = TimeSpan.FromSeconds(8);
            var cancellation = new CancellationTokenSource();
            _detectionCancellation = cancellation;
            StartDetectionCountdown(timeout);

            try
            {
                var detected = await _profile.Context.DevicesManager.DetectInputDeviceAsync(timeout, cancellation.Token);
                if (cancellation.IsCancellationRequested)
                {
                    DetectionStatus = "Detection cancelled.";
                    return null;
                }
                if (detected == null)
                {
                    DetectionStatus = "No button or key press detected.";
                    return null;
                }

                var logicalDevice = _profile.Context.DevicesManager.RegisterDetectedInputDevice(detected) ?? detected;
                var existing = Devices.FirstOrDefault(deviceItem => SameDevice(deviceItem.DeviceConfiguration?.Device, logicalDevice));
                if (existing != null)
                {
                    SelectedDeviceConfiguration = existing;
                    DetectionStatus = "Detected: " + existing.Title;
                    return existing;
                }

                var candidate = _profile.GetMissingDeviceList(DeviceIoType.Input)
                    .FirstOrDefault(device => SameDevice(device, logicalDevice)) ?? logicalDevice;
                var configuration = new DeviceConfiguration(candidate);
                _profile.AddDeviceConfigurations(new List<DeviceConfiguration> { configuration }, DeviceIoType.Input);
                var item = new DeviceItem(configuration, _profile, DeviceIoType.Input);
                Devices.Add(item);
                if (_profile.GetPrimaryDeviceConfiguration(DeviceIoType.Input)?.Guid == configuration.Guid)
                {
                    var index = Devices.IndexOf(item);
                    if (index > 0) Devices.Move(index, 0);
                }
                RefreshPrimaryState();
                SelectedDeviceConfiguration = item;
                _presentationChanged?.Invoke();
                DetectionStatus = "Detected and added: " + item.Title;
                return item;
            }
            catch (InvalidOperationException exception)
            {
                DetectionStatus = exception.Message;
                return null;
            }
            catch (Exception exception)
            {
                DetectionStatus = "Device detection failed. Check the UCR log for details.";
                Logger.Error("Input-device detection failed in profile editor", exception);
                return null;
            }
            finally
            {
                StopDetectionCountdown();
                if (ReferenceEquals(_detectionCancellation, cancellation)) _detectionCancellation = null;
                cancellation.Dispose();
            }
        }

        private void StartDetectionCountdown(TimeSpan timeout)
        {
            _detectionDeadlineUtc = DateTime.UtcNow.Add(timeout);
            IsDetecting = true;
            UpdateDetectionCountdown();
            _detectionUiTimer?.Stop();
            _detectionUiTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _detectionUiTimer.Tick += DetectionUiTimerOnTick;
            _detectionUiTimer.Start();
        }

        private void DetectionUiTimerOnTick(object sender, EventArgs e)
        {
            UpdateDetectionCountdown();
        }

        private void UpdateDetectionCountdown()
        {
            var remaining = Math.Max(0, (_detectionDeadlineUtc - DateTime.UtcNow).TotalSeconds);
            DetectionSecondsRemaining = remaining;
            DetectionProgress = Math.Max(0, Math.Min(100, remaining / 8.0 * 100.0));
            DetectionStatus = "Listening — press a button or key — " + remaining.ToString("0.0") + "s";
        }

        private void StopDetectionCountdown()
        {
            if (_detectionUiTimer != null)
            {
                _detectionUiTimer.Stop();
                _detectionUiTimer.Tick -= DetectionUiTimerOnTick;
                _detectionUiTimer = null;
            }
            IsDetecting = false;
            DetectionSecondsRemaining = 0;
            DetectionProgress = 0;
        }

        private static bool SameDevice(Device left, Device right)
        {
            if (DeviceIdentity.SelectionEquals(left, right)) return true;
            if (left == null || right == null) return false;

            // Handle-only matching is appropriate when reconciling a stale cache entry with a live
            // endpoint, but must never merge two simultaneously live identical keyboards/mice.
            return left.LogicalInstanceNumber == right.LogicalInstanceNumber &&
                   left.IsCache != right.IsCache &&
                   DeviceIdentity.CacheRepresentsLiveEndpoint(left, right);
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
            var result = (ManageDeviceConfigurationViewModel)await DialogHost.Show(dialog, _dialogIdentifier);
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
            _presentationChanged?.Invoke();
        }


        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            StopDetectionCountdown();
            if (_detectionCancellation != null)
            {
                _detectionCancellation.Cancel();
                _profile?.Context.DevicesManager.CancelInputDeviceDetection();
                _detectionCancellation.Dispose();
                _detectionCancellation = null;
            }
            if (_profile != null) _profile.Context.DeviceAliasesChangedEvent -= ContextOnDeviceAliasesChanged;
        }

        private bool CanManageDeviceConfiguration()
        {
            if (SelectedDeviceConfiguration == null) return false;
            return true;
        }
    }
}
