using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using HidWizards.UCR.Core.Managers;
using HidWizards.UCR.Core.Utilities;
using HidWizards.UCR.Utilities;
using HidWizards.UCR.ViewModels.Dashboard;

namespace HidWizards.UCR.Views.Dialogs
{
    public partial class DeviceManagerDialog : Window
    {
        private bool _disposed;

        public DeviceManagerDialog(DevicesManager devicesManager)
        {
            DataContext = new DeviceManagerViewModel(devicesManager);
            InitializeComponent();
        }

        protected override void OnClosed(EventArgs e)
        {
            DisposeViewModel();
            base.OnClosed(e);
        }

        private void DeviceManagerWindow_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control) return;
            var scale = AppearanceManager.AdjustUiScale(e.Delta);
            Logger.Info("UI scale changed to " + Math.Round(scale * 100) + "%");
            e.Handled = true;
        }

        private void DeviceManagerWindow_OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape) return;
            Close();
            e.Handled = true;
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
            if (detected == null) return;
            DeviceList.UpdateLayout();
            DeviceList.ScrollIntoView(detected);
        }

        private void RemoveStale_OnClick(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as DeviceManagerViewModel;
            if (viewModel == null) return;
            var removed = viewModel.RemoveStaleCacheCopies();
            DarkMessageBox.Show(this,
                removed == 1 ? "Removed 1 stale cached device record." : "Removed " + removed + " stale cached device records.",
                "Device cache cleanup", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void RemoveFromUcr_OnClick(object sender, RoutedEventArgs e)
        {
            var item = (sender as FrameworkElement)?.DataContext as DeviceManagerItemViewModel;
            var viewModel = DataContext as DeviceManagerViewModel;
            if (item == null || viewModel == null) return;

            if (item.IsCachedOnly)
            {
                if (DarkMessageBox.Show(this, "Forget this cached/disconnected UCR device record?", "Forget device record",
                        MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

                if (!viewModel.ForgetCachedDevice(item, out var error))
                {
                    DarkMessageBox.Show(this, error, "Could not forget device", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                return;
            }

            if (DarkMessageBox.Show(this,
                    "Remove this live device from UCR selection lists for the rest of this session?\n\n" +
                    "This does not uninstall it from Windows and does not remove existing profile bindings.",
                    "Remove from UCR", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            viewModel.DismissLiveDevice(item);
        }

        private void RemoveFromWindows_OnClick(object sender, RoutedEventArgs e)
        {
            var item = (sender as FrameworkElement)?.DataContext as DeviceManagerItemViewModel;
            var viewModel = DataContext as DeviceManagerViewModel;
            if (item?.Device == null || viewModel == null) return;

            string instanceId;
            if (!DevicesManager.TryGetWindowsDeviceInstanceId(item.Device, out instanceId))
            {
                DarkMessageBox.Show(this,
                    "UCR cannot prove the exact Windows device instance for this row, so it will not guess.",
                    "Windows device unavailable", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = DarkMessageBox.Show(this,
                "Remove this exact device instance from Windows?\n\n" +
                instanceId + "\n\n" +
                "Windows will request administrator approval. The device may disconnect immediately and may be rediscovered if it is still attached.",
                "Remove device from Windows", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "pnputil.exe",
                    Arguments = "/remove-device \"" + instanceId.Replace("\"", string.Empty) + "\"",
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Normal
                };

                using (var process = Process.Start(startInfo))
                {
                    process?.WaitForExit();
                    if (process != null && process.ExitCode != 0)
                    {
                        DarkMessageBox.Show(this,
                            "Windows did not remove the device. pnputil returned exit code " + process.ExitCode + ".",
                            "Windows device removal", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }

                viewModel.Refresh();
            }
            catch (System.ComponentModel.Win32Exception exception)
            {
                // ERROR_CANCELLED (1223) is the ordinary UAC-cancel path. Keep it non-fatal.
                if (exception.NativeErrorCode != 1223)
                {
                    Logger.Error(exception, "Unable to remove Windows device instance: " + instanceId);
                    DarkMessageBox.Show(this, exception.Message, "Windows device removal", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "Unable to remove Windows device instance: " + instanceId);
                DarkMessageBox.Show(this, exception.Message, "Windows device removal", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OpenWindowsDevices_OnClick(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start("devmgmt.msc");
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "Unable to open Windows Device Manager");
                DarkMessageBox.Show(this, "UCR could not open Windows Device Manager.", "Windows Devices",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void Save_OnClick(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as DeviceManagerViewModel;
            if (viewModel == null) { Close(); return; }
            if (!viewModel.Apply(out var error))
            {
                DarkMessageBox.Show(this, error, "Device settings", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            Close();
        }

        private void Cancel_OnClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void DisposeViewModel()
        {
            if (_disposed) return;
            _disposed = true;
            (DataContext as DeviceManagerViewModel)?.Dispose();
        }
    }
}
