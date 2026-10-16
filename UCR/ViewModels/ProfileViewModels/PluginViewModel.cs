using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Runtime.CompilerServices;
using HidWizards.UCR.Core.Annotations;
using HidWizards.UCR.Core.Models;
using HidWizards.UCR.Core.Utilities;
using HidWizards.UCR.Views.Dialogs;
using MaterialDesignThemes.Wpf;

namespace HidWizards.UCR.ViewModels.ProfileViewModels
{
    public class PluginViewModel : INotifyPropertyChanged, IDisposable
    {
        public MappingViewModel MappingViewModel { get; }
        public Plugin Plugin { get; set; }
        public string OutputType => IsFourButtonAxis ? "Axis" : new SimplePluginViewModel(Plugin).OutputType;
        private System.Reflection.PropertyInfo InvertProperty => Plugin.GetType().GetProperty("Invert");
        public Visibility QuickInvertVisibility =>
            InvertProperty?.PropertyType == typeof(bool) && InvertProperty.CanWrite
                ? Visibility.Visible : Visibility.Collapsed;
        public bool QuickInvert
        {
            get => QuickInvertVisibility == Visibility.Visible &&
                (bool)InvertProperty.GetValue(Plugin, null);
            set
            {
                if (QuickInvertVisibility != Visibility.Visible || QuickInvert == value) return;
                InvertProperty.SetValue(Plugin, value, null);
                MappingViewModel.ProfileViewModel.Profile.Context.ContextChanged();
                OnPropertyChanged();
            }
        }
        public ObservableCollection<PluginPropertyGroupViewModel> InlinePropertyGroups { get; }
            = new ObservableCollection<PluginPropertyGroupViewModel>();

        public bool IsAxisButtonMapping =>
            string.Equals(Plugin.PluginName, "Button to Axis", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Plugin.PluginName, "Buttons to Axis", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Plugin.PluginName, "Buttons to Axes", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Plugin.PluginName, "Buttons to DPad", StringComparison.OrdinalIgnoreCase);
        public bool IsFourButtonAxis => string.Equals(Plugin.PluginName, "Buttons to Axes", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Plugin.PluginName, "Buttons to DPad", StringComparison.OrdinalIgnoreCase);
        public bool IsDPadOutput => string.Equals(Plugin.PluginName, "Buttons to DPad", StringComparison.OrdinalIgnoreCase);
        public string FourWayOutputLabel
        {
            get
            {
                if (!IsFourButtonAxis || Plugin.Outputs.Count == 0 ||
                    Plugin.Outputs.Any(binding => !binding.IsBound)) return "Choose output";
                if (IsDPadOutput) return "D-pad";
                return Plugin.Outputs[0].KeyValue == 2 ? "Right stick" : "Left stick";
            }
        }
        public void RefreshFourWayDestination()
        {
            OnPropertyChanged(nameof(FourWayOutputLabel));
            OnPropertyChanged(nameof(SelectedFourWayOutputDeviceGuid));
            OnPropertyChanged(nameof(FourWayOutputDevices));
        }
        public bool IsTwoButtonAxis => string.Equals(Plugin.PluginName, "Buttons to Axis", StringComparison.OrdinalIgnoreCase);
        public bool IsOneButtonAxis => IsAxisButtonMapping && !IsTwoButtonAxis && !IsFourButtonAxis;
        public Visibility AxisButtonModeVisibility => IsAxisButtonMapping ? Visibility.Visible : Visibility.Collapsed;
        public bool CanChangeAxisButtonMode => MappingViewModel.ButtonsEnabled &&
            MappingViewModel.Plugins.Count == 1;
        public string AxisButtonModeDescription => IsFourButtonAxis ? "4 ways" : IsTwoButtonAxis ? "2 buttons" : "1 button";
        public Visibility FourButtonPresetVisibility => IsFourButtonAxis ? Visibility.Visible : Visibility.Collapsed;
        public Visibility KeyboardAxisPresetVisibility =>
            IsFourButtonAxis && MappingViewModel.SelectedPresetInputIsKeyboard ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ControllerAxisPresetVisibility =>
            IsFourButtonAxis && MappingViewModel.SelectedPresetInputHasDPad ? Visibility.Visible : Visibility.Collapsed;
        public void RefreshInputPresetDisplay()
        {
            OnPropertyChanged(nameof(KeyboardAxisPresetVisibility));
            OnPropertyChanged(nameof(ControllerAxisPresetVisibility));
        }
        public System.Collections.Generic.List<FourWayOutputDeviceOption> FourWayOutputDevices =>
            MappingViewModel.GetFourWayOutputDevices();
        public Guid SelectedFourWayOutputDeviceGuid
        {
            get
            {
                var current = Plugin.Outputs.FirstOrDefault()?.DeviceConfigurationGuid ?? Guid.Empty;
                var devices = FourWayOutputDevices;
                return devices.Any(item => item.Guid == current)
                    ? current : devices.FirstOrDefault()?.Guid ?? Guid.Empty;
            }
            set
            {
                if (value == Guid.Empty || value == Plugin.Outputs.FirstOrDefault()?.DeviceConfigurationGuid) return;
                MappingViewModel.SetFourWayOutputDevice(this, value);
                OnPropertyChanged(nameof(SelectedFourWayOutputDeviceGuid));
                OnPropertyChanged(nameof(FourWayOutputLabel));
            }
        }

