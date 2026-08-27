using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
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
        public ObservableCollection<DeviceBindingViewModel> DeviceBindings { get; set; }
        public ObservableCollection<PluginPropertyGroupViewModel> PluginPropertyGroups { get; set; }
        public bool CanRemove => !MappingViewModel.ProfileViewModel.Profile.IsActive() && MappingViewModel.Plugins.Count > 1;
        public bool CanAddFilter => !MappingViewModel.ProfileViewModel.Profile.IsActive();
        public ObservableCollection<FilterViewModel> Filters { get; set; }
        private bool _disposed;

        public PluginViewModel(MappingViewModel mappingViewModel, Plugin plugin)
        {
            MappingViewModel = mappingViewModel;
            Plugin = plugin;
            mappingViewModel.ProfileViewModel.Profile.Context.ActiveProfileChangedEvent += ContextOnActiveProfileChangedEvent;
            mappingViewModel.Plugins.CollectionChanged += Plugins_CollectionChanged;
            Plugin.FilterDefinitionChanged += PluginOnFilterDefinitionChanged;
            PopulateDeviceBindingsViewModels();
            PopulatePluginProperties();
            PopulateFilterViewModels();
        }


        private void PluginOnFilterDefinitionChanged(string oldName, string newName)
        {
            Logger.Info("Filter definition changed: '" + (oldName ?? "") + "' -> '" + (newName ?? "") + "' in mapping " + MappingViewModel.MappingTitle);
            MappingViewModel.ProfileViewModel.RefreshFilterNames();
            MappingViewModel.ProfileViewModel.RefreshFilterReferenceLabels();
            MappingViewModel.RefreshCollapsedSummary();
        }

        private void PopulateFilterViewModels()
        {
            Filters = new ObservableCollection<FilterViewModel>();
            foreach (var filter in Plugin.Filters)
            {
                Filters.Add(new FilterViewModel(this, filter));
            }
        }

        public void ReloadFiltersFromModel()
        {
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
        }

        private void ContextOnActiveProfileChangedEvent(Profile profile)
        {
            OnPropertyChanged(nameof(CanRemove));
            OnPropertyChanged(nameof(CanAddFilter));
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
            PluginPropertyGroups = new ObservableCollection<PluginPropertyGroupViewModel>();
            foreach (var pluginPropertyGroup in Plugin.PluginPropertyGroups)
            {
                if (pluginPropertyGroup.PluginProperties.Count == 0 || pluginPropertyGroup.GroupType.Equals(PluginPropertyGroup.GroupTypes.Output)) continue;

                PluginPropertyGroups.Add(new PluginPropertyGroupViewModel(pluginPropertyGroup));
            }
        }


        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            var context = MappingViewModel.ProfileViewModel.Profile.Context;
            context.ActiveProfileChangedEvent -= ContextOnActiveProfileChangedEvent;
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
