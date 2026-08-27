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
        public bool IsCachedOnly => Device?.IsCache ?? false;
        public bool CanForget => IsCachedOnly;
        public bool CanDismiss => Device != null && !IsCachedOnly;
        public bool CanRemoveFromWindows
        {
            get
            {
                string instanceId;
                return !IsCachedOnly && DevicesManager.TryGetWindowsDeviceInstanceId(Device, out instanceId);
            }
        }
        public string RemoveFromUcrToolTip => IsCachedOnly
            ? "Forget this cached/disconnected UCR record"
            : "Remove this live device from UCR selection lists for this session";

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
            // Runtime selectability and persistent presentation settings are deliberately separate.
            // Some providers can tell UCR exactly which live slot produced input without exposing a
            // stable identity that survives enumeration changes. Keep those devices usable now, while
            // disabling only the metadata that would be unsafe to persist.
            Hidden = canPersist && hidden;
            StableKey = stableKey;
            IoTypes = type == DeviceIoType.Input ? "Input" : "Output";
            IdentityNote = IsCachedOnly
                ? "Cached/disconnected device record"
                : canPersist
                    ? "Persistent identity available"
                    : "Session identity only — selectable now, but friendly name/hide/order cannot be persisted reliably.";
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
            // Clean automatically whenever the manager opens; the user should not have to manually
            // remove cache copies that correspond to endpoints Windows is already reporting live.
            _devicesManager.RemoveStaleDeviceCacheCopies();
            Populate();
        }

        private void Populate()
        {
            Devices.Clear();
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
                if (_devicesManager.IsSessionDismissed(device)) continue;
                var canPersist = _devicesManager.CanPersistDeviceAlias(device, liveDevices);
                var identity = DevicesManager.BuildAliasIdentity(device);
                var stableKey = canPersist && identity != null
                    ? BuildStableKey(identity)
                    : BuildEphemeralKey(device);

                if (byStableIdentity.TryGetValue(stableKey, out var existing))
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
                byStableIdentity[stableKey] = item;
            }
        }

        private static string BuildStableKey(DeviceAlias alias)
        {
            return alias.ProviderName + "|" + alias.IdentityKind + "|" + alias.IdentityValue + "|" + alias.DeviceNumber;
        }

        private static string BuildEphemeralKey(Device device)
        {
            return "runtime|" + device.ProviderName + "|" + device.DeviceHandle + "|" + device.DeviceNumber + "|" + device.HidPath;
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

        public int RemoveStaleCacheCopies()
        {
            var removed = _devicesManager.RemoveStaleDeviceCacheCopies();
            Populate();
            DetectionStatus = removed == 1
                ? "Removed 1 stale cached device record."
                : "Removed " + removed + " stale cached device records.";
            return removed;
        }

        public bool ForgetCachedDevice(DeviceManagerItemViewModel item, out string error)
        {
            error = null;
            if (item == null) return false;
            if (!_devicesManager.ForgetCachedDevice(item.Device, out error)) return false;
            Devices.Remove(item);
            if (ReferenceEquals(SelectedDevice, item)) SelectedDevice = null;
            return true;
        }

        public bool DismissLiveDevice(DeviceManagerItemViewModel item)
        {
            if (item == null || item.IsCachedOnly) return false;
            if (!_devicesManager.DismissDeviceForSession(item.Device)) return false;
            Devices.Remove(item);
            if (ReferenceEquals(SelectedDevice, item)) SelectedDevice = null;
            DetectionStatus = "Removed from UCR lists for this session: " + item.ProviderDeviceName;
            return true;
        }

        public void Refresh()
        {
            _devicesManager.RefreshDeviceList();
            _devicesManager.RemoveStaleDeviceCacheCopies();
            Populate();
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
