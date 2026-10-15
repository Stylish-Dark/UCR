using HidWizards.UCR.Core.Attributes;
using HidWizards.UCR.Core.Models;
using HidWizards.UCR.Core.Models.Binding;

namespace HidWizards.UCR.Plugins.Remapper
{
    // Four digital input directions become one logical D-pad destination.
    // The UI resolves the target's four POV buttons as a single choice.
    [Plugin("Buttons to DPad", Group = "Axis", Description = "Control a virtual controller D-pad with four directions")]
    [PluginInput(DeviceBindingCategory.Momentary, "Up")]
    [PluginInput(DeviceBindingCategory.Momentary, "Down")]
    [PluginInput(DeviceBindingCategory.Momentary, "Left")]
    [PluginInput(DeviceBindingCategory.Momentary, "Right")]
    [PluginOutput(DeviceBindingCategory.Momentary, "Up")]
    [PluginOutput(DeviceBindingCategory.Momentary, "Down")]
    [PluginOutput(DeviceBindingCategory.Momentary, "Left")]
    [PluginOutput(DeviceBindingCategory.Momentary, "Right")]
    public class ButtonsToDPad : Plugin
    {
        public bool InvertX { get; set; }
        public bool InvertY { get; set; }

        public override void Update(params short[] values)
        {
            if (values == null || values.Length < 4) return;
            WriteOutput(0, (short)((InvertY ? values[1] : values[0]) != 0 ? 1 : 0));
            WriteOutput(1, (short)((InvertY ? values[0] : values[1]) != 0 ? 1 : 0));
            WriteOutput(2, (short)((InvertX ? values[3] : values[2]) != 0 ? 1 : 0));
            WriteOutput(3, (short)((InvertX ? values[2] : values[3]) != 0 ? 1 : 0));
        }
    }
}
