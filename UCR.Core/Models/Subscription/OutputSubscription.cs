
using System;
using System.Threading;
using HidWizards.UCR.Core.Managers;
using HidWizards.UCR.Core.Models.Binding;
using NLog;

namespace HidWizards.UCR.Core.Models.Subscription
{
    public class OutputSubscription
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly DeviceBinding.ValueChanged _outputSink;
        private int _writeFailed;

        internal bool WriteFailed => Volatile.Read(ref _writeFailed) != 0;

        public DeviceBinding DeviceBinding { get; }
        public Guid SubscriptionStateGuid { get; }
        public DeviceSubscription DeviceSubscription { get; }

        public OutputSubscription(DeviceBinding deviceBinding, Guid subscriptionStateGuid, DeviceSubscription outputDeviceSubscription)
        {
            DeviceBinding = deviceBinding;
            SubscriptionStateGuid = subscriptionStateGuid;
            DeviceSubscription = outputDeviceSubscription;
            _outputSink = WriteOutput;
            // Candidate states are prepared while the previous profile may still be active.
            // Do not steal its output callback until this state's devices are subscribed.
        }

        public void Attach()
        {
            DeviceBinding.OutputSink = _outputSink;
        }

        public void Detach()
        {
            // Do not let an old SubscriptionState tear down a newer replacement sink.
            if (ReferenceEquals(DeviceBinding.OutputSink, _outputSink))
            {
                DeviceBinding.OutputSink = null;
            }
        }

        private void SetWriteHealth(bool success)
        {
            var failed = success ? 0 : 1;
            if (Interlocked.Exchange(ref _writeFailed, failed) != failed)
                DeviceBinding.Profile?.Context?.SubscriptionsManager?.NotifyOutputWriteHealthChanged();
        }

        private void WriteOutput(short value)
        {
            try
            {
                var success = DeviceBinding.Profile.Context.IOController.SetOutputstate(
                    SubscriptionsManager.GetOutputSubscriptionRequest(SubscriptionStateGuid, DeviceSubscription),
                    SubscriptionsManager.GetBindingDescriptor(DeviceBinding),
                    (int)value);

                SetWriteHealth(success);
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
                SetWriteHealth(false);
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
