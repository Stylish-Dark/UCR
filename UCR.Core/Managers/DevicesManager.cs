using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;
using HidWizards.IOWrapper.DataTransferObjects;
using HidWizards.UCR.Core.Models;
using HidWizards.UCR.Core.Models.Binding;
using HidWizards.UCR.Core.Utilities;
using Newtonsoft.Json;
using NLog;
using Logger = NLog.Logger;

namespace HidWizards.UCR.Core.Managers
{
    public class DevicesManager
    {
        private readonly Context _context;

        private Dictionary<string, List<Device>> _providerCache;
        private readonly HashSet<string> _sessionDismissedDevices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private static readonly TimeSpan DeviceDetectionArmDelay = TimeSpan.FromMilliseconds(350);
        private readonly object _deviceDetectionLock = new object();
        private TaskCompletionSource<Device> _deviceDetectionCompletion;
        private List<Device> _deviceDetectionDevices;
        private Timer _deviceDetectionTimer;
        private CancellationTokenRegistration _deviceDetectionCancellation;
        private DateTime _deviceDetectionAcceptAfterUtc = DateTime.MaxValue;


        public DevicesManager(Context context)
        {
            _context = context;
            _providerCache = new Dictionary<string, List<Device>>();
        }

        /// <summary>
        /// Gets a list of available devices from the backend
        /// </summary>
        /// <param name="type"></param>
        public List<Device> GetAvailableDeviceList(DeviceIoType type, bool includeCache = true)
        {
            var result = new List<Device>();
            var providerList = type == DeviceIoType.Input
                ? _context.IOController.GetInputList()
                : _context.IOController.GetOutputList();

            foreach (var providerReport in providerList)
            {
                foreach (var ioWrapperDevice in providerReport.Value.Devices)
                {
                    result.Add(new Device(ioWrapperDevice, providerReport.Value, BuildDeviceBindingMenu(ioWrapperDevice.Nodes, type)));
                }

                if (includeCache)
                {
                    var cachedDevices = LoadDeviceCache(providerReport.Value.ProviderDescriptor.ProviderName);
                    foreach (var cachedDevice in cachedDevices)
                    {
                        // A cache entry is only useful when its endpoint is currently absent. Old
                        // provider instance numbers can otherwise accumulate across restarts and make
                        // one physical keyboard/mouse appear several times. Prefer the live report.
                        if (result.Any(liveDevice => CacheRepresentsLiveEndpoint(cachedDevice, liveDevice))) continue;
                        result.Add(cachedDevice);
                    }
                    
                }
            }

            ApplyAliases(result);
            return SortDevices(result);
        }

        /// <summary>
        /// Returns devices intended for user-selection surfaces. Selection and persistence are separate
        /// concerns: a provider may expose enough runtime identity to use a device right now without
        /// exposing a stable identity that is safe for persistent alias/hide/order metadata. Those
        /// session-only devices must remain selectable. Cached devices are excluded by default so stale
        /// provider enumeration slots do not pollute add-device lists.
        /// </summary>
        public List<Device> GetVisibleDeviceList(DeviceIoType type, bool includeCache = false)
        {
            var devices = GetAvailableDeviceList(type, includeCache);
            return devices.Where(device => !IsDeviceHidden(device, devices) && !IsSessionDismissed(device)).ToList();
        }

        public bool DismissDeviceForSession(Device device)
        {
            var key = BuildSessionDeviceKey(device);
            if (key == null) return false;
            return _sessionDismissedDevices.Add(key);
        }

        public bool IsSessionDismissed(Device device)
        {
            var key = BuildSessionDeviceKey(device);
            return key != null && _sessionDismissedDevices.Contains(key);
        }

        private static string BuildSessionDeviceKey(Device device)
        {
            if (device == null || string.IsNullOrWhiteSpace(device.ProviderName)) return null;
            if (!string.IsNullOrWhiteSpace(device.HidPath))
                return "hid|" + device.ProviderName + "|" + device.HidPath;
            return "slot|" + device.ProviderName + "|" + device.DeviceHandle + "|" + device.DeviceNumber;
        }

        /// <summary>
        /// Temporarily listens to all live input devices and returns the first device that produces a
        /// deliberate button/key-style input. Axis movement and delta input are ignored so mouse motion,
        /// stick drift and resting analogue values cannot win detection accidentally.
        /// </summary>
        public Task<Device> DetectInputDeviceAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
            if (_context.BindingManager != null && _context.BindingManager.IsBindModeActive)
            {
                throw new InvalidOperationException("Finish the current binding operation before detecting a device.");
            }

