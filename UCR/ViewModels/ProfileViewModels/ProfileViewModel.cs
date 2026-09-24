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
        public bool CanActivateProfile => Profile != null;
        public bool CanDeactivateProfile => Profile.IsActive();
        public bool CanEditProfile => !Profile.IsActive();
        public bool IsProfileActive => Profile.IsActive();
        public string EditLockReason => IsProfileActive ? "Profile is running — stop it to edit mappings." : null;
        public ObservableCollection<MappingViewModel> MappingsList { get; set; }
        public ObservableCollection<MappingGroupViewModel> MappingSections { get; private set; }
        private MappingGroupViewModel _selectedMappingSection;
        private static MappingGroup _copiedMappingGroup;
        private static Profile _copiedMappingGroupSourceProfile;

        public MappingGroupViewModel SelectedMappingSection
        {
            get => _selectedMappingSection;
            set
            {
                if (ReferenceEquals(_selectedMappingSection, value)) return;
                _selectedMappingSection = value;
                OnPropertyChanged();
            }
        }

        public bool CanPasteMappingGroup => _copiedMappingGroup != null && !Profile.IsActive();
        public ObservableCollection<string> FilterNames { get; private set; }
        public ObservableCollection<FilterDefinitionItemViewModel> FilterDefinitions { get; private set; }
        public PluginToolboxViewModel PluginToolbox { get; set; }
        public ProfileDeviceListControlViewModel InputDeviceControlViewModel { get; private set; }
        public ProfileDeviceListControlViewModel OutputDeviceControlViewModel { get; private set; }

        public string ProfileDialogIdentifier => $"ProfileDialog-{Profile.Guid}";
        private bool _disposed;
        private bool _lastKnownActiveState;

        public ProfileViewModel()
        {

        }

        public ProfileViewModel(Profile profile)
        {
            Profile = profile;
            _lastKnownActiveState = profile.IsActive();
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
            var isActive = Profile.IsActive();
            if (isActive == _lastKnownActiveState) return;
            _lastKnownActiveState = isActive;

            OnPropertyChanged(nameof(CanActivateProfile));
            OnPropertyChanged(nameof(CanDeactivateProfile));
            OnPropertyChanged(nameof(CanEditProfile));
            OnPropertyChanged(nameof(IsProfileActive));
            OnPropertyChanged(nameof(EditLockReason));
            OnPropertyChanged(nameof(CanPasteMappingGroup));
            foreach (var section in MappingSections ?? new ObservableCollection<MappingGroupViewModel>()) section.RefreshActiveState();
        }

        private void PopulateMappingsList(Profile profile)
        {
            MappingsList = new ObservableCollection<MappingViewModel>();
            MappingSections = new ObservableCollection<MappingGroupViewModel>();

            var main = new MappingGroupViewModel(this, null, true);
            MappingSections.Add(main);
            SelectedMappingSection = main;
            foreach (var profileMapping in profile.Mappings ?? new List<Mapping>())
            {
                AddMapping(profileMapping, main, false);
            }

            foreach (var group in profile.MappingGroups ?? new List<MappingGroup>())
            {
                if (group == null) continue;
                var section = new MappingGroupViewModel(this, group, false);
                MappingSections.Add(section);
                foreach (var mapping in group.Mappings ?? new List<Mapping>()) AddMapping(mapping, section, false);
            }

            // Build the flattened compatibility list once. Rebuilding it after every item turns
            // profile opening into quadratic work on larger profiles.
            RebuildFlatMappingsList();
            RefreshMappingPositions();
        }

        public MappingViewModel AddMapping(string title)
        {
            if (Profile.IsActive()) return null;
            var main = MappingSections?.FirstOrDefault(section => section.IsMain);
            return AddMapping(Profile.AddMapping(title), main);
        }

        public MappingViewModel AddMappingToSelectedSection(string title)
        {
            return AddMapping(title, SelectedMappingSection ?? MappingSections?.FirstOrDefault(section => section.IsMain));
        }

        public MappingViewModel AddMapping(string title, MappingGroupViewModel section)
        {
            if (section == null || section.IsMain) return AddMapping(title);
            if (section.Model == null || Profile.IsActive()) return null;
            return AddMapping(section.Model.AddMapping(title), section);
        }

        public string GetNextMappingTitle()
        {
            var number = 1;
            while (true)
            {
                var candidate = "Mapping " + number;
                var exists = Profile.GetAllMappings().Any(mapping =>
                    string.Equals(mapping.Title, candidate, StringComparison.CurrentCultureIgnoreCase));
                if (!exists) return candidate;
                number++;
            }
        }

        public MappingViewModel AddMapping(Mapping mapping)
        {
            var group = Profile.GetMappingGroup(mapping);
            var section = group == null
                ? MappingSections?.FirstOrDefault(candidate => candidate.IsMain)
                : MappingSections?.FirstOrDefault(candidate => ReferenceEquals(candidate.Model, group));
            return AddMapping(mapping, section);
        }

        private MappingViewModel AddMapping(Mapping mapping, MappingGroupViewModel section, bool refreshCollections = true)
        {
            if (mapping == null) return null;
            var mappingViewModel = new MappingViewModel(this, mapping);
            if (section == null)
            {
                MappingsList.Add(mappingViewModel);
            }
            else
            {
                section.Mappings.Add(mappingViewModel);
                if (refreshCollections) RebuildFlatMappingsList();
            }
            if (refreshCollections) RefreshMappingPositions();
            return mappingViewModel;
        }

        public MappingGroupViewModel AddMappingGroup(string title)
        {
            if (Profile.IsActive()) return null;
            var model = Profile.AddMappingGroup(title);
            var section = new MappingGroupViewModel(this, model, false);
            MappingSections.Add(section);
            SelectedMappingSection = section;
            OnPropertyChanged(nameof(MappingSections));
            return section;
        }

        public bool RenameMappingGroup(MappingGroupViewModel section, string title)
        {
            if (section == null || section.IsMain || section.Model == null || Profile.IsActive()) return false;
            if (!section.Model.Rename(title)) return false;
            section.RefreshTitle();
            return true;
        }

        public MappingGroupViewModel DuplicateMappingGroup(MappingGroupViewModel section)
        {
            if (section == null || section.IsMain || section.Model == null || Profile.IsActive()) return null;
            var copy = Profile.CopyMappingGroup(section.Model);
            return AddSection(copy);
        }

        public void CopyMappingGroup(MappingGroupViewModel section)
        {
            if (section == null || section.IsMain || section.Model == null) return;
            _copiedMappingGroup = HidWizards.UCR.Core.Context.DeepXmlClone<MappingGroup>(section.Model);
            _copiedMappingGroupSourceProfile = Profile;
            OnPropertyChanged(nameof(CanPasteMappingGroup));
        }

        public MappingGroupViewModel PasteMappingGroup()
        {
            if (_copiedMappingGroup == null || Profile.IsActive()) return null;
            var copy = Profile.CopyMappingGroup(_copiedMappingGroup, _copiedMappingGroupSourceProfile, _copiedMappingGroup.Title);
            return AddSection(copy);
        }

        private MappingGroupViewModel AddSection(MappingGroup model)
        {
            if (model == null) return null;
            var section = new MappingGroupViewModel(this, model, false);
            MappingSections.Add(section);
            foreach (var mapping in model.Mappings ?? new List<Mapping>()) AddMapping(mapping, section, false);
            RebuildFlatMappingsList();
            RefreshMappingPositions();
            SelectedMappingSection = section;
            OnPropertyChanged(nameof(MappingSections));
            return section;
        }

        public bool RemoveMappingGroup(MappingGroupViewModel section)
        {
            if (section == null || section.IsMain || section.Model == null || Profile.IsActive()) return false;
            if (!Profile.RemoveMappingGroup(section.Model)) return false;
            foreach (var mapping in section.Mappings.ToList())
            {
                mapping.Dispose();
                MappingsList.Remove(mapping);
            }
            MappingSections.Remove(section);
            if (ReferenceEquals(SelectedMappingSection, section))
                SelectedMappingSection = MappingSections.FirstOrDefault(candidate => candidate.IsMain);
            RefreshMappingPositions();
            RefreshFilterReferenceLabels();
            return true;
        }

        private MappingGroupViewModel FindSection(MappingViewModel mappingViewModel)
        {
            return MappingSections?.FirstOrDefault(section => section.Mappings.Contains(mappingViewModel));
        }

        public MappingGroupViewModel GetMappingSection(MappingViewModel mappingViewModel)
        {
            return FindSection(mappingViewModel);
        }

        public bool MoveMappingToSection(MappingViewModel mappingViewModel, MappingGroupViewModel targetSection)
        {
            if (mappingViewModel == null || targetSection == null || Profile.IsActive()) return false;

            var sourceSection = FindSection(mappingViewModel);
            if (sourceSection == null) return false;
            if (ReferenceEquals(sourceSection, targetSection)) return true;

            var targetGroup = targetSection.IsMain ? null : targetSection.Model;
            if (!Profile.MoveMapping(mappingViewModel.Mapping, targetGroup, targetSection.Mappings.Count)) return false;

            sourceSection.Mappings.Remove(mappingViewModel);
            targetSection.Mappings.Add(mappingViewModel);
            SelectedMappingSection = targetSection;
            RebuildFlatMappingsList();
            RefreshMappingPositions();
            return true;
        }

        public bool CanMoveMapping(MappingViewModel mappingViewModel, int offset)
        {
            if (mappingViewModel == null || Profile.IsActive()) return false;
            var section = FindSection(mappingViewModel);
            if (section == null) return false;
            var index = section.Mappings.IndexOf(mappingViewModel);
            var target = index + offset;
            return index >= 0 && target >= 0 && target < section.Mappings.Count;
        }

        public bool MoveMapping(MappingViewModel mappingViewModel, int offset)
        {
            if (mappingViewModel == null || Profile.IsActive()) return false;
            var section = FindSection(mappingViewModel);
            if (section == null) return false;
            var sourceIndex = section.Mappings.IndexOf(mappingViewModel);
            var targetIndex = sourceIndex + offset;
            if (sourceIndex < 0 || targetIndex < 0 || targetIndex >= section.Mappings.Count) return false;
            if (!Profile.MoveMapping(mappingViewModel.Mapping, targetIndex)) return false;

            section.Mappings.Move(sourceIndex, targetIndex);
            RebuildFlatMappingsList();
            RefreshMappingPositions();
            return true;
        }

        public bool MoveMappingTo(MappingViewModel mappingViewModel, int targetIndex)
        {
            if (mappingViewModel == null || Profile.IsActive()) return false;
            var section = FindSection(mappingViewModel);
            if (section == null) return false;
            var sourceIndex = section.Mappings.IndexOf(mappingViewModel);
            if (sourceIndex < 0 || targetIndex < 0 || targetIndex >= section.Mappings.Count || sourceIndex == targetIndex) return false;
            if (!Profile.MoveMapping(mappingViewModel.Mapping, targetIndex)) return false;

            section.Mappings.Move(sourceIndex, targetIndex);
            RebuildFlatMappingsList();
            RefreshMappingPositions();
            return true;
        }

        private void RebuildFlatMappingsList()
        {
            var ordered = (MappingSections ?? new ObservableCollection<MappingGroupViewModel>())
                .SelectMany(section => section.Mappings).ToList();
            MappingsList.Clear();
            foreach (var mapping in ordered) MappingsList.Add(mapping);
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
                var section = FindSection(mappingViewModel);
                mappingViewModel.Dispose();
                section?.Mappings.Remove(mappingViewModel);
                MappingsList.Remove(mappingViewModel);
                RefreshMappingPositions();
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
