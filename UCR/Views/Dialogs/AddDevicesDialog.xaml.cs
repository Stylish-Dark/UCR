using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HidWizards.UCR.Core.Managers;
using HidWizards.UCR.Core.Models;
using HidWizards.UCR.ViewModels.Dashboard;

namespace HidWizards.UCR.Views.Dialogs
{
    public partial class AddDevicesDialog : UserControl
    {
        public AddDevicesDialog(List<Device> devices, DeviceIoType deviceIoType, DevicesManager devicesManager)
        {
            DataContext = new AddDevicesDialogViewModel(devices, deviceIoType, devicesManager);
            InitializeComponent();
            Unloaded += OnUnloaded;
        }

        private async void DetectDevice_OnClick(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as AddDevicesDialogViewModel;
            if (viewModel == null) return;

            Keyboard.ClearFocus();
            var detected = await viewModel.DetectDeviceAsync();
            if (detected != null) DeviceSelector.BringDeviceIntoView(detected);
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            (DataContext as AddDevicesDialogViewModel)?.Dispose();
        }
    }
}
