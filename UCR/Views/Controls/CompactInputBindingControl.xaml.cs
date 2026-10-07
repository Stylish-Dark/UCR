using System;
using System.Windows;
using System.Windows.Controls;
using HidWizards.UCR.Core.Models;
using HidWizards.UCR.Core.Models.Binding;
using HidWizards.UCR.Core.Utilities;
using HidWizards.UCR.ViewModels;
using HidWizards.UCR.ViewModels.ProfileViewModels;

namespace HidWizards.UCR.Views.Controls
{
    public partial class CompactInputBindingControl : UserControl
    {
        public static readonly DependencyProperty DeviceBindingProperty =
            DependencyProperty.Register("DeviceBinding", typeof(DeviceBinding), typeof(CompactInputBindingControl),
                new PropertyMetadata(default(DeviceBinding)));

        private bool _loaded;

        public CompactInputBindingControl()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        public DeviceBinding DeviceBinding
        {
            get => (DeviceBinding)GetValue(DeviceBindingProperty);
            set => SetValue(DeviceBindingProperty, value);
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as DeviceBindingViewModel;
            viewModel?.EnsureDeviceListLoaded();
            _loaded = true;
            BuildContextMenu();
        }

        private void DeviceSelectionBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_loaded) return;
            var selected = DeviceSelectionBox.SelectedItem as ComboBoxItemViewModel;
            var viewModel = DataContext as DeviceBindingViewModel;
            if (selected == null || viewModel == null) return;

            viewModel.ChangeDeviceConfiguration(selected.Value);
            BuildContextMenu();
        }

        private void BindButton_OnClick(object sender, RoutedEventArgs e)
        {
            if (DeviceBinding == null || DeviceBinding.IsInBindMode) return;
            try
            {
                DeviceBinding.DeviceBindingCategory = DeviceBindingCategory.Momentary;
                DeviceBinding.EnterBindMode();
            }
            catch (Exception exception)
            {
                Logger.Error("Failed to enter compact input bind mode", exception);
                HidWizards.UCR.Utilities.DarkMessageBox.Show(
                    "UCR could not start input detection for this binding. The error has been written to the log.",
                    "Unable to bind input", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MenuButton_OnClick(object sender, RoutedEventArgs e)
        {
            BuildContextMenu();
            Ddl.PlacementTarget = MenuButton;
            Ddl.IsOpen = true;
        }

        private void BuildContextMenu()
        {
            if (Ddl == null) return;
            Ddl.Items.Clear();

            var configuration = GetSelectedDeviceConfiguration();
            if (configuration?.Device != null && DeviceBinding?.Profile?.Context != null)
            {
                foreach (var node in configuration.Device.GetDeviceBindingMenu(
                    DeviceBinding.Profile.Context, DeviceBinding.DeviceIoType))
                {
                    var item = BuildMenuItem(node, configuration.Guid);
                    if (item != null) Ddl.Items.Add(item);
                }
            }

            if (Ddl.Items.Count > 0) Ddl.Items.Add(new Separator());

            var notItem = new MenuItem
            {
                Header = "Negate input (NOT)",
                IsCheckable = true,
                IsChecked = DeviceBinding?.InputExpressionNegated == true,
                Foreground = System.Windows.Media.Brushes.White
            };
            notItem.Click += (clickSender, clickArgs) =>
            {
                if (DeviceBinding == null) return;
                DeviceBinding.SetInputExpressionNegated(notItem.IsChecked);
            };
            Ddl.Items.Add(notItem);

            var blockItem = new MenuItem
            {
                Header = "Block original input",
                IsCheckable = true,
                IsChecked = DeviceBinding?.Block == true,
                IsEnabled = DeviceBinding?.IsBlockable() == true,
                Foreground = System.Windows.Media.Brushes.White
            };
            blockItem.Click += (clickSender, clickArgs) =>
            {
                if (DeviceBinding != null) DeviceBinding.SetBlock(blockItem.IsChecked);
            };
            Ddl.Items.Add(blockItem);

            var clearItem = new MenuItem
            {
                Header = "Clear binding",
                Foreground = System.Windows.Media.Brushes.White
            };
            clearItem.Click += (clickSender, clickArgs) => DeviceBinding?.ClearBinding();
            Ddl.Items.Add(clearItem);
        }

        private MenuItem BuildMenuItem(DeviceBindingNode node, Guid configurationGuid)
        {
            if (node == null) return null;

            var item = new MenuItem
            {
                Header = node.Title,
                Foreground = System.Windows.Media.Brushes.White
            };

            if (node.IsBinding)
            {
                var info = node.DeviceBindingInfo;
                if (info == null || info.DeviceBindingCategory != DeviceBindingCategory.Momentary) return null;
                item.Click += (sender, args) =>
                {
                    DeviceBinding.SetDeviceConfigurationGuid(configurationGuid);
                    DeviceBinding.DeviceBindingCategory = DeviceBindingCategory.Momentary;
                    DeviceBinding.SetKeyTypeValue(info.KeyType, info.KeyValue, info.KeySubValue);
                };
                return item;
            }

            foreach (var child in node.ChildrenNodes ?? new System.Collections.Generic.List<DeviceBindingNode>())
            {
                var childItem = BuildMenuItem(child, configurationGuid);
                if (childItem != null) item.Items.Add(childItem);
            }

            return item.Items.Count == 0 ? null : item;
        }

        private DeviceConfiguration GetSelectedDeviceConfiguration()
        {
            var selected = DeviceSelectionBox?.SelectedItem as ComboBoxItemViewModel;
            if (selected == null || DeviceBinding?.Profile == null) return null;
            return DeviceBinding.Profile.GetDeviceConfiguration(DeviceIoType.Input, selected.Value);
        }
    }
}
