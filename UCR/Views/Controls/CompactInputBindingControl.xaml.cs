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
            if (DeviceBinding == null || DeviceBinding.IsInBindMode) return;
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

            var inputViewModel = DataContext as DeviceBindingViewModel;
            if (DeviceBinding?.DeviceIoType == DeviceIoType.Input &&
                inputViewModel != null &&
                (inputViewModel.ExpressionMapping != null ||
                 inputViewModel.LegacyInputTypeVisibility == Visibility.Visible))
            {
                // The default Button case is intentionally single-row. Type selection
                // lives in the binding's existing ellipsis rather than repeating on every
                // input card; axis-specific controls appear only once an axis is selected.
                var inputType = new MenuItem
                {
                    Header = "Input type",
                    Foreground = Brushes.White,
                    Background = Brushes.Transparent,
                    Padding = new Thickness(10, 6, 14, 6)
                };
                var labels = new[] { "Button", "Axis positive", "Axis negative", "Axis value" };
                for (var index = 0; index < labels.Length; index++)
                {
                    var selectedKind = index;
                    var kindItem = new MenuItem
                    {
                        Header = labels[index],
                        IsChecked = inputViewModel.InputExpressionInputKind == index,
                        Foreground = Brushes.White,
                        Background = Brushes.Transparent,
                        Padding = new Thickness(10, 6, 14, 6)
                    };
                    kindItem.Click += (s, args) =>
                    {
                        inputViewModel.InputExpressionInputKind = selectedKind;
                    };
                    inputType.Items.Add(kindItem);
                }
                Ddl.Items.Add(inputType);
                Ddl.Items.Add(new Separator());
            }

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

        private HidWizards.UCR.Core.Models.Plugin FindButtonToAxisPlugin()
        {
            if (DeviceBinding?.Profile == null) return null;
            return DeviceBinding.Profile.GetAllMappings()
                .Where(mapping => mapping?.Plugins != null)
                .SelectMany(mapping => mapping.Plugins)
                .FirstOrDefault(plugin =>
                    string.Equals(plugin?.PluginName, "Button to Axis", StringComparison.OrdinalIgnoreCase)
                    && plugin.Outputs != null
                    && plugin.Outputs.Any(binding => binding.Guid == DeviceBinding.Guid));
        }

        private bool IsButtonToAxisOutput()
        {
            return FindButtonToAxisPlugin() != null;
        }

        private void SetButtonToAxisDirection(int percent, bool trigger = false)
        {
            var plugin = FindButtonToAxisPlugin();
            var property = plugin?.GetType().GetProperty("RangePressed");
            if (property == null || !property.CanWrite) return;
            property.SetValue(plugin, (double)percent, null);
            if (trigger)
            {
                // Trigger is a one-direction analog control. Neutral must be released,
                // rather than an arbitrary Left/Right half-axis direction.
                plugin.GetType().GetProperty("Range")?.SetValue(plugin, -100.0, null);
                plugin.GetType().GetProperty("Initialize")?.SetValue(plugin, true, null);
            }
            DeviceBinding.Profile.Context.ContextChanged();
        }

        private void ApplyOutputControl(Guid configuration, DeviceBindingInfo info)
        {
            DeviceBinding.SetDeviceConfigurationGuid(configuration);
            DeviceBinding.DeviceBindingCategory = info.DeviceBindingCategory;
            DeviceBinding.SetKeyTypeValue(info.KeyType, info.KeyValue, info.KeySubValue);
        }

        // One picker owns destination type AND control. The three tabs are an
        // immediate one-click switch, never nested type-selection dropdowns.
        private void ShowOutputPicker(FrameworkElement target)
        {
            var config = GetSelectedDeviceConfiguration();
            if (config?.Device == null || DeviceBinding?.Profile?.Context == null)
            {
                HidWizards.UCR.Utilities.DarkMessageBox.Show(
                    "Select an output device before choosing a control.",
                    "Output device unavailable", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (_outputPicker != null) _outputPicker.IsOpen = false;
            var popup = new Popup
            {
                PlacementTarget = target, Placement = PlacementMode.Bottom,
                AllowsTransparency = true, StaysOpen = false,
                PopupAnimation = PopupAnimation.Fade
            };
            _outputPicker = popup;
            var all = FlattenControls(config.Device.GetDeviceBindingMenu(
                DeviceBinding.Profile.Context, DeviceIoType.Output)).ToList();
            var profile = DeviceBinding.Profile;
            var options = all.Select(node =>
            {
                var info = node.DeviceBindingInfo;
                var temporary = new DeviceBinding
                {
                    Profile = profile, DeviceIoType = DeviceIoType.Output,
                    DeviceBindingCategory = info.DeviceBindingCategory,
                    DeviceConfigurationGuid = config.Guid, IsBound = true,
                    KeyType = info.KeyType, KeyValue = info.KeyValue, KeySubValue = info.KeySubValue
                };
                return new
                {
                    Node = node,
                    Visual = DeviceVisualCatalog.DescribeBinding(
                        temporary, info.DeviceBindingCategory, profile)
                };
            }).ToList();

            var buttonGrid = new UniformGrid { Columns = 2 };
            var directionalGrid = new UniformGrid { Columns = 2 };
            var axisGrid = new UniformGrid { Columns = 2 };
            var used = new HashSet<DeviceBindingNode>();
            var buttonOrder = new[] { "A", "B", "X", "Y", "LB", "RB", "LT", "RT",
                "LS", "RS", "BK", "ST" };

            // The two trigger axes deliberately appear in Buttons, directly
            // below their matching shoulder buttons.
            foreach (var key in buttonOrder)
            {
                var entry = options.FirstOrDefault(item =>
                    string.Equals((item.Visual.ControlLabel ?? "").Trim(), key,
                        StringComparison.OrdinalIgnoreCase));
                if (entry == null) continue;
                used.Add(entry.Node);
                AddOutputPickerChoice(buttonGrid, popup, config, entry.Node,
                    entry.Visual, key,
                    entry.Visual.ControlKind == ControlVisualKind.Trigger
                        ? "Button to Axis" : "Button to Button",
                    entry.Visual.ControlKind == ControlVisualKind.Trigger ? 100 : 0,
                    entry.Visual.ControlKind == ControlVisualKind.Trigger);
            }
            foreach (var entry in options.Where(item =>
                item.Node.DeviceBindingInfo.DeviceBindingCategory == DeviceBindingCategory.Momentary &&
                item.Visual.ControlKind != ControlVisualKind.DPad &&
                !used.Contains(item.Node)))
            {
                AddOutputPickerChoice(buttonGrid, popup, config, entry.Node,
                    entry.Visual, entry.Node.Title, "Button to Button", 0, false);
            }

            // D-pad four directions plus one full-pad assignment.
            foreach (var direction in new[] { "Up", "Down", "Left", "Right" })
            {
                var entry = options.FirstOrDefault(item =>
                    item.Visual.ControlKind == ControlVisualKind.DPad &&
                    ((item.Node.Title ?? "").IndexOf(direction, StringComparison.OrdinalIgnoreCase) >= 0 ||
                     string.Equals(item.Visual.ControlLabel, direction, StringComparison.OrdinalIgnoreCase)));
                if (entry != null)
                    AddOutputPickerChoice(directionalGrid, popup, config, entry.Node,
                        entry.Visual, "D-pad " + direction, "Button to Button", 0, false);
            }
            AddFullDirectionalChoice(directionalGrid, popup, config, "D-pad");

            // Each stick offers four *signed* directions from its X/Y axes.
            // Full stick is one four-way destination, not four unrelated bindings.
            foreach (var stick in new[] { "Left", "Right" })
            {
                var prefix = stick == "Left" ? "L" : "R";
                foreach (var direction in new[] { "Up", "Left", "Down", "Right" })
                {
                    var axis = direction == "Up" || direction == "Down" ? "Y" : "X";
                    var entry = options.FirstOrDefault(item =>
                        item.Visual.ControlKind == ControlVisualKind.StickAxis &&
                        string.Equals(item.Visual.ControlLabel, prefix + axis,
                            StringComparison.OrdinalIgnoreCase));
                    if (entry == null) continue;
                    var sign = direction == "Up" || direction == "Left" ? -100 : 100;
                    AddOutputPickerChoice(directionalGrid, popup, config, entry.Node,
                        entry.Visual, stick + " stick " + direction,
                        "Button to Axis", sign, false);
                }
                AddFullDirectionalChoice(directionalGrid, popup, config, stick + " stick");
            }

            foreach (var entry in options.Where(item =>
                item.Node.DeviceBindingInfo.DeviceBindingCategory == DeviceBindingCategory.Range &&
                item.Visual.ControlKind != ControlVisualKind.Trigger))
            {
                AddOutputPickerChoice(axisGrid, popup, config, entry.Node, entry.Visual,
                    entry.Node.Title, "Axis to Axis", 0, false);
            }

            var tabs = new TabControl
            {
                Width = 452, Background = new SolidColorBrush(Color.FromRgb(32, 32, 32)),
                BorderThickness = new Thickness(0), SelectedIndex = 0
            };
            tabs.Items.Add(OutputPickerTab("Buttons", buttonGrid));
            tabs.Items.Add(OutputPickerTab("Directionals", directionalGrid));
            tabs.Items.Add(OutputPickerTab("Axes", axisGrid));
            var root = new StackPanel();
            root.Children.Add(new TextBlock
            {
                Text = "CHOOSE OUTPUT", FontSize = 12, FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.LightGray, Margin = new Thickness(8, 7, 0, 6)
            });
            root.Children.Add(tabs);
            var clear = new Button
            {
                Content = "Clear binding", Padding = new Thickness(10, 5, 10, 5),
                Background = Brushes.Transparent, Foreground = Brushes.White,
                BorderThickness = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Left
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
                BorderBrush = new SolidColorBrush(Color.FromRgb(84, 84, 84)),
                BorderThickness = new Thickness(1), Padding = new Thickness(7)
            };
            popup.IsOpen = true;
        }

        private static TabItem OutputPickerTab(string header, Panel choices)
        {
            var content = new ScrollViewer
            {
                MaxHeight = 415, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = choices
            };
            return new TabItem { Header = header, Content = content,
                Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromRgb(42, 42, 42)) };
        }

        private void AddOutputPickerChoice(Panel panel, Popup popup,
            DeviceConfiguration config, DeviceBindingNode node, BindingVisualDescriptor visual,
            string label, string pluginName, int direction, bool trigger)
        {
            var contents = new StackPanel { Orientation = Orientation.Horizontal };
            contents.Children.Add(new ControlGlyphControl
            {
                Width = 48, Height = 34, Margin = new Thickness(0, 0, 8, 0),
                Kind = visual.ControlKind, AccentBrush = visual.ControlBrush ?? Brushes.Gray,
                Label = visual.ControlLabel
            });
            contents.Children.Add(new TextBlock
            {
                Text = label, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 132
            });
            var button = new Button
            {
                Content = contents, Width = 212, Height = 49, Margin = new Thickness(2),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = new SolidColorBrush(Color.FromRgb(42, 42, 42)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                BorderThickness = new Thickness(1), Padding = new Thickness(8, 3, 5, 3),
                ToolTip = visual.ToolTip
            };
            button.Click += (sender, args) =>
            {
                if (ApplyTypedOutput(config.Guid, node.DeviceBindingInfo, pluginName, direction, trigger))
                    popup.IsOpen = false;
            };
            panel.Children.Add(button);
        }

        private bool ApplyTypedOutput(Guid configuration, DeviceBindingInfo info,
            string pluginName, int direction, bool trigger)
        {
            var mapping = FindAncestor<MappingCardControl>(this)?.DataContext as MappingViewModel;
            if (mapping == null || !mapping.ButtonsEnabled) return false;
            var oldPlugin = mapping.Plugins.FirstOrDefault(plugin =>
                plugin.DeviceBindings.Any(binding => binding.DeviceBinding?.Guid == DeviceBinding?.Guid));
            if (oldPlugin == null) return false;
            var pluginIndex = mapping.Plugins.IndexOf(oldPlugin);
            var outputIndex = oldPlugin.DeviceBindings.ToList().FindIndex(binding =>
                binding.DeviceBinding?.Guid == DeviceBinding.Guid);
            var target = mapping.GetCompatiblePluginOptions().FirstOrDefault(option =>
                string.Equals(option.Name, pluginName, StringComparison.OrdinalIgnoreCase));
            if (target == null)
            {
                HidWizards.UCR.Utilities.DarkMessageBox.Show(
                    "This input mapping cannot use " + pluginName + ".",
                    "Output type unavailable", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }
            if (!mapping.ChangeOutputType(oldPlugin, target.Plugin)) return false;
            var index = Math.Max(0, outputIndex);
            var refreshed = pluginIndex >= 0 && pluginIndex < mapping.Plugins.Count
                ? mapping.Plugins[pluginIndex] : null;
            if (refreshed == null || refreshed.DeviceBindings.Count <= index) return false;
            var binding = refreshed.DeviceBindings[index].DeviceBinding;
            binding.DeviceBindingCategory = info.DeviceBindingCategory;
            binding.SetDeviceConfigurationGuid(configuration);
            binding.SetKeyTypeValue(info.KeyType, info.KeyValue, info.KeySubValue);
            if (direction != 0)
            {
                var property = refreshed.Plugin.GetType().GetProperty("RangePressed");
                if (property != null && property.CanWrite)
                    property.SetValue(refreshed.Plugin, (double)direction, null);
                if (trigger)
                {
                    refreshed.Plugin.GetType().GetProperty("Range")?.SetValue(
                        refreshed.Plugin, -100.0, null);
                    refreshed.Plugin.GetType().GetProperty("Initialize")?.SetValue(
                        refreshed.Plugin, true, null);
                }
            }
            refreshed.Plugin.ContextChanged();
            mapping.RefreshCollapsedSummary();
            return true;
        }

        private void AddFullDirectionalChoice(Panel panel, Popup popup,
            DeviceConfiguration config, string destination)
        {
            var button = new Button
            {
                Content = "Full " + destination, Width = 212, Height = 49,
                Margin = new Thickness(2),
                Background = new SolidColorBrush(Color.FromRgb(42, 42, 42)),
                Foreground = Brushes.White, BorderBrush = Brushes.DimGray,
                ToolTip = "Bind all four directions as one output"
            };
            button.Click += (sender, args) =>
            {
                var mapping = FindAncestor<MappingCardControl>(this)?.DataContext as MappingViewModel;
                if (mapping == null || !mapping.ButtonsEnabled || mapping.Plugins.Count != 1) return;
                var choice = mapping.GetFourWayOutputChoices().FirstOrDefault(option =>
                    option.DeviceConfigurationGuid == config.Guid &&
                    string.Equals(option.Destination, destination, StringComparison.OrdinalIgnoreCase));
                if (choice == null) return;

                var axis = mapping.Plugins[0];
                if (!axis.IsFourButtonAxis)
                {
                    var confirm = HidWizards.UCR.Utilities.DarkMessageBox.Show(
                        "A full directional output requires four independent inputs. " +
                        "This will replace this mapping's existing input expression and bindings. Continue?",
                        "Convert to four-way mapping?", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (confirm != MessageBoxResult.Yes) return;
                    if (!axis.IsAxisButtonMapping)
                    {
                        var standard = mapping.GetCompatiblePluginOptions().FirstOrDefault(option =>
                            string.Equals(option.Name, "Button to Axis", StringComparison.OrdinalIgnoreCase));
                        if (standard == null || !mapping.ChangeOutputType(axis, standard.Plugin)) return;
                        axis = mapping.Plugins[0];
                    }
                    if (!mapping.ChangeAxisInputMode(axis, 4)) return;
                    axis = mapping.Plugins[0];
                }
                if (mapping.ApplyFourWayOutput(axis, choice)) popup.IsOpen = false;
            };
            panel.Children.Add(button);
        }

        private DeviceConfiguration GetSelectedDeviceConfiguration()
        {
            var selected = DeviceSelectionBox?.SelectedItem as ComboBoxItemViewModel;
            if (selected == null || DeviceBinding?.Profile == null) return null;
            return DeviceBinding.Profile.GetDeviceConfiguration(DeviceBinding.DeviceIoType, selected.Value);
        }
    }
}
