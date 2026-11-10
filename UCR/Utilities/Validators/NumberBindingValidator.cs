using System;
using System.Globalization;
using System.Windows.Controls;
using HidWizards.UCR.Views.Controls.Plugin;

namespace HidWizards.UCR.Utilities.Validators
{
    public class NumberBindingValidator : ValidationRule
    {
        public PluginPropertyDependencyObject PluginPropertyDependencyObject { get; set; }

        public override ValidationResult Validate(object value, CultureInfo cultureInfo)
        {
            var text = value as string;
            if (string.IsNullOrWhiteSpace(text)) return new ValidationResult(false, "Enter a whole number.");

            var property = PluginPropertyDependencyObject?.PluginProperty;
            if (property == null) return new ValidationResult(false, "The setting is unavailable.");
            object number;
            try
            {
                // Match the type and culture used by PluginProperty when saving the value.
                number = Convert.ChangeType(text, property.PropertyInfo.PropertyType, CultureInfo.InvariantCulture);
            }
            catch (FormatException)
            {
                return new ValidationResult(false, "Enter a whole number.");
            }
            catch (OverflowException)
            {
                return new ValidationResult(false, "The number is outside the supported range.");
            }

            var propertyValidationResult = property.Validate(number);
            return propertyValidationResult.IsValid
                ? ValidationResult.ValidResult
                : new ValidationResult(false, propertyValidationResult.ErrorMessage);
        }
    }
}
