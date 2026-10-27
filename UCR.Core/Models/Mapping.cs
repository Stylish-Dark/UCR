using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Xml.Serialization;
using HidWizards.UCR.Core.Models.Binding;
using HidWizards.UCR.Core.Models.Subscription;

namespace HidWizards.UCR.Core.Models
{
    public enum MappingRelationship
    {
        None,
        Button,
        OneWay,
        TwoWay,
        FourWay,
        ContinuousAxis,
        Mixed,
        Other
    }

    public class Mapping
    {
        // Relationship is derived from the effective input and output topology.
        // It must not become a second, conflicting source of persisted truth.
        [XmlIgnore]
        public MappingRelationship Relationship
        {
            get
            {
                if (Plugins == null || Plugins.Count == 0) return MappingRelationship.None;
                var categories = Plugins.SelectMany(plugin => plugin.OutputCategories)
                    .Select(output => output.Category).Distinct().ToList();
                if (categories.Count != 1) return MappingRelationship.Mixed;
                if (categories[0] == DeviceBindingCategory.Momentary)
                    return MappingRelationship.Button;
                if (categories[0] != DeviceBindingCategory.Range)
                    return MappingRelationship.Other;

                var directionCount = DeviceBindings?.Count(binding =>
                    binding.InputDirectionSlot > 0) ?? 0;
                var physicalChannels = Plugins[0].InputCategories?.Count ?? 0;
                var outputChannels = Plugins.Sum(plugin => plugin.OutputCategories.Count);
                if (directionCount >= 4 ||
                    directionCount == 0 && physicalChannels >= 4 && outputChannels >= 2)
                    return MappingRelationship.FourWay;
                if (directionCount >= 2 || directionCount == 0 && physicalChannels == 2)
                    return MappingRelationship.TwoWay;
                if (Plugins[0].InputCategories != null &&
                    Plugins[0].InputCategories.All(input => input.Category == DeviceBindingCategory.Momentary))
                    return MappingRelationship.OneWay;
                return MappingRelationship.ContinuousAxis;
            }
        }

        /* Persistence */
        [XmlAttribute]
        public string Title { get; set; }
        public List<DeviceBinding> DeviceBindings { get; set; }
        public List<Plugin> Plugins { get; set; }

        [XmlAttribute]
        [DefaultValue(false)]
        public bool UseInputExpression { get; set; }

        /* Runtime */
        private Profile Profile { get; set; }
        private List<short> InputCache { get; set; }
        private List<CallbackMultiplexer> Multiplexer { get; set; }
        private short? _lastInputExpressionValue;
        private Dictionary<Plugin, short> _lastDigitalByPlugin = new Dictionary<Plugin, short>();
        private FilterState _subscribedFilterState;
        

        internal bool IsShadowMapping { get; set; }
        internal int ShadowDeviceNumber { get; set; }
        internal int PossibleShadowClones => CountPossibleShadowClones();
        internal FilterState FilterState { get; set; }
        internal Guid RuntimeScopeGuid { get; private set; }

        private int CountPossibleShadowClones()
        {
            var usedDeviceConfigurations = new List<DeviceConfiguration>();

            foreach (var deviceBinding in DeviceBindings)
            {
                if (!deviceBinding.IsBound) continue;

                var deviceConfiguration = Profile.GetDeviceConfiguration(DeviceIoType.Input, deviceBinding.DeviceConfigurationGuid);
                if (deviceConfiguration != null) usedDeviceConfigurations.Add(deviceConfiguration);
            }

            if (usedDeviceConfigurations.Count == 0) return 0;

            return usedDeviceConfigurations
                .Select(deviceConfiguration => deviceConfiguration.ShadowDevices)
                .Max(shadowDevices => shadowDevices.Count);
        }

        [XmlIgnore]
        public string FullTitle
        {
            get
            {
                var mapping = GetOverridenMapping();
                return mapping != null ? $"{Title} (Overrides {mapping.Profile.Title})" : Title;
            }
        }

        public Mapping()
        {
            DeviceBindings = new List<DeviceBinding>();
            Plugins = new List<Plugin>();
            
            IsShadowMapping = false;
            ShadowDeviceNumber = 0;
        }

