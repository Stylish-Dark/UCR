using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using System.Text.RegularExpressions;
using HidWizards.UCR.Core.Annotations;
using HidWizards.UCR.Core.Managers;
using HidWizards.UCR.Core.Models;
using HidWizards.UCR.Core.Models.Binding;
using HidWizards.UCR.Core.Utilities;
using HidWizards.UCR.Views.Dialogs;
using HidWizards.UCR.ViewModels.Presentation;
using MaterialDesignThemes.Wpf;

namespace HidWizards.UCR.ViewModels.ProfileViewModels
{
    public partial class MappingViewModel
    {
        public List<FourWayOutputDeviceOption> GetFourWayOutputDevices()
        {
            return GetFourWayOutputChoices().GroupBy(option => option.DeviceConfigurationGuid)
                .Select(group => new FourWayOutputDeviceOption
                {
                    Guid = group.Key, Title = group.First().DeviceTitle
                }).ToList();
        }

        public void SetFourWayOutputDevice(PluginViewModel axis, Guid guid)
        {
            if (axis?.IsFourButtonAxis != true || !ButtonsEnabled) return;
            var options = GetFourWayOutputChoices()
                .Where(option => option.DeviceConfigurationGuid == guid).ToList();
            if (options.Count == 0) return;
            var preferred = axis.FourWayOutputLabel;
            var chosen = options.FirstOrDefault(option => option.Destination == preferred)
                ?? options.FirstOrDefault(option => option.Destination == "Left stick")
                ?? options.First();
            ApplyFourWayOutput(axis, chosen);
        }

        public List<FourWayOutputChoice> GetFourWayOutputChoices()
        {
            var result = new List<FourWayOutputChoice>();
            var profile = ProfileViewModel.Profile;
            foreach (var device in profile.GetDeviceConfigurationList(DeviceIoType.Output))
            {
                if (device?.Device == null) continue;
                var nodes = FlattenBindingNodes(device.Device.GetDeviceBindingMenu(profile.Context, DeviceIoType.Output))
                    .Where(node => node?.DeviceBindingInfo != null).ToList();
                var axes = nodes.Where(node => node.DeviceBindingInfo.DeviceBindingCategory == DeviceBindingCategory.Range &&
                    node.DeviceBindingInfo.KeyValue >= 0 && node.DeviceBindingInfo.KeyValue <= 3)
                    .GroupBy(node => node.DeviceBindingInfo.KeyType)
                    .FirstOrDefault(group => Enumerable.Range(0, 4).All(index =>
                        group.Any(node => node.DeviceBindingInfo.KeyValue == index)))?.ToList();
                if (axes != null)
                {
                    foreach (var side in new[] { 0, 2 })
                    {
                        result.Add(new FourWayOutputChoice
                        {
                            DeviceConfigurationGuid = device.Guid,
                            DeviceTitle = device.GetFullTitleForProfile(profile),
                            Destination = side == 0 ? "Left stick" : "Right stick",
                            Controls = new[] { axes.First(node => node.DeviceBindingInfo.KeyValue == side).DeviceBindingInfo,
                                axes.First(node => node.DeviceBindingInfo.KeyValue == side + 1).DeviceBindingInfo }
                        });
                    }
                }
                var dpad = FindDPadControls(device, DeviceIoType.Output);
                if (dpad != null)
                    result.Add(new FourWayOutputChoice
                    {
                        DeviceConfigurationGuid = device.Guid,
                        DeviceTitle = device.GetFullTitleForProfile(profile),
                        Destination = "D-pad", IsDPad = true,
                        Controls = dpad
                    });

                // Keyboard presets and four face buttons are also complete
                // four-way destinations. All four must resolve before being offered.
                var kind = DeviceVisualCatalog.Describe(device, profile, DeviceIoType.Output).Kind;
                var momentary = nodes.Where(node => node.DeviceBindingInfo.DeviceBindingCategory ==
                    DeviceBindingCategory.Momentary).ToList();
                Action<string, string[]> addDigital = (name, keys) =>
                {
                    if (keys == null || keys.Length != 4) return;
                    var controls = new DeviceBindingInfo[4];
                    // FourWayOutputChoice stores Up, Down, Left, Right.
                    var indexOrder = new[] { 2, 3, 0, 1 };
                    for (var index = 0; index < 4; index++)
                    {
                        var key = keys[indexOrder[index]];
                        var aliases = key.StartsWith("Numpad", StringComparison.OrdinalIgnoreCase)
                            ? new[] { key, key.Replace("Numpad", "Num"),
                                key.Replace("Numpad", "Keypad") }
                            : new[] { key };
                        var option = momentary.FirstOrDefault(node => aliases.Any(alias =>
                            MatchesControlLeafName(node.Title, alias)));
                        if (option == null) return;
                        controls[index] = option.DeviceBindingInfo;
                    }
                    result.Add(new FourWayOutputChoice
                    {
                        DeviceConfigurationGuid = device.Guid,
                        DeviceTitle = device.GetFullTitleForProfile(profile),
                        Destination = name, IsDPad = true, Controls = controls,
                        VisualKeys = string.Join("|", keys)
                    });
                };
                if (kind == DeviceVisualKind.Keyboard)
                {
                    addDigital("WASD", new[] { "A", "D", "W", "S" });
                    addDigital("Arrows", new[] { "Left", "Right", "Up", "Down" });
                    addDigital("Numpad", new[] { "Numpad4", "Numpad6", "Numpad8", "Numpad2" });
                    foreach (var preset in profile.CompoundBindingPresets ??
                        new List<CompoundBindingPreset>())
                        if (preset.Capacity == 4)
                            addDigital(preset.Name, preset.Keys?.ToArray());
                }
                else
                {
                    string[] face = null;
                    if (kind == DeviceVisualKind.XboxOriginal || kind == DeviceVisualKind.Xbox360 ||
                        kind == DeviceVisualKind.XboxOne || kind == DeviceVisualKind.XboxSeries)
                        face = new[] { "X", "B", "Y", "A" };
                    else if (kind == DeviceVisualKind.PlayStation1 || kind == DeviceVisualKind.PlayStation2 ||
                        kind == DeviceVisualKind.PlayStation3 || kind == DeviceVisualKind.PlayStation4 ||
                        kind == DeviceVisualKind.PlayStation5)
                        face = new[] { "Square", "Circle", "Triangle", "Cross" };
                    else if (kind == DeviceVisualKind.SwitchPro || kind == DeviceVisualKind.SwitchJoyCon ||
                        kind == DeviceVisualKind.GameCube || kind == DeviceVisualKind.WiiClassic)
                        face = new[] { "Y", "A", "X", "B" };
                    if (face != null) addDigital("Face buttons", face);
                }
            }
            return result;
        }

