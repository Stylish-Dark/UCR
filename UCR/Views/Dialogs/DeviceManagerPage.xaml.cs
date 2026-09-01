using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using HidWizards.UCR.Core.Managers;
using HidWizards.UCR.Core.Utilities;
using HidWizards.UCR.Utilities;
using HidWizards.UCR.ViewModels.Dashboard;

namespace HidWizards.UCR.Views.Dialogs
{
    public partial class DeviceManagerPage : UserControl, IDisposable
    {
        private bool _disposed;

        // Test-only construction path. Production always supplies DevicesManager below.
        public DeviceManagerPage()
        {
            InitializeComponent();
        }

        public DeviceManagerPage(DevicesManager devicesManager)
        {
            // Keep the proven pre-regression ordering: bindings see the real view model while XAML is built.
            DataContext = new DeviceManagerViewModel(devicesManager);
            InitializeComponent();
        }

        public event EventHandler BackRequested;

        public void Dispose()
        {
            DisposeViewModel();
        }

        private void Back_OnClick(object sender, RoutedEventArgs e)
        {
            if (!TryApply()) return;
            BackRequested?.Invoke(this, EventArgs.Empty);
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
            BackRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }

        private void AliasTextBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            var textBox = sender as TextBox;
            if (textBox == null || (Keyboard.Modifiers & ModifierKeys.Alt) != ModifierKeys.Alt) return;

            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            var offset = key == Key.Up ? -1 : key == Key.Down ? 1 : 0;
            if (offset == 0) return;
            e.Handled = true;

            var item = textBox.DataContext as DeviceManagerItemViewModel;
            var viewModel = DataContext as DeviceManagerViewModel;
            if (item == null || viewModel == null) return;

            // Capture the selection before ObservableCollection.Move makes WPF rebuild/recycle the row.
            var selectionStart = textBox.SelectionStart;
            var selectionLength = textBox.SelectionLength;
            if (!viewModel.Move(item, offset)) return;

            viewModel.SelectedDevice = item;
            RestoreAliasFocus(item, selectionStart, selectionLength);
        }

        private void RestoreAliasFocus(DeviceManagerItemViewModel item, int selectionStart, int selectionLength, int attemptsRemaining = 2)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                DeviceList.ScrollIntoView(item);
                DeviceList.UpdateLayout();

                var container = DeviceList.ItemContainerGenerator.ContainerFromItem(item) as ListViewItem;
                var target = FindVisualChild<TextBox>(container);
                if (target == null)
                {
                    if (attemptsRemaining > 0)
                        RestoreAliasFocus(item, selectionStart, selectionLength, attemptsRemaining - 1);
                    return;
                }

                var length = target.Text?.Length ?? 0;
                var start = Math.Max(0, Math.Min(selectionStart, length));
                var selected = Math.Max(0, Math.Min(selectionLength, length - start));

