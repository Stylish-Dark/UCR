using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
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