        public Mapping(Profile profile, string title) : this()
        {
            Profile = profile;
            Title = title;
        }

        public void Rename(string title)
        {
            Title = title;
            Profile.Context.ContextChanged();
        }

        internal bool IsBound()
        {
            if (DeviceBindings.Count == 0) return false;
            var result = true;
            foreach (var deviceBinding in DeviceBindings)
            {
                result &= deviceBinding.IsBound;
            }
            return result;
        }

        internal void PrepareMapping(FilterState filterState, Guid runtimeScopeGuid)
        {
            InputCache = new List<short>();
            DeviceBindings.ForEach(_ => InputCache.Add(0));
            Multiplexer = new List<CallbackMultiplexer>();
            for (var i = 0; i < DeviceBindings.Count; i++)
            {
                var cm = new CallbackMultiplexer(InputCache, i, Update);
                Multiplexer.Add(cm);
                DeviceBindings[i].Callback = cm.Update;
                DeviceBindings[i].CurrentValue = 0;
            }

            _lastInputExpressionValue = null;
            _lastDigitalByPlugin.Clear();
            StopListeningForFilters();
            FilterState = filterState;
            _subscribedFilterState = filterState;
            if (_subscribedFilterState != null)
                _subscribedFilterState.FilterStateChangedEvent += OnFilterStateChanged;
            RuntimeScopeGuid = runtimeScopeGuid;
            Plugins.ForEach(p =>
            {
                p.RuntimeMapping = this;
                p.WasBlockedByFilter = false;
            });
        }

        internal Mapping GetOverridenMapping()
        {
            var parentProfile = Profile?.ParentProfile;
            while (parentProfile != null)
            {
                foreach (var mapping in parentProfile.Mappings ?? Enumerable.Empty<Mapping>())
                {
                    if (mapping != null &&
                        string.Equals(Title, mapping.Title, StringComparison.CurrentCultureIgnoreCase))
                    {
                        return mapping;
                    }
                }

                parentProfile = parentProfile.ParentProfile;
            }

            return null;
        }
        
        internal string GetRuntimeFilterKey(string filterName)
        {
            return GetRuntimeFilterKey(RuntimeScopeGuid, filterName);
        }

        public static string GetRuntimeFilterKey(Guid runtimeScopeGuid, string filterName)
        {
            if (string.IsNullOrWhiteSpace(filterName)) return null;
            return runtimeScopeGuid.ToString("N") + ":" + filterName.Trim().ToLowerInvariant();
        }

        public void Update(short value)
        {
            if (!UseInputExpression)
            {
                foreach (var plugin in Plugins)
                {
                    if (plugin.IsFiltered())
                    {
                        NeutralizeFilteredPlugin(plugin);
                        continue;
                    }
                    plugin.WasBlockedByFilter = false;
                    var values = InputCache.ToArray();
                    // Preserve positional inputs for multi-channel legacy plugins, while
                    // allowing each physical slot to bind a different signal category.
                    for (var i = 0; i < values.Length && i < plugin.InputCategories.Count; i++)
                        values[i] = AdaptPositionalInput(values[i], DeviceBindings[i],
                            plugin.InputCategories[i].Category);
                    plugin.Update(values);
                }
                return;
            }

            // Each output can consume either the legacy aggregate expression or
            // a dedicated input sub-mapping. Independent Boolean destinations
            // must detect their own transitions, even if another group is held.
            var legacyDigital = EvaluateInputExpression(InputCache);
            var legacyAnalogue = EvaluateAnalogueInputExpression(InputCache);
            var legacyChanged = !_lastInputExpressionValue.HasValue ||
                                _lastInputExpressionValue.Value != legacyDigital;
            _lastInputExpressionValue = legacyDigital;

            var globalFourWay = DeviceBindings.Any(b => b.InputDirectionSlot >= 3);
            var legacyRangeIndex = 0;
            var rangeIndexesByGroup = new Dictionary<int, int>();
            foreach (var plugin in Plugins)
            {
                if (plugin.InputCategories.Count != 1) continue;
                var isRange = plugin.InputCategories[0].Category == DeviceBindingCategory.Range;
                var group = plugin.InputGroupIndex;
                var digital = group >= 0 ? EvaluateInputExpression(InputCache, group) : legacyDigital;
                var analogue = group >= 0 ? EvaluateAnalogueInputExpression(InputCache, group) : legacyAnalogue;
                var scopedChanged = !_lastDigitalByPlugin.ContainsKey(plugin) ||
                                    _lastDigitalByPlugin[plugin] != digital;
                _lastDigitalByPlugin[plugin] = digital;

                var outputValue = analogue;
                if (isRange)
                {
                    var axisIndex = legacyRangeIndex;
                    if (group >= 0)
                    {
                        rangeIndexesByGroup.TryGetValue(group, out axisIndex);
                        rangeIndexesByGroup[group] = axisIndex + 1;
                    }
                    else legacyRangeIndex++;
                    var isFourWay = group >= 0
                        ? DeviceBindings.Any(b => b.InputExpressionGroup == group && b.InputDirectionSlot >= 3)
                        : globalFourWay;
                    if (isFourWay)
                        outputValue = EvaluateDirectionalAxis(InputCache,
                            axisIndex % 2, group);
                }
                if (plugin.IsFiltered())
                {
                    NeutralizeFilteredPlugin(plugin);
                    continue;
                }
                var returningFromFilter = plugin.WasBlockedByFilter;
                plugin.WasBlockedByFilter = false;
                if (!isRange && !(group >= 0 ? scopedChanged : legacyChanged) && !returningFromFilter)
                    continue;
                plugin.Update(new[] { isRange ? outputValue : digital });
            }
        }

