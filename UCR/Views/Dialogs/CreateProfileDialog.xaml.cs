using System.Windows;
using System.Windows.Controls;
using HidWizards.UCR.ViewModels.Dashboard;
using HidWizards.UCR.ViewModels.Dialogs;

namespace HidWizards.UCR.Views.Dialogs
{
    public partial class CreateProfileDialog : UserControl
    {
        private CreateProfileDialogViewModel ViewModel { get; set; }

        public CreateProfileDialog(string title)
        {
            ViewModel = new CreateProfileDialogViewModel(title);
            DataContext = ViewModel;
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            TextValue.SelectAll();
        }
    }
}