        public ObservableCollection<DeviceBindingViewModel> DeviceBindings { get; set; }
        public ObservableCollection<PluginPropertyGroupViewModel> PluginPropertyGroups { get; set; }
        public bool CanRemove => !MappingViewModel.ProfileViewModel.Profile.IsActive() && MappingViewModel.Plugins.Count > 1;
        public bool CanAddFilter => !MappingViewModel.ProfileViewModel.Profile.IsActive();
        public ObservableCollection<FilterViewModel> Filters { get; set; }
        private bool _editorInitialized;
        private bool _disposed;

        public PluginViewModel(MappingViewModel mappingViewModel, Plugin plugin)
        {
            MappingViewModel = mappingViewModel;
            Plugin = plugin;
            mappingViewModel.Plugins.CollectionChanged += Plugins_CollectionChanged;
            Plugin.FilterDefinitionChanged += PluginOnFilterDefinitionChanged;
            PluginPropertyGroups = new ObservableCollection<PluginPropertyGroupViewModel>();
            Filters = new ObservableCollection<FilterViewModel>();
            PopulateDeviceBindingsViewModels();
        }

        public void EnsureEditorInitialized()
        {
            if (_editorInitialized || _disposed) return;
            _editorInitialized = true;
            PopulatePluginProperties();
            PopulateFilterViewModels();
        }


        private void PluginOnFilterDefinitionChanged(string oldName, string newName)
        {
            Logger.Info("Filter definition changed: '" + (oldName ?? "") + "' -> '" + (newName ?? "") + "' in mapping " + MappingViewModel.MappingTitle);
            MappingViewModel.ProfileViewModel.RefreshFilterReferenceLabels();
            MappingViewModel.RefreshCollapsedSummary();
        }

        private void PopulateFilterViewModels()
        {
            foreach (var filter in Plugin.Filters)
            {
                Filters.Add(new FilterViewModel(this, filter));
            }
        }

        public void ReloadFiltersFromModel()
        {
            if (!_editorInitialized) return;

            // Keep existing view-model instances where possible so renaming a definition does not
            // accumulate duplicate ActiveProfileChanged / FilterState subscriptions.
            for (var index = Filters.Count - 1; index >= 0; index--)
            {
                var filterViewModel = Filters[index];
                if (Plugin.Filters.Contains(filterViewModel.Filter)) continue;
                filterViewModel.Dispose();
                Filters.RemoveAt(index);
            }

            foreach (var filter in Plugin.Filters)
            {
                var existing = Filters.FirstOrDefault(viewModel => object.ReferenceEquals(viewModel.Filter, filter));
                if (existing == null)
                {
                    Filters.Add(new FilterViewModel(this, filter));
                }
                else
                {
                    existing.RefreshName();
                }
            }
        }

