using System;

namespace HidWizards.UCR.Core.Utilities.AxisHelpers
{
    public class DeadZoneHelper
    {
        private double _scaleFactor;
        private double _deadzoneCutoff;
        
        public int Percentage
        {
            get => _percentage;
            set
            {
                if (value < 0)
                {
                    _percentage = 0;
                }
                else if (value > 100)
                {
                    _percentage = 100;
                }
                else
                {
                    _percentage = value;
                }
                
                PrecalculateValues();
            }
        }
        private int _percentage;

        public DeadZoneHelper()
        {
            PrecalculateValues();
        }

        private void PrecalculateValues()
        {
            _deadzoneCutoff = Constants.AxisMaxValue * (_percentage / 100d);
            _scaleFactor = _percentage == 100
                ? 0 : Constants.AxisMaxValue / (Constants.AxisMaxValue - _deadzoneCutoff);
        }

        public short ApplyRangeDeadZone(short value)
        {
            if (_percentage == 100) return 0;

            var wideVal = Functions.WideAbs(value);
            if (wideVal <= _deadzoneCutoff) return 0;

            var adjustedValue = (wideVal - _deadzoneCutoff) * _scaleFactor;
            return Functions.ClampAxisRange((int)Math.Round(adjustedValue * Math.Sign(value)));
        }
    }
}
