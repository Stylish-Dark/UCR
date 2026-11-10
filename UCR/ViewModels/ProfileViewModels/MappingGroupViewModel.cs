using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using HidWizards.UCR.Core.Annotations;
using HidWizards.UCR.Core.Models;

namespace HidWizards.UCR.ViewModels.ProfileViewModels
{
    public class MappingGroupViewModel : INotifyPropertyChanged
    {
        private readonly ProfileViewModel _profileViewModel;

        public MappingGroup Model { get; }
        public bool IsMain { get; }
        public ObservableCollection<MappingViewModel> Mappings { get; } = new ObservableCollection<MappingViewModel>();
        public string Title => IsMain ? (_profileViewModel.Profile.MainGroupTitle ?? "Main") : Model?.Title ?? "Group";
        public bool CanEdit => !_profileViewModel.Profile.IsActive();

        private bool _isExpanded = true;
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

        public bool Enabled
        {
            get => IsMain ? _profileViewModel.Profile.MainMappingsEnabled : (Model?.Enabled ?? false);
            set => SetEnabled(value);
        }

        private async void SetEnabled(bool enabled)
        {
            if (Enabled == enabled) return;
            bool changed;
            if (IsMain)
            {
                var profile = _profileViewModel.Profile;
                changed = await MappingGroup.ChangeEnabledAsync(profile, enabled,
                    () => profile.MainMappingsEnabled, value => profile.MainMappingsEnabled = value);
            }
            else
            {
                if (Model == null) return;
                changed = await Model.SetEnabledAsync(enabled);
            }

            // A synchronous rejection must update the checkbox after WPF finishes writing its value.
            if (!changed) await System.Threading.Tasks.Task.Yield();
            OnPropertyChanged(nameof(Enabled));
        }

        public bool BlockInputs
        {
            get => IsMain ? _profileViewModel.Profile.MainBlockInputs : Model?.BlockInputs == true;
            set
            {
                if (!CanEdit || BlockInputs == value) return;
                if (IsMain) _profileViewModel.Profile.MainBlockInputs = value;
                else if (Model != null) Model.BlockInputs = value;
                ApplyBlockingToMappings();
                _profileViewModel.Profile.Context?.ContextChanged();
                OnPropertyChanged();
            }
        }

        internal void ApplyBlockingToMappings()
        {
            foreach (var mapping in Mappings)
                foreach (var input in mapping.Mapping.DeviceBindings)
                {
                    if (!input.IsBound) continue;
                    input.SetBlock(BlockInputs && input.IsBlockable());
                }
        }

        internal MappingGroupViewModel(ProfileViewModel profileViewModel, MappingGroup model, bool isMain)
        {
            _profileViewModel = profileViewModel;
            Model = model;
            IsMain = isMain;
        }

        internal void RefreshActiveState()
        {
            OnPropertyChanged(nameof(CanEdit));
        }

        internal void RefreshTitle()
        {
            OnPropertyChanged(nameof(Title));
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
