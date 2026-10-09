using System;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using HidWizards.IOWrapper.DataTransferObjects;
using HidWizards.UCR.Core.Annotations;
using HidWizards.UCR.Core.Models;
using HidWizards.UCR.Core.Models.Binding;
using HidWizards.UCR.Core.Models.Subscription;
using NLog;
using Logger = NLog.Logger;

namespace HidWizards.UCR.Core.Managers
{
    public sealed class SubscriptionsManager : IDisposable, INotifyPropertyChanged
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private bool _profileActive;
        public bool ProfileActive
        {
            get => _profileActive;
            set
            {
                if (_profileActive == value) return;
                _profileActive = value;
                OnPropertyChanged();
            }
        }

        internal SubscriptionState SubscriptionState { get; set; }
        private readonly Context _context;
        private readonly ExclusiveDeviceModeManager _exclusiveMode;
        private const int EmergencyStopHoldMilliseconds = 3000;
        private readonly object _emergencyStopLock = new object();
        private readonly Dictionary<string, Timer> _emergencyStopTimers = new Dictionary<string, Timer>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _emergencyStopDeviceKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private int _emergencyStopInProgress;

        public SubscriptionsManager(Context context)
        {
            _context = context;
            _exclusiveMode = new ExclusiveDeviceModeManager();
        }

        #region ManagerApi

        public Profile GetActiveProfile()
        {
            return SubscriptionState?.ActiveProfile;
        }

        public IReadOnlyList<Profile> GetActiveProfiles()
        {
            return SubscriptionState?.ActiveProfiles ?? _context.ActiveProfiles;
        }

        public bool IsProfileActive(Guid profileGuid)
        {
            return SubscriptionState != null && SubscriptionState.IsActive &&
                   SubscriptionState.ActiveProfiles.Any(profile => profile.Guid == profileGuid);
        }

        public bool ActivateProfile(Profile profile, bool refreshDevices = true)
        {
            if (profile == null) return false;
            var activeProfiles = GetActiveProfiles();
            var alreadyExclusive = activeProfiles.Count == 1 && activeProfiles[0].Guid == profile.Guid;
            if (alreadyExclusive && !refreshDevices) return true;

            Logger.Info((alreadyExclusive ? "Rebuilding active profile after device refresh: {" : "Activating profile exclusively: {") +
                        profile.ProfileBreadCrumbs() + "}");
            return RebuildActiveProfiles(new List<Profile> { profile }, refreshDevices, profile);
        }

        public bool ActivateProfileAlongside(Profile profile, bool refreshDevices = true)
        {
            if (profile == null) return false;
            var targetProfiles = GetActiveProfiles().ToList();
            var alreadyActive = targetProfiles.Any(active => active.Guid == profile.Guid);
            if (!alreadyActive) targetProfiles.Add(profile);
            else if (!refreshDevices) return true;

            Logger.Info((alreadyActive ? "Rebuilding multi-profile runtime: {" : "Explicitly adding profile alongside active set: {") +
                        profile.ProfileBreadCrumbs() + "}");
            return RebuildActiveProfiles(targetProfiles, refreshDevices, profile);
        }

        public bool RefreshActiveProfile(Profile profile)
        {
            if (profile == null || !IsProfileActive(profile.Guid)) return false;
            Logger.Info("Refreshing active profile after runtime configuration change: {" +
                        profile.ProfileBreadCrumbs() + "}");
            return RebuildActiveProfiles(GetActiveProfiles().ToList(), false, profile);
        }

        public bool DeactivateProfile(Profile profile)
        {
            if (profile == null) return true;
            var activeProfiles = GetActiveProfiles();
            var targetProfiles = activeProfiles.Where(active => active.Guid != profile.Guid).ToList();
            if (targetProfiles.Count == activeProfiles.Count) return true;
            Logger.Info("Removing profile from active set: {" + profile.ProfileBreadCrumbs() + "}");
            return RebuildActiveProfiles(targetProfiles, false, profile);
        }

