using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using HidWizards.UCR.Core.Annotations;
using HidWizards.UCR.Core.Models;
using HidWizards.UCR.Core.Models.Binding;

namespace HidWizards.UCR.ViewModels.ProfileViewModels
{
    public class PluginRouteOption
    {
        public PluginItemViewModel PluginItem { get; }
        public string InputLabel { get; }
        public string OutputLabel { get; }
        public string OutputDisplayLabel { get; set; }

        public PluginRouteOption(PluginItemViewModel pluginItem, string inputLabel, string outputLabel)
        {
            PluginItem = pluginItem;
            InputLabel = inputLabel;
            OutputLabel = outputLabel;
            OutputDisplayLabel = outputLabel;
        }
    }

    public class PluginToolboxViewModel : INotifyPropertyChanged
    {
        public Dictionary<string, PluginGroupViewModel> PluginGroupList { get; set; }
        public ObservableCollection<string> InputOptions { get; }
        public ObservableCollection<PluginRouteOption> OutputOptions { get; }

        private readonly Profile _profile;
        private readonly List<PluginRouteOption> _routeOptions;
        private readonly HashSet<DeviceBindingCategory> _supportedInputCategories;

        private string _selectedInput;
        public string SelectedInput
        {
            get => _selectedInput;
            set
            {
                if (_selectedInput == value) return;
                _selectedInput = value;
                OnPropertyChanged();
                RefreshOutputOptions();
            }
        }

