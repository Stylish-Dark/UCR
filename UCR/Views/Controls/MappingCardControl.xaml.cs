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

        private void OutputSettings_OnExpanded(object sender, RoutedEventArgs e)
        {
            // WPF routes Expanded events from children as well; initialise only this
            // output's settings, and only when its own Settings disclosure is opened.
            if (!ReferenceEquals(sender, e.OriginalSource)) return;
            ((sender as FrameworkElement)?.DataContext as PluginViewModel)
                ?.EnsureSettingsInitialized();
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
            if (InspectorMode || e.PropertyName != nameof(MappingViewModel.IsExpanded)) return;
            // The inline workspace owns expansion and destroys this inspector's
            // template on collapse. Reacting here would redundantly rebuild its
            // already-open body just before the entire control is unloaded.
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => RefreshExpandedVisual(true)));
                return;
            }
            RefreshExpandedVisual(true);
        }

        // ProfilePage.xaml owns the only mapping header. This control contains
        // the editor; the workspace decides when to show or hide it.
        private void RefreshExpandedVisual(bool animateCollapse)
        {
            if (ExpandedBodyHost == null) return;
            if (InspectorMode || (DataContext as MappingViewModel)?.IsExpanded == true)
                ShowExpandedBody();
            else
                HideExpandedBody(animateCollapse);
        }

        // Expanding height inside a non-virtualized mappings list remeasures every
        // card on every animation frame. Apply the state change once instead.
        // Keep the heavy editor template lazy, so collapsed cards stay inexpensive.
        private void ShowExpandedBody()
        {
            if (ExpandedBodyHost == null) return;
            // Output property controls (especially Button/Axis-to-Filter's Filter name)
            // are populated lazily. The compact inspector must initialize them too.
            var mapping = DataContext as MappingViewModel;
            if (mapping != null)
                foreach (var plugin in mapping.Plugins)
                    plugin.EnsureFilterInitialized();

            // An inspector whose body is already open has nothing to initialise.
            // Avoid restarting animations and forcing layout on every WPF load event.
            if (ExpandedBodyHost.Visibility == Visibility.Visible &&
                ExpandedBodyHost.ContentTemplate != null && ExpandedBodyHost.Opacity >= 1)
                return;
            // Interrupt any closing fade if the user immediately reopens the card.
            ExpandedBodyHost.BeginAnimation(OpacityProperty, null);
            if (ExpandedBodyHost.ContentTemplate == null)
                ExpandedBodyHost.ContentTemplate = FindResource("ExpandedMappingBodyTemplate") as DataTemplate;

            ExpandedBodyHost.Height = double.NaN;
            ExpandedBodyHost.Opacity = 1;
            ExpandedBodyHost.Visibility = Visibility.Visible;
        }

        private void HideExpandedBody(bool animate = false)
        {
            if (ExpandedBodyHost == null) return;

            ExpandedBodyHost.BeginAnimation(OpacityProperty, null);
            if (animate && ExpandedBodyHost.Visibility == Visibility.Visible)
            {
                // A compositor-only fade keeps the panel visible while closing,
                // without repeatedly measuring the entire non-virtualized list.
                var fade = new DoubleAnimation(ExpandedBodyHost.Opacity, 0,
                    TimeSpan.FromMilliseconds(125))
                {
                    FillBehavior = FillBehavior.Stop
                };
                fade.Completed += (animationSender, args) =>
                {
                    if ((DataContext as MappingViewModel)?.IsExpanded != true)
                        HideExpandedBody();
                };
                ExpandedBodyHost.BeginAnimation(OpacityProperty, fade);
                return;
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

        public static bool RequiresDeleteConfirmation(MappingViewModel mapping)
        {
            if (mapping?.Mapping == null) return false;
            return !string.IsNullOrWhiteSpace(mapping.Mapping.Title) ||
                (mapping.Mapping.DeviceBindings != null &&
                 mapping.Mapping.DeviceBindings.Any(binding => binding != null && binding.IsBound));
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
            if (mapping == null || descriptor == null) return;
            var input = mapping.ResolveCollapsedBinding(descriptor, DeviceIoType.Input)?.DeviceBinding;
            if (input?.IsInBindMode == true)
            {
                input.CancelBindMode();
                e.Handled = true;
                return;
            }
            var outputLane = false;
            for (DependencyObject current = button; current != null; current = VisualTreeHelper.GetParent(current))
            {
                var items = current as ItemsControl;
                if ((items?.Tag as string) == "Output") { outputLane = true; break; }
                if ((items?.Tag as string) == "Input") break;
            }
            if (!outputLane || !mapping.ButtonsEnabled) return;
            var binding = mapping.ResolveCollapsedBinding(descriptor, DeviceIoType.Output);
            ShowOutputCategoryMenuForBinding(binding, button);
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
                if (mapping.InputIsFullStick && mapping.DirectionalCapacity == 4)
                {
                    var logical = mapping.ResolveCollapsedBinding(descriptor, DeviceIoType.Input);
                    ShowQuickCompoundInputMenu(mapping, button,
                        logical?.DeviceBinding?.InputExpressionGroup ?? 0);
                    return;
                }
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
                    DarkMessageBox.Show("UCR could not start input detection for this binding. The error has been written to the log.",
                        "Unable to bind input", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else if (lane == "Output")
            {
                if (mapping.IsCompoundOutput)
                    ShowQuickCompoundOutputMenu(mapping, button);
                else
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

        private static StackPanel CompoundChoiceHeader(string label,
            ControlVisualKind kind, string controls)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new ControlGlyphControl
            {
                Kind = kind, Label = controls, AccentBrush = Brushes.White,
                Width = 47, Height = 36, Margin = new Thickness(0, 0, 10, 0)
            });
            row.Children.Add(new TextBlock
            {
                Text = label, VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brushes.White, FontWeight = FontWeights.SemiBold
            });
            return row;
        }

        internal static void ShowQuickCompoundInputMenu(MappingViewModel mapping,
            FrameworkElement anchor, int groupIndex = 0)
        {
            if (mapping?.ButtonsEnabled != true || anchor == null) return;
            var menu = CreateDarkContextMenu(anchor);
            menu.Placement = PlacementMode.Bottom;
            var availableCount = 0;
            var keyboardOptions = new[]
            {
                new { Name = "WASD", Keys = "A|D|W|S" },
                new { Name = "Arrows", Keys = "Left|Right|Up|Down" },
                new { Name = "Numpad", Keys = "4|6|8|2" },
                new { Name = "UHJK", Keys = "H|K|U|J" }
            };
            foreach (var preset in keyboardOptions)
            {
                var selected = preset;
                if (!mapping.CanApplyCompoundInputPreset(selected.Name)) continue;
                var item = new MenuItem
                {
                    Header = CompoundChoiceHeader(selected.Name,
                        ControlVisualKind.CompoundKeys, selected.Keys)
                };
                item.Click += (sender, args) => ApplyCompoundPreset(
                    mapping, selected.Name, groupIndex);
                menu.Items.Add(item);
                availableCount++;
            }
            var deviceChoices = new[]
            {
                new { Name = "Controller D-pad", Kind = ControlVisualKind.DPad,
                    Label = "", Preset = 0 },
                new { Name = "Left stick", Kind = ControlVisualKind.CompoundStick,
                    Label = "L", Preset = 1 },
                new { Name = "Right stick", Kind = ControlVisualKind.CompoundStick,
                    Label = "R", Preset = 2 },
                new { Name = "Face buttons", Kind = ControlVisualKind.CompoundFaceButtons,
                    Label = "X|B|Y|A", Preset = 3 }
            };
            foreach (var preset in deviceChoices)
            {
                var selected = preset;
                var item = new MenuItem
                {
                    Header = CompoundChoiceHeader(selected.Name, selected.Kind, selected.Label)
                };
                item.Click += (sender, args) =>
                {
                    string error;
                    bool success;
                    switch (selected.Preset)
                    {
                        case 0: success = mapping.ApplyDirectionalDPadPreset(out error, groupIndex); break;
                        case 1: success = mapping.ApplyTypedControllerStickPreset(0, out error, groupIndex); break;
                        case 2: success = mapping.ApplyTypedControllerStickPreset(2, out error, groupIndex); break;
                        default: success = mapping.ApplyTypedControllerFaceButtons(out error, groupIndex); break;
                    }
                    if (!success)
                        DarkMessageBox.Show(error ?? "This complete 4-Way preset is unavailable.",
                            "Preset unavailable", MessageBoxButton.OK, MessageBoxImage.Information);
                };
                menu.Items.Add(item);
                availableCount++;
            }
            var saved = mapping.ProfileViewModel.Profile.CompoundBindingPresets;
            if (saved != null && saved.Count > 0)
            {
                foreach (var preset in saved.Where(item => item.Capacity == 4 &&
                    mapping.CanApplyCompoundInputPreset(item.Name)))
                {
                    var selected = preset;
                    if (availableCount > 0)
                    {
                        // Custom presets share the same physical device pool, but
                        // should be visually separated from built-in choices.
                        if (!menu.Items.OfType<Separator>().Any())
                            menu.Items.Add(new Separator());
                    }
                    var item = new MenuItem
                    {
                        Header = CompoundChoiceHeader(selected.Name,
                            ControlVisualKind.CompoundKeys,
                            string.Join("|", selected.Keys ?? new System.Collections.Generic.List<string>()))
                    };
                    item.Click += (sender, args) => ApplyCompoundPreset(
                        mapping, selected.Name, groupIndex);
                    menu.Items.Add(item);
                    availableCount++;
                }
            }
            if (availableCount == 0)
                menu.Items.Add(new MenuItem
                {
                    Header = "No complete 4-Way presets for configured input devices",
                    IsEnabled = false
                });
            menu.Items.Add(new Separator());
            var single = new MenuItem
            {
                Header = "Bind one button instead",
                ToolTip = "Switch to Button; retain bound 4-Way inputs as AND guards",
                Foreground = Brushes.White
            };
            single.Click += (sender, args) =>
            {
                string error;
                if (!mapping.ConvertCompoundToSingleButton(out error))
                {
                    DarkMessageBox.Show(error ?? "Unable to convert the compound binding.",
                        "Binding unavailable", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                var source = mapping.DeviceBindings.FirstOrDefault();
                source?.DeviceBinding?.EnterBindMode(autoDetectInputCategory: true);
            };
            menu.Items.Add(single);
            menu.IsOpen = true;
        }

        internal static void ShowQuickCompoundOutputMenu(MappingViewModel mapping,
            FrameworkElement anchor)
        {
            if (mapping?.ButtonsEnabled != true || anchor == null) return;
            var menu = CreateDarkContextMenu(anchor);
            menu.Placement = PlacementMode.Bottom;
            foreach (var choice in mapping.GetFourWayOutputChoices())
            {
                var selected = choice;
                var device = mapping.ProfileViewModel.Profile.GetDeviceConfiguration(
                    DeviceIoType.Output, selected.DeviceConfigurationGuid);
                var deviceKind = device?.Device == null ? DeviceVisualKind.Unknown :
                    DeviceVisualCatalog.Describe(device, mapping.ProfileViewModel.Profile,
                        DeviceIoType.Output).Kind;
                var kind = selected.Destination == "Face buttons"
                    ? ControlVisualKind.CompoundFaceButtons
                    : deviceKind == DeviceVisualKind.Keyboard
                        ? ControlVisualKind.CompoundKeys
                        : selected.IsDPad ? ControlVisualKind.DPad
                        : ControlVisualKind.CompoundStick;
                var controls = selected.VisualKeys ??
                    (selected.Destination == "Right stick" ? "R" : "L");
                var item = new MenuItem
                {
                    Header = CompoundChoiceHeader(
                        selected.Destination, kind, controls)
                };
                item.Click += (sender, args) =>
                {
                    if (!mapping.ApplyTypedFourWayOutput(selected))
                        DarkMessageBox.Show("Unable to assign the complete 4-Way destination.",
                            "Output unavailable", MessageBoxButton.OK, MessageBoxImage.Information);
                };
                menu.Items.Add(item);
            }
            if (menu.Items.Count == 0)
                menu.Items.Add(new MenuItem { Header = "No configured 4-Way outputs",
                    IsEnabled = false });
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

        private void FilterOutput_OnClick(object sender, RoutedEventArgs e)
        {
            var mapping = DataContext as MappingViewModel;
            if (mapping?.ButtonsEnabled != true) return;
            var analogue = mapping.Mapping.DeviceBindings.Any(input =>
                input.InputExpressionInputKind == 3);
            var choices = mapping.GetCompatiblePluginOptions().Where(option =>
                string.Equals(option.OutputType, "Filter", StringComparison.OrdinalIgnoreCase));
            var selected = PreferredOutputImplementation(choices, analogue);
            if (selected == null || !mapping.ChangeAllOutputTypes(selected.Plugin))
                DarkMessageBox.Show("A compatible Filter output is unavailable.",
                    "Cannot select Filter", MessageBoxButton.OK, MessageBoxImage.Information);
            mapping.RefreshRepresentationSelections();
        }

        private void InputRepresentation_OnClick(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as MappingViewModel;
            int value;
            if (vm == null || !int.TryParse((sender as FrameworkElement)?.Tag as string, out value))
                return;
            // Trigger is the analogue form of the *selected* Button family.
            // Clicking an already-selected Button must not discard that
            // trigger's physical assignment by forcing representation 3.
            if (value == 3 && vm.InputIsTrigger) return;
            if (!vm.SetInputRepresentation(value))
            {
                DarkMessageBox.Show("The input representation could not be prepared. Check the available remapper plugins.",
                    "Unable to change input type", MessageBoxButton.OK, MessageBoxImage.Information);
                vm.RefreshRepresentationSelections();
                (sender as RadioButton)?.GetBindingExpression(ToggleButton.IsCheckedProperty)?.UpdateTarget();
            }
        }

        private void OutputRepresentation_OnClick(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as MappingViewModel;
            int value;
            if (vm == null || !int.TryParse((sender as FrameworkElement)?.Tag as string, out value))
                return;
            if (value == 3 && vm.OutputIsTrigger) return;
            // Filter's selected type supersedes the persisted physical route.
            // Returning to a type reconstructs or restores the correct output bank,
            // including the case where its representation number did not change.
            if (!vm.SetOutputRepresentation(value))
            {
                DarkMessageBox.Show("The output representation could not be prepared. Check the available remapper plugins.",
                    "Unable to change output type", MessageBoxButton.OK, MessageBoxImage.Information);
                vm.RefreshRepresentationSelections();
                (sender as RadioButton)?.GetBindingExpression(ToggleButton.IsCheckedProperty)?.UpdateTarget();
            }
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

        private void MappingEditorColumns_OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Keep input and output editors usable in the narrow middle workspace.
            // Two columns need room for device, Bind, NOT and Remove controls.
            if (sender is UniformGrid editor)
            {
                var singleInspector = InspectorMode && !InlineWorkspaceMode;
                var desired = singleInspector || e.NewSize.Width < 650 ? 1 : 2;
                if (editor.Columns != desired)
                    editor.Columns = desired;
            }
        }

        private void RemoveOrClearInput_OnClick(object sender, RoutedEventArgs e)
        {
            var binding = (sender as FrameworkElement)?.DataContext as DeviceBindingViewModel;
            if (binding?.DeviceBinding == null) return;
            var mapping = DataContext as MappingViewModel;
            if (mapping == null || !mapping.ButtonsEnabled) return;
            if (binding.CanRemoveExpressionInput)
                mapping.RemoveExpressionInput(binding);
            else
                binding.DeviceBinding.ClearBinding();
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

        private void SpatialInputOptions_OnClick(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var input = (button?.DataContext as DirectionalCompassSlotViewModel)?.PhysicalBinding ??
                        button?.DataContext as DeviceBindingViewModel;
            if (button == null || input?.DeviceBinding == null || !input.BindingEnabled ||
                (DataContext as MappingViewModel)?.ButtonsEnabled != true) return;
            var menu = CompactInputBindingControl.CreateInputOptionsMenu(input, button);
            var mapping = DataContext as MappingViewModel;
            var profile = mapping?.ProfileViewModel.Profile;
            if (profile != null)
            {
                var deviceMenu = new MenuItem { Header = "Input device" };
                var currentGuid = input.DeviceBinding.DeviceConfigurationGuid;
                var any = new MenuItem
                {
                    Header = "Any input device",
                    IsChecked = currentGuid == Guid.Empty
                };
                any.Click += (s, args) => mapping.SetDirectionalSlotDevice(input, Guid.Empty);
                deviceMenu.Items.Add(any);
                foreach (var device in profile.GetDeviceConfigurationList(DeviceIoType.Input))
                {
                    if (device?.Device == null) continue;
                    var guid = device.Guid;
                    var choice = new MenuItem
                    {
                        Header = device.GetFullTitleForProfile(profile),
                        IsChecked = guid == currentGuid
                    };
                    choice.Click += (s, args) => mapping.SetDirectionalSlotDevice(input, guid);
                    deviceMenu.Items.Add(choice);
                }
                menu.Items.Insert(0, new Separator());
                menu.Items.Insert(0, deviceMenu);
            }
            menu.Items.Add(new Separator());
            var block = new MenuItem
            {
                Header = "Block original input",
                IsCheckable = true,
                IsChecked = input.Block,
                IsEnabled = input.CanBlock,
                ToolTip = input.BlockAvailability
            };
            block.Click += (s, args) => input.Block = block.IsChecked;
            menu.Items.Add(block);
            var not = new MenuItem
            {
                Header = "NOT (invert this button condition)",
                IsCheckable = true,
                IsChecked = input.InputExpressionNegated
            };
            not.Click += (s, args) => mapping.SetInputNegated(input, not.IsChecked);
            menu.Items.Add(not);
            menu.IsOpen = true;
        }

        private void BindDirectionalSlot_OnClick(object sender, RoutedEventArgs e)
        {
            var data = (sender as FrameworkElement)?.DataContext;
            var input = (data as DirectionalCompassSlotViewModel)?.PhysicalBinding ??
                        data as DeviceBindingViewModel;
            if (input?.DeviceBinding == null) return;
            if (input.DeviceBinding.IsInBindMode)
            {
                input.DeviceBinding.CancelBindMode();
                return;
            }
            try
            {
                input.ExpressionMapping?.EnableInputExpression();
                input.DeviceBinding.DeviceBindingCategory = input.DeviceBindingCategory;
                input.DeviceBinding.EnterBindMode(autoDetectInputCategory: true);
            }
            catch (Exception exception)
            {
                Logger.Error("Unable to capture directional input", exception);
                DarkMessageBox.Show("Unable to detect this direction: " + exception.GetBaseException().Message,
                    "Input detection unavailable", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DirectionalCapture_OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var data = (sender as FrameworkElement)?.DataContext;
            var input = (data as DirectionalCompassSlotViewModel)?.PhysicalBinding ??
                        data as DeviceBindingViewModel;
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
            if (group == null || mapping?.ButtonsEnabled != true) return;
            var enabled = (sender as ToggleButton)?.IsChecked == true;
            if (mapping.IsTypedDirectionalMapping)
            {
                foreach (var input in group.Inputs)
                {
                    var binding = input.DeviceBinding;
                    binding.SetBlock(enabled && binding.IsBound && binding.IsBlockable());
                }
                group.RefreshCompoundSummary();
                return;
            }
            if (group.DirectionalPrimary?.DeviceBinding?.InputDirectionSlot > 0)
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
            var binding = (toggle?.DataContext as DirectionalCompassSlotViewModel)?.PhysicalBinding ??
                          toggle?.DataContext as DeviceBindingViewModel;
            if (toggle == null || binding == null) return;
            (DataContext as MappingViewModel)?.SetInputNegated(binding, toggle.IsChecked == true);
        }

        private void AddPlugin_OnClick(object sender, RoutedEventArgs e)
        {
            var mapping = DataContext as MappingViewModel;
            if (mapping?.ButtonsEnabled != true) return;
            mapping.AddDefaultOutput();
        }

        private void ChooseInputCategory_OnClick(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var mapping = DataContext as MappingViewModel;
            if (button == null || mapping?.ButtonsEnabled != true || mapping.Plugins.Count != 1) return;

            var current = mapping.Plugins[0];
            var options = mapping.GetCompatibleInputPluginOptions();
            var menu = CreateDarkContextMenu(button);
            menu.Placement = PlacementMode.Bottom;
            // Do not strand legacy multi-axis mappings without a route to an
            // ordinary Button mapping. This explicit choice retains input keys.
            if (!string.Equals(current.OutputType, "Button", StringComparison.OrdinalIgnoreCase))
            {
                var buttonOption = new MenuItem
                {
                    Header = "Button",
                    ToolTip = "Use a button destination and keep current input assignments",
                    Foreground = Brushes.White, Background = Brushes.Transparent,
                    Padding = new Thickness(10, 6, 14, 6)
                };
                buttonOption.Click += (s, args) => mapping.ConvertToButtonMapping();
                menu.Items.Add(buttonOption);
                menu.Items.Add(new Separator());
            }

            var advanced = new MenuItem
            {
                Header = "Advanced remappers",
                Foreground = Brushes.White, Background = Brushes.Transparent,
                Padding = new Thickness(10, 6, 14, 6)
            };
            foreach (var option in options)
            {
                var selected = option;
                var item = new MenuItem
                {
                    Header = selected.Name,
                    ToolTip = selected.Description,
                    Foreground = Brushes.White,
                    Background = Brushes.Transparent,
                    Padding = new Thickness(10, 6, 14, 6),
                    IsChecked = current.Plugin.GetType() == selected.Plugin.GetType()
                };
                item.Click += (s, args) =>
                {
                    if (current.Plugin.GetType() == selected.Plugin.GetType()) return;
                    mapping.ChangeInputType(current, selected.Plugin);
                };
                advanced.Items.Add(item);
            }
            if (advanced.Items.Count > 0) menu.Items.Add(advanced);
            if (menu.Items.Count == 0)
                menu.Items.Add(new MenuItem
                {
                    Header = "No compatible input types",
                    IsEnabled = false,
                    Foreground = Brushes.LightGray
                });
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

            var options = mapping.GetCompatiblePluginOptions();
            var analogue = mapping.Mapping.DeviceBindings.Any(input => input.InputExpressionInputKind == 3);
            var menu = CreateDarkContextMenu(button);
            menu.Placement = PlacementMode.Bottom;

            var categories = options
                .Where(option => !string.Equals(option.Plugin.Group, "Axis",
                    StringComparison.OrdinalIgnoreCase) &&
                    option.Plugin.InputCategories.All(input => input.Category != DeviceBindingCategory.Range) &&
                    option.Plugin.OutputCategories.All(output => output.Category != DeviceBindingCategory.Range))
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

            if (!categories.Any(group => string.Equals(group.Key, "Button",
                    StringComparison.OrdinalIgnoreCase)) && mapping.Plugins.Count == 1)
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

        // Shared categorical destination picker. The header right-click and
        // the binding ellipsis must expose the same category choices.
        internal static void ShowOutputCategoryMenu(MappingViewModel mapping,
            FrameworkElement anchor, PluginViewModel output = null)
        {
            if (mapping?.ButtonsEnabled != true || anchor == null) return;
            var analogue = mapping.Mapping.DeviceBindings.Any(binding =>
                binding.InputExpressionInputKind == 3);
            var menu = CreateDarkContextMenu(anchor);
            menu.Placement = PlacementMode.Bottom;
            foreach (var group in mapping.GetCompatiblePluginOptions()
                .Where(option => IsPrimaryOutputCategory(option.OutputType))
                .GroupBy(option => option.OutputType, StringComparer.OrdinalIgnoreCase))
            {
                var choice = PreferredOutputImplementation(group, analogue);
                if (choice == null) continue;
                var selected = choice;
                var category = group.Key;
                var item = new MenuItem
                {
                    Header = category,
                    ToolTip = selected.Description,
                    IsChecked = output == null
                        ? mapping.Plugins.All(plugin => string.Equals(plugin.OutputType,
                            category, StringComparison.OrdinalIgnoreCase))
                        : string.Equals(output.OutputType, category, StringComparison.OrdinalIgnoreCase),
                    Foreground = Brushes.White,
                    Background = Brushes.Transparent,
                    Padding = new Thickness(10, 6, 14, 6)
                };
                item.Click += (sender, args) =>
                {
                    if (output != null) mapping.ChangeOutputType(output, selected.Plugin);
                    else mapping.ChangeAllOutputTypes(selected.Plugin);
                    mapping.RefreshRepresentationSelections();
                };
                menu.Items.Add(item);
            }
            if (menu.Items.Count == 0)
                menu.Items.Add(new MenuItem { Header = "No compatible output categories", IsEnabled = false });
            menu.IsOpen = true;
        }

        internal void ShowOutputCategoryMenuForBinding(DeviceBindingViewModel binding,
            FrameworkElement anchor)
        {
            var mapping = DataContext as MappingViewModel;
            var output = mapping?.Plugins.FirstOrDefault(plugin => plugin.DeviceBindings
                .Any(item => ReferenceEquals(item.DeviceBinding, binding?.DeviceBinding)));
            ShowOutputCategoryMenu(mapping, anchor, output);
        }

        private void ChangeOutputType_OnClick(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var output = button?.DataContext as PluginViewModel;
            var mapping = output?.MappingViewModel;
            if (mapping?.ButtonsEnabled != true) return;
            var options = mapping.GetCompatiblePluginOptions();
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
                addOutput.Click += (s, args) => mapping.AddDefaultOutput();
                menu.Items.Add(addOutput);
            }
            menu.IsOpen = true;
        }

        private void FilterDestinationSelector_OnClick(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var output = button?.DataContext as PluginViewModel;
            var mapping = output?.MappingViewModel;
            if (button == null || mapping?.ButtonsEnabled != true ||
                !string.Equals(output.OutputType, "Filter", StringComparison.OrdinalIgnoreCase)) return;

            var profile = mapping.ProfileViewModel.Profile;
            var menu = CreateDarkContextMenu(button);
            menu.Placement = PlacementMode.Bottom;
            menu.Items.Add(new MenuItem
            {
                Header = "Filter", IsChecked = true, IsEnabled = false,
                Foreground = Brushes.White, Background = Brushes.Transparent,
                Padding = new Thickness(10, 6, 14, 6)
            });

            var analogue = mapping.Mapping.DeviceBindings.Any(input => input.InputExpressionInputKind == 3);
            var options = mapping.GetCompatiblePluginOptions();
            foreach (var configuration in profile.GetDeviceConfigurationList(DeviceIoType.Output)
                         .Where(item => item?.Device != null))
            {
                var supportsButton = DeviceBindingViewModel.SupportsOutputCategory(configuration,
                    DeviceBindingCategory.Momentary, profile.Context);
                var supportsAxis = DeviceBindingViewModel.SupportsOutputCategory(configuration,
                    DeviceBindingCategory.Range, profile.Context);
                if (!supportsButton && !supportsAxis) continue;

                var name = supportsButton ? (analogue ? "Axis to Button" : "Button to Button")
                                          : (analogue ? "Axis to Axis" : "Button to Axis");
                var candidate = options.FirstOrDefault(option =>
                    string.Equals(option.Name, name, StringComparison.OrdinalIgnoreCase));
                if (candidate == null) continue;
                var selectedConfiguration = configuration;
                var selectedTemplate = candidate.Plugin;
                var item = new MenuItem
                {
                    Header = configuration.GetFullTitleForProfile(profile),
                    Foreground = Brushes.White, Background = Brushes.Transparent,
                    Padding = new Thickness(10, 6, 14, 6)
                };
                item.Click += (s, args) => Dispatcher.BeginInvoke((Action)(() =>
                {
                    var index = mapping.Plugins.IndexOf(output);
                    if (index < 0 || !mapping.ChangeOutputType(output, selectedTemplate)) return;
                    var next = mapping.Plugins[index].DeviceBindings.FirstOrDefault(binding =>
                        binding.DeviceBinding?.DeviceIoType == DeviceIoType.Output);
                    if (next?.DeviceBinding == null) return;
                    next.DeviceBinding.SetDeviceConfigurationGuid(selectedConfiguration.Guid);
                    next.RefreshDeviceList();
                    profile.Context.ContextChanged();
                }), System.Windows.Threading.DispatcherPriority.Background);
                menu.Items.Add(item);
            }
            if (menu.Items.Count == 1)
                menu.Items.Add(new MenuItem { Header = "No compatible output devices", IsEnabled = false });
            menu.IsOpen = true;
        }

        internal bool ApplyOutputControlChoice(DeviceBindingViewModel binding, Guid deviceGuid,
            Device outputDevice, DeviceBindingInfo selectedControl, int direction = 0)
        {
            var mapping = DataContext as MappingViewModel;
            if (mapping?.ButtonsEnabled != true || binding?.DeviceBinding == null || selectedControl == null)
                return false;

            var original = mapping.Plugins.FirstOrDefault(plugin =>
                plugin.DeviceBindings.Any(item => ReferenceEquals(item.DeviceBinding, binding.DeviceBinding)));
            if (original == null) return false;
            var index = mapping.Plugins.IndexOf(original);
            var requested = selectedControl.DeviceBindingCategory;
            var configuration = mapping.ProfileViewModel.Profile.GetDeviceConfiguration(
                DeviceIoType.Output, deviceGuid);
            if (configuration?.Device == null || !ReferenceEquals(configuration.Device, outputDevice) ||
                binding.DeviceBinding.DeviceConfigurationGuid != deviceGuid) return false;
            var isTrigger = requested == DeviceBindingCategory.Range &&
                DeviceVisualCatalog.IsKnownTriggerAxis(configuration,
                    mapping.ProfileViewModel.Profile, DeviceIoType.Output, selectedControl.KeyValue);
            var selectedRepresentation = requested == DeviceBindingCategory.Momentary ? 3 :
                requested == DeviceBindingCategory.Range ? (isTrigger ? 4 : 2) : 0;
            if (mapping.IsTypedDirectionalMapping)
            {
                if (selectedRepresentation == 0) return false;
                if (mapping.OutputIsFullStick)
                {
                    // A 4-Way destination is a complete bank. Selecting a single
                    // control cannot replace the whole bank with a one-sided type.
                    var currentCategory = original.Plugin.Outputs.FirstOrDefault()
                        ?.DeviceBindingCategory;
                    if (currentCategory != requested) return false;
                }
                else if (mapping.Mapping.EffectiveOutputRepresentation != selectedRepresentation)
                {
                    // Trigger and Button share the selector but remain distinct
                    // physical signal types, so never turn a trigger into an Axis.
                    if (!mapping.SetOutputRepresentation(selectedRepresentation)) return false;
                    index = Math.Min(index, mapping.Plugins.Count - 1);
                    original = mapping.Plugins[index];
                }
            }
            var existing = original.Plugin.Outputs.FirstOrDefault()?.DeviceBindingCategory;
            if (existing != requested)
            {
                var analogue = mapping.Mapping.DeviceBindings.Any(input => input.InputExpressionInputKind == 3);
                var preferredName = requested == DeviceBindingCategory.Range
                    ? (analogue ? "Axis to Axis" : "Button to Axis")
                    : requested == DeviceBindingCategory.Momentary
                        ? (analogue ? "Axis to Button" : "Button to Button") : null;
                if (preferredName == null) return false;
                var replacement = mapping.GetCompatiblePluginOptions().FirstOrDefault(option =>
                    string.Equals(option.Name, preferredName, StringComparison.OrdinalIgnoreCase));
                if (replacement == null || !mapping.ChangeOutputType(original, replacement.Plugin)) return false;
            }

            if (index < 0 || index >= mapping.Plugins.Count) return false;
            var destinationPlugin = mapping.Plugins[index];
            var destination = destinationPlugin.DeviceBindings.FirstOrDefault(item =>
                item.DeviceBinding?.DeviceIoType == DeviceIoType.Output);
            if (destination?.DeviceBinding == null) return false;
            destination.DeviceBinding.DeviceBindingCategory = requested;
            destination.DeviceBinding.SetDeviceConfigurationGuid(deviceGuid);
            destination.DeviceBinding.SetKeyTypeValue(selectedControl.KeyType,
                selectedControl.KeyValue, selectedControl.KeySubValue);

            // A button maps to a signed analogue extreme for the chosen stick direction.
            // Neutral is restored when released; a full axis choice remains unrestricted.
            var pressed = destinationPlugin.Plugin.GetType().GetProperty("RangePressed");
            var released = destinationPlugin.Plugin.GetType().GetProperty("Range");
            if (pressed != null && pressed.PropertyType == typeof(double))
            {
                pressed.SetValue(destinationPlugin.Plugin, direction < 0 ? -100.0 : 100.0, null);
                if (released != null && released.PropertyType == typeof(double))
                    released.SetValue(destinationPlugin.Plugin, 0.0, null);
            }
            else
            {
                var invert = destinationPlugin.Plugin.GetType().GetProperty("Invert");
                if (invert != null && invert.PropertyType == typeof(bool))
                    invert.SetValue(destinationPlugin.Plugin, direction < 0, null);
            }
            destination.RefreshDeviceList();
            mapping.InferOutputTypeFromAssignment(requested,
                isTrigger || DeviceVisualCatalog.IsTriggerBinding(destination.DeviceBinding,
                    mapping.ProfileViewModel.Profile));
            mapping.ProfileViewModel.Profile.Context.ContextChanged();
            return true;
        }

        internal bool SwitchOutputBindingToFilter(DeviceBindingViewModel binding)
        {
            var mapping = DataContext as MappingViewModel;
            if (mapping?.ButtonsEnabled != true || binding?.DeviceBinding == null) return false;
            var current = mapping.Plugins.FirstOrDefault(plugin =>
                plugin.DeviceBindings.Any(item => ReferenceEquals(item.DeviceBinding, binding.DeviceBinding)));
            if (current == null) return false;
            var analogue = mapping.Mapping.DeviceBindings.Any(input => input.InputExpressionInputKind == 3);
            var filter = mapping.GetCompatiblePluginOptions().FirstOrDefault(option =>
                string.Equals(option.Name, analogue ? "Axis to Filter" : "Button to Filter",
                    StringComparison.OrdinalIgnoreCase)) ??
                mapping.GetCompatiblePluginOptions().FirstOrDefault(option =>
                    string.Equals(option.OutputType, "Filter", StringComparison.OrdinalIgnoreCase));
            return filter != null && mapping.ChangeOutputType(current, filter.Plugin);
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
                DarkMessageBox.Show(error, "Keyboard preset unavailable", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void AxisStickInputPreset_OnClick(object sender, RoutedEventArgs e)
        {
            var mapping = DataContext as MappingViewModel;
            int index;
            if (mapping == null || !int.TryParse((sender as Button)?.Tag as string, out index)) return;
            string error;
            if (!mapping.ApplyControllerStickInputPreset(index, out error) && !string.IsNullOrEmpty(error))
                DarkMessageBox.Show(error, "Controller input preset unavailable", MessageBoxButton.OK,
                    MessageBoxImage.Information);
        }

        private void AxisControllerPreset_OnClick(object sender, RoutedEventArgs e)
        {
            var axis = (sender as FrameworkElement)?.DataContext as PluginViewModel;
            if (axis?.MappingViewModel == null) return;
            string error;
            if (!axis.MappingViewModel.ApplyControllerDPadPreset(out error) && !string.IsNullOrEmpty(error))
                DarkMessageBox.Show(error, "Controller preset unavailable", MessageBoxButton.OK, MessageBoxImage.Information);
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

        private async void CompassOutputBind_OnClick(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var binding = button?.DataContext as DeviceBindingViewModel;
            if (binding == null) return;
            await OpenQuickOutputPickerAsync(binding, button);
        }

        private void CompoundOutputDevice_OnClick(object sender, RoutedEventArgs e)
        {
            var mapping = DataContext as MappingViewModel;
            var button = sender as Button;
            if (mapping?.ButtonsEnabled != true || button == null ||
                (!mapping.IsCompoundOutput && !mapping.HasOutputDirectionalCompass))
                return;
            var menu = CreateDarkContextMenu(button);
            menu.Placement = PlacementMode.Bottom;
            if (mapping.HasOutputDirectionalCompass)
            {
                foreach (var device in mapping.ProfileViewModel.Profile
                             .GetDeviceConfigurationList(DeviceIoType.Output))
                {
                    if (device?.Device == null) continue;
                    var chosenGuid = device.Guid;
                    var option = new MenuItem { Header = device.GetFullTitleForProfile(mapping.ProfileViewModel.Profile) };
                    option.Click += (source, args) =>
                        mapping.SetFourWayButtonOutputDevice(chosenGuid);
                    menu.Items.Add(option);
                }
                if (menu.Items.Count == 0)
                    menu.Items.Add(new MenuItem { Header = "No configured output devices", IsEnabled = false });
                menu.IsOpen = true;
                return;
            }
            foreach (var device in mapping.GetFourWayOutputChoices()
                         .GroupBy(choice => choice.DeviceConfigurationGuid))
            {
                var choices = device.ToList();
                var item = new MenuItem { Header = choices[0].DeviceTitle };
                item.Click += (source, args) =>
                {
                    var selected = choices.FirstOrDefault(choice => choice.Destination == "Left stick")
                                   ?? choices[0];
                    mapping.ApplyTypedFourWayOutput(selected);
                };
                menu.Items.Add(item);
            }
            if (menu.Items.Count == 0)
                menu.Items.Add(new MenuItem { Header = "No configured output devices", IsEnabled = false });
            menu.IsOpen = true;
        }

        private void CompoundOutput_OnClick(object sender, RoutedEventArgs e)
        {
            // The large binding surface is the graphical quick selector, not
            // the former full technical channel/context menu.
            ShowQuickCompoundOutputMenu(DataContext as MappingViewModel,
                sender as FrameworkElement);
        }

        private void CompoundDevice_OnClick(object sender, RoutedEventArgs e)
        {
            var mapping = DataContext as MappingViewModel;
            var button = sender as Button;
            var group = button?.DataContext as InputConditionViewModel;
            if (mapping?.ButtonsEnabled != true || group == null) return;
            var profile = mapping.ProfileViewModel.Profile;
            var selectedGuid = group.Inputs.FirstOrDefault()?.DeviceBinding?.DeviceConfigurationGuid
                ?? Guid.Empty;
            var menu = CreateDarkContextMenu(button);
            menu.Placement = PlacementMode.Bottom;
            var any = new MenuItem { Header = "Any input device", IsChecked = selectedGuid == Guid.Empty };
            any.Click += (s, args) => mapping.SetCompoundInputDevice(group.GroupIndex, Guid.Empty);
            menu.Items.Add(any);
            foreach (var device in profile.GetDeviceConfigurationList(DeviceIoType.Input))
            {
                if (device?.Device == null) continue;
                var guid = device.Guid;
                var item = new MenuItem
                {
                    Header = device.GetFullTitleForProfile(profile),
                    IsChecked = guid == selectedGuid
                };
                item.Click += (s, args) => mapping.SetCompoundInputDevice(group.GroupIndex, guid);
                menu.Items.Add(item);
            }
            menu.IsOpen = true;
        }

        private void CompoundPrimaryBind_OnClick(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var group = button?.DataContext as InputConditionViewModel;
            var mapping = DataContext as MappingViewModel;
            if (mapping?.ButtonsEnabled != true || group == null) return;
            // Axis and Trigger really have one physical bind button. Capturing
            // from the large centre control must not bury detection in a menu.
            if (group.CoreInputs.Count == 1)
            {
                var input = group.CoreInputs[0];
                if (input?.DeviceBinding == null) return;
                if (input.DeviceBinding.IsInBindMode)
                {
                    input.DeviceBinding.CancelBindMode();
                    return;
                }
                try
                {
                    input.DeviceBinding.EnterBindMode(autoDetectInputCategory: true);
                }
                catch (Exception exception)
                {
                    Logger.Error("Unable to capture compound input", exception);
                    DarkMessageBox.Show("Unable to capture input: " +
                        exception.GetBaseException().Message,
                        "Input detection unavailable", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                return;
            }
            // One compound 4-Way selection, even when routing internally
            // requires multiple physical channels.
            ShowQuickCompoundInputMenu(mapping, button, group.GroupIndex);
        }

        private void CompoundBindingsMenu_OnClick(object sender, RoutedEventArgs e)
        {
            var mapping = DataContext as MappingViewModel;
            var button = sender as Button;
            if (mapping?.IsCompoundInput != true || !mapping.ButtonsEnabled || button == null) return;
            var menu = CreateDarkContextMenu(button);
            var group = button.DataContext as InputConditionViewModel;
            foreach (var input in group?.CoreInputs ?? mapping.DeviceBindings.Where(item =>
                !item.DeviceBinding.IsDirectionalAndCondition))
            {
                var binding = input;
                var label = binding.DeviceBindingName;
                var assignment = binding.DeviceBinding.IsBound
                    ? binding.CompactBindButtonText : "Unassigned";
                var item = new MenuItem
                {
                    Header = label + "  —  " + assignment,
                    IsEnabled = mapping.ButtonsEnabled
                };
                item.Click += (source, args) =>
                {
                    // Use the same Binding Category Menu as an ordinary input.
                    // The underlying directional assignment remains independently addressable,
                    // but it occupies no permanent row in the compound editor.
                    Dispatcher.BeginInvoke((Action)(() =>
                    {
                        var picker = CompactInputBindingControl.CreateInputOptionsMenu(binding, button);
                        picker.IsOpen = true;
                    }), System.Windows.Threading.DispatcherPriority.Background);
                };
                menu.Items.Add(item);
            }
            menu.IsOpen = true;
        }

        private void CompoundPreset_OnClick(object sender, RoutedEventArgs e)
        {
            var mapping = DataContext as MappingViewModel;
            var button = sender as Button;
            if (mapping?.ButtonsEnabled != true || button == null) return;
            var groupIndex = (button.DataContext as InputConditionViewModel)?.GroupIndex ?? 0;
            var menu = CreateDarkContextMenu(button);
            foreach (var preset in new[] { "WASD", "Arrows", "Numpad", "UHJK" })
            {
                var name = preset;
                var item = new MenuItem { Header = name };
                item.Click += (source, args) => ApplyCompoundPreset(mapping, name, groupIndex);
                menu.Items.Add(item);
            }
            // A D-pad preset can promote a Button pair or Axis to 4-Way.
            {
                var dpad = new MenuItem { Header = "Controller D-pad" };
                dpad.Click += (source, args) =>
                {
                    string error;
                    if (!mapping.ApplyDirectionalDPadPreset(out error, groupIndex))
                        DarkMessageBox.Show(error ?? "D-pad preset could not be applied.",
                            "Binding preset unavailable", MessageBoxButton.OK, MessageBoxImage.Information);
                };
                menu.Items.Add(dpad);
            }
            var leftStick = new MenuItem { Header = "Controller left stick" };
            leftStick.Click += (source, args) =>
            {
                string error;
                if (!mapping.ApplyTypedControllerStickPreset(0, out error, groupIndex))
                    DarkMessageBox.Show(error ?? "The left stick could not be assigned.",
                        "Binding preset unavailable", MessageBoxButton.OK, MessageBoxImage.Information);
            };
            menu.Items.Add(leftStick);
            var rightStick = new MenuItem { Header = "Controller right stick" };
            rightStick.Click += (source, args) =>
            {
                string error;
                if (!mapping.ApplyTypedControllerStickPreset(2, out error, groupIndex))
                    DarkMessageBox.Show(error ?? "The right stick could not be assigned.",
                        "Binding preset unavailable", MessageBoxButton.OK, MessageBoxImage.Information);
            };
            menu.Items.Add(rightStick);
            var faceButtons = new MenuItem { Header = "Controller face buttons" };
            faceButtons.Click += (source, args) =>
            {
                string error;
                if (!mapping.ApplyTypedControllerFaceButtons(out error, groupIndex))
                    DarkMessageBox.Show(error ?? "The face buttons could not be assigned.",
                        "Binding preset unavailable", MessageBoxButton.OK, MessageBoxImage.Information);
            };
            menu.Items.Add(faceButtons);
            foreach (var saved in mapping.ProfileViewModel.Profile.CompoundBindingPresets ??
                new System.Collections.Generic.List<CompoundBindingPreset>())
            {
                if (saved.Capacity != 4 && saved.Capacity != mapping.DirectionalCapacity) continue;
                var name = saved.Name;
                var item = new MenuItem { Header = name };
                item.Click += (source, args) => ApplyCompoundPreset(mapping, name, groupIndex);
                menu.Items.Add(item);
            }
            menu.Items.Add(new Separator());
            var analogue = new MenuItem { Header = "Use analogue axis bindings" };
            analogue.Click += (source, args) =>
            {
                string error;
                if (!mapping.UseAnalogueCompoundInput(out error))
                    DarkMessageBox.Show(error ?? "Analogue bindings could not be restored.",
                        "Input representation unavailable", MessageBoxButton.OK, MessageBoxImage.Information);
            };
            menu.Items.Add(analogue);
            var save = new MenuItem { Header = "Save current bindings as preset..." };
            save.Click += (source, args) =>
            {
                var dialog = new Window
                {
                    Title = "Save keyboard binding preset",
                    Width = 360,
                    Height = 164,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    ResizeMode = ResizeMode.NoResize,
                    Background = new SolidColorBrush(Color.FromRgb(38, 38, 38)),
                    Foreground = Brushes.White,
                    Owner = Window.GetWindow(this)
                };
                var layout = new StackPanel { Margin = new Thickness(14) };
                layout.Children.Add(new TextBlock { Text = "Preset name", Margin = new Thickness(0, 0, 0, 6) });
                var textBox = new TextBox { Height = 30, MaxLength = 60 };
                layout.Children.Add(textBox);
                var saveButton = new Button
                {
                    Content = "Save", Width = 85, Height = 30,
                    HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0)
                };
                saveButton.Click += (clicked, clickArgs) =>
                {
                    string error;
                    if (!mapping.SaveCompoundKeyboardPreset(textBox.Text, out error, groupIndex))
                    {
                        DarkMessageBox.Show(error, "Cannot save preset", MessageBoxButton.OK,
                            MessageBoxImage.Information);
                        return;
                    }
                    dialog.DialogResult = true;
                };
                layout.Children.Add(saveButton);
                dialog.Content = layout;
                dialog.ShowDialog();
            };
            menu.Items.Add(save);
            menu.IsOpen = true;
        }

        private static void ApplyCompoundPreset(MappingViewModel mapping, string name,
            int groupIndex)
        {
            string error;
            if (!mapping.ApplyDirectionalKeyboardPreset(name, out error, groupIndex))
                DarkMessageBox.Show(error ?? "Preset could not be applied.",
                    "Binding preset unavailable", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void CompoundBlock_OnClick(object sender, RoutedEventArgs e)
        {
            var mapping = DataContext as MappingViewModel;
            if (mapping?.CanBlockCompound != true) return;
            mapping.SetCompoundBlockEnabled((sender as ToggleButton)?.IsChecked == true);
        }

        private void AddMappingFilter_OnClick(object sender, RoutedEventArgs e)
        {
            var mapping = DataContext as MappingViewModel;
            var button = sender as Button;
            if (mapping?.ButtonsEnabled != true || button == null) return;
            var outputs = mapping.Plugins.Where(plugin => plugin.CanAddFilter).ToList();
            if (outputs.Count == 0) return;
            if (outputs.Count == 1)
            {
                outputs[0].AddFilter();
                return;
            }

            // One mapping-level action; select the output receiving the filter.
            // Filters remain attached to their existing output plugin internally.
            var menu = new ContextMenu
            {
                PlacementTarget = button,
                Placement = PlacementMode.Bottom
            };
            for (var index = 0; index < outputs.Count; index++)
            {
                var output = outputs[index];
                var name = string.IsNullOrWhiteSpace(output.DirectionalSlotLabel)
                    ? "Output " + (index + 1) : output.DirectionalSlotLabel;
                var item = new MenuItem { Header = name };
                item.Click += (source, args) => output.AddFilter();
                menu.Items.Add(item);
            }
            menu.IsOpen = true;
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

        private void RemoveCompoundOutput_OnClick(object sender, RoutedEventArgs e)
        {
            (DataContext as MappingViewModel)?.RemoveAllOutputSlots();
        }

        private void RemoveOutput_OnClick(object sender, RoutedEventArgs e)
        {
            var plugin = (sender as FrameworkElement)?.DataContext as PluginViewModel;
            if (plugin?.CanRemove == true) plugin.Remove();
        }

    }
}
