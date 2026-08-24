using System;
using System.CodeDom;
using System.Globalization;
using System.Reflection;

namespace HidWizards.UCR.Core.Models
{
    public class PluginProperty : IComparable<PluginProperty>
    {
        public string Name { get; }
        public Plugin Plugin { get; }
        public int Order { get; }
        public string Group { get; }

        public PropertyInfo PropertyInfo { get; }
        public dynamic Property
        {
            get => PropertyInfo.GetValue(Plugin);
            set
            {
                var previousValue = PropertyInfo.GetValue(Plugin);
                if (Equals(value, previousValue)) return;

                var convertedValue = Convert.ChangeType(value, PropertyInfo.PropertyType, CultureInfo.InvariantCulture);
                PropertyInfo.SetValue(Plugin, convertedValue);

                if (string.Equals(PropertyInfo.Name, "FilterName", StringComparison.Ordinal) &&
                    string.Equals(Plugin.Group, "Filter", StringComparison.OrdinalIgnoreCase))
                {
                    var oldName = previousValue as string;
                    var newName = convertedValue as string;
                    if (!string.IsNullOrWhiteSpace(oldName)) Plugin.Profile.RenameFilterReferences(oldName, newName);
                    Plugin.OnFilterDefinitionChanged(oldName, newName);
                }

                if (Plugin.Profile.IsActive())
                {
                    Plugin.InitializeCacheValues();
                    Plugin.OnPropertyChanged();
                }
                Plugin.ContextChanged();
            }
        }

        public PluginProperty(Plugin plugin, PropertyInfo propertyInfo, string name, int order = 0, string group = null)
        {
            Plugin = plugin;
            PropertyInfo = propertyInfo;
            Name = name;
            Order = order;
            Group = group;
        }

        public int CompareTo(PluginProperty other)
        {
            return Order.CompareTo(other.Order);
        }

        public PropertyValidationResult Validate(dynamic value)
        {
            return Plugin.Validate(PropertyInfo, value);
        }
    }
}
