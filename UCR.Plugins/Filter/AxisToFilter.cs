using System.Reflection;
using HidWizards.UCR.Core.Attributes;
using HidWizards.UCR.Core.Models;
using HidWizards.UCR.Core.Models.Binding;
using HidWizards.UCR.Core.Utilities;

namespace HidWizards.UCR.Plugins.Filter
{
    [Plugin("Axis to Filter", Group = "Filter", Description = "Change filter state using an axis")]
    [PluginInput(DeviceBindingCategory.Range, "Axis")]
    public class AxisToFilter : Plugin
    {
        [PluginGui("Filter name")]
        public string FilterName { get; set; }

        [PluginGui("Lower bound")]
        public double RangeLowerBound { get; set; }
        [PluginGui("Upper bound")]
        public double RangeUpperBound { get; set; }

        [PluginGui("Filter state entering range")]
        public FilterMode FilterStateEntering { get; set; }

        [PluginGui("Filter state exiting range")]
        public FilterMode FilterStateExiting { get; set; }

        private readonly object _transitionLock = new object();
        private bool _active;
        private bool? _wasInside;


        public AxisToFilter()
        {
            FilterStateExiting = FilterMode.Active;
            FilterStateEntering = FilterMode.Inactive;
            RangeLowerBound = -50;
            RangeUpperBound = 50;
        }

        public override bool InitializeFromAxisSnapshot => true;

        public override void OnActivate()
        {
            lock (_transitionLock)
            {
                _active = true;
                _wasInside = null;
                // An input provider may already have reported a value while subscribing.
                if (!IsFiltered() && TryReadPrimaryInput(out var value))
                    ApplyInput(value);
            }
        }

        public override void OnDeactivate()
        {
            lock (_transitionLock) _active = false;
        }

        public override void Update(params short[] values)
        {
            lock (_transitionLock)
            {
                if (_active) ApplyInput(values[0]);
            }
        }

        private void ApplyInput(short value)
        {
            var isInside = IsWithinBounds(value);
            if (_wasInside == isInside) return;
            _wasInside = isInside;
            ChangeState(isInside ? FilterStateEntering : FilterStateExiting);
        }

        private bool IsWithinBounds(short value)
        {
            return value >= Functions.GetRangeFromPercentage(RangeLowerBound) && value <= Functions.GetRangeFromPercentage(RangeUpperBound);
        }

        private void ChangeState(FilterMode filterState)
        {
            switch (filterState)
            {
                case FilterMode.Active:
                    WriteFilterState(FilterName, true);
                    break;
                case FilterMode.Inactive:
                    WriteFilterState(FilterName, false);
                    break;
                case FilterMode.Toggle:
                    ToggleFilterState(FilterName);
                    break;
                case FilterMode.Unchanged:
                    break;
            }
        }

        public override PropertyValidationResult Validate(PropertyInfo propertyInfo, dynamic value)
        {
            PropertyValidationResult validation;
            switch (propertyInfo.Name)
            {
                case nameof(RangeLowerBound):
                    validation = InputValidation.ValidateRange(value, -100, 100);
                    if (!validation.IsValid) return validation;
                    
                    return InputValidation.ValidateRange(value, -100, RangeUpperBound);
                case nameof(RangeUpperBound):
                    validation = InputValidation.ValidateRange(value, -100, 100);
                    if (!validation.IsValid) return validation;

                    return InputValidation.ValidateRange(value, RangeLowerBound, 100);
            }

            return PropertyValidationResult.ValidResult;
        }
    }
}
