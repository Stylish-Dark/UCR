using System.Windows;
using System.Windows.Controls;
using HidWizards.UCR.Core.Utilities;
using HidWizards.UCR.Utilities;
using HidWizards.UCR.ViewModels.Dialogs;
using MaterialDesignThemes.Wpf;

namespace HidWizards.UCR.Views.Dialogs
{
    public partial class AppearanceDialog : UserControl
    {
        public AppearanceDialog()
        {
            InitializeComponent();
            DataContext = new AppearanceDialogViewModel();
        }

        private void AccentSwatch_OnClick(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var option = button?.DataContext as AccentOptionViewModel;
            if (option == null) return;

            AppearanceManager.ApplyAccent(option.Name);
            Logger.Info("Appearance accent changed to: " + option.Name);
            DialogHost.CloseDialogCommand.Execute(null, button);
        }
    }
}