        // Disabling a mapping must release any held output immediately. Otherwise
        // pressing a filter key while a button is held leaves the virtual key stuck.
        // One-shot Event and relative Delta outputs have no neutral held state.
        private static void NeutralizeFilteredPlugin(Plugin plugin)
        {
            if (plugin.WasBlockedByFilter) return;
            plugin.WasBlockedByFilter = true;
            for (var i = 0; i < plugin.Outputs.Count && i < plugin.OutputCategories.Count; i++)
            {
                var category = plugin.OutputCategories[i].Category;
                if ((category == DeviceBindingCategory.Momentary ||
                     category == DeviceBindingCategory.Range) &&
                    plugin.Outputs[i].CurrentValue != 0)
                    plugin.Outputs[i].WriteOutput(0);
            }
        }

        internal void StopListeningForFilters()
        {
            if (_subscribedFilterState == null) return;
            _subscribedFilterState.FilterStateChangedEvent -= OnFilterStateChanged;
            _subscribedFilterState = null;
        }

        // Filter transitions affect held outputs even when no physical input moves.
        // Refresh only affected outputs: replaying all mappings could inadvertently
        // retrigger unrelated one-shot event outputs, such as wheel increments.
        private void OnFilterStateChanged(string name, bool active)
        {
            if (InputCache == null || Plugins == null) return;
            var analogueIndex = 0;
            var rangeIndexesByGroup = new Dictionary<int, int>();
            foreach (var plugin in Plugins)
            {
                if (plugin.InputCategories == null) continue;
                var group = plugin.InputGroupIndex;
                var isRange = UseInputExpression && plugin.InputCategories.Count == 1 &&
                    plugin.InputCategories[0].Category == DeviceBindingCategory.Range;
                var axis = 0;
                if (isRange)
                {
                    if (group >= 0)
                    {
                        rangeIndexesByGroup.TryGetValue(group, out axis);
                        rangeIndexesByGroup[group] = axis + 1;
                    }
                    else axis = analogueIndex++;
                }
                if (plugin.Filters == null || !plugin.Filters.Any(filter =>
                {
                    var runtimeKey = GetRuntimeFilterKey(filter.Name);
                    if (IsShadowMapping && runtimeKey != null)
                        runtimeKey = Filter.GetShadowName(runtimeKey, ShadowDeviceNumber);
                    return string.Equals(runtimeKey, name, StringComparison.OrdinalIgnoreCase);
                })) continue;
                if (plugin.IsFiltered())
                {
                    NeutralizeFilteredPlugin(plugin);
                    continue;
                }
                if (!plugin.WasBlockedByFilter) continue;
                plugin.WasBlockedByFilter = false;
                if (UseInputExpression)
                {
                    if (plugin.InputCategories.Count != 1) continue;
                    var fourWay = group >= 0
                        ? DeviceBindings.Any(binding => binding.InputExpressionGroup == group &&
                            binding.InputDirectionSlot >= 3)
                        : DeviceBindings.Any(binding => binding.InputDirectionSlot >= 3);
                    var value = isRange
                        ? (fourWay
                            ? EvaluateDirectionalAxis(InputCache, axis % 2, group)
                            : EvaluateAnalogueInputExpression(InputCache, group))
                        : EvaluateInputExpression(InputCache, group);
                    plugin.Update(new[] { value });
                }
                else
                {
                    var values = InputCache.ToArray();
                    for (var i = 0; i < values.Length && i < plugin.InputCategories.Count; i++)
                        values[i] = AdaptPositionalInput(values[i], DeviceBindings[i],
                            plugin.InputCategories[i].Category);
                    plugin.Update(values);
                }
            }
        }

