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
        private static readonly Dictionary<string, string[]> BuiltInKeyboardPresets =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                { "WASD", new[] { "A", "D", "W", "S" } },
                { "Arrows", new[] { "Left", "Right", "Up", "Down" } },
                { "Numpad", new[] { "Numpad4", "Numpad6", "Numpad8", "Numpad2" } },
                { "UHJK", new[] { "H", "K", "U", "J" } },
                { "UJHK", new[] { "H", "K", "U", "J" } }
            };

        private IReadOnlyList<string> GetKeyboardPresetKeys(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            if (BuiltInKeyboardPresets.TryGetValue(name, out var keys)) return keys;
            return ProfileViewModel.Profile.CompoundBindingPresets?.FirstOrDefault(preset =>
                preset?.Capacity == 4 && string.Equals(preset.Name, name,
                    StringComparison.OrdinalIgnoreCase))?.Keys;
        }

        private static bool HasFourDistinctPresetKeys(IReadOnlyList<string> keys)
        {
            return keys?.Count == 4 && keys.All(key => !string.IsNullOrWhiteSpace(key)) &&
                keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 4;
        }

        private static DeviceBindingNode[] ResolveKeyboardPresetNodes(
            IEnumerable<DeviceBindingNode> nodes, IReadOnlyList<string> keys)
        {
            if (!HasFourDistinctPresetKeys(keys)) return null;
            var buttons = nodes.Where(node => node?.DeviceBindingInfo?.DeviceBindingCategory ==
                DeviceBindingCategory.Momentary).ToList();
            var matches = keys.Select(key => buttons.FirstOrDefault(node =>
                string.Equals(node.Title?.Trim(), key.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? buttons.FirstOrDefault(node => PresetControlMatches(node.Title, key))).ToArray();
            if (matches.Any(node => node == null)) return null;
            return matches.Select(node => new
            {
                node.DeviceBindingInfo.KeyType,
                node.DeviceBindingInfo.KeyValue,
                node.DeviceBindingInfo.KeySubValue
            }).Distinct().Count() == 4 ? matches : null;
        }

        // Query only: menu availability must reflect the physical controls in
        // configured devices rather than showing preset choices that cannot bind.
        public bool CanApplyCompoundInputPreset(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            var profile = ProfileViewModel.Profile;
            var keys = GetKeyboardPresetKeys(name);
            foreach (var device in profile.GetDeviceConfigurationList(DeviceIoType.Input))
            {
                if (device?.Device == null) continue;
                if (name == "Controller D-pad" && FindDPadControls(device, DeviceIoType.Input) != null)
                    return true;
                var kind = DeviceVisualCatalog.Describe(device, profile, DeviceIoType.Input).Kind;
                var nodes = FlattenBindingNodes(device.Device.GetDeviceBindingMenu(
                    profile.Context, DeviceIoType.Input))
                    .Where(node => node?.DeviceBindingInfo != null).ToList();
                if (keys != null)
                {
                    if (kind == DeviceVisualKind.Keyboard &&
                        ResolveKeyboardPresetNodes(nodes, keys) != null) return true;
                    continue;
                }
                if (name == "Left stick" || name == "Right stick")
                {
                    if (kind == DeviceVisualKind.Keyboard || kind == DeviceVisualKind.Mouse ||
                        kind == DeviceVisualKind.Unknown || kind == DeviceVisualKind.Unavailable) continue;
                    var index = name == "Left stick" ? 0 : 2;
                    if (FindStickControls(device.Device, nodes, index) != null)
                        return true;
                }
                else if (name == "Face buttons")
                {
                    string[] face = null;
                    if (kind == DeviceVisualKind.XboxOriginal || kind == DeviceVisualKind.Xbox360 ||
                        kind == DeviceVisualKind.XboxOne || kind == DeviceVisualKind.XboxSeries)
                        face = new[] { "X", "B", "Y", "A" };
                    else if (kind == DeviceVisualKind.PlayStation1 ||
                             kind == DeviceVisualKind.PlayStation2 ||
                             kind == DeviceVisualKind.PlayStation3 ||
                             kind == DeviceVisualKind.PlayStation4 ||
                             kind == DeviceVisualKind.PlayStation5)
                        face = new[] { "Square", "Circle", "Triangle", "Cross" };
                    else if (kind == DeviceVisualKind.SwitchPro || kind == DeviceVisualKind.SwitchJoyCon ||
                             kind == DeviceVisualKind.WiiClassic || kind == DeviceVisualKind.GameCube)
                        face = new[] { "Y", "A", "X", "B" };
                    if (face != null && face.All(label => nodes.Any(node =>
                        node.DeviceBindingInfo.DeviceBindingCategory ==
                            DeviceBindingCategory.Momentary &&
                        MatchesControlLeafName(node.Title, label)))) return true;
                }
            }
            return false;
        }

        public bool ApplyTypedControllerStickPreset(int axisIndex, out string error,
            int groupIndex = 0)
        {
            error = null;
            if (!ButtonsEnabled || (axisIndex != 0 && axisIndex != 2))
            {
                error = "Stop the profile before editing a stick preset.";
                return false;
            }
            var original = Mapping.DeviceBindings.FirstOrDefault(binding =>
                binding.InputExpressionGroup == groupIndex &&
                !binding.IsDirectionalAndCondition);
            if (original == null)
            {
                error = "This input OR set no longer exists.";
                return false;
            }
            var profile = ProfileViewModel.Profile;
            var preferred = original.DeviceConfigurationGuid;
            var primary = profile.GetPrimaryDeviceConfiguration(DeviceIoType.Input)?.Guid;
            var candidates = new List<Tuple<DeviceConfiguration, DeviceBindingInfo[]>>();
            foreach (var device in profile.GetDeviceConfigurationList(DeviceIoType.Input))
            {
                if (device?.Device == null) continue;
                var kind = DeviceVisualCatalog.Describe(device, profile, DeviceIoType.Input).Kind;
                if (kind == DeviceVisualKind.Keyboard || kind == DeviceVisualKind.Mouse ||
                    kind == DeviceVisualKind.Unavailable || kind == DeviceVisualKind.Unknown) continue;
                var axes = FindStickControls(device.Device, FlattenBindingNodes(
                    device.Device.GetDeviceBindingMenu(profile.Context, DeviceIoType.Input)), axisIndex);
                if (axes != null) candidates.Add(Tuple.Create(device, axes));
            }
            var chosen = candidates.FirstOrDefault(item => item.Item1.Guid == preferred)
                ?? candidates.FirstOrDefault(item => item.Item1.Guid == primary)
                ?? candidates.FirstOrDefault();
            if (chosen == null)
            {
                error = "No configured controller exposes this stick's two axes.";
                return false;
            }
            if (!EnsureFourWayGroupSlots(groupIndex, false, out error)) return false;
            var group = DeviceBindings.Where(binding =>
                binding.DeviceBinding.InputExpressionGroup == groupIndex &&
                !binding.DeviceBinding.IsDirectionalAndCondition).ToList();
            if (group.Count != 2)
            {
                error = "This OR set does not contain both stick axes.";
                return false;
            }
            for (var i = 0; i < 2; i++)
            {
                var binding = group[i].DeviceBinding;
                var info = chosen.Item2[i];
                binding.AssignPhysicalControl(chosen.Item1.Guid, DeviceBindingCategory.Range,
                    info.KeyType, info.KeyValue, info.KeySubValue, null, inputKind: 3);
                binding.CompoundPresetName = axisIndex == 0 ? "Left stick" : "Right stick";
                group[i].RefreshDeviceList();
            }
            RefreshInputExpressionPresentation();
            RefreshCollapsedSummary();
            RefreshDirectionalAvailability();
            return true;
        }

        public bool ApplyTypedControllerFaceButtons(out string error, int groupIndex = 0)
        {
            error = null;
            if (!ButtonsEnabled) return false;
            var source = Mapping.DeviceBindings.FirstOrDefault(input =>
                input.InputExpressionGroup == groupIndex &&
                !input.IsDirectionalAndCondition);
            if (source == null)
            {
                error = "This input OR set no longer exists.";
                return false;
            }
            var profile = ProfileViewModel.Profile;
            var preferred = source.DeviceConfigurationGuid;
            var primary = profile.GetPrimaryDeviceConfiguration(DeviceIoType.Input)?.Guid;
            var candidates = new List<Tuple<DeviceConfiguration, DeviceBindingInfo[]>>();
            foreach (var device in profile.GetDeviceConfigurationList(DeviceIoType.Input))
            {
                if (device?.Device == null) continue;
                var kind = DeviceVisualCatalog.Describe(device, profile, DeviceIoType.Input).Kind;
                string[] names;
                if (kind == DeviceVisualKind.SwitchPro || kind == DeviceVisualKind.SwitchJoyCon ||
                    kind == DeviceVisualKind.WiiClassic || kind == DeviceVisualKind.GameCube)
                    names = new[] { "Y", "A", "X", "B" };
                else if (kind == DeviceVisualKind.PlayStation1 ||
                         kind == DeviceVisualKind.PlayStation2 ||
                         kind == DeviceVisualKind.PlayStation3 ||
                         kind == DeviceVisualKind.PlayStation4 ||
                         kind == DeviceVisualKind.PlayStation5)
                    names = new[] { "Square", "Circle", "Triangle", "Cross" };
                else if (kind == DeviceVisualKind.XboxOriginal ||
                         kind == DeviceVisualKind.Xbox360 ||
                         kind == DeviceVisualKind.XboxOne ||
                         kind == DeviceVisualKind.XboxSeries)
                    names = new[] { "X", "B", "Y", "A" };
                else continue;
                var controls = FlattenBindingNodes(device.Device.GetDeviceBindingMenu(profile.Context,
                    DeviceIoType.Input)).Where(node =>
                        node?.DeviceBindingInfo?.DeviceBindingCategory ==
                        DeviceBindingCategory.Momentary).ToList();
                var matched = names.Select(name => controls.FirstOrDefault(node =>
                    MatchesControlLeafName(node.Title, name))).ToArray();
                if (matched.Any(node => node == null)) continue;
                candidates.Add(Tuple.Create(device, matched
                    .Select(node => node.DeviceBindingInfo).ToArray()));
            }
            var chosen = candidates.FirstOrDefault(item => item.Item1.Guid == preferred)
                ?? candidates.FirstOrDefault(item => item.Item1.Guid == primary)
                ?? candidates.FirstOrDefault();
            if (chosen == null)
            {
                error = "No configured controller exposes a recognisable four-button face layout.";
                return false;
            }
            // Do not replace any binding when the controller has no recognised
            // face-button layout.
            if (!EnsureFourWayGroupSlots(groupIndex, true, out error)) return false;
            var group = DeviceBindings.Where(input =>
                input.DeviceBinding.InputExpressionGroup == groupIndex &&
                !input.DeviceBinding.IsDirectionalAndCondition)
                .OrderBy(input => input.DeviceBinding.InputDirectionSlot).ToList();
            if (group.Count != 4)
            {
                error = "This 4-Way set does not contain four directions.";
                return false;
            }
            for (var i = 0; i < 4; i++)
            {
                var binding = group[i].DeviceBinding;
                var info = chosen.Item2[i];
                binding.AssignPhysicalControl(chosen.Item1.Guid, DeviceBindingCategory.Momentary,
                    info.KeyType, info.KeyValue, info.KeySubValue, null, inputKind: 0);
                binding.CompoundPresetName = "Face buttons";
                group[i].RefreshDeviceList();
            }
            RefreshInputExpressionPresentation();
            RefreshCollapsedSummary();
            RefreshDirectionalAvailability();
            return true;
        }

        public bool ApplyDirectionalDPadPreset(out string error, int groupIndex = 0)
        {
            error = null;
            if (!ButtonsEnabled)
            {
                error = "Stop the running profile before selecting a D-pad preset.";
                return false;
            }
            var original = Mapping.DeviceBindings.FirstOrDefault(binding =>
                binding.InputExpressionGroup == groupIndex &&
                !binding.IsDirectionalAndCondition);
            if (original == null)
            {
                error = "This input OR set no longer exists.";
                return false;
            }
            var profile = ProfileViewModel.Profile;
            var selectedGuid = original.DeviceConfigurationGuid;
            var primaryGuid = profile.GetPrimaryDeviceConfiguration(DeviceIoType.Input)?.Guid;
            var candidates = profile.GetDeviceConfigurationList(DeviceIoType.Input)
                .Select(config => new { Device = config, Controls = FindDPadControls(config, DeviceIoType.Input) })
                .Where(candidate => candidate.Controls != null).ToList();
            // Choosing a D-pad preset explicitly selects a D-pad-capable device.
            // Prefer the currently selected controller, then the profile primary,
            // then the first configured compatible controller.
            var choice = candidates.FirstOrDefault(candidate => candidate.Device.Guid == selectedGuid)
                ?? candidates.FirstOrDefault(candidate => candidate.Device.Guid == primaryGuid)
                ?? candidates.FirstOrDefault();
            var device = choice?.Device;
            var dpad = choice?.Controls;
            if (device == null || dpad == null)
            {
                error = "Select a controller with a D-pad in the shared device picker, or add one to this profile.";
                return false;
            }

            // Never convert existing bindings until the requested D-pad exists.
            if (!EnsureFourWayGroupSlots(groupIndex, true, out error)) return false;
            var inputs = DeviceBindings.Where(input =>
                input.DeviceBinding.InputExpressionGroup == groupIndex &&
                !input.DeviceBinding.IsDirectionalAndCondition)
                .OrderBy(input => input.DeviceBinding.InputDirectionSlot).ToList();
            if (inputs.Count != 4)
            {
                error = "The 4-Way binding does not contain four directions.";
                return false;
            }
            // Provider controls are ordered Up, Down, Left, Right. Logical unit
            // slots are Left, Right, Up, Down.
            var order = new[] { 2, 3, 0, 1 };
            for (var i = 0; i < 4; i++)
            {
                var binding = inputs[i].DeviceBinding;
                var info = dpad[order[i]];
                binding.AssignPhysicalControl(device.Guid, DeviceBindingCategory.Momentary,
                    info.KeyType, info.KeyValue, info.KeySubValue, null, inputKind: 0);
                binding.CompoundPresetName = "D-pad";
            }
            foreach (var input in inputs) input.RefreshDeviceList();
            RefreshInputExpressionPresentation();
            RefreshCollapsedSummary();
            RefreshDirectionalAvailability();
            return true;
        }

        public bool UseAnalogueCompoundInput(out string error, int groupIndex = 0)
        {
            error = null;
            if (!ButtonsEnabled || !IsCompoundInput)
            {
                error = "Choose 4-Way or Axis input first.";
                return false;
            }
            if (Mapping.UsesFourWayGroupShapes)
                return EnsureFourWayGroupSlots(groupIndex, false, out error);
            if (!Mapping.CompoundDigitalInput) return true;
            Mapping.CompoundDigitalInput = false;
            if (!SwitchDirectionalInputs(Mapping.EffectiveInputRepresentation))
            {
                Mapping.CompoundDigitalInput = true;
                error = "Unable to restore analogue input slots.";
                return false;
            }
            ProfileViewModel.Profile.Context.ContextChanged();
            RefreshInputExpressionPresentation();
            OnPropertyChanged(nameof(InputDirectionalSlotCount));
            OnPropertyChanged(nameof(CompoundBindingSummary));
            OnPropertyChanged(nameof(IsDigitalCompoundInput));
            OnPropertyChanged(nameof(CompoundBlockEnabled));
            OnPropertyChanged(nameof(CanBlockCompound));
            return true;
        }

        private bool EnsureFourWayGroupSlots(int groupIndex, bool digital, out string error)
        {
            error = null;
            if (!ButtonsEnabled)
            {
                error = "Stop the running profile to change bindings.";
                return false;
            }
            if (!Mapping.DeviceBindings.Any(binding =>
                binding.InputExpressionGroup == groupIndex && !binding.IsDirectionalAndCondition))
            {
                error = "This input OR set no longer exists.";
                return false;
            }
            if ((!Mapping.UsesFourWayGroupShapes || Mapping.InputRepresentation != 1) &&
                !SetInputRepresentation(1))
            {
                error = "Unable to prepare a 4-Way input.";
                return false;
            }

            var inputs = Mapping.DeviceBindings.Where(binding =>
                binding.InputExpressionGroup == groupIndex && !binding.IsDirectionalAndCondition).ToList();
            var required = digital ? 4 : 2;
            if (inputs.Count == required)
            {
                if (!Mapping.UseInputExpression)
                {
                    Mapping.UseInputExpression = true;
                    ProfileViewModel.Profile.Context.ContextChanged();
                    RebuildDirectionalEditors();
                }
                return true;
            }
            if (inputs.Count == 0)
            {
                error = "Unable to locate the 4-Way input.";
                return false;
            }

            var banks = Mapping.InactiveInputRepresentations ??
                (Mapping.InactiveInputRepresentations = new List<DirectionalInputBank>());
            var saved = banks.LastOrDefault(bank => bank.GroupIndex == groupIndex &&
                bank.Representation == 1 && bank.Bindings?.Count == required);
            var replacement = saved?.Bindings ?? Enumerable.Range(0, required).Select(index =>
                new DeviceBinding(Mapping.Update, ProfileViewModel.Profile, DeviceIoType.Input)
                {
                    DeviceBindingCategory = digital ? DeviceBindingCategory.Momentary : DeviceBindingCategory.Range,
                    InputExpressionInputKind = digital ? 0 : 3,
                    InputExpressionKindConfigured = true,
                    InputExpressionGroup = groupIndex,
                    InputDirectionSlot = digital ? index + 1 : 0
                }).ToList();
            var first = Mapping.DeviceBindings.IndexOf(inputs[0]);
            var next = Mapping.DeviceBindings.Where(binding => !inputs.Contains(binding)).ToList();
            next.InsertRange(first, replacement);
            if (saved != null) banks.Remove(saved);
            if (Mapping.IsFourWayGroupSlotCount(inputs.Count))
            {
                banks.RemoveAll(bank => bank.GroupIndex == groupIndex &&
                    bank.Representation == 1 && bank.Bindings?.Count == inputs.Count);
                banks.Add(new DirectionalInputBank { Representation = 1, GroupIndex = groupIndex, Bindings = inputs });
            }
            Mapping.DeviceBindings = next;
            Mapping.UseInputExpression = true;
            ProfileViewModel.Profile.Context.ContextChanged();
            RebuildDirectionalEditors();
            return true;
        }

        public bool ApplyDirectionalKeyboardPreset(string preset, out string error,
            int groupIndex = 0)
        {
            error = null;
            if (!ButtonsEnabled) return false;
            var source = Mapping.DeviceBindings.FirstOrDefault(binding =>
                binding.InputExpressionGroup == groupIndex &&
                !binding.IsDirectionalAndCondition);
            if (source == null)
            {
                error = "This input OR set no longer exists.";
                return false;
            }
            var profile = ProfileViewModel.Profile;
            var keys = GetKeyboardPresetKeys(preset);
            if (keys == null)
            {
                error = "Preset not found.";
                return false;
            }
            if (!HasFourDistinctPresetKeys(keys))
            {
                error = "A 4-Way preset must contain four distinct directional keys.";
                return false;
            }
            var currentGuid = source.DeviceConfigurationGuid;
            var primaryGuid = profile.GetPrimaryDeviceConfiguration(DeviceIoType.Input)?.Guid;
            // A preset is offered when any configured keyboard has the complete
            // set. Applying it must use that same criterion, not blindly choose
            // the first keyboard (which may expose only some keys).
            var compatible = profile.GetDeviceConfigurationList(DeviceIoType.Input)
                .Where(config => config?.Device != null &&
                    DeviceVisualCatalog.Describe(config, profile, DeviceIoType.Input).Kind ==
                        DeviceVisualKind.Keyboard)
                .Select(config =>
                {
                    var available = FlattenBindingNodes(config.Device.GetDeviceBindingMenu(
                        profile.Context, DeviceIoType.Input));
                    return new { Configuration = config, Selections = ResolveKeyboardPresetNodes(available, keys) };
                })
                .Where(candidate => candidate.Selections != null)
                .ToList();
            var selected = compatible.FirstOrDefault(candidate =>
                    candidate.Configuration.Guid == currentGuid)
                ?? compatible.FirstOrDefault(candidate =>
                    candidate.Configuration.Guid == primaryGuid)
                ?? compatible.FirstOrDefault();
            if (selected == null)
            {
                error = "No configured keyboard exposes all four keys for " + preset + ".";
                return false;
            }
            var keyboard = selected.Configuration;
            var selections = selected.Selections;

            // Discovery must complete before any topology change. A missing
            // keyboard/key must leave the mapping and its bindings untouched.
            if (!EnsureFourWayGroupSlots(groupIndex, true, out error)) return false;
            var inputs = DeviceBindings.Where(binding =>
                binding.DeviceBinding.InputExpressionGroup == groupIndex &&
                !binding.DeviceBinding.IsDirectionalAndCondition)
                .OrderBy(binding => binding.DeviceBinding.InputDirectionSlot).ToList();
            if (inputs.Count != 4)
            {
                error = "Unable to resolve the four compound directions.";
                return false;
            }
            for (var i = 0; i < 4; i++)
            {
                var input = inputs[i].DeviceBinding;
                var info = selections[i].DeviceBindingInfo;
                input.AssignPhysicalControl(keyboard.Guid, DeviceBindingCategory.Momentary,
                    info.KeyType, info.KeyValue, info.KeySubValue, selections[i].Title, inputKind: 0);
                input.CompoundPresetName = preset;
            }
            foreach (var input in inputs) input.RefreshDeviceList();
            RefreshInputExpressionPresentation();
            RefreshCollapsedSummary();
            return true;
        }

        public bool SaveCompoundKeyboardPreset(string name, out string error,
            int groupIndex = 0)
        {
            error = null;
            name = (name ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                error = "Enter a preset name.";
                return false;
            }
            if (BuiltInKeyboardPresets.ContainsKey(name))
            {
                error = "That name is reserved for a built-in preset.";
                return false;
            }
            var inputs = DeviceBindings.Where(input =>
                    input.DeviceBinding.InputExpressionGroup == groupIndex &&
                !input.DeviceBinding.IsDirectionalAndCondition)
                .OrderBy(input => input.DeviceBinding.InputDirectionSlot)
                .Select(input => input.DeviceBinding).ToList();
            if (!Mapping.UsesFourWayGroupShapes ||
                inputs.Count != 4 || inputs.Any(input => !input.IsBound))
            {
                error = "Bind all compound directions before saving a preset.";
                return false;
            }
            var profile = ProfileViewModel.Profile;
            var keyboard = profile.GetDeviceConfiguration(DeviceIoType.Input,
                inputs[0].DeviceConfigurationGuid);
            if (keyboard?.Device == null ||
                DeviceVisualCatalog.Describe(keyboard.Device, DeviceIoType.Input).Kind != DeviceVisualKind.Keyboard ||
                inputs.Any(input => input.DeviceConfigurationGuid != keyboard.Guid))
            {
                error = "A keyboard preset requires all directions on the same keyboard.";
                return false;
            }
            var controls = FlattenBindingNodes(keyboard.Device.GetDeviceBindingMenu(profile.Context, DeviceIoType.Input)).Where(node => node.DeviceBindingInfo != null).ToList();
            var names = new List<string>();
            foreach (var input in inputs)
            {
                var matching = controls.FirstOrDefault(node =>
                    node.DeviceBindingInfo.KeyType == input.KeyType &&
                    node.DeviceBindingInfo.KeyValue == input.KeyValue &&
                    node.DeviceBindingInfo.KeySubValue == input.KeySubValue);
                if (matching == null)
                {
                    error = "Could not identify every assigned keyboard key.";
                    return false;
                }
                names.Add(matching.Title);
            }
            if (ResolveKeyboardPresetNodes(controls, names) == null)
            {
                error = "A 4-Way preset must contain four distinct directional keys.";
                return false;
            }
            if (profile.CompoundBindingPresets == null)
                profile.CompoundBindingPresets = new List<CompoundBindingPreset>();
            var existing = profile.CompoundBindingPresets.FirstOrDefault(item =>
                item.Capacity == DirectionalCapacity &&
                string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                existing = new CompoundBindingPreset { Name = name, Capacity = DirectionalCapacity };
                profile.CompoundBindingPresets.Add(existing);
            }
            existing.Keys = names;
            profile.Context.ContextChanged();
            ProfileViewModel.RefreshCompoundPresetItems();
            RefreshInputExpressionPresentation();
            RefreshCollapsedSummary();
            return true;
        }

        public void SetCompoundBlockEnabled(bool blocked)
        {
            if (!CanBlockCompound) return;
            foreach (var binding in Mapping.DeviceBindings)
                binding.SetBlock(blocked && binding.IsBound && binding.IsBlockable());
            OnPropertyChanged(nameof(CompoundBlockEnabled));
            RefreshCollapsedSummary();
        }

        public void SetDirectionalBlocked(DeviceBindingViewModel source, bool blocked)
        {
            if (source?.DeviceBinding == null || !ButtonsEnabled) return;
            foreach (var input in Mapping.DeviceBindings.Where(input =>
                input.InputDirectionSlot > 0 &&
                input.InputExpressionGroup == source.DeviceBinding.InputExpressionGroup))
                input.SetBlock(blocked && input.IsBound && input.IsBlockable());
        }

        public void AddAndInput()
        {
            var condition = InputConditions?.LastOrDefault();
            AddInputToCondition(condition);
        }

        public void AddOrInput()
        {
            AddCondition();
        }

        public void AddInputToCondition(InputConditionViewModel condition)
        {
            if (!ButtonsEnabled || !SupportsNativeInputExpression) return;
            var group = condition?.GroupIndex ?? 0;
            var binding = Mapping.AddExpressionInputToGroup(group);
            if (IsTypedDirectionalMapping && binding != null)
                binding.IsDirectionalAndCondition = true;
            AddExpressionBindingViewModel(binding);
        }

        // Device selection belongs to each OR set, including compound controls.
        // Changing it changes only that set; the preset or physical binding can
        // subsequently establish the representation without a device-type lock.
        public bool SetDirectionalSlotDevice(DeviceBindingViewModel input, Guid deviceGuid)
        {
            if (!ButtonsEnabled || input?.DeviceBinding == null ||
                !DeviceBindings.Contains(input)) return false;
            var profile = ProfileViewModel.Profile;
            if (deviceGuid != Guid.Empty &&
                profile.GetDeviceConfiguration(DeviceIoType.Input, deviceGuid) == null)
                return false;
            input.DeviceBinding.SetDeviceConfigurationGuid(deviceGuid, false);
            input.RefreshDeviceList();
            profile.Context.ContextChanged();
            RefreshInputExpressionPresentation();
            RefreshCollapsedSummary();
            return true;
        }

        public bool SetCompoundInputDevice(int groupIndex, Guid deviceGuid)
        {
            if (!ButtonsEnabled) return false;
            var profile = ProfileViewModel.Profile;
            if (deviceGuid != Guid.Empty &&
                profile.GetDeviceConfiguration(DeviceIoType.Input, deviceGuid) == null)
                return false;
            var inputs = DeviceBindings.Where(item => item.DeviceBinding != null &&
                item.DeviceBinding.InputExpressionGroup == groupIndex &&
                !item.DeviceBinding.IsDirectionalAndCondition).ToList();
            if (inputs.Count == 0) return false;
            foreach (var input in inputs)
            {
                // A new device does not guarantee that the old physical key is
                // meaningful. Keep representation banks, but clear that key.
                input.DeviceBinding.SetDeviceConfigurationGuid(deviceGuid, false);
                input.RefreshDeviceList();
            }
            ProfileViewModel.Profile.Context.ContextChanged();
            RefreshInputExpressionPresentation();
            RefreshCollapsedSummary();
            return true;
        }

        public void AddCondition()
        {
            if (!ButtonsEnabled || !SupportsNativeInputExpression) return;
            if (IsTypedDirectionalMapping)
            {
                var group = Mapping.DeviceBindings.Count == 0 ? 0 :
                    Mapping.DeviceBindings.Max(input => Math.Max(0, input.InputExpressionGroup)) + 1;
                var count = Mapping.RequiredInputBindingSlotsPerGroup;
                var capacity = DirectionalCapacity;
                var isButton = Mapping.EffectiveInputRepresentation == 3 ||
                    Mapping.CompoundDigitalInput;
                if (Mapping.UsesFourWayGroupShapes)
                {
                    var firstGroup = Mapping.DeviceBindings.Where(input => !input.IsDirectionalAndCondition)
                        .GroupBy(input => input.InputExpressionGroup).OrderBy(set => set.Key).FirstOrDefault();
                    var firstCount = firstGroup?.Count() ?? 0;
                    if (Mapping.IsFourWayGroupSlotCount(firstCount)) count = firstCount;
                    isButton = count == 4;
                }
                if (count < 1) return;
                for (var index = 0; index < count; index++)
                {
                    var binding = new DeviceBinding(Mapping.Update, ProfileViewModel.Profile,
                        DeviceIoType.Input)
                    {
                        DeviceBindingCategory = isButton
                            ? DeviceBindingCategory.Momentary : DeviceBindingCategory.Range,
                        InputExpressionInputKind = isButton ? 0 : 3,
                        InputExpressionKindConfigured = true,
                        InputExpressionGroup = group,
                        InputDirectionSlot = (isButton ||
                            (Mapping.EffectiveInputRepresentation == 4 && capacity == 4)) &&
                            capacity > 1 ? index + 1 : 0
                    };
                    Mapping.DeviceBindings.Add(binding);
                }
                Mapping.UseInputExpression = true;
                ProfileViewModel.Profile.Context.ContextChanged();
                RebuildDirectionalEditors();
                return;
            }
            var added = Mapping.AddExpressionCondition();
            AddExpressionBindingViewModel(added);
        }

        private void AddExpressionBindingViewModel(DeviceBinding binding)
        {
            if (binding == null) return;

            var viewModel = new DeviceBindingViewModel(binding)
            {
                DeviceBindingName = binding.IsDirectionalAndCondition ? "AND" : "Input",
                ExpressionMapping = Mapping,
                DeviceBindingCategory = binding.InputExpressionInputKind == 0 ? DeviceBindingCategory.Momentary : DeviceBindingCategory.Range
            };
            var modelIndex = Mapping.DeviceBindings.IndexOf(binding);
            if (modelIndex >= 0 && modelIndex <= DeviceBindings.Count)
                DeviceBindings.Insert(modelIndex, viewModel);
            else
                DeviceBindings.Add(viewModel);
            SubscribeSummaryBinding(viewModel);
            RefreshInputExpressionPresentation();
            RefreshCollapsedSummary();
        }

        public void RemoveExpressionInput(DeviceBindingViewModel bindingViewModel)
        {
            if (!ButtonsEnabled || bindingViewModel?.DeviceBinding == null) return;
            if (!Mapping.RemoveExpressionInput(bindingViewModel.DeviceBinding)) return;

            RebuildDirectionalEditors();
        }

        public void RemoveCondition(InputConditionViewModel condition)
        {
            if (!ButtonsEnabled || condition == null || !condition.CanRemoveCondition) return;
            var removed = condition.Inputs.ToList();
            if (!Mapping.RemoveExpressionGroup(condition.GroupIndex)) return;

            foreach (var binding in removed)
            {
                binding.Dispose();
                DeviceBindings.Remove(binding);
            }
            RefreshInputExpressionPresentation();
            RefreshCollapsedSummary();
        }

        public void SetInputNegated(DeviceBindingViewModel bindingViewModel, bool negated)
        {
            if (!ButtonsEnabled || bindingViewModel?.DeviceBinding == null) return;
            if (IsTypedDirectionalMapping)
                bindingViewModel.DeviceBinding.SetInputExpressionNegated(negated);
            else if (!Mapping.SetExpressionNegated(bindingViewModel.DeviceBinding, negated)) return;
            RefreshInputExpressionPresentation();
            RefreshCollapsedSummary();
        }

        private void RefreshInputExpressionPresentation()
        {
            var canRemove = UsesInputExpression && DeviceBindings.Count > 1;
            var directionalIndex = 0;
            for (var i = 0; i < DeviceBindings.Count; i++)
            {
                DeviceBindings[i].DeviceBindingName = IsTypedDirectionalMapping
                    ? (DeviceBindings[i].DeviceBinding.IsDirectionalAndCondition
                        ? "AND" : GetDirectionalInputSlotLabel(directionalIndex++, DeviceBindings[i].DeviceBinding))
                    : SupportsNativeInputExpression
                        ? "Input"
                        : (Mapping.Plugins.Count > 0 && i < Mapping.Plugins[0].InputCategories.Count
                            ? Mapping.Plugins[0].InputCategories[i].Name
                            : "Input " + (i + 1));
                DeviceBindings[i].InputExpressionControlsVisibility = Visibility.Collapsed;
                DeviceBindings[i].CanRemoveExpressionInput = canRemove;
            }

            RebuildInputConditions();
            OnPropertyChanged(nameof(FourWayInputGroup));
            OnPropertyChanged(nameof(SupportsNativeInputExpression));
            OnPropertyChanged(nameof(UsesInputExpression));
            OnPropertyChanged(nameof(InputLogicSummary));
            OnPropertyChanged(nameof(InputConditions));
            OnPropertyChanged(nameof(HasMultipleInputConditions));
            OnPropertyChanged(nameof(SharedAddInputVisibility));
            // Binding changes also affect the singular/plural header, even during initial load.
            _mappingRouteTokens = BuildMappingRouteTokens(MappingRouteDisplay);
            OnPropertyChanged(nameof(MappingInputTypeLabel));
            OnPropertyChanged(nameof(MappingRouteDisplay));
            OnPropertyChanged(nameof(MappingRouteTokens));
        }

        private void RebuildInputConditions()
        {
            if (InputConditions == null) InputConditions = new ObservableCollection<InputConditionViewModel>();
            InputConditions.Clear();
            if (!SupportsNativeInputExpression || DeviceBindings == null || DeviceBindings.Count == 0) return;

            if (!UsesInputExpression)
            {
                InputConditions.Add(new InputConditionViewModel(0, DeviceBindings.Take(1), false,
                    owner: this));
                return;
            }

            var groups = DeviceBindings
                .Where(binding => binding?.DeviceBinding != null)
                .GroupBy(binding => Math.Max(0, binding.DeviceBinding.InputExpressionGroup))
                .OrderBy(group => group.Key)
                .ToList();
            var canRemoveCondition = groups.Count > 1;
            foreach (var group in groups)
            {
                InputConditions.Add(new InputConditionViewModel(group.Key, group, canRemoveCondition,
                    owner: this));
            }
        }

        private string BuildInputLogicSummary()
        {
            if (!UsesInputExpression) return "Single input";
            if (Mapping?.DeviceBindings == null || Mapping.DeviceBindings.Count == 0) return "No inputs";

            return string.Join("  OR  ", Mapping.DeviceBindings.Select((binding, index) => new
                {
                    Binding = binding,
                    Index = index
                })
                .GroupBy(item => Math.Max(0, item.Binding.InputExpressionGroup))
                .OrderBy(group => group.Key)
                .Select(group => string.Join(" + ", group.Select(item =>
                    (item.Binding.InputExpressionNegated ? "NOT " : string.Empty) + "Input " + (item.Index + 1)))));
        }
    }
}