            var devices = GetAvailableDeviceList(DeviceIoType.Input, false);
            if (devices.Count == 0) return Task.FromResult<Device>(null);

            TaskCompletionSource<Device> completion;
            lock (_deviceDetectionLock)
            {
                if (_deviceDetectionCompletion != null)
                {
                    throw new InvalidOperationException("Device detection is already running.");
                }

                completion = new TaskCompletionSource<Device>();
                _deviceDetectionCompletion = completion;
                _deviceDetectionDevices = devices;
                _deviceDetectionAcceptAfterUtc = DateTime.MaxValue;
            }

            try
            {
                foreach (var device in devices)
                {
                    try
                    {
                        _context.IOController.SetDetectionMode(DetectionMode.Bind,
                            GetProviderDescriptor(device), GetDeviceDescriptor(device), DeviceDetectionInputChanged);
                    }
                    catch (Exception exception)
                    {
                        Logger.Error(exception,
                            $"Could not enable device detection for provider={device.ProviderName}, handle={device.DeviceHandle}, instance={device.DeviceNumber}");
                    }
                }

                lock (_deviceDetectionLock)
                {
                    if (_deviceDetectionCompletion == completion)
                    {
                        _deviceDetectionAcceptAfterUtc = DateTime.UtcNow.Add(DeviceDetectionArmDelay);
                        _deviceDetectionTimer = new Timer(_ => CompleteDeviceDetection(null), null, timeout,
                            Timeout.InfiniteTimeSpan);
                        if (cancellationToken.CanBeCanceled)
                        {
                            _deviceDetectionCancellation = cancellationToken.Register(() =>
                                ThreadPool.QueueUserWorkItem(_ => CompleteDeviceDetection(null)));
                        }
                    }
                }
            }
            catch
            {
                CompleteDeviceDetection(null);
                throw;
            }

            return completion.Task;
        }

        public void CancelInputDeviceDetection()
        {
            CompleteDeviceDetection(null);
        }

