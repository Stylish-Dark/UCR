using System;
using System.Collections.Generic;

namespace HidWizards.UCR.Core.Models.Subscription
{
    public class MappingSubscription
    {
        public Profile Profile { get; }
        public Guid RuntimeScopeGuid { get; }
        public Mapping Mapping { get; }
        internal Mapping SourceMapping { get; }
        public List<InputSubscription> DeviceBindingSubscriptions { get; }
        public List<PluginSubscription> PluginSubscriptions { get; }
        public bool Overriden { get; set; }

        public MappingSubscription(Profile profile, Mapping mapping, Guid subscriptionStateGuid,
            Guid runtimeScopeGuid, List<DeviceConfigurationSubscription> subscriptionOutputDeviceConfigurations)
            : this(profile, mapping, subscriptionStateGuid, runtimeScopeGuid, subscriptionOutputDeviceConfigurations, mapping)
        {
        }

        internal MappingSubscription(Profile profile, Mapping mapping, Guid subscriptionStateGuid,
            Guid runtimeScopeGuid, List<DeviceConfigurationSubscription> subscriptionOutputDeviceConfigurations,
            Mapping sourceMapping)
        {
            Profile = profile;
            RuntimeScopeGuid = runtimeScopeGuid;
            Mapping = mapping;
            SourceMapping = sourceMapping;
            Overriden = false;
            DeviceBindingSubscriptions = new List<InputSubscription>();
            foreach (var mappingDeviceBinding in Mapping.DeviceBindings)
            {
                if (!mappingDeviceBinding.IsBound) continue;

                // Preserve invalid bound references so candidate validation can report them.
                DeviceBindingSubscriptions.Add(new InputSubscription(mapping, mappingDeviceBinding, profile, subscriptionStateGuid));
            }

            PluginSubscriptions = new List<PluginSubscription>();
            foreach (var mappingPlugin in Mapping.Plugins)
            {
                PluginSubscriptions.Add(new PluginSubscription(Mapping, mappingPlugin, subscriptionStateGuid, subscriptionOutputDeviceConfigurations));
            }
        }
    }
}
