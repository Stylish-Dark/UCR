using System.Windows.Controls;
using HidWizards.UCR.ViewModels.Dialogs;
using HidWizards.UCR.ViewModels.ProfileViewModels;

namespace HidWizards.UCR.Views.Dialogs
{
    public partial class BatchDeviceChangeDialog : UserControl
    {
        public BatchDeviceChangeDialog(ProfileViewModel profileViewModel)
        {
            DataContext = new BatchDeviceChangeDialogViewModel(profileViewModel);
            InitializeComponent();
        }
    }
}
