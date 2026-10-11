using HidWizards.UCR.Core.Attributes;
using HidWizards.UCR.Core.Utilities;
using HidWizards.UCR.Core.Models;
using HidWizards.UCR.Core.Models.Binding;

namespace HidWizards.UCR.Plugins.Remapper
{
    [Plugin("Buttons to Axes", Group = "Axis", Description = "Control a two-axis joystick using four directional buttons")]
    [PluginInput(DeviceBindingCategory.Momentary, "Up")]
    [PluginInput(DeviceBindingCategory.Momentary, "Down")]
    [PluginInput(DeviceBindingCategory.Momentary, "Left")]
    [PluginInput(DeviceBindingCategory.Momentary, "Right")]
    [PluginOutput(DeviceBindingCategory.Range, "X Axis", Group = "X axis")]
    [PluginOutput(DeviceBindingCategory.Range, "Y Axis", Group = "Y axis")]
    public class ButtonsToAxes : Plugin
    {
        [PluginGui("Invert X", Group = "X axis")]
        public bool InvertX { get; set; }

        [PluginGui("Invert Y", Group = "Y axis")]
        public bool InvertY { get; set; }

        public static short DirectionValue(bool negativePressed, bool positivePressed)
        {
            if (negativePressed == positivePressed) return 0;
            return (short)(negativePressed ? Constants.AxisMinValue : Constants.AxisMaxValue);
        }

        public override void Update(params short[] values)
        {
            // Opposite directions cancel, including when neither is held.
            var x = DirectionValue(values[2] != 0, values[3] != 0);
            var y = DirectionValue(values[0] != 0, values[1] != 0);
            WriteOutput(0, InvertX ? Functions.Invert(x) : x);
            WriteOutput(1, InvertY ? Functions.Invert(y) : y);
        }
    }
}
