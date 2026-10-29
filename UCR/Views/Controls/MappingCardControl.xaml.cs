using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using HidWizards.UCR.Core.Utilities;
using HidWizards.UCR.Core.Models;
using HidWizards.UCR.Core.Models.Binding;
using HidWizards.UCR.Utilities;
using HidWizards.UCR.ViewModels;
using HidWizards.UCR.ViewModels.Presentation;
using HidWizards.UCR.ViewModels.ProfileViewModels;

namespace HidWizards.UCR.Views.Controls
{
    public partial class MappingCardControl : UserControl
    {
        public static readonly DependencyProperty InspectorModeProperty = DependencyProperty.Register(
            nameof(InspectorMode), typeof(bool), typeof(MappingCardControl),
            new PropertyMetadata(false));

        public bool InspectorMode
        {
            get => (bool)GetValue(InspectorModeProperty);
            set => SetValue(InspectorModeProperty, value);
        }
        public static readonly DependencyProperty InspectorSectionProperty = DependencyProperty.Register(
            nameof(InspectorSection), typeof(string), typeof(MappingCardControl),
            new PropertyMetadata("All"));

        public string InspectorSection
        {
            get => (string)GetValue(InspectorSectionProperty);
            set => SetValue(InspectorSectionProperty, value);
        }

        public static readonly DependencyProperty InlineWorkspaceModeProperty = DependencyProperty.Register(
            nameof(InlineWorkspaceMode), typeof(bool), typeof(MappingCardControl), new PropertyMetadata(false));

        public bool InlineWorkspaceMode
        {
            get => (bool)GetValue(InlineWorkspaceModeProperty);
            set => SetValue(InlineWorkspaceModeProperty, value);
        }

        private MappingViewModel _observedMapping;

        public MappingCardControl()
        {
            InitializeComponent();
            Loaded += MappingCardControl_OnLoaded;
            Unloaded += MappingCardControl_OnUnloaded;
            DataContextChanged += MappingCardControl_OnDataContextChanged;
        }

        private void MappingCardControl_OnLoaded(object sender, RoutedEventArgs e)
        {
            ObserveMapping();
            RefreshExpandedVisual(false);
        }

        private void MappingCardControl_OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (_observedMapping != null)
                _observedMapping.PropertyChanged -= ObservedMappingOnPropertyChanged;
            _observedMapping = null;
        }