        internal static short AdaptPositionalInput(short value, DeviceBinding binding,
            DeviceBindingCategory expectedCategory)
        {
            if (binding == null || (expectedCategory != DeviceBindingCategory.Momentary &&
                                    expectedCategory != DeviceBindingCategory.Range))
                return value;

            var threshold = Math.Max(1, Math.Min(32767, binding.InputExpressionAxisThreshold));
            switch (binding.InputExpressionInputKind)
            {
                case 1:
                    return value >= threshold
                        ? (expectedCategory == DeviceBindingCategory.Range ? short.MaxValue : (short)1)
                        : (short)0;
                case 2:
                    return value <= -threshold
                        ? (expectedCategory == DeviceBindingCategory.Range ? short.MinValue : (short)1)
                        : (short)0;
                case 3:
                    return expectedCategory == DeviceBindingCategory.Range
                        ? value : (short)(Math.Abs((int)value) >= threshold ? 1 : 0);
                default:
                    return expectedCategory == DeviceBindingCategory.Range
                        ? (short)(value != 0 ? short.MaxValue : 0) : value;
            }
        }

        internal short EvaluateInputExpression(IList<short> values, int inputGroup = -1)
        {
            short unused;
            return EvaluateInputSignal(values, false, out unused, inputGroup) ? (short)1 : (short)0;
        }

        internal short EvaluateAnalogueInputExpression(IList<short> values, int inputGroup = -1)
        {
            short analogue;
            EvaluateInputSignal(values, true, out analogue, inputGroup);
            return analogue;
        }

        // Physical directions within one logical input are alternatives rather than
        // AND terms. Remaining inputs in the same group are genuine AND gates.
        internal short EvaluateDirectionalAxis(IList<short> values, int axis, int inputGroup = -1)
        {
            if (values == null || !UseInputExpression) return 0;
            var count = Math.Min(values.Count, DeviceBindings.Count);
            foreach (var group in Enumerable.Range(0, count)
                .GroupBy(i => DeviceBindings[i].InputExpressionGroup)
                .Where(group => inputGroup < 0 || group.Key == inputGroup)
                .OrderBy(g => g.Key))
            {
                var directions = group.Where(i => DeviceBindings[i].InputDirectionSlot > 0).ToList();
                if (directions.Count == 0) continue;
                var allowed = group.Where(i => DeviceBindings[i].InputDirectionSlot == 0)
                    .All(i => EvaluateOrdinaryCondition(DeviceBindings[i], values[i]));
                if (!allowed) continue;

                int amount = 0;
                foreach (var i in directions)
                {
                    var slot = DeviceBindings[i].InputDirectionSlot;
                    if (axis == 0 && slot > 2 || axis == 1 && slot < 3) continue;
                    var raw = values[i];
                    var threshold = DeviceBindings[i].InputExpressionAxisThreshold;
                    var magnitude = DeviceBindings[i].InputExpressionInputKind == 0
                        ? (raw != 0 ? short.MaxValue : 0)
                        : Math.Abs((int)raw) >= threshold ? Math.Abs((int)raw) : 0;
                    amount += slot == 1 || slot == 3 ? -magnitude : magnitude;
                }
                // NOT negates the logical condition; it must not reverse the
                // joystick direction (the output plugin has its own Invert setting).
                var anyDirection = directions.Any(i => values[i] != 0);
                var active = DeviceBindings[directions[0]].InputExpressionNegated
                    ? !anyDirection : anyDirection;
                if (!active) continue;
                // A satisfied OR group owns both X and Y, including a neutral
                // component. Do not combine X from one group with Y from another.
                return (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, amount));
            }
            return 0;
        }

