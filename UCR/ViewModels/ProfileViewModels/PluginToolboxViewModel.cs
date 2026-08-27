using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
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
        public string VariantLabel { get; set; }

        public PluginRouteOption(PluginItemViewModel pluginItem, string inputLabel, string outputLabel)
        {
            PluginItem = pluginItem;
            InputLabel = inputLabel;
            OutputLabel = outputLabel;
            VariantLabel = pluginItem?.Name ?? outputLabel;
        }
    }

    public sealed class PluginOutputOption
    {
        public string OutputLabel { get; set; }
        public List<PluginRouteOption> Routes { get; set; }
    }

    public class PluginToolboxViewModel : INotifyPropertyChanged, IDisposable
    {
        public Dictionary<string, PluginGroupViewModel> PluginGroupList { get; set; }
        public ObservableCollection<string> InputOptions { get; }
        public ObservableCollection<PluginOutputOption> OutputOptions { get; }
        public ObservableCollection<PluginRouteOption> VariantOptions { get; }

        private readonly Profile _profile;
        private readonly List<PluginRouteOption> _routeOptions;
        private readonly HashSet<DeviceBindingCategory> _supportedInputCategories;
        private bool _disposed;

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

        private PluginOutputOption _selectedOutput;
        public PluginOutputOption SelectedOutput
        {
            get => _selectedOutput;
            set
            {
                if (_selectedOutput == value) return;
                _selectedOutput = value;
                OnPropertyChanged();
                RefreshVariantOptions();
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
                OnPropertyChanged(nameof(SelectedPluginName));
                OnPropertyChanged(nameof(SelectedPluginDescription));
            }
        }

        public bool HasRouteVariants => VariantOptions.Count > 1;
        public string SelectedPluginName => SelectedRoute?.PluginItem?.Name ?? string.Empty;
        public string SelectedPluginDescription => SelectedRoute?.PluginItem?.Description ?? string.Empty;
        public bool IsEnabled => _profile != null && !_profile.IsActive();
        public bool CanAddMapping => IsEnabled && SelectedRoute != null;

        public PluginToolboxViewModel(Profile profile, List<Plugin> pluginList)
        {
            _profile = profile;
            _routeOptions = new List<PluginRouteOption>();
            _supportedInputCategories = GetSupportedInputCategories();
            InputOptions = new ObservableCollection<string>();
            OutputOptions = new ObservableCollection<PluginOutputOption>();
            VariantOptions = new ObservableCollection<PluginRouteOption>();
            PluginGroupList = new Dictionary<string, PluginGroupViewModel>();

            foreach (var plugin in pluginList)
            {
                var groupName = plugin.Group ?? "Ungrouped";
                if (!PluginGroupList.ContainsKey(groupName)) PluginGroupList.Add(groupName, new PluginGroupViewModel(groupName));
                if (!PluginGroupList.TryGetValue(groupName, out var group)) continue;

                var pluginItem = new PluginItemViewModel(profile, plugin);
                group.Plugins.Add(pluginItem);
                var route = new PluginRouteOption(pluginItem, GetInputLabel(plugin), GetOutputLabel(plugin));
                route.VariantLabel = BuildVariantLabel(plugin, route);
                _routeOptions.Add(route);
            }

            foreach (var pluginGroup in PluginGroupList.Values)
            {
                if (pluginGroup.Plugins.Count > 0) pluginGroup.Plugins[0].FirstElement = true;
            }

            BuildInputOptions();
            profile.Context.ActiveProfileChangedEvent += ContextOnActiveProfileChangedEvent;
        }

        public void RefreshDeviceCapabilities()
        {
            if (_disposed) return;
            _supportedInputCategories.Clear();
            foreach (var category in GetSupportedInputCategories()) _supportedInputCategories.Add(category);

            var previousInput = SelectedInput;
            InputOptions.Clear();
            var labels = _routeOptions.Where(RouteInputSupported)
                .Select(route => route.InputLabel)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(label => label, Comparer<string>.Create(CompareEndpointLabels))
                .ToList();
            foreach (var label in labels) InputOptions.Add(label);

            if (!string.IsNullOrWhiteSpace(previousInput) && InputOptions.Contains(previousInput))
                SelectedInput = previousInput;
            else
                SelectedInput = InputOptions.Count > 0 ? InputOptions[0] : null;

            RefreshOutputOptions();
            OnPropertyChanged(nameof(InputOptions));
            OnPropertyChanged(nameof(IsEnabled));
            OnPropertyChanged(nameof(CanAddMapping));
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
            VariantOptions.Clear();
            SelectedRoute = null;

            var matches = _routeOptions
                .Where(RouteInputSupported)
                .Where(route => string.Equals(route.InputLabel, SelectedInput, StringComparison.Ordinal))
                .OrderBy(route => EndpointOrder(route.OutputLabel))
                .ThenBy(route => route.OutputLabel, StringComparer.OrdinalIgnoreCase)
                .ThenBy(route => route.PluginItem.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var group in matches.GroupBy(route => route.OutputLabel, StringComparer.OrdinalIgnoreCase))
            {
                OutputOptions.Add(new PluginOutputOption
                {
                    OutputLabel = group.Key,
                    Routes = group.ToList()
                });
            }

            SelectedOutput = OutputOptions.Count > 0 ? OutputOptions[0] : null;
            OnPropertyChanged(nameof(OutputOptions));
        }

        private void RefreshVariantOptions()
        {
            VariantOptions.Clear();
            if (SelectedOutput?.Routes != null)
            {
                foreach (var route in SelectedOutput.Routes) VariantOptions.Add(route);
            }

            SelectedRoute = VariantOptions.Count > 0 ? VariantOptions[0] : null;
            OnPropertyChanged(nameof(VariantOptions));
            OnPropertyChanged(nameof(HasRouteVariants));
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

        private static string BuildVariantLabel(Plugin plugin, PluginRouteOption route)
        {
            if (plugin == null || route == null) return string.Empty;

            if (string.Equals(route.OutputLabel, "Axis", StringComparison.OrdinalIgnoreCase) &&
                plugin.InputCategories != null && plugin.InputCategories.Count > 0 &&
                plugin.InputCategories.All(definition => definition.Category == DeviceBindingCategory.Momentary))
            {
                return plugin.InputCategories.Count == 1 ? "1 button" : plugin.InputCategories.Count + " buttons";
            }

            return plugin.PluginName;
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


        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_profile != null) _profile.Context.ActiveProfileChangedEvent -= ContextOnActiveProfileChangedEvent;

            foreach (var group in PluginGroupList?.Values ?? Enumerable.Empty<PluginGroupViewModel>())
            {
                foreach (var plugin in group.Plugins) plugin.Dispose();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
