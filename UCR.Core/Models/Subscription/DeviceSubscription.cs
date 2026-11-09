using System;
using HidWizards.IOWrapper.DataTransferObjects;

namespace HidWizards.UCR.Core.Models.Subscription
{
    public class DeviceSubscription
    {
        public Guid DeviceSubscriptionGuid { get; }
        public Device Device { get; }
        public Device ResolvedDevice { get; set; }
        internal OutputSubscriptionRequest SubscribedOutputRequest { get; set; }

        public DeviceSubscription(Device device)
        {
            DeviceSubscriptionGuid = Guid.NewGuid();
            Device = device;
            ResolvedDevice = null;
        }

        public Device GetRuntimeDevice()
        {
            return ResolvedDevice ?? Device;
        }
    }
}
