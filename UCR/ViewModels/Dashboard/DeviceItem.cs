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

        public event PropertyChangedEventHandler PropertyChanged;

        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
