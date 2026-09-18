using System;
using System.Collections.Generic;

namespace HidWizards.UCR.Core.Models.Subscription
{
    public class MappingSubscription
    {
        public Profile Profile { get; }
        public Guid RuntimeScopeGuid { get; }
        public Mapping Mapping { get; }
        public List<InputSubscription> DeviceBindingSubscriptions { get; }
        public List<PluginSubscription> PluginSubscriptions { get; }
        public bool Overriden { get; set; }

        public MappingSubscription(Profile profile, Mapping mapping, Guid subscriptionStateGuid,
            Guid runtimeScopeGuid, List<DeviceConfigurationSubscription> subscriptionOutputDeviceConfigurations)
        {
            Profile = profile;
            RuntimeScopeGuid = runtimeScopeGuid;
            Mapping = mapping;
            Overriden = false;
            DeviceBindingSubscriptions = new List<InputSubscription>();
            foreach (var mappingDeviceBinding in Mapping.DeviceBindings)
            {
                if (!mappingDeviceBinding.IsBound) continue;

                var inputSubscription = new InputSubscription(mapping, mappingDeviceBinding, profile, subscriptionStateGuid);
                if (inputSubscription.DeviceSubscription != null) DeviceBindingSubscriptions.Add(inputSubscription);
            }

            PluginSubscriptions = new List<PluginSubscription>();
            foreach (var mappingPlugin in Mapping.Plugins)
            {
                PluginSubscriptions.Add(new PluginSubscription(Mapping, mappingPlugin, subscriptionStateGuid, subscriptionOutputDeviceConfigurations));
            }
        }
    }
}
