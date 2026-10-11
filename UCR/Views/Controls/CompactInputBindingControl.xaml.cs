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

        private void BindButton_OnClick(object sender, RoutedEventArgs e)
        {
            if (DeviceBinding == null || DeviceBinding.IsInBindMode) return;
            if (DeviceBinding.DeviceIoType == DeviceIoType.Output)
            {
                ShowOutputPicker(BindButton);
                return;
            }
            StartPressCapture();
        }

        private void MenuButton_OnClick(object sender, RoutedEventArgs e)
        {
            if (DeviceBinding?.DeviceIoType == DeviceIoType.Output)
            {
                ShowOutputPicker(MenuButton);
                return;
            }
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
                // Keyboard keys are text, not gaming-control icons.
                var candidate = new DeviceBinding
                {
                    Profile = DeviceBinding.Profile,
                    DeviceIoType = DeviceBinding.DeviceIoType,
                    DeviceConfigurationGuid = configurationGuid,
                    DeviceBindingCategory = info.DeviceBindingCategory,
                    IsBound = true,
                    KeyType = info.KeyType,
                    KeyValue = info.KeyValue,
                    KeySubValue = info.KeySubValue
                };
                var visual = DeviceVisualCatalog.DescribeBinding(
                    candidate, info.DeviceBindingCategory, DeviceBinding.Profile);
                var label = new StackPanel { Orientation = Orientation.Horizontal };
                if (visual.ControlKind != ControlVisualKind.Key)
                    label.Children.Add(new ControlGlyphControl
                    {
                        Width = 46,
                        Height = 28,
                        Margin = new Thickness(0, 0, 10, 0),
                        Kind = visual.ControlKind,
                        AccentBrush = visual.ControlBrush ?? Brushes.Gray,
                        Label = visual.ControlLabel
                    });
                label.Children.Add(new TextBlock
                {
                    Text = node.Title,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = Brushes.White
                });
                item.Header = label;
                item.ToolTip = visual.ToolTip;
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

        private static IEnumerable<DeviceBindingNode> FlattenControls(IEnumerable<DeviceBindingNode> nodes)
        {
            foreach (var node in nodes ?? Enumerable.Empty<DeviceBindingNode>())
            {
                if (node == null) continue;
                if (node.IsBinding && node.DeviceBindingInfo != null) yield return node;
                foreach (var child in FlattenControls(node.ChildrenNodes)) yield return child;
            }
        }

        private void ShowOutputPicker(FrameworkElement target)
        {
            var config = GetSelectedDeviceConfiguration();
            if (config?.Device == null || DeviceBinding?.Profile?.Context == null)
            {
                HidWizards.UCR.Utilities.DarkMessageBox.Show(
                    "Select a configured output device first. UCR does not need the game's virtual controller to be physically connected.",
                    "Output device unavailable", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var category = (DataContext as DeviceBindingViewModel)?.DeviceBindingCategory
                ?? DeviceBinding.DeviceBindingCategory;
            var rawMenu = config.Device.GetDeviceBindingMenu(
                DeviceBinding.Profile.Context, DeviceIoType.Output);
            var keyboard = DeviceVisualCatalog.Describe(config, DeviceBinding.Profile, DeviceIoType.Output).Kind
                == DeviceVisualKind.Keyboard;
            var groups = new List<KeyValuePair<string, List<DeviceBindingNode>>>();
            if (keyboard)
            {
                foreach (var rootNode in rawMenu)
                {
                    if (!IsKeyboardRoot(rootNode)) continue;
                    foreach (var group in DeviceBindingControl.BuildKeyboardCategories(rootNode.ChildrenNodes))
                    {
                        var matching = FlattenControls(group.ChildrenNodes)
                            .Where(node => node.DeviceBindingInfo.DeviceBindingCategory == category).ToList();
                        if (matching.Count > 0)
                            groups.Add(new KeyValuePair<string, List<DeviceBindingNode>>(group.Title, matching));
                    }
                }
            }
            if (groups.Count == 0)
            {
                var matching = FlattenControls(rawMenu)
                    .Where(node => node.DeviceBindingInfo.DeviceBindingCategory == category).ToList();
                if (matching.Count > 0)
                    groups.Add(new KeyValuePair<string, List<DeviceBindingNode>>("Controls", matching));
            }
            var choices = groups.SelectMany(group => group.Value).ToList();

            if (_outputPicker != null) _outputPicker.IsOpen = false;
            var grid = new StackPanel();
            var popup = new Popup
            {
                PlacementTarget = target,
                Placement = PlacementMode.Bottom,
                AllowsTransparency = true,
                StaysOpen = false,
                PopupAnimation = PopupAnimation.Fade
            };
            _outputPicker = popup;

            foreach (var group in groups)
            {
                if (keyboard)
                    grid.Children.Add(new TextBlock
                    {
                        Text = group.Key.ToUpperInvariant(), FontWeight = FontWeights.SemiBold,
                        Foreground = Brushes.LightGray, Margin = new Thickness(8, 11, 0, 5)
                    });
                var groupGrid = new UniformGrid { Columns = 2 };
                grid.Children.Add(groupGrid);
                foreach (var node in group.Value)
                {
                var info = node.DeviceBindingInfo;
                var candidate = new DeviceBinding
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
                var visual = DeviceVisualCatalog.DescribeBinding(candidate, category, DeviceBinding.Profile);
                var contents = new StackPanel { Orientation = Orientation.Horizontal };
                if (visual.ControlKind != ControlVisualKind.Key)
                    contents.Children.Add(new ControlGlyphControl
                    {
                        Width = 54, Height = 34, Margin = new Thickness(0, 0, 8, 0),
                        Kind = visual.ControlKind,
                        AccentBrush = visual.ControlBrush ?? Brushes.Gray,
                        Label = visual.ControlLabel
                    });
                contents.Children.Add(new TextBlock
                {
                    Text = node.Title, Foreground = Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = 122
                });
                var button = new Button
                {
                    Content = contents,
                    Width = 208,
                    Height = 49,
                    Margin = new Thickness(2),
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Background = new SolidColorBrush(Color.FromRgb(42, 42, 42)),
                    Foreground = Brushes.White,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(8, 3, 5, 3),
                    ToolTip = visual.ToolTip
                };
                button.Click += (sender, args) =>
                {
                    DeviceBinding.SetDeviceConfigurationGuid(config.Guid);
                    DeviceBinding.DeviceBindingCategory = info.DeviceBindingCategory;
                    DeviceBinding.SetKeyTypeValue(info.KeyType, info.KeyValue, info.KeySubValue);
                    popup.IsOpen = false;
                };
                groupGrid.Children.Add(button);
                }
            }

            var root = new StackPanel();
            root.Children.Add(new TextBlock
            {
                Text = "CHOOSE OUTPUT", FontWeight = FontWeights.SemiBold, FontSize = 12,
                Foreground = Brushes.LightGray, Margin = new Thickness(8, 7, 0, 6)
            });
            if (choices.Count == 0)
                root.Children.Add(new TextBlock
                {
                    Text = "No compatible output controls for this mapping.",
                    Foreground = Brushes.White,
                    Margin = new Thickness(12)
                });
            else
                root.Children.Add(new ScrollViewer
                {
                    Content = grid,
                    MaxHeight = 415,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
                });

            var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 7, 4, 3) };
            var clear = new Button { Content = "Clear binding", Padding = new Thickness(10, 5, 10, 5),
                Background = Brushes.Transparent, Foreground = Brushes.White, BorderThickness = new Thickness(0) };
            clear.Click += (sender, args) => { DeviceBinding.ClearBinding(); popup.IsOpen = false; };
            actions.Children.Add(clear);

            if (DeviceVisualCatalog.Describe(config, DeviceBinding.Profile, DeviceIoType.Output).Kind
                == DeviceVisualKind.Keyboard)
            {
                var capture = new Button { Content = "Detect keyboard key", Padding = new Thickness(10, 5, 10, 5),
                    Background = Brushes.Transparent, Foreground = Brushes.White, BorderThickness = new Thickness(0) };
                capture.Click += (sender, args) => { popup.IsOpen = false; StartPressCapture(); };
                actions.Children.Add(capture);
            }
            root.Children.Add(actions);
            popup.Child = new Border
            {
                Child = root,
                Background = new SolidColorBrush(Color.FromRgb(32, 32, 32)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(84, 84, 84)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(7)
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
