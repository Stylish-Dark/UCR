using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using HidWizards.UCR.Core.Annotations;
using HidWizards.UCR.Core.Models;
using HidWizards.UCR.Core.Utilities;
using HidWizards.UCR.Views.Dialogs;
using HidWizards.UCR.ViewModels.Presentation;
using MaterialDesignThemes.Wpf;

namespace HidWizards.UCR.ViewModels.ProfileViewModels
{
    public sealed class MappingHeaderToken
    {
        public string Text { get; }
        public string TypeLabel { get; }

        public MappingHeaderToken(string text, string typeLabel)
        {
            Text = text;
            TypeLabel = typeLabel;
        }
    }

    public class MappingViewModel : INotifyPropertyChanged
    {
        public string MappingTitle => Mapping.FullTitle;
        public ProfileViewModel ProfileViewModel { get; }
        public Mapping Mapping { get; set; }
        public ObservableCollection<PluginViewModel> Plugins { get; set; }
        public ObservableCollection<DeviceBindingViewModel> DeviceBindings { get; set; }
        public bool ButtonsEnabled => !ProfileViewModel.Profile.IsActive();
        public bool CanMoveUp => ButtonsEnabled && ProfileViewModel.MappingsList != null && ProfileViewModel.MappingsList.IndexOf(this) > 0;
        public bool CanMoveDown => ButtonsEnabled && ProfileViewModel.MappingsList != null &&
                                   ProfileViewModel.MappingsList.IndexOf(this) >= 0 &&
                                   ProfileViewModel.MappingsList.IndexOf(this) < ProfileViewModel.MappingsList.Count - 1;
        public string MappingRoute => Mapping != null && Mapping.Plugins.Count > 0 ? Mapping.Plugins[0].PluginName : "No plugin";
        public string MappingRouteDisplay => FormatMappingRoute(MappingRoute);
        public List<MappingHeaderToken> MappingRouteTokens => BuildMappingRouteTokens(MappingRouteDisplay);
        public bool HasFilters => Mapping != null && Mapping.Plugins != null &&
                                  Mapping.Plugins.Any(plugin => plugin.Filters != null && plugin.Filters.Count > 0);
        public string CollapsedSummary => BuildCollapsedSummary();
        public List<BindingVisualDescriptor> CollapsedInputVisuals => BuildCollapsedInputVisuals();
        public List<BindingVisualDescriptor> CollapsedOutputVisuals => BuildCollapsedOutputVisuals();

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded == value) return;
                _isExpanded = value;
                OnPropertyChanged();
            }
        }

        public MappingViewModel(ProfileViewModel profileViewModel, Mapping mapping)
        {
            ProfileViewModel = profileViewModel;
            Mapping = mapping;
            IsExpanded = false;
            profileViewModel.Profile.Context.ActiveProfileChangedEvent += ContextOnActiveProfileChangedEvent;
            DeviceBindings = new ObservableCollection<DeviceBindingViewModel>();
            PopulateDeviceBindingsViewModels();
            PopulatePlugins(mapping);
            SubscribeSummaryBindings();
        }

        private void ContextOnActiveProfileChangedEvent(Profile profile)
        {
            OnPropertyChanged(nameof(ButtonsEnabled));
            OnPropertyChanged(nameof(CanMoveUp));
            OnPropertyChanged(nameof(CanMoveDown));
        }

        public void RefreshPositionState()
        {
            OnPropertyChanged(nameof(CanMoveUp));
            OnPropertyChanged(nameof(CanMoveDown));
        }

        public void RefreshCollapsedSummary()
        {
            OnPropertyChanged(nameof(CollapsedSummary));
            OnPropertyChanged(nameof(CollapsedInputVisuals));
            OnPropertyChanged(nameof(CollapsedOutputVisuals));
        }

        public void RefreshTitle()
        {
            OnPropertyChanged(nameof(MappingTitle));
        }

        private void SubscribeSummaryBindings()
        {
            foreach (var binding in DeviceBindings) SubscribeSummaryBinding(binding);
            foreach (var plugin in Plugins)
            {
                foreach (var binding in plugin.DeviceBindings) SubscribeSummaryBinding(binding);
            }
        }

        private void SubscribeSummaryBinding(DeviceBindingViewModel binding)
        {
            if (binding == null) return;
            binding.PropertyChanged -= SummaryBindingOnPropertyChanged;
            binding.PropertyChanged += SummaryBindingOnPropertyChanged;
        }

        private void SummaryBindingOnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            RefreshCollapsedSummary();
        }

        public void MoveUp()
        {
            ProfileViewModel.MoveMapping(this, -1);
        }

        public void MoveDown()
        {
            ProfileViewModel.MoveMapping(this, 1);
        }

        public void Remove()
        {
            ProfileViewModel.RemoveMapping(this);
        }

        public void AddPlugin(Plugin plugin)
        {
            var newPlugin = ProfileViewModel.Profile.Context.PluginManager.GetNewPlugin(plugin);
            if (!Mapping.AddPlugin(newPlugin)) return;

            var pluginViewModel = new PluginViewModel(this, newPlugin);
            Plugins.Add(pluginViewModel);
            foreach (var binding in pluginViewModel.DeviceBindings) SubscribeSummaryBinding(binding);
            RefreshHeaderState();
            RefreshCollapsedSummary();
            if (Plugins.Count != 1) return;
            
            PopulateDeviceBindingsViewModels();
            foreach (var binding in DeviceBindings) SubscribeSummaryBinding(binding);
            RefreshCollapsedSummary();
        }

        private void PopulateDeviceBindingsViewModels()
        {
            if (Mapping.Plugins.Count == 0) return;

            var plugin = Mapping.Plugins[0];
            for (var i = 0; i < plugin.InputCategories.Count; i++)
            {
                DeviceBindings.Add(new DeviceBindingViewModel(Mapping.DeviceBindings[i])
                {
                    DeviceBindingName = plugin.InputCategories[i].Name,
                    DeviceBindingCategory = plugin.InputCategories[i].Category
                });
            }
        }

        public void RemovePlugin(PluginViewModel pluginViewModel)
        {
            if (!Mapping.RemovePlugin(pluginViewModel.Plugin)) return;

            Plugins.Remove(pluginViewModel);
            RefreshHeaderState();
            RefreshCollapsedSummary();
            ProfileViewModel.RefreshFilterNames();
            ProfileViewModel.RefreshFilterReferenceLabels();
            if (Plugins.Count == 0) DeviceBindings.Clear();
        }

        private void PopulatePlugins(Mapping mapping)
        {
            Plugins = new ObservableCollection<PluginViewModel>();
            foreach (var mappingPlugin in mapping.Plugins)
            {
                Plugins.Add(new PluginViewModel(this, mappingPlugin));
            }
        }

        public void RefreshFilterIndicator()
        {
            OnPropertyChanged(nameof(HasFilters));
        }

        private void RefreshHeaderState()
        {
            OnPropertyChanged(nameof(MappingRoute));
            OnPropertyChanged(nameof(MappingRouteDisplay));
            OnPropertyChanged(nameof(MappingRouteTokens));
            OnPropertyChanged(nameof(HasFilters));
        }

        private static string FormatMappingRoute(string route)
        {
            if (string.IsNullOrWhiteSpace(route)) return route;
            return Regex.Replace(route.Trim(), @"\s+to\s+", " → ", RegexOptions.IgnoreCase);
        }

        private static List<MappingHeaderToken> BuildMappingRouteTokens(string route)
        {
            var result = new List<MappingHeaderToken>();
            if (string.IsNullOrEmpty(route)) return result;

            var parts = Regex.Split(route, @"\b(Button|Buttons|Axis|Axes|Filter|Event|Events|Delta|Deltas|Value|Values|Multiple|None)\b", RegexOptions.IgnoreCase);
            foreach (var part in parts)
            {
                if (string.IsNullOrEmpty(part)) continue;
                result.Add(new MappingHeaderToken(part, IsMappingTypeWord(part) ? part : null));
            }
            return result;
        }

        private static bool IsMappingTypeWord(string value)
        {
            switch (value.ToLowerInvariant())
            {
                case "button":
                case "buttons":
                case "axis":
                case "axes":
                case "filter":
                case "event":
                case "events":
                case "delta":
                case "deltas":
                case "value":
                case "values":
                case "multiple":
                case "none":
                    return true;
                default:
                    return false;
            }
        }


        private List<BindingVisualDescriptor> BuildCollapsedInputVisuals()
        {
            var result = new List<BindingVisualDescriptor>();
            foreach (var binding in DeviceBindings.Take(3))
            {
                result.Add(DeviceVisualCatalog.DescribeBinding(
                    binding.DeviceBinding, binding.DeviceBindingCategory, ProfileViewModel.Profile));
            }

            if (result.Count == 0)
            {
                result.Add(DeviceVisualCatalog.DescribeBinding(null, DeviceBindingCategory.Momentary, ProfileViewModel.Profile));
            }
            return result;
        }

        private List<BindingVisualDescriptor> BuildCollapsedOutputVisuals()
        {
            var result = new List<BindingVisualDescriptor>();
            var lastPlugin = Plugins.LastOrDefault();
            if (lastPlugin != null)
            {
                foreach (var binding in lastPlugin.DeviceBindings.Take(3))
                {
                    result.Add(DeviceVisualCatalog.DescribeBinding(
                        binding.DeviceBinding, binding.DeviceBindingCategory, ProfileViewModel.Profile));
                }

                if (result.Count == 0)
                {
                    var filterName = lastPlugin.Plugin.GetDefinedFilterName();
                    if (!string.IsNullOrWhiteSpace(filterName)) result.Add(DeviceVisualCatalog.Filter(filterName));
                }
            }

            if (result.Count == 0)
            {
                result.Add(DeviceVisualCatalog.DescribeBinding(null, DeviceBindingCategory.Momentary, ProfileViewModel.Profile));
            }
            return result;
        }

        private string BuildCollapsedSummary()
        {
            var inputs = DeviceBindings.Select(DescribeBinding).Where(value => !string.IsNullOrWhiteSpace(value)).ToList();
            var outputs = new List<string>();

            var lastPlugin = Plugins.LastOrDefault();
            if (lastPlugin != null)
            {
                outputs.AddRange(lastPlugin.DeviceBindings.Select(DescribeBinding).Where(value => !string.IsNullOrWhiteSpace(value)));
                if (outputs.Count == 0)
                {
                    var filterName = lastPlugin.Plugin.GetDefinedFilterName();
                    if (!string.IsNullOrWhiteSpace(filterName)) outputs.Add("Filter: " + filterName);
                }
            }

            var inputText = inputs.Count == 0 ? "No input bound" : JoinCompact(inputs);
            var outputText = outputs.Count == 0 ? "No output bound" : JoinCompact(outputs);
            return inputText + "  →  " + outputText;
        }

        private string DescribeBinding(DeviceBindingViewModel viewModel)
        {
            if (viewModel == null || viewModel.DeviceBinding == null) return null;
            var binding = viewModel.DeviceBinding;
            var configuration = ProfileViewModel.Profile.GetDeviceConfiguration(binding.DeviceIoType, binding.DeviceConfigurationGuid);
            var deviceName = configuration?.GetFullTitleForProfile(ProfileViewModel.Profile);
            if (string.IsNullOrWhiteSpace(deviceName)) deviceName = binding.DeviceIoType == DeviceIoType.Input ? "Input device" : "Output device";

            var controlName = binding.IsBound ? binding.BoundName() : "unbound";
            return Truncate(deviceName, 22) + ": " + Truncate(controlName, 24);
        }

        private static string JoinCompact(IList<string> values)
        {
            if (values.Count == 1) return values[0];
            if (values.Count == 2) return values[0] + " + " + values[1];
            return values[0] + " + " + (values.Count - 1) + " more";
        }

        private static string Truncate(string value, int maximumLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maximumLength) return value;
            return value.Substring(0, maximumLength - 1) + "…";
        }

        public async void Rename()
        {
            var dialog = new StringDialog("Rename mapping", "Mapping name", Mapping.Title);
            var result = (bool?)await DialogHost.Show(dialog, ProfileViewModel.ProfileDialogIdentifier);
            if (result == null || !result.Value) return;

            var oldTitle = Mapping.Title;
            Mapping.Rename(dialog.Value);
            Logger.Info("Mapping renamed: '" + oldTitle + "' -> '" + Mapping.Title + "'");
            OnPropertyChanged(nameof(MappingTitle));
        }

        public async void AddPlugin()
        {
            var dialog = new AddMappingPluginDialog(this);
            var result = (AddMappingPluginDialogViewModel)await DialogHost.Show(dialog, ProfileViewModel.ProfileDialogIdentifier);
            if (result?.SelectedPlugin == null) return;

            AddPlugin(result.SelectedPlugin.Plugin);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
