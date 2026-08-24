using System;
using System.Collections.Generic;
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

        public bool ActivateProfile(Profile profile, bool refreshDevices = true)
        {
            if (refreshDevices) _context.IOController.RefreshDevices();

            if (profile.PruneUndefinedFilterReferencesRecursive())
            {
                Logger.Warn($"Removed undefined filter references before activating profile: {{{profile.ProfileBreadCrumbs()}}}");
                _context.ContextChanged();
            }

            Logger.Debug($"Activating profile: {{{profile.ProfileBreadCrumbs()}}}");
            if (SubscriptionState?.ActiveProfile?.Guid == profile.Guid) return true;

            var state = new SubscriptionState(profile);
            if (!PopulateSubscriptionStateForProfile(state, profile))
            {
                Logger.Error("Failed to populate SubscriptionState");
                return false;
            }
            Logger.Debug("Successfully populated subscription state");

            if (!ConfigureFiltersForState(state, profile))
            {
                Logger.Error("Failed to configure filters for profile successfully");
                return false;
            }
            Logger.Debug("Successfully configured filters for subscription state");

            if (!ActivateSubscriptionState(state))
            {
                Logger.Error("Failed to activate profile successfully");
                DeactivateProfile(state);
                return false;
            }
            Logger.Debug("SubscriptionState successfully activated");

            if (!DeactivateCurrentProfile()) Logger.Error("Failed to deactivate previous profile successfully");
            
            FinalizeNewState(profile, state);

            return true;
        }

        private void FinalizeNewState(Profile profile, SubscriptionState subscriptionState)
        {
            // Set new active profile
            SubscriptionState = subscriptionState;
            _context.ActiveProfile = profile;

            // Activate plugins
            foreach (var mapping in subscriptionState.MappingSubscriptions)
            {
                foreach (var pluginSubscription in mapping.PluginSubscriptions)
                {
                    pluginSubscription.Plugin.InitializeCacheValues();
                    pluginSubscription.Plugin.OnActivate();
                }
            }

            ProfileActive = true;
            _context.OnActiveProfileChangedEvent(profile);
        }

        public bool DeactivateCurrentProfile()
        {
            if (SubscriptionState == null) return true;
            
            var state = SubscriptionState;
            if (!state.IsActive) return true;

            var success = DeactivateProfile(state);

            SubscriptionState = null;
            _context.ActiveProfile = null;
            _context.OnActiveProfileChangedEvent(null);
            ProfileActive = false;

            return success;
        }

        public bool DeactivateProfile(SubscriptionState state)
        {
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

            return success;
        }

        #endregion

        private bool PopulateSubscriptionStateForProfile(SubscriptionState state, Profile profile)
        {
            var success = true;
            profile.PrepareProfile();

            if (profile.ParentProfile != null)
            {
                success &= PopulateSubscriptionStateForProfile(state, profile.ParentProfile);
            }

            foreach (var deviceConfiguration in profile.OutputDeviceConfigurations)
            {
                state.AddOutputDeviceConfiguration(deviceConfiguration);
            }

            state.AddMappings(profile, state.OutputDeviceConfigurationSubscriptions);
            
            return success;
        }
        private bool ConfigureFiltersForState(SubscriptionState state, Profile profile)
        {
            // Allocate runtime state from actual filter definitions, never from mappings that merely
            // reference a filter. Build the set from the subscription mappings so shadow clones get
            // their corresponding shadow definition keys as well.
            var uniqueFilters = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);
            foreach (var mappingSubscription in state.MappingSubscriptions)
            {
                var runtimeMapping = mappingSubscription.Mapping;
                foreach (var plugin in runtimeMapping.Plugins)
                {
                    var definition = plugin.GetDefinedFilterName();
                    if (string.IsNullOrWhiteSpace(definition)) continue;

                    var key = definition.Trim().ToLowerInvariant();
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

                mappingSubscription.Mapping.PrepareMapping(state.FilterState);

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
