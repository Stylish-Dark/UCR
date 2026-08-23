using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using HidWizards.IOWrapper.DataTransferObjects;
using HidWizards.UCR.Core.Annotations;
using HidWizards.UCR.Core.Models;
using HidWizards.UCR.Core.Models.Binding;
using HidWizards.UCR.Core.Utilities;
using NLog;
using Logger = NLog.Logger;

namespace HidWizards.UCR.Core.Managers
{
    public sealed class BindingManager : IDisposable, INotifyPropertyChanged
    {
        private double _bindModeProgress = 0;

        public double BindModeProgress
        {
            get { return _bindModeProgress / BindModeTime * 100.0; }
            set
            {
                _bindModeProgress = value;
                OnPropertyChanged();
            }
        }

        private static readonly double BindModeTime = 5000.0;
        private static readonly int BindModeTick = 20;
        private static readonly TimeSpan BindArmDelay = TimeSpan.FromMilliseconds(150);
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly Context _context;
        private List<DeviceConfiguration> _deviceConfigurationList;
        private Dictionary<Guid, Device> _bindModeRuntimeDevices;
        private DeviceBinding _deviceBinding;
        private DispatcherTimer BindingTimer;
        private readonly object bindmodeLock = new object();
        private readonly Dispatcher _dispatcher;
        private bool bindmodeActive;
        private bool bindCommitPending;
        private DateTime bindAcceptAfterUtc = DateTime.MaxValue;

        public delegate void EndBindModeDelegate(DeviceBinding deviceBinding);
        public event EndBindModeDelegate EndBindModeHandler;

        public BindingManager(Context context)
        {
            _context = context;
            _deviceConfigurationList = new List<DeviceConfiguration>();
            _bindModeRuntimeDevices = new Dictionary<Guid, Device>();
            _dispatcher = Dispatcher.CurrentDispatcher;
            Logger.Debug("Binding manager initialized");
        }

        public void BeginBindMode(DeviceBinding deviceBinding)
        {
            if (deviceBinding == null) throw new ArgumentNullException(nameof(deviceBinding));

            // BindingManager is created on UCR's UI dispatcher. All state transitions and all
            // INotifyPropertyChanged mutations are serialized back onto that dispatcher.
            if (!_dispatcher.CheckAccess())
            {
                _dispatcher.BeginInvoke(new Action(() => BeginBindMode(deviceBinding)), DispatcherPriority.Input);
                return;
            }

            if (bindmodeActive) EndBindMode();

            lock (bindmodeLock)
            {
                _deviceBinding = deviceBinding;
                _deviceConfigurationList = new List<DeviceConfiguration>();
                _bindModeRuntimeDevices = new Dictionary<Guid, Device>();
                bindCommitPending = false;
                bindAcceptAfterUtc = DateTime.MaxValue;
                bindmodeActive = true;
            }

            Logger.Debug($"Begin bind mode: binding={deviceBinding.Guid}, io={deviceBinding.DeviceIoType}, category={deviceBinding.DeviceBindingCategory}");

            try
            {
                foreach (var deviceConfiguration in deviceBinding.Profile.GetDeviceConfigurationList(deviceBinding.DeviceIoType))
                {
                    var runtimeDevice = _context.DevicesManager.ResolveDevice(deviceConfiguration.Device, deviceBinding.DeviceIoType);
                    if (runtimeDevice == null)
                    {
                        Logger.Debug($"Bind mode skipped unavailable device configuration {deviceConfiguration.Guid}");
                        continue;
                    }

                    Logger.Debug($"Bind mode enabling detection: provider={runtimeDevice.ProviderName}, handle={runtimeDevice.DeviceHandle}, instance={runtimeDevice.DeviceNumber}, configuration={deviceConfiguration.Guid}");
                    _context.IOController.SetDetectionMode(DetectionMode.Bind, GetProviderDescriptor(runtimeDevice), GetDeviceDescriptor(runtimeDevice), InputChanged);
                    _deviceConfigurationList.Add(deviceConfiguration);
                    _bindModeRuntimeDevices[deviceConfiguration.Guid] = runtimeDevice;
                }

                // Do not let the mouse/key event that clicked the Bind button become the new
                // binding. WPF raises Button.Click at the tail of that same input gesture, while
                // Interception can still deliver the gesture to detection mode immediately after.
                bindAcceptAfterUtc = DateTime.UtcNow.Add(BindArmDelay);

                BindingTimer?.Stop();
                BindingTimer = new DispatcherTimer(DispatcherPriority.Render, _dispatcher);
                BindingTimer.Tick += BindingTimerOnTick;
                BindingTimer.Interval = TimeSpan.FromMilliseconds(BindModeTick);
                BindModeProgress = BindModeTime;
                BindingTimer.Start();
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "Failed while entering bind mode");
                EndBindMode();
                throw;
            }
        }

        private void BindingTimerOnTick(object sender, EventArgs e)
        {
            BindModeProgress = _bindModeProgress - BindModeTick;
            if (BindModeProgress <= 0.0) EndBindMode();
        }

