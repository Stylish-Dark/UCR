using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HidWizards.UCR.Core.Managers;
using HidWizards.UCR.ViewModels.Dashboard;

namespace HidWizards.UCR.Views.Dialogs
{
    public partial class DeviceManagerDialog : UserControl
    {
        public DeviceManagerDialog(DevicesManager devicesManager)
        {
            DataContext = new DeviceManagerViewModel(devicesManager);
            InitializeComponent();
            Unloaded += OnUnloaded;
        }

        private void MoveUp_OnClick(object sender, RoutedEventArgs e)
        {
            var item = (sender as FrameworkElement)?.DataContext as DeviceManagerItemViewModel;
            (DataContext as DeviceManagerViewModel)?.Move(item, -1);
        }

        private void MoveDown_OnClick(object sender, RoutedEventArgs e)
        {
            var item = (sender as FrameworkElement)?.DataContext as DeviceManagerItemViewModel;
            (DataContext as DeviceManagerViewModel)?.Move(item, 1);
        }

        private async void DetectDevice_OnClick(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as DeviceManagerViewModel;
            if (viewModel == null) return;

            Keyboard.ClearFocus();
            var detected = await viewModel.DetectInputDeviceAsync();
            if (detected != null)
            {
                DeviceList.UpdateLayout();
                DeviceList.ScrollIntoView(detected);
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            (DataContext as DeviceManagerViewModel)?.Dispose();
        }
    }
}
