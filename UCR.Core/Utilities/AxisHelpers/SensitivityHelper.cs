using System;

namespace HidWizards.UCR.Core.Utilities.AxisHelpers
{
    public class SensitivityHelper
    {
        private double _scaleFactor;

        public int Percentage
        {
            get => _percentage;
            set
            {
                // Do NOT throw if percentage is not in range 0..100, other values are valid!
                _percentage = value;
                PrecalculateValues();
            }
        }

        private int _percentage;

        public bool IsLinear { get; set; }

        public SensitivityHelper()
        {
            PrecalculateValues();
        }

        private void PrecalculateValues()
        {
            _scaleFactor = _percentage / 100d;
        }

        public short ApplyRangeSensitivity(short value)
        {
            if (IsLinear)
            {
                var scaled = value * _scaleFactor;
                return (short)Math.Round(Math.Max(Constants.AxisMinValue,
                    Math.Min(Constants.AxisMaxValue, scaled)));
            }

            // Normalize each half of the axis separately to keep zero neutral.
            // At 100% the linear term is exactly the input and the cubic term is zero.
            var endpoint = value < 0 ? Constants.AxisMinValue : Constants.AxisMaxValue;
            var magnitude = (double)value / endpoint;
            var shaped = magnitude * (_scaleFactor + (1d - _scaleFactor) * magnitude * magnitude);
            return (short)Math.Round(Math.Max(0d, Math.Min(1d, shaped)) * endpoint);
        }

    }
}
