using System;
using System.Collections.Generic;

namespace HidWizards.UCR.Core.Models.Subscription
{
    public class DeviceConfigurationSubscription
    {
        public DeviceConfiguration DeviceConfiguration { get; }
        public Guid RuntimeScopeGuid { get; }

        public DeviceSubscription DeviceSubscription { get; }
        public List<DeviceSubscription> ShadowDeviceSubscriptions { get; }

        public DeviceConfigurationSubscription(DeviceConfiguration deviceConfiguration)
            : this(deviceConfiguration, Guid.Empty)
        {
        }

        public DeviceConfigurationSubscription(DeviceConfiguration deviceConfiguration, Guid runtimeScopeGuid)
        {
            DeviceConfiguration = deviceConfiguration;
            RuntimeScopeGuid = runtimeScopeGuid;

            DeviceSubscription = new DeviceSubscription(deviceConfiguration.Device);

            ShadowDeviceSubscriptions = new List<DeviceSubscription>();
            deviceConfiguration.ShadowDevices.ForEach(shadowDevice => ShadowDeviceSubscriptions.Add(new DeviceSubscription(shadowDevice)));
        }
    }
}
