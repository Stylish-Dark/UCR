using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
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
            Loaded += MappingCardControl_OnLoaded;
        }

        private void MappingCardControl_OnLoaded(object sender, RoutedEventArgs e)
        {
            var mapping = DataContext as MappingViewModel;
            if (mapping?.IsExpanded == true)
            {
                ShowExpandedBody(false);
            }
            else
            {
                CollapseExpandedBody(false);
            }
        }

        private void MappingExpander_OnExpanded(object sender, RoutedEventArgs e)
        {
            ShowExpandedBody(true);
        }

        private void MappingExpander_OnCollapsed(object sender, RoutedEventArgs e)
        {
            CollapseExpandedBody(true);
        }

        private void ShowExpandedBody(bool animate)
        {
            if (ExpandedBodyHost == null) return;

            ExpandedBodyHost.BeginAnimation(HeightProperty, null);
            ExpandedBodyHost.BeginAnimation(OpacityProperty, null);
            if (ExpandedBodyHost.ContentTemplate == null)
                ExpandedBodyHost.ContentTemplate = FindResource("ExpandedMappingBodyTemplate") as DataTemplate;

            ExpandedBodyHost.Visibility = Visibility.Visible;
            ExpandedBodyHost.Opacity = animate ? 0 : 1;
            ExpandedBodyHost.Height = double.NaN;

            if (!animate) return;

            // Measure synchronously before WPF paints this frame, then animate from zero to the real
            // editor height. Once complete, return to Auto so validation rows can grow naturally.
            var width = Math.Max(1.0, ActualWidth);
            ExpandedBodyHost.Measure(new Size(width, double.PositiveInfinity));
            var targetHeight = Math.Max(0.0, ExpandedBodyHost.DesiredSize.Height);
            if (targetHeight <= 0.5)
            {
                ExpandedBodyHost.Height = double.NaN;
                ExpandedBodyHost.Opacity = 1;
                return;
            }

            ExpandedBodyHost.Height = 0;
            var duration = TimeSpan.FromMilliseconds(145);
            var heightAnimation = new DoubleAnimation(0, targetHeight, duration)
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            };
            heightAnimation.Completed += (completedSender, completedArgs) =>
            {
                if (MappingExpander?.IsExpanded != true) return;
                ExpandedBodyHost.BeginAnimation(HeightProperty, null);
                ExpandedBodyHost.Height = double.NaN;
                ExpandedBodyHost.Opacity = 1;
            };

            ExpandedBodyHost.BeginAnimation(HeightProperty, heightAnimation, HandoffBehavior.SnapshotAndReplace);
            ExpandedBodyHost.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(105))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                },
                HandoffBehavior.SnapshotAndReplace);
        }

        private void CollapseExpandedBody(bool animate)
        {
            if (ExpandedBodyHost == null) return;

            ExpandedBodyHost.BeginAnimation(HeightProperty, null);
            ExpandedBodyHost.BeginAnimation(OpacityProperty, null);

            if (!animate || ExpandedBodyHost.Visibility != Visibility.Visible)
            {
                FinishCollapsedBody();
                return;
            }

            var startHeight = Math.Max(0.0, ExpandedBodyHost.ActualHeight);
            if (startHeight <= 0.5)
            {
                FinishCollapsedBody();
                return;
            }

            ExpandedBodyHost.Height = startHeight;
            var duration = TimeSpan.FromMilliseconds(125);
            var heightAnimation = new DoubleAnimation(startHeight, 0, duration)
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn },
                FillBehavior = FillBehavior.Stop
            };
            heightAnimation.Completed += (completedSender, completedArgs) =>
            {
                if (MappingExpander?.IsExpanded == true) return;
                FinishCollapsedBody();
            };

            ExpandedBodyHost.BeginAnimation(HeightProperty, heightAnimation, HandoffBehavior.SnapshotAndReplace);
            ExpandedBodyHost.BeginAnimation(OpacityProperty,
                new DoubleAnimation(ExpandedBodyHost.Opacity, 0, TimeSpan.FromMilliseconds(95))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
                },
                HandoffBehavior.SnapshotAndReplace);
        }

        private void FinishCollapsedBody()
        {
            if (ExpandedBodyHost == null) return;
            ExpandedBodyHost.BeginAnimation(HeightProperty, null);
            ExpandedBodyHost.BeginAnimation(OpacityProperty, null);
            ExpandedBodyHost.Height = 0;
            ExpandedBodyHost.Opacity = 0;
            ExpandedBodyHost.Visibility = Visibility.Collapsed;
            ExpandedBodyHost.ContentTemplate = null;
        }

        private void MoveUp_OnClick(object sender, RoutedEventArgs e)
        {
            (DataContext as MappingViewModel)?.MoveUp();
        }

        private void MoveDown_OnClick(object sender, RoutedEventArgs e)
        {
            (DataContext as MappingViewModel)?.MoveDown();
        }

        private void MoveSection_OnClick(object sender, RoutedEventArgs e)
        {
            var mappingViewModel = DataContext as MappingViewModel;
            var button = sender as Button;
            if (mappingViewModel == null || button == null || !mappingViewModel.ButtonsEnabled) return;

            var profileViewModel = mappingViewModel.ProfileViewModel;
            var currentSection = profileViewModel.GetMappingSection(mappingViewModel);
            var menu = CreateDarkContextMenu(button);
            menu.Placement = PlacementMode.Bottom;

            foreach (var section in profileViewModel.MappingSections)
            {
                var capturedSection = section;
                var item = new MenuItem
                {
                    Header = (ReferenceEquals(currentSection, capturedSection) ? "✓  " : string.Empty) + capturedSection.Title,
                    IsEnabled = !ReferenceEquals(currentSection, capturedSection),
                    Foreground = Brushes.White,
                    Background = Brushes.Transparent,
                    Padding = new Thickness(10, 6, 14, 6)
                };
                item.Click += (clickSender, clickArgs) =>
                    profileViewModel.MoveMappingToSection(mappingViewModel, capturedSection);
                menu.Items.Add(item);
            }

            menu.Closed += (closedSender, closedArgs) =>
            {
                if (ReferenceEquals(button.ContextMenu, menu)) button.ContextMenu = null;
            };
            button.ContextMenu = menu;
            menu.IsOpen = true;
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
            await OpenQuickOutputPickerAsync(mappingViewModel, descriptor, placementTarget);
        }

        internal async Task<bool> OpenQuickOutputPickerAsync(DeviceBindingViewModel bindingViewModel,
            FrameworkElement placementTarget)
        {
            var mappingViewModel = DataContext as MappingViewModel;
            if (mappingViewModel == null || bindingViewModel?.DeviceBinding == null || placementTarget == null ||
                !mappingViewModel.ButtonsEnabled || !bindingViewModel.BindingEnabled)
                return false;

            var descriptor = new BindingVisualDescriptor
            {
                BindingGuid = bindingViewModel.DeviceBinding.Guid,
                DeviceConfigurationGuid = bindingViewModel.DeviceBinding.DeviceConfigurationGuid
            };
            return await OpenQuickOutputPickerAsync(mappingViewModel, descriptor, placementTarget);
        }

        private async Task<bool> OpenQuickOutputPickerAsync(MappingViewModel mappingViewModel,
            BindingVisualDescriptor descriptor, FrameworkElement placementTarget)
        {
            try
            {
                // Keyboard outputs are fastest to choose by pressing the desired key. For every
                // other output type keep the explicit control picker; an input press must never
                // be interpreted as a controller/mouse output selection.
                if (mappingViewModel.UsesPressCaptureForQuickOutput(descriptor))
                {
                    await mappingViewModel.QuickBindOutputAsync(descriptor);
                    return true;
                }

                ShowQuickOutputMenu(mappingViewModel, descriptor, placementTarget);
                return true;
            }
            catch (Exception exception)
            {
                Logger.Error("Failed to start mapping-card quick output bind", exception);
                DarkMessageBox.Show("UCR could not bind this output control. The error has been written to the log.",
                    "Unable to bind output", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
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