        public bool DeactivateCurrentProfile()
        {
            CancelEmergencyStopTimers();
            if (SubscriptionState == null)
            {
                _context.SetActiveProfiles(Enumerable.Empty<Profile>());
                ProfileActive = false;
                return _exclusiveMode.Apply(Enumerable.Empty<Profile>());
            }

            var state = SubscriptionState;
            var success = !state.IsActive || DeactivateProfile(state);
            SubscriptionState = null;
            _context.SetActiveProfiles(Enumerable.Empty<Profile>());
            ProfileActive = false;
            _context.OnActiveProfileChangedEvent(null);
            return _exclusiveMode.Apply(Enumerable.Empty<Profile>()) && success;
        }

        private bool RebuildActiveProfiles(IList<Profile> targetProfiles, bool refreshDevices, Profile changedProfile)
        {
            targetProfiles = NormalizeProfiles(targetProfiles);
            var previousProfiles = NormalizeProfiles(GetActiveProfiles().ToList());
            var previousState = SubscriptionState;

            if (refreshDevices)
            {
                Logger.Debug("Refreshing device providers before rebuilding active profiles");
                _context.DevicesManager.RefreshDeviceList();
            }

            if (targetProfiles.Count == 0)
            {
                var stopSuccess = true;
                if (previousState != null && previousState.IsActive)
                {
                    stopSuccess = DeactivateProfile(previousState);
                    if (!stopSuccess)
                        Logger.Warn("One or more subscriptions could not be removed while stopping the active profile set.");
                }
                SubscriptionState = null;
                _context.SetActiveProfiles(Enumerable.Empty<Profile>());
                ProfileActive = false;
                var released = _exclusiveMode.Apply(Enumerable.Empty<Profile>());
                if (!released) Logger.Error("Exclusive Device Mode cleanup failed on profile stop.");
                _context.OnActiveProfileChangedEvent(changedProfile);
                return stopSuccess && released;
            }

            foreach (var profile in targetProfiles)
            {
                if (!profile.PruneUndefinedFilterReferencesRecursive()) continue;
                Logger.Warn("Removed undefined filter references before activating profile: {" + profile.ProfileBreadCrumbs() + "}");
                _context.ContextChanged();
            }

            // Build and validate the candidate state before disturbing the currently running set.
            // This catches missing mappings/filter failures without dropping a working profile.
            var candidate = BuildSubscriptionState(targetProfiles);
            if (candidate == null)
            {
                Logger.Error("Unable to build candidate active profile state; existing profiles remain running.");
                return false;
            }

            var unsubscribeSuccess = true;
            if (previousState != null && previousState.IsActive)
            {
                unsubscribeSuccess = DeactivateProfile(previousState);
                if (!unsubscribeSuccess)
                    Logger.Warn("One or more subscriptions could not be removed while switching active profiles.");
            }
            SubscriptionState = null;

            if (!ActivateSubscriptionState(candidate))
            {
                Logger.Error("Failed to activate candidate profile set; restoring the previous active profiles.");
                if (candidate.IsActive) DeactivateProfile(candidate);
                SubscriptionState = null;

                if (previousProfiles.Count > 0 && RestorePreviousProfiles(previousProfiles, changedProfile))
                {
                    Logger.Warn("Previous active profile set restored after activation failure.");
                }
                else
                {
                    ClearFailedState(null, changedProfile);
                }
                return false;
            }

            // Hide the physical controller only after its input subscriptions exist.
            // If HidHide is unavailable, do not claim the requested exclusive profile started.
            if (!_exclusiveMode.Apply(targetProfiles))
            {
                Logger.Error("Unable to apply Exclusive Device Mode; restoring the previous profile.");
                DeactivateProfile(candidate);
                SubscriptionState = null;
                if (previousProfiles.Count > 0 && RestorePreviousProfiles(previousProfiles, changedProfile))
                    return false;
                // A failed HidHide write may have partially hidden a controller.
                // Recover our ownership journal before leaving UCR inactive.
                if (!_exclusiveMode.Apply(Enumerable.Empty<Profile>()))
                    Logger.Error("Exclusive Device Mode cleanup failed after activation rollback.");
                ClearFailedState(null, changedProfile);
                return false;
            }
            FinalizeNewState(targetProfiles, candidate, changedProfile);
            // The candidate is live. Teardown warnings from the old state are forensic only and must
            // not make the UI claim this successful switch failed.
            return true;
        }

