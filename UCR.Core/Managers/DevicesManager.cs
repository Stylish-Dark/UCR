using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Xml.Serialization;
using HidWizards.IOWrapper.DataTransferObjects;
using HidWizards.UCR.Core.Models;
using HidWizards.UCR.Core.Models.Binding;
using HidWizards.UCR.Core.Utilities;
using Newtonsoft.Json;

namespace HidWizards.UCR.Core.Managers
{
    public class DevicesManager
    {
        private readonly Context _context;

        private Dictionary<string, List<Device>> _providerCache;

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
                        if (result.Contains(cachedDevice)) continue;
                        result.Add(cachedDevice);
                    }
                    
                }
            }

            ApplyAliases(result);
            return SortDevices(result);
        }

        /// <summary>
        /// Returns devices intended for user-selection surfaces. Hidden devices remain available to
        /// runtime resolution and existing profiles, but are omitted from add/create-device pickers.
        /// </summary>
        public List<Device> GetVisibleDeviceList(DeviceIoType type, bool includeCache = true)
        {
            var devices = GetAvailableDeviceList(type, includeCache);
            var liveDevices = GetAvailableDeviceList(type, false);

            // A device that cannot be uniquely identified cannot safely carry an individual persistent
            // alias/hide/order preference. More importantly, presenting indistinguishable units in a
            // selection list invites the user to bind to an enumeration slot that may represent a
            // different physical unit next time. Keep such entries available for runtime resolution and
            // diagnostics, but force-hide them from user selection surfaces.
            return devices.Where(device =>
                    CanPersistDeviceAlias(device, liveDevices) &&
                    !IsDeviceHidden(device, devices))
                .ToList();
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
            
            foreach (var device in availableDeviceList)
            {
                success &= SaveDeviceCache(device);
            }

            return success;
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