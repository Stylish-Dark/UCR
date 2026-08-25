using System.Windows.Controls;
using HidWizards.UCR.ViewModels.Dialogs;

namespace HidWizards.UCR.Views.Dialogs
{
    public partial class AppearanceDialog : UserControl
    {
        public AppearanceDialog()
        {
            InitializeComponent();
            DataContext = new AppearanceDialogViewModel();
        }
    }
}