        private static List<Profile> NormalizeProfiles(IEnumerable<Profile> profiles)
        {
            return (profiles ?? Enumerable.Empty<Profile>())
                .Where(profile => profile != null)
                .GroupBy(profile => profile.Guid)
                .Select(group => group.First())
                .ToList();
        }

        private SubscriptionState BuildSubscriptionState(IList<Profile> profiles)
        {
            var state = new SubscriptionState(profiles);
            foreach (var profile in profiles)
            {
                var populatedLayers = new HashSet<Guid>();
                var profileOutputDevices = new List<DeviceConfigurationSubscription>();
                if (!PopulateSubscriptionStateForProfile(state, profile, profile.Guid, populatedLayers, profileOutputDevices))
                {
                    Logger.Error("Failed to populate SubscriptionState for profile: " + profile.ProfileBreadCrumbs());
                    return null;
                }
            }

            Logger.Debug("Successfully populated composite subscription state");
            if (ConfigureFiltersForState(state)) return state;

            Logger.Error("Failed to configure filters for composite subscription state");
            return null;
        }

        private bool RestorePreviousProfiles(IList<Profile> previousProfiles, Profile changedProfile)
        {
            var rollback = BuildSubscriptionState(previousProfiles);
            if (rollback == null) return false;
            if (!ActivateSubscriptionState(rollback))
            {
                if (rollback.IsActive) DeactivateProfile(rollback);
                return false;
            }

            if (!_exclusiveMode.Apply(previousProfiles))
                Logger.Error("Unable to restore Exclusive Device Mode for previous profiles.");
            FinalizeNewState(previousProfiles, rollback, changedProfile);
            return true;
        }

        private void ClearFailedState(SubscriptionState state, Profile changedProfile)
        {
            if (state != null && state.IsActive) DeactivateProfile(state);
            SubscriptionState = null;
            _context.SetActiveProfiles(Enumerable.Empty<Profile>());
            ProfileActive = false;
            _context.OnActiveProfileChangedEvent(changedProfile);
        }

        private void FinalizeNewState(IList<Profile> profiles, SubscriptionState subscriptionState, Profile changedProfile)
        {
            SubscriptionState = subscriptionState;
            _context.SetActiveProfiles(profiles);

            foreach (var mapping in subscriptionState.MappingSubscriptions)
            {
                if (mapping.Overriden) continue;
                foreach (var pluginSubscription in mapping.PluginSubscriptions)
                {
                    pluginSubscription.Plugin.InitializeCacheValues();
                    pluginSubscription.Plugin.OnActivate();
                }
            }

            ProfileActive = profiles.Count > 0;
            _context.OnActiveProfileChangedEvent(changedProfile);
            Logger.Info("Active profile set rebuilt: " + string.Join(", ", profiles.Select(profile => profile.Title)));
        }

        public bool DeactivateProfile(SubscriptionState state)
        {
            if (state == null) return true;
            var success = true;
            CancelEmergencyStopTimers();

            // Safety subscriptions include the global Esc rescue key and generated blockers for
            // Block Unmapped Inputs. Remove these first so normal input is restored immediately.
            foreach (var safetySubscription in state.SafetyInputSubscriptions.ToList())
            {
                success &= UnsubscribeDeviceBindingInput(state, safetySubscription);
            }
            state.SafetyInputSubscriptions.Clear();

            foreach (var mappingSubscription in state.MappingSubscriptions)
            {
                if (mappingSubscription.Overriden) continue;

                foreach (var deviceBindingSubscription in mappingSubscription.DeviceBindingSubscriptions)
                {
                    success &= UnsubscribeDeviceBindingInput(state, deviceBindingSubscription);
                }

                foreach (var pluginSubscription in mappingSubscription.PluginSubscriptions)
                {
                    pluginSubscription.Plugin.OnDeactivate();
                    pluginSubscription.DetachOutputs();
                }
            }

            foreach (var deviceConfigurationSubscription in state.OutputDeviceConfigurationSubscriptions)
            {
                foreach (var shadowDeviceSubscription in deviceConfigurationSubscription.ShadowDeviceSubscriptions)
                {
                    success &= UnsubscribeOutput(state, shadowDeviceSubscription);
                }

                success &= UnsubscribeOutput(state, deviceConfigurationSubscription.DeviceSubscription);
            }

            state.IsActive = false;
            return success;
        }

        #endregion

