using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using HidWizards.UCR.Core.Annotations;
using HidWizards.UCR.Core.Managers;
using HidWizards.UCR.Core.Models;
using HidWizards.UCR.Core.Utilities;

namespace HidWizards.UCR.ViewModels.Dashboard
{
    public class DeviceManagerItemViewModel : INotifyPropertyChanged
    {
        public Device Device { get; }
        public DeviceIoType ValidationType { get; }
        public bool CanPersist { get; }
        public string ProviderDeviceName => Device?.Title ?? "Device";
        public string ProviderName => Device?.ProviderName ?? string.Empty;
        public string IoTypes { get; private set; }
        public string IdentityNote { get; }

        private string _alias;
        public string Alias
        {
            get => _alias;
            set
            {
                if (_alias == value) return;
                _alias = value;
                OnPropertyChanged();
            }
        }

        private bool _hidden;
        public bool Hidden
        {
            get => _hidden;
            set
            {
                if (_hidden == value) return;
                _hidden = value;
                OnPropertyChanged();
            }
        }

        internal string StableKey { get; }

        public DeviceManagerItemViewModel(Device device, DeviceIoType type, bool canPersist,
            string alias, bool hidden, string stableKey)
        {
            Device = device;
            ValidationType = type;
            CanPersist = canPersist;
            Alias = alias;
            // If UCR cannot identify this unit reliably enough to persist presentation settings,
            // it is unsafe to offer it as a selectable device. Keep it visible here for diagnosis,
            // but lock it in the hidden state.
            Hidden = canPersist ? hidden : true;
            StableKey = stableKey;
            IoTypes = type == DeviceIoType.Input ? "Input" : "Output";
            IdentityNote = canPersist
                ? "Persistent identity available"
                : "Provider cannot uniquely identify this unit; it is forced hidden from device selection lists.";
        }

        public void AddIoType(DeviceIoType type)
        {
            var label = type == DeviceIoType.Input ? "Input" : "Output";
            if (IoTypes.IndexOf(label, StringComparison.OrdinalIgnoreCase) >= 0) return;
            IoTypes += " + " + label;
            OnPropertyChanged(nameof(IoTypes));
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class DeviceManagerViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly DevicesManager _devicesManager;
        private CancellationTokenSource _detectionCancellation;
        private bool _disposed;
        private bool _isDetecting;
        private string _detectionStatus;
        private DeviceManagerItemViewModel _selectedDevice;

        public ObservableCollection<DeviceManagerItemViewModel> Devices { get; }
        public DeviceManagerViewModel ViewModel => this;

        public DeviceManagerItemViewModel SelectedDevice
        {
            get => _selectedDevice;
            set
            {
                if (_selectedDevice == value) return;
                _selectedDevice = value;
                OnPropertyChanged();
            }
        }

        public bool IsDetecting
        {
            get => _isDetecting;
            private set
            {
                if (_isDetecting == value) return;
                _isDetecting = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DetectionButtonText));
            }
        }

        public string DetectionButtonText => IsDetecting ? "CANCEL DETECTION" : "DETECT DEVICE";

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

        public DeviceManagerViewModel(DevicesManager devicesManager)
        {
            _devicesManager = devicesManager;
            Devices = new ObservableCollection<DeviceManagerItemViewModel>();
            Populate();
        }

