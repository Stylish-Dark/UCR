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
    // Output picker construction and binding belong together, separately
    // from the card's layout and mapping command handlers.
    public partial class MappingCardControl
    {
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
                    return await mappingViewModel.QuickBindOutputAsync(descriptor);
                }

                return ShowQuickOutputMenu(mappingViewModel, descriptor, placementTarget);
            }
            catch (Exception exception)
            {
                Logger.Error("Failed to start mapping-card quick output bind", exception);
                mappingViewModel.OutputSelectionFeedback = "Output binding could not start. Details are in the log.";
                return false;
            }
        }

        private static bool ShowQuickOutputMenu(MappingViewModel mappingViewModel,
            BindingVisualDescriptor descriptor, FrameworkElement placementTarget)
        {
            var options = mappingViewModel.GetQuickOutputBindingOptions(descriptor);
            if (options.Count == 0)
            {
                mappingViewModel.OutputSelectionFeedback =
                    mappingViewModel.OutputSideDeviceGuid == Guid.Empty
                        ? "Choose an output device before binding a destination."
                        : "No compatible output controls are available for this device.";
                return false;
            }

            var menu = CreateDarkContextMenu(placementTarget);
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

            menu.Closed += (sender, args) =>
            {
                if (ReferenceEquals(placementTarget.ContextMenu, menu))
                    placementTarget.ContextMenu = null;
            };
            placementTarget.ContextMenu = menu;
            menu.IsOpen = true;
            return true;
        }

        private static System.Collections.Generic.IEnumerable<DeviceBindingNode> FlattenPickerNodes(
            System.Collections.Generic.IEnumerable<DeviceBindingNode> nodes)
        {
            if (nodes == null) yield break;
            foreach (var node in nodes)
            {
                if (node == null) continue;
                // Only terminal controls can be bound; category nodes are navigation.
                if (node.IsBinding && node.DeviceBindingInfo != null) yield return node;
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
            {
                mapping.OutputSelectionFeedback = "No compatible output controls are available on this device.";
                return;
            }

            // An unattached ContextMenu can be immediately dismissed by the
            // opening click on the underlying raised Button. Own it through
            // the actual button, just as the working mapping action menus do.
            menu.Closed += (sender, args) =>
            {
                if (ReferenceEquals(target.ContextMenu, menu))
                    target.ContextMenu = null;
            };
            target.ContextMenu = menu;
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

    }
}
