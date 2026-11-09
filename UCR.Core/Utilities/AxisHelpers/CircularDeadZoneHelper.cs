using System;
using System.Windows;

namespace HidWizards.UCR.Core.Utilities.AxisHelpers
{
    public class CircularDeadZoneHelper
    {
        private double _deadzoneRadius;

        public int Percentage
        {
            get => _percentage;
            set
            {
                _percentage = Math.Max(0, Math.Min(100, value));
                PrecalculateValues();
            }
        }
        private int _percentage;

        public CircularDeadZoneHelper()
        {
            PrecalculateValues();
        }

        private void PrecalculateValues()
        {
            _deadzoneRadius = (_percentage / 100d) * Constants.AxisMaxValue;
        }

        public short[] ApplyRangeDeadZone(short[] values)
        {
            var vector = new Vector(values[0], values[1]);
            var originalLength = vector.Length;
            if (_percentage == 100 || originalLength <= _deadzoneRadius)
                return new short[] { 0, 0 };
            var newLength = (originalLength - _deadzoneRadius) / (Constants.AxisMaxValue - _deadzoneRadius);
            var result = vector * (newLength / originalLength) * Constants.AxisMaxValue;

            return new[] { Functions.ClampAxisRange((int)result.X), Functions.ClampAxisRange((int)result.Y) };
        }
    }
}