        private bool PopulateSubscriptionStateForProfile(SubscriptionState state, Profile profile,
            Guid runtimeScopeGuid, ISet<Guid> populatedLayers,
            ICollection<DeviceConfigurationSubscription> profileOutputDevices)
        {
            if (profile == null) return true;
            if (populatedLayers.Contains(profile.Guid)) return true;

            var success = true;
            profile.PrepareProfile();

            if (profile.ParentProfile != null)
            {
                success &= PopulateSubscriptionStateForProfile(state, profile.ParentProfile, runtimeScopeGuid,
                    populatedLayers, profileOutputDevices);
            }

            foreach (var deviceConfiguration in profile.OutputDeviceConfigurations ?? new List<DeviceConfiguration>())
            {
                var subscription = state.AddOutputDeviceConfiguration(deviceConfiguration, runtimeScopeGuid);
                if (subscription != null && !profileOutputDevices.Contains(subscription))
                    profileOutputDevices.Add(subscription);
            }

            var scopedOutputs = profileOutputDevices.ToList();
            state.AddMappings(profile, profile.Mappings ?? new List<Mapping>(), runtimeScopeGuid, scopedOutputs);
            foreach (var group in profile.MappingGroups ?? new List<MappingGroup>())
            {
                if (group == null || !group.Enabled) continue;
                state.AddMappings(profile, group.Mappings ?? new List<Mapping>(), runtimeScopeGuid, scopedOutputs);
            }

            populatedLayers.Add(profile.Guid);
            return success;
        }

        private bool ConfigureFiltersForState(SubscriptionState state)
        {
            var uniqueFilters = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);
            foreach (var mappingSubscription in state.MappingSubscriptions)
            {
                if (mappingSubscription.Overriden) continue;
                var runtimeMapping = mappingSubscription.Mapping;
                foreach (var plugin in runtimeMapping.Plugins)
                {
                    var definition = plugin.GetDefinedFilterName();
                    if (string.IsNullOrWhiteSpace(definition)) continue;

                    var key = Mapping.GetRuntimeFilterKey(mappingSubscription.RuntimeScopeGuid, definition);
                    if (runtimeMapping.IsShadowMapping)
                    {
                        key = Filter.GetShadowName(key, runtimeMapping.ShadowDeviceNumber);
                    }
                    uniqueFilters.Add(key);
                }
            }

            foreach (var uniqueFilter in uniqueFilters)
            {
                if (!state.FilterState.FilterRuntimeDictionary.ContainsKey(uniqueFilter))
                {
                    state.FilterState.FilterRuntimeDictionary.Add(uniqueFilter, false);
                }
            }

