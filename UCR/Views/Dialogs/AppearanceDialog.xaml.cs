using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HidWizards.UCR.Core.Utilities;
using HidWizards.UCR.Utilities;
using HidWizards.UCR.ViewModels.Dialogs;

namespace HidWizards.UCR.Views.Dialogs
{
    public partial class AppearanceDialog : UserControl
    {
        public event EventHandler AccentSelected;
        public event EventHandler CancelRequested;

        public AppearanceDialog()
        {
            InitializeComponent();
            Refresh();
        }

        public void Refresh()
        {
            DataContext = new AppearanceDialogViewModel();
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                CancelRequested?.Invoke(this, EventArgs.Empty);
                return;
            }

            base.OnPreviewKeyDown(e);
        }

        private void AccentSwatch_OnClick(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var option = button?.DataContext as AccentOptionViewModel;
            if (option == null) return;

            AppearanceManager.ApplyAccent(option.Name);
            Logger.Info("Appearance accent changed to: " + option.Name);
            AccentSelected?.Invoke(this, EventArgs.Empty);
        }
    }
}
