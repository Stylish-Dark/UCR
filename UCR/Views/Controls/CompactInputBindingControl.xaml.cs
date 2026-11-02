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

        public bool ShowIndividualBlock { get; set; } = true;

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
            // Binding refresh/replacement can raise SelectionChanged while a
            // dropdown is merely being rebuilt. Never convert that placeholder
            // into an instruction to clear a real output destination.
            if (viewModel.DeviceBinding?.DeviceIoType == DeviceIoType.Output &&
                selected.Value == Guid.Empty) return;
            if (viewModel.DeviceBinding != null &&
                selected.Value == viewModel.DeviceBinding.DeviceConfigurationGuid) return;

            if (viewModel.DeviceBinding?.DeviceIoType == DeviceIoType.Output &&
                selected.Value == DeviceBindingViewModel.FilterOutputChoiceGuid)
            {
                var card = FindAncestor<MappingCardControl>(this);
                if (card != null)
                    Dispatcher.BeginInvoke((Action)(() => card.SwitchOutputBindingToFilter(viewModel)),
                        System.Windows.Threading.DispatcherPriority.Background);
            }
            else
            {
                viewModel.ChangeDeviceConfiguration(selected.Value);
            }
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
            if (DeviceBinding?.DeviceIoType == DeviceIoType.Output)
            {
                ShowOutputPicker(MenuButton);
                return;
            }
            BuildContextMenu();
            Ddl.PlacementTarget = MenuButton;
            // The inspector has a docked footer and a constrained scroll viewport.
            // Open the binding menu upwards so it cannot cover the footer or clipped rows.
            Ddl.Placement = PlacementMode.Top;
            Ddl.IsOpen = true;
        }

        private void BuildContextMenu()
        {
            PopulateInputMenu(Ddl, DeviceBinding, DataContext as DeviceBindingViewModel);
        }

        // The collapsed mapping glyph and the editor ellipsis use exactly the
        // same menu implementation. No second set of input-kind choices.
        internal static ContextMenu CreateInputOptionsMenu(DeviceBindingViewModel viewModel,
            FrameworkElement target)
        {
            var menu = new ContextMenu
            {
                PlacementTarget = target,
                Placement = PlacementMode.Top,
                Style = target.TryFindResource("MaterialDesignContextMenu") as Style,
                Background = new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x24)),
                Foreground = Brushes.White
            };
            PopulateInputMenu(menu, viewModel?.DeviceBinding, viewModel);
            return menu;
        }

        private static void PopulateInputMenu(ContextMenu menu, DeviceBinding binding,
            DeviceBindingViewModel viewModel)
        {
            if (menu == null) return;
            menu.Items.Clear();
            var profile = binding?.Profile;
            if (profile != null)
            {
                var configuration = profile.GetDeviceConfiguration(DeviceIoType.Input,
                    binding.DeviceConfigurationGuid)
                    ?? profile.GetPrimaryDeviceConfiguration(DeviceIoType.Input);
                if (configuration?.Device != null)
                {
                    var nodes = configuration.Device.GetDeviceBindingMenu(profile.Context,
                        DeviceIoType.Input);
                    foreach (var node in nodes)
                    {
                        if (IsKeyboardRoot(node))
                        {
                            foreach (var group in DeviceBindingControl.BuildKeyboardCategories(node.ChildrenNodes))
                            {
                                var item = BuildInputMenuItem(group, configuration.Guid, binding);
                                if (item != null) menu.Items.Add(item);
                            }
                        }
                        else
                        {
                            var item = BuildInputMenuItem(node, configuration.Guid, binding);
                            if (item != null) menu.Items.Add(item);
                        }
                    }
                }
            }
            if (menu.Items.Count > 0) menu.Items.Add(new Separator());
            var clear = new MenuItem { Header = "Clear binding", Foreground = Brushes.White,
                IsEnabled = binding?.IsBound == true };
            clear.Click += (s, e) => binding?.ClearBinding();
            menu.Items.Add(clear);
        }

        private static bool IsKeyboardRoot(DeviceBindingNode node)
        {
            return node != null && node.ChildrenNodes != null &&
                   node.ChildrenNodes.Count >= 20 &&
                   string.Equals(node.Title, "Keys", StringComparison.OrdinalIgnoreCase);
        }

        private static MenuItem BuildInputMenuItem(DeviceBindingNode node, Guid configurationGuid,
            DeviceBinding binding)
        {
            if (node == null) return null;
            var item = new MenuItem { Header = node.Title, Foreground = Brushes.White };
            if (node.IsBinding)
            {
                var info = node.DeviceBindingInfo;
                if (info == null) return null;
                item.ToolTip = "Assign " + node.Title;
                item.Click += (sender, args) =>
                {
                    binding.SetInputExpressionInputKind(
                        info.DeviceBindingCategory == DeviceBindingCategory.Range ? 3 : 0);
                    binding.SetDeviceConfigurationGuid(configurationGuid);
                    binding.DeviceBindingCategory = info.DeviceBindingCategory;
                    binding.SetKeyTypeValue(info.KeyType, info.KeyValue, info.KeySubValue);
                };
                return item;
            }
            foreach (var child in node.ChildrenNodes ?? new List<DeviceBindingNode>())
            {
                var childItem = BuildInputMenuItem(child, configurationGuid, binding);
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
                if (DeviceBinding.DeviceIoType == DeviceIoType.Input)
                    (DataContext as DeviceBindingViewModel)?.ExpressionMapping?.EnableInputExpression();
                DeviceBinding.EnterBindMode(autoDetectInputCategory: DeviceBinding.DeviceIoType == DeviceIoType.Input);
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

        // Output controls come directly from the selected device's provider descriptors.
        private void ShowOutputPicker(FrameworkElement target)
        {
            if (_outputPicker != null) _outputPicker.IsOpen = false;
            var config = GetSelectedDeviceConfiguration();
            if (config?.Device == null || DeviceBinding?.Profile?.Context == null) return;

            var popup = new Popup
            {
                PlacementTarget = target,
                Placement = PlacementMode.Bottom,
                AllowsTransparency = true,
                StaysOpen = false
            };
            _outputPicker = popup;

            var nodes = DeviceBinding.Profile.Context.DevicesManager.GetDeviceBindingMenu(
                config.Device, DeviceIoType.Output);
            var controls = FlattenControls(nodes)
                .Where(node => node.DeviceBindingInfo != null &&
                    (node.DeviceBindingInfo.DeviceBindingCategory == DeviceBindingCategory.Momentary ||
                     node.DeviceBindingInfo.DeviceBindingCategory == DeviceBindingCategory.Range))
                .ToList();
            var buttons = new WrapPanel { Width = 440 };
            var directionals = new WrapPanel { Width = 440 };
            var axes = new WrapPanel { Width = 440 };

            foreach (var node in controls)
            {
                var info = node.DeviceBindingInfo;
                if (info == null) continue;
                var temporary = new DeviceBinding
                {
                    Profile = DeviceBinding.Profile,
                    DeviceIoType = DeviceIoType.Output,
                    DeviceBindingCategory = info.DeviceBindingCategory,
                    DeviceConfigurationGuid = config.Guid,
                    IsBound = true,
                    KeyType = info.KeyType,
                    KeyValue = info.KeyValue,
                    KeySubValue = info.KeySubValue
                };
                var visual = DeviceVisualCatalog.DescribeBinding(
                    temporary, info.DeviceBindingCategory, DeviceBinding.Profile);
                var title = node.Title ?? "";
                var lower = title.ToLowerInvariant();
                bool isDirectional = visual.ControlKind == ControlVisualKind.StickAxis ||
                    lower.Contains("d-pad") || lower.Contains("dpad") ||
                    lower.Contains("hat") || lower.Contains("pov") ||
                    lower.Contains("axis") || lower.Contains("stick") ||
                    lower == "up" || lower == "down" || lower == "left" || lower == "right" ||
                    lower.Contains("north") || lower.Contains("south") ||
                    lower.Contains("east") || lower.Contains("west");
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Children.Add(new ControlGlyphControl
                {
                    Kind = visual.ControlKind,
                    Label = visual.ControlLabel,
                    AccentBrush = visual.ControlBrush ?? Brushes.Gray,
                    Width = 41,
                    Height = 33,
                    Margin = new Thickness(0, 0, 8, 0)
                });
                row.Children.Add(new TextBlock
                {
                    Text = title,
                    Foreground = Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 158
                });
                var choice = new Button
                {
                    Content = row,
                    Width = 212,
                    MinHeight = 46,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(3),
                    Padding = new Thickness(8, 4, 5, 4),
                    Background = new SolidColorBrush(Color.FromRgb(43, 43, 43)),
                    Foreground = Brushes.White,
                    BorderBrush = Brushes.DimGray,
                    BorderThickness = new Thickness(1),
                    ToolTip = title
                };
                var selectedInfo = info;
                choice.Click += (sender, args) =>
                {
                    var card = FindAncestor<MappingCardControl>(this);
                    var bindingVm = DataContext as DeviceBindingViewModel;
                    if (card != null && bindingVm != null)
                    {
                        // Apply before closing. Replacing the output plug-in can
                        // invalidate the old binding view-model during a deferred callback.
                        if (card.ApplyOutputControlChoice(bindingVm, config.Guid, selectedInfo))
                            popup.IsOpen = false;
                    }
                };
                var isTrigger = visual.ControlKind == ControlVisualKind.Trigger ||
                                lower.Contains("trigger") || lower == "lt" || lower == "rt" ||
                                lower.Contains("l2") || lower.Contains("r2");
                if (isTrigger || info.DeviceBindingCategory == DeviceBindingCategory.Momentary && !isDirectional)
                    buttons.Children.Add(choice);
                else if (info.DeviceBindingCategory == DeviceBindingCategory.Range)
                    axes.Children.Add(choice);
                else
                    directionals.Children.Add(choice);

                // Each stick axis also offers its two signed directions. A selected
                // direction configures the same underlying range axis, with an
                // appropriate signed output value, rather than inventing HID buttons.
                var axis = visual.ControlLabel;
                if (info.DeviceBindingCategory == DeviceBindingCategory.Range &&
                    (axis == "LX" || axis == "LY" || axis == "RX" || axis == "RY"))
                {
                    var stick = axis.StartsWith("L", StringComparison.Ordinal) ? "Left Stick" : "Right Stick";
                    var horizontal = axis.EndsWith("X", StringComparison.Ordinal);
                    foreach (var direction in new[] {
                        new { Name = horizontal ? "Left" : "Up", Sign = -1 },
                        new { Name = horizontal ? "Right" : "Down", Sign = 1 }
                    })
                    {
                        var selectedSign = direction.Sign;
                        var label = stick + " " + direction.Name;
                        var directional = new Button
                        {
                            Content = label,
                            Width = 212,
                            MinHeight = 46,
                            HorizontalContentAlignment = HorizontalAlignment.Left,
                            Margin = new Thickness(3),
                            Padding = new Thickness(8, 4, 5, 4),
                            Background = new SolidColorBrush(Color.FromRgb(43, 43, 43)),
                            Foreground = Brushes.White,
                            BorderBrush = Brushes.DimGray,
                            BorderThickness = new Thickness(1),
                            ToolTip = label
                        };
                        directional.Click += (sender, args) =>
                        {
                            var card = FindAncestor<MappingCardControl>(this);
                            var bindingVm = DataContext as DeviceBindingViewModel;
                            if (card != null && bindingVm != null)
                            {
                                if (card.ApplyOutputControlChoice(bindingVm, config.Guid, selectedInfo, selectedSign))
                                    popup.IsOpen = false;
                            }
                        };
                        directionals.Children.Add(directional);
                    }
                }
            }

            // Keep the eight stick directions in compass order.
            var nextPosition = 0;
            foreach (var stickName in new[] { "Left Stick", "Right Stick" })
            foreach (var directionName in new[] { "Up", "Down", "Left", "Right" })
            {
                var title = stickName + " " + directionName;
                var item = directionals.Children.OfType<Button>()
                    .FirstOrDefault(button => string.Equals(button.Content as string, title,
                        StringComparison.Ordinal));
                if (item == null) continue;
                directionals.Children.Remove(item);
                directionals.Children.Insert(nextPosition++, item);
            }

            // Place the analogue triggers beneath their respective shoulder buttons.
            foreach (var side in new[] { "L", "R" })
            {
                var trigger = buttons.Children.OfType<Button>().FirstOrDefault(button =>
                    button.ToolTip is string title &&
                    (title.Equals(side == "L" ? "Left Trigger" : "Right Trigger", StringComparison.OrdinalIgnoreCase) ||
                     title.Equals(side == "L" ? "LT" : "RT", StringComparison.OrdinalIgnoreCase) ||
                     title.Equals(side == "L" ? "L2" : "R2", StringComparison.OrdinalIgnoreCase)));
                if (trigger == null) continue;
                var shoulder = buttons.Children.OfType<Button>().FirstOrDefault(button =>
                    button.ToolTip is string title &&
                    (title.Equals(side == "L" ? "Left Button" : "Right Button", StringComparison.OrdinalIgnoreCase) ||
                     title.Equals(side == "L" ? "LB" : "RB", StringComparison.OrdinalIgnoreCase) ||
                     title.Equals(side == "L" ? "L1" : "R1", StringComparison.OrdinalIgnoreCase) ||
                     title.Equals(side == "L" ? "Left Bumper" : "Right Bumper", StringComparison.OrdinalIgnoreCase)));
                if (shoulder == null) continue;
                buttons.Children.Remove(trigger);
                buttons.Children.Insert(buttons.Children.IndexOf(shoulder) + 1, trigger);
            }

            // WrapPanel arranges in row-major pairs. Keep the controller buttons
            // together as LB/RB on one row and LT/RT on the next.
            var orderedButtons = buttons.Children.OfType<Button>().ToList();
            var lb = orderedButtons.FirstOrDefault(x => string.Equals(x.ToolTip as string, "LB", StringComparison.OrdinalIgnoreCase));
            var rb = orderedButtons.FirstOrDefault(x => string.Equals(x.ToolTip as string, "RB", StringComparison.OrdinalIgnoreCase));
            var lt = orderedButtons.FirstOrDefault(x => string.Equals(x.ToolTip as string, "LT", StringComparison.OrdinalIgnoreCase));
            var rt = orderedButtons.FirstOrDefault(x => string.Equals(x.ToolTip as string, "RT", StringComparison.OrdinalIgnoreCase));
            if (lb != null && rb != null && lt != null && rt != null)
            {
                var index = buttons.Children.IndexOf(lb);
                foreach (var item in new[] { lb, rb, lt, rt }) buttons.Children.Remove(item);
                foreach (var item in new[] { lb, rb, lt, rt }) buttons.Children.Insert(index++, item);
            }

            var tabs = new TabControl { Background = Brushes.Transparent, MinHeight = 365 };
            foreach (var category in new[] {
                new { Title = "Buttons", Items = buttons },
                new { Title = "Directions", Items = directionals },
                new { Title = "Axes", Items = axes }
            })
            {
                if (category.Items.Children.Count == 0)
                    category.Items.Children.Add(new TextBlock {
                        Text = "No controls in this category",
                        Foreground = Brushes.LightGray,
                        Margin = new Thickness(12)
                    });
                tabs.Items.Add(new TabItem
                {
                    Header = category.Title,
                    Foreground = Brushes.White,
                    Content = new ScrollViewer
                    {
                        Content = category.Items,
                        Height = 340,
                        MinHeight = 340,
                        MaxHeight = 340,
                        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                    }
                });
            }
            tabs.SelectedIndex = 0;
            var root = new StackPanel();
            root.Children.Add(new TextBlock
            {
                Text = "CHOOSE OUTPUT",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.LightGray,
                Margin = new Thickness(8, 7, 0, 8)
            });
            root.Children.Add(tabs);
            var clear = new Button
            {
                Content = "Clear binding",
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(8, 5, 0, 4),
                Padding = new Thickness(11, 6, 11, 6),
                Background = Brushes.Transparent,
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                IsEnabled = DeviceBinding.IsBound
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
                BorderBrush = Brushes.DimGray,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(5),
                Width = 470
            };
            popup.IsOpen = true;
        }

        private DeviceConfiguration GetSelectedDeviceConfiguration()
        {
            if (DeviceBinding?.Profile == null) return null;
            var selected = DeviceSelectionBox?.SelectedItem as ComboBoxItemViewModel;
            var configurationGuid = selected?.Value ?? DeviceBinding.DeviceConfigurationGuid;
            var configuration = DeviceBinding.Profile.GetDeviceConfiguration(DeviceBinding.DeviceIoType, configurationGuid);
            return configuration ?? DeviceBinding.Profile.GetPrimaryDeviceConfiguration(DeviceBinding.DeviceIoType);
        }
    }
}