        private void Populate()
        {
            var byStableIdentity = new Dictionary<string, DeviceManagerItemViewModel>(StringComparer.OrdinalIgnoreCase);
            AddDevices(DeviceIoType.Input, byStableIdentity);
            AddDevices(DeviceIoType.Output, byStableIdentity);

            var ordered = Devices
                .OrderBy(item => item.CanPersist ? _devicesManager.GetDeviceSortOrder(item.Device) : int.MaxValue)
                .ThenBy(item => item.ProviderDeviceName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            Devices.Clear();
            foreach (var item in ordered) Devices.Add(item);
        }

        private void AddDevices(DeviceIoType type, IDictionary<string, DeviceManagerItemViewModel> byStableIdentity)
        {
            var liveDevices = _devicesManager.GetAvailableDeviceList(type, false);
            var devices = _devicesManager.GetAvailableDeviceList(type);
            foreach (var device in devices)
            {
                var canPersist = _devicesManager.CanPersistDeviceAlias(device, liveDevices);
                var identity = DevicesManager.BuildAliasIdentity(device);
                var stableKey = canPersist && identity != null
                    ? BuildStableKey(identity)
                    : BuildEphemeralKey(device, type);

                if (canPersist && byStableIdentity.TryGetValue(stableKey, out var existing))
                {
                    existing.AddIoType(type);
                    continue;
                }

                var item = new DeviceManagerItemViewModel(
                    device,
                    type,
                    canPersist,
                    _devicesManager.GetDeviceAlias(device),
                    _devicesManager.GetDeviceHidden(device),
                    stableKey);

                Devices.Add(item);
                if (canPersist) byStableIdentity[stableKey] = item;
            }
        }

        private static string BuildStableKey(DeviceAlias alias)
        {
            return alias.ProviderName + "|" + alias.IdentityKind + "|" + alias.IdentityValue + "|" + alias.DeviceNumber;
        }

        private static string BuildEphemeralKey(Device device, DeviceIoType type)
        {
            return type + "|" + device.ProviderName + "|" + device.DeviceHandle + "|" + device.DeviceNumber + "|" + device.HidPath;
        }

        public async Task<DeviceManagerItemViewModel> DetectInputDeviceAsync()
        {
            if (_disposed) return null;

            if (IsDetecting)
            {
                _detectionCancellation?.Cancel();
                return null;
            }

            var cancellation = new CancellationTokenSource();
            _detectionCancellation = cancellation;
            IsDetecting = true;
            DetectionStatus = "Listening — press a button or key on the device…";

            try
            {
                var detected = await _devicesManager.DetectInputDeviceAsync(TimeSpan.FromSeconds(8),
                    cancellation.Token);

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

                var item = Devices.FirstOrDefault(candidate => SameDevice(candidate.Device, detected));
                if (item == null)
                {
                    DetectionStatus = $"Detected: {detected.DisplayTitle} — device list may need refreshing.";
                    return null;
                }

                SelectedDevice = item;
                DetectionStatus = $"Detected: {_devicesManager.GetDisplayTitle(item.Device)}";
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
                Logger.Error("Input-device detection failed in Devices", exception);
                return null;
            }
            finally
            {
                IsDetecting = false;
                cancellation.Dispose();
                if (ReferenceEquals(_detectionCancellation, cancellation)) _detectionCancellation = null;
            }
        }

        private static bool SameDevice(Device left, Device right)
        {
            return DevicesManager.PersistedIdentityEquals(left, right) || DevicesManager.DescriptorEquals(left, right);
        }

        public bool Move(DeviceManagerItemViewModel item, int offset)
        {
            if (item == null || !item.CanPersist) return false;
            var sourceIndex = Devices.IndexOf(item);
            var targetIndex = sourceIndex + offset;
            if (sourceIndex < 0 || targetIndex < 0 || targetIndex >= Devices.Count) return false;
            Devices.Move(sourceIndex, targetIndex);
            return true;
        }

        public bool Apply(out string error)
        {
            error = null;
            for (var index = 0; index < Devices.Count; index++)
            {
                var item = Devices[index];
                if (!item.CanPersist) continue;

                if (_devicesManager.TrySetDevicePresentation(item.Device, item.ValidationType,
                        item.Alias, item.Hidden, index, out error)) continue;

                return false;
            }

            return true;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_detectionCancellation != null)
            {
                _detectionCancellation.Cancel();
                _devicesManager?.CancelInputDeviceDetection();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
