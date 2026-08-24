using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace HidWizards.UCR.Utilities.Converter
{
    [ValueConversion(typeof(string), typeof(Brush))]
    public class MappingTypeBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var label = value as string;
            var resourceKey = GetResourceKey(label);
            if (resourceKey != null)
            {
                var resource = Application.Current?.TryFindResource(resourceKey) as Brush;
                if (resource != null) return resource;
            }

            return Application.Current?.TryFindResource("UcrSecondaryTextBrush") as Brush ?? Brushes.Gainsboro;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }

        private static string GetResourceKey(string label)
        {
            if (string.IsNullOrWhiteSpace(label)) return null;

            var normalized = label.Trim();
            if (normalized.StartsWith("Button", StringComparison.OrdinalIgnoreCase)) return "MappingTypeButtonBrush";
            if (normalized.StartsWith("Axis", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("Axes", StringComparison.OrdinalIgnoreCase)) return "MappingTypeAxisBrush";
            if (normalized.StartsWith("Filter", StringComparison.OrdinalIgnoreCase)) return "MappingTypeFilterBrush";
            if (normalized.StartsWith("Event", StringComparison.OrdinalIgnoreCase)) return "MappingTypeEventBrush";
            if (normalized.StartsWith("Delta", StringComparison.OrdinalIgnoreCase)) return "MappingTypeDeltaBrush";
            if (normalized.StartsWith("Value", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("Multiple", StringComparison.OrdinalIgnoreCase)) return "MappingTypeValueBrush";
            if (normalized.StartsWith("None", StringComparison.OrdinalIgnoreCase)) return "MappingTypeNoneBrush";
            return null;
        }
    }
}