        private PluginRouteOption _selectedRoute;
        public PluginRouteOption SelectedRoute
        {
            get => _selectedRoute;
            set
            {
                if (_selectedRoute == value) return;
                _selectedRoute = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanAddMapping));
            }
        }

        public bool IsEnabled => _profile != null && !_profile.IsActive();
        public bool CanAddMapping => IsEnabled && SelectedRoute != null;

        public PluginToolboxViewModel(Profile profile, List<Plugin> pluginList)
        {
            _profile = profile;
            _routeOptions = new List<PluginRouteOption>();
            _supportedInputCategories = GetSupportedInputCategories();
            InputOptions = new ObservableCollection<string>();
            OutputOptions = new ObservableCollection<PluginRouteOption>();
            PluginGroupList = new Dictionary<string, PluginGroupViewModel>();

            foreach (var plugin in pluginList)
            {
                var groupName = plugin.Group ?? "Ungrouped";
                if (!PluginGroupList.ContainsKey(groupName)) PluginGroupList.Add(groupName, new PluginGroupViewModel(groupName));
                if (!PluginGroupList.TryGetValue(groupName, out var group)) continue;

                var pluginItem = new PluginItemViewModel(profile, plugin);
                group.Plugins.Add(pluginItem);
                _routeOptions.Add(new PluginRouteOption(pluginItem, GetInputLabel(plugin), GetOutputLabel(plugin)));
            }

            foreach (var pluginGroup in PluginGroupList.Values)
            {
                if (pluginGroup.Plugins.Count > 0) pluginGroup.Plugins[0].FirstElement = true;
            }

            BuildInputOptions();
            profile.Context.ActiveProfileChangedEvent += ContextOnActiveProfileChangedEvent;
        }

        private void BuildInputOptions()
        {
            var labels = new List<string>();
            foreach (var route in _routeOptions)
            {
                if (!RouteInputSupported(route)) continue;
                if (!labels.Contains(route.InputLabel)) labels.Add(route.InputLabel);
            }

            labels.Sort(CompareEndpointLabels);
            foreach (var label in labels) InputOptions.Add(label);

            if (InputOptions.Count > 0) SelectedInput = InputOptions[0];
        }

        private void RefreshOutputOptions()
        {
            OutputOptions.Clear();
            var matches = new List<PluginRouteOption>();
            foreach (var route in _routeOptions)
            {
                if (!RouteInputSupported(route)) continue;
                if (string.Equals(route.InputLabel, SelectedInput, StringComparison.Ordinal)) matches.Add(route);
            }

            matches.Sort((left, right) =>
            {
                var result = CompareEndpointLabels(left.OutputLabel, right.OutputLabel);
                return result != 0 ? result : string.Compare(left.PluginItem.Name, right.PluginItem.Name, StringComparison.OrdinalIgnoreCase);
            });

            foreach (var route in matches)
            {
                var duplicateCount = 0;
                foreach (var other in matches)
                {
                    if (string.Equals(route.OutputLabel, other.OutputLabel, StringComparison.Ordinal)) duplicateCount++;
                }

                route.OutputDisplayLabel = duplicateCount > 1
                    ? route.OutputLabel + " · " + route.PluginItem.Name
                    : route.OutputLabel;
                OutputOptions.Add(route);
            }

            SelectedRoute = OutputOptions.Count > 0 ? OutputOptions[0] : null;
            OnPropertyChanged(nameof(OutputOptions));
        }

        private bool RouteInputSupported(PluginRouteOption route)
        {
            if (route?.PluginItem?.Plugin == null) return false;
            if (_supportedInputCategories.Count == 0) return true;

            var definitions = route.PluginItem.Plugin.InputCategories;
            if (definitions == null || definitions.Count == 0) return true;
            foreach (var definition in definitions)
            {
                if (!_supportedInputCategories.Contains(definition.Category)) return false;
            }
            return true;
        }

        private HashSet<DeviceBindingCategory> GetSupportedInputCategories()
        {
            var result = new HashSet<DeviceBindingCategory>();
            if (_profile == null) return result;

            foreach (var configuration in _profile.GetDeviceConfigurationList(DeviceIoType.Input))
            {
                var device = configuration?.Device;
                if (device == null) continue;
                CollectCategories(device.GetDeviceBindingMenu(_profile.Context, DeviceIoType.Input), result);
            }
            return result;
        }

        private static void CollectCategories(IEnumerable<DeviceBindingNode> nodes, ISet<DeviceBindingCategory> result)
        {
            if (nodes == null || result == null) return;
            foreach (var node in nodes)
            {
                if (node?.DeviceBindingInfo != null) result.Add(node.DeviceBindingInfo.DeviceBindingCategory);
                if (node?.ChildrenNodes != null) CollectCategories(node.ChildrenNodes, result);
            }
        }

        private static string GetInputLabel(Plugin plugin)
        {
            return GetEndpointLabel(plugin.InputCategories, true, plugin);
        }

        private static string GetOutputLabel(Plugin plugin)
        {
            return GetEndpointLabel(plugin.OutputCategories, false, plugin);
        }

        private static string GetEndpointLabel(List<Plugin.IODefinition> definitions, bool isInput, Plugin plugin)
        {
            if (definitions == null || definitions.Count == 0)
            {
                if (!isInput && string.Equals(plugin.Group, "Filter", StringComparison.OrdinalIgnoreCase)) return "Filter";
                return "None";
            }

            var category = definitions[0].Category;
            for (var i = 1; i < definitions.Count; i++)
            {
                if (definitions[i].Category != category) return "Multiple";
            }

            return CategoryLabel(category);
        }

        private static string CategoryLabel(DeviceBindingCategory category)
        {
            switch (category)
            {
                case DeviceBindingCategory.Momentary: return "Button";
                case DeviceBindingCategory.Range: return "Axis";
                case DeviceBindingCategory.Event: return "Event";
                case DeviceBindingCategory.Delta: return "Delta";
                default: return "Value";
            }
        }

        private static int CompareEndpointLabels(string left, string right)
        {
            var leftOrder = EndpointOrder(left);
            var rightOrder = EndpointOrder(right);
            if (leftOrder != rightOrder) return leftOrder.CompareTo(rightOrder);
            return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
        }

        private static int EndpointOrder(string label)
        {
            switch (label)
            {
                case "Button": return 0;
                case "Axis": return 1;
                case "Event": return 2;
                case "Filter": return 3;
                case "Delta": return 4;
                case "Multiple": return 5;
                case "Value": return 6;
                case "None": return 7;
                default: return 8;
            }
        }

        private void ContextOnActiveProfileChangedEvent(Profile profile)
        {
            OnPropertyChanged(nameof(IsEnabled));
            OnPropertyChanged(nameof(CanAddMapping));
        }

        public event PropertyChangedEventHandler PropertyChanged;

        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
