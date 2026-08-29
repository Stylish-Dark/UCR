using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using HidWizards.UCR.Core.Utilities;
using HidWizards.UCR.Utilities;
using HidWizards.UCR.ViewModels.Presentation;
using HidWizards.UCR.ViewModels.ProfileViewModels;

namespace HidWizards.UCR.Views.Controls
{
    public partial class MappingCardControl : UserControl
    {
        public MappingCardControl()
        {
            InitializeComponent();
        }


        private void MoveUp_OnClick(object sender, RoutedEventArgs e)
        {
            (DataContext as MappingViewModel)?.MoveUp();
        }

        private void MoveDown_OnClick(object sender, RoutedEventArgs e)
        {
            (DataContext as MappingViewModel)?.MoveDown();
        }

        private void Remove_OnClick(object sender, RoutedEventArgs e)
        {
            var mappingViewModel = DataContext as MappingViewModel;
            mappingViewModel?.Remove();
        }

        private void Rename_OnClick(object sender, RoutedEventArgs e)
        {
            var mappingViewModel = DataContext as MappingViewModel;
            mappingViewModel?.Rename();
        }

        private void RenameHeader_OnMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            var mappingViewModel = DataContext as MappingViewModel;
            if (mappingViewModel == null || !mappingViewModel.ButtonsEnabled) return;
            e.Handled = true;
            mappingViewModel.Rename();
        }

        private void QuickBindInput_OnMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            var mappingViewModel = DataContext as MappingViewModel;
            var descriptor = FindBindingDescriptor(e.OriginalSource as DependencyObject);
            if (mappingViewModel == null || descriptor == null || !mappingViewModel.ButtonsEnabled) return;

            e.Handled = true;
            try
            {
                mappingViewModel.QuickBindInput(descriptor);
            }
            catch (Exception exception)
            {
                Logger.Error("Failed to start mapping-card quick input bind", exception);
                DarkMessageBox.Show("UCR could not start input detection for this binding. The error has been written to the log.",
                    "Unable to bind input", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void QuickBindOutput_OnMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            var mappingViewModel = DataContext as MappingViewModel;
            var descriptor = FindBindingDescriptor(e.OriginalSource as DependencyObject);
            if (mappingViewModel == null || descriptor == null || !mappingViewModel.ButtonsEnabled) return;

            e.Handled = true;
            try
            {
                await mappingViewModel.QuickBindOutputAsync(descriptor);
            }
            catch (Exception exception)
            {
                Logger.Error("Failed to start mapping-card quick output bind", exception);
                DarkMessageBox.Show("UCR could not capture an input for this output binding. The error has been written to the log.",
                    "Unable to bind output", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static BindingVisualDescriptor FindBindingDescriptor(DependencyObject source)
        {
            var current = source;
            while (current != null)
            {
                var element = current as FrameworkElement;
                var descriptor = element?.DataContext as BindingVisualDescriptor;
                if (descriptor != null) return descriptor;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        private void AddPlugin_OnClick(object sender, RoutedEventArgs e)
        {
            var mappingViewModel = DataContext as MappingViewModel;
            var button = sender as Button;
            if (mappingViewModel == null || button == null) return;

            var options = mappingViewModel.GetCompatiblePluginOptions();
            if (options.Count == 0) return;

            var menu = new ContextMenu
            {
                PlacementTarget = button,
                Placement = PlacementMode.Bottom,
                Background = new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x24)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x45, 0x45, 0x45)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(4)
            };

            foreach (var option in options)
            {
                var capturedOption = option;
                var item = new MenuItem
                {
                    Header = capturedOption.MenuLabel,
                    ToolTip = capturedOption.Description,
                    Foreground = Brushes.White,
                    Background = Brushes.Transparent,
                    Padding = new Thickness(10, 6, 14, 6)
                };
                item.Click += (clickSender, clickArgs) => mappingViewModel.AddPlugin(capturedOption.Plugin);
                menu.Items.Add(item);
            }

            menu.Closed += (closedSender, closedArgs) =>
            {
                if (ReferenceEquals(button.ContextMenu, menu)) button.ContextMenu = null;
            };
            button.ContextMenu = menu;
            menu.IsOpen = true;
        }
    }
}
