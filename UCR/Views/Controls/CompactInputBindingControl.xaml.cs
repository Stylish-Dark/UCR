using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
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

        public static readonly DependencyProperty MenuOnLeftProperty =
            DependencyProperty.Register(nameof(MenuOnLeft), typeof(bool),
                typeof(CompactInputBindingControl),
                new PropertyMetadata(false, (owner, args) =>
                    ((CompactInputBindingControl)owner).UpdateMenuPosition()));

        public bool MenuOnLeft
        {
            get => (bool)GetValue(MenuOnLeftProperty);
            set => SetValue(MenuOnLeftProperty, value);
        }

        // Directional button pairs have half a pane per binding. Keep the
        // existing device / options / blocking actions, but compact their chrome
        // so the binding itself remains usable at ordinary window widths.
        public static readonly DependencyProperty CompactDirectionalProperty =
            DependencyProperty.Register(nameof(CompactDirectional), typeof(bool),
                typeof(CompactInputBindingControl),
                new PropertyMetadata(false, (owner, args) =>
                    ((CompactInputBindingControl)owner).UpdateMenuPosition()));

        public bool CompactDirectional
        {
            get => (bool)GetValue(CompactDirectionalProperty);
            set => SetValue(CompactDirectionalProperty, value);
        }

        // Reuse the exact existing icon/arrow device selector inside a compound
        // input group instead of introducing a differently styled fake button.
        public static readonly DependencyProperty DeviceOnlyProperty =
            DependencyProperty.Register(nameof(DeviceOnly), typeof(bool),
                typeof(CompactInputBindingControl),
                new PropertyMetadata(false, (owner, args) =>
                    ((CompactInputBindingControl)owner).UpdateMenuPosition()));

        public bool DeviceOnly
        {
            get => (bool)GetValue(DeviceOnlyProperty);
            set => SetValue(DeviceOnlyProperty, value);
        }

        private void UpdateMenuPosition()
        {
            if (BindingGrid == null || BindingActionGroup == null || DeviceSelectionBox == null)
                return;
            BindingActionGroup.Visibility = DeviceOnly ? Visibility.Collapsed : Visibility.Visible;
            BindSurface.Visibility = DeviceOnly ? Visibility.Collapsed : Visibility.Visible;
            BindingGrid.ColumnDefinitions[1].Width = DeviceOnly ?
                new GridLength(0) : new GridLength(1, GridUnitType.Star);
            BindingGrid.ColumnDefinitions[3].Width = DeviceOnly ?
                new GridLength(0) : GridLength.Auto;
            Width = DeviceOnly ? 62 : double.NaN;
            if (DeviceOnly)
            {
                BindingGrid.ColumnDefinitions[0].Width = new GridLength(62);
                BindingGrid.ColumnDefinitions[2].Width = new GridLength(0);
                Grid.SetColumn(DeviceSelectionBox, 0);
                return;
            }
            // All binding representations obey the same geometry:
            // device on the left, Bind filling the remaining width.
            var deviceColumnWidth = CompactDirectional ? 50 : 62;
            BindingGrid.ColumnDefinitions[0].Width = new GridLength(deviceColumnWidth);
            BindingGrid.ColumnDefinitions[2].Width = new GridLength(0);
            Grid.SetColumn(DeviceSelectionBox, 0);
            Grid.SetColumn(BindingActionGroup, 1);
            Grid.SetColumnSpan(BindingActionGroup, 2);
        }

        private bool _loaded;
        private Popup _outputPicker;

        public CompactInputBindingControl()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += (sender, args) =>
            {
                _loaded = false;
                CloseOutputPicker();
            };
            DataContextChanged += (sender, args) =>
            {
                CloseOutputPicker();
                if (_loaded)
                    (args.NewValue as DeviceBindingViewModel)?.EnsureSelectedDevicePreviewLoaded();
            };
        }

        public DeviceBinding DeviceBinding
        {
            get => (DeviceBinding)GetValue(DeviceBindingProperty);
            set => SetValue(DeviceBindingProperty, value);
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            UpdateMenuPosition();
            // Apply the loose XAML geometry to these exact named controls after the
            // view's internal layout has been configured.
            HidWizards.UCR.Utilities.LayoutOverrideManager.ApplyToView(this,
                BindingGrid, DeviceSelectionBox, BindingActionGroup, BindSurface, BindButton);
            var viewModel = DataContext as DeviceBindingViewModel;
            viewModel?.EnsureSelectedDevicePreviewLoaded();
            _loaded = true;
        }

        private void DeviceSelectionBox_OnDropDownOpened(object sender, EventArgs e)
        {
            // Opening the selector is the first time the complete list is needed.
            // Suppress transient SelectionChanged events while replacing the preview
            // options so they cannot alter a saved device assignment.
            _loaded = false;
            try
            {
                (DataContext as DeviceBindingViewModel)?.EnsureDeviceListLoaded();
            }
            finally
            {
                _loaded = true;
            }
        }

        private void DeviceSelectionBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_loaded) return;
            var selected = DeviceSelectionBox.SelectedItem as ComboBoxItemViewModel;
            var viewModel = DataContext as DeviceBindingViewModel;
            var binding = viewModel?.DeviceBinding;
            if (selected == null || binding == null || viewModel.IsRefreshingDeviceChoices ||
                !viewModel.Devices.Contains(selected) || selected.Value == binding.DeviceConfigurationGuid) return;

            // Binding refresh/replacement can raise SelectionChanged while a
            // dropdown is merely being rebuilt. Never convert that placeholder
            // into an instruction to clear a real output destination.
            if (binding.DeviceIoType == DeviceIoType.Output && selected.Value == Guid.Empty)
            {
                RestoreDeviceSelection();
                return;
            }

            if (binding.DeviceIoType == DeviceIoType.Output &&
                selected.Value == DeviceBindingViewModel.FilterOutputChoiceGuid)
            {
                var card = FindAncestor<MappingCardControl>(this);
                Dispatcher.BeginInvoke((Action)(() =>
                {
                    if (!_loaded || !ReferenceEquals(DataContext, viewModel) ||
                        !ReferenceEquals(DeviceSelectionBox.SelectedItem, selected)) return;
                    if (card?.SwitchOutputBindingToFilter(viewModel) != true) RestoreDeviceSelection();
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
            else if (DeviceOnly && binding.DeviceIoType == DeviceIoType.Input)
            {
                var mapping = FindAncestor<MappingCardControl>(this)?.DataContext as MappingViewModel;
                if (mapping?.SetCompoundInputDevice(binding.InputExpressionGroup, selected.Value) != true)
                    RestoreDeviceSelection();
            }
            else if (DeviceOnly && binding.DeviceIoType == DeviceIoType.Output)
            {
                var mapping = FindAncestor<MappingCardControl>(this)?.DataContext as MappingViewModel;
                if (mapping?.SetFourWayButtonOutputDevice(selected.Value) != true)
                    RestoreDeviceSelection();
            }
            else
            {
                viewModel.ChangeDeviceConfiguration(selected.Value);
                if (selected.Value != binding.DeviceConfigurationGuid) RestoreDeviceSelection();
            }
            CloseOutputPicker();
        }

        private void RestoreDeviceSelection()
        {
            DeviceSelectionBox.GetBindingExpression(Selector.SelectedItemProperty)?.UpdateTarget();
        }

        private async void BindButton_OnClick(object sender, RoutedEventArgs e)
        {
            // DataContext can arrive after construction. Do not silently ignore a click.
            if (DeviceBinding == null)
                DeviceBinding = (DataContext as DeviceBindingViewModel)?.DeviceBinding;
            if (DeviceBinding == null)
            {
                Logger.Warn("Bind button was clicked without a device binding.");
                HidWizards.UCR.Utilities.DarkMessageBox.Show(
                    "This binding is not initialized. Reopen the mapping and try again.",
                    "Binding unavailable", MessageBoxButton.OK, MessageBoxImage.Warning);
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

            // Keyboard outputs use pressed-key capture; controllers retain the
            // graphical picker with trigger and axis-specific choices.
            try
            {
                var configuration = GetSelectedOutputConfiguration();
                if (configuration?.Device == null)
                {
                    MappingCardControl.ShowOutputPickerStatus(BindButton,
                        DeviceBinding.DeviceConfigurationGuid == Guid.Empty
                            ? "Add an output device to this profile"
                            : "Output device is unavailable.");
                    return;
                }

                if (DeviceBinding.DeviceConfigurationGuid != Guid.Empty &&
                    DeviceVisualCatalog.Describe(configuration, DeviceBinding.Profile, DeviceIoType.Output).Kind ==
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
                ShowOutputPicker(BindButton, configuration);
            }
            catch (Exception exception)
            {
                Logger.Error("Unable to open output binding picker", exception);
                MappingCardControl.ShowOutputPickerStatus(BindButton, "Output controls are unavailable.");
            }
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

        private void BindButton_OnContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (DeviceBinding == null || DeviceBinding.IsInBindMode ||
                (DataContext as DeviceBindingViewModel)?.BindingEnabled != true)
            {
                e.Handled = true;
                return;
            }

            BringIntoView();
            BindingOptions.PlacementTarget = BindButton;
            BindingOptions.Placement = PlacementMode.Top;
            if (DeviceBinding.DeviceIoType == DeviceIoType.Output)
                PopulateOutputOptions();
            else
                PopulateInputMenu(BindingOptions, DeviceBinding, DataContext as DeviceBindingViewModel);
        }

        private void PopulateOutputOptions()
        {
            BindingOptions.Items.Clear();
            var binding = DeviceBinding;
            var choose = new MenuItem
            {
                Header = "Choose output control",
                Foreground = Brushes.White,
                IsEnabled = GetSelectedOutputConfiguration()?.Device != null
            };
            choose.Click += (sender, args) => Dispatcher.BeginInvoke(
                new Action(() => BindButton_OnClick(sender, args)), System.Windows.Threading.DispatcherPriority.Background);
            BindingOptions.Items.Add(choose);

            var clear = new MenuItem
            {
                Header = "Clear binding",
                Foreground = Brushes.White,
                IsEnabled = binding.IsBound
            };
            clear.Click += (sender, args) => binding.ClearBinding();
            BindingOptions.Items.Add(clear);
        }

        // The collapsed mapping glyph and the editor binding button use exactly the
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
                var configurations = profile.GetDeviceConfigurationList(DeviceIoType.Input)
                    .Where(configuration => configuration?.Device != null &&
                        (binding.DeviceConfigurationGuid == Guid.Empty ||
                         binding.DeviceConfigurationGuid == configuration.Guid))
                    .ToList();
                foreach (var configuration in configurations)
                {
                    if (configurations.Count == 1)
                    {
                        AddInputControls(menu.Items, profile, configuration, binding);
                        continue;
                    }

                    var device = new MenuItem
                    {
                        Header = configuration.GetFullTitleForProfile(profile),
                        Foreground = Brushes.White
                    };
                    AddInputControls(device.Items, profile, configuration, binding);
                    if (device.HasItems) menu.Items.Add(device);
                }
            }
            if (menu.Items.Count > 0) menu.Items.Add(new Separator());
            var clear = new MenuItem { Header = "Clear binding", Foreground = Brushes.White,
                IsEnabled = binding?.IsBound == true };
            clear.Click += (s, e) => binding?.ClearBinding();
            menu.Items.Add(clear);
        }

        private static void AddInputControls(ItemCollection items, Profile profile,
            DeviceConfiguration configuration, DeviceBinding binding)
        {
            var nodes = configuration.Device.GetDeviceBindingMenu(profile.Context, DeviceIoType.Input);
            foreach (var node in nodes)
            {
                var controls = IsRedundantPovRoot(node) ? node.ChildrenNodes
                    : IsKeyboardRoot(node) ? DeviceBindingControl.BuildKeyboardCategories(node.ChildrenNodes)
                    : new List<DeviceBindingNode> { node };
                foreach (var control in controls)
                {
                    var item = BuildInputMenuItem(control, configuration.Guid, binding);
                    if (item != null) items.Add(item);
                }
            }
        }

        private static bool IsRedundantPovRoot(DeviceBindingNode node)
        {
            return node != null &&
                   string.Equals(node.Title, "POV", StringComparison.OrdinalIgnoreCase) &&
                   node.ChildrenNodes != null && node.ChildrenNodes.Count > 0 &&
                   node.ChildrenNodes.All(child => child?.Title != null &&
                       child.Title.StartsWith("POV", StringComparison.OrdinalIgnoreCase) &&
                       child.Title.Length > 3 && child.Title.Substring(3).All(char.IsDigit));
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
                    if (binding.Profile.Context.SubscriptionsManager.IsTransitionInProgress ||
                        binding.Profile.IsActive() ||
                        binding.Profile.GetDeviceConfiguration(DeviceIoType.Input, configurationGuid) == null) return;
                    binding.AssignPhysicalControl(configurationGuid, info.DeviceBindingCategory,
                        info.KeyType, info.KeyValue, info.KeySubValue, node.Title,
                        info.DeviceBindingCategory == DeviceBindingCategory.Range ? 3 : 0);
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
                // The binding already specifies Momentary or Range. Never let a
                // button capture auto-promote a drifting DirectInput axis.
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

        // Classify by HID semantics: analogue triggers are full-range axes,
        // never a separate 'Trigger' representation or an ordinary button.
        public static string OutputPickerCategory(DeviceBindingCategory category,
            ControlVisualKind visualKind, string title)
        {
            if (category == DeviceBindingCategory.Range) return "Axes";
            if (category == DeviceBindingCategory.Event) return "Events";
            if (category != DeviceBindingCategory.Momentary) return null;
            var label = (title ?? string.Empty).Trim().ToLowerInvariant();
            var isDirection = visualKind == ControlVisualKind.DPad ||
                label.Contains("d-pad") || label.Contains("dpad") ||
                label.Contains("pov") || label.Contains("hat") ||
                label == "up" || label == "down" || label == "left" ||
                label == "right" || label == "north" || label == "south" ||
                label == "east" || label == "west";
            return isDirection ? "Directions" : "Buttons";
        }

        public static int PreferredOutputPickerTab(DeviceBindingCategory current,
            int buttonCount, int directionCount, int axisCount)
        {
            if (current == DeviceBindingCategory.Range && axisCount > 0) return 2;
            if (current == DeviceBindingCategory.Momentary && buttonCount > 0) return 0;
            // When the configured device lacks the current category, prefer
            // ordinary buttons before digital directions, never an empty tab.
            if (buttonCount > 0) return 0;
            if (directionCount > 0) return 1;
            if (axisCount > 0) return 2;
            return 0;
        }

        // The vector glyph already prints abbreviated controller codes.
        // Give the adjacent caption a human-readable name instead of repeating
        // "LX LX" or "LT LT" in each control tile.
        public static string OutputPickerDisplayName(string title,
            ControlVisualKind visualKind = ControlVisualKind.Unknown)
        {
            switch ((title ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "LX": return "Left stick X axis";
                case "LY": return "Left stick Y axis";
                case "RX": return "Right stick X axis";
                case "RY": return "Right stick Y axis";
                case "LT":
                case "L2": return "Left trigger";
                case "RT":
                case "R2": return "Right trigger";
                case "LB":
                case "L1": return "Left shoulder";
                case "RB":
                case "R1": return "Right shoulder";
                case "A":
                case "B":
                case "X":
                case "Y":
                    // A keyboard key called "A" is not an Xbox face button.
                    return visualKind == ControlVisualKind.XboxFaceButton ||
                           visualKind == ControlVisualKind.PlayStationFaceButton
                        ? "Face button " + title.Trim().ToUpperInvariant()
                        : title ?? string.Empty;
                default: return title ?? string.Empty;
            }
        }

        private void CloseOutputPicker()
        {
            if (_outputPicker != null) _outputPicker.IsOpen = false;
        }

        // Output controls come directly from the selected device's provider descriptors.
        private void ShowOutputPicker(FrameworkElement target, DeviceConfiguration config)
        {
            CloseOutputPicker();
            if (config?.Device == null || DeviceBinding.Profile.Context == null) return;

            var popup = new Popup
            {
                PlacementTarget = target,
                Placement = PlacementMode.Bottom,
                AllowsTransparency = true,
                StaysOpen = false
            };
            var content = BuildOutputPickerContent(config, popup);
            content.PreviewKeyDown += (sender, args) =>
            {
                if (args.Key != Key.Escape) return;
                args.Handled = true;
                popup.IsOpen = false;
                if (target.IsVisible && target.IsEnabled) target.Focus();
            };
            popup.Child = content;
            popup.Opened += (sender, args) =>
            {
                content.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
            };
            popup.Closed += (sender, args) =>
            {
                if (ReferenceEquals(_outputPicker, popup)) _outputPicker = null;
            };
            _outputPicker = popup;
            popup.IsOpen = true;
        }

        // Real chooser visual is independently measurable and renderable in
        // headless WPF CI without requiring a popup HWND or a user desktop.
        private Border BuildOutputPickerContent(DeviceConfiguration config, Popup popup)
        {
            var mapping = FindAncestor<MappingCardControl>(this)?.DataContext
                as MappingViewModel;
            var bindingViewModel = DataContext as DeviceBindingViewModel;
            var outputDevice = config.Device;
            var selectedBinding = DeviceBinding;
            var currentCategory = selectedBinding.DeviceBindingCategory;
            var options = mapping?.GetOutputBindingOptions(bindingViewModel, config)
                ?? new List<QuickBindingOption>();
            var workArea = SystemParameters.WorkArea;
            var popupWidth = Math.Min(470, Math.Max(240, workArea.Width - 24));
            var contentWidth = popupWidth - 30;
            var tileWidth = contentWidth >= 430 ? 212 : contentWidth - 8;
            var availableHeight = Math.Max(100, Math.Min(340, workArea.Height - 150));
            var buttons = new WrapPanel { Width = contentWidth };
            var directionals = new WrapPanel { Width = contentWidth };
            var axes = new WrapPanel { Width = contentWidth };
            var events = new WrapPanel { Width = contentWidth };
            string selectedCategory = null;

            foreach (var option in options)
            {
                var info = option.OutputControl;
                var visual = option.Visual;
                var title = option.Title ?? string.Empty;
                var category = OutputPickerCategory(info.DeviceBindingCategory, visual.ControlKind, title);
                if (category == null) continue;
                var isSelectedControl = DeviceBinding.IsBound && DeviceBinding.KeyType == info.KeyType &&
                    DeviceBinding.KeyValue == info.KeyValue && DeviceBinding.KeySubValue == info.KeySubValue;
                var isCurrentChoice = isSelectedControl && DeviceBinding.OutputAxisDirection == 0;
                if (isCurrentChoice) selectedCategory = category;

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
                    Text = OutputPickerDisplayName(title, visual.ControlKind),
                    Foreground = Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = Math.Max(80, tileWidth - 65)
                });
                var choice = new Button
                {
                    Content = row,
                    Width = tileWidth,
                    MinHeight = 46,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(3),
                    Padding = new Thickness(8, 4, 5, 4),
                    Background = new SolidColorBrush(Color.FromRgb(43, 43, 43)),
                    Foreground = Brushes.White,
                    BorderBrush = isCurrentChoice ? Brushes.SteelBlue : Brushes.DimGray,
                    BorderThickness = new Thickness(1),
                    ToolTip = title
                };
                AutomationProperties.SetName(choice, OutputPickerDisplayName(title, visual.ControlKind));
                var selectedInfo = info;
                choice.Click += (sender, args) =>
                {
                    var card = FindAncestor<MappingCardControl>(this);
                    var bindingVm = DataContext as DeviceBindingViewModel;
                    if (card != null && bindingVm != null)
                    {
                        // Apply before closing. Replacing the output plug-in can
                        // invalidate the old binding view-model during a deferred callback.
                        if (card.ApplyOutputControlChoice(bindingVm, config.Guid, outputDevice, selectedInfo, controlName: title))
                            popup.IsOpen = false;
                    }
                };
                if (category == "Axes") axes.Children.Add(choice);
                else if (category == "Directions") directionals.Children.Add(choice);
                else if (category == "Buttons") buttons.Children.Add(choice);
                else if (category == "Events") events.Children.Add(choice);

                // Each stick axis also offers its two signed directions. A selected
                // direction configures the same underlying range axis, with an
                // appropriate signed output value, rather than inventing HID buttons.
                var axis = visual.ControlLabel;
                if (info.DeviceBindingCategory == DeviceBindingCategory.Range &&
                    (axis == "LX" || axis == "LY" || axis == "RX" || axis == "RY"))
                {
                    if (isSelectedControl && DeviceBinding.OutputAxisDirection != 0)
                        selectedCategory = "Directions";
                    var stick = axis.StartsWith("L", StringComparison.Ordinal) ? "Left Stick" : "Right Stick";
                    var horizontal = axis.EndsWith("X", StringComparison.Ordinal);
                    foreach (var direction in new[] {
                        new { Name = horizontal ? "Left" : "Up", Sign = -1 },
                        new { Name = horizontal ? "Right" : "Down", Sign = 1 }
                    })
                    {
                        var selectedSign = direction.Sign;
                        var label = stick + " " + direction.Name;
                        // Signed stick directions are virtual half-axis choices.
                        // Give them a directional icon so they cannot be mistaken
                        // for a second full axis or an ordinary face button.
                        var glyph = horizontal
                            ? (selectedSign < 0 ? "←" : "→")
                            : (selectedSign < 0 ? "↑" : "↓");
                        var directionalContent = new StackPanel { Orientation = Orientation.Horizontal };
                        directionalContent.Children.Add(new TextBlock
                        {
                            Text = glyph,
                            FontSize = 23,
                            FontWeight = FontWeights.SemiBold,
                            Width = 41,
                            Margin = new Thickness(0, 0, 8, 0),
                            TextAlignment = TextAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                            Foreground = Brushes.White
                        });
                        directionalContent.Children.Add(new TextBlock
                        {
                            Text = label,
                            VerticalAlignment = VerticalAlignment.Center,
                            Foreground = Brushes.White
                        });
                        var directional = new Button
                        {
                            Content = directionalContent,
                            Width = tileWidth,
                            MinHeight = 46,
                            HorizontalContentAlignment = HorizontalAlignment.Left,
                            Margin = new Thickness(3),
                            Padding = new Thickness(8, 4, 5, 4),
                            Background = new SolidColorBrush(Color.FromRgb(43, 43, 43)),
                            Foreground = Brushes.White,
                            BorderBrush = isSelectedControl && DeviceBinding.OutputAxisDirection == selectedSign
                                ? Brushes.SteelBlue : Brushes.DimGray,
                            BorderThickness = new Thickness(1),
                            ToolTip = label
                        };
                        AutomationProperties.SetName(directional, label);
                        directional.Click += (sender, args) =>
                        {
                            var card = FindAncestor<MappingCardControl>(this);
                            var bindingVm = DataContext as DeviceBindingViewModel;
                            if (card != null && bindingVm != null)
                            {
                                if (card.ApplyOutputControlChoice(bindingVm, config.Guid, outputDevice, selectedInfo, selectedSign))
                                    popup.IsOpen = false;
                            }
                        };
                        directionals.Children.Add(directional);
                    }
                }
            }

            // WrapPanel is row-major. Order each direction pair consistently,
            // regardless of provider enumeration (X-before-Y or Y-before-X).
            // Each button's Content is a StackPanel, not a string: use ToolTip.
            var nextPosition = 0;
            foreach (var stickName in new[] { "Left Stick", "Right Stick" })
            foreach (var directionName in new[] { "Up", "Down", "Left", "Right" })
            {
                var title = stickName + " " + directionName;
                var item = directionals.Children.OfType<Button>()
                    .FirstOrDefault(button => string.Equals(button.ToolTip as string, title,
                        StringComparison.OrdinalIgnoreCase));
                if (item == null) continue;
                directionals.Children.Remove(item);
                directionals.Children.Insert(nextPosition++, item);
            }

            // A D-pad follows the same two-column direction pairing: Up/Down,
            // then Left/Right. The device provider may enumerate clockwise
            // (Up/Right/Down/Left), which otherwise produces inconsistent rows.
            foreach (var directionName in new[] { "Up", "Down", "Left", "Right" })
            {
                var title = "D-pad " + directionName;
                var item = directionals.Children.OfType<Button>()
                    .FirstOrDefault(button => string.Equals(button.ToolTip as string, title,
                        StringComparison.OrdinalIgnoreCase));
                if (item == null) continue;
                directionals.Children.Remove(item);
                directionals.Children.Insert(nextPosition++, item);
            }

            var selectedTab = PreferredOutputPickerTab(currentCategory,
                buttons.Children.Count, directionals.Children.Count, axes.Children.Count);
            var preferredCategory = selectedCategory ?? new[] { "Buttons", "Directions", "Axes" }[selectedTab];
            var tabs = new TabControl { Background = Brushes.Transparent };
            foreach (var category in new[] {
                new { Title = "Buttons", Items = buttons },
                new { Title = "Directions", Items = directionals },
                new { Title = "Axes", Items = axes },
                new { Title = "Events", Items = events }
            })
            {
                var count = category.Items.Children.Count;
                // A device should not present tabs with no selectable controls.
                if (count == 0) continue;
                // Two 212-DIP tiles per row; size to actual content, with a
                // scrollbar only when the selected category needs it.
                tabs.Items.Add(new TabItem
                {
                    Header = category.Title,
                    Style = TryFindResource("OutputPickerTabStyle") as Style,
                    Content = new ScrollViewer
                    {
                        Content = category.Items,
                        MaxHeight = availableHeight,
                        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                    }
                });
            }
            var matchingTab = tabs.Items.Cast<TabItem>().ToList().FindIndex(
                item => string.Equals(item.Header as string, preferredCategory,
                    StringComparison.Ordinal));
            if (tabs.Items.Count > 0)
                tabs.SelectedIndex = matchingTab >= 0 ? matchingTab : 0;
            // Preserve the category chosen for this binding; never force Buttons.
            var root = new StackPanel();
            KeyboardNavigation.SetTabNavigation(root, KeyboardNavigationMode.Cycle);
            AutomationProperties.SetAutomationId(root, "UiOutputPicker");
            var deviceTitle = config.GetFullTitleForProfile(DeviceBinding.Profile);
            var header = new DockPanel { Margin = new Thickness(8, 7, 8, 8),
                LastChildFill = true };
            header.Children.Add(new TextBlock
            {
                Text = "CHOOSE OUTPUT",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.LightGray,
                Width = 126
            });
            header.Children.Add(new TextBlock
            {
                Text = deviceTitle,
                FontSize = 11,
                Foreground = Brushes.LightGray,
                Opacity = 0.8,
                TextAlignment = TextAlignment.Right,
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = deviceTitle
            });
            root.Children.Add(header);
            if (tabs.Items.Count > 0)
                root.Children.Add(tabs);
            else
                root.Children.Add(new TextBlock
                {
                    Text = mapping?.GetOutputPickerUnavailableReason(bindingViewModel)
                        ?? "The output slot is unavailable.",
                    Foreground = Brushes.LightGray,
                    Margin = new Thickness(12, 10, 12, 10),
                    TextWrapping = TextWrapping.Wrap
                });
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
                if (ReferenceEquals(DeviceBinding, selectedBinding) && bindingViewModel?.BindingEnabled == true &&
                    mapping?.CanApplyOutputBinding == true)
                    selectedBinding.ClearBinding();
                popup.IsOpen = false;
            };
            root.Children.Add(clear);
            return new Border
            {
                Child = root,
                Background = new SolidColorBrush(Color.FromRgb(32, 32, 32)),
                BorderBrush = Brushes.DimGray,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(5),
                Width = popupWidth
            };
        }

        private DeviceConfiguration GetSelectedOutputConfiguration()
        {
            return (DataContext as DeviceBindingViewModel)?.ResolveOutputConfiguration();
        }
    }
}
