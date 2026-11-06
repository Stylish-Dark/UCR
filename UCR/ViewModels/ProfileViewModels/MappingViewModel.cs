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
using HidWizards.UCR.Utilities;
using MaterialDesignThemes.Wpf;

namespace HidWizards.UCR.ViewModels.ProfileViewModels
{
    public sealed class MappingHeaderToken
    {
        public string Text { get; }
        public string TypeLabel { get; }

        public MappingHeaderToken(string text, string typeLabel)
        {
            Text = text;
            TypeLabel = typeLabel;
        }
    }

    public sealed class FilterReferenceBadge
    {
        public string Name { get; set; }
        public string ToolTip { get; set; }
    }

    public sealed class FourWayOutputDeviceOption
    {
        public Guid Guid { get; set; }
        public string Title { get; set; }
    }

    public sealed class FourWayOutputChoice
    {
        public Guid DeviceConfigurationGuid { get; set; }
        public string DeviceTitle { get; set; }
        public string Destination { get; set; }
        public string VisualKeys { get; set; }
        public bool IsDPad { get; set; }
        // Input/output key descriptors are resolved before any binding is changed.
        public DeviceBindingInfo[] Controls { get; set; }
    }

    public sealed class QuickBindingOption
    {
        public string Title { get; set; }
        public Guid DeviceConfigurationGuid { get; set; }
        public int KeyType { get; set; }
        public int KeyValue { get; set; }
        public int KeySubValue { get; set; }
        public BindingVisualDescriptor Visual { get; set; }
    }

    // One visual direction in the compass. Analogue sticks deliberately
    // share their X binding between Left/Right and Y between Up/Down.
    // Four-key D-pads instead reference four independent physical bindings.
    public sealed class DirectionalCompassSlotViewModel
    {
        public string Arrow { get; }
        public DeviceBindingViewModel PhysicalBinding { get; }

        public DirectionalCompassSlotViewModel(string arrow, DeviceBindingViewModel physicalBinding)
        {
            Arrow = arrow;
            PhysicalBinding = physicalBinding;
        }
    }

    public sealed class InputConditionViewModel : INotifyPropertyChanged
    {
        private readonly MappingViewModel _owner;
        public bool IsTypedDirectional => _owner?.IsTypedDirectionalMapping == true;
        public bool IsCompound => IsTypedDirectional && _owner.IsCompoundInput;
        public bool IsButtonPair => IsTypedDirectional && !IsCompound;
        public ObservableCollection<DeviceBindingViewModel> AndInputs { get; }
        public ObservableCollection<DeviceBindingViewModel> CoreInputs { get; }
        public Visibility CompoundVisibility => IsCompound ? Visibility.Visible : Visibility.Collapsed;
        // A full 4-Way input is one logical assignment. Keep its preset button,
        // but expose the actual physical channels with their directional labels.
        // A slash-joined key list hides which key belongs to which direction.
        // 4-Way is one logical compound binding in the editor. Its underlying
        // digital/analogue channels remain intact for routing and saved profiles.
        public Visibility CompoundChannelPreviewVisibility => Visibility.Collapsed;
        public Visibility StandaloneCompoundVisibility =>
            IsCompound ? Visibility.Visible : Visibility.Collapsed;
        public Visibility CompoundAdvancedMenuVisibility =>
            IsCompound && !_owner.InputIsFullStick
                ? Visibility.Visible : Visibility.Collapsed;
        public string CompoundPrimaryLabel => IsCompound && _owner.InputIsFullStick
            ? (CoreInputs.Any(input => input.DeviceBinding.IsBound)
                ? (_owner.ResolveCompoundPresetName(CoreInputs, false) ?? "Custom 4-Way") + " ▾"
                : UiLabels.Bind)
            : CompoundSummary;

