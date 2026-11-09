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
            WriteDirectionPair(0, values[InvertY ? 1 : 0] != 0, values[InvertY ? 0 : 1] != 0);
            WriteDirectionPair(2, values[InvertX ? 3 : 2] != 0, values[InvertX ? 2 : 3] != 0);
        }

        private void WriteDirectionPair(int negativeOutput, bool negativePressed, bool positivePressed)
        {
            // Release the opposite direction first so a switch cannot emit both at once.
            if (positivePressed && !negativePressed)
            {
                WriteOutput(negativeOutput, 0);
                WriteOutput(negativeOutput + 1, 1);
                return;
            }

            WriteOutput(negativeOutput + 1, 0);
            WriteOutput(negativeOutput, (short)(negativePressed && !positivePressed ? 1 : 0));
        }
    }
}
