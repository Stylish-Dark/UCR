using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using HidWizards.UCR.Core.Annotations;
using HidWizards.UCR.Core.Managers;
using HidWizards.UCR.Core.Models;
using HidWizards.UCR.Core.Utilities;
using HidWizards.UCR.ViewModels.Controls;
using HidWizards.UCR.ViewModels.DeviceViewModels;

namespace HidWizards.UCR.ViewModels.Dashboard
{
    public class AddDevicesDialogViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly DevicesManager _devicesManager;
        private readonly DeviceIoType _deviceIoType;
        private CancellationTokenSource _detectionCancellation;
        private bool _disposed;
        private bool _isDetecting;
        private string _detectionStatus;

        public DeviceSelectControlViewModel Devices { get; set; }
        public AddDevicesDialogViewModel ViewModel { get; set; }
        public bool CanDetectDevice => _deviceIoType == DeviceIoType.Input && _devicesManager != null;
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

        public AddDevicesDialogViewModel()
        {
        }

        public AddDevicesDialogViewModel(List<Device> devices, DeviceIoType deviceIoType, DevicesManager devicesManager)
        {
            _devicesManager = devicesManager;
            _deviceIoType = deviceIoType;
            Devices = new DeviceSelectControlViewModel($"Add {(deviceIoType == DeviceIoType.Input ? "input" : "output")} devices", devices);
            ViewModel = this;
        }

        public async Task<DeviceViewModel> DetectDeviceAsync()
        {
            if (!CanDetectDevice || _disposed) return null;

            if (IsDetecting)
            {
                _detectionCancellation?.Cancel();
                return null;
            }

            foreach (var item in Devices.Devices) item.IsDetected = false;

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

                var item = Devices.Devices.FirstOrDefault(candidate => SameDevice(candidate.Device, detected));
                if (item == null)
                {
                    DetectionStatus = $"Detected: {detected.DisplayTitle} — already added, hidden, or unavailable in this list.";
                    return null;
                }

                item.IsDetected = true;
                item.Checked = true;
                DetectionStatus = $"Detected: {item.Title}";
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
                Logger.Error("Input-device detection failed in Add Devices", exception);
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
