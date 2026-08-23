using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using HidWizards.UCR.Core.Annotations;
using HidWizards.UCR.Core.Models;
using HidWizards.UCR.Views.Dialogs;
using MaterialDesignThemes.Wpf;

namespace HidWizards.UCR.ViewModels.ProfileViewModels
{
    public class ProfileViewModel : INotifyPropertyChanged
    {
        public Profile Profile { get; }
        public bool CanActivateProfile => Profile.Context.ActiveProfile != Profile;
        public bool CanDeactivateProfile => Profile.Context.ActiveProfile != null;
        public ObservableCollection<MappingViewModel> MappingsList { get; set; }
        public ObservableCollection<string> FilterNames { get; private set; }
        public PluginToolboxViewModel PluginToolbox { get; set; }
        public string ProfileDialogIdentifier => $"ProfileDialog-{Profile.Guid}";

        public ProfileViewModel()
        {

        }

        public ProfileViewModel(Profile profile)
        {
            Profile = profile;
            profile.Context.ActiveProfileChangedEvent += ContextOnActiveProfileChangedEvent;
            PopulateMappingsList(profile);
            RefreshFilterNames();
            var pluginList = profile.Context.GetPlugins();
            pluginList.Sort();
            PluginToolbox = new PluginToolboxViewModel(profile, pluginList);
        }

        private void ContextOnActiveProfileChangedEvent(Profile profile)
        {
            OnPropertyChanged(nameof(CanActivateProfile));
            OnPropertyChanged(nameof(CanDeactivateProfile));
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

        private void RefreshMappingPositions()
        {
            foreach (var mapping in MappingsList) mapping.RefreshPositionState();
        }

        public void RefreshFilterNames()
        {
            var names = Profile.GetFilters().OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase).ToList();
            FilterNames = new ObservableCollection<string>(names);
            OnPropertyChanged(nameof(FilterNames));
        }

        public async void RemoveMapping(MappingViewModel mappingViewModel)
        {
            if (mappingViewModel.Mapping.DeviceBindings.Count > 0)
            {
                var dialog = new BoolDialog("Remove mapping", "Are you sure you want to remove the mapping: " + mappingViewModel.Mapping.Title + "?");
                var result = (bool?)await DialogHost.Show(dialog, ProfileDialogIdentifier);
                if (result == null || !result.Value) return;
            }

            if (Profile.RemoveMapping(mappingViewModel.Mapping))
            {
                MappingsList.Remove(mappingViewModel);
                RefreshMappingPositions();
                RefreshFilterNames();
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
