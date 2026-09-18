using System;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel;
using System.Runtime.CompilerServices;
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
                _profileActive = value;
                OnPropertyChanged();
            }
        }

        internal SubscriptionState SubscriptionState { get; set; }
        private readonly Context _context;

        public SubscriptionsManager(Context context)
        {
            _context = context;
        }

        #region ManagerApi

        public Profile GetActiveProfile()
        {
            return SubscriptionState?.ActiveProfile;
        }

        public IReadOnlyList<Profile> GetActiveProfiles()
        {
            return SubscriptionState?.ActiveProfiles ?? (IReadOnlyList<Profile>)new List<Profile>().AsReadOnly();
        }

        public bool IsProfileActive(Guid profileGuid)
        {
            return SubscriptionState != null && SubscriptionState.IsActive &&
                   SubscriptionState.ActiveProfiles.Any(profile => profile.Guid == profileGuid);
        }

        public bool ActivateProfile(Profile profile, bool refreshDevices = true)
        {
            if (profile == null) return false;
            var targetProfiles = GetActiveProfiles().ToList();
            var alreadyActive = targetProfiles.Any(active => active.Guid == profile.Guid);
            if (!alreadyActive) targetProfiles.Add(profile);
            else if (!refreshDevices) return true;

            Logger.Info((alreadyActive ? "Rebuilding active profile after device refresh: {" : "Adding profile to active set: {") +
                        profile.ProfileBreadCrumbs() + "}");
            return RebuildActiveProfiles(targetProfiles, refreshDevices, profile);
        }

        public bool DeactivateProfile(Profile profile)
        {
            if (profile == null) return true;
            var targetProfiles = GetActiveProfiles().Where(active => active.Guid != profile.Guid).ToList();
            if (targetProfiles.Count == GetActiveProfiles().Count) return true;
            Logger.Info("Removing profile from active set: {" + profile.ProfileBreadCrumbs() + "}");
            return RebuildActiveProfiles(targetProfiles, false, profile);
        }

        public bool DeactivateCurrentProfile()
        {
            if (SubscriptionState == null)
            {
                _context.SetActiveProfiles(Enumerable.Empty<Profile>());
                ProfileActive = false;
                return true;
            }

            var state = SubscriptionState;
            var success = !state.IsActive || DeactivateProfile(state);
            SubscriptionState = null;
            _context.SetActiveProfiles(Enumerable.Empty<Profile>());
            ProfileActive = false;
            _context.OnActiveProfileChangedEvent(null);
            return success;
        }

        private bool RebuildActiveProfiles(IList<Profile> targetProfiles, bool refreshDevices, Profile changedProfile)
        {
            targetProfiles = (targetProfiles ?? new List<Profile>())
                .Where(profile => profile != null)
                .GroupBy(profile => profile.Guid)
                .Select(group => group.First())
                .ToList();

            var unsubscribeSuccess = true;
            if (SubscriptionState != null && SubscriptionState.IsActive)
            {
                unsubscribeSuccess = DeactivateProfile(SubscriptionState);
                if (!unsubscribeSuccess)
                    Logger.Warn("One or more subscriptions could not be removed while rebuilding the active profile set.");
            }
            SubscriptionState = null;

            if (refreshDevices)
            {
                Logger.Debug("Refreshing device providers before rebuilding active profiles");
                _context.DevicesManager.RefreshDeviceList();
            }

            if (targetProfiles.Count == 0)
            {
                _context.SetActiveProfiles(Enumerable.Empty<Profile>());
                ProfileActive = false;
                _context.OnActiveProfileChangedEvent(changedProfile);
                return unsubscribeSuccess;
            }

            foreach (var profile in targetProfiles)
            {
                if (!profile.PruneUndefinedFilterReferencesRecursive()) continue;
                Logger.Warn("Removed undefined filter references before activating profile: {" + profile.ProfileBreadCrumbs() + "}");
                _context.ContextChanged();
            }

            var state = new SubscriptionState(targetProfiles);
            foreach (var profile in targetProfiles)
            {
                var populatedLayers = new HashSet<Guid>();
                var profileOutputDevices = new List<DeviceConfigurationSubscription>();
                if (!PopulateSubscriptionStateForProfile(state, profile, profile.Guid, populatedLayers, profileOutputDevices))
                {
                    Logger.Error("Failed to populate SubscriptionState for profile: " + profile.ProfileBreadCrumbs());
                    ClearFailedState(state, changedProfile);
                    return false;
                }
            }
            Logger.Debug("Successfully populated composite subscription state");

            if (!ConfigureFiltersForState(state))
            {
                Logger.Error("Failed to configure filters for composite subscription state");
                ClearFailedState(state, changedProfile);
                return false;
            }

            if (!ActivateSubscriptionState(state))
            {
                Logger.Error("Failed to activate composite subscription state");
                DeactivateProfile(state);
                ClearFailedState(state, changedProfile);
                return false;
            }

            FinalizeNewState(targetProfiles, state, changedProfile);
            // The new composite state is live at this point. A stale provider may have reported an
            // unsubscribe problem while tearing down the old state, but that must not make the UI
            // claim this successful activation/deactivation failed. The warning above preserves the
            // forensic signal without lying about the current runtime state.
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

                var groupOutputs = scopedOutputs.ToList();
                foreach (var configuration in group.OutputDeviceConfigurations ?? new List<DeviceConfiguration>())
                {
                    AddScopedOutputDevice(state, configuration, runtimeScopeGuid, groupOutputs);
                }

                // Legacy nested children could inherit an output from an ancestor child. Resolve any
                // output referenced by this group's mappings even if that configuration lives in a
                // different local group after migration.
                foreach (var mapping in group.Mappings ?? new List<Mapping>())
                {
                    if (mapping == null) continue;
                    foreach (var plugin in mapping.Plugins ?? new List<Plugin>())
                    {
                        if (plugin == null) continue;
                        foreach (var binding in plugin.Outputs ?? new List<DeviceBinding>())
                        {
                            if (binding == null || binding.DeviceConfigurationGuid == Guid.Empty) continue;
                            if (groupOutputs.Any(output =>
                                    output?.DeviceConfiguration?.Guid == binding.DeviceConfigurationGuid)) continue;

                            var configuration = profile.GetDeviceConfiguration(
                                DeviceIoType.Output, binding.DeviceConfigurationGuid);
                            if (configuration != null)
                                AddScopedOutputDevice(state, configuration, runtimeScopeGuid, groupOutputs);
                        }
                    }
                }

                state.AddMappings(profile, group.Mappings ?? new List<Mapping>(), runtimeScopeGuid, groupOutputs);
            }

            populatedLayers.Add(profile.Guid);
            return success;
        }

        private static void AddScopedOutputDevice(SubscriptionState state,
            DeviceConfiguration configuration, Guid runtimeScopeGuid,
            ICollection<DeviceConfigurationSubscription> scopedOutputs)
        {
            if (state == null || configuration == null || scopedOutputs == null) return;
            var subscription = state.AddOutputDeviceConfiguration(configuration, runtimeScopeGuid);
            if (subscription != null && !scopedOutputs.Contains(subscription))
                scopedOutputs.Add(subscription);
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

            foreach (var deviceConfigurationSubscription in state.OutputDeviceConfigurationSubscriptions)
            {
                success &= SubscribeOutput(state, deviceConfigurationSubscription.DeviceSubscription);

                foreach (var shadowDeviceSubscription in deviceConfigurationSubscription.ShadowDeviceSubscriptions)
                {
                    success &= SubscribeOutput(state, shadowDeviceSubscription);
                }
            }

            foreach (var mappingSubscription in state.MappingSubscriptions)
            {
                if (mappingSubscription.Overriden) continue;

                mappingSubscription.Mapping.PrepareMapping(state.FilterState, mappingSubscription.RuntimeScopeGuid);

                foreach (var deviceBindingSubscription in mappingSubscription.DeviceBindingSubscriptions)
                {
                    success &= SubscribeDeviceBindingInput(state, deviceBindingSubscription);
                }
            }

            state.IsActive = true;
            return success;
        }


        #region Subscriber Actions
        
        private bool SubscribeDeviceBindingInput(SubscriptionState state, InputSubscription deviceBindingSubscription)
        {
            if (!deviceBindingSubscription.DeviceBinding.IsBound) return true;
            try
            {
                var runtimeDevice = _context.DevicesManager.ResolveDevice(
                    deviceBindingSubscription.DeviceSubscription.Device,
                    DeviceIoType.Input);
                if (runtimeDevice == null)
                {
                    Logger.Error($"Failed to resolve input device safely: {{{deviceBindingSubscription.DeviceSubscription.Device.LogName()}}}");
                    return false;
                }

                deviceBindingSubscription.DeviceSubscription.ResolvedDevice = runtimeDevice;
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
            if (!deviceBindingSubscription.DeviceBinding.IsBound) return true;
            if (deviceBindingSubscription.DeviceSubscription.ResolvedDevice == null) return true;
            return _context.IOController.UnsubscribeInput(GetInputSubscriptionRequest(state, deviceBindingSubscription));
        }

        private bool SubscribeOutput(SubscriptionState state, DeviceSubscription deviceSubscription)
        {
            Logger.Debug($"Subscribing output device: {{{deviceSubscription.Device.LogName()}}}");
            if (string.IsNullOrEmpty(deviceSubscription.Device.ProviderName) || string.IsNullOrEmpty(deviceSubscription.Device.DeviceHandle))
            {
                Logger.Error($"Failed to subscribe output device. Providername or devicehandle missing from: {{{deviceSubscription.Device.LogName()}}}");
                return false;
            }

            var runtimeDevice = _context.DevicesManager.ResolveDevice(deviceSubscription.Device, DeviceIoType.Output);
            if (runtimeDevice == null)
            {
                Logger.Error($"Failed to resolve output device safely: {{{deviceSubscription.Device.LogName()}}}");
                return false;
            }

            deviceSubscription.ResolvedDevice = runtimeDevice;
            var success = _context.IOController.SubscribeOutput(GetOutputSubscriptionRequest(state.StateGuid, deviceSubscription));

            if (!success) Logger.Error($"Failed to subscribe output device. Provider might be unavailable: {{{deviceSubscription.Device.LogName()}}}");

            return success;
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
        }

        public event PropertyChangedEventHandler PropertyChanged;

        [NotifyPropertyChangedInvocator]
        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
