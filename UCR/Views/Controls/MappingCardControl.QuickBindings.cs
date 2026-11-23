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
using System.Windows.Threading;
using HidWizards.UCR.Core.Utilities;
using HidWizards.UCR.Core.Models;
using HidWizards.UCR.Core.Models.Binding;
using HidWizards.UCR.Utilities;
using HidWizards.UCR.ViewModels;
using HidWizards.UCR.ViewModels.Presentation;
using HidWizards.UCR.ViewModels.ProfileViewModels;

namespace HidWizards.UCR.Views.Controls
{
    public partial class MappingCardControl
    {
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
                if (descriptor.IsCompoundVisual && mapping.InputIsFullStick)
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

                CreateQuickOutputMenu(mappingViewModel, descriptor, placementTarget).IsOpen = true;
                return true;
            }
            catch (Exception exception)
            {
                Logger.Error("Failed to start mapping-card quick output bind", exception);
                ShowOutputPickerStatus(placementTarget, "Output controls are unavailable.");
                return false;
            }
        }

        internal static void ShowOutputPickerStatus(FrameworkElement target, string message)
        {
            var menu = CreateDarkContextMenu(target);
            menu.Items.Add(new MenuItem { Header = message, IsEnabled = false });
            menu.IsOpen = true;
        }

        internal static ContextMenu CreateQuickOutputMenu(MappingViewModel mappingViewModel,
            BindingVisualDescriptor descriptor, FrameworkElement placementTarget,
            DeviceBindingCategory? category = null)
        {
            var options = mappingViewModel.GetQuickOutputBindingOptions(descriptor);
            if (category.HasValue)
                options.RemoveAll(option => option.OutputControl.DeviceBindingCategory != category.Value);
            var menu = CreateBoundedOutputMenu(placementTarget);

            var originalContext = placementTarget.DataContext;
            var anchorInvalidated = false;
            bool IsCurrentAnchor() => !anchorInvalidated && placementTarget.IsLoaded &&
                ReferenceEquals(placementTarget.DataContext, originalContext) &&
                mappingViewModel.ProfileViewModel.MappingsList?.Contains(mappingViewModel) == true;

            void ShowAssignmentFailure(string message)
            {
                placementTarget.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    if (IsCurrentAnchor()) ShowOutputPickerStatus(placementTarget, message);
                }));
            }

            void InvalidateAnchor()
            {
                anchorInvalidated = true;
                menu.IsOpen = false;
            }
            RoutedEventHandler unloaded = (sender, args) => InvalidateAnchor();
            DependencyPropertyChangedEventHandler contextChanged = (sender, args) => InvalidateAnchor();
            menu.Opened += (sender, args) =>
            {
                placementTarget.Unloaded += unloaded;
                placementTarget.DataContextChanged += contextChanged;
            };
            menu.Closed += (sender, args) =>
            {
                placementTarget.Unloaded -= unloaded;
                placementTarget.DataContextChanged -= contextChanged;
            };

            if (options.Count == 0)
            {
                menu.Items.Add(new MenuItem
                {
                    Header = mappingViewModel.GetOutputPickerUnavailableReason(
                        mappingViewModel.ResolveCollapsedBinding(descriptor, DeviceIoType.Output)),
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
                        MaxWidth = Math.Max(0, menu.MaxWidth - 100),
                        TextTrimming = TextTrimming.CharacterEllipsis,
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
                    System.Windows.Automation.AutomationProperties.SetName(item, capturedOption.Title);
                    item.Click += (clickSender, clickArgs) =>
                    {
                        if (!IsCurrentAnchor() || !mappingViewModel.CanApplyOutputBinding) return;
                        try
                        {
                            if (!mappingViewModel.ApplyQuickOutputBinding(descriptor, capturedOption))
                                ShowAssignmentFailure("That control could not be assigned to this output slot.");
                        }
                        catch (Exception exception)
                        {
                            Logger.Error("Failed to apply mapping output", exception);
                            ShowAssignmentFailure("Could not assign that output.");
                        }
                    };
                    menu.Items.Add(item);
                }
            }

            return menu;
        }

        private static StackPanel CompoundChoiceHeader(string label,
            ControlVisualKind kind, string controls, double maxLabelWidth = double.PositiveInfinity)
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
                MaxWidth = maxLabelWidth, TextTrimming = TextTrimming.CharacterEllipsis,
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
                if (!mapping.CanApplyCompoundInputPreset(preset.Name)) continue;
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
            var menu = CreateBoundedOutputMenu(anchor);
            var profile = mapping.ProfileViewModel.Profile;
            var choices = mapping.GetFourWayOutputChoices();
            var multipleDevices = choices.Select(choice => choice.DeviceConfigurationGuid)
                .Distinct().Skip(1).Any();

            foreach (var choice in choices)
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
                var label = multipleDevices ? selected.Destination + " (" + selected.DeviceTitle + ")" :
                    selected.Destination;
                var item = new MenuItem
                {
                    Header = CompoundChoiceHeader(label, kind, controls, Math.Max(0, menu.MaxWidth - 100)),
                    ToolTip = label
                };
                System.Windows.Automation.AutomationProperties.SetName(item, label);
                item.Click += (sender, args) =>
                {
                    if (!mapping.ApplyTypedFourWayOutput(selected))
                        ShowOutputPickerStatus(anchor, "That 4-Way output is unavailable.");
                };
                menu.Items.Add(item);
            }
            if (menu.Items.Count == 0)
                menu.Items.Add(new MenuItem
                {
                    Header = profile.GetPrimaryDeviceConfiguration(DeviceIoType.Output) == null
                        ? "Add an output device to this profile"
                        : "No compatible 4-Way outputs",
                    IsEnabled = false
                });
            menu.IsOpen = true;
        }

        private static ContextMenu CreateBoundedOutputMenu(FrameworkElement anchor)
        {
            var menu = CreateDarkContextMenu(anchor);
            menu.Placement = PlacementMode.Bottom;
            var owner = Window.GetWindow(anchor);
            if (owner == null || !CompactInputBindingControl.TryGetPickerBounds(anchor, owner, out var bounds))
                return menu;

            menu.MaxWidth = bounds.Width;
            menu.MaxHeight = bounds.Height;
            menu.Placement = PlacementMode.Custom;
            menu.CustomPopupPlacementCallback = (popupSize, targetSize, offset) =>
            {
                var origin = anchor.TranslatePoint(new Point(), owner);
                var position = CompactInputBindingControl.PlaceOutputPicker(bounds, origin, targetSize, popupSize);
                return new[]
                {
                    new CustomPopupPlacement(position, PopupPrimaryAxis.None)
                };
            };
            return menu;
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
    }
}
