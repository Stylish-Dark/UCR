using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HidWizards.UCR.Core.Models;
using HidWizards.UCR.Core.Models.Binding;

namespace HidWizards.UCR.ViewModels.ProfileViewModels
{
    public class SimplePluginViewModel
    {
        public string Name => Plugin.PluginName;
        public string Description => Plugin.Description;
        public Plugin Plugin { get; set; }
        public string OutputType => GetOutputType();
        public int OutputTypeOrder => GetOutputTypeOrder(OutputType);
        public string MenuLabel => string.IsNullOrWhiteSpace(OutputType) ? Name : OutputType + "  ·  " + Name;

        private string GetOutputType()
        {
            if (!string.IsNullOrWhiteSpace(Plugin.GetDefinedFilterName())) return "Filter";
            if (Plugin.OutputCategories == null || Plugin.OutputCategories.Count == 0) return "Action";
            var category = Plugin.OutputCategories[0].Category;
            if (Plugin.OutputCategories.Any(definition => definition.Category != category)) return "Multiple";
            switch (category)
            {
                case DeviceBindingCategory.Momentary: return "Button";
                case DeviceBindingCategory.Range: return "Axis";
                case DeviceBindingCategory.Event: return "Event";
                case DeviceBindingCategory.Delta: return "Delta";
                default: return "Value";
            }
        }

        private static int GetOutputTypeOrder(string outputType)
        {
            switch (outputType)
            {
                case "Button": return 0;
                case "Axis": return 1;
                case "Event": return 2;
                case "Filter": return 3;
                case "Delta": return 4;
                default: return 5;
            }
        }

        public SimplePluginViewModel(Plugin plugin)
        {
            Plugin = plugin;
        }
    }
}
