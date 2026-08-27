using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Windows;
using HidWizards.UCR.Core.Annotations;
using HidWizards.UCR.Core.Models;
using HidWizards.UCR.ViewModels.Dashboard;
using HidWizards.UCR.Views.Dialogs;

namespace HidWizards.UCR.ViewModels.ProfileViewModels
{
    public sealed class FilterDefinitionItemViewModel
    {
        public string Name { get; set; }
        public int ReferenceCount { get; set; }
        public bool IsInherited { get; set; }
        public MappingViewModel DefiningMapping { get; set; }
        public string ReferenceText => ReferenceCount == 1 ? "1 use" : ReferenceCount + " uses";
        public string KindText => IsInherited ? "INHERITED" : "DEFINED";
        public string ToolTip => IsInherited
            ? "Filter inherited from a parent profile. Click to highlight mappings that use it."
            : "Filter definition. Click to highlight its definition and every mapping that uses it.";
    }

    public class ProfileViewModel : INotifyPropertyChanged, IDisposable
    {
        public Profile Profile { get; }
        public bool CanActivateProfile => Profile.Context.ActiveProfile != Profile;
        public bool CanDeactivateProfile => Profile.Context.ActiveProfile != null;
        public bool CanEditProfile => !Profile.IsActive();
        public ObservableCollection<MappingViewModel> MappingsList { get; set; }
        public ObservableCollection<string> FilterNames { get; private set; }
        public ObservableCollection<FilterDefinitionItemViewModel> FilterDefinitions { get; private set; }
        public PluginToolboxViewModel PluginToolbox { get; set; }
        public ProfileDeviceListControlViewModel InputDeviceControlViewModel { get; private set; }
        public ProfileDeviceListControlViewModel OutputDeviceControlViewModel { get; private set; }

        public string ProfileDialogIdentifier => $"ProfileDialog-{Profile.Guid}";
        private bool _disposed;

        public ProfileViewModel()
        {

        }

        public ProfileViewModel(Profile profile)
        {
            Profile = profile;
            profile.Context.ActiveProfileChangedEvent += ContextOnActiveProfileChangedEvent;
            if (profile.PruneUndefinedFilterReferencesRecursive()) profile.Context.ContextChanged();
            PopulateMappingsList(profile);
            RefreshFilterNames();
            var pluginList = profile.Context.GetPlugins();
            pluginList.Sort();
            PluginToolbox = new PluginToolboxViewModel(profile, pluginList);
            InputDeviceControlViewModel = new ProfileDeviceListControlViewModel(profile,
                profile.GetDeviceConfigurationList(DeviceIoType.Input), DeviceIoType.Input, RefreshDevicePresentation, ProfileDialogIdentifier);
            OutputDeviceControlViewModel = new ProfileDeviceListControlViewModel(profile,
                profile.GetDeviceConfigurationList(DeviceIoType.Output), DeviceIoType.Output, RefreshDevicePresentation, ProfileDialogIdentifier);
        }

        public void RefreshDevicePresentation()
        {
            PluginToolbox?.RefreshDeviceCapabilities();
            foreach (var binding in GetAllBindingViewModels().ToList()) binding?.RefreshDeviceList();
            foreach (var mapping in MappingsList ?? new ObservableCollection<MappingViewModel>())
            {
                mapping.RefreshCollapsedSummary();
            }
            OnPropertyChanged(nameof(InputDeviceControlViewModel));
            OnPropertyChanged(nameof(OutputDeviceControlViewModel));
        }

        private void ContextOnActiveProfileChangedEvent(Profile profile)
        {
            OnPropertyChanged(nameof(CanActivateProfile));
            OnPropertyChanged(nameof(CanDeactivateProfile));
            OnPropertyChanged(nameof(CanEditProfile));
        }

        private void PopulateMappingsList(Profile profile)
        {
            MappingsList = new ObservableCollection<MappingViewModel>();
            foreach (var profileMapping in profile.Mappings)
            {
                AddMapping(profileMapping);
            }
        }

        public MappingViewModel AddMapping(string title)
        {
            return AddMapping(Profile.AddMapping(title));
        }

        public string GetNextMappingTitle()
        {
            var number = 1;
            while (true)
            {
                var candidate = "Mapping " + number;
                var exists = false;
                foreach (var mapping in Profile.Mappings)
                {
                    if (!string.Equals(mapping.Title, candidate, StringComparison.CurrentCultureIgnoreCase)) continue;
                    exists = true;
                    break;
                }

                if (!exists) return candidate;
                number++;
            }
        }

        public MappingViewModel AddMapping(Mapping mapping)
        {
            var mappingViewModel = new MappingViewModel(this, mapping);
            MappingsList.Add(mappingViewModel);
            RefreshMappingPositions();
            return mappingViewModel;
        }

        public bool MoveMapping(MappingViewModel mappingViewModel, int offset)
        {
            if (mappingViewModel == null || Profile.IsActive()) return false;
            var sourceIndex = MappingsList.IndexOf(mappingViewModel);
            var targetIndex = sourceIndex + offset;
            if (sourceIndex < 0 || targetIndex < 0 || targetIndex >= MappingsList.Count) return false;
            if (!Profile.MoveMapping(mappingViewModel.Mapping, targetIndex)) return false;

            MappingsList.Move(sourceIndex, targetIndex);
            RefreshMappingPositions();
            return true;
        }

