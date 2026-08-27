using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using HidWizards.UCR.Core.Models;
using HidWizards.UCR.ViewModels.ProfileViewModels;
using HidWizards.UCR.ViewModels.Presentation;

namespace HidWizards.UCR.ViewModels.Dialogs
{
    public sealed class BatchDeviceOption
    {
        public Guid Guid { get; set; }
        public DeviceIoType IoType { get; set; }
        public string Title { get; set; }
        public DeviceVisualDescriptor Visual { get; set; }
        public string DisplayTitle => IoType + " — " + Title;
    }

    public sealed class BatchDeviceChangeResult
    {
        public int Changed { get; set; }
        public int ClearedAsIncompatible { get; set; }
        public int PreservedUnknown { get; set; }
    }

    public class BatchDeviceChangeDialogViewModel : INotifyPropertyChanged
    {
        private readonly ProfileViewModel _profileViewModel;
        private readonly List<BatchDeviceOption> _allDevices;

        public ObservableCollection<BatchDeviceOption> SourceDevices { get; }
        public ObservableCollection<BatchDeviceOption> TargetDevices { get; }
        public BatchDeviceChangeDialogViewModel ViewModel => this;

        private BatchDeviceOption _selectedSource;
        public BatchDeviceOption SelectedSource
        {
            get => _selectedSource;
            set
            {
                if (_selectedSource == value) return;
                _selectedSource = value;
                RebuildTargets();
                OnPropertyChanged(nameof(SelectedSource));
                OnPropertyChanged(nameof(CanApply));
            }
        }

        private BatchDeviceOption _selectedTarget;
        public BatchDeviceOption SelectedTarget
        {
            get => _selectedTarget;
            set
            {
                if (_selectedTarget == value) return;
                _selectedTarget = value;
                OnPropertyChanged(nameof(SelectedTarget));
                OnPropertyChanged(nameof(CanApply));
            }
        }

        public bool CanApply => SelectedSource != null && SelectedTarget != null;

        public BatchDeviceChangeDialogViewModel(ProfileViewModel profileViewModel)
        {
            _profileViewModel = profileViewModel;
            _allDevices = BuildAllDeviceOptions(profileViewModel.Profile);
            SourceDevices = new ObservableCollection<BatchDeviceOption>(BuildUsedDeviceOptions());
            TargetDevices = new ObservableCollection<BatchDeviceOption>();
            if (SourceDevices.Count > 0) SelectedSource = SourceDevices[0];
        }

        private static List<BatchDeviceOption> BuildAllDeviceOptions(Profile profile)
        {
            var result = new List<BatchDeviceOption>();
            foreach (var type in new[] { DeviceIoType.Input, DeviceIoType.Output })
            {
                foreach (var configuration in profile.GetDeviceConfigurationList(type))
                {
                    if (configuration == null) continue;
                    result.Add(new BatchDeviceOption
                    {
                        Guid = configuration.Guid,
                        IoType = type,
                        Title = configuration.GetFullTitleForProfile(profile),
                        Visual = DeviceVisualCatalog.Describe(configuration, profile, type)
                    });
                }
            }
            return result;
        }

        private IEnumerable<BatchDeviceOption> BuildUsedDeviceOptions()
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var binding in _profileViewModel.GetAllBindingViewModels())
            {
                if (binding?.DeviceBinding == null) continue;
                var key = binding.DeviceBinding.DeviceIoType + "|" + binding.DeviceBinding.DeviceConfigurationGuid;
                used.Add(key);
            }

            return _allDevices
                .Where(option => used.Contains(option.IoType + "|" + option.Guid))
                .OrderBy(option => option.IoType)
                .ThenBy(option => option.Title, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        private void RebuildTargets()
        {
            TargetDevices.Clear();
            SelectedTarget = null;
            if (SelectedSource == null) return;

            foreach (var option in _allDevices
                .Where(option => option.IoType == SelectedSource.IoType && option.Guid != SelectedSource.Guid)
                .OrderBy(option => option.Title, StringComparer.CurrentCultureIgnoreCase))
            {
                TargetDevices.Add(option);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