        private static bool EvaluateOrdinaryCondition(DeviceBinding binding, short raw)
        {
            var threshold = Math.Max(1, binding.InputExpressionAxisThreshold);
            var active = binding.InputExpressionInputKind == 1 ? raw >= threshold :
                binding.InputExpressionInputKind == 2 ? raw <= -threshold :
                binding.InputExpressionInputKind == 3 ? Math.Abs((int)raw) >= threshold : raw != 0;
            return binding.InputExpressionNegated ? !active : active;
        }

        // The first satisfied OR group wins. A non-negated continuous axis in that group
        // drives analogue outputs; otherwise a true group drives a full-scale axis.
        private bool EvaluateInputSignal(IList<short> values, bool forAnalogue, out short analogue,
            int inputGroup = -1)
        {
            analogue = 0;
            if (!UseInputExpression || values == null || DeviceBindings == null || DeviceBindings.Count == 0)
                return false;

            var count = Math.Min(values.Count, DeviceBindings.Count);
            foreach (var group in Enumerable.Range(0, count)
                .GroupBy(index => Math.Max(0, DeviceBindings[index].InputExpressionGroup))
                .Where(group => inputGroup < 0 || group.Key == inputGroup)
                .OrderBy(group => group.Key))
            {
                if (group.Any(i => DeviceBindings[i].InputDirectionSlot > 0))
                {
                    var allowed = group.Where(i => DeviceBindings[i].InputDirectionSlot == 0)
                        .All(i => EvaluateOrdinaryCondition(DeviceBindings[i], values[i]));
                    if (!allowed) continue;
                    var directions = group.Where(i => DeviceBindings[i].InputDirectionSlot > 0).ToList();
                    var active = directions.Any(i => values[i] != 0);
                    if (DeviceBindings[directions[0]].InputExpressionNegated) active = !active;
                    if (!active) continue;
                    analogue = forAnalogue ? EvaluateDirectionalAxis(values, 0, inputGroup) : (short)1;
                    return true;
                }

                var groupTrue = true;
                var driverPresent = false;
                short driverValue = 0;

                foreach (var index in group)
                {
                    var binding = DeviceBindings[index];
                    var raw = values[index];
                    var threshold = Math.Max(1, Math.Min(32767, binding.InputExpressionAxisThreshold));
                    bool termTrue;
                    switch (binding.InputExpressionInputKind)
                    {
                        case 1:
                            termTrue = raw >= threshold;
                            break;
                        case 2:
                            termTrue = raw <= -threshold;
                            break;
                        case 3:
                            termTrue = forAnalogue || Math.Abs((int)raw) >= threshold;
                            if (forAnalogue && !binding.InputExpressionNegated && !driverPresent)
                            {
                                driverPresent = true;
                                driverValue = raw;
                            }
                            break;
                        default:
                            termTrue = raw != 0;
                            break;
                    }

                    if (binding.InputExpressionNegated) termTrue = !termTrue;
                    if (!termTrue)
                    {
                        groupTrue = false;
                        break;
                    }
                }

                if (!groupTrue) continue;
                // A centred analogue driver produces no activation. Allow a later
                // OR group to supply an alternative, rather than shadowing it at zero.
                if (forAnalogue && driverPresent && driverValue == 0) continue;
                analogue = forAnalogue
                    ? (driverPresent ? driverValue : short.MaxValue)
                    : (short)1;
                return true;
            }
            return false;
        }

        public bool SupportsNativeInputExpression()
        {
            return Plugins != null && Plugins.Count > 0 && Plugins.All(plugin =>
                plugin.InputCategories != null && plugin.InputCategories.Count == 1 &&
                (plugin.InputCategories[0].Category == DeviceBindingCategory.Momentary ||
                 plugin.InputCategories[0].Category == DeviceBindingCategory.Range));
        }