        public bool MoveMappingTo(MappingViewModel mappingViewModel, int targetIndex)
        {
            if (mappingViewModel == null || Profile.IsActive()) return false;
            var sourceIndex = MappingsList.IndexOf(mappingViewModel);
            if (sourceIndex < 0 || targetIndex < 0 || targetIndex >= MappingsList.Count || sourceIndex == targetIndex) return false;
            if (!Profile.MoveMapping(mappingViewModel.Mapping, targetIndex)) return false;

            MappingsList.Move(sourceIndex, targetIndex);
            RefreshMappingPositions();
            return true;
        }

        private void RefreshMappingPositions()
        {
            foreach (var mapping in MappingsList) mapping.RefreshPositionState();
        }

        public void RefreshFilterNames()
        {
            var names = Profile.GetFilters().OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase).ToList();
            FilterNames = new ObservableCollection<string>(names);
            FilterDefinitions = new ObservableCollection<FilterDefinitionItemViewModel>();

            foreach (var name in names)
            {
                var definingMapping = MappingsList?.FirstOrDefault(mapping => mapping.DefinesFilter(name));
                var referenceCount = MappingsList == null ? 0 : MappingsList.Count(mapping => mapping.ReferencesFilter(name));
                FilterDefinitions.Add(new FilterDefinitionItemViewModel
                {
                    Name = name,
                    ReferenceCount = referenceCount,
                    IsInherited = definingMapping == null,
                    DefiningMapping = definingMapping
                });
            }

            OnPropertyChanged(nameof(FilterNames));
            OnPropertyChanged(nameof(FilterDefinitions));
        }

        public void RefreshFilterReferenceLabels()
        {
            foreach (var mapping in MappingsList)
            {
                foreach (var plugin in mapping.Plugins) plugin.ReloadFiltersFromModel();
                mapping.RefreshFilterIndicator();
            }
            RefreshFilterNames();
        }

        public MappingViewModel HighlightFilter(FilterDefinitionItemViewModel filter)
        {
            if (filter == null)
            {
                ClearFilterHighlight();
                return null;
            }

            foreach (var mapping in MappingsList)
            {
                mapping.SetFilterHighlight(mapping.DefinesFilter(filter.Name), mapping.ReferencesFilter(filter.Name));
            }
            return filter.DefiningMapping;
        }

        public void ClearFilterHighlight()
        {
            foreach (var mapping in MappingsList) mapping.SetFilterHighlight(false, false);
        }

        public IEnumerable<DeviceBindingViewModel> GetAllBindingViewModels()
        {
            foreach (var mapping in MappingsList)
            {
                foreach (var binding in mapping.DeviceBindings) yield return binding;
                foreach (var plugin in mapping.Plugins)
                {
                    foreach (var binding in plugin.DeviceBindings) yield return binding;
                }
            }
        }

        public HidWizards.UCR.ViewModels.Dialogs.BatchDeviceChangeResult BatchChangeDevice(
            HidWizards.UCR.ViewModels.Dialogs.BatchDeviceOption source,
            HidWizards.UCR.ViewModels.Dialogs.BatchDeviceOption target)
        {
            var result = new HidWizards.UCR.ViewModels.Dialogs.BatchDeviceChangeResult();
            if (source == null || target == null || source.IoType != target.IoType || source.Guid == target.Guid) return result;

            foreach (var binding in GetAllBindingViewModels())
            {
                if (binding?.DeviceBinding == null) continue;
                if (binding.DeviceBinding.DeviceIoType != source.IoType || binding.DeviceBinding.DeviceConfigurationGuid != source.Guid) continue;

                var compatibility = binding.ChangeDeviceConfiguration(target.Guid);
                result.Changed++;
                if (compatibility == DeviceBindingTransferCompatibility.Incompatible) result.ClearedAsIncompatible++;
                if (compatibility == DeviceBindingTransferCompatibility.Unknown) result.PreservedUnknown++;
            }

            foreach (var mapping in MappingsList) mapping.RefreshCollapsedSummary();
            return result;
        }

        public void ApplyMappingNames(IEnumerable<HidWizards.UCR.ViewModels.Dialogs.RenameMappingItemViewModel> items)
        {
            if (items == null || Profile.IsActive()) return;
            foreach (var item in items)
            {
                if (item?.Mapping == null || string.IsNullOrWhiteSpace(item.Name)) continue;
                var cleanName = item.Name.Trim();
                if (string.Equals(item.Mapping.Mapping.Title, cleanName, StringComparison.Ordinal)) continue;
                item.Mapping.Mapping.Rename(cleanName);
                item.Mapping.RefreshTitle();
            }
        }

        public void RemoveMapping(MappingViewModel mappingViewModel)
        {
            if (mappingViewModel == null) return;
            if (mappingViewModel.Mapping.DeviceBindings.Count > 0 || mappingViewModel.Plugins.Count > 0)
            {
                var result = HidWizards.UCR.Utilities.DarkMessageBox.Show(
                    "Remove mapping '" + mappingViewModel.Mapping.Title + "'?",
                    "Remove mapping", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result != MessageBoxResult.Yes) return;
            }

            if (Profile.RemoveMapping(mappingViewModel.Mapping))
            {
                mappingViewModel.Dispose();
                MappingsList.Remove(mappingViewModel);
                RefreshMappingPositions();
                RefreshFilterNames();
                RefreshFilterReferenceLabels();
            }
        }


        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Profile.Context.ActiveProfileChangedEvent -= ContextOnActiveProfileChangedEvent;
            PluginToolbox?.Dispose();
            InputDeviceControlViewModel?.Dispose();
            OutputDeviceControlViewModel?.Dispose();
            foreach (var mapping in MappingsList ?? new ObservableCollection<MappingViewModel>()) mapping.Dispose();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
