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
        private static readonly int BindModeTick = 50;
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
        private PendingBindInput _pendingBindInput;
        private int _bindGeneration;
        private bool _autoDetectInputCategory;
        private DateTime bindAcceptAfterUtc = DateTime.MaxValue;

        public bool IsBindModeActive
        {
            get
            {
                lock (bindmodeLock) return bindmodeActive;
            }
        }

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

        // An unselected input listens to every device configured in its profile.
        // Once a specific device has been selected, detection is restricted to it.
        internal static bool IsEligibleBindDevice(Guid selectedDeviceGuid, Guid candidateDeviceGuid)
        {
            return selectedDeviceGuid == Guid.Empty || selectedDeviceGuid == candidateDeviceGuid;
        }

        public void BeginBindMode(DeviceBinding deviceBinding, bool autoDetectInputCategory = false)
        {
            if (deviceBinding == null) throw new ArgumentNullException(nameof(deviceBinding));

            // BindingManager is created on UCR's UI dispatcher. All state transitions and all
            // INotifyPropertyChanged mutations are serialized back onto that dispatcher.
            if (!_dispatcher.CheckAccess())
            {
                _dispatcher.BeginInvoke(new Action(() => BeginBindMode(deviceBinding, autoDetectInputCategory)), DispatcherPriority.Input);
                return;
            }

            // Device identification and binding both use provider detection mode. If another window
            // is currently identifying a device, binding takes precedence and cleanly ends that transient
            // detector before arming this binding.
            _context.DevicesManager?.CancelInputDeviceDetection();

            if (bindmodeActive) EndBindMode();

            int generation;
            lock (bindmodeLock)
            {
                generation = ++_bindGeneration;
                _deviceBinding = deviceBinding;
                _autoDetectInputCategory = autoDetectInputCategory && deviceBinding.DeviceIoType == DeviceIoType.Input;
                _deviceConfigurationList = new List<DeviceConfiguration>();
                _bindModeRuntimeDevices = new Dictionary<Guid, Device>();
                _pendingBindInput = null;
                bindAcceptAfterUtc = DateTime.UtcNow.Add(BindArmDelay);
                bindmodeActive = true;
            }

            Logger.Debug($"Begin bind mode: binding={deviceBinding.Guid}, io={deviceBinding.DeviceIoType}, category={deviceBinding.DeviceBindingCategory}");

            try
            {
                foreach (var deviceConfiguration in deviceBinding.Profile.GetDeviceConfigurationList(deviceBinding.DeviceIoType))
                {
                    if (!IsEligibleBindDevice(deviceBinding.DeviceConfigurationGuid, deviceConfiguration.Guid))
                        continue;
                    var runtimeDevice = _context.DevicesManager.ResolveDevice(deviceConfiguration.Device, deviceBinding.DeviceIoType);
                    if (runtimeDevice == null)
                    {
                        Logger.Debug($"Bind mode skipped unavailable device configuration {deviceConfiguration.Guid}");
                        continue;
                    }

                    Logger.Debug($"Bind mode enabling detection: provider={runtimeDevice.ProviderName}, handle={runtimeDevice.DeviceHandle}, instance={runtimeDevice.DeviceNumber}, configuration={deviceConfiguration.Guid}");
                    // Providers can report synchronously. Register provisionally and discard
                    // both the endpoint and its queued reservation if arming fails.
                    lock (bindmodeLock)
                    {
                        if (!bindmodeActive || generation != _bindGeneration) return;
                        if (FindDeviceConfiguration(GetProviderDescriptor(runtimeDevice),
                            GetDeviceDescriptor(runtimeDevice)) != null) continue;
                        _deviceConfigurationList.Add(deviceConfiguration);
                        _bindModeRuntimeDevices[deviceConfiguration.Guid] = runtimeDevice;
                    }
                    try
                    {
                        _context.IOController.SetDetectionMode(DetectionMode.Bind,
                            GetProviderDescriptor(runtimeDevice), GetDeviceDescriptor(runtimeDevice),
                            (provider, device, report, value) => InputChanged(generation, provider, device, report, value));
                    }
                    catch (Exception exception)
                    {
                        lock (bindmodeLock)
                        {
                            _deviceConfigurationList.Remove(deviceConfiguration);
                            _bindModeRuntimeDevices.Remove(deviceConfiguration.Guid);
                            if (_pendingBindInput?.DeviceConfigurationGuid == deviceConfiguration.Guid)
                                _pendingBindInput = null;
                        }
                        Logger.Warn(exception, "Could not arm binding for " + runtimeDevice.LogName());
                        RestoreSubscriptionMode(runtimeDevice);
                    }
                    if (!IsBindModeActive) return;
                }

                // Never show a fake waiting state when every selected input device
                // is disconnected or otherwise unavailable to its provider.
                if (_deviceConfigurationList.Count == 0)
                    throw new InvalidOperationException(
                        "No available input device can be detected. Select a connected device " +
                        "in the device dropdown, or check the device/provider connection.");

                BindingTimer?.Stop();
                BindingTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher);
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
            if (BindModeProgress > 0.0) return;

            lock (bindmodeLock)
            {
                // Once a provider callback has reserved a real input, never let a cosmetic UI timer
                // cancel it while the final model mutation is queued on the dispatcher.
                if (_pendingBindInput != null) return;
            }
            EndBindMode();
        }

        // A single cancellation path works for every mapping editor and the header.
        public void CancelBindMode(DeviceBinding target = null)
        {
            if (!_dispatcher.CheckAccess())
            {
                _dispatcher.BeginInvoke(new Action(() => CancelBindMode(target)), DispatcherPriority.Input);
                return;
            }
            lock (bindmodeLock)
            {
                if (!bindmodeActive || (target != null && !ReferenceEquals(_deviceBinding, target))) return;
            }
            EndBindMode();
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
                _autoDetectInputCategory = false;
                _pendingBindInput = null;
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

                RestoreSubscriptionMode(runtimeDevice);
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

        private void RestoreSubscriptionMode(Device runtimeDevice)
        {
            try
            {
                _context.IOController.SetDetectionMode(DetectionMode.Subscription,
                    GetProviderDescriptor(runtimeDevice), GetDeviceDescriptor(runtimeDevice));
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "Could not leave bind mode for " + runtimeDevice.LogName());
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

        private sealed class PendingBindInput
        {
            public DeviceBinding Binding { get; set; }
            public Guid DeviceConfigurationGuid { get; set; }
            public int KeyType { get; set; }
            public int KeyValue { get; set; }
            public int KeySubValue { get; set; }
            public string ProviderName { get; set; }
            public string DeviceHandle { get; set; }
            public int DeviceInstance { get; set; }
            public short Value { get; set; }
            public DeviceBindingCategory Category { get; set; }
        }

        private void InputChanged(int generation, ProviderDescriptor providerDescriptor, DeviceDescriptor deviceDescriptor, BindingReport bindingReport, short value)
        {
            // Reserve the first valid physical input on the provider callback thread. The old path
            // marshalled the whole callback to WPF before reserving it, so a busy UI could make a
            // perfectly real key/button press arrive late enough to look "missed". Only the tiny
            // model mutation is dispatched now; detection itself wins immediately.
            var pendingInput = TryReserveDetectedInput(generation, providerDescriptor, deviceDescriptor, bindingReport, value);
            if (pendingInput == null) return;

            try
            {
                // Always queue completion, even for synchronous UI-thread provider callbacks.
                // Ending detection inside SetDetectionMode would re-enter that provider.
                _dispatcher.BeginInvoke(new Action(() => CommitDetectedInput(pendingInput)),
                    DispatcherPriority.Input);
            }
            catch (InvalidOperationException exception)
            {
                Logger.Error(exception, "Could not marshal detected input to the UCR dispatcher");
                lock (bindmodeLock)
                {
                    if (ReferenceEquals(_pendingBindInput, pendingInput)) _pendingBindInput = null;
                }
            }
        }

        internal static bool IsCompatibleCaptureCategory(DeviceBindingCategory expected,
            DeviceBindingCategory detected, bool autoDetect)
        {
            return autoDetect
                ? detected == DeviceBindingCategory.Momentary || detected == DeviceBindingCategory.Range
                : detected == expected;
        }

        private PendingBindInput TryReserveDetectedInput(int generation, ProviderDescriptor providerDescriptor,
            DeviceDescriptor deviceDescriptor, BindingReport bindingReport, short value)
        {
            lock (bindmodeLock)
            {
                if (!bindmodeActive || generation != _bindGeneration || _pendingBindInput != null || _deviceBinding == null ||
                    providerDescriptor == null ||
                    bindingReport?.BindingDescriptor == null) return null;

                var category = DeviceBinding.MapCategory(bindingReport.Category);
                if (!IsCompatibleCaptureCategory(_deviceBinding.DeviceBindingCategory,
                        category, _autoDetectInputCategory) ||
                    !IsInputValid(bindingReport.Category, value))
                    return null;

                var deviceConfiguration = FindDeviceConfiguration(providerDescriptor, deviceDescriptor);
                if (deviceConfiguration == null ||
                    !IsEligibleBindDevice(_deviceBinding.DeviceConfigurationGuid, deviceConfiguration.Guid))
                    return null;

                // The click that opened bind mode can only contaminate pointer bindings. Keyboard,
                // controller and other device input is accepted immediately instead of being thrown
                // away by the former blanket 150ms arm delay.
                if (DateTime.UtcNow < bindAcceptAfterUtc &&
                    ShouldSuppressInitiatingPointerInput(deviceConfiguration, category))
                {
                    return null;
                }

                _pendingBindInput = new PendingBindInput
                {
                    Binding = _deviceBinding,
                    DeviceConfigurationGuid = deviceConfiguration.Guid,
                    KeyType = (int)bindingReport.BindingDescriptor.Type,
                    KeyValue = bindingReport.BindingDescriptor.Index,
                    KeySubValue = bindingReport.BindingDescriptor.SubIndex,
                    ProviderName = providerDescriptor?.ProviderName,
                    DeviceHandle = deviceDescriptor.DeviceHandle,
                    DeviceInstance = deviceDescriptor.DeviceInstance,
                    Value = value,
                    Category = category
                };
                return _pendingBindInput;
            }
        }

        private bool ShouldSuppressInitiatingPointerInput(DeviceConfiguration deviceConfiguration,
            DeviceBindingCategory category)
        {
            if (category != DeviceBindingCategory.Momentary || deviceConfiguration == null) return false;

            Device runtimeDevice;
            if (!_bindModeRuntimeDevices.TryGetValue(deviceConfiguration.Guid, out runtimeDevice) || runtimeDevice == null)
                return false;

            var title = DevicesManager.GetLogicalDeviceTitle(runtimeDevice) ?? string.Empty;
            var handle = runtimeDevice.DeviceHandle ?? string.Empty;
            return title.StartsWith("M:", StringComparison.OrdinalIgnoreCase) ||
                   title.IndexOf("mouse", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   handle.StartsWith("Mouse", StringComparison.OrdinalIgnoreCase) ||
                   handle.IndexOf("mouse", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void CommitDetectedInput(PendingBindInput pendingInput)
        {
            if (pendingInput == null) return;

            lock (bindmodeLock)
            {
                if (!bindmodeActive || !ReferenceEquals(_pendingBindInput, pendingInput))
                    return;
            }

            try
            {
                Logger.Debug($"Bind input accepted: provider={pendingInput.ProviderName}, handle={pendingInput.DeviceHandle}, instance={pendingInput.DeviceInstance}, type={pendingInput.KeyType}, index={pendingInput.KeyValue}, subIndex={pendingInput.KeySubValue}, value={pendingInput.Value}");
                // Only an unselected input can detect across devices. Always bind to
                // the exact device that supplied the accepted control.
                if (_autoDetectInputCategory)
                {
                    // A digital direction captured from an analogue control
                    // must retain the sign of the movement. Otherwise moving
                    // either way activates BOTH opposing button slots.
                    var detectedKind = pendingInput.Category == DeviceBindingCategory.Range
                        ? pendingInput.Binding.InputDirectionSlot > 0
                            ? (pendingInput.Value < 0 ? 2 : 1)
                            : 3
                        : 0;
                    pendingInput.Binding.SetInputExpressionInputKind(detectedKind);
                    pendingInput.Binding.DeviceBindingCategory = pendingInput.Category;
                }
                pendingInput.Binding.SetDeviceConfigurationGuid(pendingInput.DeviceConfigurationGuid);
                pendingInput.Binding.SetKeyTypeValue(pendingInput.KeyType, pendingInput.KeyValue, pendingInput.KeySubValue);
                EndBindMode();
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "Failed to commit detected input binding");
                lock (bindmodeLock) _pendingBindInput = null;
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
                    // A stick pushed fully to its stop is a valid capture too.
                    // Ignore the near-centre noise without rejecting strong movement.
                    return wideVal > Constants.AxisMaxValue * 0.4;
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
