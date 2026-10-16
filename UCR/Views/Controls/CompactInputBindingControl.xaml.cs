using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using HidWizards.UCR.ViewModels.Presentation;
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
        private Popup _outputPicker;

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
        }

        private void DeviceSelectionBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_loaded) return;
            var selected = DeviceSelectionBox.SelectedItem as ComboBoxItemViewModel;
            var viewModel = DataContext as DeviceBindingViewModel;
            if (selected == null || viewModel == null) return;

            viewModel.ChangeDeviceConfiguration(selected.Value);
            if (_outputPicker != null) _outputPicker.IsOpen = false;
        }

        private async void BindButton_OnClick(object sender, RoutedEventArgs e)
        {
            if (DeviceBinding == null) return;
            if (DeviceBinding.IsInBindMode)
            {
                DeviceBinding.CancelBindMode();
                return;
            }
            if (DeviceBinding.DeviceIoType == DeviceIoType.Input)
            {
                // No device selected => listen to all profile input devices.
                // A chosen device => listen to that device only.
                StartPressCapture();
                return;
            }

            // Keyboard outputs use pressed-key capture; controllers retain the
            // graphical picker with trigger and axis-specific choices.
            var configuration = GetSelectedDeviceConfiguration();
            if (DeviceVisualCatalog.Describe(configuration, DeviceBinding.Profile, DeviceIoType.Output).Kind ==
                DeviceVisualKind.Keyboard)
            {
                var mappingCard = FindAncestor<MappingCardControl>(this);
                var bindingViewModel = DataContext as DeviceBindingViewModel;
                if (mappingCard != null && bindingViewModel != null)
                {
                    await mappingCard.OpenQuickOutputPickerAsync(bindingViewModel, BindButton);
                    return;
                }
            }
            ShowOutputPicker(BindButton);
        }

        private void BindButton_OnPreviewMouseLeftButtonDown(object sender,
            System.Windows.Input.MouseButtonEventArgs e)
        {
            if (DeviceBinding?.IsInBindMode != true) return;
            DeviceBinding.CancelBindMode();
            e.Handled = true;
        }

        private void BindButton_OnPreviewMouseRightButtonDown(object sender,
            System.Windows.Input.MouseButtonEventArgs e)
        {
            if (DeviceBinding?.IsInBindMode != true) return;
            DeviceBinding.CancelBindMode();
            e.Handled = true;
        }

        private void MenuButton_OnClick(object sender, RoutedEventArgs e)
        {
            // Reveal the whole binding row before opening its options. Otherwise a
            // partly scrolled-off row leaves Block/NOT underneath the fixed footer.
            BringIntoView();
            BuildContextMenu();
            Ddl.PlacementTarget = MenuButton;
            // The inspector has a docked footer and a constrained scroll viewport.
            // Open the binding menu upwards so it cannot cover the footer or clipped rows.
            Ddl.Placement = PlacementMode.Top;
            Ddl.IsOpen = true;
        }

        private void BuildContextMenu()
        {
            if (Ddl == null) return;
            Ddl.Items.Clear();

            var configuration = GetSelectedDeviceConfiguration();
            if (configuration?.Device != null && DeviceBinding?.Profile?.Context != null)
            {
                var nodes = configuration.Device.GetDeviceBindingMenu(
                    DeviceBinding.Profile.Context, DeviceBinding.DeviceIoType);
                foreach (var node in nodes)
                {
                    // Expand only the keyboard's top-level "Keys" folder into
                    // the same categories offered by the legacy binding editor.
                    if (IsKeyboardRoot(node))
                    {
                        foreach (var categoryNode in DeviceBindingControl.BuildKeyboardCategories(node.ChildrenNodes))
                        {
                            var categoryItem = BuildMenuItem(categoryNode, configuration.Guid);
                            if (categoryItem != null) Ddl.Items.Add(categoryItem);
                        }
                        continue;
                    }
                    var item = BuildMenuItem(node, configuration.Guid);
                    if (item != null) Ddl.Items.Add(item);
                }
            }

            if (Ddl.Items.Count > 0) Ddl.Items.Add(new Separator());

            var clearItem = new MenuItem
            {
                Header = "Clear binding",
                Foreground = System.Windows.Media.Brushes.White
            };
            clearItem.IsEnabled = DeviceBinding?.IsBound == true;
            clearItem.Click += (clickSender, clickArgs) => DeviceBinding?.ClearBinding();
            Ddl.Items.Add(clearItem);
        }

        private static bool IsKeyboardRoot(DeviceBindingNode node)
        {
            return node != null && node.ChildrenNodes != null &&
                   node.ChildrenNodes.Count >= 20 &&
                   string.Equals(node.Title, "Keys", StringComparison.OrdinalIgnoreCase);
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
                if (info == null || info.DeviceBindingCategory !=
                    (DataContext as DeviceBindingViewModel)?.DeviceBindingCategory) return null;
                // The ellipsis is a text-only nested menu, not a second visual picker.
                item.ToolTip = "Assign " + node.Title;
                item.Click += (sender, args) =>
                {
                    DeviceBinding.SetDeviceConfigurationGuid(configurationGuid);
                    DeviceBinding.DeviceBindingCategory = info.DeviceBindingCategory;
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

        private void StartPressCapture()
        {
            if (DeviceBinding == null || DeviceBinding.IsInBindMode) return;
            try
            {
                var category = (DataContext as DeviceBindingViewModel)?.DeviceBindingCategory
                    ?? DeviceBinding.DeviceBindingCategory;
                DeviceBinding.DeviceBindingCategory = category;
                DeviceBinding.EnterBindMode();
            }
            catch (Exception exception)
            {
                Logger.Error("Unable to start control detection", exception);
                var reason = exception.GetBaseException()?.Message ?? exception.Message;
                HidWizards.UCR.Utilities.DarkMessageBox.Show(
                    "Unable to start detection for the selected device. " + reason +
                    "\n\nChoose the control from the menu instead, or check that the input device is connected.",
                    "Control detection unavailable", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static T FindAncestor<T>(DependencyObject start) where T : DependencyObject
        {
            var current = System.Windows.Media.VisualTreeHelper.GetParent(start);
            while (current != null)
            {
                var found = current as T;
                if (found != null) return found;
                current = System.Windows.Media.VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        private static IEnumerable<DeviceBindingNode> FlattenControls(IEnumerable<DeviceBindingNode> nodes)
        {
            foreach (var node in nodes ?? Enumerable.Empty<DeviceBindingNode>())
            {
                if (node == null) continue;
                if (node.IsBinding && node.DeviceBindingInfo != null) yield return node;
                foreach (var child in FlattenControls(node.ChildrenNodes)) yield return child;
            }
        }

        // Button-only picker. No axis, trigger, full-stick or conversion controls
        // are exposed here; the installed remapper engine still supports old profiles.
        private void ShowOutputPicker(FrameworkElement target)
        {
            var config = GetSelectedDeviceConfiguration();
            if (config?.Device == null || DeviceBinding?.Profile?.Context == null) return;
            if (_outputPicker != null) _outputPicker.IsOpen = false;

            var popup = new Popup
            {
                PlacementTarget = target,
                Placement = PlacementMode.Bottom,
                AllowsTransparency = true,
                StaysOpen = false
            };
            _outputPicker = popup;
            var controls = FlattenControls(config.Device.GetDeviceBindingMenu(
                    DeviceBinding.Profile.Context, DeviceIoType.Output))
                .Where(node => node.DeviceBindingInfo != null &&
                    node.DeviceBindingInfo.DeviceBindingCategory == DeviceBinding.DeviceBindingCategory)
                .ToList();

            var choices = new WrapPanel { Width = 448 };
            foreach (var node in controls)
            {
                var info = node.DeviceBindingInfo;
                var temporary = new DeviceBinding
                {
                    Profile = DeviceBinding.Profile, DeviceIoType = DeviceIoType.Output,
                    DeviceBindingCategory = info.DeviceBindingCategory,
                    DeviceConfigurationGuid = config.Guid, IsBound = true,
                    KeyType = info.KeyType, KeyValue = info.KeyValue, KeySubValue = info.KeySubValue
                };
                var visual = DeviceVisualCatalog.DescribeBinding(
                    temporary, info.DeviceBindingCategory, DeviceBinding.Profile);
                // Axis-shaped output controls are deliberately absent from this UI.
                if (visual.ControlKind == ControlVisualKind.StickAxis ||
                    visual.ControlKind == ControlVisualKind.Trigger) continue;

                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Children.Add(new ControlGlyphControl
                {
                    Kind = visual.ControlKind, Label = visual.ControlLabel,
                    AccentBrush = visual.ControlBrush ?? Brushes.Gray,
                    Width = 42, Height = 33, Margin = new Thickness(0, 0, 7, 0)
                });
                row.Children.Add(new TextBlock
                {
                    Text = node.Title, Foreground = Brushes.White,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = 144, VerticalAlignment = VerticalAlignment.Center
                });
                var choice = new Button
                {
                    Content = row, Width = 211, Height = 48,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(3), Padding = new Thickness(8, 3, 4, 3),
                    Background = new SolidColorBrush(Color.FromRgb(43, 43, 43)),
                    Foreground = Brushes.White, BorderBrush = Brushes.DimGray,
                    BorderThickness = new Thickness(1), ToolTip = node.Title
                };
                var selectedInfo = info;
                choice.Click += (sender, args) =>
                {
                    DeviceBinding.SetDeviceConfigurationGuid(config.Guid);
                    DeviceBinding.SetKeyTypeValue(selectedInfo.KeyType, selectedInfo.KeyValue,
                        selectedInfo.KeySubValue);
                    popup.IsOpen = false;
                };
                choices.Children.Add(choice);
            }

            var root = new StackPanel();
            root.Children.Add(new TextBlock
            {
                Text = "CHOOSE OUTPUT", FontSize = 12, FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.LightGray, Margin = new Thickness(8, 8, 0, 7)
            });
            root.Children.Add(new ScrollViewer
            {
                MaxHeight = 430,
                Content = choices,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            });
            var clear = new Button
            {
                Content = "Clear binding", HorizontalAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(11, 6, 11, 6),
                Background = Brushes.Transparent, Foreground = Brushes.White,
                BorderThickness = new Thickness(0)
            };
            clear.Click += (sender, args) =>
            {
                DeviceBinding.ClearBinding();
                popup.IsOpen = false;
            };
            root.Children.Add(clear);
            popup.Child = new Border
            {
                Child = root,
                Background = new SolidColorBrush(Color.FromRgb(32, 32, 32)),
                BorderBrush = Brushes.DimGray, BorderThickness = new Thickness(1),
                Padding = new Thickness(5)
            };
            popup.IsOpen = true;
        }

        private DeviceConfiguration GetSelectedDeviceConfiguration()
        {
            var selected = DeviceSelectionBox?.SelectedItem as ComboBoxItemViewModel;
            if (selected == null || DeviceBinding?.Profile == null) return null;
            return DeviceBinding.Profile.GetDeviceConfiguration(DeviceBinding.DeviceIoType, selected.Value);
        }
    }
}
