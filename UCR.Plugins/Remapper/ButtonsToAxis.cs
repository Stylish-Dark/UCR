using HidWizards.UCR.Core.Attributes;
using HidWizards.UCR.Core.Models;
using HidWizards.UCR.Core.Models.Binding;
using HidWizards.UCR.Core.Utilities;

namespace HidWizards.UCR.Plugins.Remapper
{
    [Plugin("Buttons to Axis", Group = "Axis", Description = "Map two buttons to one axis")]
    [PluginInput(DeviceBindingCategory.Momentary, "Positive (+)")]
    [PluginInput(DeviceBindingCategory.Momentary, "Negative (−)")]
    [PluginOutput(DeviceBindingCategory.Range, "Axis")]
    public class ButtonsToAxis : Plugin
    {
        [PluginGui("Invert")]
        public bool Invert { get; set; }

        public override void Update(params short[] values)
        {
            var positive = values[0] != 0;
            var negative = values[1] != 0;
            var value = positive == negative ? (short)0 :
                positive ? short.MaxValue : short.MinValue;

            if (Invert) value = Functions.Invert(value);
            WriteOutput(0, value);
        }

    }
}