        public bool EnableInputExpression()
        {
            if (!SupportsNativeInputExpression()) return false;
            if (UseInputExpression) return true;
            if (DeviceBindings == null || DeviceBindings.Count != 1) return false;

            UseInputExpression = true;
            var first = DeviceBindings[0];
            first.InputExpressionGroup = 0;
            first.InputExpressionNegated = false;
            if (Plugins[0].InputCategories[0].Category == DeviceBindingCategory.Range)
            {
                // Existing axis mappings retain their original continuous analogue source.
                first.InputExpressionInputKind = 3;
                first.DeviceBindingCategory = DeviceBindingCategory.Range;
            }
            Profile?.Context?.ContextChanged();
            return true;
        }

        public DeviceBinding AddExpressionInput(bool startNewOrGroup)
        {
            if (!EnableInputExpression()) return null;
            var group = 0;
            if (DeviceBindings.Count > 0)
            {
                var lastGroup = DeviceBindings.Max(item => Math.Max(0, item.InputExpressionGroup));
                group = startNewOrGroup ? lastGroup + 1 : lastGroup;
            }
            return AddExpressionInputToGroup(group);
        }

        public DeviceBinding AddExpressionInputToGroup(int group)
        {
            if (!EnableInputExpression()) return null;
            group = Math.Max(0, group);

            var binding = new DeviceBinding(Update, Profile, DeviceIoType.Input)
            {
                DeviceBindingCategory = DeviceBindingCategory.Momentary,
                InputExpressionInputKind = 0,
                InputExpressionAxisThreshold = 16384,
                InputExpressionGroup = group,
                InputExpressionNegated = false
            };

            // A newly added condition has no selected device. Binding its input
            // discovers the control from any configured profile input device.

            var insertAfter = DeviceBindings.FindLastIndex(item =>
                Math.Max(0, item.InputExpressionGroup) == group);
            if (insertAfter >= 0 && insertAfter < DeviceBindings.Count - 1)
                DeviceBindings.Insert(insertAfter + 1, binding);
            else
                DeviceBindings.Add(binding);

            Profile?.Context?.ContextChanged();
            return binding;
        }

        // Pair and four-way modes belong to the logical input group. Existing
        // independent AND conditions in that group remain untouched.
        public bool SetDirectionalInputMode(int groupIndex, int slots)
        {
            if (slots != 0 && slots != 2 && slots != 4) return false;
            if (!EnableInputExpression()) return false;
            var members = DeviceBindings.Where(b => b.InputExpressionGroup == groupIndex).ToList();
            var directions = members.Where(b => b.InputDirectionSlot > 0)
                .OrderBy(b => b.InputDirectionSlot).ToList();
            var first = directions.FirstOrDefault(b => b.InputDirectionSlot == 1)
                ?? members.FirstOrDefault(b => b.InputDirectionSlot == 0);
            if (first == null) return false;

            if (slots == 0)
            {
                foreach (var sibling in directions.Where(b => b != first))
                    DeviceBindings.Remove(sibling);
                // Returning to a continuous input cannot retain a digital key.
                if (first.InputExpressionInputKind != 3)
                {
                    first.DeviceConfigurationGuid = Guid.Empty;
                    first.KeyType = first.KeyValue = first.KeySubValue = 0;
                    first.IsBound = false;
                    first.Block = false;
                }
                first.InputDirectionSlot = 0;
                first.InputExpressionInputKind = 3;
                first.InputExpressionKindConfigured = true;
                first.DeviceBindingCategory = DeviceBindingCategory.Range;
                Profile?.Context?.ContextChanged();
                return true;
            }

            // Preserve existing directional assignments when extending or reducing
            // a two-/four-way group. Re-creating these bindings discarded working
            // keyboard and controller assignments every time the mode changed.
            if (directions.Count == 0)
            {
                // A continuous axis is incompatible with the first digital slot,
                // but a previously bound button remains a valid first direction.
                if (first.InputExpressionInputKind != 0)
                {
                    first.DeviceConfigurationGuid = Guid.Empty;
                    first.KeyType = first.KeyValue = first.KeySubValue = 0;
                    first.IsBound = false;
                    first.Block = false;
                }
                first.InputDirectionSlot = 1;
                first.InputExpressionInputKind = 0;
                first.InputExpressionKindConfigured = true;
                first.DeviceBindingCategory = DeviceBindingCategory.Momentary;
                directions.Add(first);
            }

            foreach (var redundant in directions.Where(b => b.InputDirectionSlot > slots).ToList())
                DeviceBindings.Remove(redundant);
            var insertPosition = DeviceBindings.IndexOf(first);
            for (var slot = 2; slot <= slots; slot++)
            {
                var existing = directions.FirstOrDefault(b => b.InputDirectionSlot == slot);
                if (existing != null)
                {
                    insertPosition = DeviceBindings.IndexOf(existing);
                    continue;
                }
                DeviceBindings.Insert(++insertPosition, new DeviceBinding(Update, Profile, DeviceIoType.Input)
                {
                    InputExpressionGroup = groupIndex,
                    InputDirectionSlot = slot,
                    InputExpressionInputKind = 0,
                    InputExpressionKindConfigured = true,
                    DeviceBindingCategory = DeviceBindingCategory.Momentary,
                    InputExpressionNegated = first.InputExpressionNegated
                });
            }
            Profile?.Context?.ContextChanged();
            return true;
        }

