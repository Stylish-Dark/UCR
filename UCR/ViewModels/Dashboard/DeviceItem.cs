using System.ComponentModel;
using System.Runtime.CompilerServices;
using HidWizards.UCR.Core.Annotations;
using HidWizards.UCR.Core.Models;
using HidWizards.UCR.ViewModels.Presentation;

namespace HidWizards.UCR.ViewModels.Dashboard
{
    public class DeviceItem : INotifyPropertyChanged
    {
        public string Title => DeviceConfiguration.GetFullTitleForProfile(Profile);
        public string ProviderName => DeviceConfiguration.Device.ProviderName;
        public DeviceVisualDescriptor Visual => DeviceVisualCatalog.Describe(DeviceConfiguration, Profile, DeviceIoType);
        public bool IsInput => DeviceIoType == DeviceIoType.Input;
        public bool CanEditConfiguration => Profile?.IsActive() != true;
        public bool BlockUnmappedInputs
        {
            get => IsInput && DeviceConfiguration?.BlockUnmappedInputs == true;
            set
            {
                if (!IsInput || DeviceConfiguration == null || DeviceConfiguration.BlockUnmappedInputs == value) return;
                DeviceConfiguration.ChangeBlockUnmappedInputs(value);
                OnPropertyChanged();
            }
        }
        public bool IsPrimary => Profile?.GetPrimaryDeviceConfiguration(DeviceIoType)?.Guid == DeviceConfiguration.Guid;
        public string PrimaryToolTip => IsPrimary
            ? $"Primary {DeviceIoType.ToString().ToLowerInvariant()} device"
            : $"Make primary {DeviceIoType.ToString().ToLowerInvariant()} device";

        public DeviceConfiguration DeviceConfiguration { get; set; }
        private Profile Profile { get; set; }
        private DeviceIoType DeviceIoType { get; set; }

        public DeviceItem(DeviceConfiguration deviceConfiguration, Profile profile, DeviceIoType deviceIoType = DeviceIoType.Input)
        {
            DeviceConfiguration = deviceConfiguration;
            Profile = profile;
            DeviceIoType = deviceIoType;
        }

        public void TitleChanged()
        {
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(Visual));
        }

        public void PrimaryChanged()
        {
            OnPropertyChanged(nameof(IsPrimary));
            OnPropertyChanged(nameof(PrimaryToolTip));
        }

        public void ActiveStateChanged()
        {
            OnPropertyChanged(nameof(CanEditConfiguration));
        }

        public event PropertyChangedEventHandler PropertyChanged;

        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
