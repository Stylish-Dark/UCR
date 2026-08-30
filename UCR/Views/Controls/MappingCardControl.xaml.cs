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
        private const double CompactFilterBadgeThreshold = 620.0;
        private static readonly GridLength FullFilterColumnWidth = new GridLength(220.0);
        private static readonly GridLength CompactFilterColumnWidth = new GridLength(54.0);

        public static readonly DependencyProperty UseCompactFilterBadgesProperty = DependencyProperty.Register(
            nameof(UseCompactFilterBadges), typeof(bool), typeof(MappingCardControl), new PropertyMetadata(false));

        public static readonly DependencyProperty FilterColumnWidthProperty = DependencyProperty.Register(
            nameof(FilterColumnWidth), typeof(GridLength), typeof(MappingCardControl),
            new PropertyMetadata(FullFilterColumnWidth));

        public bool UseCompactFilterBadges
        {
            get => (bool)GetValue(UseCompactFilterBadgesProperty);
            private set => SetValue(UseCompactFilterBadgesProperty, value);
        }

        public GridLength FilterColumnWidth
        {
            get => (GridLength)GetValue(FilterColumnWidthProperty);
            private set => SetValue(FilterColumnWidthProperty, value);
        }

        public MappingCardControl()
        {
            InitializeComponent();
            Loaded += MappingCardControl_OnLoaded;
            SizeChanged += MappingCardControl_OnSizeChanged;
        }

        private void MappingCardControl_OnLoaded(object sender, RoutedEventArgs e)
        {
            UpdateResponsiveHeader();
        }

        private void MappingCardControl_OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateResponsiveHeader();
        }

        private void UpdateResponsiveHeader()
        {
            var compact = ActualWidth > 0 && ActualWidth < CompactFilterBadgeThreshold;
            UseCompactFilterBadges = compact;
            FilterColumnWidth = compact ? CompactFilterColumnWidth : FullFilterColumnWidth;
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
            var placementTarget = sender as FrameworkElement;
            if (mappingViewModel == null || descriptor == null || placementTarget == null || !mappingViewModel.ButtonsEnabled) return;

            e.Handled = true;
            try
            {
                // Keyboard outputs are fastest to choose by pressing the desired key. For every
                // other output type keep the explicit control picker; an input press must never
                // be interpreted as a controller/mouse output selection.
                if (mappingViewModel.UsesPressCaptureForQuickOutput(descriptor))
                {
                    await mappingViewModel.QuickBindOutputAsync(descriptor);
                    return;
                }

                ShowQuickOutputMenu(mappingViewModel, descriptor, placementTarget);
            }
            catch (Exception exception)
            {
                Logger.Error("Failed to start mapping-card quick output bind", exception);
                DarkMessageBox.Show("UCR could not bind this output control. The error has been written to the log.",
                    "Unable to bind output", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static void ShowQuickOutputMenu(MappingViewModel mappingViewModel,
            BindingVisualDescriptor descriptor, FrameworkElement placementTarget)
        {
            var options = mappingViewModel.GetQuickOutputBindingOptions(descriptor);
            var menu = CreateDarkContextMenu(placementTarget);

            if (options.Count == 0)
            {
                menu.Items.Add(new MenuItem
                {
                    Header = "No compatible controls",
                    IsEnabled = false,
                    Foreground = Brushes.Gray,
                    Background = Brushes.Transparent,
                    Padding = new Thickness(10, 6, 14, 6)
                });
            }
            else
            {
                foreach (var option in options)
                {
                    var capturedOption = option;
                    var header = new StackPanel { Orientation = Orientation.Horizontal };
                    var visual = capturedOption.Visual;
                    header.Children.Add(new ControlGlyphControl
                    {
                        Width = 46,
                        Height = 27,
                        Margin = new Thickness(0, 0, 8, 0),
                        Kind = visual?.ControlKind ?? ControlVisualKind.Unknown,
                        AccentBrush = visual?.ControlBrush ?? Brushes.Gray,
                        Label = visual?.ControlLabel ?? "?"
                    });
                    header.Children.Add(new TextBlock
                    {
                        Text = capturedOption.Title,
                        VerticalAlignment = VerticalAlignment.Center,
                        Foreground = Brushes.White
                    });

                    var item = new MenuItem
                    {
                        Header = header,
                        ToolTip = visual?.ToolTip,
                        Foreground = Brushes.White,
                        Background = Brushes.Transparent,
                        Padding = new Thickness(8, 4, 12, 4)
                    };
                    item.Click += (clickSender, clickArgs) =>
                        mappingViewModel.ApplyQuickOutputBinding(descriptor, capturedOption);
                    menu.Items.Add(item);
                }
            }

            menu.IsOpen = true;
        }

        private static ContextMenu CreateDarkContextMenu(FrameworkElement placementTarget)
        {
            return new ContextMenu
            {
                PlacementTarget = placementTarget,
                Placement = PlacementMode.MousePoint,
                Background = new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x24)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x45, 0x45, 0x45)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(4)
            };
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
