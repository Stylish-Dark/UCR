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
        public static readonly DeviceOutlineColor[] OutlineColorOptions = DeviceOutlineColors.Options;
        public DeviceOutlineColor[] AvailableOutlineColors => OutlineColorOptions;

        public Device Device { get; }
        public DeviceIoType ValidationType { get; }
        public bool CanPersist { get; }
        public string ProviderDeviceName => Device?.Title ?? "Device";
        public string ProviderName => Device?.ProviderName ?? string.Empty;
        public string IoTypes { get; private set; }
        public bool IsCachedOnly => Device?.IsCache ?? false;
        public bool HasInput { get; private set; }
        public bool HasOutput { get; private set; }
        public bool CanRemoveFromUcr => HasInput;
        public bool CanHide => HasOutput && !HasInput && CanPersist;
        public bool CanRemoveFromWindows
        {
            get
            {
                string instanceId;
                return !IsCachedOnly && DevicesManager.TryGetWindowsDeviceInstanceId(Device, out instanceId);
            }
        }
        public string RemoveFromUcrToolTip =>
            "Remove this input device from UCR. Use Detect Device to add it back.";

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
                var normalized = CanHide && value;
                if (_hidden == normalized) return;
                _hidden = normalized;
                OnPropertyChanged();
            }
        }

        private DeviceOutlineColor _outlineColor;
        public DeviceOutlineColor OutlineColor
        {
            get => _outlineColor;
            set
            {
                if (_outlineColor == value) return;
                _outlineColor = value;
                OnPropertyChanged();
            }
        }

        private string _defaultOutlineColor;
        public string DefaultOutlineColor
        {
            get => _defaultOutlineColor;
            set
            {
                var normalized = DeviceOutlineColors.NormalizeHex(value);
                if (string.Equals(_defaultOutlineColor, normalized, StringComparison.OrdinalIgnoreCase)) return;
                _defaultOutlineColor = normalized;
                OnPropertyChanged();
            }
        }

        internal string StableKey { get; }

        public DeviceManagerItemViewModel(Device device, DeviceIoType type, bool canPersist,
            string alias, bool hidden, string stableKey, DeviceOutlineColor outlineColor,
            string defaultOutlineColor)
        {
            Device = device;
            ValidationType = type;
            CanPersist = canPersist;
            Alias = alias;
            StableKey = stableKey;
            OutlineColor = outlineColor;
            DefaultOutlineColor = defaultOutlineColor;
            AddIoType(type);
            Hidden = hidden;
        }

        public void AddIoType(DeviceIoType type)
        {
            if (type == DeviceIoType.Input) HasInput = true;
            if (type == DeviceIoType.Output) HasOutput = true;

            IoTypes = HasInput && HasOutput ? "Input + Output" : HasInput ? "Input" : "Output";
            if (HasInput && _hidden)
            {
                _hidden = false;
                OnPropertyChanged(nameof(Hidden));
            }

            OnPropertyChanged(nameof(IoTypes));
            OnPropertyChanged(nameof(HasInput));
            OnPropertyChanged(nameof(HasOutput));
            OnPropertyChanged(nameof(CanRemoveFromUcr));
            OnPropertyChanged(nameof(CanHide));
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
            Devices.Clear();
            var byStableIdentity = new Dictionary<string, DeviceManagerItemViewModel>(StringComparer.OrdinalIgnoreCase);
            var allInputs = _devicesManager.GetAvailableDeviceList(DeviceIoType.Input);
            var removedInputKeys = new HashSet<string>(
                allInputs.Where(_devicesManager.IsInputRemoved)
                    .Select(BuildLogicalInstanceKey)
                    .Where(key => key != null),
                StringComparer.OrdinalIgnoreCase);

            AddDevices(DeviceIoType.Input, byStableIdentity, removedInputKeys);
            AddDevices(DeviceIoType.Output, byStableIdentity, removedInputKeys);

            var ordered = Devices
                .OrderBy(item => item.CanPersist ? _devicesManager.GetDeviceSortOrder(item.Device) : int.MaxValue)
                .ThenBy(item => item.ProviderDeviceName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            Devices.Clear();
            foreach (var item in ordered) Devices.Add(item);
            AssignUniqueDefaultOutlineColors();
        }

        private void AssignUniqueDefaultOutlineColors()
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in Devices)
            {
                var current = DeviceOutlineColors.NormalizeHex(item.DefaultOutlineColor);
                if (current == null || used.Contains(current))
                {
                    current = DeviceOutlineColors.GenerateUniqueDefault(item.StableKey, used);
                    item.DefaultOutlineColor = current;
                }
                used.Add(current);
            }
        }

        private void AddDevices(DeviceIoType type,
            IDictionary<string, DeviceManagerItemViewModel> byStableIdentity,
            ISet<string> removedInputKeys)
        {
            var liveDevices = _devicesManager.GetAvailableDeviceList(type, false);
            var devices = _devicesManager.GetAvailableDeviceList(type);
            foreach (var device in devices)
            {
                var logicalInstanceKey = BuildLogicalInstanceKey(device);
                if (type == DeviceIoType.Input && _devicesManager.IsInputRemoved(device)) continue;
                if (type == DeviceIoType.Output && logicalInstanceKey != null && removedInputKeys.Contains(logicalInstanceKey)) continue;

                var canPersist = _devicesManager.CanPersistDeviceAlias(device, liveDevices);
                var identity = DevicesManager.BuildAliasIdentity(device);
                var stableKey = canPersist && identity != null
                    ? BuildStableKey(identity)
                    : BuildEphemeralKey(device);

                DeviceManagerItemViewModel existing;
                if (byStableIdentity.TryGetValue(stableKey, out existing))
                {
                    existing.AddIoType(type);
                    continue;
                }

                var item = new DeviceManagerItemViewModel(
                    device,
                    type,
                    canPersist,
                    _devicesManager.GetDeviceAlias(device),
                    type == DeviceIoType.Output && _devicesManager.GetDeviceHidden(device),
                    stableKey,
                    _devicesManager.GetDeviceOutlineColor(device),
                    _devicesManager.GetDeviceDefaultOutlineColor(device));

                Devices.Add(item);
                byStableIdentity[stableKey] = item;
            }
        }

        private static string BuildStableKey(DeviceAlias alias)
        {
            return alias.ProviderName + "|" + alias.IdentityKind + "|" + alias.IdentityValue + "|" + alias.DeviceNumber;
        }

        private static string BuildLogicalInstanceKey(Device device)
        {
            var logicalKey = DevicesManager.BuildLogicalDeviceKey(device);
            return logicalKey == null ? null : logicalKey + "|instance|" + Math.Max(1, device.LogicalInstanceNumber);
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

                var logicalDevice = _devicesManager.RegisterDetectedInputDevice(detected) ?? detected;
                Populate();
                var item = Devices.FirstOrDefault(candidate => SameDevice(candidate.Device, logicalDevice));
                if (item == null)
                {
                    DetectionStatus = $"Detected: {_devicesManager.GetDisplayTitle(logicalDevice)}";
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
            return DevicesManager.DescriptorEquals(left, right) ||
                   (DevicesManager.LogicalIdentityEquals(left, right) &&
                    left.LogicalInstanceNumber == right.LogicalInstanceNumber);
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

        public bool RemoveFromUcr(DeviceManagerItemViewModel item)
        {
            if (item == null || !item.CanRemoveFromUcr) return false;
            if (!_devicesManager.RemoveInputDevice(item.Device)) return false;
            Devices.Remove(item);
            if (ReferenceEquals(SelectedDevice, item)) SelectedDevice = null;
            DetectionStatus = "Removed from UCR: " + item.ProviderDeviceName + ". Use Detect Device to add it back.";
            return true;
        }

        public void Refresh()
        {
            _devicesManager.RefreshDeviceList();
            Populate();
        }

        public bool Apply(out string error)
        {
            error = null;
            for (var index = 0; index < Devices.Count; index++)
            {
                var item = Devices[index];
                if (!item.CanPersist) continue;

                var hidden = item.CanHide && item.Hidden;
                if (_devicesManager.TrySetDevicePresentation(item.Device, item.ValidationType,
                        item.Alias, hidden, index, item.OutlineColor, item.DefaultOutlineColor, out error)) continue;

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