        // Stored order matches the four input directions: Up, Down, Left, Right.
        private DeviceBindingInfo[] FindDPadControls(DeviceConfiguration device, DeviceIoType ioType)
        {
            var profile = ProfileViewModel.Profile;
            if (device?.Device == null) return null;
            var nodes = FlattenBindingNodes(device.Device.GetDeviceBindingMenu(profile.Context, ioType)).Where(node => node?.DeviceBindingInfo != null &&
                    node.DeviceBindingInfo.DeviceBindingCategory == DeviceBindingCategory.Momentary).ToList();
            var kind = DeviceVisualCatalog.Describe(device, profile, ioType).Kind;
            if (kind == DeviceVisualKind.Keyboard || kind == DeviceVisualKind.Mouse) return null;
            var dpad = nodes.Where(node =>
            {
                // The device's binding-menu title is authoritative for controls
                // explicitly named as a D-pad/POV/hat direction. Rendering metadata
                // may not be populated for a cached or temporarily disconnected HID,
                // so visual classification alone must not hide valid directions.
                var title = node.Title ?? string.Empty;
                if (Regex.IsMatch(title, @"(?:d[\s-]?pad|pov|hat)", RegexOptions.IgnoreCase) &&
                    Regex.IsMatch(title, @"(?:^|[\s,;:/_-])(Up|Down|Left|Right|North|South|West|East)$",
                        RegexOptions.IgnoreCase))
                    return true;
                var info = node.DeviceBindingInfo;
                var candidate = new DeviceBinding
                {
                    Profile = profile, DeviceIoType = ioType,
                    DeviceBindingCategory = DeviceBindingCategory.Momentary,
                    DeviceConfigurationGuid = device.Guid, IsBound = true,
                    KeyType = info.KeyType, KeyValue = info.KeyValue, KeySubValue = info.KeySubValue
                };
                return DeviceVisualCatalog.DescribeBinding(candidate,
                    DeviceBindingCategory.Momentary, profile).ControlKind == ControlVisualKind.DPad;
            }).ToList();
            // Some DirectInput/POV providers use arbitrary key values. Prefer
            // complete named four-direction sets from the same hat/D-pad instead
            // of assuming every provider numbers directions 0..3.
            var named = dpad.Select(node => new
            {
                Node = node,
                Direction = Regex.Match(node.Title ?? string.Empty,
                    @"(?:^|[\s,;:/_-])(Up|Down|Left|Right|North|South|West|East)$",
                    RegexOptions.IgnoreCase),
                Prefix = Regex.Replace(node.Title ?? string.Empty,
                    @"(?:[\s,;:/_-])?(?:Up|Down|Left|Right|North|South|West|East)$",
                    string.Empty, RegexOptions.IgnoreCase).Trim()
            }).Where(item => item.Direction.Success)
                .Select(item => new
                {
                    item.Node,
                    item.Prefix,
                    Direction = item.Direction.Groups[1].Value.ToLowerInvariant()
                }).GroupBy(item => item.Prefix, StringComparer.OrdinalIgnoreCase);
            foreach (var hat in named)
            {
                var aliases = new[]
                {
                    new[] { "up", "north" }, new[] { "down", "south" },
                    new[] { "left", "west" }, new[] { "right", "east" }
                };
                var four = aliases.Select(names => hat.FirstOrDefault(item =>
                    names.Contains(item.Direction))?.Node?.DeviceBindingInfo).ToArray();
                if (four.All(info => info != null)) return four;
            }

            // The original controller convention encodes Up, Right, Down, Left
            // as key values 0..3. Retain it for providers with generic labels.
            if (!Enumerable.Range(0, 4).All(index =>
                dpad.Any(node => node.DeviceBindingInfo.KeyValue == index)))
                return null;
            return new[] { 0, 2, 3, 1 }
                .Select(index => dpad.First(node =>
                    node.DeviceBindingInfo.KeyValue == index).DeviceBindingInfo)
                .ToArray();
        }