        private void MappingCardControl_OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            ObserveMapping();
            RefreshExpandedVisual(false);
        }

        private void ObserveMapping()
        {
            var mapping = DataContext as MappingViewModel;
            if (ReferenceEquals(mapping, _observedMapping)) return;
            if (_observedMapping != null)
                _observedMapping.PropertyChanged -= ObservedMappingOnPropertyChanged;
            _observedMapping = mapping;
            if (_observedMapping != null)
                _observedMapping.PropertyChanged += ObservedMappingOnPropertyChanged;
        }

        private void ObservedMappingOnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(MappingViewModel.IsExpanded)) return;
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => RefreshExpandedVisual(true)));
                return;
            }
            RefreshExpandedVisual(true);
        }

        private void RefreshExpandedVisual(bool animateCollapse)
        {
            if (MappingHeaderSurface == null || ExpandedBodyHost == null) return;
            if (InspectorMode)
            {
                MappingHeaderSurface.Visibility = Visibility.Collapsed;
                ShowExpandedBody();
                return;
            }

            MappingHeaderSurface.Visibility = Visibility.Visible;
            if ((DataContext as MappingViewModel)?.IsExpanded == true)
                ShowExpandedBody(animateCollapse);
            else
                HideExpandedBody(animateCollapse);
        }

        private void ExpandMappingButton_OnClick(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (InspectorMode) return;
            var mapping = DataContext as MappingViewModel;
            if (mapping == null) return;
            mapping.IsExpanded = !mapping.IsExpanded;
            // The property-change observer performs the transition once.
            // Avoid a second refresh, which would cancel the animation.
            if (!ReferenceEquals(mapping, _observedMapping))
                RefreshExpandedVisual(true);
        }

        // The inspector embeds its editor in the workspace's animated row; it
        // must not animate a second time. Standalone cards sweep their own body.
        // Initialize heavy editor content only when it is actually expanded.
        private void ShowExpandedBody(bool animate = false)
        {
            if (ExpandedBodyHost == null) return;
            var mapping = DataContext as MappingViewModel;
            if (mapping != null)
                foreach (var plugin in mapping.Plugins)
                    plugin.EnsureEditorInitialized();

            var alreadyVisible = ExpandedBodyHost.Visibility == Visibility.Visible;
            ExpandedBodyHost.BeginAnimation(FrameworkElement.HeightProperty, null);
            ExpandedBodyHost.BeginAnimation(OpacityProperty, null);
            if (ExpandedBodyHost.ContentTemplate == null)
                ExpandedBodyHost.ContentTemplate = FindResource("ExpandedMappingBodyTemplate") as DataTemplate;

            ExpandedBodyHost.Visibility = Visibility.Visible;
            ExpandedBodyHost.Opacity = 1;
            ExpandedBodyHost.Height = double.NaN;
            if (!animate || InspectorMode || alreadyVisible) return;

            // Determine the completed height before beginning the sweep.
            ExpandedBodyHost.Measure(new Size(Math.Max(1, ActualWidth), double.PositiveInfinity));
            var targetHeight = ExpandedBodyHost.DesiredSize.Height;
            if (targetHeight <= 0) return;
            ExpandedBodyHost.Height = 0;
            var sweep = new DoubleAnimation(0, targetHeight, TimeSpan.FromMilliseconds(190))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            sweep.Completed += (sender, args) =>
            {
                if ((DataContext as MappingViewModel)?.IsExpanded == true)
                {
                    ExpandedBodyHost.BeginAnimation(FrameworkElement.HeightProperty, null);
                    ExpandedBodyHost.Height = double.NaN;
                }
            };
            ExpandedBodyHost.BeginAnimation(FrameworkElement.HeightProperty, sweep);
        }

        private void HideExpandedBody(bool animate = false)
        {
            if (ExpandedBodyHost == null) return;
            ExpandedBodyHost.BeginAnimation(FrameworkElement.HeightProperty, null);
            ExpandedBodyHost.BeginAnimation(OpacityProperty, null);
            if (animate && !InspectorMode && ExpandedBodyHost.Visibility == Visibility.Visible)
            {
                var startHeight = ExpandedBodyHost.ActualHeight;
                if (startHeight > 0)
                {
                    ExpandedBodyHost.Height = startHeight;
                    var sweep = new DoubleAnimation(startHeight, 0, TimeSpan.FromMilliseconds(150))
                    {
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
                    };
                    sweep.Completed += (sender, args) =>
                    {
                        if ((DataContext as MappingViewModel)?.IsExpanded != true)
                            HideExpandedBody(false);
                    };
                    ExpandedBodyHost.BeginAnimation(FrameworkElement.HeightProperty, sweep);
                    return;
                }
            }

            ExpandedBodyHost.Visibility = Visibility.Collapsed;
            ExpandedBodyHost.Height = 0;
            ExpandedBodyHost.Opacity = 0;
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

        public static bool RequiresDeleteConfirmation(MappingViewModel mapping)
        {
            if (mapping?.Mapping == null) return false;
            // An empty mapping is disposable without interruption. A configured
            // source OR destination is user work worth confirming before deletion.
            return (mapping.Mapping.DeviceBindings?.Any(binding => binding?.IsBound == true) == true) ||
                   (mapping.Mapping.Plugins?.Any(plugin =>
                       plugin?.Outputs?.Any(binding => binding?.IsBound == true) == true) == true);
        }

        private void Remove_OnClick(object sender, RoutedEventArgs e)
        {
            var mappingViewModel = DataContext as MappingViewModel;
            if (mappingViewModel == null) return;
            if (RequiresDeleteConfirmation(mappingViewModel) && MessageBox.Show("Delete mapping '" + mappingViewModel.MappingTitle + "'?", "Delete mapping", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            mappingViewModel.Remove();
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

        // Every visible binding glyph is a Button in MappingBindingVisualTemplate.
        // Its parent lane determines whether to detect an input or choose an output.
        private void QuickBindGlyph_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var button = sender as Button;
            var descriptor = button?.DataContext as BindingVisualDescriptor;
            var binding = (DataContext as MappingViewModel)?
                .ResolveCollapsedBinding(descriptor, DeviceIoType.Input)?.DeviceBinding;
            if (binding?.IsInBindMode != true) return;
            binding.CancelBindMode();
            e.Handled = true;
        }

        private void QuickBindGlyph_OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var button = sender as Button;
            var descriptor = button?.DataContext as BindingVisualDescriptor;
            var mapping = DataContext as MappingViewModel;
            var binding = mapping?.ResolveCollapsedBinding(descriptor, DeviceIoType.Input)?.DeviceBinding;
            if (binding?.IsInBindMode != true) return;
            binding.CancelBindMode();
            e.Handled = true;
        }

        private async void QuickBindGlyph_OnClick(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            var button = sender as Button;
            var mapping = DataContext as MappingViewModel;
            var descriptor = button?.DataContext as BindingVisualDescriptor;
            if (mapping == null || descriptor == null || !mapping.ButtonsEnabled) return;

            string lane = null;
            for (DependencyObject current = button; current != null; current = VisualTreeHelper.GetParent(current))
            {
                var items = current as ItemsControl;
                var tag = items?.Tag as string;
                if (tag == "Input" || tag == "Output")
                {
                    lane = tag;
                    break;
                }
            }

            if (lane == "Input")
            {
                try
                {
                    var existing = mapping.ResolveCollapsedBinding(descriptor, DeviceIoType.Input);
                    if (existing?.DeviceBinding?.IsInBindMode == true)
                        existing.DeviceBinding.CancelBindMode();
                    else
                        mapping.QuickBindInput(descriptor);
                }
                catch (Exception exception)
                {
                    Logger.Error("Failed to start mapping-card quick input bind", exception);
                    mapping.InputPresetFeedback = "Input detection could not start. Details are in the log.";
                }
            }
            else if (lane == "Output")
            {
                await OpenQuickOutputPickerAsync(mapping, descriptor, button);
            }
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

        internal static async Task<bool> OpenQuickOutputPickerAsync(MappingViewModel mappingViewModel,
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
                mappingViewModel.OutputSelectionFeedback = "Output binding could not start. Details are in the log.";
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
                mappingViewModel.OutputSelectionFeedback =
                    mappingViewModel.OutputSideDeviceGuid == Guid.Empty
                        ? "Choose an output device before binding a destination."
                        : "No compatible output controls are available for this device.";
                return;
            }
            else
            {
                mappingViewModel.OutputSelectionFeedback = null;
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

        private static System.Collections.Generic.IEnumerable<DeviceBindingNode> FlattenPickerNodes(
            System.Collections.Generic.IEnumerable<DeviceBindingNode> nodes)
        {
            if (nodes == null) yield break;
            foreach (var node in nodes)
            {
                if (node == null) continue;
                if (node.DeviceBindingInfo != null) yield return node;
                foreach (var child in FlattenPickerNodes(node.ChildrenNodes)) yield return child;
            }
        }

        private static MenuItem PickerItem(string label)
        {
            return new MenuItem
            {
                Header = label, Foreground = Brushes.White,
                Background = Brushes.Transparent, Padding = new Thickness(10, 6, 14, 6)
            };
        }

        private static string KeyboardSection(string title)
        {
            var s = (title ?? "").Trim();
            if (s.Length == 1 && char.IsLetter(s[0])) return "Letters";
            if (s.Length == 1 && char.IsDigit(s[0])) return "Numbers";
            if (s.StartsWith("F", StringComparison.OrdinalIgnoreCase) && s.Length > 1 &&
                s.Skip(1).All(char.IsDigit)) return "Function keys";
            if (s.IndexOf("numpad", StringComparison.OrdinalIgnoreCase) >= 0 ||
                s.IndexOf("keypad", StringComparison.OrdinalIgnoreCase) >= 0) return "Numpad";
            if (new[] { "Up", "Down", "Left", "Right", "Home", "End",
                "PageUp", "PageDown", "Insert", "Delete" }.Any(x =>
                string.Equals(x, s, StringComparison.OrdinalIgnoreCase))) return "Navigation";
            return "Other keys";
        }

        // Clicking OUTPUT lists real controls and complete presets. One
        // control choice implicitly establishes the opposing slot structure.
        internal void OpenUnifiedOutputPicker(DeviceBindingViewModel binding, FrameworkElement target)
        {
            var mapping = DataContext as MappingViewModel;
            if (mapping?.ButtonsEnabled != true || binding == null || target == null) return;
            mapping.OutputSelectionFeedback = null;
            var profile = mapping.ProfileViewModel.Profile;
            var guid = mapping.OutputSideDeviceGuid;
            var device = profile.GetDeviceConfiguration(DeviceIoType.Output, guid);
            if (device?.Device == null)
            {
                mapping.OutputSelectionFeedback = "Choose the output device using its icon above.";
                return;
            }
            var menu = CreateDarkContextMenu(target);
            menu.Placement = PlacementMode.Bottom;
            System.Collections.Generic.List<DeviceBindingNode> choices;
            try
            {
                choices = FlattenPickerNodes(profile.Context.DevicesManager.GetDeviceBindingMenu(
                    device.Device, DeviceIoType.Output)).ToList();
            }
            catch (Exception error)
            {
                Logger.Error("Unable to enumerate output controls", error);
                mapping.OutputSelectionFeedback = "The selected device has no available output controls.";
                return;
            }
            var keyboard = DeviceVisualCatalog.Describe(device, profile, DeviceIoType.Output)
                .Kind == DeviceVisualKind.Keyboard;
            foreach (var category in new[]
            {
                new { Kind = DeviceBindingCategory.Momentary, Caption = "Button" },
                new { Kind = DeviceBindingCategory.Range, Caption = "Axis" }
            })
            {
                var kind = category.Kind;
                var nodes = choices.Where(node =>
                    node.DeviceBindingInfo.DeviceBindingCategory == kind).ToList();
                if (nodes.Count == 0 || !mapping.OutputDeviceSupports(kind)) continue;
                var categoryMenu = PickerItem(category.Caption);
                var groups = nodes.GroupBy(node => keyboard && kind ==
                    DeviceBindingCategory.Momentary ? KeyboardSection(node.Title) : "Controls");
                foreach (var group in groups)
                {
                    var section = keyboard && kind == DeviceBindingCategory.Momentary
                        ? PickerItem(group.Key) : categoryMenu;
                    foreach (var node in group)
                    {
                        var info = node.DeviceBindingInfo;
                        var item = PickerItem(node.Title);
                        item.Click += (s, args) =>
                        {
                            if (!mapping.ChooseSingleOutputControl(binding, kind, guid, info) &&
                                string.IsNullOrEmpty(mapping.OutputSelectionFeedback))
                                mapping.OutputSelectionFeedback = "That output control is unavailable.";
                        };
                        section.Items.Add(item);
                    }
                    if (section != categoryMenu) categoryMenu.Items.Add(section);
                }
                menu.Items.Add(categoryMenu);
            }
            var presets = mapping.GetFourWayOutputChoices()
                .Where(choice => choice.DeviceConfigurationGuid == guid).ToList();
            var sticks = presets.Where(choice => !choice.IsDPad).ToList();
            if (sticks.Count > 0)
            {
                var directionMenu = PickerItem("Stick direction");
                foreach (var stick in sticks)
                {
                    var stickItem = PickerItem(stick.Destination);
                    for (var index = 0; index < 4; index++)
                    {
                        var selectedStick = stick;
                        var selectedDirection = index;
                        var direction = PickerItem(new[] { "← Left", "→ Right",
                            "↑ Up", "↓ Down" }[index]);
                        direction.Click += (s, args) =>
                            mapping.ChooseStickDirection(selectedStick, selectedDirection);
                        stickItem.Items.Add(direction);
                    }
                    directionMenu.Items.Add(stickItem);
                }
                menu.Items.Add(directionMenu);
                var stickMenu = PickerItem("Full joystick");
                foreach (var choice in sticks)
                {
                    var chosen = choice;
                    var item = PickerItem(chosen.Destination);
                    item.Click += (s, args) => mapping.ApplyFullJoystickOutput(chosen);
                    stickMenu.Items.Add(item);
                }
                menu.Items.Add(stickMenu);
            }
            var presetMenu = BuildOutputPresetPicker(mapping, keyboard, presets);
            if (presetMenu.Items.Count > 0) menu.Items.Add(presetMenu);
            if (menu.Items.Count == 0)
                menu.Items.Add(new MenuItem { Header = "No compatible output controls", IsEnabled = false });
            menu.IsOpen = true;
        }

        private static MenuItem BuildOutputPresetPicker(MappingViewModel mapping, bool keyboard,
            System.Collections.Generic.IEnumerable<FourWayOutputChoice> choices)
        {
            var menu = PickerItem("Preset");
            if (keyboard)
                foreach (var name in new[] { "WASD", "Arrow Keys", "Numpad", "UHJL" })
                {
                    var preset = name;
                    var item = PickerItem(preset);
                    item.Click += (s, args) => mapping.ApplyOutputKeyboardPreset(preset);
                    menu.Items.Add(item);
                }
            foreach (var choice in choices.Where(item => item.IsDPad))
            {
                var selected = choice;
                var item = PickerItem("D-pad");
                item.Click += (s, args) => mapping.ApplyOutputDPadPreset(selected);
                menu.Items.Add(item);
            }
            return menu;
        }

        private void OpenOutputPreset_OnClick(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var mapping = DataContext as MappingViewModel;
            var group = button?.DataContext as OutputBindingSetViewModel;
            if (mapping?.ButtonsEnabled != true || group == null) return;
            var guid = mapping.OutputSideDeviceGuid;
            var profile = mapping.ProfileViewModel.Profile;
            var device = profile.GetDeviceConfiguration(DeviceIoType.Output, guid);
            if (device?.Device == null)
            {
                mapping.OutputSelectionFeedback = "Choose an output device first.";
                return;
            }
            var keyboard = DeviceVisualCatalog.Describe(device, profile, DeviceIoType.Output)
                .Kind == DeviceVisualKind.Keyboard;
            var choices = mapping.GetFourWayOutputChoices()
                .Where(choice => choice.DeviceConfigurationGuid == guid).ToList();
            var menu = CreateDarkContextMenu(button);
            menu.Placement = PlacementMode.Bottom;
            if (group.IsFullJoystick)
            {
                foreach (var choice in choices.Where(choice => !choice.IsDPad))
                {
                    var picked = choice;
                    var item = PickerItem(picked.Destination);
                    item.Click += (s, args) => mapping.ApplyFullJoystickOutput(picked);
                    menu.Items.Add(item);
                }
            }
            else
            {
                var presets = BuildOutputPresetPicker(mapping, keyboard, choices);
                foreach (var item in presets.Items.OfType<MenuItem>().ToList())
                {
                    presets.Items.Remove(item);
                    menu.Items.Add(item);
                }
            }
            if (menu.Items.Count == 0)
                menu.Items.Add(new MenuItem { Header = "No presets for this device", IsEnabled = false });
            menu.IsOpen = true;
        }

        private void RemoveOutputSet_OnClick(object sender, RoutedEventArgs e)
        {
            var group = (sender as FrameworkElement)?.DataContext as OutputBindingSetViewModel;
            var mapping = DataContext as MappingViewModel;
            if (group?.Primary != null && mapping?.ButtonsEnabled == true)
                mapping.RemoveOutputBindingSet(group.Primary);
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

        private void AddAndInput_OnClick(object sender, RoutedEventArgs e)
        {
            (DataContext as MappingViewModel)?.AddAndInput();
        }

        private void AddOrInput_OnClick(object sender, RoutedEventArgs e)
        {
            (DataContext as MappingViewModel)?.AddOrInput();
        }

        private void AddInputToCondition_OnClick(object sender, RoutedEventArgs e)
        {
            var condition = (sender as FrameworkElement)?.DataContext as InputConditionViewModel;
            (DataContext as MappingViewModel)?.AddInputToCondition(condition);
        }

        private void AddCondition_OnClick(object sender, RoutedEventArgs e)
        {
            (DataContext as MappingViewModel)?.AddCondition();
        }

        private void RemoveExpressionInput_OnClick(object sender, RoutedEventArgs e)
        {
            var binding = (sender as FrameworkElement)?.DataContext as DeviceBindingViewModel;
            (DataContext as MappingViewModel)?.RemoveExpressionInput(binding);
        }

        private void RemoveCondition_OnClick(object sender, RoutedEventArgs e)
        {
            var condition = (sender as FrameworkElement)?.DataContext as InputConditionViewModel;
            (DataContext as MappingViewModel)?.RemoveCondition(condition);
        }

        private void BindDirectionalSlot_OnClick(object sender, RoutedEventArgs e)
        {
            var input = (sender as FrameworkElement)?.DataContext as DeviceBindingViewModel;
            if (input?.DeviceBinding == null) return;
            if (input.DeviceBinding.IsInBindMode)
            {
                input.DeviceBinding.CancelBindMode();
                return;
            }
            try
            {
                input.DeviceBinding.DeviceBindingCategory = input.DeviceBindingCategory;
                input.DeviceBinding.EnterBindMode();
            }
            catch (Exception exception)
            {
                Logger.Error("Unable to capture directional input", exception);
                (DataContext as MappingViewModel).InputPresetFeedback =
                    "Unable to detect this input direction. Details are in the log.";
            }
        }

        private void DirectionalCapture_OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var input = (sender as FrameworkElement)?.DataContext as DeviceBindingViewModel;
            if (input?.DeviceBinding?.IsInBindMode != true) return;
            input.DeviceBinding.CancelBindMode();
            e.Handled = true;
        }

        private void DirectionalDevice_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var combo = sender as ComboBox;
            var group = combo?.DataContext as InputConditionViewModel;
            var selected = combo?.SelectedItem as ComboBoxItemViewModel;
            if (group?.DirectionalPrimary == null || selected == null) return;

            // WPF fires SelectionChanged when the template first loads. That must
            // not clear existing inputs unless the user chose a different device.
            var primary = group.DirectionalPrimary;
            if (selected.Value == primary.DeviceBinding.DeviceConfigurationGuid) return;
            foreach (var input in group.DirectionalInputs)
                input.ChangeDeviceConfiguration(selected.Value);
        }

        private void OpenDirectionalPresetMenu_OnClick(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var mapping = DataContext as MappingViewModel;
            if (button == null || mapping?.ButtonsEnabled != true) return;
            var menu = CreateDarkContextMenu(button);
            menu.Placement = PlacementMode.Bottom;
            var device = mapping.ProfileViewModel.Profile.GetDeviceConfiguration(
                DeviceIoType.Input, mapping.InputSideDeviceGuid);
            var keyboard = device?.Device != null && DeviceVisualCatalog.Describe(
                device, mapping.ProfileViewModel.Profile, DeviceIoType.Input)
                .Kind == DeviceVisualKind.Keyboard;
            // One shortcut for all input slots; individual slots remain bindable.
            foreach (var pair in new[]
            {
                new { Title = "WASD", Key = "WASD" },
                new { Title = "Arrow Keys", Key = "Arrows" },
                new { Title = "Numpad", Key = "Numpad" },
                new { Title = "UHJL", Key = "UHJL" }
            })
            {
                if (!keyboard && device != null) continue;
                var selected = pair.Key;
                var item = PickerItem(pair.Title);
                item.Click += (s, args) =>
                {
                    string error;
                    var applied = mapping.ApplyDirectionalKeyboardPreset(selected, out error);
                    mapping.InputPresetFeedback = applied ? null :
                        (error ?? "This input preset is unavailable.");
                };
                menu.Items.Add(item);
            }
            if (device == null || mapping.SelectedPresetInputHasDPad)
            {
                var item = PickerItem("Controller D-pad");
                item.Click += (s, args) =>
                {
                    string error;
                    var applied = mapping.ApplyDirectionalDPadPreset(out error);
                    mapping.InputPresetFeedback = applied ? null :
                        (error ?? "This D-pad is unavailable.");
                };
                menu.Items.Add(item);
            }
            if (menu.Items.Count == 0)
                menu.Items.Add(new MenuItem
                {
                    Header = "No presets for this input device",
                    IsEnabled = false
                });
            menu.IsOpen = true;
        }

        private void DirectionalType_OnClick(object sender, RoutedEventArgs e)
        {
            var item = sender as MenuItem;
            var button = (item?.Parent as ContextMenu)?.PlacementTarget as Button;
            var input = button?.DataContext as DeviceBindingViewModel ?? item?.DataContext as DeviceBindingViewModel;
            int kind;
            if (input == null || !int.TryParse(item?.Tag as string, out kind)) return;
            input.InputExpressionInputKind = kind;
        }

        private void DirectionalNot_OnClick(object sender, RoutedEventArgs e)
        {
            var group = (sender as FrameworkElement)?.DataContext as InputConditionViewModel;
            if (group?.DirectionalPrimary == null) return;
            (DataContext as MappingViewModel)?.SetInputNegated(group.DirectionalPrimary,
                (sender as ToggleButton)?.IsChecked == true);
        }

        private void DirectionalBlock_OnClick(object sender, RoutedEventArgs e)
        {
            var group = (sender as FrameworkElement)?.DataContext as InputConditionViewModel;
            var mapping = DataContext as MappingViewModel;
            if (group?.DirectionalPrimary == null || mapping?.ButtonsEnabled != true) return;
            var enabled = (sender as ToggleButton)?.IsChecked == true;
            if (group.DirectionalPrimary.DeviceBinding.InputDirectionSlot > 0)
            {
                mapping.SetDirectionalBlocked(group.DirectionalPrimary, enabled);
            }
            else
            {
                // The four-input ButtonsToAxes plugin is positional: it has
                // no InputDirectionSlot markers. Its group Block toggle still
                // applies to all four independently bindable physical inputs.
                foreach (var input in group.DirectionalInputs)
                {
                    var binding = input.DeviceBinding;
                    binding.SetBlock(enabled && binding.IsBound && binding.IsBlockable());
                }
            }
        }

        private void RemoveDirectionalUnit_OnClick(object sender, RoutedEventArgs e)
        {
            var group = (sender as FrameworkElement)?.DataContext as InputConditionViewModel;
            if (group == null) return;
            (DataContext as MappingViewModel)?.SetDirectionalMode(0);
        }

        private void InputNegated_OnClick(object sender, RoutedEventArgs e)
        {
            var toggle = sender as ToggleButton;
            var binding = toggle?.DataContext as DeviceBindingViewModel;
            if (toggle == null || binding == null) return;
            (DataContext as MappingViewModel)?.SetInputNegated(binding, toggle.IsChecked == true);
        }

        private void AddPlugin_OnClick(object sender, RoutedEventArgs e)
        {
            var mapping = DataContext as MappingViewModel;
            if (mapping?.ButtonsEnabled != true) return;
            mapping.AddOutputBindingSet();
        }

        private void InputSideDevice_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var combo = sender as ComboBox;
            var mapping = DataContext as MappingViewModel;
            var selected = combo?.SelectedItem as ComboBoxItemViewModel;
            if (combo?.IsLoaded != true || mapping?.ButtonsEnabled != true || selected == null) return;
            var guid = (Guid)selected.Value;
            if (guid == mapping.InputSideDeviceGuid) return;
            if (!mapping.ChangeInputSideDevice(guid))
                mapping.InputPresetFeedback = "This input device is unavailable for the current mapping.";
            else
                mapping.InputPresetFeedback = null;
            mapping.RefreshSideDeviceChoices();
        }

        private void OutputSideDevice_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var combo = sender as ComboBox;
            var mapping = DataContext as MappingViewModel;
            var selected = combo?.SelectedItem as ComboBoxItemViewModel;
            if (combo?.IsLoaded != true || mapping?.ButtonsEnabled != true || selected == null) return;
            var guid = (Guid)selected.Value;
            if (guid == mapping.OutputSideDeviceGuid) return;
            if (!mapping.ChangeOutputSideDevice(guid))
                mapping.OutputSelectionFeedback = "This output device is unavailable.";
            else
                mapping.OutputSelectionFeedback = null;
            mapping.RefreshSideDeviceChoices();
        }

        private void ChooseInputCategory_OnClick(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var mapping = DataContext as MappingViewModel;
            if (button == null || mapping?.ButtonsEnabled != true) return;
            var menu = CreateDarkContextMenu(button);
            menu.Placement = PlacementMode.Bottom;
            foreach (var entry in new[]
            {
                new { Label = "Button", Category = DeviceBindingCategory.Momentary },
                new { Label = "Axis", Category = DeviceBindingCategory.Range }
            })
            {
                if (!mapping.InputDeviceSupports(entry.Category)) continue;
                var category = entry.Category;
                var item = new MenuItem
                {
                    Header = entry.Label,
                    Foreground = Brushes.White,
                    Background = Brushes.Transparent,
                    Padding = new Thickness(10, 6, 14, 6),
                    IsChecked = mapping.MappingInputTypeLabel == entry.Label
                };
                item.Click += (s, args) =>
                {
                    if (mapping.MappingInputTypeLabel == entry.Label) return;
                    if (!mapping.ChangeInputSignalType(category))
                        mapping.InputPresetFeedback = "Clear the incompatible assigned directions before changing this input type.";
                };
                menu.Items.Add(item);
            }
            if (menu.Items.Count == 0)
                menu.Items.Add(new MenuItem
                {
                    Header = "Selected device has no supported input types",
                    IsEnabled = false
                });
            menu.IsOpen = true;
        }

        private void ChooseInputLayout_OnClick(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var mapping = DataContext as MappingViewModel;
            if (button == null || mapping?.ButtonsEnabled != true) return;
            var menu = CreateDarkContextMenu(button);
            menu.Placement = PlacementMode.Bottom;
            foreach (var entry in new[]
            {
                new { Label = "1-way", Slots = 0 },
                new { Label = "2-way", Slots = 2 },
                new { Label = "4-way", Slots = 4 }
            })
            {
                var slots = entry.Slots;
                var item = new MenuItem
                {
                    Header = entry.Label,
                    Foreground = Brushes.White,
                    Background = Brushes.Transparent,
                    Padding = new Thickness(10, 6, 14, 6),
                    IsChecked = mapping.InputBindingLayoutLabel == entry.Label
                };
                item.Click += (s, args) =>
                {
                    if (mapping.Mapping.DeviceBindings.Any(binding =>
                        binding.InputDirectionSlot > slots && binding.IsBound))
                    {
                        mapping.InputPresetFeedback = "Clear assigned directions before reducing the layout.";
                        return;
                    }
                    var wasButton = mapping.MappingInputTypeLabel == "Button";
                    mapping.SetDirectionalMode(slots);
                    if (slots == 0 && wasButton)
                        mapping.ChangeInputSignalType(DeviceBindingCategory.Momentary);
                };
                menu.Items.Add(item);
            }
            menu.IsOpen = true;
        }

        private void ConvertLegacyToButton_OnClick(object sender, RoutedEventArgs e)
        {
            var mapping = DataContext as MappingViewModel;
            if (mapping?.ButtonsEnabled == true)
                mapping.ConvertToButtonMapping();
        }

        private void ChangeMappingOutputCategory_OnClick(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var mapping = DataContext as MappingViewModel;
            if (button == null || mapping?.ButtonsEnabled != true || mapping.Plugins.Count == 0) return;

            var selectedDevice = mapping.OutputSideDeviceGuid;
            var menu = CreateDarkContextMenu(button);
            menu.Placement = PlacementMode.Bottom;
            if (selectedDevice == Guid.Empty)
            {
                menu.Items.Add(new MenuItem
                {
                    Header = "Select an output device first",
                    IsEnabled = false
                });
                menu.IsOpen = true;
                return;
            }
            var options = mapping.GetCompatiblePluginOptions()
                .Where(option => option.Plugin.OutputCategories.Count > 0 &&
                    option.Plugin.OutputCategories.All(outputCategory =>
                        mapping.OutputDeviceSupports(outputCategory.Category)))
                .ToList();
            var analogue = mapping.Mapping.DeviceBindings.Any(input => input.InputExpressionInputKind == 3);
            var categories = options
                .GroupBy(option => option.OutputType, StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var category in categories)
            {
                if (!IsPrimaryOutputCategory(category.Key)) continue;
                var chosen = PreferredOutputImplementation(category, analogue);
                if (chosen == null) continue;
                var item = new MenuItem
                {
                    Header = category.Key,
                    Foreground = Brushes.White, Background = Brushes.Transparent,
                    Padding = new Thickness(10, 6, 14, 6),
                    ToolTip = "Set every destination to " + category.Key,
                    IsChecked = mapping.Plugins.All(p => string.Equals(p.OutputType,
                        category.Key, StringComparison.OrdinalIgnoreCase))
                };
                item.Click += (s, args) => mapping.ChangeAllOutputTypes(chosen.Plugin);
                menu.Items.Add(item);
            }

            var joystickChoice = new MenuItem
            {
                Header = "Full Joystick",
                Foreground = Brushes.White,
                Background = Brushes.Transparent,
                Padding = new Thickness(10, 6, 14, 6),
                ToolTip = "Two continuous output axes: X and Y",
                IsChecked = mapping.MappingOutputCategoryDisplay == "Full Joystick"
            };
            joystickChoice.Click += (s, args) =>
            {
                if (!mapping.ChangeOutputToFullJoystick())
                    mapping.OutputSelectionFeedback = "A full joystick requires two compatible output axes. Clear incompatible extra outputs first.";
            };
            if (mapping.OutputDeviceSupportsControls(DeviceBindingCategory.Range, 2))
                menu.Items.Add(joystickChoice);

            var dpadChoice = new MenuItem
            {
                Header = "4-way",
                Foreground = Brushes.White,
                Background = Brushes.Transparent,
                Padding = new Thickness(10, 6, 14, 6),
                ToolTip = "Four independently assignable button directions",
                IsChecked = mapping.Mapping.DirectionalButtonOutputs
            };
            dpadChoice.Click += (s, args) => mapping.ChangeOutputToDPad();
            if (mapping.OutputDeviceSupportsControls(DeviceBindingCategory.Momentary, 4))
                menu.Items.Add(dpadChoice);

            if (!categories.Any(group => string.Equals(group.Key, "Button",
                    StringComparison.OrdinalIgnoreCase)) && mapping.Plugins.Count == 1 &&
                    mapping.OutputDeviceSupports(DeviceBindingCategory.Momentary))
            {
                var buttonChoice = new MenuItem
                {
                    Header = "Button",
                    Foreground = Brushes.White, Background = Brushes.Transparent,
                    Padding = new Thickness(10, 6, 14, 6),
                    ToolTip = "Switch to a button output while preserving existing physical input assignments"
                };
                buttonChoice.Click += (s, args) => mapping.ConvertToButtonMapping();
                menu.Items.Insert(0, buttonChoice);
            }

            if (menu.Items.Count == 0)
                menu.Items.Add(new MenuItem { Header = "No compatible output types", IsEnabled = false });
            menu.IsOpen = true;
        }

        private void ChangeOutputType_OnClick(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var output = button?.DataContext as PluginViewModel;
            var mapping = output?.MappingViewModel;
            if (mapping?.ButtonsEnabled != true) return;
            // Per-output menus obey the side's selected output device too.
            var options = mapping.GetCompatiblePluginOptions()
                .Where(option => option.Plugin.OutputCategories.Count > 0 &&
                    option.Plugin.OutputCategories.All(category =>
                        mapping.OutputDeviceSupports(category.Category)))
                .ToList();
            if (options.Count == 0) return;

            var changeAll = string.Equals(button.Tag as string, "CategoryHeader", StringComparison.Ordinal);
            Action<SimplePluginViewModel> apply = selected =>
            {
                if (changeAll) mapping.ChangeAllOutputTypes(selected.Plugin);
                else mapping.ChangeOutputType(output, selected.Plugin);
            };

            var menu = CreateDarkContextMenu(button);
            menu.Placement = PlacementMode.Bottom;

            // A mapping no longer has an input-to-output plugin identity. Expose one
            // destination category at the surface, not a duplicate menu entry for
            // every old 'Button to ...' and 'Axis to ...' implementation.
            var categories = options.GroupBy(option => option.OutputType,
                StringComparer.OrdinalIgnoreCase).ToList();
            var analogueInput = mapping.Mapping.DeviceBindings.Any(input =>
                input.InputExpressionInputKind == 3);
            foreach (var category in categories)
            {
                if (!IsPrimaryOutputCategory(category.Key)) continue;
                var preferred = PreferredOutputImplementation(category, analogueInput);
                if (preferred == null) continue;
                var selected = preferred;
                var item = new MenuItem
                {
                    Header = category.Key,
                    ToolTip = selected.Description,
                    Foreground = Brushes.White,
                    Background = Brushes.Transparent,
                    Padding = new Thickness(10, 6, 14, 6),
                    IsChecked = string.Equals(output.OutputType, category.Key,
                        StringComparison.OrdinalIgnoreCase)
                };
                item.Click += (s, args) => apply(selected);
                menu.Items.Add(item);
            }

            if (changeAll)
            {
                menu.Items.Add(new Separator());
                var addOutput = new MenuItem
                {
                    Header = "Add another output",
                    Foreground = Brushes.White,
                    Background = Brushes.Transparent,
                    Padding = new Thickness(10, 6, 14, 6)
                };
                addOutput.Click += (s, args) => mapping.AddOutputBindingSet();
                menu.Items.Add(addOutput);
            }
            menu.IsOpen = true;
        }

        private static bool IsPrimaryOutputCategory(string category)
        {
            return category == "Button" || category == "Axis" ||
                   category == "Filter" || category == "Event" ||
                   category == "Delta" || category == "Value";
        }

        private static SimplePluginViewModel PreferredOutputImplementation(
            System.Collections.Generic.IEnumerable<SimplePluginViewModel> group,
            bool analogueInput)
        {
            var variants = group.ToList();
            if (variants.Count == 0) return null;
            // Changing the category chooses the standard output behaviour, not the
            // legacy input-to-output plugin identity. Existing specialised mappings
            // continue to work until the user explicitly changes their output type.
            var category = variants[0].OutputType;
            string name = null;
            switch (category)
            {
                case "Button": name = "Button to Button"; break;
                case "Axis": name = "Axis to Axis"; break;
                case "Filter": name = analogueInput ? "Axis to Filter" : "Button to Filter"; break;
                case "Event": name = "Button to Event"; break;
            }
            return variants.FirstOrDefault(option =>
                       string.Equals(option.Name, name, StringComparison.OrdinalIgnoreCase))
                ?? variants.OrderBy(option => option.Name, StringComparer.OrdinalIgnoreCase)
                    .First();
        }

        private void AxisFourButtons_OnClick(object sender, RoutedEventArgs e)
        {
            var axis = (sender as FrameworkElement)?.DataContext as PluginViewModel;
            RequestAxisInputMode(axis, 4);
        }

        private void RequestAxisInputMode(PluginViewModel axis, int buttonCount)
        {
            if (axis == null || !axis.CanChangeAxisButtonMode) return;
            var currentCount = axis.IsFourButtonAxis ? 4 : axis.IsTwoButtonAxis ? 2 : 1;
            if (currentCount == buttonCount) return;

            axis.MappingViewModel.ChangeAxisInputMode(axis, buttonCount);
        }

        private void AxisKeyboardPreset_OnClick(object sender, RoutedEventArgs e)
        {
            var mapping = ((sender as Button)?.DataContext as PluginViewModel)?.MappingViewModel;
            var preset = (sender as Button)?.Tag as string;
            if (mapping == null || string.IsNullOrEmpty(preset)) return;
            string error;
            if (!mapping.ApplyKeyboardAxisPreset(preset, out error) && !string.IsNullOrEmpty(error))
                mapping.InputPresetFeedback = error;
        }

        private void AxisStickInputPreset_OnClick(object sender, RoutedEventArgs e)
        {
            var mapping = DataContext as MappingViewModel;
            int index;
            if (mapping == null || !int.TryParse((sender as Button)?.Tag as string, out index)) return;
            string error;
            if (!mapping.ApplyControllerStickInputPreset(index, out error) && !string.IsNullOrEmpty(error))
                mapping.InputPresetFeedback = error;
        }

        private void AxisControllerPreset_OnClick(object sender, RoutedEventArgs e)
        {
            var axis = (sender as FrameworkElement)?.DataContext as PluginViewModel;
            if (axis?.MappingViewModel == null) return;
            string error;
            if (!axis.MappingViewModel.ApplyControllerDPadPreset(out error) && !string.IsNullOrEmpty(error))
                axis.MappingViewModel.OutputSelectionFeedback = error;
        }

        private void AxisInvertX_OnClick(object sender, RoutedEventArgs e) => SetFourWayInversion(sender, "InvertX");
        private void AxisInvertY_OnClick(object sender, RoutedEventArgs e) => SetFourWayInversion(sender, "InvertY");

        private static void SetFourWayInversion(object sender, string propertyName)
        {
            var toggle = sender as ToggleButton;
            var axis = toggle?.DataContext as PluginViewModel;
            if (axis?.IsFourButtonAxis != true || axis.MappingViewModel?.ButtonsEnabled != true) return;
            var property = axis.Plugin.GetType().GetProperty(propertyName);
            if (property == null || property.PropertyType != typeof(bool) || !property.CanWrite) return;
            property.SetValue(axis.Plugin, toggle.IsChecked == true, null);
            axis.Plugin.ContextChanged();
        }

        private void FourWayOutput_OnClick(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var axis = button?.DataContext as PluginViewModel;
            if (axis?.IsFourButtonAxis != true || axis.MappingViewModel?.ButtonsEnabled != true) return;

            // The output device and whole D-pad/stick destination are one choice.
            // Do not require a second device dropdown or another submenu click.
            var options = axis.MappingViewModel.GetFourWayOutputChoices();
            var menu = CreateDarkContextMenu(button);
            menu.Placement = PlacementMode.Bottom;
            var hasMultipleDevices = options.Select(option => option.DeviceConfigurationGuid)
                .Distinct().Count() > 1;
            foreach (var option in options)
            {
                var chosen = option;
                var item = new MenuItem
                {
                    Header = hasMultipleDevices
                        ? option.Destination + "  —  " + option.DeviceTitle
                        : option.Destination,
                    Foreground = Brushes.White,
                    Background = Brushes.Transparent,
                    Padding = new Thickness(10, 6, 12, 6)
                };
                item.Click += (s, args) => axis.MappingViewModel.ApplyFourWayOutput(axis, chosen);
                menu.Items.Add(item);
            }
            if (options.Count == 0) menu.Items.Add(new MenuItem
            {
                Header = "No compatible controller outputs in this profile",
                IsEnabled = false, Foreground = Brushes.LightGray
            });
            menu.IsOpen = true;
        }

        private void AxisOneButton_OnClick(object sender, RoutedEventArgs e)
        {
            var axis = (sender as FrameworkElement)?.DataContext as PluginViewModel;
            RequestAxisInputMode(axis, 1);
        }

        private void AxisTwoButtons_OnClick(object sender, RoutedEventArgs e)
        {
            var axis = (sender as FrameworkElement)?.DataContext as PluginViewModel;
            RequestAxisInputMode(axis, 2);
        }

        private void AddOutputFilter_OnClick(object sender, RoutedEventArgs e)
        {
            ((sender as FrameworkElement)?.DataContext as PluginViewModel)?.AddFilter();
        }

        private void ToggleOutputFilter_OnClick(object sender, RoutedEventArgs e)
        {
            ((sender as FrameworkElement)?.DataContext as FilterViewModel)?.ToggleFilter();
        }

        private void RemoveOutputFilter_OnClick(object sender, RoutedEventArgs e)
        {
            ((sender as FrameworkElement)?.DataContext as FilterViewModel)?.RemoveFilter();
        }

        private void RemoveOutput_OnClick(object sender, RoutedEventArgs e)
        {
            var plugin = (sender as FrameworkElement)?.DataContext as PluginViewModel;
            if (plugin?.CanRemove == true) plugin.MappingViewModel.RemoveOutputBindingSet(plugin);
        }

    }
}
