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
        public bool IsAvailable { get; set; }
        public int ReferenceCount { get; set; }
        public string AvailabilityText => IsAvailable ? IoType.ToString() : IoType + " — UNAVAILABLE";
        public string DisplayTitle => IoType + " — " + Title + (IsAvailable ? string.Empty : " — Unavailable");
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
                    var available = configuration.Device != null &&
                                    profile.Context.DevicesManager.ResolveDevice(configuration.Device, type) != null;
                    result.Add(new BatchDeviceOption
                    {
                        Guid = configuration.Guid,
                        IoType = type,
                        Title = configuration.GetFullTitleForProfile(profile),
                        Visual = DeviceVisualCatalog.Describe(configuration, profile, type),
                        IsAvailable = available
                    });
                }
            }
            return result;
        }

        private IEnumerable<BatchDeviceOption> BuildUsedDeviceOptions()
        {
            var used = _profileViewModel.GetAllBindingViewModels()
                .Where(binding => binding?.DeviceBinding != null)
                .GroupBy(binding => new
                {
                    binding.DeviceBinding.DeviceIoType,
                    binding.DeviceBinding.DeviceConfigurationGuid
                })
                .ToList();

            var result = new List<BatchDeviceOption>();
            foreach (var group in used)
            {
                var option = _allDevices.FirstOrDefault(candidate =>
                    candidate.IoType == group.Key.DeviceIoType && candidate.Guid == group.Key.DeviceConfigurationGuid);
                if (option == null)
                {
                    option = BuildMissingUsedDeviceOption(group.Key.DeviceIoType,
                        group.Key.DeviceConfigurationGuid, group.Count());
                    _allDevices.Add(option);
                }
                else
                {
                    option.ReferenceCount = group.Count();
                }
                result.Add(option);
            }

            return result
                .OrderBy(option => option.IoType)
                .ThenBy(option => option.IsAvailable ? 0 : 1)
                .ThenBy(option => option.Title, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        private BatchDeviceOption BuildMissingUsedDeviceOption(DeviceIoType ioType, Guid configurationGuid, int referenceCount)
        {
            var isUnassigned = configurationGuid == Guid.Empty;
            return new BatchDeviceOption
            {
                Guid = configurationGuid,
                IoType = ioType,
                Title = isUnassigned ? "Unassigned " + ioType.ToString().ToLowerInvariant() + " device" : "Unavailable device",
                Visual = DeviceVisualCatalog.Describe((DeviceConfiguration)null, _profileViewModel.Profile, ioType),
                IsAvailable = false,
                ReferenceCount = referenceCount
            };
        }

        private void RebuildTargets()
        {
            TargetDevices.Clear();
            SelectedTarget = null;
            if (SelectedSource == null) return;

            foreach (var option in _allDevices
                .Where(option => option.IoType == SelectedSource.IoType && option.Guid != SelectedSource.Guid && option.IsAvailable)
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
