using System.Threading;
using HidWizards.UCR.Core.Attributes;
using HidWizards.UCR.Core.Models;
using HidWizards.UCR.Core.Models.Binding;

namespace HidWizards.UCR.Plugins.Remapper
{
    [Plugin("Button to Event", Group = "Event", Description = "Remap a Button to an Event")]
    [PluginInput(DeviceBindingCategory.Momentary, "Button")]
    [PluginOutput(DeviceBindingCategory.Event, "Event")]
    public class ButtonToEvent : Plugin
    {
        public enum ButtonToEventMode { Press, Release}

        [PluginGui("Perform action on", Order = 0)]
        public ButtonToEventMode Mode { get; set; }

        private int _pressed;

        public ButtonToEvent()
        {
            Mode = ButtonToEventMode.Press;
        }

        public override void Update(params short[] values)
        {
            var pressed = values[0] != 0 ? 1 : 0;
            var previous = Interlocked.Exchange(ref _pressed, pressed);
            if (previous != pressed && (pressed != 0 ? Mode == ButtonToEventMode.Press : Mode == ButtonToEventMode.Release))
                WriteOutput(0, 1);
        }

        public override void ObserveFilteredInput(params short[] values)
        {
            Interlocked.Exchange(ref _pressed, values[0] != 0 ? 1 : 0);
        }

        public override void OnDeactivate()
        {
            Interlocked.Exchange(ref _pressed, 0);
        }
    }
}
