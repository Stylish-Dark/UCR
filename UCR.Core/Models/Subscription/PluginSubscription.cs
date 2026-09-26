using System;
using System.Collections.Generic;
using System.Linq;

namespace HidWizards.UCR.Core.Models.Subscription
{
    public class PluginSubscription
    {
        public Plugin Plugin { get; set; }
        public Guid SubscriptionStateGuid { get; set; }
        public List<OutputSubscription> OutputSubscriptions { get; set; }

        public PluginSubscription(Mapping mapping, Plugin plugin, Guid subscriptionStateGuid, List<DeviceConfigurationSubscription> outputDeviceConfigurations)
        {
            Plugin = plugin;
            SubscriptionStateGuid = subscriptionStateGuid;
            OutputSubscriptions = new List<OutputSubscription>();

            foreach (var deviceBinding in Plugin.Outputs)
            {
                // OutputSink is runtime-only. A profile rebuild must never leave a binding pointing at
                // the previous state's DeviceSubscription if this output can no longer be resolved.
                deviceBinding.OutputSink = null;
                if (!deviceBinding.IsBound) continue;
                
                var deviceConfigurationSubscription = outputDeviceConfigurations.FirstOrDefault(configuration => configuration.DeviceConfiguration.Guid == deviceBinding.DeviceConfigurationGuid);
                if (deviceConfigurationSubscription == null) continue;

                var deviceConfiguration = mapping.IsShadowMapping
                    ? deviceConfigurationSubscription.ShadowDeviceSubscriptions[mapping.ShadowDeviceNumber]
                    : deviceConfigurationSubscription.DeviceSubscription;

                OutputSubscriptions.Add(new OutputSubscription(deviceBinding, subscriptionStateGuid, deviceConfiguration));
            }
        }

        public void DetachOutputs()
        {
            foreach (var outputSubscription in OutputSubscriptions)
            {
                outputSubscription.Detach();
            }
            OutputSubscriptions.Clear();
        }
    }
}