            return true;
        }

        // Subscribes the backend when it is built
        private bool ActivateSubscriptionState(SubscriptionState state)
        {
            var success = true;
            if (state.IsActive) return true;

            // Most mappings in a profile point at the same one or two configured devices. Resolve a
            // configured Device object once per rebuild instead of re-enumerating providers for every
            // individual binding that happens to use it.
            var inputResolutionCache = new Dictionary<Device, Device>(DeviceReferenceComparer.Instance);
            var outputResolutionCache = new Dictionary<Device, Device>(DeviceReferenceComparer.Instance);

            foreach (var deviceConfigurationSubscription in state.OutputDeviceConfigurationSubscriptions)
            {
                success &= SubscribeOutput(state, deviceConfigurationSubscription.DeviceSubscription,
                    outputResolutionCache);

                foreach (var shadowDeviceSubscription in deviceConfigurationSubscription.ShadowDeviceSubscriptions)
                {
                    success &= SubscribeOutput(state, shadowDeviceSubscription, outputResolutionCache);
                }
            }

            // Install the hardwired rescue listener before any profile input subscription can start
            // suppressing keys. It is deliberately outside the mapping engine.
            success &= SubscribeEmergencyStopInputs(state, inputResolutionCache);

            foreach (var mappingSubscription in state.MappingSubscriptions)
            {
                if (mappingSubscription.Overriden) continue;

                mappingSubscription.Mapping.PrepareMapping(state.FilterState, mappingSubscription.RuntimeScopeGuid);

                foreach (var deviceBindingSubscription in mappingSubscription.DeviceBindingSubscriptions)
                {
                    success &= SubscribeDeviceBindingInput(state, deviceBindingSubscription, inputResolutionCache);
                }
            }

            success &= SubscribeUnmappedInputBlockers(state, inputResolutionCache);

            state.IsActive = true;
            return success;
        }

        private bool SubscribeEmergencyStopInputs(SubscriptionState state,
            IDictionary<Device, Device> resolutionCache)
        {
            var success = true;
            List<Device> devices;
            try
            {
                devices = _context.DevicesManager.GetAvailableDeviceList(DeviceIoType.Input, false);
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "Unable to enumerate keyboards for the emergency stop");
                return false;
            }

            foreach (var configuredDevice in devices.Where(device => device != null && !device.IsCache))
            {
                var runtimeDevice = ResolveRuntimeDevice(configuredDevice, DeviceIoType.Input, resolutionCache);
                if (runtimeDevice == null) continue;

                var escape = FindEscapeBinding(runtimeDevice);
                if (escape == null) continue;

                var deviceKey = GetRuntimeDeviceKey(runtimeDevice);
                var binding = CreateSafetyBinding(state.ActiveProfile, escape, false,
                    value => HandleEmergencyEscape(deviceKey, value));
                var subscription = new InputSubscription(binding, state.ActiveProfile, state.StateGuid, runtimeDevice);
                subscription.DeviceSubscription.ResolvedDevice = runtimeDevice;
                state.SafetyInputSubscriptions.Add(subscription);
                var subscribed = SubscribeDeviceBindingInput(state, subscription, resolutionCache);
                success &= subscribed;
                if (subscribed)
                {
                    lock (_emergencyStopLock) _emergencyStopDeviceKeys.Add(deviceKey);
                }
            }

            return success;
        }

        private bool SubscribeUnmappedInputBlockers(SubscriptionState state,
            IDictionary<Device, Device> resolutionCache)
        {
            var success = true;
            var mappedByDevice = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var mappingSubscription in state.MappingSubscriptions)
            {
                if (mappingSubscription.Overriden) continue;
                foreach (var inputSubscription in mappingSubscription.DeviceBindingSubscriptions)
                {
                    var runtimeDevice = inputSubscription.DeviceSubscription?.ResolvedDevice;
                    var binding = inputSubscription.DeviceBinding;
                    if (runtimeDevice == null || binding == null || !binding.IsBound) continue;

                    var deviceKey = GetRuntimeDeviceKey(runtimeDevice);
                    HashSet<string> mapped;
                    if (!mappedByDevice.TryGetValue(deviceKey, out mapped))
                    {
                        mapped = new HashSet<string>(StringComparer.Ordinal);
                        mappedByDevice[deviceKey] = mapped;
                    }
                    mapped.Add(GetBindingKey(binding.KeyType, binding.KeyValue, binding.KeySubValue));
                }
            }

            var processedDevices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var profile in state.ActiveProfiles)
            {
                if (profile == null) continue;

                foreach (var configuration in profile.GetDeviceConfigurationList(DeviceIoType.Input)
                    .Where(item => item != null && item.Device != null && profile.IsBlockUnmappedInputsEnabled(item)))
                {
                    var runtimeDevice = ResolveRuntimeDevice(configuration.Device, DeviceIoType.Input, resolutionCache);
                    if (runtimeDevice == null)
                    {
                        Logger.Error("Block Unmapped Inputs could not resolve device: {" + configuration.Device.LogName() + "}");
                        success = false;
                        continue;
                    }

                    var deviceKey = GetRuntimeDeviceKey(runtimeDevice);
                    if (!processedDevices.Add(deviceKey)) continue;

                    // A keyboard may only enter Block Unmapped mode after its hardwired Esc listener
                    // is confirmed live. Failure is deliberately fail-open.
                    if (FindEscapeBinding(runtimeDevice) != null)
                    {
                        lock (_emergencyStopLock)
                        {
                            if (!_emergencyStopDeviceKeys.Contains(deviceKey))
                            {
                                Logger.Error("Refusing Block Unmapped Inputs because the emergency Esc listener is unavailable for: {" +
                                             runtimeDevice.LogName() + "}");
                                success = false;
                                continue;
                            }
                        }
                    }

                    HashSet<string> mapped;
                    if (!mappedByDevice.TryGetValue(deviceKey, out mapped))
                        mapped = new HashSet<string>(StringComparer.Ordinal);

                    foreach (var node in FlattenBindingNodes(
                        _context.DevicesManager.GetDeviceBindingMenu(runtimeDevice, DeviceIoType.Input, false)))
                    {
                        var info = node?.DeviceBindingInfo;
                        if (info == null || !info.Blockable) continue;
                        if (mapped.Contains(GetBindingKey(info.KeyType, info.KeyValue, info.KeySubValue))) continue;

                        var binding = CreateSafetyBinding(profile, info, true, value => { });
                        var subscription = new InputSubscription(binding, profile, state.StateGuid, runtimeDevice);
                        subscription.DeviceSubscription.ResolvedDevice = runtimeDevice;
                        state.SafetyInputSubscriptions.Add(subscription);
                        success &= SubscribeDeviceBindingInput(state, subscription, resolutionCache);
                    }
                }
            }

            return success;
        }

        private DeviceBindingInfo FindEscapeBinding(Device runtimeDevice)
        {
            var nodes = FlattenBindingNodes(
                _context.DevicesManager.GetDeviceBindingMenu(runtimeDevice, DeviceIoType.Input, false)).ToList();

            var named = nodes.FirstOrDefault(node =>
                node?.DeviceBindingInfo != null &&
                (string.Equals(node.Title, "Esc", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(node.Title, "Escape", StringComparison.OrdinalIgnoreCase)));
            if (named != null) return named.DeviceBindingInfo;

            // Interception exposes keyboard scan code 1 as button index 0. Keep this fallback
            // deliberately keyboard-specific; mouse button zero must never become a rescue key.
            if (string.Equals(runtimeDevice.ProviderName, "Core_Interception", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(runtimeDevice.Title) &&
                runtimeDevice.Title.IndexOf("keyboard", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return nodes.Select(node => node?.DeviceBindingInfo)
                    .FirstOrDefault(info => info != null &&
                                            info.DeviceBindingCategory == DeviceBindingCategory.Momentary &&
                                            info.KeyValue == 0 && info.KeySubValue == 0);
            }

            return null;
        }

        private static DeviceBinding CreateSafetyBinding(Profile profile, DeviceBindingInfo info, bool block,
            Action<short> callback)
        {
            return new DeviceBinding(callback ?? (value => { }), profile, DeviceIoType.Input)
            {
                IsBound = true,
                DeviceBindingCategory = info.DeviceBindingCategory,
                KeyType = info.KeyType,
                KeyValue = info.KeyValue,
                KeySubValue = info.KeySubValue,
                Block = block
            };
        }

        private void HandleEmergencyEscape(string deviceKey, short value)
        {
            if (string.IsNullOrWhiteSpace(deviceKey)) return;

            lock (_emergencyStopLock)
            {
                Timer existing;
                if (value != 0)
                {
                    if (_emergencyStopTimers.TryGetValue(deviceKey, out existing)) return;
                    _emergencyStopTimers[deviceKey] = new Timer(
                        ignored => TriggerEmergencyStop(deviceKey), null,
                        EmergencyStopHoldMilliseconds, Timeout.Infinite);
                    return;
                }

                if (!_emergencyStopTimers.TryGetValue(deviceKey, out existing)) return;
                _emergencyStopTimers.Remove(deviceKey);
                existing.Dispose();
            }
        }

        private void TriggerEmergencyStop(string deviceKey)
        {
            lock (_emergencyStopLock)
            {
                Timer timer;
                if (_emergencyStopTimers.TryGetValue(deviceKey, out timer))
                {
                    _emergencyStopTimers.Remove(deviceKey);
                    timer.Dispose();
                }
            }

            if (Interlocked.Exchange(ref _emergencyStopInProgress, 1) != 0) return;
            try
            {
                Logger.Warn("Emergency stop triggered: Esc held for three seconds.");
                DeactivateCurrentProfile();
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "Emergency stop failed while stopping active profiles");
            }
            finally
            {
                Interlocked.Exchange(ref _emergencyStopInProgress, 0);
            }
        }

        private void CancelEmergencyStopTimers()
        {
            lock (_emergencyStopLock)
            {
                foreach (var timer in _emergencyStopTimers.Values) timer.Dispose();
                _emergencyStopTimers.Clear();
                _emergencyStopDeviceKeys.Clear();
            }
        }

        private static IEnumerable<DeviceBindingNode> FlattenBindingNodes(IEnumerable<DeviceBindingNode> nodes)
        {
            if (nodes == null) yield break;
            foreach (var node in nodes)
            {
                if (node == null) continue;
                if (node.IsBinding) yield return node;
                foreach (var child in FlattenBindingNodes(node.ChildrenNodes)) yield return child;
            }
        }

        private static string GetBindingKey(int keyType, int keyValue, int keySubValue)
        {
            return keyType + ":" + keyValue + ":" + keySubValue;
        }

        private static string GetRuntimeDeviceKey(Device device)
        {
            if (device == null) return string.Empty;
            return (device.ProviderName ?? string.Empty) + "\u001f" +
                   (device.DeviceHandle ?? string.Empty) + "\u001f" + device.DeviceNumber;
        }


        #region Subscriber Actions
        
        private bool SubscribeDeviceBindingInput(SubscriptionState state, InputSubscription deviceBindingSubscription,
            IDictionary<Device, Device> resolutionCache)
        {
            if (deviceBindingSubscription?.DeviceBinding == null) return false;
            if (!deviceBindingSubscription.DeviceBinding.IsBound) return true;

            var deviceSubscription = deviceBindingSubscription.DeviceSubscription;
            var configuredDevice = deviceSubscription?.Device;
            if (configuredDevice == null)
            {
                Logger.Error("Failed to subscribe input because its configured device is unavailable.");
                return false;
            }

            try
            {
                var runtimeDevice = ResolveRuntimeDevice(configuredDevice, DeviceIoType.Input, resolutionCache);
                if (runtimeDevice == null)
                {
                    Logger.Error($"Failed to resolve input device safely: {{{configuredDevice.LogName()}}}");
                    return false;
                }

                deviceSubscription.ResolvedDevice = runtimeDevice;
                return _context.IOController.SubscribeInput(GetInputSubscriptionRequest(state,
                    deviceBindingSubscription));
            }
            catch (Exception e)
            {
                Logger.Error(e, "Failed to subscribe input");
                return false;
            }
        }

        private bool UnsubscribeDeviceBindingInput(SubscriptionState state, InputSubscription deviceBindingSubscription)
        {
            if (deviceBindingSubscription?.DeviceBinding == null || !deviceBindingSubscription.DeviceBinding.IsBound)
                return true;
            if (deviceBindingSubscription.DeviceSubscription?.ResolvedDevice == null) return true;
            return _context.IOController.UnsubscribeInput(GetInputSubscriptionRequest(state, deviceBindingSubscription));
        }

        private bool SubscribeOutput(SubscriptionState state, DeviceSubscription deviceSubscription,
            IDictionary<Device, Device> resolutionCache)
        {
            var configuredDevice = deviceSubscription?.Device;
            if (configuredDevice == null)
            {
                Logger.Error("Failed to subscribe output because its configured device is unavailable.");
                return false;
            }

            Logger.Debug($"Subscribing output device: {{{configuredDevice.LogName()}}}");
            if (string.IsNullOrEmpty(configuredDevice.ProviderName) || string.IsNullOrEmpty(configuredDevice.DeviceHandle))
            {
                Logger.Error($"Failed to subscribe output device. Providername or devicehandle missing from: {{{configuredDevice.LogName()}}}");
                return false;
            }

            var runtimeDevice = ResolveRuntimeDevice(configuredDevice, DeviceIoType.Output, resolutionCache);
            if (runtimeDevice == null)
            {
                Logger.Error($"Failed to resolve output device safely: {{{configuredDevice.LogName()}}}");
                return false;
            }

            deviceSubscription.ResolvedDevice = runtimeDevice;
            var success = _context.IOController.SubscribeOutput(GetOutputSubscriptionRequest(state.StateGuid, deviceSubscription));

            if (!success) Logger.Error($"Failed to subscribe output device. Provider might be unavailable: {{{configuredDevice.LogName()}}}");

            return success;
        }

        private Device ResolveRuntimeDevice(Device configuredDevice, DeviceIoType type,
            IDictionary<Device, Device> resolutionCache)
        {
            if (configuredDevice == null) return null;

            Device resolvedDevice;
            if (resolutionCache != null && resolutionCache.TryGetValue(configuredDevice, out resolvedDevice))
                return resolvedDevice;

            resolvedDevice = _context.DevicesManager.ResolveDevice(configuredDevice, type);
            if (resolutionCache != null) resolutionCache[configuredDevice] = resolvedDevice;
            return resolvedDevice;
        }

        private bool UnsubscribeOutput(SubscriptionState state, DeviceSubscription deviceSubscription)
        {
            Logger.Debug($"Unsubscribing output device: {{{deviceSubscription.Device.LogName()}}}");
            if (deviceSubscription.ResolvedDevice == null) return true;
            if (string.IsNullOrEmpty(deviceSubscription.ResolvedDevice.ProviderName) || string.IsNullOrEmpty(deviceSubscription.ResolvedDevice.DeviceHandle))
            {
                Logger.Error($"Failed to unsubscribe output device. Providername or devicehandle missing from: {{{deviceSubscription.ResolvedDevice.LogName()}}}");
                return false;
            }
            return _context.IOController.UnsubscribeOutput(GetOutputSubscriptionRequest(state.StateGuid, deviceSubscription));
        }

        #endregion

        #region DescriptionHelpers

        private InputSubscriptionRequest GetInputSubscriptionRequest(SubscriptionState state, InputSubscription deviceBindingSubscription)
        {
            var device = deviceBindingSubscription.DeviceSubscription.GetRuntimeDevice();
            return new InputSubscriptionRequest()
            {
                ProviderDescriptor = GetProviderDescriptor(device),
                DeviceDescriptor = GetDeviceDescriptor(device),
                SubscriptionDescriptor = GetSubscriptionDescriptor(deviceBindingSubscription.DeviceBindingSubscriptionGuid, state.StateGuid),
                BindingDescriptor = GetBindingDescriptor(deviceBindingSubscription.DeviceBinding),
                Callback = deviceBindingSubscription.DeviceBinding.Callback,
                Block = deviceBindingSubscription.DeviceBinding.Block
            };
        }

        public static OutputSubscriptionRequest GetOutputSubscriptionRequest(Guid subscriptionStateGuid, DeviceSubscription deviceSubscription)
        {
            return new OutputSubscriptionRequest()
            {
                ProviderDescriptor = GetProviderDescriptor(deviceSubscription.GetRuntimeDevice()),
                DeviceDescriptor = GetDeviceDescriptor(deviceSubscription.GetRuntimeDevice()),
                SubscriptionDescriptor = GetSubscriptionDescriptor(deviceSubscription.DeviceSubscriptionGuid, subscriptionStateGuid)
            };
        }

        private static ProviderDescriptor GetProviderDescriptor(Device device)
        {
            return new ProviderDescriptor()
            {
                ProviderName = device.ProviderName
            };
        }

        private static DeviceDescriptor GetDeviceDescriptor(Device device)
        {
            return new DeviceDescriptor()
            {
                DeviceHandle = device.DeviceHandle,
                DeviceInstance = device.DeviceNumber
            };
        }

        private static SubscriptionDescriptor GetSubscriptionDescriptor(Guid subscriberGuid, Guid profileGuid)
        {
            return new SubscriptionDescriptor()
            {
                SubscriberGuid = subscriberGuid,
                ProfileGuid = profileGuid
            };
        }

        public static BindingDescriptor GetBindingDescriptor(DeviceBinding deviceBinding)
        {
            return new BindingDescriptor()
            {
                Type = (BindingType)deviceBinding.KeyType,
                Index = deviceBinding.KeyValue,
                SubIndex = deviceBinding.KeySubValue
            };
        }

        #endregion

        public void Dispose()
        {
            if (SubscriptionState != null)
            {
                DeactivateCurrentProfile();
            }
            _exclusiveMode.Dispose();
        }

        private sealed class DeviceReferenceComparer : IEqualityComparer<Device>
        {
            public static readonly DeviceReferenceComparer Instance = new DeviceReferenceComparer();

            public bool Equals(Device x, Device y)
            {
                return ReferenceEquals(x, y);
            }

            public int GetHashCode(Device obj)
            {
                return obj == null ? 0 : RuntimeHelpers.GetHashCode(obj);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        [NotifyPropertyChangedInvocator]
        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
