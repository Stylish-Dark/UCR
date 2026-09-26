
using System;
using HidWizards.UCR.Core.Managers;
using HidWizards.UCR.Core.Models.Binding;
using NLog;

namespace HidWizards.UCR.Core.Models.Subscription
{
    public class OutputSubscription
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly DeviceBinding.ValueChanged _outputSink;

        public DeviceBinding DeviceBinding { get; }
        public Guid SubscriptionStateGuid { get; }
        public DeviceSubscription DeviceSubscription { get; }

        public OutputSubscription(DeviceBinding deviceBinding, Guid subscriptionStateGuid, DeviceSubscription outputDeviceSubscription)
        {
            DeviceBinding = deviceBinding;
            SubscriptionStateGuid = subscriptionStateGuid;
            DeviceSubscription = outputDeviceSubscription;
            _outputSink = WriteOutput;
            deviceBinding.OutputSink = _outputSink;
        }

        public void Detach()
        {
            // Do not let an old SubscriptionState tear down a newer replacement sink.
            if (ReferenceEquals(DeviceBinding.OutputSink, _outputSink))
            {
                DeviceBinding.OutputSink = null;
            }
        }

        private void WriteOutput(short value)
        {
            try
            {
                var success = DeviceBinding.Profile.Context.IOController.SetOutputstate(
                    SubscriptionsManager.GetOutputSubscriptionRequest(SubscriptionStateGuid, DeviceSubscription),
                    SubscriptionsManager.GetBindingDescriptor(DeviceBinding),
                    (int)value);

                if (!success)
                {
                    Logger.Error("Output provider rejected write. Device={0}, Type={1}, Index={2}, SubIndex={3}, Value={4}",
                        DeviceSubscription.GetRuntimeDevice()?.LogName(),
                        DeviceBinding.KeyType,
                        DeviceBinding.KeyValue,
                        DeviceBinding.KeySubValue,
                        value);
                }
            }
            catch (Exception exception)
            {
                // Input callbacks can execute synchronously to preserve physical key order. A dead output
                // subscription must therefore be contained here rather than killing the input event path.
                Logger.Error(exception,
                    "Output write failed. Device={0}, Type={1}, Index={2}, SubIndex={3}, Value={4}",
                    DeviceSubscription.GetRuntimeDevice()?.LogName(),
                    DeviceBinding.KeyType,
                    DeviceBinding.KeyValue,
                    DeviceBinding.KeySubValue,
                    value);
            }
        }
    }
}