        public DeviceBinding AddExpressionCondition()
        {
            if (!EnableInputExpression()) return null;
            var nextGroup = DeviceBindings.Count == 0
                ? 0
                : DeviceBindings.Max(item => Math.Max(0, item.InputExpressionGroup)) + 1;
            return AddExpressionInputToGroup(nextGroup);
        }

        public bool SetExpressionNegated(DeviceBinding binding, bool negated)
        {
            if (binding == null || !DeviceBindings.Contains(binding) || !EnableInputExpression()) return false;
            foreach (var member in binding.InputDirectionSlot > 0
                ? DeviceBindings.Where(b => b.InputExpressionGroup == binding.InputExpressionGroup &&
                                            b.InputDirectionSlot > 0)
                : new[] { binding }.AsEnumerable())
                member.SetInputExpressionNegated(negated);
            return true;
        }

        public bool RemoveExpressionInput(DeviceBinding binding)
        {
            if (!UseInputExpression || binding == null || DeviceBindings == null || DeviceBindings.Count <= 1)
                return false;
            if (binding.InputDirectionSlot > 0)
                foreach (var sibling in DeviceBindings.Where(b =>
                    b.InputDirectionSlot > 0 && b.InputExpressionGroup == binding.InputExpressionGroup).ToList())
                    DeviceBindings.Remove(sibling);
            else if (!DeviceBindings.Remove(binding)) return false;
            NormalizeExpressionGroups();
            Profile?.Context?.ContextChanged();
            return true;
        }

        public bool RemoveExpressionGroup(int group)
        {
            if (!UseInputExpression || DeviceBindings == null) return false;
            var members = DeviceBindings.Where(item => Math.Max(0, item.InputExpressionGroup) == Math.Max(0, group)).ToList();
            if (members.Count == 0 || members.Count == DeviceBindings.Count) return false;

            foreach (var member in members) DeviceBindings.Remove(member);
            NormalizeExpressionGroups();
            Profile?.Context?.ContextChanged();
            return true;
        }

        private void NormalizeExpressionGroups()
        {
            var groups = DeviceBindings.Select(item => Math.Max(0, item.InputExpressionGroup))
                .Distinct().OrderBy(group => group).ToList();
            var remap = groups.Select((group, index) => new { group, index })
                .ToDictionary(item => item.group, item => item.index);
            foreach (var item in DeviceBindings)
                item.SetInputExpressionGroup(remap[Math.Max(0, item.InputExpressionGroup)]);
            // Input/output routing is based on these same group identifiers.
            // Removing an earlier sub-mapping must never retarget remaining outputs.
            foreach (var plugin in Plugins.Where(p => p.InputGroupIndex >= 0))
                if (remap.ContainsKey(plugin.InputGroupIndex))
                    plugin.InputGroupIndex = remap[plugin.InputGroupIndex];
        }

        #region Plugin

