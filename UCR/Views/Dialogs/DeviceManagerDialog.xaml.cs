using System.Windows;
using System.Windows.Controls;
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
    }
}