        public bool ApplyTypedFourWayOutput(FourWayOutputChoice choice)
        {
            if (!ButtonsEnabled || choice?.Controls == null ||
                choice.Controls.Length != (choice.IsDPad ? 4 : 2)) return false;
            var profile = ProfileViewModel.Profile;
            var device = profile.GetDeviceConfiguration(DeviceIoType.Output,
                choice.DeviceConfigurationGuid);
            if (device?.Device == null) return false;
            if (!IsCompoundOutput && !SetOutputRepresentation(1)) return false;

            var previousDigital = Mapping.CompoundDigitalOutput;
            var required = choice.IsDPad ? 4 : 2;
            Mapping.CompoundDigitalOutput = choice.IsDPad;
            if (Mapping.Plugins.Count != required && !SwitchDirectionalOutputs(1))
            {
                Mapping.CompoundDigitalOutput = previousDigital;
                return false;
            }

            // The runtime digital channel order is Left/Right/Up/Down.
            // Provider D-pad menus expose Up/Down/Left/Right.
            var order = choice.IsDPad ? new[] { 2, 3, 0, 1 } : new[] { 0, 1 };
            for (var i = 0; i < required; i++)
            {
                var plugin = Plugins[i];
                var output = plugin.DeviceBindings.FirstOrDefault(binding =>
                    binding.DeviceBinding.DeviceIoType == DeviceIoType.Output);
                if (output?.DeviceBinding == null) return false;
                var info = choice.Controls[order[i]];
                output.DeviceBinding.DeviceBindingCategory = info.DeviceBindingCategory;
                output.DeviceBinding.SetDeviceConfigurationGuid(device.Guid, false);
                output.DeviceBinding.SetKeyTypeValue(info.KeyType, info.KeyValue, info.KeySubValue);
                output.DeviceBinding.CompoundPresetName = choice.Destination;
                output.RefreshDeviceList();
            }

            profile.Context.ContextChanged();
            OnPropertyChanged(nameof(OutputDirectionalSlotCount));
            OnPropertyChanged(nameof(CompoundOutputSummary));
            OnPropertyChanged(nameof(FourWayOutputDeviceLabel));
            RefreshDirectionalAvailability();
            RefreshCollapsedSummary();
            return true;
        }