        private void DeviceDetectionInputChanged(ProviderDescriptor providerDescriptor,
            DeviceDescriptor deviceDescriptor, BindingReport bindingReport, short value)
        {
            List<Device> devices;
            DateTime acceptAfter;
            lock (_deviceDetectionLock)
            {
                if (_deviceDetectionCompletion == null) return;
                devices = _deviceDetectionDevices;
                acceptAfter = _deviceDetectionAcceptAfterUtc;
            }

            if (DateTime.UtcNow < acceptAfter || bindingReport == null) return;

            var category = DeviceBinding.MapCategory(bindingReport.Category);
            var isDeliberatePress = category == DeviceBindingCategory.Momentary && value != 0;
            var isDiscreteEvent = category == DeviceBindingCategory.Event;
            if (!isDeliberatePress && !isDiscreteEvent) return;

            var device = devices?.FirstOrDefault(candidate =>
                string.Equals(candidate.ProviderName, providerDescriptor?.ProviderName,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(candidate.DeviceHandle, deviceDescriptor.DeviceHandle,
                    StringComparison.OrdinalIgnoreCase) &&
                candidate.DeviceNumber == deviceDescriptor.DeviceInstance);

            if (device == null) return;

            Logger.Debug($"Detected input device: provider={device.ProviderName}, handle={device.DeviceHandle}, instance={device.DeviceNumber}, title={device.DisplayTitle}");
            ThreadPool.QueueUserWorkItem(_ => CompleteDeviceDetection(device));
        }

        private void CompleteDeviceDetection(Device detectedDevice)
        {
            TaskCompletionSource<Device> completion;
            List<Device> devices;
            Timer timer;
            CancellationTokenRegistration cancellation;

            lock (_deviceDetectionLock)
            {
                completion = _deviceDetectionCompletion;
                if (completion == null) return;

                devices = _deviceDetectionDevices ?? new List<Device>();
                timer = _deviceDetectionTimer;
                cancellation = _deviceDetectionCancellation;

                _deviceDetectionCompletion = null;
                _deviceDetectionDevices = null;
                _deviceDetectionTimer = null;
                _deviceDetectionCancellation = default(CancellationTokenRegistration);
                _deviceDetectionAcceptAfterUtc = DateTime.MaxValue;
            }

            timer?.Dispose();
            cancellation.Dispose();

            foreach (var device in devices)
            {
                try
                {
                    _context.IOController.SetDetectionMode(DetectionMode.Subscription,
                        GetProviderDescriptor(device), GetDeviceDescriptor(device));
                }
                catch (Exception exception)
                {
                    Logger.Error(exception,
                        $"Could not restore input subscription after device detection for provider={device.ProviderName}, handle={device.DeviceHandle}, instance={device.DeviceNumber}");
                }
            }

            completion.TrySetResult(detectedDevice);
        }

        private static DeviceDescriptor GetDeviceDescriptor(Device device)
        {
            return new DeviceDescriptor
            {
                DeviceHandle = device.DeviceHandle,
                DeviceInstance = device.DeviceNumber
            };
        }

        private static ProviderDescriptor GetProviderDescriptor(Device device)
        {
            return new ProviderDescriptor
            {
                ProviderName = device.ProviderName
            };
        }

        public static bool CacheRepresentsLiveEndpoint(Device cachedDevice, Device liveDevice)
        {
            if (cachedDevice == null || liveDevice == null) return false;
            if (!string.Equals(cachedDevice.ProviderName, liveDevice.ProviderName, StringComparison.OrdinalIgnoreCase)) return false;

            if (!string.IsNullOrWhiteSpace(cachedDevice.HidPath) && !string.IsNullOrWhiteSpace(liveDevice.HidPath))
            {
                return string.Equals(cachedDevice.HidPath, liveDevice.HidPath, StringComparison.OrdinalIgnoreCase);
            }

            return !string.IsNullOrWhiteSpace(cachedDevice.DeviceHandle) &&
                   string.Equals(cachedDevice.DeviceHandle, liveDevice.DeviceHandle, StringComparison.OrdinalIgnoreCase);
        }

        public static bool TryGetWindowsDeviceInstanceId(Device device, out string instanceId)
        {
            instanceId = null;
            var hidPath = device?.HidPath;
            if (string.IsNullOrWhiteSpace(hidPath)) return false;

            var normalized = hidPath.Trim();
            if (normalized.StartsWith(@"\\?\", StringComparison.Ordinal)) normalized = normalized.Substring(4);

            var classGuidIndex = normalized.IndexOf("#{", StringComparison.Ordinal);
            if (classGuidIndex >= 0) normalized = normalized.Substring(0, classGuidIndex);
            normalized = normalized.TrimEnd('#');

            var parts = normalized.Split(new[] { '#' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) return false;

            instanceId = string.Join(@"\", parts);
            return !string.IsNullOrWhiteSpace(instanceId);
        }

        public void RefreshDeviceList()
        {
            _context.IOController.RefreshDevices();
        }

        public List<Device> GetAvailableDevicesListFromSameProvider(DeviceIoType type, Device device)
        {
            var availableDeviceList = GetVisibleDeviceList(type);
            return availableDeviceList.Where(d => d.ProviderName.Equals(device.ProviderName)).ToList();
        }

        /// <summary>
        /// Resolves a persisted/profile device to the descriptor that the provider is using right now.
        /// Prefer the per-device HID path when the provider exposes one. Otherwise, a unique hardware
        /// handle can safely survive provider instance-number changes. Legacy exact descriptor matching
        /// remains as the final fallback for providers such as XInput/ViGEm that expose only numbered slots.
        ///
        /// If a persisted HID path no longer matches, do not guess from a different physical path.
        /// Selecting by an old instance number could silently bind the wrong unit.
        /// </summary>
        public Device ResolveDevice(Device configuredDevice, DeviceIoType type)
        {
            if (configuredDevice == null) return null;

            var availableDevices = GetAvailableDeviceList(type, false);
            var resolvedDevice = ResolveDevice(configuredDevice, availableDevices);

            // Migrate legacy profiles forward only when the match is unambiguous. This allows an older
            // configuration that pre-dates HidPath persistence to acquire the stronger identity without
            // cementing a possibly-wrong device when duplicate handles are present.
            if (resolvedDevice != null && string.IsNullOrEmpty(configuredDevice.HidPath) &&
                !string.IsNullOrEmpty(resolvedDevice.HidPath))
            {
                var sameHandleCount = availableDevices.Count(d => d != null &&
                    string.Equals(d.ProviderName, configuredDevice.ProviderName, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(d.DeviceHandle, configuredDevice.DeviceHandle, StringComparison.OrdinalIgnoreCase));

                if (sameHandleCount == 1)
                {
                    configuredDevice.HidPath = resolvedDevice.HidPath;
                    _context.ContextChanged();
                }
            }

            return resolvedDevice;
        }

        public static Device ResolveDevice(Device configuredDevice, IEnumerable<Device> availableDevices)
        {
            if (configuredDevice == null || availableDevices == null) return null;

            var providerCandidates = availableDevices
                .Where(d => d != null &&
                            string.Equals(d.ProviderName, configuredDevice.ProviderName,
                                StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (providerCandidates.Count == 0) return null;

            if (!string.IsNullOrEmpty(configuredDevice.HidPath))
            {
                var hidMatches = providerCandidates
                    .Where(d => !string.IsNullOrEmpty(d.HidPath) &&
                                string.Equals(d.HidPath, configuredDevice.HidPath,
                                    StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (hidMatches.Count == 1) return hidMatches[0];
                if (hidMatches.Count > 1)
                {
                    return hidMatches.FirstOrDefault(d => DescriptorEquals(d, configuredDevice));
                }
            }

            var handleMatches = providerCandidates
                .Where(d => string.Equals(d.DeviceHandle, configuredDevice.DeviceHandle,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (!string.IsNullOrEmpty(configuredDevice.HidPath))
            {
                // A different non-empty HID path is evidence that this is not the persisted endpoint.
                // It may be the same unit moved to another port, but it may equally be another identical
                // unit; without stronger evidence, requiring an explicit re-selection is safer.
                if (handleMatches.Any(d => !string.IsNullOrEmpty(d.HidPath)))
                {
                    return null;
                }

                // Some providers/builds may stop exposing HidPath. Only then fall back to a unique handle.
                return handleMatches.Count == 1 ? handleMatches[0] : null;
            }

            if (handleMatches.Count == 1) return handleMatches[0];

            // Numbered virtual/API slots are the identity those providers intentionally expose.
            // For physical-device providers, duplicate hardware handles without a stronger path are
            // indistinguishable; refusing to guess is safer than silently following enumeration order.
            if (UsesLogicalSlotIdentity(configuredDevice.ProviderName))
            {
                return handleMatches.FirstOrDefault(d => DescriptorEquals(d, configuredDevice));
            }

            return null;
        }

        private static bool UsesLogicalSlotIdentity(string providerName)
        {
            return string.Equals(providerName, "SharpDX_XInput", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(providerName, "Core_ViGEm", StringComparison.OrdinalIgnoreCase);
        }

        public static bool DescriptorEquals(Device left, Device right)
        {
            if (left == null || right == null) return false;
            return string.Equals(left.ProviderName, right.ProviderName, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(left.DeviceHandle, right.DeviceHandle, StringComparison.OrdinalIgnoreCase)
                   && left.DeviceNumber == right.DeviceNumber;
        }

        public static bool PersistedIdentityEquals(Device left, Device right)
        {
            if (left == null || right == null) return false;
            if (!string.Equals(left.ProviderName, right.ProviderName, StringComparison.OrdinalIgnoreCase)) return false;

            if (!string.IsNullOrEmpty(left.HidPath) && !string.IsNullOrEmpty(right.HidPath))
            {
                return string.Equals(left.HidPath, right.HidPath, StringComparison.OrdinalIgnoreCase);
            }

            return DescriptorEquals(left, right);
        }

        #region Device aliases

        public string GetDisplayTitle(Device device)
        {
            if (device == null) return string.Empty;
            var alias = FindAlias(device);
            return alias == null || string.IsNullOrWhiteSpace(alias.Alias) ? device.Title : alias.Alias;
        }

        public string GetDeviceAlias(Device device)
        {
            return FindAlias(device)?.Alias;
        }

        public bool GetDeviceHidden(Device device)
        {
            return FindAlias(device)?.Hidden ?? false;
        }

        public int GetDeviceSortOrder(Device device)
        {
            return FindAlias(device)?.SortOrder ?? int.MaxValue;
        }

        public bool CanPersistDeviceAlias(Device device, DeviceIoType type)
        {
            return CanPersistDeviceAlias(device, GetAvailableDeviceList(type, false));
        }

        public bool CanPersistDeviceAlias(Device device, IEnumerable<Device> liveDevices)
        {
            if (device == null || string.IsNullOrWhiteSpace(device.ProviderName)) return false;
            if (!string.IsNullOrWhiteSpace(device.HidPath)) return true;
            if (UsesLogicalSlotIdentity(device.ProviderName)) return !string.IsNullOrWhiteSpace(device.DeviceHandle);
            if (string.IsNullOrWhiteSpace(device.DeviceHandle)) return false;
            return CountHandleMatches(device, liveDevices) == 1;
        }

        public bool TrySetDeviceAlias(Device device, DeviceIoType type, string alias, out string error)
        {
            error = null;
            if (device == null)
            {
                error = "No device is selected.";
                return false;
            }

            if (_context.DeviceAliases == null) _context.DeviceAliases = new List<DeviceAlias>();

            var identity = BuildAliasIdentity(device);
            if (identity == null)
            {
                error = "This device does not expose enough identity information for a persistent alias.";
                return false;
            }

            var normalizedAlias = string.IsNullOrWhiteSpace(alias) ? null : alias.Trim();
            var existing = _context.DeviceAliases.FirstOrDefault(candidate => AliasIdentityEquals(candidate, identity));

            // Clearing a friendly name is always safe: it cannot make an ambiguous physical device
            // claim a new identity. Preserve any independently stored hide/order preferences.
            if (normalizedAlias == null)
            {
                if (existing == null)
                {
                    device.Alias = null;
                    return true;
                }

                if (existing.Alias == null)
                {
                    device.Alias = null;
                    return true;
                }

                existing.Alias = null;
                device.Alias = null;
                if (!existing.HasPresentationSettings) _context.DeviceAliases.Remove(existing);
                _context.ContextChanged();
                _context.OnDeviceAliasesChangedEvent();
                return true;
            }

            if (!CanPersistDeviceAlias(device, type))
            {
                error = "UCR cannot safely persist an individual alias for this device because the provider does not expose a unique identity for it while identical devices are present.";
                return false;
            }

            if (existing == null)
            {
                existing = identity;
                _context.DeviceAliases.Add(existing);
            }

            if (string.Equals(existing.Alias, normalizedAlias, StringComparison.Ordinal))
            {
                device.Alias = normalizedAlias;
                return true;
            }

            existing.Alias = normalizedAlias;
            device.Alias = normalizedAlias;
            _context.ContextChanged();
            _context.OnDeviceAliasesChangedEvent();
            return true;
        }

        public bool TrySetDevicePresentation(Device device, DeviceIoType type, string alias, bool hidden,
            int sortOrder, out string error)
        {
            error = null;
            if (device == null)
            {
                error = "No device is selected.";
                return false;
            }

            if (_context.DeviceAliases == null) _context.DeviceAliases = new List<DeviceAlias>();

            var identity = BuildAliasIdentity(device);
            if (identity == null)
            {
                error = "This device does not expose enough identity information for persistent device settings.";
                return false;
            }

            var existing = _context.DeviceAliases.FirstOrDefault(candidate => AliasIdentityEquals(candidate, identity));
            var normalizedAlias = string.IsNullOrWhiteSpace(alias) ? null : alias.Trim();
            var normalizedSortOrder = sortOrder < 0 ? int.MaxValue : sortOrder;
            var wantsPersistentSettings = normalizedAlias != null || hidden || normalizedSortOrder != int.MaxValue;

            // HID paths and intentional logical slots are stable. A handle-only physical device is safe
            // only while that provider exposes exactly one matching live device; otherwise two identical
            // units would share the same settings and UCR would be pretending to know which is which.
            if (wantsPersistentSettings && !CanPersistDeviceAlias(device, type))
            {
                error = "UCR cannot safely persist individual settings for this device because the provider does not expose a unique identity for it while identical devices are present.";
                return false;
            }

            if (!wantsPersistentSettings)
            {
                if (existing != null)
                {
                    _context.DeviceAliases.Remove(existing);
                    _context.ContextChanged();
                    _context.OnDeviceAliasesChangedEvent();
                }
                device.Alias = null;
                return true;
            }

            if (existing == null)
            {
                existing = identity;
                _context.DeviceAliases.Add(existing);
            }
            else if (string.Equals(existing.Alias, normalizedAlias, StringComparison.Ordinal) &&
                     existing.Hidden == hidden && existing.SortOrder == normalizedSortOrder)
            {
                device.Alias = normalizedAlias;
                return true;
            }

            existing.Alias = normalizedAlias;
            existing.Hidden = hidden;
            existing.SortOrder = normalizedSortOrder;
            device.Alias = normalizedAlias;
            _context.ContextChanged();
            _context.OnDeviceAliasesChangedEvent();
            return true;
        }

        public void MergeDeviceAliases(IEnumerable<DeviceAlias> aliases, bool overwriteExisting)
        {
            if (aliases == null) return;
            if (_context.DeviceAliases == null) _context.DeviceAliases = new List<DeviceAlias>();
            var changed = false;

            foreach (var imported in aliases.Where(alias => alias != null))
            {
                var existing = _context.DeviceAliases.FirstOrDefault(candidate => AliasIdentityEquals(candidate, imported));
                if (existing == null)
                {
                    _context.DeviceAliases.Add(imported.Clone());
                    changed = true;
                }
                else if (overwriteExisting &&
                         (!string.Equals(existing.Alias, imported.Alias, StringComparison.Ordinal) ||
                          existing.Hidden != imported.Hidden ||
                          existing.SortOrder != imported.SortOrder))
                {
                    existing.Alias = imported.Alias;
                    existing.Hidden = imported.Hidden;
                    existing.SortOrder = imported.SortOrder;
                    changed = true;
                }
            }

            if (changed) _context.OnDeviceAliasesChangedEvent();
        }

        public void ReplaceDeviceAliases(IEnumerable<DeviceAlias> aliases)
        {
            _context.DeviceAliases = aliases == null
                ? new List<DeviceAlias>()
                : aliases.Where(alias => alias != null).Select(alias => alias.Clone()).ToList();
            _context.OnDeviceAliasesChangedEvent();
        }

        public static bool AliasIdentityEquals(DeviceAlias left, DeviceAlias right)
        {
            if (left == null || right == null) return false;
            return left.IdentityKind == right.IdentityKind
                   && left.DeviceNumber == right.DeviceNumber
                   && string.Equals(left.ProviderName, right.ProviderName, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(left.IdentityValue, right.IdentityValue, StringComparison.OrdinalIgnoreCase);
        }

        public static DeviceAlias BuildAliasIdentity(Device device)
        {
            if (device == null || string.IsNullOrWhiteSpace(device.ProviderName)) return null;

            if (!string.IsNullOrWhiteSpace(device.HidPath))
            {
                return new DeviceAlias
                {
                    ProviderName = device.ProviderName,
                    IdentityKind = DeviceAliasIdentityKind.HidPath,
                    IdentityValue = device.HidPath.Trim(),
                    DeviceNumber = 0
                };
            }

            if (string.IsNullOrWhiteSpace(device.DeviceHandle)) return null;

            if (UsesLogicalSlotIdentity(device.ProviderName))
            {
                return new DeviceAlias
                {
                    ProviderName = device.ProviderName,
                    IdentityKind = DeviceAliasIdentityKind.LogicalSlot,
                    IdentityValue = device.DeviceHandle.Trim(),
                    DeviceNumber = device.DeviceNumber
                };
            }

            return new DeviceAlias
            {
                ProviderName = device.ProviderName,
                IdentityKind = DeviceAliasIdentityKind.HardwareHandle,
                IdentityValue = device.DeviceHandle.Trim(),
                DeviceNumber = 0
            };
        }

        private DeviceAlias FindAlias(Device device)
        {
            if (device == null || _context.DeviceAliases == null) return null;
            var identity = BuildAliasIdentity(device);
            if (identity == null) return null;
            return _context.DeviceAliases.FirstOrDefault(alias => AliasIdentityEquals(alias, identity));
        }

        private void ApplyAliases(List<Device> devices)
        {
            if (devices == null) return;
            foreach (var device in devices)
            {
                device.Alias = null;
                var alias = FindAlias(device);
                if (alias == null || string.IsNullOrWhiteSpace(alias.Alias)) continue;

                if (alias.IdentityKind == DeviceAliasIdentityKind.HardwareHandle &&
                    CountHandleMatches(device, GetRelevantIdentityPopulation(device, devices)) != 1)
                {
                    continue;
                }

                device.Alias = alias.Alias;
            }
        }

        private static List<Device> GetRelevantIdentityPopulation(Device device, List<Device> devices)
        {
            var liveMatches = devices.Where(candidate => candidate != null && !candidate.IsCache &&
                string.Equals(candidate.ProviderName, device.ProviderName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(candidate.DeviceHandle, device.DeviceHandle, StringComparison.OrdinalIgnoreCase)).ToList();

            return liveMatches.Count > 0 ? liveMatches : devices;
        }

        private static int CountHandleMatches(Device device, IEnumerable<Device> devices)
        {
            if (device == null || devices == null) return 0;
            return devices.Count(candidate => candidate != null &&
                string.Equals(candidate.ProviderName, device.ProviderName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(candidate.DeviceHandle, device.DeviceHandle, StringComparison.OrdinalIgnoreCase));
        }

        private bool IsDeviceHidden(Device device, IEnumerable<Device> population)
        {
            var preference = FindAlias(device);
            if (preference == null || !preference.Hidden) return false;

            var devices = population == null ? new List<Device>() : population.ToList();
            if (preference.IdentityKind == DeviceAliasIdentityKind.HardwareHandle &&
                CountHandleMatches(device, GetRelevantIdentityPopulation(device, devices)) != 1)
            {
                return false;
            }

            return true;
        }

        private List<Device> SortDevices(List<Device> devices)
        {
            return devices
                .Select((device, index) =>
                {
                    var preference = FindAlias(device);
                    if (preference != null && preference.IdentityKind == DeviceAliasIdentityKind.HardwareHandle &&
                        CountHandleMatches(device, GetRelevantIdentityPopulation(device, devices)) != 1)
                    {
                        preference = null;
                    }

                    return new
                    {
                        Device = device,
                        OriginalIndex = index,
                        Preference = preference
                    };
                })
                .OrderBy(item => item.Preference?.SortOrder ?? int.MaxValue)
                .ThenBy(item => item.OriginalIndex)
                .Select(item => item.Device)
                .ToList();
        }

        #endregion

        public List<DeviceBindingNode> GetDeviceBindingMenu(Device device, DeviceIoType type, bool includeCache = true)
        {
            var resolvedDevice = ResolveDevice(device, type);
            if (resolvedDevice != null)
            {
                return resolvedDevice.GetDeviceBindingMenu();
            }

            if (includeCache)
            {
                var cachedDevice = GetAvailableDeviceList(type, true)
                    .FirstOrDefault(candidate => candidate.IsCache && DescriptorEquals(candidate, device));
                if (cachedDevice != null) return cachedDevice.GetDeviceBindingMenu();
            }

            return new List<DeviceBindingNode>
            {
                new DeviceBindingNode()
                {
                    Title = "Device not connected"
                }
            };
        }

        private static List<DeviceBindingNode> BuildDeviceBindingMenu(List<DeviceReportNode> deviceNodes, DeviceIoType type)
        {
            var result = new List<DeviceBindingNode>();
            if (deviceNodes == null) return result;

            foreach (var deviceNode in deviceNodes)
            {
                var groupNode = new DeviceBindingNode()
                {
                    Title = deviceNode.Title,
                    ChildrenNodes = BuildDeviceBindingMenu(deviceNode.Nodes, type),
                };

                if (groupNode.ChildrenNodes == null) groupNode.ChildrenNodes = new List<DeviceBindingNode>();
                

                foreach (var bindingInfo in deviceNode.Bindings)
                {
                    var bindingNode = new DeviceBindingNode()
                    {
                        Title = bindingInfo.Title,
                        DeviceBindingInfo = new DeviceBindingInfo()
                        {
                            KeyType = (int)bindingInfo.BindingDescriptor.Type,
                            KeyValue = bindingInfo.BindingDescriptor.Index,
                            KeySubValue = bindingInfo.BindingDescriptor.SubIndex,
                            DeviceBindingCategory = DeviceBinding.MapCategory(bindingInfo.Category),
                            Blockable = bindingInfo.Blockable
                        }
                    };


                    groupNode.ChildrenNodes.Add(bindingNode);
                }
                result.Add(groupNode);
            }
            return result.Count != 0 ? result : null;
        }

        #region Cache

        public bool UpdateDeviceCache()
        {
            var success = true;
            RefreshDeviceList();
            var availableDeviceList = GetAvailableDeviceList(DeviceIoType.Input, false);

            // Remove obsolete cache copies for endpoints that are live now before writing the current
            // provider descriptors. This stops changing provider instance numbers from accumulating as
            // duplicate devices across UCR sessions while retaining caches for genuinely disconnected
            // hardware (which are still useful for old profile binding menus).
            RemoveCacheEntriesOverlappingLiveDevices(availableDeviceList);

            foreach (var device in availableDeviceList)
            {
                success &= SaveDeviceCache(device);
            }

            _providerCache.Clear();
            return success;
        }

        public int RemoveStaleDeviceCacheCopies()
        {
            RefreshDeviceList();
            var liveDevices = GetAvailableDeviceList(DeviceIoType.Input, false)
                .Concat(GetAvailableDeviceList(DeviceIoType.Output, false))
                .ToList();
            var removed = RemoveCacheEntriesOverlappingLiveDevices(liveDevices);
            _providerCache.Clear();
            return removed;
        }

        public bool ForgetCachedDevice(Device device, out string error)
        {
            error = null;
            if (device == null || !device.IsCache)
            {
                error = "Only cached/disconnected device records can be forgotten. Live devices are reported by Windows and their provider.";
                return false;
            }

            try
            {
                var directory = GetProviderCacheDirectory(device.ProviderName);
                if (!Directory.Exists(directory)) return true;

                foreach (var path in Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
                {
                    var cached = ReadDeviceCache(device.ProviderName, path);
                    if (cached == null) continue;
                    if (!DescriptorEquals(cached, device) && !PersistedIdentityEquals(cached, device)) continue;
                    File.Delete(path);
                }

                _providerCache.Remove(device.ProviderName);
                return true;
            }
            catch (Exception exception)
            {
                error = "UCR could not remove the cached device record. See the log for details.";
                Logger.Error(exception, "Failed to forget cached device: " + device.LogName());
                return false;
            }
        }

        private int RemoveCacheEntriesOverlappingLiveDevices(IEnumerable<Device> liveDevices)
        {
            var live = (liveDevices ?? Enumerable.Empty<Device>()).Where(device => device != null).ToList();
            if (live.Count == 0) return 0;

            var removed = 0;
            var providers = live.Select(device => device.ProviderName)
                .Where(provider => !string.IsNullOrWhiteSpace(provider))
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var provider in providers)
            {
                var directory = GetProviderCacheDirectory(provider);
                if (!Directory.Exists(directory)) continue;

                foreach (var path in Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
                {
                    var cached = ReadDeviceCache(provider, path);
                    if (cached == null) continue;
                    if (!live.Any(liveDevice => CacheRepresentsLiveEndpoint(cached, liveDevice))) continue;

                    try
                    {
                        File.Delete(path);
                        removed++;
                    }
                    catch (Exception exception)
                    {
                        Logger.Error(exception, "Failed to remove stale device cache: " + path);
                    }
                }
            }

            return removed;
        }

        private bool SaveDeviceCache(Device device)
        {
            var serializer = new JsonSerializer();
            Directory.CreateDirectory(GetProviderCacheDirectory(device.ProviderName));
            using (var streamWriter = new StreamWriter(GetDeviceCachePath(device)))
            {
                var deviceCache = new DeviceCache()
                {
                    Title = device.Title,
                    ProviderName = device.ProviderName,
                    DeviceHandle = device.DeviceHandle,
                    DeviceNumber = device.DeviceNumber,
                    HidPath = device.HidPath,
                    DeviceBindingMenu = GetDeviceBindingMenu(device, DeviceIoType.Input, false)
                };

                serializer.Serialize(streamWriter, deviceCache);
            }

            return true;
        }

        private List<Device> LoadDeviceCache(string provider)
        {
            if (_providerCache.ContainsKey(provider)) return _providerCache[provider];

            var result = new List<Device>();
            string[] deviceCacheFiles;
            try
            {
                deviceCacheFiles = Directory.GetFiles(GetProviderCacheDirectory(provider), "*.json",
                    SearchOption.TopDirectoryOnly);
            }
            catch (DirectoryNotFoundException)
            {
                return result;
            }

            foreach (var deviceCacheFile in deviceCacheFiles)
            {
                var device = ReadDeviceCache(provider, deviceCacheFile);
                if (device != null)  result.Add(device);
            }

            _providerCache.Add(provider, result);
            return result;

        }

        private static Device ReadDeviceCache(string provider, string devicePath)
        {
            if (string.IsNullOrEmpty(provider) || string.IsNullOrEmpty(devicePath)) return null;

            try
            {
                using (var fileStream = new FileStream(devicePath, FileMode.Open))  
                {
                    using (var reader = new StreamReader(fileStream))
                    {
                        return new Device(JsonConvert.DeserializeObject<DeviceCache>(reader.ReadToEnd()));
                    }
                }
            }
            catch (IOException e)
            {
                Logger.Error($"Failed to load Cache for Provider: {provider}. Path: {devicePath}", e);
            }
            catch (InvalidOperationException e)
            {
                Logger.Error($"Errors processing XML for Provider cache: {provider}. Path: {devicePath}", e);
            }

            try
            {
                File.Delete(devicePath);
            }
            catch (Exception e)
            {
                Logger.Error($"Failed to delete invalid cache file: {devicePath}", e);
            }

            return null;
        }

        private static string GetDeviceCachePath(Device device)
        {
            return $"{GetProviderCacheDirectory(device.ProviderName)}\\{device.GetHashCode()}.json";
        }

        private static string GetProviderCacheDirectory(string provider)
        {
            return $".\\Cache\\{provider}\\";
        }

        #endregion
    }
}