                target.Focus();
                Keyboard.Focus(target);
                target.Select(start, selected);
            }));
        }

        private static T FindVisualChild<T>(DependencyObject root) where T : DependencyObject
        {
            if (root == null) return null;
            var childCount = VisualTreeHelper.GetChildrenCount(root);
            for (var index = 0; index < childCount; index++)
            {
                var child = VisualTreeHelper.GetChild(root, index);
                var typed = child as T;
                if (typed != null) return typed;
                var nested = FindVisualChild<T>(child);
                if (nested != null) return nested;
            }
            return null;
        }

        private void OutlineColorButton_OnClick(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var device = button?.DataContext as DeviceManagerItemViewModel;
            if (button == null || device == null || device.AvailableOutlineColors == null) return;

            // Build the palette only after the click. Nothing picker-specific lives in the ListView
            // row template, so a picker failure cannot prevent device rows from being created.
            var strip = new StackPanel { Orientation = Orientation.Horizontal };
            var popup = new System.Windows.Controls.Primitives.Popup
            {
                PlacementTarget = button,
                Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
                StaysOpen = false,
                AllowsTransparency = true,
                PopupAnimation = System.Windows.Controls.Primitives.PopupAnimation.Fade
            };

            var shell = new Border
            {
                Margin = new Thickness(0, 4, 0, 0),
                Padding = new Thickness(6),
                CornerRadius = new CornerRadius(5),
                Background = new SolidColorBrush(Color.FromRgb(37, 37, 37)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(85, 85, 85)),
                BorderThickness = new Thickness(1),
                Child = strip
            };

            foreach (var choice in device.AvailableOutlineColors)
            {
                var selectedChoice = choice;
                var tile = new Border
                {
                    Width = 28,
                    Height = 28,
                    Margin = new Thickness(0, 0, 5, 0),
                    Padding = new Thickness(5),
                    CornerRadius = new CornerRadius(4),
                    Background = new SolidColorBrush(Color.FromRgb(37, 37, 37)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
                    BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand,
                    ToolTip = selectedChoice.ToolTip
                };

                var swatch = new Grid { Width = 16, Height = 16 };
                swatch.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(2),
                    Background = selectedChoice.Brush,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(138, 138, 138)),
                    BorderThickness = new Thickness(1)
                });

                if (selectedChoice.IsDefault)
                {
                    swatch.Children.Add(new System.Windows.Shapes.Ellipse
                    {
                        Width = 6,
                        Height = 6,
                        Fill = new SolidColorBrush(Color.FromRgb(32, 32, 32)),
                        Stroke = new SolidColorBrush(Color.FromRgb(242, 242, 242)),
                        StrokeThickness = 1,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    });
                }

                tile.Child = swatch;
                tile.MouseEnter += (o, args) => tile.Background = new SolidColorBrush(Color.FromRgb(52, 52, 52));
                tile.MouseLeave += (o, args) => tile.Background = new SolidColorBrush(Color.FromRgb(37, 37, 37));
                tile.MouseLeftButtonUp += (o, args) =>
                {
                    device.OutlineColor = selectedChoice.Value;
                    popup.IsOpen = false;
                    args.Handled = true;
                };
                strip.Children.Add(tile);
            }

            popup.Child = shell;
            popup.IsOpen = true;
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

        private void RemoveFromUcr_OnClick(object sender, RoutedEventArgs e)
        {
            var item = (sender as FrameworkElement)?.DataContext as DeviceManagerItemViewModel;
            var viewModel = DataContext as DeviceManagerViewModel;
            if (item == null || viewModel == null || !item.CanRemoveFromUcr) return;

            if (DarkMessageBox.Show(Window.GetWindow(this),
                    "Remove this input device from UCR?\n\n" +
                    "This does not uninstall it from Windows and does not remove existing profile bindings. " +
                    "Use Detect Device whenever you want to add it back.",
                    "Remove from UCR", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            viewModel.RemoveFromUcr(item);
        }

        private void RemoveFromWindows_OnClick(object sender, RoutedEventArgs e)
        {
            var item = (sender as FrameworkElement)?.DataContext as DeviceManagerItemViewModel;
            var viewModel = DataContext as DeviceManagerViewModel;
            if (item?.Device == null || viewModel == null) return;

            string instanceId;
            if (!DevicesManager.TryGetWindowsDeviceInstanceId(item.Device, out instanceId))
            {
                DarkMessageBox.Show(Window.GetWindow(this),
                    "UCR cannot prove the exact Windows device instance for this row, so it will not guess.",
                    "Windows device unavailable", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = DarkMessageBox.Show(Window.GetWindow(this),
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
                        DarkMessageBox.Show(Window.GetWindow(this),
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
                    Logger.Error("Unable to remove Windows device instance: " + instanceId, exception);
                    DarkMessageBox.Show(Window.GetWindow(this), exception.Message, "Windows device removal", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception exception)
            {
                Logger.Error("Unable to remove Windows device instance: " + instanceId, exception);
                DarkMessageBox.Show(Window.GetWindow(this), exception.Message, "Windows device removal", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                Logger.Error("Unable to open Windows Device Manager", exception);
                DarkMessageBox.Show(Window.GetWindow(this), "UCR could not open Windows Device Manager.", "Windows Devices",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private bool TryApply()
        {
            var viewModel = DataContext as DeviceManagerViewModel;
            if (viewModel == null) return true;
            if (viewModel.Apply(out var error)) return true;
            DarkMessageBox.Show(Window.GetWindow(this), error, "Device settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        private void Save_OnClick(object sender, RoutedEventArgs e)
        {
            if (!TryApply()) return;
            BackRequested?.Invoke(this, EventArgs.Empty);
        }

        private void Cancel_OnClick(object sender, RoutedEventArgs e)
        {
            BackRequested?.Invoke(this, EventArgs.Empty);
        }

        private void DisposeViewModel()
        {
            if (_disposed) return;
            _disposed = true;
            (DataContext as DeviceManagerViewModel)?.Dispose();
        }
    }
}