        public bool ApplyFourWayOutput(PluginViewModel axis, FourWayOutputChoice choice)
        {
            if (!ButtonsEnabled || axis?.IsFourButtonAxis != true || choice?.Controls == null ||
                Plugins.Count != 1 || Mapping.Plugins.Count != 1) return false;
            var destination = ProfileViewModel.Profile.GetDeviceConfiguration(
                DeviceIoType.Output, choice.DeviceConfigurationGuid);
            if (destination?.Device == null ||
                choice.Controls.Length != (choice.IsDPad ? 4 : 2)) return false;
            var typeName = choice.IsDPad ? "ButtonsToDPad" : "ButtonsToAxes";
            var type = axis.Plugin.GetType().Assembly.GetType(axis.Plugin.GetType().Namespace + "." + typeName);
            if (type == null || !typeof(Plugin).IsAssignableFrom(type)) return false;
            var replace = axis.Plugin.GetType() != type;
            var plugin = replace ? (Plugin)Activator.CreateInstance(type) : axis.Plugin;
            if (replace)
            {
                plugin.SetProfile(ProfileViewModel.Profile);
                plugin.Filters = axis.Plugin.Filters.ToList();
                foreach (var propertyName in new[] { "InvertX", "InvertY" })
                {
                    var source = axis.Plugin.GetType().GetProperty(propertyName);
                    var target = type.GetProperty(propertyName);
                    if (source != null && target != null)
                        target.SetValue(plugin, source.GetValue(axis.Plugin, null), null);
                }
            }
            if (plugin.Outputs.Count != choice.Controls.Length) return false;
            for (var i = 0; i < plugin.Outputs.Count; i++)
            {
                var info = choice.Controls[i];
                var binding = plugin.Outputs[i];
                binding.DeviceBindingCategory = plugin.OutputCategories[i].Category;
                binding.SetDeviceConfigurationGuid(destination.Guid, false);
                binding.SetKeyTypeValue(info.KeyType, info.KeyValue, info.KeySubValue);
            }
            if (replace)
            {
                foreach (var oldBinding in axis.DeviceBindings)
                    oldBinding.PropertyChanged -= SummaryBindingOnPropertyChanged;
                axis.Dispose();
                Mapping.Plugins[0] = plugin;
                var newAxis = new PluginViewModel(this, plugin);
                Plugins[0] = newAxis;
                foreach (var output in newAxis.DeviceBindings) SubscribeSummaryBinding(output);
            }
            else
            {
                foreach (var output in axis.DeviceBindings) output.RefreshDeviceList();
                axis.RefreshFourWayDestination();
            }
            ProfileViewModel.Profile.Context.ContextChanged();
            RefreshHeaderState();
            RefreshCollapsedSummary();
            return true;
        }

        public bool ApplyControllerDPadPreset(out string error)
        {
            error = null;
            if (!ButtonsEnabled || Mapping.Plugins.Count != 1 ||
                !(Plugins[0].IsFourButtonAxis) || Mapping.DeviceBindings.Count != 4)
            {
                error = "Select a four-way mapping first.";
                return false;
            }
            var profile = ProfileViewModel.Profile;
            var selectedDevice = GetSelectedPresetInputDevice();
            var selectedControls = FindDPadControls(selectedDevice, DeviceIoType.Input);
            if (selectedControls == null)
            {
                error = "Add a controller with a D-pad to this profile's input devices first.";
                return false;
            }
            for (var i = 0; i < 4; i++)
            {
                var info = selectedControls[i];
                var binding = Mapping.DeviceBindings[i];
                binding.DeviceBindingCategory = DeviceBindingCategory.Momentary;
                binding.SetDeviceConfigurationGuid(selectedDevice.Guid, false);
                binding.SetKeyTypeValue(info.KeyType, info.KeyValue, info.KeySubValue);
            }
            foreach (var binding in DeviceBindings) binding.RefreshDeviceList();
            RefreshCollapsedSummary();
            return true;
        }