        private DirectionalCompassSlotViewModel CompassSlot(int digitalSlot, int analogueIndex, string arrow)
        {
            // Expose the four semantic directions for routing/tests even though
            // the editor displays one compound binding instead of four buttons.
            if (!IsCompound || !_owner.InputIsFullStick || CoreInputs.Count <= 1) return null;
            DeviceBindingViewModel physical;
            if (CoreInputs.Count >= 4)
                physical = CoreInputs.FirstOrDefault(input =>
                    input.DeviceBinding.InputDirectionSlot == digitalSlot)
                    ?? CoreInputs.ElementAtOrDefault(digitalSlot - 1);
            else
                physical = CoreInputs.ElementAtOrDefault(analogueIndex);
            return physical == null ? null : new DirectionalCompassSlotViewModel(arrow, physical);
        }
        public DirectionalCompassSlotViewModel CompassInputLeft => CompassSlot(1, 0, "←");
        public DirectionalCompassSlotViewModel CompassInputRight => CompassSlot(2, 0, "→");
        public DirectionalCompassSlotViewModel CompassInputUp => CompassSlot(3, 1, "↑");
        public DirectionalCompassSlotViewModel CompassInputDown => CompassSlot(4, 1, "↓");
        public Visibility ButtonPairVisibility => IsButtonPair ? Visibility.Visible : Visibility.Collapsed;
        public string CompoundSummary
        {
            get
            {
                if (!IsCompound) return string.Empty;
                if (!CoreInputs.Any(input => input.DeviceBinding.IsBound))
                    return UiLabels.Bind;
                return _owner?.ResolveCompoundPresetName(CoreInputs, false)
                    ?? "Custom 4-Way";
            }
        }
        public string CompoundDeviceLabel
        {
            get
            {
                var assigned = CoreInputs.Where(input => input.DeviceBinding?.IsBound == true).ToList();
                var source = assigned.Count > 0 ? assigned : CoreInputs.ToList();
                var selectedGuids = source.Where(input => input.DeviceBinding != null)
                    .Select(input => input.DeviceBinding.DeviceConfigurationGuid)
                    .Where(id => id != Guid.Empty).Distinct().ToList();
                if (selectedGuids.Count > 1) return "Mixed devices ▾";
                var guid = selectedGuids.FirstOrDefault();
                if (guid == Guid.Empty) return "Any device ▾";
                var profile = _owner?.ProfileViewModel?.Profile;
                var name = profile?.GetDeviceConfiguration(DeviceIoType.Input, guid)
                    ?.GetFullTitleForProfile(profile) ?? "Unavailable device";
                return (name.Length > 13 ? name.Substring(0, 12) + "…" : name) + " ▾";
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        public void RefreshCompoundSummary()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CompoundSummary)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CompoundPrimaryLabel)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CompoundDeviceLabel)));
        }

        public int GroupIndex { get; }
        public ObservableCollection<DeviceBindingViewModel> Inputs { get; }
        public Visibility OrLabelVisibility => GroupIndex > 0 ? Visibility.Visible : Visibility.Collapsed;
        private readonly bool _positionalFourWay;
        private readonly ObservableCollection<DeviceBindingViewModel> _directionalInputs;
        private readonly ObservableCollection<DeviceBindingViewModel> _ordinaryInputs;

        // WPF evaluates these getters many times during measure/bind. Returning a
        // fresh ObservableCollection on every evaluation allocates and rebinds rows.
        // An input-condition group has a fixed membership until the mapping rebuilds it.
        public ObservableCollection<DeviceBindingViewModel> DirectionalInputs => _directionalInputs;
        public ObservableCollection<DeviceBindingViewModel> OrdinaryInputs => _ordinaryInputs;
        public DeviceBindingViewModel DirectionalPrimary => DirectionalInputs.FirstOrDefault();
        public bool DirectionalBlockEnabled => Inputs.Any(input => input.DeviceBinding.Block);
        public Visibility DirectionalVisibility => DirectionalPrimary == null ? Visibility.Collapsed : Visibility.Visible;
        public Visibility DirectionalPairVisibility =>
            DirectionalPrimary != null && !IsFourWay ? Visibility.Visible : Visibility.Collapsed;
        public Visibility FourWayVisibility => IsFourWay ? Visibility.Visible : Visibility.Collapsed;
        public bool HasDirectionalInputs => DirectionalPrimary != null;
        public bool IsFourWay => DirectionalInputs.Count == 4;
        public int DirectionalColumns => IsFourWay ? 4 : 2;
        public int ButtonPairColumns => CoreInputs.Count <= 1 ? 1 : 2;
        public bool IsSingleButton => IsButtonPair && CoreInputs.Count == 1;
        public Visibility SingleButtonVisibility => IsSingleButton ? Visibility.Visible : Visibility.Collapsed;
        public bool IsPlainAxisButtonPair => IsButtonPair && CoreInputs.Count == 2 &&
            _owner?.InputIsButton == true && _owner.OutputIsAxis;
        public Visibility PlainAxisButtonPairVisibility => IsPlainAxisButtonPair
            ? Visibility.Visible : Visibility.Collapsed;
        public Visibility DirectionalButtonsVisibility =>
            IsButtonPair && CoreInputs.Count > 1 && !IsPlainAxisButtonPair
                ? Visibility.Visible : Visibility.Collapsed;
        public Visibility GroupDevicePickerVisibility =>
            IsSingleButton || IsPlainAxisButtonPair
                ? Visibility.Collapsed : Visibility.Visible;
        private DeviceBindingViewModel Slot(int number) => CoreInputs.FirstOrDefault(input =>
            input.DeviceBinding.InputDirectionSlot == number);
        private bool IsVerticalTwoWay => CoreInputs.Count == 2 &&
            string.Equals(Slot(1)?.DeviceBindingName, "Up", StringComparison.OrdinalIgnoreCase);
        public DeviceBindingViewModel LeftDirection => CoreInputs.Count >= 2 && !IsVerticalTwoWay
            ? Slot(1) : null;
        public DeviceBindingViewModel RightDirection => CoreInputs.Count >= 2 && !IsVerticalTwoWay
            ? Slot(2) : null;
        public DeviceBindingViewModel UpDirection => CoreInputs.Count == 4 ? Slot(3)
            : IsVerticalTwoWay ? Slot(1) : null;
        public DeviceBindingViewModel DownDirection => CoreInputs.Count == 4 ? Slot(4)
            : IsVerticalTwoWay ? Slot(2) : null;
        public Visibility LeftDirectionVisibility => LeftDirection != null ? Visibility.Visible : Visibility.Collapsed;
        public Visibility RightDirectionVisibility => RightDirection != null ? Visibility.Visible : Visibility.Collapsed;
        public Visibility UpDirectionVisibility => UpDirection != null ? Visibility.Visible : Visibility.Collapsed;
        public Visibility DownDirectionVisibility => DownDirection != null ? Visibility.Visible : Visibility.Collapsed;
        public string DirectionalModeLabel => IsTypedDirectional
            ? _owner.InputDirectionalGroupTitle : IsFourWay ? "4-way joystick" : "Axis";

        public bool CanRemoveCondition { get; }

        public InputConditionViewModel(int groupIndex, IEnumerable<DeviceBindingViewModel> inputs,
            bool canRemoveCondition, bool positionalFourWay = false,
            MappingViewModel owner = null)
        {
            _owner = owner;
            GroupIndex = groupIndex;
            _positionalFourWay = positionalFourWay;
            Inputs = new ObservableCollection<DeviceBindingViewModel>(inputs ?? Enumerable.Empty<DeviceBindingViewModel>());
            CoreInputs = new ObservableCollection<DeviceBindingViewModel>(Inputs.Where(input =>
                !input.DeviceBinding.IsDirectionalAndCondition));
            AndInputs = new ObservableCollection<DeviceBindingViewModel>(Inputs.Where(input =>
                input.DeviceBinding.IsDirectionalAndCondition));
            var slots = _positionalFourWay
                ? Inputs.Take(4).ToList()
                : Inputs.Where(binding => binding.DeviceBinding.InputDirectionSlot > 0).ToList();
            if (!_positionalFourWay && slots.Count == 4)
            {
                // Preserve the existing compass order: Up, Down, Left, Right.
                slots = slots.OrderBy(binding =>
                {
                    switch (binding.DeviceBinding.InputDirectionSlot)
                    {
                        case 3: return 0;
                        case 4: return 1;
                        case 1: return 2;
                        case 2: return 3;
                        default: return 4;
                    }
                }).ToList();
            }
            else if (!_positionalFourWay)
            {
                slots = slots.OrderBy(binding => binding.DeviceBinding.InputDirectionSlot).ToList();
            }
            _directionalInputs = new ObservableCollection<DeviceBindingViewModel>(slots);
            _ordinaryInputs = new ObservableCollection<DeviceBindingViewModel>(_positionalFourWay
                ? Enumerable.Empty<DeviceBindingViewModel>()
                : Inputs.Where(binding => binding.DeviceBinding.InputDirectionSlot == 0));
            CanRemoveCondition = canRemoveCondition;
        }
    }

    public sealed class CollapsedConditionVisualRow
    {
        public List<BindingVisualDescriptor> Visuals { get; set; }
        public bool StartsAlternative { get; set; }
        public Visibility AlternativeDividerVisibility =>
            StartsAlternative ? Visibility.Visible : Visibility.Collapsed;
    }

    public class MappingViewModel : INotifyPropertyChanged, IDisposable
    {
        // Independent presentation choices. The legacy runtime remains unchanged
        // until a structurally compatible directional route is available.
        public int DirectionalCapacity => Mapping.EffectiveDirectionalCapacity;
        public int InputRepresentation => Mapping.InputRepresentation;
        public int OutputRepresentation => Mapping.OutputRepresentation;
        public bool InputIsFullStick => Mapping.EffectiveInputRepresentation == 1;
        public bool InputIsAxis => Mapping.EffectiveInputRepresentation == 2;
        public bool InputIsButton => Mapping.EffectiveInputRepresentation == 3;
        public bool InputIsTrigger => Mapping.EffectiveInputRepresentation == 4;
        public bool IsCompoundInput => IsTypedDirectionalMapping &&
            Mapping.EffectiveInputRepresentation != 3 &&
            !(Mapping.EffectiveInputRepresentation == 4 && DirectionalCapacity == 4);
        public bool CompoundBlockEnabled => IsCompoundInput &&
            Mapping.DeviceBindings.Any(binding => binding.IsBound && binding.Block);
        public bool CanBlockCompound => ButtonsEnabled && IsCompoundInput &&
            Mapping.DeviceBindings.Any(binding => binding.IsBound && binding.IsBlockable());
        public bool OutputIsFullStick => Mapping.EffectiveOutputRepresentation == 1 && !OutputIsFilter;
        public bool OutputIsAxis => Mapping.EffectiveOutputRepresentation == 2 && !OutputIsFilter;
        public bool OutputIsFilter => Plugins?.Count > 0 &&
            Plugins.All(plugin => string.Equals(plugin.OutputType, "Filter", StringComparison.OrdinalIgnoreCase));
        public bool OutputIsButton => Mapping.EffectiveOutputRepresentation == 3 && !OutputIsFilter;
        // The choice icons show how many bindings the opposite representation requires.
        public string InputButtonChoiceGlyph => InputIsButton &&
            DeviceBindings?.Any(binding => binding.InputExpressionInputKind == 3 ||
                binding.DeviceBindingCategory == DeviceBindingCategory.Range) == true
                ? "↦" : OutputIsFullStick ? "⠛" : OutputIsAxis ? "••" : "●";
        public string OutputButtonChoiceGlyph => OutputIsButton &&
            Plugins?.Any(plugin => plugin.DeviceBindings.Any(binding =>
                binding.DeviceBindingCategory == DeviceBindingCategory.Range)) == true
                ? "↦" : InputIsFullStick ? "⠛" : InputIsAxis ? "••" : "●";
        public string InputAxisChoiceGlyph => OutputIsFullStick ? "↔↔" : "↔";
        public string OutputAxisChoiceGlyph => InputIsFullStick ? "↔↔" : "↔";
        public bool OutputIsTrigger => Mapping.EffectiveOutputRepresentation == 4 && !OutputIsFilter;
        public bool IsCompoundOutput => DirectionalCapacity == 4 &&
            Mapping.EffectiveOutputRepresentation == 1 && !OutputIsFilter;
        // Four Button output destinations have spatial identities. Their
        // editor must use a D-pad, not a vertical list of four arbitrary rows.
        public bool HasOutputDirectionalCompass => IsTypedDirectionalMapping &&
            DirectionalCapacity == 4 && OutputIsButton && Plugins?.Count == 4;
        public Visibility OutputDirectionalCompassVisibility => HasOutputDirectionalCompass
            ? Visibility.Visible : Visibility.Collapsed;
        public PluginViewModel CompassOutputLeft => HasOutputDirectionalCompass ? Plugins[0] : null;
        public PluginViewModel CompassOutputRight => HasOutputDirectionalCompass ? Plugins[1] : null;
        public PluginViewModel CompassOutputUp => HasOutputDirectionalCompass ? Plugins[2] : null;
        public PluginViewModel CompassOutputDown => HasOutputDirectionalCompass ? Plugins[3] : null;
        public string CompoundOutputSummary
        {
            get
            {
                var slots = Plugins?.SelectMany(plugin => plugin.DeviceBindings)
                    .Where(binding => binding.DeviceBinding.DeviceIoType == DeviceIoType.Output)
                    .ToList();
                if (slots == null || slots.Count == 0 ||
                    !slots.Any(binding => binding.DeviceBinding.IsBound))
                    return UiLabels.Bind;
                return ResolveCompoundPresetName(slots, true) ?? "Custom 4-Way";
            }
        }

        // The selected preset is identified from the saved physical channels, so
        // its name survives profile reloads and never goes stale after rebinding.
        public string ResolveCompoundPresetName(
            IEnumerable<DeviceBindingViewModel> bindings, bool output)
        {
            var channels = (bindings ?? Enumerable.Empty<DeviceBindingViewModel>())
                .Where(binding => binding?.DeviceBinding != null).ToList();
            if (channels.Count == 0 || channels.Any(binding => !binding.DeviceBinding.IsBound))
                return null;

            var persisted = channels[0].DeviceBinding.CompoundPresetName;
            if (!string.IsNullOrWhiteSpace(persisted) &&
                channels.All(channel => string.Equals(channel.DeviceBinding.CompoundPresetName,
                    persisted, StringComparison.Ordinal)))
                return persisted;

            var labels = channels.Select(binding => binding.CompactBindButtonText).ToArray();
            if (channels.Any(binding =>
                binding.DeviceBindingCategory == DeviceBindingCategory.Range))
            {
                var right = labels.Any(name => name.IndexOf("Right", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.StartsWith("RX", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("RY", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("RS", StringComparison.OrdinalIgnoreCase))
                    || channels.Any(binding => binding.DeviceBinding.KeyValue == 2
                        || binding.DeviceBinding.KeyValue == 3);
                return right ? "Right stick" : "Left stick";
            }

            if (channels.Count != 4) return null;
            // Explicitly named presets outrank built-in pattern aliases. A user
            // who names their four keys "My Movement" must see that name, not WASD.
            var templates = new List<Tuple<string, string[]>>();
            foreach (var custom in ProfileViewModel.Profile.CompoundBindingPresets ??
                new List<CompoundBindingPreset>())
                if (custom.Capacity == 4 && custom.Keys?.Count == 4)
                    templates.Add(Tuple.Create(custom.Name, custom.Keys.ToArray()));
            templates.AddRange(new[]
            {
                Tuple.Create("WASD", new[] { "A", "D", "W", "S" }),
                Tuple.Create("Arrows", new[] { "Left", "Right", "Up", "Down" }),
                Tuple.Create("Numpad", new[] { "Numpad4", "Numpad6", "Numpad8", "Numpad2" }),
                Tuple.Create("UHJK", new[] { "H", "K", "U", "J" })
            });

            foreach (var template in templates)
            {
                if (labels.Select((label, index) => PresetControlMatches(label,
                    template.Item2[index])).All(match => match))
                    return template.Item1;
            }

            if (labels.Any(label => label.IndexOf("D-pad", StringComparison.OrdinalIgnoreCase) >= 0
                || label.IndexOf("Dpad", StringComparison.OrdinalIgnoreCase) >= 0
                || label.IndexOf("POV", StringComparison.OrdinalIgnoreCase) >= 0
                || label.IndexOf("Hat", StringComparison.OrdinalIgnoreCase) >= 0))
                return "D-pad";
            var first = channels.FirstOrDefault();
            var config = ProfileViewModel.Profile.GetDeviceConfiguration(
                output ? DeviceIoType.Output : DeviceIoType.Input,
                first.DeviceBinding.DeviceConfigurationGuid);
            var kind = config?.Device == null ? DeviceVisualKind.Unknown :
                DeviceVisualCatalog.Describe(config, ProfileViewModel.Profile,
                    output ? DeviceIoType.Output : DeviceIoType.Input).Kind;
            return kind == DeviceVisualKind.Keyboard || kind == DeviceVisualKind.Unknown
                ? null : "Face buttons";
        }

        private static bool PresetControlMatches(string label, string expected)
        {
            if (MatchesControlLeafName(label, expected)) return true;
            if (expected.StartsWith("Numpad", StringComparison.OrdinalIgnoreCase))
                return MatchesControlLeafName(label, expected.Replace("Numpad", "Num"))
                    || MatchesControlLeafName(label, expected.Replace("Numpad", "Keypad"));
            return false;
        }

        public bool HasAssignedControl =>
            Mapping.DeviceBindings.Any(input => input.IsBound) ||
            Mapping.Plugins.Any(plugin => plugin.Outputs.Any(output => output.IsBound));
        // A type selector declares the desired logical mapping. It must not be
        // locked merely because the destination or source has not been assigned.
        // Unsupported combinations are explained when chosen, not hidden.
        public bool CanChooseInputButton => ButtonsEnabled;
        public bool CanChooseOutputButton => ButtonsEnabled;
        public bool CanChooseInputAxis => ButtonsEnabled;
        public bool CanChooseOutputAxis => ButtonsEnabled;
        public bool CanChooseInputFourWay => ButtonsEnabled;
        public bool CanChooseOutputFourWay => ButtonsEnabled;
        public bool CanChooseInputTrigger => ButtonsEnabled;
        public bool CanChooseOutputTrigger => ButtonsEnabled;
        private void RefreshDirectionalAvailability()
        {
            OnPropertyChanged(nameof(HasAssignedControl));
            OnPropertyChanged(nameof(CanChooseInputButton));
            OnPropertyChanged(nameof(CanChooseOutputButton));
            OnPropertyChanged(nameof(CanChooseInputAxis));
            OnPropertyChanged(nameof(CanChooseOutputAxis));
            OnPropertyChanged(nameof(CanChooseInputFourWay));
            OnPropertyChanged(nameof(CanChooseOutputFourWay));
            OnPropertyChanged(nameof(CanChooseInputTrigger));
            OnPropertyChanged(nameof(CanChooseOutputTrigger));
        }

        public bool IsTypedDirectionalMapping => Mapping.InputRepresentation != 0 ||
            Mapping.OutputRepresentation != 0;
        public bool IsFullStickInput => IsTypedDirectionalMapping &&
            DirectionalCapacity == 4 && Mapping.EffectiveInputRepresentation == 1;
        public bool IsFullStickOutput => IsTypedDirectionalMapping &&
            DirectionalCapacity == 4 && Mapping.EffectiveOutputRepresentation == 1;
        public string InputDirectionalGroupTitle => Mapping.EffectiveInputRepresentation == 1
            ? "4-Way" : Mapping.EffectiveInputRepresentation == 2 ? "Axis" :
            Mapping.EffectiveInputRepresentation == 4 ? "Trigger" : "Buttons";
        public string OutputDirectionalGroupTitle => Mapping.EffectiveOutputRepresentation == 1
            ? "4-Way" : Mapping.EffectiveOutputRepresentation == 2 ? "Axis" :
            Mapping.EffectiveOutputRepresentation == 4 ? "Trigger" : "Buttons";
        public Visibility TypedDirectionalVisibility => IsTypedDirectionalMapping
            ? Visibility.Visible : Visibility.Collapsed;
        public Visibility LegacyOutputAddVisibility => Visibility.Visible;
        public Visibility FourWayOutputDeviceVisibility =>
            (IsCompoundOutput || HasOutputDirectionalCompass) && Plugins?.Count > 0
                ? Visibility.Visible : Visibility.Collapsed;
        public Visibility CompoundOutputRemoveVisibility =>
            ButtonsEnabled && IsTypedDirectionalMapping &&
            Mapping.RequiredOutputBindingSlots > 1 && Plugins?.Count > 0
                ? Visibility.Visible : Visibility.Collapsed;
        public string FourWayOutputDeviceLabel
        {
            get
            {
                var outputs = Plugins?.SelectMany(plugin => plugin.DeviceBindings)
                    .Where(binding => binding.DeviceBinding.DeviceIoType == DeviceIoType.Output)
                    .ToList();
                if (outputs == null || outputs.Count == 0) return "Choose output device ▾";
                var guids = outputs.Where(binding => binding.DeviceBinding.DeviceConfigurationGuid != Guid.Empty)
                    .Select(binding => binding.DeviceBinding.DeviceConfigurationGuid).Distinct().ToList();
                if (guids.Count > 1) return "Mixed output devices ▾";
                if (guids.Count == 0) return "Choose output device ▾";
                var profile = ProfileViewModel.Profile;
                var device = profile.GetDeviceConfiguration(DeviceIoType.Output, guids[0]);
                return (device?.GetFullTitleForProfile(profile) ?? "Output device") + " ▾";
            }
        }
        public DeviceVisualDescriptor FourWayOutputDeviceVisual
        {
            get
            {
                var guids = Plugins?.SelectMany(plugin => plugin.DeviceBindings)
                    .Where(binding => binding.DeviceBinding.DeviceIoType == DeviceIoType.Output)
                    .Select(binding => binding.DeviceBinding.DeviceConfigurationGuid)
                    .Where(guid => guid != Guid.Empty).Distinct().ToList();
                if (guids?.Count != 1) return null;
                var profile = ProfileViewModel.Profile;
                var device = profile.GetDeviceConfiguration(DeviceIoType.Output, guids[0]);
                return device?.Device == null ? null
                    : DeviceVisualCatalog.Describe(device, profile, DeviceIoType.Output);
            }
        }

        public bool SetFourWayButtonOutputDevice(Guid guid)
        {
            if (!ButtonsEnabled || !(IsCompoundOutput || HasOutputDirectionalCompass))
                return false;
            var profile = ProfileViewModel.Profile;
            var selected = profile.GetDeviceConfiguration(DeviceIoType.Output, guid);
            if (selected?.Device == null) return false;
            var outputs = Plugins.SelectMany(plugin => plugin.DeviceBindings)
                .Where(binding => binding.DeviceBinding.DeviceIoType == DeviceIoType.Output)
                .ToList();
            if (outputs.Count == 0 || outputs.Any(binding =>
                !DeviceBindingViewModel.SupportsOutputCategory(selected,
                    binding.DeviceBindingCategory, profile.Context)))
                return false;
            foreach (var binding in outputs)
                binding.ChangeDeviceConfiguration(guid);
            profile.Context.ContextChanged();
            OnPropertyChanged(nameof(FourWayOutputDeviceLabel));
            OnPropertyChanged(nameof(FourWayOutputDeviceVisual));
            RefreshCollapsedSummary();
            return true;
        }
        public int InputDirectionalSlotCount => Mapping.RequiredInputBindingSlots;
        public bool IsDigitalCompoundInput => IsCompoundInput && Mapping.CompoundDigitalInput;
        public string CompoundBindingSummary => !IsCompoundInput || DeviceBindings == null ||
            DeviceBindings.Count == 0 ? "Unassigned" : string.Join("  /  ",
                DeviceBindings.Select(binding => binding.DeviceBinding.IsBound
                    ? binding.CompactBindButtonText : "—"));

        public int OutputDirectionalSlotCount => Mapping.RequiredOutputBindingSlots;

        // Physical assignments are authoritative. A representation is an expected
        // shape, not a restriction that prevents choosing a different control.
        private bool _preferCapturedDirectionalBinding;

        public bool InferInputTypeFromAssignment(DeviceBinding input)
        {
            if (!ButtonsEnabled || input?.IsBound != true ||
                input.DeviceIoType != DeviceIoType.Input) return false;
            // Capturing an extra AND key must never convert the mapping's
            // shared directional input representation to Button or Axis.
            if (input.IsDirectionalAndCondition) return false;
            var physical = input.DeviceBindingCategory;
            if (physical != DeviceBindingCategory.Momentary &&
                physical != DeviceBindingCategory.Range) return false;

            if (!IsTypedDirectionalMapping && physical == DeviceBindingCategory.Range)
            {
                Mapping.DirectionalCapacity = 2;
                return SetInputRepresentation(2);
            }
            if (IsTypedDirectionalMapping)
            {
                var requested = physical == DeviceBindingCategory.Range &&
                    Mapping.EffectiveInputRepresentation == 3 ? 2 :
                    physical == DeviceBindingCategory.Momentary &&
                    (Mapping.EffectiveInputRepresentation == 2 ||
                     Mapping.EffectiveInputRepresentation == 4) ? 3 : 0;
                if (requested != 0)
                {
                    _preferCapturedDirectionalBinding = true;
                    try { return SetInputRepresentation(requested); }
                    finally { _preferCapturedDirectionalBinding = false; }
                }
            }
            RefreshDirectionalAvailability();
            return false;
        }

        public bool InferOutputTypeFromAssignment(DeviceBindingCategory physical)
        {
            if (!ButtonsEnabled ||
                (physical != DeviceBindingCategory.Momentary &&
                 physical != DeviceBindingCategory.Range)) return false;
            if (!IsTypedDirectionalMapping && physical == DeviceBindingCategory.Range)
            {
                Mapping.DirectionalCapacity = 2;
                return SetOutputRepresentation(2);
            }
            if (IsTypedDirectionalMapping)
            {
                if (physical == DeviceBindingCategory.Range &&
                    Mapping.EffectiveOutputRepresentation == 3)
                    return SetOutputRepresentation(2);
                if (physical == DeviceBindingCategory.Momentary &&
                    Mapping.EffectiveOutputRepresentation != 3)
                    return SetOutputRepresentation(3);
            }
            RefreshDirectionalAvailability();
            return true;
        }

        // WPF RadioButton groups temporarily uncheck the previous choice even
        // when a rejected selection leaves the underlying mapping unchanged.
        public void RefreshRepresentationSelections()
        {
            OnPropertyChanged(nameof(InputIsFullStick));
            OnPropertyChanged(nameof(InputIsAxis));
            OnPropertyChanged(nameof(InputIsTrigger));
            OnPropertyChanged(nameof(InputIsButton));
            OnPropertyChanged(nameof(OutputIsFullStick));
            OnPropertyChanged(nameof(OutputIsAxis));
            OnPropertyChanged(nameof(OutputIsTrigger));
            OnPropertyChanged(nameof(OutputIsButton));
            OnPropertyChanged(nameof(OutputIsFilter));
            OnPropertyChanged(nameof(InputButtonChoiceGlyph));
            OnPropertyChanged(nameof(OutputButtonChoiceGlyph));
            OnPropertyChanged(nameof(InputAxisChoiceGlyph));
            OnPropertyChanged(nameof(OutputAxisChoiceGlyph));
        }

        public bool SetInputRepresentation(int value)
        {
            return SetRepresentation(value, true);
        }

        public bool SetOutputRepresentation(int value)
        {
            return SetRepresentation(value, false);
        }

        private bool SetRepresentation(int value, bool inputSide)
        {
            if (!ButtonsEnabled || value < 1 || value > 4) return false;
            var currentInput = Mapping.EffectiveInputRepresentation;
            var currentOutput = Mapping.EffectiveOutputRepresentation;
            var wantedInput = inputSide ? value : currentInput;
            var wantedOutput = inputSide ? currentOutput : value;
            var previousCapacity = Mapping.EffectiveDirectionalCapacity;
            var targetCapacity = Math.Max(
                wantedInput == 1 ? 4 : wantedInput == 2 ? 2 : 1,
                wantedOutput == 1 ? 4 : wantedOutput == 2 ? 2 : 1);
            var capacityChanged = previousCapacity != targetCapacity;
            // Capacity can shrink as well as grow. Otherwise a former 4-Way
            // mapping remains stuck with extra X/Y channels when set to Axis.
            Mapping.DirectionalCapacity = targetCapacity;
            if (inputSide)
            {
                if (value == 1 && !Mapping.CompoundDigitalInput &&
                    Mapping.EffectiveDirectionalCapacity == 4)
                {
                    // Returning to a previously configured 4-Way input restores
                    // the saved compound keyboard/D-pad unit, not a fresh pair of
                    // empty analogue axes. Analogue 4-Way banks still retain the
                    // normal two-channel structure.
                    var groupCount = Math.Max(1, Mapping.DeviceBindings
                        .Select(binding => Math.Max(0, binding.InputExpressionGroup))
                        .Distinct().Count());
                    var hasSavedDigitalFourWay = Mapping.InactiveInputRepresentations?
                        .Any(bank => bank.Representation == 1 &&
                            bank.Bindings != null &&
                            bank.Bindings.Count == 4 * groupCount &&
                            bank.Bindings.GroupBy(binding =>
                                Math.Max(0, binding.InputExpressionGroup))
                                .All(group => group.Count() == 4)) == true;
                    if (hasSavedDigitalFourWay) Mapping.CompoundDigitalInput = true;
                }
                // Axis is one continuous source per axis; Button is a set of
                // independent digital directions. The compound-digital backing
                // belongs only to a 4-Way preset, not to a later Axis selection.
                if (value == 2 || value == 3 || value == 4)
                    Mapping.CompoundDigitalInput = false;
                if (Mapping.InputRepresentation == value && !capacityChanged)
                {
                    if (Mapping.DeviceBindings.Count == Mapping.RequiredInputBindingSlots)
                        return true;
                }
                if (!SwitchDirectionalInputs(value)) return false;
                if (capacityChanged || Mapping.Plugins.Count !=
                    Mapping.RequiredOutputBindingSlots)
                {
                    var otherType = Mapping.EffectiveOutputRepresentation;
                    // Materialise the inferred representation without changing
                    // the visible choice; keep the original legacy output bank.
                    if (!SwitchDirectionalOutputs(otherType)) return false;
                }
            }
            else
            {
                if (Mapping.OutputRepresentation == value && !capacityChanged &&
                    !OutputIsFilter &&
                    Mapping.Plugins.Count == Mapping.RequiredOutputBindingSlots)
                    return true;
                if (!SwitchDirectionalOutputs(value)) return false;
                if (capacityChanged || Mapping.DeviceBindings.Count != Mapping.RequiredInputBindingSlots)
                {
                    var otherType = Mapping.EffectiveInputRepresentation;
                    // Keep the old input representation in its legacy bank.
                    if (!SwitchDirectionalInputs(otherType)) return false;
                }
            }
            ProfileViewModel.Profile.Context.ContextChanged();
            OnPropertyChanged(nameof(DirectionalCapacity));
            OnPropertyChanged(nameof(InputRepresentation));
            OnPropertyChanged(nameof(OutputRepresentation));
            OnPropertyChanged(nameof(InputIsFullStick));
            OnPropertyChanged(nameof(InputIsAxis));
            OnPropertyChanged(nameof(InputIsButton));
            OnPropertyChanged(nameof(InputIsTrigger));
            OnPropertyChanged(nameof(IsCompoundInput));
            OnPropertyChanged(nameof(CompoundBlockEnabled));
            OnPropertyChanged(nameof(CanBlockCompound));
            OnPropertyChanged(nameof(OutputIsFullStick));
            OnPropertyChanged(nameof(OutputIsAxis));
            OnPropertyChanged(nameof(OutputIsButton));
            OnPropertyChanged(nameof(OutputIsTrigger));
            OnPropertyChanged(nameof(OutputIsFilter));
            OnPropertyChanged(nameof(InputButtonChoiceGlyph));
            OnPropertyChanged(nameof(OutputButtonChoiceGlyph));
            OnPropertyChanged(nameof(InputAxisChoiceGlyph));
            OnPropertyChanged(nameof(OutputAxisChoiceGlyph));
            OnPropertyChanged(nameof(InputDirectionalSlotCount));
            OnPropertyChanged(nameof(CompoundBindingSummary));
            OnPropertyChanged(nameof(IsDigitalCompoundInput));
            OnPropertyChanged(nameof(OutputDirectionalSlotCount));
            OnPropertyChanged(nameof(IsCompoundOutput));
            OnPropertyChanged(nameof(CompoundOutputSummary));
            OnPropertyChanged(nameof(FourWayOutputDeviceVisibility));
            OnPropertyChanged(nameof(CompoundOutputRemoveVisibility));
            OnPropertyChanged(nameof(FourWayOutputDeviceLabel));
            OnPropertyChanged(nameof(IsTypedDirectionalMapping));
            OnPropertyChanged(nameof(IsFullStickInput));
            OnPropertyChanged(nameof(IsFullStickOutput));
            OnPropertyChanged(nameof(InputDirectionalGroupTitle));
            OnPropertyChanged(nameof(OutputDirectionalGroupTitle));
            RefreshDirectionalAvailability();
            OnPropertyChanged(nameof(TypedDirectionalVisibility));
            OnPropertyChanged(nameof(LegacyOutputAddVisibility));
            RefreshInputExpressionPresentation();
            RefreshCollapsedSummary();
            return true;
        }

        private bool SwitchDirectionalInputs(int representation)
        {
            var profile = ProfileViewModel.Profile;
            var capacity = Mapping.EffectiveDirectionalCapacity;
            var perGroup = Mapping.CompoundDigitalInput && representation != 3 && capacity > 1
                ? capacity : Mapping.RequiredBindingSlots(capacity, representation);
            if (perGroup < 1) return false;

            if (Mapping.InactiveInputRepresentations == null)
                Mapping.InactiveInputRepresentations = new List<DirectionalInputBank>();
            var guards = Mapping.DeviceBindings.Where(input =>
                input.IsDirectionalAndCondition).ToList();
            var previous = Mapping.DeviceBindings.Where(input =>
                !input.IsDirectionalAndCondition).ToList();
            var priorType = Mapping.InputRepresentation;
            var oldGroups = previous.GroupBy(input => Math.Max(0, input.InputExpressionGroup))
                .OrderBy(group => group.Key).Select(group => group.ToList()).ToList();
            if (oldGroups.Count == 0) oldGroups.Add(new List<DeviceBinding>());

            var required = perGroup * oldGroups.Count;
            var bank = Mapping.InactiveInputRepresentations.FirstOrDefault(item =>
                item.Representation == representation && item.Bindings != null &&
                item.Bindings.Count == required &&
                item.Bindings.Select(input => Math.Max(0, input.InputExpressionGroup))
                    .Distinct().Count() == oldGroups.Count &&
                item.Bindings.GroupBy(input => Math.Max(0, input.InputExpressionGroup))
                    .All(group => group.Count() == perGroup));

            List<DeviceBinding> next;
            if (bank != null && !_preferCapturedDirectionalBinding)
            {
                next = bank.Bindings;
                Mapping.InactiveInputRepresentations.Remove(bank);
            }
            else
            {
                next = new List<DeviceBinding>();
                for (var groupIndex = 0; groupIndex < oldGroups.Count; groupIndex++)
                {
                    var sources = oldGroups[groupIndex];
                    var isButton = representation == 3 || Mapping.CompoundDigitalInput;
                    for (var index = 0; index < perGroup; index++)
                    {
                        var previousIndex = isButton && capacity > 1 && sources.Count == 1
                            ? (index == 1 ? 0 : -1) : index;
                        var derivedAxisDirection = false;
                        if (isButton && capacity > 1 && sources.Count == capacity / 2 &&
                            sources[index / 2].IsBound &&
                            sources[index / 2].DeviceBindingCategory == DeviceBindingCategory.Range)
                        {
                            previousIndex = index / 2;
                            derivedAxisDirection = true;
                        }
                        else if (representation != 4 && !isButton && capacity > 1 &&
                                 sources.Count == capacity)
                        {
                            // Match only the requested X or Y pair. Copying an
                            // arbitrary axis into both channels would make a single
                            // bound direction drive the entire joystick diagonally.
                            var axisOffset = index * 2;
                            var compatibleAxis = sources.FindIndex(axisOffset, 2, source =>
                                source.IsBound && source.DeviceBindingCategory ==
                                    DeviceBindingCategory.Range);
                            if (compatibleAxis >= 0) previousIndex = compatibleAxis;
                            var negative = sources[index * 2];
                            var positive = sources[index * 2 + 1];
                            if (negative.IsBound && positive.IsBound &&
                                negative.DeviceBindingCategory == DeviceBindingCategory.Range &&
                                positive.DeviceBindingCategory == DeviceBindingCategory.Range &&
                                negative.DeviceConfigurationGuid == positive.DeviceConfigurationGuid &&
                                negative.KeyType == positive.KeyType &&
                                negative.KeyValue == positive.KeyValue &&
                                negative.KeySubValue == positive.KeySubValue)
                                previousIndex = index * 2;
                        }
                        // Full-stick X/Y channels and four separate one-sided
                        // triggers have different meanings. Keep them in their
                        // inactive banks; never relabel one as the other.
                        if (capacity == 4 && ((representation == 4 && sources.Count == 2) ||
                                              (priorType == 4 && representation != 4)))
                            previousIndex = -1;
                        var compatible = previousIndex >= 0 && previousIndex < sources.Count &&
                            sources[previousIndex].IsBound &&
                            (derivedAxisDirection ||
                                (isButton
                                    ? sources[previousIndex].DeviceBindingCategory == DeviceBindingCategory.Momentary
                                    : sources[previousIndex].DeviceBindingCategory == DeviceBindingCategory.Range));
                        DeviceBinding input;
                        if (compatible)
                        {
                            input = HidWizards.UCR.Core.Context.DeepXmlClone(sources[previousIndex]);
                            input.Profile = profile;
                            input.DeviceIoType = DeviceIoType.Input;
                        }
                        else
                            input = new DeviceBinding(Mapping.Update, profile, DeviceIoType.Input);
                        input.InputExpressionInputKind = derivedAxisDirection
                            ? (index % 2 == 0 ? 2 : 1) : isButton ? 0 : 3;
                        input.InputExpressionKindConfigured = true;
                        input.DeviceBindingCategory = derivedAxisDirection
                            ? DeviceBindingCategory.Range
                            : isButton ? DeviceBindingCategory.Momentary : DeviceBindingCategory.Range;
                        input.InputExpressionGroup = groupIndex;
                        input.InputDirectionSlot = (isButton || (representation == 4 && capacity == 4)) &&
                            capacity > 1 ? index + 1 : 0;
                        next.Add(input);
                    }
                }
            }

            Mapping.InactiveInputRepresentations.RemoveAll(item =>
                item.Representation == priorType && item.Bindings != null &&
                item.Bindings.Count == previous.Count);
            Mapping.InactiveInputRepresentations.Add(new DirectionalInputBank
            {
                Representation = priorType,
                Bindings = previous
            });
            Mapping.DeviceBindings = next.Concat(guards).OrderBy(input =>
                Math.Max(0, input.InputExpressionGroup))
                .ThenBy(input => input.IsDirectionalAndCondition ? 1 : 0).ToList();
            Mapping.InputRepresentation = representation;
            Mapping.UseInputExpression = true;
            RebuildDirectionalEditors();
            return true;
        }

        private bool SwitchDirectionalOutputs(int representation)
        {
            var profile = ProfileViewModel.Profile;
            var required = Mapping.CompoundDigitalOutput && representation == 1 &&
                Mapping.EffectiveDirectionalCapacity == 4 ? 4 :
                Mapping.RequiredBindingSlots(Mapping.EffectiveDirectionalCapacity, representation);
            if (required < 1) return false;
            if (Mapping.InactiveOutputRepresentations == null)
                Mapping.InactiveOutputRepresentations = new List<DirectionalOutputBank>();
            var bank = Mapping.InactiveOutputRepresentations.FirstOrDefault(item =>
                item.Representation == representation &&
                item.Plugins != null && item.Plugins.Count == required);
            var previous = Mapping.Plugins.ToList();
            var priorType = Mapping.OutputRepresentation;
            List<Plugin> next;
            if (bank != null && bank.Plugins != null && bank.Plugins.Count == required)
            {
                next = bank.Plugins;
                Mapping.InactiveOutputRepresentations.Remove(bank);
            }
            else
            {
                // As above, capacity changes require a complete destination bank.
                var isButton = representation == 3 ||
                    (Mapping.CompoundDigitalOutput && representation == 1);
                var pluginName = isButton ? "Button to Button" : "Axis to Axis";
                var template = profile.Context.GetPlugins().FirstOrDefault(item =>
                    string.Equals(item.PluginName, pluginName, StringComparison.OrdinalIgnoreCase));
                if (template == null) return false;
                next = new List<Plugin>();
                for (var index = 0; index < required; index++)
                {
                    var plugin = profile.Context.PluginManager.GetNewPlugin(template);
                    plugin.SetProfile(profile);
                    for (var outputIndex = 0; outputIndex < Math.Min(plugin.Outputs.Count,
                        plugin.OutputCategories.Count); outputIndex++)
                        plugin.Outputs[outputIndex].DeviceBindingCategory =
                            plugin.OutputCategories[outputIndex].Category;
                    var previousIndex = isButton && required > 1 && previous.Count == 1
                        ? (index == 1 ? 0 : -1) : index;
                    var previousPlugin = previousIndex >= 0 && previousIndex < previous.Count
                        ? previous[previousIndex] : null;
                    if (Mapping.EffectiveDirectionalCapacity == 4 &&
                        (representation == 4 || priorType == 4) && representation != priorType)
                        previousPlugin = null;
                    if (previousPlugin != null)
                    {
                        plugin.Filters = previousPlugin.Filters.ToList();
                        if (previousPlugin.Outputs.Count > 0 && plugin.Outputs.Count > 0 &&
                            previousPlugin.OutputCategories[0].Category ==
                                plugin.OutputCategories[0].Category &&
                            previousPlugin.Outputs[0].IsBound)
                        {
                            var source = previousPlugin.Outputs[0];
                            plugin.Outputs[0].SetDeviceConfigurationGuid(
                                source.DeviceConfigurationGuid, false);
                            plugin.Outputs[0].SetKeyTypeValue(
                                source.KeyType, source.KeyValue, source.KeySubValue);
                        }
                        SelectCompatibleOutputDevices(plugin,
                            previousPlugin.Outputs.Select(output =>
                                output.DeviceConfigurationGuid).ToList());
                    }
                    next.Add(plugin);
                }
            }

            Mapping.InactiveOutputRepresentations.RemoveAll(item =>
                item.Representation == priorType &&
                item.Plugins != null && item.Plugins.Count == previous.Count);
            Mapping.InactiveOutputRepresentations.Add(new DirectionalOutputBank
            {
                Representation = priorType,
                Plugins = previous
            });
            foreach (var vm in Plugins.ToList())
            {
                foreach (var input in vm.DeviceBindings)
                    input.PropertyChanged -= SummaryBindingOnPropertyChanged;
                vm.Dispose();
            }
            Plugins.Clear();
            Mapping.Plugins = next;
            Mapping.OutputRepresentation = representation;
            foreach (var plugin in next)
            {
                plugin.SetProfile(profile);
                var vm = new PluginViewModel(this, plugin);
                Plugins.Add(vm);
                foreach (var binding in vm.DeviceBindings)
                    SubscribeSummaryBinding(binding);
            }
            RefreshHeaderState();
            ProfileViewModel.RefreshFilterReferenceLabels();
            return true;
        }

        public string MappingTitle => Mapping.FullTitle;
        public ProfileViewModel ProfileViewModel { get; }
        public bool IsSelectedMapping => ReferenceEquals(ProfileViewModel.SelectedMapping, this);
        public void RefreshSelectionHighlight() => OnPropertyChanged(nameof(IsSelectedMapping));
        public Mapping Mapping { get; set; }
        public ObservableCollection<PluginViewModel> Plugins { get; set; }
        public ObservableCollection<DeviceBindingViewModel> DeviceBindings { get; set; }
        public ObservableCollection<InputConditionViewModel> InputConditions { get; private set; }
        public bool HasMultipleInputConditions => InputConditions?.Count > 1;
        public bool ButtonsEnabled => !ProfileViewModel.Profile.IsActive();
        // Axis runtime/plugins remain supported for existing profiles, but their
        // experimental editor is intentionally absent until it is redesigned.
        public bool IsLegacyAxisMapping => Mapping?.Plugins?.Any(plugin =>
            plugin.InputCategories.Any(category => category.Category == DeviceBindingCategory.Range) ||
            plugin.OutputCategories.Any(category => category.Category == DeviceBindingCategory.Range) ||
            string.Equals(plugin.Group, "Axis", StringComparison.OrdinalIgnoreCase)) == true ||
            Mapping?.DeviceBindings?.Any(binding =>
                binding.InputExpressionInputKind != 0 || binding.InputDirectionSlot != 0) == true;
        public bool SupportsNativeInputExpression => Mapping?.SupportsNativeInputExpression() == true;
        public bool UsesInputExpression => Mapping?.UseInputExpression == true;
        public string InputLogicSummary => BuildInputLogicSummary();
        public bool CanMoveUp => ButtonsEnabled && ProfileViewModel.CanMoveMapping(this, -1);
        public bool CanMoveDown => ButtonsEnabled && ProfileViewModel.CanMoveMapping(this, 1);
        public string MappingRoute => Mapping != null && Mapping.Plugins.Count > 0 ? Mapping.Plugins[0].PluginName : "No plugin";
        public string MappingRouteDisplay => SupportsNativeInputExpression
            ? "Inputs → " + ((Plugins?.Count ?? 0) > 1 ? "Outputs" : MappingOutputCategoryDisplay)
            : FormatMappingRoute(MappingRoute, Mapping?.DeviceBindings?.Count ?? 0, Mapping?.Plugins?.Sum(p => p.Outputs?.Count ?? 0) ?? 0);
        public List<MappingHeaderToken> MappingRouteTokens => _mappingRouteTokens;
        public string MappingOutputTypeLabel => _mappingOutputTypeLabel;
        public string MappingOutputCategoryDisplay
        {
            get
            {
                // The inspector describes what the mapping actually produces.
                // Different destination categories must never be mislabelled with
                // the type of the first output plugin.
                if (Plugins == null || Plugins.Count == 0) return "None";
                var categories = Plugins.Select(plugin => plugin.OutputType)
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (categories.Count != 1) return "Mixed";
                var category = categories[0];
                var outputs = Mapping?.Plugins?.Sum(plugin =>
                    Math.Max(1, plugin.Outputs?.Count ?? 0)) ?? 0;
                if (outputs <= 1) return category;
                switch (category)
                {
                    case "Button": return "Buttons";
                    case "Axis": return "Axes";
                    case "Event": return "Events";
                    case "Filter": return "Filters";
                    default: return category;
                }
            }
        }
        public string DefinedFilterName => _definedFilterName;
        public List<FilterReferenceBadge> ReferencedFilters => _referencedFilters;
        public bool HasFilterReferences => _referencedFilters.Count > 0;
        public string FilterIndicatorToolTip => _filterIndicatorToolTip;
        public bool HasFilters => Mapping != null && Mapping.Plugins != null &&
                                  Mapping.Plugins.Any(plugin => plugin.Filters != null && plugin.Filters.Count > 0);
        public string CollapsedSummary => _collapsedSummary;
        public List<BindingVisualDescriptor> CollapsedInputVisuals => _collapsedInputVisuals;
        public List<CollapsedConditionVisualRow> CollapsedInputRows => _collapsedInputRows;
        public List<CollapsedConditionVisualRow> CollapsedOutputRows => _collapsedOutputRows;
        public List<BindingVisualDescriptor> CollapsedOutputVisuals => _collapsedOutputVisuals;

        private List<MappingHeaderToken> _mappingRouteTokens = new List<MappingHeaderToken>();
        private string _mappingOutputTypeLabel = "None";
        private string _definedFilterName;
        private List<FilterReferenceBadge> _referencedFilters = new List<FilterReferenceBadge>();
        private string _filterIndicatorToolTip;
        private string _collapsedSummary;
        private List<BindingVisualDescriptor> _collapsedInputVisuals = new List<BindingVisualDescriptor>();
        private List<CollapsedConditionVisualRow> _collapsedInputRows = new List<CollapsedConditionVisualRow>();
        private List<CollapsedConditionVisualRow> _collapsedOutputRows = new List<CollapsedConditionVisualRow>();
        private List<BindingVisualDescriptor> _collapsedOutputVisuals = new List<BindingVisualDescriptor>();

        private bool _isFilterDefinitionHighlighted;
        public bool IsFilterDefinitionHighlighted
        {
            get => _isFilterDefinitionHighlighted;
            private set
            {
                if (_isFilterDefinitionHighlighted == value) return;
                _isFilterDefinitionHighlighted = value;
                OnPropertyChanged();
            }
        }

        private bool _isFilterReferenceHighlighted;
        public bool IsFilterReferenceHighlighted
        {
            get => _isFilterReferenceHighlighted;
            private set
            {
                if (_isFilterReferenceHighlighted == value) return;
                _isFilterReferenceHighlighted = value;
                OnPropertyChanged();
            }
        }

        private bool _isDragging;
        public bool IsDragging
        {
            get => _isDragging;
            set
            {
                if (_isDragging == value) return;
                _isDragging = value;
                OnPropertyChanged();
            }
        }

        private DeviceBinding _quickInputBinding;
        private bool _isQuickInputDetecting;
        private string _quickInputDetectionStatus;
        private double _quickInputDetectionProgress;

        public bool IsQuickInputDetecting
        {
            get => _isQuickInputDetecting;
            private set
            {
                if (_isQuickInputDetecting == value) return;
                _isQuickInputDetecting = value;
                OnPropertyChanged();
            }
        }

        public string QuickInputDetectionStatus
        {
            get => _quickInputDetectionStatus;
            private set
            {
                if (_quickInputDetectionStatus == value) return;
                _quickInputDetectionStatus = value;
                OnPropertyChanged();
            }
        }

        public double QuickInputDetectionProgress
        {
            get => _quickInputDetectionProgress;
            private set
            {
                if (Math.Abs(_quickInputDetectionProgress - value) < 0.01) return;
                _quickInputDetectionProgress = value;
                OnPropertyChanged();
            }
        }

        private CancellationTokenSource _quickOutputDetectionCancellation;
        private DispatcherTimer _quickOutputDetectionTimer;
        private DateTime _quickOutputDetectionDeadlineUtc;
        private bool _isQuickOutputDetecting;
        private string _quickOutputDetectionStatus;
        private double _quickOutputDetectionProgress;

        public bool IsQuickOutputDetecting
        {
            get => _isQuickOutputDetecting;
            private set
            {
                if (_isQuickOutputDetecting == value) return;
                _isQuickOutputDetecting = value;
                OnPropertyChanged();
            }
        }

        public string QuickOutputDetectionStatus
        {
            get => _quickOutputDetectionStatus;
            private set
            {
                if (_quickOutputDetectionStatus == value) return;
                _quickOutputDetectionStatus = value;
                OnPropertyChanged();
            }
        }

        public double QuickOutputDetectionProgress
        {
            get => _quickOutputDetectionProgress;
            private set
            {
                if (Math.Abs(_quickOutputDetectionProgress - value) < 0.01) return;
                _quickOutputDetectionProgress = value;
                OnPropertyChanged();
            }
        }

        private bool _disposed;
        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded == value) return;
                _isExpanded = value;
                OnPropertyChanged();
            }
        }

        public MappingViewModel(ProfileViewModel profileViewModel, Mapping mapping)
        {
            ProfileViewModel = profileViewModel;
            Mapping = mapping;
            IsExpanded = false;
            DeviceBindings = new ObservableCollection<DeviceBindingViewModel>();
            InputConditions = new ObservableCollection<InputConditionViewModel>();
            PopulateDeviceBindingsViewModels();
            PopulatePlugins(mapping);
            SubscribeSummaryBindings();
            RefreshHeaderState();
            RefreshCollapsedSummary();
            // Saved mappings are never converted just because their editor loads.
            // Fresh axis destinations receive pairing when the user adds/selects them.
        }

        internal void RefreshActiveState()
        {
            if (_disposed) return;

            OnPropertyChanged(nameof(ButtonsEnabled));
            OnPropertyChanged(nameof(CanBlockCompound));
            OnPropertyChanged(nameof(CompoundBlockEnabled));
            OnPropertyChanged(nameof(CanMoveUp));
            OnPropertyChanged(nameof(CanMoveDown));
            foreach (var binding in DeviceBindings) binding.RefreshActiveState();
            foreach (var plugin in Plugins) plugin.RefreshActiveState();
        }

        public void RefreshPositionState()
        {
            OnPropertyChanged(nameof(CanMoveUp));
            OnPropertyChanged(nameof(CanMoveDown));
        }

        public void RefreshFilterDefinitionPresentation()
        {
            RefreshHeaderState();
            RefreshCollapsedSummary();
        }

        public void RefreshCollapsedSummary()
        {
            _collapsedSummary = BuildCollapsedSummary();
            _collapsedInputVisuals = BuildCollapsedInputVisuals();
            _collapsedInputRows = BuildCollapsedInputRows();
            _collapsedOutputVisuals = BuildCollapsedOutputVisuals();
            _collapsedOutputRows = _collapsedOutputVisuals.Select(visual =>
                new CollapsedConditionVisualRow
                {
                    Visuals = new List<BindingVisualDescriptor> { visual }
                }).ToList();

            OnPropertyChanged(nameof(CollapsedSummary));
            OnPropertyChanged(nameof(CollapsedInputVisuals));
            OnPropertyChanged(nameof(CollapsedInputRows));
            OnPropertyChanged(nameof(CollapsedOutputRows));
            OnPropertyChanged(nameof(CollapsedOutputVisuals));
            OnPropertyChanged(nameof(InputLogicSummary));
        }

        public void RefreshTitle()
        {
            OnPropertyChanged(nameof(MappingTitle));
        }

        private void SubscribeSummaryBindings()
        {
            foreach (var binding in DeviceBindings) SubscribeSummaryBinding(binding);
            foreach (var plugin in Plugins)
            {
                foreach (var binding in plugin.DeviceBindings) SubscribeSummaryBinding(binding);
            }
        }

        private void SubscribeSummaryBinding(DeviceBindingViewModel binding)
        {
            if (binding == null) return;
            binding.PropertyChanged -= SummaryBindingOnPropertyChanged;
            binding.PropertyChanged += SummaryBindingOnPropertyChanged;
        }

        private void SummaryBindingOnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (_rebuildingDirectionalEditors || e == null) return;
            var property = e.PropertyName;
            var changed = sender as DeviceBindingViewModel;

            // Dropdown population, preview updates and editor cosmetics do not change
            // the logical route. Previously each notification could rebuild input
            // condition groups, route tokens, glyph collections and preset detection.
            // This became especially expensive when all mapping editors loaded together.
            if (property == nameof(DeviceBindingViewModel.CompactBindButtonText))
            {
                OnPropertyChanged(nameof(CompoundBindingSummary));
                return;
            }
            switch (property)
            {
                case nameof(DeviceBindingViewModel.Devices):
                case nameof(DeviceBindingViewModel.SelectedDevice):
                case nameof(DeviceBindingViewModel.BindingEnabled):
                case nameof(DeviceBindingViewModel.CanBindCurrentDevice):
                case nameof(DeviceBindingViewModel.BlockAvailability):
                case nameof(DeviceBindingViewModel.ShowBlock):
                case nameof(DeviceBindingViewModel.CanBlock):
                case nameof(DeviceBindingViewModel.ShowBindMode):
                case nameof(DeviceBindingViewModel.ShowPreview):
                case nameof(DeviceBindingViewModel.ShowButtonPreview):
                case nameof(DeviceBindingViewModel.IsCapturing):
                case nameof(DeviceBindingViewModel.BindModeProgress):
                case nameof(DeviceBindingViewModel.CurrentValue):
                case nameof(DeviceBindingViewModel.PreviewValue):
                case nameof(DeviceBindingViewModel.DeviceBindingName):
                case nameof(DeviceBindingViewModel.InputExpressionControlsVisibility):
                case nameof(DeviceBindingViewModel.CanRemoveExpressionInput):
                case nameof(DeviceBindingViewModel.InputTypeDetailsVisibility):
                case nameof(DeviceBindingViewModel.AxisConditionVisibility):
                case nameof(DeviceBindingViewModel.ThresholdConditionVisibility):
                case nameof(DeviceBindingViewModel.AxisThresholdPercent):
                case nameof(DeviceBindingViewModel.InputExpressionAxisThreshold):
                case nameof(DeviceBindingViewModel.DirectionalBindLabel):
                    return;
            }

            var boundLabelChanged = property == nameof(DeviceBindingViewModel.BindButtonText);
            if (boundLabelChanged && changed?.DeviceBinding?.IsBound == true)
            {
                // Preserve the group/compound blocking policy for newly captured keys.
                var section = ProfileViewModel.MappingSections?.FirstOrDefault(group =>
                    group.Mappings.Contains(this));
                if (section?.BlockInputs == true && !changed.DeviceBinding.Block &&
                    changed.DeviceBinding.IsBlockable())
                    changed.DeviceBinding.SetBlock(true);

                var member = changed.DeviceBinding;
                if (member.InputDirectionSlot > 0 && !member.Block &&
                    Mapping.DeviceBindings.Any(other =>
                        other != member && other.InputDirectionSlot > 0 &&
                        other.InputExpressionGroup == member.InputExpressionGroup &&
                        other.Block) && member.IsBlockable())
                    member.SetBlock(true);

                if (IsCompoundInput && !member.Block &&
                    Mapping.DeviceBindings.Any(other =>
                        other != member && other.IsBound && other.Block) &&
                    member.IsBlockable())
                    member.SetBlock(true);
            }

            // Bind capture can resolve a different physical category from the
            // one expected by the current representation. Respect the capture
            // and restructure its OR set, keeping the actual new assignment.
            if (boundLabelChanged && changed?.DeviceBinding?.IsBound == true &&
                changed.DeviceBinding.DeviceIoType == DeviceIoType.Input &&
                InferInputTypeFromAssignment(changed.DeviceBinding))
                return;

            if (boundLabelChanged && changed?.DeviceBinding?.DeviceIoType == DeviceIoType.Output &&
                IsTypedDirectionalMapping && Mapping.EffectiveInputRepresentation == 3)
            {
                for (var index = 0; index < DeviceBindings.Count; index++)
                    DeviceBindings[index].DeviceBindingName = GetDirectionalInputSlotLabel(index);
            }

            var structural = property == nameof(DeviceBindingViewModel.InputExpressionInputKind) ||
                             property == nameof(DeviceBindingViewModel.InputExpressionGroupLabel) ||
                             property == nameof(DeviceBindingViewModel.DeviceBindingCategory);

            // A binding-name change or Block switch requires new glyphs, not a
            // destruction/reconstruction of every OR/input condition in the mapping.
            if (structural)
                RefreshInputExpressionPresentation();
            if (structural || boundLabelChanged)
                RefreshPresetAvailability();

            OnPropertyChanged(nameof(CompoundBlockEnabled));
            OnPropertyChanged(nameof(CanBlockCompound));
            OnPropertyChanged(nameof(CompoundBindingSummary));
            OnPropertyChanged(nameof(CompoundOutputSummary));
            foreach (var group in InputConditions)
                group.RefreshCompoundSummary();
            RefreshDirectionalAvailability();
            RefreshCollapsedSummary();
        }

        public void MoveUp()
        {
            ProfileViewModel.MoveMapping(this, -1);
        }

        public void MoveDown()
        {
            ProfileViewModel.MoveMapping(this, 1);
        }

        public void Remove()
        {
            ProfileViewModel.RemoveMapping(this);
        }

        public void AddPlugin(Plugin plugin)
        {
            var preferredDevices = Plugins.LastOrDefault()?.Plugin?.Outputs
                .Select(output => output.DeviceConfigurationGuid).ToList();
            var newPlugin = ProfileViewModel.Profile.Context.PluginManager.GetNewPlugin(plugin);
            if (!Mapping.AddPlugin(newPlugin)) return;
            SelectCompatibleOutputDevices(newPlugin, preferredDevices);

            var pluginViewModel = new PluginViewModel(this, newPlugin);
            Plugins.Add(pluginViewModel);
            foreach (var binding in pluginViewModel.DeviceBindings) SubscribeSummaryBinding(binding);
            RefreshHeaderState();
            RefreshCollapsedSummary();
            ProfileViewModel.RefreshFilterReferenceLabels();
            RefreshInputExpressionPresentation();
            Logger.Info("Output added to mapping '" + MappingTitle + "': " + newPlugin.PluginName);
            if (Plugins.Count != 1) return;
            
            PopulateDeviceBindingsViewModels();
            foreach (var binding in DeviceBindings) SubscribeSummaryBinding(binding);
            if (HasAxisOutput && DeviceBindings.Count == 1 &&
                !DeviceBindings[0].DeviceBinding.IsBound)
                SetDirectionalMode(2);
            RefreshCollapsedSummary();
        }

        private string GetDirectionalInputSlotLabel(int index)
        {
            if (!IsTypedDirectionalMapping) return "Input " + (index + 1);
            // Each OR group restarts its own directional slots.
            var perGroup = Math.Max(1, Mapping.RequiredInputBindingSlotsPerGroup);
            index %= perGroup;
            var representation = Mapping.EffectiveInputRepresentation;
            if (DirectionalCapacity == 4 && (representation == 1 || representation == 2) &&
                !Mapping.CompoundDigitalInput)
                return index == 0 ? "X axis" : "Y axis";
            if (DirectionalCapacity == 4)
                return new[] { "Left", "Right", "Up", "Down" }[Math.Min(3, index)];
            if (DirectionalCapacity == 2 && (representation == 3 || Mapping.CompoundDigitalInput))
            {
                // Distinguish signed physical directions using the assigned axis
                // whenever its orientation can be identified. Otherwise show signs.
                var axis = Mapping.Plugins.SelectMany(plugin => plugin.Outputs)
                    .FirstOrDefault(output => output.IsBound &&
                        output.DeviceBindingCategory == DeviceBindingCategory.Range);
                var name = axis?.BoundName() ?? string.Empty;
                var vertical = Regex.IsMatch(name, @"(^|[^a-z])Y([^a-z]|$)",
                    RegexOptions.IgnoreCase) ||
                    name.IndexOf("vertical", StringComparison.OrdinalIgnoreCase) >= 0;
                var horizontal = Regex.IsMatch(name, @"(^|[^a-z])X([^a-z]|$)",
                    RegexOptions.IgnoreCase) ||
                    name.IndexOf("horizontal", StringComparison.OrdinalIgnoreCase) >= 0;
                if (vertical) return index == 0 ? "Up" : "Down";
                if (horizontal) return index == 0 ? "Left" : "Right";
                return index == 0 ? "−" : "+";
            }
            return representation == 3 ? "Button" : representation == 4 ? "Trigger" : "Axis";
        }

        private void PopulateDeviceBindingsViewModels()
        {
            if (Mapping.Plugins.Count == 0) return;

            var plugin = Mapping.Plugins[0];
            if (Mapping.UseInputExpression)
            {
                for (var i = 0; i < Mapping.DeviceBindings.Count; i++)
                {
                    DeviceBindings.Add(new DeviceBindingViewModel(Mapping.DeviceBindings[i])
                    {
                        DeviceBindingName = GetDirectionalInputSlotLabel(i),
                        ExpressionMapping = Mapping,
                        DeviceBindingCategory = Mapping.DeviceBindings[i].InputExpressionInputKind == 0 ? DeviceBindingCategory.Momentary : DeviceBindingCategory.Range
                    });
                }
            }
            else
            {
                for (var i = 0; i < plugin.InputCategories.Count && i < Mapping.DeviceBindings.Count; i++)
                {
                    DeviceBindings.Add(new DeviceBindingViewModel(Mapping.DeviceBindings[i])
                    {
                        DeviceBindingName = plugin.InputCategories[i].Name,
                        ExpressionMapping = SupportsNativeInputExpression ? Mapping : null,
                        DeviceBindingCategory = Mapping.DeviceBindings[i].InputExpressionKindConfigured
                            ? (Mapping.DeviceBindings[i].InputExpressionInputKind == 0
                                ? DeviceBindingCategory.Momentary : DeviceBindingCategory.Range)
                            : plugin.InputCategories[i].Category
                    });
                }
            }

            RefreshInputExpressionPresentation();
        }

        // A four-way plugin has four physical channels but exactly one logical
        // input unit in the inspector. Both native-expression and legacy plugin
        // routes consume the same group/compass editor.
        public InputConditionViewModel FourWayInputGroup =>
            Plugins?.Count == 1 && Plugins[0].IsFourButtonAxis && DeviceBindings?.Count == 4
                ? new InputConditionViewModel(0, DeviceBindings, false, positionalFourWay: true,
                    owner: this) : null;

        public bool HasAxisOutput => Plugins != null &&
            Plugins.Any(p => string.Equals(p.OutputType, "Axis", StringComparison.OrdinalIgnoreCase));
        public Visibility DirectionalPresetsVisibility => HasAxisOutput ? Visibility.Visible : Visibility.Collapsed;

        public void SetDirectionalMode(int slots)
        {
            if (!ButtonsEnabled || !HasAxisOutput || InputConditions == null) return;
            var group = InputConditions.FirstOrDefault()?.GroupIndex ?? 0;
            if (!Mapping.SetDirectionalInputMode(group, slots)) return;

            // The legacy Button-to-Axis plugin only receives a Boolean, so it
            // cannot represent negative/positive travel. Convert axis destinations
            // to the continuous driver before pairing their physical inputs.
            if (slots > 0)
            {
                var axisTemplate = GetCompatiblePluginOptions().FirstOrDefault(option =>
                    string.Equals(option.Name, "Axis to Axis", StringComparison.OrdinalIgnoreCase))?.Plugin;
                if (axisTemplate != null)
                    foreach (var output in Plugins.Where(output => output.OutputType == "Axis" &&
                        output.Plugin.InputCategories.Count == 1 &&
                        output.Plugin.InputCategories[0].Category != DeviceBindingCategory.Range).ToList())
                        ChangeOutputType(output, axisTemplate);
            }

            // A joystick needs two independent analogue destinations (X and Y).
            // Do not automatically delete the user's existing outputs when switching back.
            if (slots == 4 && Plugins.Count(p => p.OutputType == "Axis") < 2)
                AddDefaultOutput();

            RebuildDirectionalEditors();
        }

        private bool _rebuildingDirectionalEditors;
        private void RebuildDirectionalEditors()
        {
            if (_rebuildingDirectionalEditors) return;
            _rebuildingDirectionalEditors = true;
            try
            {
                // A binding can emit a property notification while it is being
                // disposed. Snapshot and detach before changing the collection.
                var oldBindings = DeviceBindings.ToList();
                DeviceBindings.Clear();
                foreach (var binding in oldBindings)
                {
                    binding.PropertyChanged -= SummaryBindingOnPropertyChanged;
                    binding.Dispose();
                }
                PopulateDeviceBindingsViewModels();
                OnPropertyChanged(nameof(CompoundBindingSummary));
                foreach (var binding in DeviceBindings.ToList()) SubscribeSummaryBinding(binding);
                RefreshHeaderState();
                RefreshCollapsedSummary();
            }
            finally
            {
                _rebuildingDirectionalEditors = false;
            }
        }

        // Query only: menu availability must reflect the physical controls in
        // configured devices rather than showing preset choices that cannot bind.
        public bool CanApplyCompoundInputPreset(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            var profile = ProfileViewModel.Profile;
            var patterns = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                { "WASD", new[] { "A", "D", "W", "S" } },
                { "Arrows", new[] { "Left", "Right", "Up", "Down" } },
                { "Numpad", new[] { "Numpad4", "Numpad6", "Numpad8", "Numpad2" } },
                { "UHJK", new[] { "H", "K", "U", "J" } },
                { "UJHK", new[] { "H", "K", "U", "J" } }
            };
            string[] keys;
            if (!patterns.TryGetValue(name, out keys))
                keys = profile.CompoundBindingPresets?.FirstOrDefault(preset =>
                    preset.Capacity == 4 && string.Equals(preset.Name, name,
                        StringComparison.OrdinalIgnoreCase))?.Keys?.ToArray();
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
                    if (keys.Length != 4 ||
                        keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 4 ||
                        kind != DeviceVisualKind.Keyboard) continue;
                    var buttons = nodes.Where(node => node.DeviceBindingInfo.DeviceBindingCategory ==
                        DeviceBindingCategory.Momentary).ToList();
                    if (keys.All(key => buttons.Any(node =>
                        MatchesControlLeafName(node.Title, key)))) return true;
                    continue;
                }
                if (name == "Left stick" || name == "Right stick")
                {
                    if (kind == DeviceVisualKind.Keyboard || kind == DeviceVisualKind.Mouse ||
                        kind == DeviceVisualKind.Unknown || kind == DeviceVisualKind.Unavailable) continue;
                    var index = name == "Left stick" ? 0 : 2;
                    if (nodes.Where(node => node.DeviceBindingInfo.DeviceBindingCategory ==
                            DeviceBindingCategory.Range)
                        .GroupBy(node => node.DeviceBindingInfo.KeyType)
                        .Any(group => group.Any(node => node.DeviceBindingInfo.KeyValue == index)
                                   && group.Any(node => node.DeviceBindingInfo.KeyValue == index + 1)))
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
                var axes = FlattenBindingNodes(device.Device.GetDeviceBindingMenu(profile.Context,
                    DeviceIoType.Input)).Where(node =>
                        node?.DeviceBindingInfo?.DeviceBindingCategory == DeviceBindingCategory.Range).ToList();
                var matching = axes.GroupBy(node => node.DeviceBindingInfo.KeyType)
                    .FirstOrDefault(nodes => nodes.Any(node =>
                        node.DeviceBindingInfo.KeyValue == axisIndex) &&
                        nodes.Any(node => node.DeviceBindingInfo.KeyValue == axisIndex + 1));
                if (matching == null) continue;
                candidates.Add(Tuple.Create(device, new[]
                {
                    matching.First(node => node.DeviceBindingInfo.KeyValue == axisIndex).DeviceBindingInfo,
                    matching.First(node => node.DeviceBindingInfo.KeyValue == axisIndex + 1).DeviceBindingInfo
                }));
            }
            var chosen = candidates.FirstOrDefault(item => item.Item1.Guid == preferred)
                ?? candidates.FirstOrDefault(item => item.Item1.Guid == primary)
                ?? candidates.FirstOrDefault();
            if (chosen == null)
            {
                error = "No configured controller exposes this stick's two axes.";
                return false;
            }
            // A missing stick must not change the logical input representation.
            var previousDigital = Mapping.CompoundDigitalInput;
            Mapping.CompoundDigitalInput = false;
            if (!SetInputRepresentation(1))
            {
                Mapping.CompoundDigitalInput = previousDigital;
                error = "Unable to prepare a 4-Way analogue stick binding.";
                return false;
            }
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
                binding.InputExpressionInputKind = 3;
                binding.DeviceBindingCategory = DeviceBindingCategory.Range;
                binding.SetDeviceConfigurationGuid(chosen.Item1.Guid, false);
                binding.SetKeyTypeValue(info.KeyType, info.KeyValue, info.KeySubValue);
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
            if (!EnsureCompoundDigitalSlots(out error, fourWay: true)) return false;
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
                binding.InputExpressionInputKind = 0;
                binding.DeviceBindingCategory = DeviceBindingCategory.Momentary;
                binding.SetDeviceConfigurationGuid(chosen.Item1.Guid, false);
                binding.SetKeyTypeValue(info.KeyType, info.KeyValue, info.KeySubValue);
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
            if (!EnsureCompoundDigitalSlots(out error, fourWay: true)) return false;
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
                binding.InputExpressionInputKind = 0;
                binding.DeviceBindingCategory = DeviceBindingCategory.Momentary;
                binding.SetDeviceConfigurationGuid(device.Guid, false);
                binding.SetKeyTypeValue(info.KeyType, info.KeyValue, info.KeySubValue);
                binding.CompoundPresetName = "D-pad";
            }
            foreach (var input in inputs) input.RefreshDeviceList();
            RefreshCollapsedSummary();
            return true;
        }

        public bool UseAnalogueCompoundInput(out string error)
        {
            error = null;
            if (!ButtonsEnabled || !IsCompoundInput)
            {
                error = "Choose 4-Way or Axis input first.";
                return false;
            }
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

        private bool EnsureCompoundDigitalSlots(out string error, bool fourWay)
        {
            error = null;
            if (!ButtonsEnabled)
            {
                error = "Stop the running profile to change bindings.";
                return false;
            }
            var previousDigital = Mapping.CompoundDigitalInput;
            Mapping.CompoundDigitalInput = true;
            if (fourWay && (DirectionalCapacity != 4 ||
                            Mapping.EffectiveInputRepresentation != 1))
            {
                if (!SetInputRepresentation(1))
                {
                    Mapping.CompoundDigitalInput = previousDigital;
                    error = "Unable to change this mapping to 4-Way input.";
                    return false;
                }
            }
            else if (!IsCompoundInput && !SetInputRepresentation(2))
            {
                Mapping.CompoundDigitalInput = previousDigital;
                error = "Unable to prepare an Axis input.";
                return false;
            }
            var required = DirectionalCapacity * Math.Max(1, Mapping.DeviceBindings
                .Select(input => Math.Max(0, input.InputExpressionGroup))
                .Distinct().Count());
            if (!previousDigital && Mapping.DeviceBindings.Count(input =>
                !input.IsDirectionalAndCondition) != required)
            {
                if (!SwitchDirectionalInputs(Mapping.EffectiveInputRepresentation))
                {
                    Mapping.CompoundDigitalInput = previousDigital;
                    error = "Unable to create compound directional slots.";
                    return false;
                }
            }
            if (Mapping.DeviceBindings.Count(input => !input.IsDirectionalAndCondition) != required)
            {
                error = "The compound binding's internal slots are incomplete.";
                return false;
            }
            OnPropertyChanged(nameof(InputDirectionalSlotCount));
            OnPropertyChanged(nameof(IsDigitalCompoundInput));
            OnPropertyChanged(nameof(CompoundBindingSummary));
            OnPropertyChanged(nameof(CompoundBlockEnabled));
            ProfileViewModel.Profile.Context.ContextChanged();
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
            var patterns = new Dictionary<string, string[]>
            {
                { "WASD", new[] { "A", "D", "W", "S" } },
                { "Arrows", new[] { "Left", "Right", "Up", "Down" } },
                { "Numpad", new[] { "Numpad4", "Numpad6", "Numpad8", "Numpad2" } },
                { "UHJK", new[] { "H", "K", "U", "J" } },
                { "UJHK", new[] { "H", "K", "U", "J" } }
            };
            var profile = ProfileViewModel.Profile;
            string[] keys;
            if (!patterns.TryGetValue(preset, out keys))
            {
                var custom = profile.CompoundBindingPresets?.FirstOrDefault(item =>
                    item.Capacity == 4 &&
                    string.Equals(item.Name, preset, StringComparison.OrdinalIgnoreCase));
                keys = custom?.Keys?.ToArray();
                if (keys == null)
                {
                    error = "Preset not found.";
                    return false;
                }
            }
            if (keys.Length != 4 ||
                keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 4)
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
                        profile.Context, DeviceIoType.Input))
                        .Where(node => node?.DeviceBindingInfo != null &&
                            node.DeviceBindingInfo.DeviceBindingCategory ==
                                DeviceBindingCategory.Momentary).ToList();
                    var matched = keys.Select(name =>
                    {
                        var aliases = name.StartsWith("Numpad", StringComparison.OrdinalIgnoreCase)
                            ? new[] { name, name.Replace("Numpad", "Num"),
                                name.Replace("Numpad", "Keypad") }
                            : new[] { name };
                        return available.FirstOrDefault(node => aliases.Any(alias =>
                            MatchesControlLeafName(node.Title, alias)));
                    }).ToArray();
                    return new { Configuration = config, Selections = matched };
                })
                .Where(candidate => candidate.Selections.All(node => node != null))
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
            if (!EnsureCompoundDigitalSlots(out error, fourWay: true)) return false;
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
                input.InputExpressionInputKind = 0;
                input.DeviceBindingCategory = DeviceBindingCategory.Momentary;
                input.SetDeviceConfigurationGuid(keyboard.Guid, false);
                input.SetKeyTypeValue(info.KeyType, info.KeyValue, info.KeySubValue);
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
            if (new[] { "WASD", "Arrows", "Numpad", "UHJK", "UJHK" }.Any(value =>
                string.Equals(value, name, StringComparison.OrdinalIgnoreCase)))
            {
                error = "That name is reserved for a built-in preset.";
                return false;
            }
            var inputs = DeviceBindings.Where(input =>
                    input.DeviceBinding.InputExpressionGroup == groupIndex &&
                !input.DeviceBinding.IsDirectionalAndCondition)
                .OrderBy(input => input.DeviceBinding.InputDirectionSlot)
                .Select(input => input.DeviceBinding).ToList();
            if (!IsDigitalCompoundInput || inputs.Count != DirectionalCapacity ||
                inputs.Any(input => !input.IsBound))
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
                if (count < 1) return;
                var capacity = DirectionalCapacity;
                var isButton = Mapping.EffectiveInputRepresentation == 3 ||
                    Mapping.CompoundDigitalInput;
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
                        ? "AND" : GetDirectionalInputSlotLabel(directionalIndex++))
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

        // Adding an output uses the existing compatible output type by default.
        // The new row's type selector can change it afterwards.
        public bool AddDefaultOutput()
        {
            if (!ButtonsEnabled) return false;
            if (Plugins.Count == 0 && IsTypedDirectionalMapping)
            {
                // Restore the whole required output group, never just a single
                // physical channel of a 2-axis / 4-direction compound.
                if (!SwitchDirectionalOutputs(Mapping.EffectiveOutputRepresentation))
                    return false;
                RefreshHeaderState();
                RefreshInputExpressionPresentation();
                RefreshRepresentationSelections();
                RefreshCollapsedSummary();
                ProfileViewModel.Profile.Context.ContextChanged();
                return Plugins.Count == Mapping.RequiredOutputBindingSlots;
            }
            var options = GetCompatiblePluginOptions();
            var currentType = Plugins.LastOrDefault()?.Plugin?.GetType();
            var selected = options.FirstOrDefault(option => option.Plugin.GetType() == currentType)
                ?? options.FirstOrDefault(option => option.OutputType == "Button")
                ?? options.FirstOrDefault();
            if (selected == null) return false;

            var count = Plugins.Count;
            AddPlugin(selected.Plugin);
            return Plugins.Count > count;
        }

        // Change the number of button inputs without replacing the mapping,
        // its title, first axis output or filter references.
        public bool ChangeAxisButtonMode(PluginViewModel original, bool useTwoButtons)
        {
            return ChangeAxisInputMode(original, useTwoButtons ? 2 : 1);
        }

        public bool ChangeAxisInputMode(PluginViewModel original, int buttonCount)
        {
            if (!ButtonsEnabled || original == null || Plugins.Count != 1 ||
                Mapping.Plugins.Count != 1 ||
                !original.IsAxisButtonMapping || (buttonCount != 1 && buttonCount != 2 && buttonCount != 4))
                return false;

            var currentCount = original.IsFourButtonAxis ? 4 : original.IsTwoButtonAxis ? 2 : 1;
            if (currentCount == buttonCount) return true;

            var className = buttonCount == 1 ? "ButtonToAxis" :
                buttonCount == 2 ? "ButtonsToAxis" : "ButtonsToAxes";
            var targetType = original.Plugin.GetType().Assembly.GetType(
                original.Plugin.GetType().Namespace + "." + className);
            if (targetType == null || !typeof(Plugin).IsAssignableFrom(targetType)) return false;
            var replacement = (Plugin)Activator.CreateInstance(targetType);
            replacement.SetProfile(ProfileViewModel.Profile);
            if (replacement.InputCategories.Count != buttonCount ||
                replacement.Outputs.Count < 1 || original.Plugin.Outputs.Count < 1) return false;

            // Single-axis mode keeps X when leaving a two-axis stick. Entering four-way
            // mode requires choosing a whole destination, not reusing one arbitrary axis.
            if (buttonCount != 4 && !original.IsFourButtonAxis ||
                buttonCount != 4 && original.IsFourButtonAxis &&
                original.Plugin.OutputCategories[0].Category == DeviceBindingCategory.Range)
                replacement.Outputs[0] = original.Plugin.Outputs[0];
            replacement.Filters = original.Plugin.Filters.ToList();
            var preferredOutput = original.Plugin.Outputs.Select(binding => binding.DeviceConfigurationGuid).ToList();
            SelectCompatibleOutputDevices(replacement, preferredOutput);

            // Axis mode 2/4 consumes positional inputs, not a boolean AND/OR expression.
            // The caller must explicitly confirm discarding expression terms before this point.
            if (Mapping.UseInputExpression)
            {
                Mapping.UseInputExpression = false;
                foreach (var term in Mapping.DeviceBindings)
                {
                    term.SetInputExpressionGroup(0);
                    term.SetInputExpressionNegated(false);
                }
            }

            foreach (var binding in DeviceBindings)
            {
                binding.PropertyChanged -= SummaryBindingOnPropertyChanged;
                binding.Dispose();
            }
            DeviceBindings.Clear();
            foreach (var binding in original.DeviceBindings)
                binding.PropertyChanged -= SummaryBindingOnPropertyChanged;
            original.Dispose();

            while (Mapping.DeviceBindings.Count < buttonCount)
                Mapping.DeviceBindings.Add(new DeviceBinding(Mapping.Update,
                    ProfileViewModel.Profile, DeviceIoType.Input)
                {
                    DeviceConfigurationGuid = ProfileViewModel.Profile.GetPrimaryDeviceConfiguration(DeviceIoType.Input)?.Guid ?? Guid.Empty
                });
            while (Mapping.DeviceBindings.Count > buttonCount)
                Mapping.DeviceBindings.RemoveAt(Mapping.DeviceBindings.Count - 1);

            Mapping.Plugins[0] = replacement;
            var viewModel = new PluginViewModel(this, replacement);
            Plugins[0] = viewModel;
            PopulateDeviceBindingsViewModels();
            foreach (var binding in DeviceBindings) SubscribeSummaryBinding(binding);
            foreach (var binding in viewModel.DeviceBindings) SubscribeSummaryBinding(binding);
            ProfileViewModel.Profile.Context.ContextChanged();
            RefreshHeaderState();
            RefreshInputExpressionPresentation();
            RefreshPresetAvailability();
            RefreshCollapsedSummary();
            ProfileViewModel.RefreshFilterReferenceLabels();
            Logger.Info("Axis input mode for '" + MappingTitle + "': " + buttonCount + " buttons");
            return true;
        }

        public Visibility ControllerStickPresetVisibility =>
            Mapping?.Plugins?.Count == 1 &&
            string.Equals(Mapping.Plugins[0].PluginName, "Axes to Axes", StringComparison.OrdinalIgnoreCase) &&
            Mapping.DeviceBindings.Count == 2 ? Visibility.Visible : Visibility.Collapsed;

        public bool ApplyControllerStickInputPreset(int axisIndex, out string error)
        {
            error = null;
            if (!ButtonsEnabled || ControllerStickPresetVisibility != Visibility.Visible ||
                (axisIndex != 0 && axisIndex != 2)) return false;
            var profile = ProfileViewModel.Profile;
            var preferred = Mapping.DeviceBindings[0].DeviceConfigurationGuid;
            var primary = profile.GetPrimaryDeviceConfiguration(DeviceIoType.Input)?.Guid;
            var options = new List<Tuple<DeviceConfiguration, DeviceBindingInfo[]>>();
            foreach (var device in profile.GetDeviceConfigurationList(DeviceIoType.Input))
            {
                if (device?.Device == null) continue;
                var kind = DeviceVisualCatalog.Describe(device, profile, DeviceIoType.Input).Kind;
                if (kind == DeviceVisualKind.Keyboard || kind == DeviceVisualKind.Mouse ||
                    kind == DeviceVisualKind.Unavailable || kind == DeviceVisualKind.Unknown) continue;
                var axes = FlattenBindingNodes(device.Device.GetDeviceBindingMenu(profile.Context, DeviceIoType.Input)).Where(node => node?.DeviceBindingInfo != null &&
                    node.DeviceBindingInfo.DeviceBindingCategory == DeviceBindingCategory.Range).ToList();
                var pair = axes.GroupBy(node => node.DeviceBindingInfo.KeyType).FirstOrDefault(group =>
                    group.Any(node => node.DeviceBindingInfo.KeyValue == axisIndex) &&
                    group.Any(node => node.DeviceBindingInfo.KeyValue == axisIndex + 1));
                if (pair != null) options.Add(Tuple.Create(device, new[]
                {
                    pair.First(node => node.DeviceBindingInfo.KeyValue == axisIndex).DeviceBindingInfo,
                    pair.First(node => node.DeviceBindingInfo.KeyValue == axisIndex + 1).DeviceBindingInfo
                }));
            }
            var selected = options.FirstOrDefault(option => option.Item1.Guid == preferred)
                ?? options.FirstOrDefault(option => option.Item1.Guid == primary)
                ?? options.FirstOrDefault();
            if (selected == null)
            {
                error = "Add a controller with the requested analogue stick to the profile's input devices first.";
                return false;
            }
            for (var i = 0; i < 2; i++)
            {
                var info = selected.Item2[i];
                var binding = Mapping.DeviceBindings[i];
                binding.DeviceBindingCategory = DeviceBindingCategory.Range;
                binding.SetDeviceConfigurationGuid(selected.Item1.Guid, false);
                binding.SetKeyTypeValue(info.KeyType, info.KeyValue, info.KeySubValue);
            }
            foreach (var binding in DeviceBindings) binding.RefreshDeviceList();
            RefreshCollapsedSummary();
            return true;
        }

        // One four-way destination may consist of two analogue axes or four D-pad buttons.
        // Only offer a choice after verifying *all* its constituent controls exist.
        public DeviceConfiguration GetSelectedPresetInputDevice()
        {
            var profile = ProfileViewModel.Profile;
            var guid = Mapping.DeviceBindings.FirstOrDefault()?.DeviceConfigurationGuid ?? Guid.Empty;
            return profile.GetDeviceConfiguration(DeviceIoType.Input, guid)
                ?? profile.GetPrimaryDeviceConfiguration(DeviceIoType.Input);
        }

        public bool SelectedPresetInputIsKeyboard =>
            DeviceVisualCatalog.Describe(GetSelectedPresetInputDevice(), ProfileViewModel.Profile,
                DeviceIoType.Input).Kind == DeviceVisualKind.Keyboard;

        public bool SelectedPresetInputHasDPad =>
            FindDPadControls(GetSelectedPresetInputDevice(), DeviceIoType.Input) != null;

        public void RefreshPresetAvailability()
        {
            OnPropertyChanged(nameof(SelectedPresetInputIsKeyboard));
            OnPropertyChanged(nameof(SelectedPresetInputHasDPad));
            foreach (var plugin in Plugins ?? Enumerable.Empty<PluginViewModel>())
                plugin.RefreshInputPresetDisplay();
        }

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
        // Never mutate old mappings merely because their editor is hidden.
        // Leaving a compound 4-Way source for a single physical button must not
        // manufacture three blank directional slots or discard existing inputs.
        // Move bound compound directions into ordinary AND guards and preserve
        // the destination plugins, including any already bound output channels.
        public bool ConvertCompoundToSingleButton(out string error)
        {
            error = null;
            if (!ButtonsEnabled || !InputIsFullStick || Mapping.Plugins.Count == 0)
            {
                error = "Only an editable 4-Way input can be converted.";
                return false;
            }
            if (Mapping.Plugins.Any(plugin => plugin.InputCategories.Count != 1))
            {
                error = "This remapper requires multiple positional inputs and cannot be converted safely.";
                return false;
            }
            var profile = ProfileViewModel.Profile;
            var directions = Mapping.DeviceBindings.Where(input =>
                !input.IsDirectionalAndCondition).ToList();
            var existingGuards = Mapping.DeviceBindings.Where(input =>
                input.IsDirectionalAndCondition && input.IsBound).ToList();

            // Four directional controls are ONE compound input. Turning them all
            // into AND guards would require opposite directions simultaneously
            // (e.g. Left AND Right), making the new Button mapping unusable.
            // Keep their complete physical bank for restoring 4-Way later.
            if (Mapping.InactiveInputRepresentations == null)
                Mapping.InactiveInputRepresentations = new List<DirectionalInputBank>();
            Mapping.InactiveInputRepresentations.RemoveAll(bank =>
                bank.Representation == 1 && bank.Bindings?.Count == directions.Count);
            Mapping.InactiveInputRepresentations.Add(new DirectionalInputBank
            {
                Representation = 1,
                Bindings = directions
            });

            // Every former OR alternative becomes one unbound Button input.
            // Its explicit, bound AND conditions are retained in that same
            // alternative. Unbound redundant slots are discarded.
            var nextInputs = new List<DeviceBinding>();
            var groups = directions.Select(input =>
                Math.Max(0, input.InputExpressionGroup)).Distinct().OrderBy(i => i).ToList();
            if (groups.Count == 0) groups.Add(0);
            foreach (var groupIndex in groups)
            {
                var primary = new DeviceBinding(Mapping.Update, profile, DeviceIoType.Input)
                {
                    DeviceBindingCategory = DeviceBindingCategory.Momentary,
                    InputExpressionInputKind = 0,
                    InputExpressionKindConfigured = true,
                    InputExpressionGroup = groupIndex,
                    InputDirectionSlot = 0
                };
                nextInputs.Add(primary);
                foreach (var guard in existingGuards.Where(input =>
                    Math.Max(0, input.InputExpressionGroup) == groupIndex))
                {
                    guard.InputDirectionSlot = 0;
                    // Keep the guard marker: the representation inference must
                    // not mistake an analogue AND condition for an Axis primary.
                    guard.IsDirectionalAndCondition = true;
                    guard.InputExpressionKindConfigured = true;
                    nextInputs.Add(guard);
                }
            }

            Mapping.InputRepresentation = 0;
            Mapping.OutputRepresentation = 0;
            Mapping.DirectionalCapacity = 0;
            Mapping.CompoundDigitalInput = false;
            Mapping.CompoundDigitalOutput = false;
            Mapping.DeviceBindings = nextInputs;
            Mapping.UseInputExpression = true;
            RebuildDirectionalEditors();
            RefreshInputExpressionPresentation();
            RefreshRepresentationSelections();
            RefreshHeaderState();
            RefreshCollapsedSummary();
            profile.Context.ContextChanged();
            return true;
        }

        public bool ConvertToButtonMapping()
        {
            if (!ButtonsEnabled || Plugins.Count == 0 || Mapping.Plugins.Count != Plugins.Count)
                return false;
            var profile = ProfileViewModel.Profile;
            var template = profile.Context.GetPlugins().FirstOrDefault(plugin =>
                string.Equals(plugin.PluginName, "Button to Button", StringComparison.OrdinalIgnoreCase));
            if (template == null) return false;

            // Prepare every replacement before changing the stored mapping.
            var replacements = new List<Plugin>();
            foreach (var old in Mapping.Plugins)
            {
                var next = profile.Context.PluginManager.GetNewPlugin(template);
                next.SetProfile(profile);
                next.Filters = old.Filters.ToList();
                // Retain already assigned digital destinations, never apply an
                // analogue axis key to a digital button output.
                if (old.OutputCategories.Count > 0 && old.Outputs.Count > 0 &&
                    old.OutputCategories[0].Category == DeviceBindingCategory.Momentary &&
                    old.Outputs[0].IsBound)
                {
                    next.Outputs[0].SetDeviceConfigurationGuid(
                        old.Outputs[0].DeviceConfigurationGuid, false);
                    next.Outputs[0].SetKeyTypeValue(old.Outputs[0].KeyType,
                        old.Outputs[0].KeyValue, old.Outputs[0].KeySubValue);
                }
                else
                {
                    SelectCompatibleOutputDevices(next,
                        old.Outputs.Select(output => output.DeviceConfigurationGuid).ToList());
                }
                replacements.Add(next);
            }

            var inputs = new List<DeviceBinding>();
            foreach (var old in Mapping.DeviceBindings)
            {
                // A button cannot reuse an analogue axis's physical key identity.
                // Retain usable digital inputs; make incompatible inputs unbound.
                var input = old.DeviceBindingCategory == DeviceBindingCategory.Momentary &&
                    old.InputExpressionInputKind == 0 ? old :
                    new DeviceBinding(Mapping.Update, profile, DeviceIoType.Input)
                    {
                        DeviceBindingCategory = DeviceBindingCategory.Momentary
                    };
                input.InputExpressionGroup = inputs.Count;
                input.InputDirectionSlot = 0;
                input.InputExpressionNegated = false;
                input.InputExpressionInputKind = 0;
                input.InputExpressionKindConfigured = true;
                inputs.Add(input);
            }
            if (inputs.Count == 0)
                inputs.Add(new DeviceBinding(Mapping.Update, profile, DeviceIoType.Input)
                {
                    DeviceBindingCategory = DeviceBindingCategory.Momentary
                });

            foreach (var binding in DeviceBindings)
            {
                binding.PropertyChanged -= SummaryBindingOnPropertyChanged;
                binding.Dispose();
            }
            DeviceBindings.Clear();
            foreach (var old in Plugins)
            {
                foreach (var binding in old.DeviceBindings)
                    binding.PropertyChanged -= SummaryBindingOnPropertyChanged;
                old.Dispose();
            }
            Plugins.Clear();

            Mapping.DeviceBindings.Clear();
            Mapping.DeviceBindings.AddRange(inputs);
            Mapping.UseInputExpression = inputs.Count > 1;
            Mapping.Plugins.Clear();
            Mapping.Plugins.AddRange(replacements);
            foreach (var plugin in replacements)
            {
                var viewModel = new PluginViewModel(this, plugin);
                Plugins.Add(viewModel);
                foreach (var binding in viewModel.DeviceBindings)
                    SubscribeSummaryBinding(binding);
            }
            PopulateDeviceBindingsViewModels();
            foreach (var binding in DeviceBindings) SubscribeSummaryBinding(binding);
            profile.Context.ContextChanged();
            RefreshHeaderState();
            RefreshInputExpressionPresentation();
            RefreshPresetAvailability();
            RefreshCollapsedSummary();
            profile.PruneUndefinedFilterReferencesRecursive();
            ProfileViewModel.RefreshFilterReferenceLabels();
            Logger.Info("Replaced saved legacy mapping with Button mapping: " + MappingTitle);
            return true;
        }

        // Swap only the chosen output plugin, never the mapping's shared input bindings.
        public bool ChangeAllOutputTypes(Plugin replacementTemplate)
        {
            if (!ButtonsEnabled || replacementTemplate == null || Plugins.Count == 0) return false;
            var candidates = GetCompatiblePluginOptions();
            if (!candidates.Any(candidate => candidate.Plugin.GetType() == replacementTemplate.GetType()))
                return false;
            var selectingFilter = string.Equals(replacementTemplate.Group, "Filter",
                StringComparison.OrdinalIgnoreCase);
            if (selectingFilter && !OutputIsFilter)
            {
                // Filter is a logical output category. Keep the previously assigned
                // physical destinations in a representation bank, so switching back
                // restores them rather than silently wiping the user's bindings.
                if (Mapping.InactiveOutputRepresentations == null)
                    Mapping.InactiveOutputRepresentations = new List<DirectionalOutputBank>();
                var representation = Mapping.EffectiveOutputRepresentation;
                var count = Mapping.Plugins.Count;
                Mapping.InactiveOutputRepresentations.RemoveAll(bank =>
                    bank.Representation == representation &&
                    bank.Plugins?.Count == count);
                Mapping.InactiveOutputRepresentations.Add(new DirectionalOutputBank
                {
                    Representation = representation,
                    Plugins = Mapping.Plugins.ToList()
                });
            }
            // Apply the common category to each destination. Individual output type changes
            // remain available in the row's optional settings.
            var success = true;
            foreach (var output in Plugins.ToList())
                success &= ChangeOutputType(output, replacementTemplate);
            return success;
        }

        public bool ChangeOutputType(PluginViewModel original, Plugin replacementTemplate)
        {
            if (!ButtonsEnabled || original == null || replacementTemplate == null) return false;
            var index = Plugins.IndexOf(original);
            if (index < 0 || index >= Mapping.Plugins.Count) return false;
            if (!Mapping.GetPluginList().Any(option => option.GetType() == replacementTemplate.GetType()))
                return false;
            if (original.Plugin.GetType() == replacementTemplate.GetType()) return true;
            if (!Mapping.UseInputExpression &&
                !original.Plugin.HasSameInputCategories(replacementTemplate) &&
                !Mapping.EnableInputExpression())
                return false;

            var replacement = ProfileViewModel.Profile.Context.PluginManager.GetNewPlugin(replacementTemplate);
            replacement.SetProfile(ProfileViewModel.Profile);
            replacement.Filters = original.Plugin.Filters.ToList();
            // Preserve the actual destination when both plugins produce the same
            // output category, rather than wiping an already-bound controller axis.
            for (var position = 0; position < Math.Min(original.Plugin.Outputs.Count, replacement.Outputs.Count); position++)
            {
                var previous = original.Plugin.Outputs[position];
                var target = replacement.Outputs[position];
                if (previous.IsBound &&
                    original.Plugin.OutputCategories[position].Category == replacement.OutputCategories[position].Category)
                {
                    target.SetDeviceConfigurationGuid(previous.DeviceConfigurationGuid, false);
                    target.SetKeyTypeValue(previous.KeyType, previous.KeyValue, previous.KeySubValue);
                }
            }
            SelectCompatibleOutputDevices(replacement,
                original.Plugin.Outputs.Select(binding => binding.DeviceConfigurationGuid).ToList());
            var replacementViewModel = new PluginViewModel(this, replacement);
            foreach (var binding in original.DeviceBindings)
                binding.PropertyChanged -= SummaryBindingOnPropertyChanged;
            original.Dispose();
            Mapping.Plugins[index] = replacement;
            Plugins[index] = replacementViewModel;
            foreach (var binding in replacementViewModel.DeviceBindings)
                SubscribeSummaryBinding(binding);
            ProfileViewModel.Profile.PruneUndefinedFilterReferencesRecursive();
            ProfileViewModel.Profile.Context.ContextChanged();
            RefreshHeaderState();
            RefreshCollapsedSummary();
            ProfileViewModel.RefreshFilterReferenceLabels();
            RefreshInputExpressionPresentation();
            // Output selection never implicitly changes the number of inputs.
            // A previous axis choice may have left an unwanted paired input behind.
            if (!HasAxisOutput && Mapping.DeviceBindings.Any(input => input.InputDirectionSlot > 0))
                RestoreBooleanInputsAfterAxis();
            Logger.Info("Changed output type in mapping '" + MappingTitle + "' to " + replacement.PluginName);
            return true;
        }

        private void RestoreBooleanInputsAfterAxis()
        {
            foreach (var group in Mapping.DeviceBindings.Where(input => input.InputDirectionSlot > 0)
                         .GroupBy(input => input.InputExpressionGroup).ToList())
            {
                var primary = group.OrderBy(input => input.InputDirectionSlot).First();
                foreach (var extra in group.Where(input => input != primary).ToList())
                    Mapping.DeviceBindings.Remove(extra);
                if (primary.DeviceBindingCategory != DeviceBindingCategory.Momentary)
                {
                    primary.ClearBinding();
                }
                primary.InputDirectionSlot = 0;
                primary.InputExpressionInputKind = 0;
                primary.InputExpressionKindConfigured = true;
                primary.DeviceBindingCategory = DeviceBindingCategory.Momentary;
            }
            RebuildDirectionalEditors();
        }

        // Preserve the previous device when it supports the new output type. Otherwise
        // choose a genuinely compatible output device instead of the first device in the list.
        private void SelectCompatibleOutputDevices(Plugin plugin, IList<Guid> preferredDevices)
        {
            if (plugin?.Outputs == null) return;
            var profile = ProfileViewModel.Profile;
            var candidates = profile.GetDeviceConfigurationList(DeviceIoType.Output);
            for (var index = 0; index < plugin.Outputs.Count && index < plugin.OutputCategories.Count; index++)
            {
                var category = plugin.OutputCategories[index].Category;
                var compatible = candidates.Where(device => DeviceBindingViewModel.SupportsOutputCategory(
                    device, category, profile.Context)).ToList();
                var preference = preferredDevices != null && index < preferredDevices.Count
                    ? preferredDevices[index] : Guid.Empty;
                var primary = profile.GetPrimaryDeviceConfiguration(DeviceIoType.Output);
                var selected = compatible.FirstOrDefault(device => device.Guid == preference)
                    ?? compatible.FirstOrDefault(device => device.Guid == primary?.Guid)
                    ?? compatible.FirstOrDefault();
                if (selected != null && (!plugin.Outputs[index].IsBound ||
                    plugin.Outputs[index].DeviceConfigurationGuid != selected.Guid))
                    plugin.Outputs[index].SetDeviceConfigurationGuid(selected.Guid, false);
            }
        }

        public void RemoveAllOutputSlots()
        {
            if (!ButtonsEnabled || !IsTypedDirectionalMapping ||
                Mapping.RequiredOutputBindingSlots <= 1) return;
            foreach (var output in Plugins.ToList())
            {
                if (!Mapping.RemovePlugin(output.Plugin)) continue;
                foreach (var binding in output.DeviceBindings)
                    binding.PropertyChanged -= SummaryBindingOnPropertyChanged;
                output.Dispose();
                Plugins.Remove(output);
            }
            RefreshHeaderState();
            RefreshInputExpressionPresentation();
            RefreshRepresentationSelections();
            RefreshCollapsedSummary();
            ProfileViewModel.RefreshFilterReferenceLabels();
        }

        public void RemovePlugin(PluginViewModel pluginViewModel)
        {
            if (!ButtonsEnabled || pluginViewModel?.CanRemove != true) return;
            if (!Mapping.RemovePlugin(pluginViewModel.Plugin)) return;

            pluginViewModel.Dispose();
            Plugins.Remove(pluginViewModel);
            RefreshHeaderState();
            RefreshCollapsedSummary();
            ProfileViewModel.RefreshFilterReferenceLabels();
            // The inputs remain available if the final output is removed.
            RefreshInputExpressionPresentation();
            RefreshRepresentationSelections();
        }

        private void PopulatePlugins(Mapping mapping)
        {
            Plugins = new ObservableCollection<PluginViewModel>();
            foreach (var mappingPlugin in mapping.Plugins)
            {
                Plugins.Add(new PluginViewModel(this, mappingPlugin));
            }
        }

        public void RefreshFilterIndicator()
        {
            RefreshFilterPresentationCache();
            _mappingOutputTypeLabel = GetMappingOutputTypeLabel();

            OnPropertyChanged(nameof(HasFilters));
            OnPropertyChanged(nameof(ReferencedFilters));
            OnPropertyChanged(nameof(HasFilterReferences));
            OnPropertyChanged(nameof(FilterIndicatorToolTip));
            OnPropertyChanged(nameof(DefinedFilterName));
            OnPropertyChanged(nameof(MappingOutputTypeLabel));
        }

        private void RefreshHeaderState()
        {
            OnPropertyChanged(nameof(HasOutputDirectionalCompass));
            OnPropertyChanged(nameof(OutputDirectionalCompassVisibility));
            OnPropertyChanged(nameof(CompassOutputLeft));
            OnPropertyChanged(nameof(CompassOutputRight));
            OnPropertyChanged(nameof(CompassOutputUp));
            OnPropertyChanged(nameof(CompassOutputDown));
            _mappingRouteTokens = BuildMappingRouteTokens(MappingRouteDisplay);
            _mappingOutputTypeLabel = GetMappingOutputTypeLabel();
            RefreshFilterPresentationCache();

            OnPropertyChanged(nameof(IsLegacyAxisMapping));
            OnPropertyChanged(nameof(MappingRoute));
            OnPropertyChanged(nameof(MappingRouteDisplay));
            OnPropertyChanged(nameof(MappingRouteTokens));
            OnPropertyChanged(nameof(MappingOutputTypeLabel));
            OnPropertyChanged(nameof(MappingInputTypeLabel));
            OnPropertyChanged(nameof(MappingOutputCategoryDisplay));
            OnPropertyChanged(nameof(HasAxisOutput));
            OnPropertyChanged(nameof(DirectionalPresetsVisibility));
            OnPropertyChanged(nameof(DefinedFilterName));
            OnPropertyChanged(nameof(ReferencedFilters));
            OnPropertyChanged(nameof(HasFilterReferences));
            OnPropertyChanged(nameof(FilterIndicatorToolTip));
            OnPropertyChanged(nameof(HasFilters));
            OnPropertyChanged(nameof(ControllerStickPresetVisibility));
        }

        private void RefreshFilterPresentationCache()
        {
            _definedFilterName = GetDefinedFilterName();
            _referencedFilters = BuildReferencedFilters();
            _filterIndicatorToolTip = BuildFilterIndicatorToolTip(_referencedFilters);
        }

        private static string FormatMappingRoute(string route, int inputCount, int outputCount)
        {
            if (string.IsNullOrWhiteSpace(route)) return route;
            var sides = Regex.Split(route.Trim(), @"\s+to\s+", RegexOptions.IgnoreCase);
            if (sides.Length != 2)
                return Regex.Replace(route.Trim(), @"\s+to\s+", " → ", RegexOptions.IgnoreCase);

            var input = Regex.Replace(sides[0], @"\bButtons?\b",
                inputCount == 1 ? "Button" : "Buttons", RegexOptions.IgnoreCase);
            var output = Regex.Replace(sides[1], @"\bButtons?\b",
                outputCount == 1 ? "Button" : "Buttons", RegexOptions.IgnoreCase);
            return input + " → " + output;
        }

        private static List<MappingHeaderToken> BuildMappingRouteTokens(string route)
        {
            var result = new List<MappingHeaderToken>();
            if (string.IsNullOrEmpty(route)) return result;

            var parts = Regex.Split(route, @"\b(Button|Buttons|Axis|Axes|Filter|Event|Events|Delta|Deltas|Value|Values|Multiple|None)\b", RegexOptions.IgnoreCase);
            foreach (var part in parts)
            {
                if (string.IsNullOrEmpty(part)) continue;
                result.Add(new MappingHeaderToken(part, IsMappingTypeWord(part) ? part : null));
            }
            return result;
        }

        private static bool IsMappingTypeWord(string value)
        {
            switch (value.ToLowerInvariant())
            {
                case "button":
                case "buttons":
                case "axis":
                case "axes":
                case "filter":
                case "event":
                case "events":
                case "delta":
                case "deltas":
                case "value":
                case "values":
                case "multiple":
                case "none":
                    return true;
                default:
                    return false;
            }
        }


        public DeviceBindingViewModel ResolveCollapsedBinding(BindingVisualDescriptor descriptor, DeviceIoType ioType)
        {
            if (descriptor == null || descriptor.BindingGuid == Guid.Empty) return null;
            IEnumerable<DeviceBindingViewModel> candidates = ioType == DeviceIoType.Input
                ? DeviceBindings
                : Plugins.SelectMany(plugin => plugin.DeviceBindings);
            return candidates.FirstOrDefault(candidate => candidate?.DeviceBinding != null &&
                candidate.DeviceBinding.Guid == descriptor.BindingGuid &&
                candidate.DeviceBinding.DeviceIoType == ioType);
        }

        public bool QuickBindInput(BindingVisualDescriptor descriptor)
        {
            if (!ButtonsEnabled) return false;
            var bindingViewModel = ResolveCollapsedBinding(descriptor, DeviceIoType.Input);
            if (bindingViewModel?.DeviceBinding == null || !bindingViewModel.BindingEnabled) return false;

            // The collapsed row is interactive before its editor exists. Initialise
            // the same device list the expanded editor loads before capture.
            bindingViewModel.EnsureDeviceListLoaded();
            // Button and analogue captures share a logical input expression.
            // Without this, an axis can bind but the old button-only plugin
            // will never evaluate its continuous/threshold source correctly.
            if (Mapping.SupportsNativeInputExpression())
                Mapping.EnableInputExpression();
            StopQuickInputDetection();
            var binding = bindingViewModel.DeviceBinding;
            var bindingManager = binding.Profile?.Context?.BindingManager;
            _quickInputBinding = binding;
            binding.PropertyChanged += QuickInputBindingOnPropertyChanged;
            if (bindingManager != null) bindingManager.PropertyChanged += QuickInputBindingManagerOnPropertyChanged;

            IsQuickInputDetecting = true;
            QuickInputDetectionStatus = "WAITING FOR INPUT — 5.0s";
            QuickInputDetectionProgress = 100;

            try
            {
                binding.DeviceBindingCategory = bindingViewModel.DeviceBindingCategory;
                binding.EnterBindMode(autoDetectInputCategory: true);
                if (!binding.IsInBindMode) StopQuickInputDetection();
                return binding.IsInBindMode;
            }
            catch
            {
                StopQuickInputDetection();
                throw;
            }
        }

        private void QuickInputBindingManagerOnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (!string.Equals(e.PropertyName, nameof(BindingManager.BindModeProgress), StringComparison.Ordinal)) return;
            if (_quickInputBinding == null || !_quickInputBinding.IsInBindMode) return;

            var bindingManager = sender as BindingManager;
            if (bindingManager == null) return;
            QuickInputDetectionProgress = bindingManager.BindModeProgress;
            QuickInputDetectionStatus = "WAITING FOR INPUT — " +
                                        Math.Max(0, bindingManager.BindModeProgress / 20.0).ToString("0.0") + "s";
        }

        private void QuickInputBindingOnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (!string.Equals(e.PropertyName, nameof(DeviceBinding.IsInBindMode), StringComparison.Ordinal)) return;
            var binding = sender as DeviceBinding;
            if (binding == null || !ReferenceEquals(binding, _quickInputBinding) || binding.IsInBindMode) return;
            StopQuickInputDetection();
        }

        private void StopQuickInputDetection()
        {
            var binding = _quickInputBinding;
            if (binding != null)
            {
                binding.PropertyChanged -= QuickInputBindingOnPropertyChanged;
                var bindingManager = binding.Profile?.Context?.BindingManager;
                if (bindingManager != null) bindingManager.PropertyChanged -= QuickInputBindingManagerOnPropertyChanged;
            }

            _quickInputBinding = null;
            IsQuickInputDetecting = false;
            QuickInputDetectionProgress = 0;
            QuickInputDetectionStatus = null;
        }

        public bool UsesPressCaptureForQuickOutput(BindingVisualDescriptor descriptor)
        {
            if (!ButtonsEnabled) return false;
            var bindingViewModel = ResolveCollapsedBinding(descriptor, DeviceIoType.Output);
            var configuration = ResolveQuickOutputConfiguration(bindingViewModel);
            if (configuration?.Device == null) return false;

            return DeviceVisualCatalog.Describe(configuration, ProfileViewModel.Profile, DeviceIoType.Output).Kind ==
                   DeviceVisualKind.Keyboard;
        }

        public List<QuickBindingOption> GetQuickOutputBindingOptions(BindingVisualDescriptor descriptor)
        {
            var result = new List<QuickBindingOption>();
            if (!ButtonsEnabled) return result;

            var bindingViewModel = ResolveCollapsedBinding(descriptor, DeviceIoType.Output);
            if (bindingViewModel?.DeviceBinding == null || !bindingViewModel.BindingEnabled) return result;

            var configuration = ResolveQuickOutputConfiguration(bindingViewModel);
            if (configuration?.Device == null) return result;

            var menu = configuration.Device.GetDeviceBindingMenu(ProfileViewModel.Profile.Context, DeviceIoType.Output);
            foreach (var node in FlattenBindingNodes(menu))
            {
                if (node?.DeviceBindingInfo == null ||
                    node.DeviceBindingInfo.DeviceBindingCategory != bindingViewModel.DeviceBindingCategory) continue;

                var info = node.DeviceBindingInfo;
                var temporary = new DeviceBinding
                {
                    Profile = ProfileViewModel.Profile,
                    DeviceIoType = DeviceIoType.Output,
                    DeviceBindingCategory = bindingViewModel.DeviceBindingCategory,
                    DeviceConfigurationGuid = configuration.Guid,
                    IsBound = true,
                    KeyType = info.KeyType,
                    KeyValue = info.KeyValue,
                    KeySubValue = info.KeySubValue
                };

                result.Add(new QuickBindingOption
                {
                    Title = node.Title,
                    DeviceConfigurationGuid = configuration.Guid,
                    KeyType = info.KeyType,
                    KeyValue = info.KeyValue,
                    KeySubValue = info.KeySubValue,
                    Visual = DeviceVisualCatalog.DescribeBinding(
                        temporary, bindingViewModel.DeviceBindingCategory, ProfileViewModel.Profile)
                });
            }

            return result;
        }

        public bool ApplyQuickOutputBinding(BindingVisualDescriptor descriptor, QuickBindingOption option)
        {
            if (!ButtonsEnabled || option == null) return false;
            var bindingViewModel = ResolveCollapsedBinding(descriptor, DeviceIoType.Output);
            if (bindingViewModel?.DeviceBinding == null || !bindingViewModel.BindingEnabled) return false;

            var binding = bindingViewModel.DeviceBinding;
            binding.DeviceBindingCategory = bindingViewModel.DeviceBindingCategory;
            binding.SetDeviceConfigurationGuid(option.DeviceConfigurationGuid);
            binding.SetKeyTypeValue(option.KeyType, option.KeyValue, option.KeySubValue);
            bindingViewModel.RefreshDeviceList();
            RefreshCollapsedSummary();
            return true;
        }

        private DeviceConfiguration ResolveQuickOutputConfiguration(DeviceBindingViewModel bindingViewModel)
        {
            if (bindingViewModel?.DeviceBinding == null) return null;
            bindingViewModel.EnsureDeviceListLoaded();
            var binding = bindingViewModel.DeviceBinding;
            var configurationGuid = binding.DeviceConfigurationGuid;
            if (configurationGuid == Guid.Empty && bindingViewModel.SelectedDevice != null)
                configurationGuid = bindingViewModel.SelectedDevice.Value;
            if (configurationGuid == Guid.Empty)
                configurationGuid = ProfileViewModel.Profile.GetPrimaryDeviceConfiguration(DeviceIoType.Output)?.Guid ?? Guid.Empty;

            return ProfileViewModel.Profile.GetDeviceConfiguration(DeviceIoType.Output, configurationGuid);
        }

        public async Task<bool> QuickBindOutputAsync(BindingVisualDescriptor descriptor)
        {
            if (!ButtonsEnabled) return false;

            if (IsQuickOutputDetecting)
            {
                _quickOutputDetectionCancellation?.Cancel();
                return false;
            }

            var bindingViewModel = ResolveCollapsedBinding(descriptor, DeviceIoType.Output);
            if (bindingViewModel?.DeviceBinding == null || !bindingViewModel.BindingEnabled) return false;

            var configuration = ResolveQuickOutputConfiguration(bindingViewModel);
            if (configuration?.Device == null)
            {
                QuickOutputDetectionStatus = "Choose an output device first.";
                return false;
            }

            var timeout = TimeSpan.FromSeconds(5);
            var cancellation = new CancellationTokenSource();
            _quickOutputDetectionCancellation = cancellation;
            StartQuickOutputCountdown(timeout);

            try
            {
                var detected = await ProfileViewModel.Profile.Context.DevicesManager.DetectInputControlAsync(
                    bindingViewModel.DeviceBindingCategory, timeout, cancellation.Token);

                if (cancellation.IsCancellationRequested)
                {
                    QuickOutputDetectionStatus = "Input capture cancelled.";
                    return false;
                }
                if (detected == null)
                {
                    QuickOutputDetectionStatus = "No input detected.";
                    return false;
                }

                return ApplyDetectedOutputControl(bindingViewModel, configuration, detected);
            }
            catch (InvalidOperationException exception)
            {
                QuickOutputDetectionStatus = exception.Message;
                return false;
            }
            catch (Exception exception)
            {
                Logger.Error("Quick output binding failed", exception);
                QuickOutputDetectionStatus = "Input capture failed.";
                return false;
            }
            finally
            {
                StopQuickOutputCountdown();
                cancellation.Dispose();
                if (ReferenceEquals(_quickOutputDetectionCancellation, cancellation))
                    _quickOutputDetectionCancellation = null;
            }
        }

        private bool ApplyDetectedOutputControl(DeviceBindingViewModel bindingViewModel,
            DeviceConfiguration outputConfiguration, DetectedInputControl detected)
        {
            if (bindingViewModel?.DeviceBinding == null || outputConfiguration?.Device == null || detected == null)
                return false;

            // A press on an input device is only a selector. Resolve the same named control on the
            // already-selected output device, then bind that output control. The input device itself
            // is deliberately ignored here and can never replace the output-device configuration.
            var outputNode = FlattenBindingNodes(outputConfiguration.Device.GetDeviceBindingMenu(ProfileViewModel.Profile.Context, DeviceIoType.Output))
                .FirstOrDefault(node => node?.DeviceBindingInfo != null &&
                                        node.DeviceBindingInfo.DeviceBindingCategory == bindingViewModel.DeviceBindingCategory &&
                                        string.Equals(node.Title, detected.ControlTitle, StringComparison.CurrentCultureIgnoreCase));

            if (outputNode?.DeviceBindingInfo == null)
            {
                QuickOutputDetectionStatus = string.IsNullOrWhiteSpace(detected.ControlTitle)
                    ? "That input has no matching output control."
                    : "No matching output control named " + detected.ControlTitle + ".";
                return false;
            }

            var info = outputNode.DeviceBindingInfo;
            var binding = bindingViewModel.DeviceBinding;
            binding.DeviceBindingCategory = bindingViewModel.DeviceBindingCategory;
            binding.SetDeviceConfigurationGuid(outputConfiguration.Guid);
            binding.SetKeyTypeValue(info.KeyType, info.KeyValue, info.KeySubValue);
            bindingViewModel.RefreshDeviceList();
            RefreshCollapsedSummary();
            QuickOutputDetectionStatus = "Bound output: " + outputNode.Title;
            return true;
        }

        private void StartQuickOutputCountdown(TimeSpan timeout)
        {
            _quickOutputDetectionDeadlineUtc = DateTime.UtcNow.Add(timeout);
            IsQuickOutputDetecting = true;
            UpdateQuickOutputCountdown();

            _quickOutputDetectionTimer?.Stop();
            _quickOutputDetectionTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _quickOutputDetectionTimer.Tick += QuickOutputDetectionTimerOnTick;
            _quickOutputDetectionTimer.Start();
        }

        private void QuickOutputDetectionTimerOnTick(object sender, EventArgs e)
        {
            UpdateQuickOutputCountdown();
        }

        private void UpdateQuickOutputCountdown()
        {
            var remaining = Math.Max(0, (_quickOutputDetectionDeadlineUtc - DateTime.UtcNow).TotalSeconds);
            QuickOutputDetectionStatus = "Press an input — " + remaining.ToString("0.0") + "s";
            QuickOutputDetectionProgress = Math.Max(0, Math.Min(100, remaining / 5.0 * 100.0));
        }

        private void StopQuickOutputCountdown()
        {
            if (_quickOutputDetectionTimer != null)
            {
                _quickOutputDetectionTimer.Stop();
                _quickOutputDetectionTimer.Tick -= QuickOutputDetectionTimerOnTick;
                _quickOutputDetectionTimer = null;
            }
            IsQuickOutputDetecting = false;
            QuickOutputDetectionProgress = 0;
        }

        private static IEnumerable<DeviceBindingNode> FlattenBindingNodes(IEnumerable<DeviceBindingNode> nodes)
        {
            if (nodes == null) yield break;
            foreach (var node in nodes)
            {
                if (node == null) continue;
                if (node.IsBinding) yield return node;
                foreach (var child in FlattenBindingNodes(node.ChildrenNodes)) yield return child;
            }
        }

        // Build preview rows from the actual bindings. InputConditions is a presentation
        // collection and may be empty for plugin types without native expressions.
        // Never truncate the preview at the first three bindings.
        private BindingVisualDescriptor DescribeCompoundVisual(
            IList<DeviceBindingViewModel> channels, bool output)
        {
            var selected = channels?.FirstOrDefault(candidate => candidate.DeviceBinding.IsBound)
                ?? channels?.FirstOrDefault();
            var visual = DescribeCollapsedBinding(selected);
            if (selected == null) return visual;

            var profile = ProfileViewModel.Profile;
            var binding = selected.DeviceBinding;
            var device = profile.GetDeviceConfiguration(
                output ? DeviceIoType.Output : DeviceIoType.Input,
                binding.DeviceConfigurationGuid);
            var deviceKind = device?.Device == null ? DeviceVisualKind.Unknown :
                DeviceVisualCatalog.Describe(device, profile,
                    output ? DeviceIoType.Output : DeviceIoType.Input).Kind;
            var names = channels.Select(channel => channel.DeviceBinding.IsBound
                ? channel.CompactBindButtonText : string.Empty).ToList();
            while (names.Count < 4) names.Add(string.Empty);
            if (channels.Any(channel =>
                channel.DeviceBindingCategory == DeviceBindingCategory.Range))
            {
                visual.ControlKind = ControlVisualKind.CompoundStick;
                var rightStick = names.Any(name =>
                    name.IndexOf("Right", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.StartsWith("RX", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("RY", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("RS", StringComparison.OrdinalIgnoreCase)) ||
                    channels.Where(channel => channel.DeviceBinding.IsBound &&
                            channel.DeviceBindingCategory == DeviceBindingCategory.Range)
                        .Any(channel => channel.DeviceBinding.KeyValue == 2 ||
                            channel.DeviceBinding.KeyValue == 3);
                visual.ControlLabel = rightStick ? "R" : "L";
            }
            else if (deviceKind == DeviceVisualKind.Keyboard)
            {
                visual.ControlKind = ControlVisualKind.CompoundKeys;
                // Providers may report "Keys,A" or "Keyboard,Keys,A".
                // The physical keycaps must show A, D, W, S, not category paths.
                var keys = names.Select(name =>
                {
                    var label = name ?? string.Empty;
                    var separator = Math.Max(label.LastIndexOf(','), label.LastIndexOf(':'));
                    return (separator >= 0 ? label.Substring(separator + 1) : label).Trim();
                });
                visual.ControlLabel = string.Join("|", keys.Take(4));
            }
            else
            {
                var dpad = names.Any(name =>
                    name.IndexOf("D-pad", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("Dpad", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("POV", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("Hat", StringComparison.OrdinalIgnoreCase) >= 0);
                visual.ControlKind = dpad || !channels.Any(channel =>
                    channel.DeviceBinding.IsBound)
                    ? ControlVisualKind.DPad : ControlVisualKind.CompoundFaceButtons;
                visual.ControlLabel = dpad ? string.Empty : string.Join("|", names.Take(4));
            }
            visual.IsBound = channels.Any(channel => channel.DeviceBinding.IsBound);
            visual.IsCompoundVisual = true;
            visual.CompoundPresetName = visual.IsBound
                ? ResolveCompoundPresetName(channels, output) ?? "Custom 4-Way" : "4-Way";
            visual.ToolTip = visual.CompoundPresetName +
                " — choose a complete D-pad, stick, face-button or keyboard preset";
            visual.JoinSymbol = "+";
            return visual;
        }

        private List<CollapsedConditionVisualRow> BuildCollapsedInputRows()
        {
            var result = new List<CollapsedConditionVisualRow>();
            // The collapsed preview and the inspector must render the SAME logical
            // AND/OR groups. Never flatten all inputs into one row merely because
            // an output plugin reports a different legacy input signature.
            var groups = UsesInputExpression
                ? InputConditions.OrderBy(condition => condition.GroupIndex)
                    .Select(condition => condition.Inputs.ToList()).ToList()
                : new List<List<DeviceBindingViewModel>> { DeviceBindings.ToList() };

            for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                var bindings = groups[groupIndex];
                if (IsTypedDirectionalMapping && InputIsFullStick)
                {
                    var mainChannels = bindings.Where(binding =>
                        !binding.DeviceBinding.IsDirectionalAndCondition).ToList();
                    var guarded = bindings.Where(binding =>
                        binding.DeviceBinding.IsDirectionalAndCondition).ToList();
                    var groupedVisuals = new List<BindingVisualDescriptor>
                    {
                        DescribeCompoundVisual(mainChannels, false)
                    };
                    foreach (var guard in guarded)
                        groupedVisuals.Add(DescribeCollapsedBinding(guard));
                    result.Add(new CollapsedConditionVisualRow
                    {
                        Visuals = groupedVisuals,
                        StartsAlternative = groupIndex > 0
                    });
                    continue;
                }
                var visuals = new List<BindingVisualDescriptor>();
                for (var i = 0; i < bindings.Count; i++)
                {
                    var visual = DescribeCollapsedBinding(bindings[i]);
                    if (i > 0 && bindings[i].DeviceBinding.InputDirectionSlot > 0 &&
                        bindings[i - 1].DeviceBinding.InputDirectionSlot > 0)
                        visual.JoinSymbol = "∨";
                    visuals.Add(visual);
                }
                // Four independent directions are one visual row, not an AND
                // expression split 3+1. Keep every glyph separately clickable.
                var fourWay = Plugins.Count == 1 && Plugins[0].IsFourButtonAxis;
                if (fourWay)
                    for (var index = 1; index < visuals.Count; index++)
                        visuals[index].JoinSymbol = "·";
                var chunkSize = fourWay || bindings.Any(binding =>
                    binding.DeviceBinding.InputDirectionSlot > 0) ? 4 : 3;
                for (var start = 0; start < visuals.Count; start += chunkSize)
                    result.Add(new CollapsedConditionVisualRow
                    {
                        Visuals = visuals.Skip(start).Take(chunkSize).ToList(),
                        StartsAlternative = groupIndex > 0 && start == 0
                    });
            }
            if (result.Count == 0)
                result.Add(new CollapsedConditionVisualRow
                {
                    Visuals = new List<BindingVisualDescriptor>
                    {
                        DeviceVisualCatalog.DescribeBinding(null,
                            DeviceBindingCategory.Momentary, ProfileViewModel.Profile)
                    }
                });
            return result;
        }

        private static List<CollapsedConditionVisualRow> ChunkVisuals(List<BindingVisualDescriptor> visuals)
        {
            var rows = new List<CollapsedConditionVisualRow>();
            for (var start = 0; start < visuals.Count; start += 3)
                rows.Add(new CollapsedConditionVisualRow
                {
                    Visuals = visuals.Skip(start).Take(3).ToList()
                });
            return rows;
        }

        private List<BindingVisualDescriptor> BuildCollapsedInputVisuals()
        {
            var result = new List<BindingVisualDescriptor>();
            foreach (var binding in DeviceBindings.Take(3))
            {
                result.Add(DescribeCollapsedBinding(binding));
            }

            if (result.Count == 0)
            {
                result.Add(DeviceVisualCatalog.DescribeBinding(null, DeviceBindingCategory.Momentary, ProfileViewModel.Profile));
            }
            return result;
        }

        private List<BindingVisualDescriptor> BuildCollapsedOutputVisuals()
        {
            var result = new List<BindingVisualDescriptor>();
            // A 4-Way output is one logical destination even though its runtime
            // routes to two analogue or four digital physical channels.
            if (IsCompoundOutput)
            {
                var channels = Plugins.SelectMany(plugin => plugin.DeviceBindings)
                    .Where(binding => binding.DeviceBinding.DeviceIoType == DeviceIoType.Output)
                    .ToList();
                result.Add(DescribeCompoundVisual(channels, true));
                return result;
            }
            // Present every output route, in the same order as the expanded output rows.
            // Avoid the old primary-plugin-only summary that concealed extra outputs.
            foreach (var plugin in Plugins)
            {
                foreach (var binding in plugin.DeviceBindings)
                    result.Add(DescribeCollapsedBinding(binding));

                if (plugin.DeviceBindings.Count == 0 &&
                    string.Equals(plugin.Plugin.Group, "Filter", StringComparison.OrdinalIgnoreCase))
                {
                    // Filter-definition plugins have no physical output binding. Do not
                    // misleadingly draw an unbound '?' as though they require a device.
                    var filterName = plugin.Plugin.GetDefinedFilterName();
                    result.Add(DeviceVisualCatalog.Filter(
                        string.IsNullOrWhiteSpace(filterName) ? "Set filter name" : filterName));
                }
            }

            if (result.Count == 0)
                result.Add(DeviceVisualCatalog.DescribeBinding(null,
                    DeviceBindingCategory.Momentary, ProfileViewModel.Profile));
            return result;
        }

        private BindingVisualDescriptor DescribeCollapsedBinding(DeviceBindingViewModel viewModel)
        {
            if (viewModel == null) return DeviceVisualCatalog.DescribeBinding(null, DeviceBindingCategory.Momentary, ProfileViewModel.Profile);
            var descriptor = DeviceVisualCatalog.DescribeBinding(
                viewModel.DeviceBinding, viewModel.DeviceBindingCategory, ProfileViewModel.Profile);
            descriptor.IsNegatedInput = viewModel.DeviceBinding?.DeviceIoType == DeviceIoType.Input &&
                viewModel.InputExpressionNegated;
            descriptor.IsCapturing = viewModel.DeviceBinding?.IsInBindMode == true;
            if (descriptor.IsCapturing)
                descriptor.ToolTip = "LISTENING — press Esc or right-click to cancel";
            var binding = viewModel.DeviceBinding;
            if (binding == null || !descriptor.IsBound || descriptor.Device == null) return descriptor;

            var primary = ProfileViewModel.Profile.GetPrimaryDeviceConfiguration(binding.DeviceIoType);
            descriptor.ShowDeviceBadge = primary == null || primary.Guid != binding.DeviceConfigurationGuid;
            return descriptor;
        }

        private string BuildCollapsedSummary()
        {
            var inputs = DeviceBindings.Select(DescribeBinding).Where(value => !string.IsNullOrWhiteSpace(value)).ToList();
            var outputs = new List<string>();

            var primaryPlugin = Plugins.FirstOrDefault();
            if (primaryPlugin != null)
            {
                outputs.AddRange(primaryPlugin.DeviceBindings.Select(DescribeBinding).Where(value => !string.IsNullOrWhiteSpace(value)));
                if (outputs.Count == 0)
                {
                    var filterName = primaryPlugin.Plugin.GetDefinedFilterName();
                    if (!string.IsNullOrWhiteSpace(filterName)) outputs.Add("Filter: " + filterName);
                }
            }

            var inputText = inputs.Count == 0 ? "No input bound" : JoinCompact(inputs);
            var outputText = outputs.Count == 0 ? "No output bound" : JoinCompact(outputs);
            return inputText + "  →  " + outputText;
        }

        private string DescribeBinding(DeviceBindingViewModel viewModel)
        {
            if (viewModel == null || viewModel.DeviceBinding == null) return null;
            var binding = viewModel.DeviceBinding;
            var configuration = ProfileViewModel.Profile.GetDeviceConfiguration(binding.DeviceIoType, binding.DeviceConfigurationGuid);
            var deviceName = configuration?.GetFullTitleForProfile(ProfileViewModel.Profile);
            if (string.IsNullOrWhiteSpace(deviceName)) deviceName = binding.DeviceIoType == DeviceIoType.Input ? "Input device" : "Output device";

            var controlName = binding.IsBound ? binding.BoundName() : "unbound";
            return Truncate(deviceName, 22) + ": " + Truncate(controlName, 24);
        }

        private static string JoinCompact(IList<string> values)
        {
            if (values.Count == 1) return values[0];
            if (values.Count == 2) return values[0] + " + " + values[1];
            return values[0] + " + " + (values.Count - 1) + " more";
        }

        private static string Truncate(string value, int maximumLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maximumLength) return value;
            return value.Substring(0, maximumLength - 1) + "…";
        }

        private string GetMappingOutputTypeLabel()
        {
            var plugin = Mapping?.Plugins?.FirstOrDefault();
            if (plugin == null) return "None";

            if (!string.IsNullOrWhiteSpace(plugin.GetDefinedFilterName())) return "Filter";
            if (plugin.OutputCategories == null || plugin.OutputCategories.Count == 0)
            {
                return string.Equals(plugin.Group, "Filter", StringComparison.OrdinalIgnoreCase) ? "Filter" : "None";
            }

            var category = plugin.OutputCategories[0].Category;
            for (var i = 1; i < plugin.OutputCategories.Count; i++)
            {
                if (plugin.OutputCategories[i].Category != category) return "Multiple";
            }

            switch (category)
            {
                case DeviceBindingCategory.Momentary: return "Button";
                case DeviceBindingCategory.Range: return "Axis";
                case DeviceBindingCategory.Event: return "Event";
                case DeviceBindingCategory.Delta: return "Delta";
                default: return "Value";
            }
        }

        private string GetDefinedFilterName()
        {
            if (Mapping?.Plugins == null) return null;
            foreach (var plugin in Mapping.Plugins)
            {
                var name = plugin.GetDefinedFilterName();
                if (!string.IsNullOrWhiteSpace(name)) return name.Trim();
            }
            return null;
        }

        private static string BuildFilterIndicatorToolTip(IList<FilterReferenceBadge> filters)
        {
            if (filters.Count == 0) return null;
            if (filters.Count == 1) return "Filter: " + filters[0].Name;
            return "Filters: " + string.Join(", ", filters.Select(filter => filter.Name));
        }

        private List<FilterReferenceBadge> BuildReferencedFilters()
        {
            var result = new List<FilterReferenceBadge>();
            if (Mapping?.Plugins == null) return result;
            var seen = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
            foreach (var plugin in Mapping.Plugins)
            {
                if (plugin.Filters == null) continue;
                foreach (var filter in plugin.Filters)
                {
                    if (filter == null || string.IsNullOrWhiteSpace(filter.Name)) continue;
                    var name = filter.Name.Trim();
                    if (!seen.Add(name)) continue;
                    result.Add(new FilterReferenceBadge
                    {
                        Name = name,
                        ToolTip = filter.Negative
                            ? "Uses filter '" + name + "' (inverted)"
                            : "Uses filter '" + name + "'"
                    });
                }
            }
            return result;
        }

        public bool DefinesFilter(string filterName)
        {
            if (string.IsNullOrWhiteSpace(filterName) || Mapping?.Plugins == null) return false;
            foreach (var plugin in Mapping.Plugins)
            {
                if (string.Equals(plugin.GetDefinedFilterName(), filterName, StringComparison.CurrentCultureIgnoreCase)) return true;
            }
            return false;
        }

        public bool ReferencesFilter(string filterName)
        {
            if (string.IsNullOrWhiteSpace(filterName) || Mapping?.Plugins == null) return false;
            foreach (var plugin in Mapping.Plugins)
            {
                if (plugin.Filters == null) continue;
                foreach (var filter in plugin.Filters)
                {
                    if (filter != null && string.Equals(filter.Name, filterName, StringComparison.CurrentCultureIgnoreCase)) return true;
                }
            }
            return false;
        }

        public void SetFilterHighlight(bool definition, bool reference)
        {
            IsFilterDefinitionHighlighted = definition;
            IsFilterReferenceHighlighted = reference;
        }

        public async void Rename()
        {
            var dialog = new StringDialog("Rename mapping", "Mapping name", Mapping.Title);
            var result = (bool?)await DialogHost.Show(dialog, ProfileViewModel.ProfileDialogIdentifier);
            if (result == null || !result.Value) return;

            var oldTitle = Mapping.Title;
            Mapping.Rename(dialog.Value);
            Logger.Info("Mapping renamed: '" + oldTitle + "' -> '" + Mapping.Title + "'");
            OnPropertyChanged(nameof(MappingTitle));
        }


        // Input type changes select a real remapper with the same output signature.
        // They never masquerade as output changes, and cannot silently mutate multi-output mappings.
        public string MappingInputTypeLabel
        {
            get
            {
                var inputs = Mapping?.Plugins?.FirstOrDefault()?.InputCategories;
                if (inputs == null || inputs.Count == 0) return "None";
                var kinds = inputs.Select(item => item.Category).Distinct().ToList();
                if (kinds.Count != 1) return "Multiple";
                switch (kinds[0])
                {
                    case DeviceBindingCategory.Momentary:
                        return (Mapping?.DeviceBindings?.Count ?? 0) == 1 ? "Button" : "Buttons";
                    case DeviceBindingCategory.Range: return "Axis";
                    case DeviceBindingCategory.Event: return "Event";
                    case DeviceBindingCategory.Delta: return "Delta";
                    default: return "Value";
                }
            }
        }

        // A category change must preserve every existing output, including mappings with
        // several independently configured destinations.
        private bool HasReplacementForEachOutput(Plugin template, IList<Plugin> catalog)
        {
            if (template == null || Mapping?.Plugins == null) return false;
            var inputSignature = template.InputCategories.Select(c => c.Category).ToList();
            return Mapping.Plugins.All(original => catalog.Any(candidate =>
                candidate.InputCategories.Select(c => c.Category).SequenceEqual(inputSignature) &&
                candidate.OutputCategories.Select(c => c.Category)
                    .SequenceEqual(original.OutputCategories.Select(c => c.Category))));
        }

        public List<SimplePluginViewModel> GetCompatibleInputPluginOptions()
        {
            if (Mapping?.Plugins?.Count == 0) return new List<SimplePluginViewModel>();
            var current = Mapping.Plugins[0];
            var catalog = ProfileViewModel.Profile.Context.GetPlugins().ToList();
            return catalog
                .Where(candidate => candidate.InputCategories.Count > 0 &&
                    candidate.OutputCategories.Select(c => c.Category)
                        .SequenceEqual(current.OutputCategories.Select(c => c.Category)) &&
                    HasReplacementForEachOutput(candidate, catalog))
                .Select(candidate => new SimplePluginViewModel(candidate))
                .OrderBy(candidate => candidate.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        public bool ChangeInputType(PluginViewModel original, Plugin replacementTemplate)
        {
            if (!ButtonsEnabled || original == null || replacementTemplate == null ||
                Plugins.Count == 0 || Plugins[0] != original ||
                Mapping.Plugins.Count != Plugins.Count) return false;
            if (!GetCompatibleInputPluginOptions().Any(option =>
                option.Plugin.GetType() == replacementTemplate.GetType())) return false;
            if (original.Plugin.GetType() == replacementTemplate.GetType() &&
                Mapping.Plugins.All(p => p.InputCategories.Select(c => c.Category)
                    .SequenceEqual(replacementTemplate.InputCategories.Select(c => c.Category))))
                return true;

            var profile = ProfileViewModel.Profile;
            var catalog = profile.Context.GetPlugins().ToList();
            var inputSignature = replacementTemplate.InputCategories.Select(c => c.Category).ToList();
            var replacements = new List<Plugin>();
            for (var i = 0; i < Mapping.Plugins.Count; i++)
            {
                var existing = Mapping.Plugins[i];
                var choice = i == 0 ? replacementTemplate : catalog.FirstOrDefault(candidate =>
                    candidate.InputCategories.Select(c => c.Category).SequenceEqual(inputSignature) &&
                    candidate.OutputCategories.Select(c => c.Category)
                        .SequenceEqual(existing.OutputCategories.Select(c => c.Category)));
                if (choice == null) return false;
                var next = profile.Context.PluginManager.GetNewPlugin(choice);
                next.SetProfile(profile);
                if (next.Outputs.Count != existing.Outputs.Count) return false;
                for (var j = 0; j < next.Outputs.Count; j++)
                    next.Outputs[j] = existing.Outputs[j];
                next.Filters = existing.Filters.ToList();
                replacements.Add(next);
            }

            var oldInputCategories = original.Plugin.InputCategories.Select(c => c.Category).ToList();
            foreach (var binding in DeviceBindings)
            {
                binding.PropertyChanged -= SummaryBindingOnPropertyChanged;
                binding.Dispose();
            }
            DeviceBindings.Clear();
            foreach (var output in Plugins)
            {
                foreach (var binding in output.DeviceBindings)
                    binding.PropertyChanged -= SummaryBindingOnPropertyChanged;
                output.Dispose();
            }

            Mapping.UseInputExpression = false;
            var former = Mapping.DeviceBindings.ToList();
            Mapping.DeviceBindings.Clear();
            for (var i = 0; i < inputSignature.Count; i++)
            {
                var previous = i < former.Count && i < oldInputCategories.Count &&
                    oldInputCategories[i] == inputSignature[i] ? former[i] : null;
                if (previous != null)
                {
                    previous.SetInputExpressionGroup(0);
                    previous.SetInputExpressionNegated(false);
                    Mapping.DeviceBindings.Add(previous);
                }
                else
                {
                    Mapping.DeviceBindings.Add(new DeviceBinding(Mapping.Update, profile, DeviceIoType.Input)
                    {
                        DeviceConfigurationGuid = profile.GetPrimaryDeviceConfiguration(DeviceIoType.Input)?.Guid ?? Guid.Empty
                    });
                }
            }

            Mapping.Plugins.Clear();
            Mapping.Plugins.AddRange(replacements);
            Plugins.Clear();
            foreach (var next in replacements)
            {
                var viewModel = new PluginViewModel(this, next);
                Plugins.Add(viewModel);
                foreach (var binding in viewModel.DeviceBindings)
                    SubscribeSummaryBinding(binding);
            }
            PopulateDeviceBindingsViewModels();
            foreach (var binding in DeviceBindings) SubscribeSummaryBinding(binding);
            profile.Context.ContextChanged();
            RefreshHeaderState();
            RefreshInputExpressionPresentation();
            RefreshPresetAvailability();
            RefreshCollapsedSummary();
            profile.PruneUndefinedFilterReferencesRecursive();
            ProfileViewModel.RefreshFilterReferenceLabels();
            Logger.Info("Changed input category in mapping '" + MappingTitle + "' to " + replacementTemplate.PluginName);
            return true;
        }

        public List<SimplePluginViewModel> GetCompatiblePluginOptions()
        {
            return Mapping.GetPluginList()
                .Select(plugin => new SimplePluginViewModel(plugin))
                .OrderBy(plugin => plugin.OutputTypeOrder)
                .ThenBy(plugin => plugin.OutputType, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(plugin => plugin.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            StopQuickInputDetection();
            _quickOutputDetectionCancellation?.Cancel();
            StopQuickOutputCountdown();

            foreach (var binding in DeviceBindings ?? new ObservableCollection<DeviceBindingViewModel>())
            {
                binding.PropertyChanged -= SummaryBindingOnPropertyChanged;
                binding.Dispose();
            }

            foreach (var plugin in Plugins ?? new ObservableCollection<PluginViewModel>())
            {
                foreach (var binding in plugin.DeviceBindings) binding.PropertyChanged -= SummaryBindingOnPropertyChanged;
                plugin.Dispose();
            }
        }

        public async void AddPlugin()
        {
            var dialog = new AddMappingPluginDialog(this);
            var result = (AddMappingPluginDialogViewModel)await DialogHost.Show(dialog, ProfileViewModel.ProfileDialogIdentifier);
            if (result?.SelectedPlugin == null) return;

            AddPlugin(result.SelectedPlugin.Plugin);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