        private void Plugins_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(CanRemove));
            OnPropertyChanged(nameof(CanChangeAxisButtonMode));
        }

        internal void RefreshActiveState()
        {
            if (_disposed) return;
            OnPropertyChanged(nameof(CanRemove));
            OnPropertyChanged(nameof(CanAddFilter));
            foreach (var binding in DeviceBindings) binding.RefreshActiveState();
        }

        public void Remove()
        {
            MappingViewModel.RemovePlugin(this);
        }

        private void PopulateDeviceBindingsViewModels()
        {
            DeviceBindings = new ObservableCollection<DeviceBindingViewModel>();
            for (var i = 0; i < Plugin.OutputCategories.Count; i++)
            {
                DeviceBindings.Add(new DeviceBindingViewModel(Plugin.Outputs[i])
                {
                    DeviceBindingName = Plugin.OutputCategories[i].Name,
                    DeviceBindingCategory = Plugin.OutputCategories[i].Category,
                    PluginPropertyGroup = GetPluginPropertyGroupForOutput(Plugin.OutputCategories[i].GroupName)
                });
            }
        }

        private PluginPropertyGroupViewModel GetPluginPropertyGroupForOutput(string groupName)
        {
            if (groupName == null) return null;
            return new PluginPropertyGroupViewModel(Plugin.GetGuiMatrix().Find(p => p.GroupName.Equals(groupName)));
        }

        private void PopulatePluginProperties()
        {
            foreach (var pluginPropertyGroup in Plugin.PluginPropertyGroups)
            {
                if (pluginPropertyGroup.PluginProperties.Count == 0) continue;
                // The compact output inspector must show output-specific values too:
                // Button to Axis has pressed/released axis positions in its "Axis"
                // output group, which used to be silently omitted here.
                PluginPropertyGroups.Add(new PluginPropertyGroupViewModel(pluginPropertyGroup));
                var inlineGroup = new PluginPropertyGroupViewModel(pluginPropertyGroup);
                if (QuickInvertVisibility == Visibility.Visible)
                    inlineGroup.PluginProperties.RemoveAll(property => property.Name == "Invert");
                if (inlineGroup.PluginProperties.Count > 0)
                    InlinePropertyGroups.Add(inlineGroup);
            }
        }


        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            MappingViewModel.Plugins.CollectionChanged -= Plugins_CollectionChanged;
            Plugin.FilterDefinitionChanged -= PluginOnFilterDefinitionChanged;

            foreach (var filter in Filters ?? new ObservableCollection<FilterViewModel>()) filter.Dispose();
            foreach (var binding in DeviceBindings ?? new ObservableCollection<DeviceBindingViewModel>()) binding.Dispose();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public async void AddFilter()
        {
            EnsureEditorInitialized();
            var existingOnPlugin = Filters.Select(existingFilter => existingFilter.Name).ToList();
            var availableNames = MappingViewModel.ProfileViewModel.Profile.GetFilters()
                .Where(name => !existingOnPlugin.Any(existing =>
                    string.Equals(existing, name, System.StringComparison.CurrentCultureIgnoreCase)))
                .OrderBy(name => name, System.StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            if (availableNames.Count == 0)
            {
                HidWizards.UCR.Utilities.DarkMessageBox.Show(
                    "This profile does not currently define any filters. Create one with a Button to Filter or Axis to Filter mapping first.",
                    "No filters available", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }

            var dialog = new FilterSelectDialog(availableNames);
            var result = (HidWizards.UCR.ViewModels.Dialogs.FilterSelectDialogViewModel)await DialogHost.Show(
                dialog, MappingViewModel.ProfileViewModel.ProfileDialogIdentifier);
            if (result == null || string.IsNullOrWhiteSpace(result.SelectedFilter)) return;
            if (!availableNames.Any(name => string.Equals(name, result.SelectedFilter, System.StringComparison.CurrentCultureIgnoreCase))) return;

            var filter = Plugin.AddFilter(result.SelectedFilter.Trim());
            if (!Filters.Any(existing => string.Equals(existing.Name, filter.Name, System.StringComparison.CurrentCultureIgnoreCase)))
            {
                Filters.Add(new FilterViewModel(this, filter));
            }
            Logger.Info("Filter reference added: '" + filter.Name + "' to mapping " + MappingViewModel.MappingTitle);
            MappingViewModel.ProfileViewModel.RefreshFilterNames();
            MappingViewModel.RefreshFilterIndicator();
        }

        public void RemoveFilter(FilterViewModel filterViewModel)
        {
            if (filterViewModel == null) return;
            EnsureEditorInitialized();
            if (Plugin.RemoveFilter(filterViewModel.Filter))
            {
                Logger.Info("Filter reference removed: '" + filterViewModel.Name + "' from mapping " + MappingViewModel.MappingTitle);
                filterViewModel.Dispose();
                Filters.Remove(filterViewModel);
                MappingViewModel.ProfileViewModel.RefreshFilterNames();
                MappingViewModel.RefreshFilterIndicator();
            }
        }

        public void ToggleFilter(FilterViewModel filterViewModel)
        {
            Plugin.ToggleFilter(filterViewModel.Filter);
        }
    }
}