        internal List<Plugin> GetPluginList()
        {
            var plugins = Profile.Context.GetPlugins();
            plugins.Sort();
            if (Plugins.Count > 0)
            {
                if (SupportsNativeInputExpression())
                {
                    // Once an expression can provide a digital state or analogue drive,
                    // the output plugin's input signature no longer restricts its choice.
                    plugins = plugins.FindAll(p => p.InputCategories != null &&
                        p.InputCategories.Count == 1 &&
                        (p.InputCategories[0].Category == DeviceBindingCategory.Momentary ||
                         p.InputCategories[0].Category == DeviceBindingCategory.Range));
                }
                else
                {
                    // Legacy multi-channel plugins retain their positional input contract.
                    plugins = plugins.FindAll(p => p.HasSameInputCategories(Plugins[0]));
                }
            }
            return plugins;
        }

        public bool AddPlugin(Plugin plugin)
        {
            if (plugin == null) return false;
            if (Plugins.Count > 0 && !plugin.HasSameInputCategories(Plugins[0]) &&
                !UseInputExpression && !EnableInputExpression())
                return false;
            if (Plugins.Count == 0)
            {
                foreach (var definition in plugin.InputCategories)
                {
                    DeviceBindings.Add(new DeviceBinding(Update, Profile, DeviceIoType.Input)
                    {
                        DeviceConfigurationGuid = Guid.Empty,
                        DeviceBindingCategory = definition.Category,
                        InputExpressionInputKind = definition.Category == DeviceBindingCategory.Range ? 3 : 0
                    });
                }
            }

            plugin.SetProfile(Profile);
            Plugins.Add(plugin);

            Profile.Context.ContextChanged();
            return true;
        }

        public bool RemovePlugin(Plugin plugin)
        {
            if (!Plugins.Remove(plugin)) return false;

            if (Plugins.Count == 0)
            {
                DeviceBindings = new List<DeviceBinding>();
                UseInputExpression = false;
            }
            Profile.PruneUndefinedFilterReferencesRecursive();
            Profile.Context.ContextChanged();

            return true;
        }

        #endregion


        internal Mapping CreateShadowClone(int shadowCloneNumber)
        {
            var clonedMapping = Context.DeepXmlClone<Mapping>(this);
            clonedMapping.Title = $"{clonedMapping.Title} (Shadow {shadowCloneNumber})";
            clonedMapping.IsShadowMapping = true;
            clonedMapping.ShadowDeviceNumber = shadowCloneNumber;
            clonedMapping.Profile = Profile;
            clonedMapping.PostLoad(Profile.Context, Profile);

            foreach (var plugin in clonedMapping.Plugins)
            {
                plugin.Filters.ForEach(f => f.Name = Filter.GetShadowName(f.Name, shadowCloneNumber));
            }

            return clonedMapping;
        }

        internal void PostLoad(Context context, Profile profile = null)
        {
            Profile = profile;
            // Each old positional slot initially inherits the category expected by
            // its plugin. A saved explicit type choice always takes precedence.
            if (!UseInputExpression && Plugins.Count > 0)
            {
                var definitions = Plugins[0].InputCategories;
                for (var i = 0; i < DeviceBindings.Count && i < definitions.Count; i++)
                {
                    if (DeviceBindings[i].InputExpressionKindConfigured) continue;
                    DeviceBindings[i].InputExpressionInputKind =
                        definitions[i].Category == DeviceBindingCategory.Range ? 3 : 0;
                }
            }

            for (var i = 0; i < DeviceBindings.Count; i++)
            {
                var deviceBinding = DeviceBindings[i];
                deviceBinding.Profile = profile;
                deviceBinding.DeviceIoType = DeviceIoType.Input;
                if (UseInputExpression || deviceBinding.InputExpressionKindConfigured)
                    deviceBinding.DeviceBindingCategory = deviceBinding.InputExpressionInputKind == 0
                        ? DeviceBindingCategory.Momentary
                        : DeviceBindingCategory.Range;
                else if (Plugins.Count > 0 && i < Plugins[0].InputCategories.Count)
                    deviceBinding.DeviceBindingCategory = Plugins[0].InputCategories[i].Category;
            }

            foreach (var plugin in Plugins)
            {
                plugin.PostLoad(context, profile);
            }
        }
    }
}
