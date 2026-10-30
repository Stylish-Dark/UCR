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
        // The mapping editor owns device selection at the side level.
        // Keep the per-binding selector for other UCR views.
        // Expanded mapping cards use the same output binding to select the
        // control TYPE and destination in one action, not two separate menus.
        public static readonly DependencyProperty EnableUnifiedOutputPickerProperty =
            DependencyProperty.Register(nameof(EnableUnifiedOutputPicker), typeof(bool),
                typeof(CompactInputBindingControl), new PropertyMetadata(false));

        public bool EnableUnifiedOutputPicker
        {
            get => (bool)GetValue(EnableUnifiedOutputPickerProperty);
            set => SetValue(EnableUnifiedOutputPickerProperty, value);
        }

        public static readonly DependencyProperty DeviceSelectorVisibilityProperty =
            DependencyProperty.Register(nameof(DeviceSelectorVisibility), typeof(Visibility),
                typeof(CompactInputBindingControl), new PropertyMetadata(Visibility.Visible));

        public Visibility DeviceSelectorVisibility
        {
            get => (Visibility)GetValue(DeviceSelectorVisibilityProperty);
            set => SetValue(DeviceSelectorVisibilityProperty, value);
        }

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
            // DataTemplates can realize their binding view-model before the
            // separate DeviceBinding dependency property is populated. The
            // button must operate on the actual row binding, never silently exit.
            var currentBinding = (DataContext as DeviceBindingViewModel)?.DeviceBinding;
            if (currentBinding != null && !ReferenceEquals(DeviceBinding, currentBinding))
                DeviceBinding = currentBinding;
            if (DeviceBinding == null)
            {
                Logger.Warn("Output/input button has no backing device binding.");
                return;
            }
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

            await OpenOutputSelectionAsync(BindButton);
        }

        // Both visible Output actions use one dispatch path. The unified picker
        // owns mapping-editor selection; legacy controls retain their own popup.
        private async System.Threading.Tasks.Task OpenOutputSelectionAsync(FrameworkElement target)
        {
            var binding = DataContext as DeviceBindingViewModel;
            var card = FindAncestor<MappingCardControl>(this);
            if (EnableUnifiedOutputPicker && card != null)
            {
                card.OpenUnifiedOutputPicker(binding, target);
                return;
            }

            if (!EnableUnifiedOutputPicker && card != null && binding != null)
            {
                var configuration = GetSelectedDeviceConfiguration();
                if (DeviceVisualCatalog.Describe(configuration, DeviceBinding.Profile,
                    DeviceIoType.Output).Kind == DeviceVisualKind.Keyboard)
                {
                    await card.OpenQuickOutputPickerAsync(binding, target);
                    return;
                }
            }

            ShowOutputPicker(target);
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

        private async void MenuButton_OnClick(object sender, RoutedEventArgs e)
        {
            // Reveal the whole binding row before opening its options. Otherwise a
            // partly scrolled-off row leaves Block/NOT underneath the fixed footer.
            BringIntoView();
            var activeBinding = (DataContext as DeviceBindingViewModel)?.DeviceBinding ?? DeviceBinding;
            if (activeBinding?.DeviceIoType == DeviceIoType.Output)
            {
                await OpenOutputSelectionAsync(MenuButton);
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
                var mapping = FindAncestor<MappingCardControl>(this)?.DataContext
                    as MappingViewModel;
                if (mapping != null)
                    mapping.InputPresetFeedback =
                        "Input detection could not start. Check the input device or use the binding menu.";
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

        // Output selection is divided into buttons and directionals, backed by the
        // selected device's actual provider descriptors (not hard-coded key IDs).
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
            // A single button input must not silently bind an entire continuous axis.
            // Only discrete output controls are offered until directional-axis mappings
            // have explicit negative and positive input slots.
            var controls = FlattenControls(nodes)
                .Where(node => node.DeviceBindingInfo != null &&
                    node.DeviceBindingInfo.DeviceBindingCategory == DeviceBindingCategory.Momentary)
                .ToList();
            var buttons = new WrapPanel { Width = 440 };
            var directionals = new WrapPanel { Width = 440 };

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
                if (visual.ControlKind == ControlVisualKind.StickAxis) continue;
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
                    DeviceBinding.DeviceBindingCategory = selectedInfo.DeviceBindingCategory;
                    DeviceBinding.SetDeviceConfigurationGuid(config.Guid);
                    DeviceBinding.SetKeyTypeValue(selectedInfo.KeyType, selectedInfo.KeyValue, selectedInfo.KeySubValue);
                    popup.IsOpen = false;
                };
                (isDirectional ? directionals : buttons).Children.Add(choice);
            }

            var tabs = new TabControl { Background = Brushes.Transparent };
            foreach (var category in new[] {
                new { Title = "Buttons", Items = buttons },
                new { Title = "Directionals", Items = directionals }
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