        // Apply all four directions as a single transaction. Resolve every key
        // before changing a binding, so missing keys cannot partially apply.
        public bool ApplyKeyboardAxisPreset(string preset, out string error)
        {
            error = null;
            if (!ButtonsEnabled || Mapping.Plugins.Count != 1 ||
                !Plugins[0].IsFourButtonAxis || Mapping.DeviceBindings.Count != 4)
            {
                error = "Select a four-way joystick mapping first.";
                return false;
            }

            var presets = new Dictionary<string, string[]>
            {
                { "WASD", new[] { "W", "S", "A", "D" } },
                { "Arrows", new[] { "Up", "Down", "Left", "Right" } },
                { "Numpad", new[] { "Numpad8", "Numpad2", "Numpad4", "Numpad6" } },
                { "UJHK", new[] { "U", "J", "H", "K" } }
            };
            string[] names;
            if (!presets.TryGetValue(preset ?? string.Empty, out names)) return false;

            var profile = ProfileViewModel.Profile;
            // Respect the keyboard explicitly chosen on the first input, otherwise
            // the profile primary keyboard, before considering a fallback keyboard.
            var keyboard = SelectedPresetInputIsKeyboard ? GetSelectedPresetInputDevice() : null;
            if (keyboard == null)
            {
                error = "Add a keyboard to this profile's input devices first.";
                return false;
            }

            var keys = FlattenBindingNodes(keyboard.Device.GetDeviceBindingMenu(profile.Context, DeviceIoType.Input))
                .Where(node => node.DeviceBindingInfo != null &&
                    node.DeviceBindingInfo.DeviceBindingCategory == DeviceBindingCategory.Momentary)
                .ToList();
            var selected = new List<DeviceBindingNode>();
            foreach (var name in names)
            {
                var aliases = name.StartsWith("Numpad", StringComparison.OrdinalIgnoreCase)
                    ? new[] { name, name.Replace("Numpad", "Num"), name.Replace("Numpad", "Keypad") }
                    : new[] { name };
                var key = keys.FirstOrDefault(node => aliases.Any(alias =>
                    MatchesControlLeafName(node.Title, alias)));
                if (key == null)
                {
                    error = "The input keyboard does not expose " + name + ".";
                    return false;
                }
                selected.Add(key);
            }

            for (var i = 0; i < selected.Count; i++)
            {
                var binding = Mapping.DeviceBindings[i];
                var info = selected[i].DeviceBindingInfo;
                binding.DeviceBindingCategory = DeviceBindingCategory.Momentary;
                binding.SetDeviceConfigurationGuid(keyboard.Guid);
                binding.SetKeyTypeValue(info.KeyType, info.KeyValue, info.KeySubValue);
            }
            foreach (var binding in DeviceBindings) binding.RefreshDeviceList();
            RefreshCollapsedSummary();
            return true;
        }

        public static bool MatchesControlLeafName(string controlTitle, string name)
        {
            var normalized = NormalizeAxisPresetKey(name);
            if (NormalizeAxisPresetKey(controlTitle) == normalized) return true;
            // Keyboard arrow keys have several complete names depending on
            // the provider. Accept exact "Left Arrow" / "Arrow Left" tokens
            // without weakening the strict A-vs-Caps matching below.
            if (new[] { "UP", "DOWN", "LEFT", "RIGHT" }.Contains(normalized) &&
                (NormalizeAxisPresetKey(controlTitle) == normalized + "ARROW" ||
                 NormalizeAxisPresetKey(controlTitle) == "ARROW" + normalized))
                return true;
            // Providers may prefix controls with categories, e.g. "Buttons,A"
            // or "Face buttons: Cross". Compare only complete trailing tokens,
            // not arbitrary suffixes that would confuse A with another key.
            var parts = Regex.Split(controlTitle ?? string.Empty, @"[,;:/\\|>]+");
            return parts.Any(part => NormalizeAxisPresetKey(part) == normalized);
        }

        private static string NormalizeAxisPresetKey(string value)
        {
            return Regex.Replace(value ?? string.Empty, "[^A-Za-z0-9]", "").ToUpperInvariant();
        }

        // Explicitly replace a saved legacy mapping with a clean Button mapping.
    }
}