        private void EndBindMode()
        {
            if (!_dispatcher.CheckAccess())
            {
                _dispatcher.BeginInvoke(new Action(EndBindMode), DispatcherPriority.Input);
                return;
            }

            List<DeviceConfiguration> configurations;
            Dictionary<Guid, Device> runtimeDevices;
            DeviceBinding endingBinding;

            lock (bindmodeLock)
            {
                if (!bindmodeActive) return;

                bindmodeActive = false;
                bindCommitPending = false;
                bindAcceptAfterUtc = DateTime.MaxValue;
                endingBinding = _deviceBinding;
                configurations = new List<DeviceConfiguration>(_deviceConfigurationList);
                runtimeDevices = new Dictionary<Guid, Device>(_bindModeRuntimeDevices);
                _deviceConfigurationList = new List<DeviceConfiguration>();
                _bindModeRuntimeDevices = new Dictionary<Guid, Device>();
                _deviceBinding = null;
            }

            BindingTimer?.Stop();
            Logger.Debug($"End bind mode: binding={endingBinding?.Guid}");

            foreach (var deviceConfiguration in configurations)
            {
                Device runtimeDevice;
                if (!runtimeDevices.TryGetValue(deviceConfiguration.Guid, out runtimeDevice)) continue;

                try
                {
                    _context.IOController.SetDetectionMode(DetectionMode.Subscription,
                        GetProviderDescriptor(runtimeDevice), GetDeviceDescriptor(runtimeDevice));
                }
                catch (Exception exception)
                {
                    Logger.Error(exception, $"Failed to leave bind mode for provider={runtimeDevice.ProviderName}, handle={runtimeDevice.DeviceHandle}, instance={runtimeDevice.DeviceNumber}");
                }
            }

            try
            {
                EndBindModeHandler?.Invoke(endingBinding);
            }
            catch (Exception exception)
            {
                Logger.Error(exception, $"Bind-mode completion handler failed for binding={endingBinding?.Guid}");
            }
        }

        private DeviceDescriptor GetDeviceDescriptor(Device device)
        {
            return new DeviceDescriptor()
            {
                DeviceHandle = device.DeviceHandle,
                DeviceInstance = device.DeviceNumber
            };
        }

        private ProviderDescriptor GetProviderDescriptor(Device device)
        {
            return new ProviderDescriptor()
            {
                ProviderName = device.ProviderName
            };
        }

        private void InputChanged(ProviderDescriptor providerDescriptor, DeviceDescriptor deviceDescriptor, BindingReport bindingReport, short value)
        {
            if (!_dispatcher.CheckAccess())
            {
                // Core Interception and several other providers deliver detection callbacks from worker/
                // timer threads. Mutating DeviceBinding directly there can raise WPF PropertyChanged events
                // off-thread and crash the application. Marshal the entire commit to UCR's UI dispatcher.
                try
                {
                    _dispatcher.BeginInvoke(new Action(() => InputChanged(providerDescriptor, deviceDescriptor, bindingReport, value)), DispatcherPriority.Input);
                }
                catch (InvalidOperationException exception)
                {
                    Logger.Error(exception, "Could not marshal detected input to the UCR dispatcher");
                }
                return;
            }

            lock (bindmodeLock)
            {
                if (!bindmodeActive || bindCommitPending || _deviceBinding == null) return;
                if (DateTime.UtcNow < bindAcceptAfterUtc) return;
            }

            if (bindingReport == null) return;
            if (!DeviceBinding.MapCategory(bindingReport.Category).Equals(_deviceBinding.DeviceBindingCategory)) return;
            if (!IsInputValid(bindingReport.Category, value)) return;

            var deviceConfiguration = FindDeviceConfiguration(providerDescriptor, deviceDescriptor);
            if (deviceConfiguration == null) return;

            lock (bindmodeLock)
            {
                if (!bindmodeActive || bindCommitPending || _deviceBinding == null) return;
                bindCommitPending = true;
            }

            try
            {
                Logger.Debug($"Bind input accepted: provider={providerDescriptor?.ProviderName}, handle={deviceDescriptor.DeviceHandle}, instance={deviceDescriptor.DeviceInstance}, type={bindingReport.BindingDescriptor.Type}, index={bindingReport.BindingDescriptor.Index}, subIndex={bindingReport.BindingDescriptor.SubIndex}, value={value}");
                _deviceBinding.SetDeviceConfigurationGuid(deviceConfiguration.Guid);
                _deviceBinding.SetKeyTypeValue((int)bindingReport.BindingDescriptor.Type,
                    bindingReport.BindingDescriptor.Index, bindingReport.BindingDescriptor.SubIndex);
                EndBindMode();
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "Failed to commit detected input binding");
                lock (bindmodeLock) bindCommitPending = false;
                // A failed rebind should never take down the whole application. End detection cleanly;
                // the improved crash/bind logging above preserves the diagnostic information.
                EndBindMode();
            }
        }

        private bool IsInputValid(BindingCategory bindingCategory, short value)
        {
            switch (DeviceBinding.MapCategory(bindingCategory))
            {
                case DeviceBindingCategory.Delta:
                case DeviceBindingCategory.Event:
                    return true;
                case DeviceBindingCategory.Momentary:
                    return value != 0;
                case DeviceBindingCategory.Range:
                    var wideVal = Functions.WideAbs(value);
                    return Constants.AxisMaxValue * 0.4 < wideVal
                        && Constants.AxisMaxValue * 0.6 > wideVal;
                default:
                    return false;
            }
        }

        private DeviceConfiguration FindDeviceConfiguration(ProviderDescriptor providerDescriptor, DeviceDescriptor deviceDescriptor)
        {
            foreach (var deviceConfiguration in _deviceConfigurationList)
            {
                Device runtimeDevice;
                if (!_bindModeRuntimeDevices.TryGetValue(deviceConfiguration.Guid, out runtimeDevice)) continue;

                if (string.Equals(runtimeDevice.ProviderName, providerDescriptor.ProviderName, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(runtimeDevice.DeviceHandle, deviceDescriptor.DeviceHandle, StringComparison.OrdinalIgnoreCase)
                    && runtimeDevice.DeviceNumber == deviceDescriptor.DeviceInstance)
                {
                    return deviceConfiguration;
                }
            }

            return null;
        }

        public void Dispose()
        {
            EndBindMode();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        [NotifyPropertyChangedInvocator]
        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
