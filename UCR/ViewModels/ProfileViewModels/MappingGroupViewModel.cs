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
        public string Title => IsMain ? "Main" : Model?.Title ?? "Group";
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
            get => IsMain || (Model?.Enabled ?? false);
            set
            {
                if (IsMain || Model == null || !CanEdit || Model.Enabled == value) return;
                Model.Enabled = value;
                OnPropertyChanged();
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
