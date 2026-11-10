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
        private void RefreshDirectionalAvailability()
        {
            // First assignment and clearing a trigger can change the Button
            // glyph without changing its selected logical representation.
            OnPropertyChanged(nameof(InputButtonChoiceGlyph));
            OnPropertyChanged(nameof(OutputButtonChoiceGlyph));
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
            ? "4-Way" : Mapping.EffectiveInputRepresentation == 2 ? "Axis" : "Button";
        public string OutputDirectionalGroupTitle => Mapping.EffectiveOutputRepresentation == 1
            ? "4-Way" : Mapping.EffectiveOutputRepresentation == 2 ? "Axis" : "Button";
        public Visibility TypedDirectionalVisibility => IsTypedDirectionalMapping
            ? Visibility.Visible : Visibility.Collapsed;
        public Visibility LegacyOutputAddVisibility => Visibility.Visible;
        public Visibility FourWayOutputDeviceVisibility =>
            (IsCompoundOutput || HasOutputDirectionalCompass) && Plugins?.Count > 0
                ? Visibility.Visible : Visibility.Collapsed;
        public Visibility CompoundOutputRemoveVisibility =>
            ButtonsEnabled && IsTypedDirectionalMapping &&
            Plugins != null && Plugins.Count > Mapping.RequiredOutputBindingSlots
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
            // Capturing an AND guard or an individual 4-Way direction must
            // not replace the mapping's complete logical input control.
            if (input.IsDirectionalAndCondition ||
                (IsTypedDirectionalMapping && InputIsFullStick)) return false;
            var physical = input.DeviceBindingCategory;
            if (physical != DeviceBindingCategory.Momentary &&
                physical != DeviceBindingCategory.Range) return false;

            var physicalRepresentation = physical == DeviceBindingCategory.Momentary ? 3 :
                DeviceVisualCatalog.IsTriggerBinding(input, ProfileViewModel.Profile) ? 4 : 2;
            if (!IsTypedDirectionalMapping && physical == DeviceBindingCategory.Range)
            {
                return SetInputRepresentation(physicalRepresentation);
            }
            if (IsTypedDirectionalMapping)
            {
                var requested = Mapping.EffectiveInputRepresentation != physicalRepresentation
                    ? physicalRepresentation : 0;
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

        public bool InferOutputTypeFromAssignment(DeviceBindingCategory physical, bool isTrigger)
        {
            if (!ButtonsEnabled ||
                (physical != DeviceBindingCategory.Momentary &&
                 physical != DeviceBindingCategory.Range)) return false;
            // A 4-Way destination consists of multiple physical controls.
            // Rebinding an individual direction never changes the entire type.
            if (IsTypedDirectionalMapping && OutputIsFullStick) return true;
            var representation = physical == DeviceBindingCategory.Momentary ? 3 :
                isTrigger ? 4 : 2;
            if (!IsTypedDirectionalMapping && physical == DeviceBindingCategory.Range)
            {
                return SetOutputRepresentation(representation);
            }
            if (IsTypedDirectionalMapping &&
                Mapping.EffectiveOutputRepresentation != representation)
                return SetOutputRepresentation(representation);
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
            var capacity = Mapping.DirectionalCapacity;
            var compoundInput = Mapping.CompoundDigitalInput;
            var inputRepresentation = Mapping.InputRepresentation;
            var outputRepresentation = Mapping.OutputRepresentation;
            var inputExpression = Mapping.UseInputExpression;
            var inputs = Mapping.DeviceBindings;
            var plugins = Mapping.Plugins;
            var inputBanks = Mapping.InactiveInputRepresentations;
            var outputBanks = Mapping.InactiveOutputRepresentations;
            Mapping.InactiveInputRepresentations = inputBanks?.ToList();
            Mapping.InactiveOutputRepresentations = outputBanks?.ToList();

            var committed = false;
            try
            {
                if (!ApplyRepresentationModel(value, inputSide)) return false;
                if (ReferenceEquals(inputs, Mapping.DeviceBindings) &&
                    ReferenceEquals(plugins, Mapping.Plugins)) return true;
                committed = true;
            }
            catch (Exception exception)
            {
                Logger.Error("Unable to prepare a mapping representation change.", exception);
                return false;
            }
            finally
            {
                if (!committed)
                {
                    Mapping.DirectionalCapacity = capacity;
                    Mapping.CompoundDigitalInput = compoundInput;
                    Mapping.InputRepresentation = inputRepresentation;
                    Mapping.OutputRepresentation = outputRepresentation;
                    Mapping.UseInputExpression = inputExpression;
                    Mapping.DeviceBindings = inputs;
                    Mapping.Plugins = plugins;
                    Mapping.InactiveInputRepresentations = inputBanks;
                    Mapping.InactiveOutputRepresentations = outputBanks;
                }
            }

            if (!ReferenceEquals(plugins, Mapping.Plugins)) RebuildDirectionalOutputEditors();
            if (!ReferenceEquals(inputs, Mapping.DeviceBindings)) RebuildDirectionalEditors();
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
            OnPropertyChanged(nameof(IsLegacyButtonAxisPair));
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

        private bool ApplyRepresentationModel(int value, bool inputSide)
        {
            var legacyAxisPair = IsLegacyButtonAxisPair;
            var legacySingleAxis = IsLegacySingleButtonAxis;
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
                if (!SwitchDirectionalInputs(value, false, legacyAxisPair, legacySingleAxis)) return false;
                if (capacityChanged || Mapping.Plugins.Count !=
                    Mapping.RequiredOutputBindingSlots)
                {
                    var otherType = Mapping.EffectiveOutputRepresentation;
                    // Materialise the inferred representation without changing
                    // the visible choice; keep the original legacy output bank.
                    if (!SwitchDirectionalOutputs(otherType, false)) return false;
                }
            }
            else
            {
                if (Mapping.OutputRepresentation == value && !capacityChanged &&
                    !OutputIsFilter &&
                    Mapping.Plugins.Count == Mapping.RequiredOutputBindingSlots)
                    return true;
                if (!SwitchDirectionalOutputs(value, false)) return false;
                if (capacityChanged || (value == 2 && (legacyAxisPair || legacySingleAxis)) ||
                    Mapping.DeviceBindings.Count != Mapping.RequiredInputBindingSlots)
                {
                    var otherType = Mapping.EffectiveInputRepresentation;
                    // Keep the old input representation in its legacy bank.
                    if (!SwitchDirectionalInputs(otherType, false, legacyAxisPair, legacySingleAxis)) return false;
                }
            }
            return true;
        }

        private bool SwitchDirectionalInputs(int representation, bool rebuildEditors = true,
            bool legacyAxisPair = false, bool legacySingleAxis = false)
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
            if (legacyAxisPair && oldGroups.Count == 1)
            {
                // The positional plugin reads Positive then Negative. The directional
                // engine reads Negative then Positive. Reverse the view, not the saved bank.
                oldGroups[0].Reverse();
            }
            if (legacySingleAxis && Mapping.UseInputExpression)
            {
                // A legacy one-button expression ANDs additional inputs with its driver.
                // Preserve those gates when materialising the negative/positive pair.
                for (var index = 0; index < oldGroups.Count; index++)
                {
                    var group = oldGroups[index];
                    if (group.Count <= 1) continue;
                    foreach (var source in group.Skip(1))
                    {
                        var guard = HidWizards.UCR.Core.Context.DeepXmlClone(source);
                        guard.Profile = profile;
                        guard.DeviceIoType = DeviceIoType.Input;
                        guard.DeviceBindingCategory = source.DeviceBindingCategory;
                        guard.InputExpressionGroup = index;
                        guard.IsDirectionalAndCondition = true;
                        guard.InputDirectionSlot = 0;
                        guards.Add(guard);
                    }
                    oldGroups[index] = group.Take(1).ToList();
                }
            }

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
                        // A captured trigger can be in either former Button slot.
                        // Reuse the actual physical Range assignment rather than
                        // replace it with an unbound trigger when switching banks.
                        if (representation == 4 && capacity != 4)
                        {
                            var triggerIndex = sources.FindIndex(source =>
                                source.IsBound &&
                                DeviceVisualCatalog.IsTriggerBinding(source, profile));
                            if (triggerIndex >= 0) previousIndex = triggerIndex;
                        }
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
                            (sources[previousIndex].IsBound || legacyAxisPair || legacySingleAxis) &&
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
            if (rebuildEditors) RebuildDirectionalEditors();
            return true;
        }

        private bool SwitchDirectionalOutputs(int representation, bool rebuildEditors = true)
        {
            var profile = ProfileViewModel.Profile;
            var required = Mapping.CompoundDigitalOutput && representation == 1 &&
                Mapping.EffectiveDirectionalCapacity == 4 ? 4 :
                Mapping.RequiredBindingSlots(Mapping.EffectiveDirectionalCapacity, representation);
            if (required < 1) return false;
            if (Mapping.InactiveOutputRepresentations == null)
                Mapping.InactiveOutputRepresentations = new List<DirectionalOutputBank>();
            var previous = Mapping.Plugins.ToList();
            var priorType = Mapping.OutputRepresentation;
            var isButton = representation == 3 || (Mapping.CompoundDigitalOutput && representation == 1);
            var category = isButton ? DeviceBindingCategory.Momentary : DeviceBindingCategory.Range;
            var bank = Mapping.InactiveOutputRepresentations.FirstOrDefault(item =>
                item.FilterRestoreKey == Guid.Empty && item.Representation == representation &&
                item.Plugins?.Count == required && item.Plugins.All(plugin => HasPhysicalOutputCategory(plugin, category)));
            var restored = previous.Count == required
                ? RestoreLegacyPhysicalOutputs(previous, category, representation) : null;
            List<Plugin> next;
            if (restored != null)
            {
                next = restored;
            }
            else if (bank != null)
            {
                next = bank.Plugins;
                Mapping.InactiveOutputRepresentations.Remove(bank);
            }
            else
            {
                // As above, capacity changes require a complete destination bank.
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
                        if (previousPlugin.PluginName == "Buttons to Axis" &&
                            previousPlugin.GetType().GetProperty("Invert")?.GetValue(previousPlugin) is bool invertAxis)
                            plugin.GetType().GetProperty("Invert")?.SetValue(plugin, invertAxis);
                        if (previousPlugin.Outputs.Count > 0 && plugin.Outputs.Count > 0 &&
                            previousPlugin.OutputCategories[0].Category ==
                                plugin.OutputCategories[0].Category &&
                            previousPlugin.Outputs[0].IsBound)
                        {
                            var source = previousPlugin.Outputs[0];
                            CopyPhysicalOutput(source, plugin.Outputs[0]);
                            // A single button already assigned to the negative half still
                            // drives that side when the typed bank uses an Axis to Axis plugin.
                            if (source.OutputAxisDirection < 0 &&
                                previousPlugin.InputCategories.FirstOrDefault().Category == DeviceBindingCategory.Momentary)
                            {
                                var invert = plugin.GetType().GetProperty("Invert");
                                if (invert?.PropertyType == typeof(bool) && invert.CanWrite)
                                    invert.SetValue(plugin, true, null);
                            }
                        }
                        SelectCompatibleOutputDevices(plugin,
                            previousPlugin.Outputs.Select(output =>
                                output.DeviceConfigurationGuid).ToList());
                    }
                    next.Add(plugin);
                }
            }

            if (previous.Any(plugin => !IsFilterOutput(plugin)))
            {
                Mapping.InactiveOutputRepresentations.RemoveAll(item =>
                    item.FilterRestoreKey == Guid.Empty && item.Representation == priorType &&
                    item.Plugins != null && item.Plugins.Count == previous.Count);
                Mapping.InactiveOutputRepresentations.Add(new DirectionalOutputBank
                {
                    Representation = priorType,
                    Plugins = previous
                });
            }
            Mapping.Plugins = next;
            Mapping.OutputRepresentation = representation;
            if (rebuildEditors) RebuildDirectionalOutputEditors();
            return true;
        }

        private void RebuildDirectionalOutputEditors()
        {
            foreach (var vm in Plugins.ToList())
            {
                foreach (var binding in vm.DeviceBindings)
                    binding.PropertyChanged -= SummaryBindingOnPropertyChanged;
                vm.Dispose();
            }
            Plugins.Clear();
            foreach (var plugin in Mapping.Plugins)
            {
                plugin.SetProfile(ProfileViewModel.Profile);
                var vm = new PluginViewModel(this, plugin);
                Plugins.Add(vm);
                foreach (var binding in vm.DeviceBindings)
                    SubscribeSummaryBinding(binding);
            }
            RefreshHeaderState();
            ProfileViewModel.RefreshFilterReferenceLabels();
        }
    }
}
