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
    public class DirectionalInputBank
    {
        public int Representation { get; set; }
        public List<DeviceBinding> Bindings { get; set; } = new List<DeviceBinding>();
    }

    public class DirectionalOutputBank
    {
        public int Representation { get; set; }

        // A non-empty key makes this bank belong to one output row instead of a whole representation.
        [XmlAttribute]
        [DefaultValue(typeof(Guid), "00000000-0000-0000-0000-000000000000")]
        public Guid FilterRestoreKey { get; set; }

        public List<Plugin> Plugins { get; set; } = new List<Plugin>();
    }

    public class Mapping
    {
        /* Persistence */
        [XmlAttribute]
        public string Title { get; set; }
        public List<DeviceBinding> DeviceBindings { get; set; }
        public List<Plugin> Plugins { get; set; }

        // The directional structure is independent of its input and output
        // presentation. Zero capacity preserves the legacy inferred structure.
        [XmlAttribute]
        [DefaultValue(0)]
        public int DirectionalCapacity { get; set; }

        [XmlAttribute]
        [DefaultValue(0)]
        public int InputRepresentation { get; set; }
        // A compound 4-Way/Axis input can contain digital directions instead of analogue axes.
        // Keep this independent from its visible representation, so presets survive restarts.
        [XmlAttribute]
        [DefaultValue(false)]
        public bool CompoundDigitalInput { get; set; }
        [XmlIgnore]
        // Directional slots are counted within each OR set. The total may
        // contain multiple independently bound alternatives.
        public int RequiredInputBindingSlotsPerGroup => CompoundDigitalInput &&
            (EffectiveInputRepresentation == 1 || EffectiveInputRepresentation == 2) &&
            EffectiveDirectionalCapacity > 1
                ? EffectiveDirectionalCapacity
                : RequiredBindingSlots(EffectiveDirectionalCapacity, EffectiveInputRepresentation);

        [XmlIgnore]
        public int RequiredInputBindingSlots => RequiredInputBindingSlotsPerGroup *
            Math.Max(1, DeviceBindings?.Select(binding =>
                Math.Max(0, binding.InputExpressionGroup)).Distinct().Count() ?? 1) +
            (DeviceBindings?.Count(binding => binding.IsDirectionalAndCondition) ?? 0);

        [XmlAttribute]
        [DefaultValue(0)]
        public int OutputRepresentation { get; set; }
        [XmlAttribute]
        [DefaultValue(false)]
        public bool CompoundDigitalOutput { get; set; }

        [XmlIgnore]
        public int RequiredOutputBindingSlots => CompoundDigitalOutput &&
            EffectiveOutputRepresentation == 1 && EffectiveDirectionalCapacity == 4
                ? 4 : RequiredBindingSlots(EffectiveDirectionalCapacity, EffectiveOutputRepresentation);

        public List<DirectionalInputBank> InactiveInputRepresentations { get; set; } =
            new List<DirectionalInputBank>();
        public List<DirectionalOutputBank> InactiveOutputRepresentations { get; set; } =
            new List<DirectionalOutputBank>();

        // Device IDs belong to the persisted mapping, including routes that will only
        // be used after the user switches a representation or restores a Filter output.
        internal IEnumerable<DeviceBinding> GetPersistedInputBindings()
        {
            return (DeviceBindings ?? Enumerable.Empty<DeviceBinding>())
                .Concat((InactiveInputRepresentations ?? Enumerable.Empty<DirectionalInputBank>())
                    .Where(bank => bank?.Bindings != null).SelectMany(bank => bank.Bindings))
                .Where(binding => binding != null).Distinct();
        }

        internal IEnumerable<DeviceBinding> GetPersistedOutputBindings()
        {
            return (Plugins ?? Enumerable.Empty<Plugin>())
                .Concat((InactiveOutputRepresentations ?? Enumerable.Empty<DirectionalOutputBank>())
                    .Where(bank => bank?.Plugins != null).SelectMany(bank => bank.Plugins))
                .Where(plugin => plugin?.Outputs != null).SelectMany(plugin => plugin.Outputs)
                .Where(binding => binding != null).Distinct();
        }

        [XmlIgnore]
        public int EffectiveInputRepresentation => InputRepresentation != 0
            ? InputRepresentation : EffectiveDirectionalCapacity == 4 &&
                DeviceBindings?.Count == 2 &&
                DeviceBindings.All(b => b.InputExpressionInputKind == 3) ? 1
            : DeviceBindings?.Any(b => !b.IsDirectionalAndCondition &&
                    b.InputExpressionInputKind == 3) == true ? 2 : 3;
        [XmlIgnore]
        public int EffectiveOutputRepresentation => OutputRepresentation != 0
            ? OutputRepresentation : EffectiveDirectionalCapacity == 4 &&
                Plugins?.Count == 2 &&
                Plugins.All(p => p.OutputCategories.All(c =>
                    c.Category == DeviceBindingCategory.Range)) ? 1
            : Plugins?.Any(p => p.OutputCategories.Any(c =>
                c.Category == DeviceBindingCategory.Range)) == true ? 2 : 3;


        public int EffectiveDirectionalCapacity
        {
            get
            {
                if (DirectionalCapacity == 1 || DirectionalCapacity == 2 ||
                    DirectionalCapacity == 4) return DirectionalCapacity;
                var directions = DeviceBindings?.Count(binding => binding.InputDirectionSlot > 0) ?? 0;
                if (directions >= 4) return 4;
                if (directions >= 2) return 2;
                var axisCount = Plugins?.Sum(plugin => plugin.OutputCategories?.Count(category =>
                    category.Category == DeviceBindingCategory.Range) ?? 0) ?? 0;
                if (axisCount >= 2) return 4;
                if (axisCount == 1 || DeviceBindings?.Any(binding =>
                    binding.InputExpressionInputKind == 3) == true) return 2;
                return 1;
            }
        }

        public static int RequiredBindingSlots(int capacity, int representation)
        {
            // Representations: 0 = legacy, 1 = 4-Way, 2 = bidirectional Axis,
            // 3 = Button, 4 = one-sided Trigger. Button uses a separate binding
            // for each direction; Trigger is one analogue channel (0..maximum).
            if (capacity != 1 && capacity != 2 && capacity != 4)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            if (representation == 0) return 0;
            if (representation < 1 || representation > 4)
                throw new ArgumentOutOfRangeException(nameof(representation));
            if (capacity == 1) return 1;
            if (representation == 3) return capacity;
            // Four one-sided sources can cover a complete 4-Way control.
            // At smaller capacities a Trigger remains one positive-side source.
            if (representation == 4) return capacity == 4 ? 4 : 1;
            // A full stick is one *logical* control but physically requires two
            // separately bound axes. The editor groups them as one stick.
            if (representation == 1 && capacity == 4) return 2;
            if (representation == 2) return capacity / 2;
            return 1;
        }

        [XmlAttribute]
        [DefaultValue(false)]
        public bool UseInputExpression { get; set; }

        /* Runtime */
        private Profile Profile { get; set; }
        private List<short> InputCache { get; set; }
        private List<CallbackMultiplexer> Multiplexer { get; set; }
        private short? _lastInputExpressionValue;
        private readonly object _routingGate = new object();
        private readonly HashSet<Plugin> _filteredOutputPlugins = new HashSet<Plugin>();
        private readonly HashSet<string> _runtimeFilterKeys = new HashSet<string>(StringComparer.Ordinal);
        private bool _filterRoutingActive;
        private bool _routingStopped;
        

        internal bool IsShadowMapping { get; set; }
        internal int ShadowDeviceNumber { get; set; }
        internal FilterState FilterState { get; set; }
        internal Guid RuntimeScopeGuid { get; private set; }

        internal IEnumerable<int> GetPossibleShadowCloneNumbers(Profile profile)
        {
            var inputs = DeviceBindings.Where(binding => binding.IsBound)
                .Select(binding => profile.GetDeviceConfiguration(DeviceIoType.Input, binding.DeviceConfigurationGuid))
                .ToList();
            if (inputs.Count == 0) return Enumerable.Empty<int>();

            var outputs = Plugins.SelectMany(plugin => plugin.Outputs)
                .Where(binding => binding.IsBound)
                .Select(binding => profile.GetDeviceConfiguration(DeviceIoType.Output, binding.DeviceConfigurationGuid));
            var configurations = inputs.Concat(outputs).Distinct().ToList();
            var commonCount = configurations.Min(configuration => configuration?.ShadowDevices?.Count ?? 0);

            // A clone uses the same shadow slot for every bound input and output.
            // If that slot is incomplete, keep the primary mapping and any complete clones.
            return Enumerable.Range(0, commonCount).Where(index =>
                configurations.All(configuration => configuration.GetShadowDevice(index) != null));
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
            StopFilterRouting();
            InputCache = new List<short>();
            // Providers expose physical triggers as signed values:
            // -32768 is released; +32767 is fully pressed.
            var triggerInput = EffectiveInputRepresentation == 4;
            for (var i = 0; i < DeviceBindings.Count; i++)
            {
                var binding = DeviceBindings[i];
                InputCache.Add(triggerInput && binding.IsBound &&
                    !binding.IsDirectionalAndCondition &&
                    binding.DeviceBindingCategory == DeviceBindingCategory.Range &&
                    binding.InputExpressionInputKind != 0 ? short.MinValue : (short)0);
            }
            Multiplexer = new List<CallbackMultiplexer>();
            for (var i = 0; i < DeviceBindings.Count; i++)
            {
                var cm = new CallbackMultiplexer(InputCache, i, Update);
                Multiplexer.Add(cm);
                DeviceBindings[i].Callback = cm.Update;
                DeviceBindings[i].CurrentValue = InputCache[i];
            }

            _lastInputExpressionValue = null;
            FilterState = filterState;
            RuntimeScopeGuid = runtimeScopeGuid;
            foreach (var plugin in Plugins)
            {
                plugin.RuntimeMapping = this;
                plugin.ResetFilterOutputTracking();
                if (!plugin.HasStatefulOutput) continue;
                _runtimeFilterKeys.UnionWith(plugin.GetRuntimeFilterKeys().Where(key => filterState.TryGetValue(key, out _)));
                if (plugin.IsFiltered()) _filteredOutputPlugins.Add(plugin);
            }
            lock (_routingGate) _routingStopped = false;
        }

        internal void SeedInitialAxisSnapshot(DeviceBinding binding, short? value)
        {
            var index = DeviceBindings.IndexOf(binding);
            if (index < 0 || Multiplexer == null) return;
            if (binding.InvertInput && value.HasValue) value = Utilities.Functions.Invert(value.Value);
            Multiplexer[index].SeedInitialValue(value);
        }

        private bool AwaitingInitialAxisSnapshot => Multiplexer.Any(input => input.AwaitingInitialValue);

        internal bool TryReadPrimaryInput(DeviceBindingCategory expectedCategory, out short value)
        {
            value = 0;
            if (InputCache == null || InputCache.Count == 0 || AwaitingInitialAxisSnapshot) return false;
            if (!UseInputExpression)
            {
                if (!DeviceBindings[0].IsBound) return false;
                value = AdaptPositionalInput(InputCache[0], DeviceBindings[0], expectedCategory);
                return true;
            }

            if (!DeviceBindings.Any(binding => binding.IsBound)) return false;
            value = expectedCategory == DeviceBindingCategory.Range
                ? EvaluateAnalogueInputExpression(InputCache)
                : EvaluateInputExpression(InputCache);
            return true;
        }

        internal Mapping GetOverridenMapping()
        {
            var parentProfile = Profile?.ParentProfile;
            while (parentProfile != null)
            {
                foreach (var mapping in parentProfile.GetRuntimeMappings())
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

        internal void StartFilterRouting()
        {
            lock (_routingGate)
            {
                if (_filterRoutingActive || _runtimeFilterKeys.Count == 0) return;

                _filterRoutingActive = true;
                FilterState.FilterStateChangedEvent += OnRuntimeFilterChanged;
                foreach (var key in _runtimeFilterKeys) RouteInput(key);
            }
        }

        internal void StopFilterRouting()
        {
            lock (_routingGate)
            {
                _routingStopped = true;
                if (_filterRoutingActive) FilterState.FilterStateChangedEvent -= OnRuntimeFilterChanged;
                _filterRoutingActive = false;
                _filteredOutputPlugins.Clear();
                _runtimeFilterKeys.Clear();
            }
        }

        private void OnRuntimeFilterChanged(string key, bool value)
        {
            lock (_routingGate)
            {
                if (_filterRoutingActive && _runtimeFilterKeys.Contains(key)) RouteInput(key);
            }
        }

        public void Update(short value)
        {
            lock (_routingGate)
            {
                if (!_routingStopped) RouteInput();
            }
        }

        private void DispatchPlugin(Plugin plugin, short[] values, string changedFilter, bool inputChanged = true)
        {
            if (changedFilter != null && (!plugin.HasStatefulOutput || !plugin.UsesRuntimeFilter(changedFilter)))
                return;

            if (plugin.IsFiltered())
            {
                if (plugin.HasStatefulOutput)
                {
                    _filteredOutputPlugins.Add(plugin);
                    if (_filterRoutingActive) plugin.ReleaseFilteredOutputs();
                }
                if (changedFilter == null) plugin.ObserveFilteredInput(values);
                return;
            }

            if (plugin.InitializeFromAxisSnapshot && AwaitingInitialAxisSnapshot) return;
            var resumed = _filterRoutingActive && _filteredOutputPlugins.Remove(plugin);
            if (changedFilter == null && inputChanged) plugin.Update(values);
            else if (resumed) plugin.ReapplyHeldInput(values);
        }

        private void RouteInput(string changedFilter = null)
        {
            if ((InputRepresentation != 0 || OutputRepresentation != 0) &&
                DirectionalCapacity > 0 && Plugins.Count > 0)
            {
                UpdateDirectionalRouting(changedFilter);
                return;
            }
            if (!UseInputExpression)
            {
                foreach (var plugin in Plugins)
                {
                    var values = InputCache.ToArray();
                    // Preserve positional inputs for multi-channel legacy plugins, while
                    // allowing each physical slot to bind a different signal category.
                    for (var i = 0; i < values.Length && i < plugin.InputCategories.Count; i++)
                        values[i] = AdaptPositionalInput(values[i], DeviceBindings[i],
                            plugin.InputCategories[i].Category);
                    DispatchPlugin(plugin, values, changedFilter);
                }
                return;
            }

            // One input expression feeds heterogeneous output plugins. Momentary consumers
            // receive its Boolean state; range consumers receive an analogue driver, or
            // full-scale activation when the expression has only discrete conditions.
            var digital = EvaluateInputExpression(InputCache);
            var analogue = EvaluateAnalogueInputExpression(InputCache);
            var digitalChanged = !_lastInputExpressionValue.HasValue ||
                                 _lastInputExpressionValue.Value != digital;
            if (changedFilter == null) _lastInputExpressionValue = digital;

            var directionalFourWay = DeviceBindings.Any(b => b.InputDirectionSlot >= 3);
            var rangeIndex = 0;
            foreach (var plugin in Plugins)
            {
                if (plugin.InputCategories.Count != 1) continue;
                var isRange = plugin.InputCategories[0].Category == DeviceBindingCategory.Range;
                var outputValue = analogue;
                if (isRange && directionalFourWay)
                    outputValue = EvaluateDirectionalAxis(InputCache, rangeIndex++ == 0 ? 0 : 1);
                else if (isRange)
                    rangeIndex++;
                DispatchPlugin(plugin, new[] { isRange ? outputValue : digital }, changedFilter,
                    isRange || digitalChanged);
            }
        }

        // Directional routing is based on capacity and physical signal values,
        // not on the labels or number of controls visible in the editor.
        // Each OR group represents one complete directional source. Never combine
        // X from one group with Y from another or ignore later OR sets.
        private void UpdateDirectionalRouting(string changedFilter)
        {
            if (InputCache == null || DeviceBindings == null || Plugins == null) return;
            var capacity = EffectiveDirectionalCapacity;
            var inputType = CompoundDigitalInput ? 3 : EffectiveInputRepresentation;
            var outputType = EffectiveOutputRepresentation;
            var routedOutputType = CompoundDigitalOutput && outputType == 1 ? 3 : outputType;
            var requiredOutput = RequiredOutputBindingSlots;
            if (RequiredInputBindingSlotsPerGroup == 0 || requiredOutput == 0 ||
                Plugins.Count < requiredOutput) return;

            var selected = ComputeDirectionalOrChannels(InputCache, DeviceBindings,
                capacity, inputType, routedOutputType, RequiredInputBindingSlotsPerGroup);
            for (var index = 0; index < Math.Min(Plugins.Count, selected.Count); index++)
            {
                var plugin = Plugins[index];
                // A logical trigger is 0..+32767; output providers expect
                // the complete signed Range with -32768 as released.
                var routed = outputType == 4 &&
                    plugin.InputCategories.Count > 0 &&
                    plugin.InputCategories[0].Category == DeviceBindingCategory.Range
                        ? ToPhysicalTriggerValue(selected[index])
                        : selected[index];
                DispatchPlugin(plugin, new[] { routed }, changedFilter);
            }
        }

        internal static List<short> ComputeDirectionalOrChannels(
            IList<short> inputValues, IList<DeviceBinding> inputBindings,
            int capacity, int inputType, int outputType, int slotsPerGroup)
        {
            var requiredOutput = RequiredBindingSlots(capacity, outputType);
            var zero = Enumerable.Repeat((short)0, requiredOutput).ToList();
            if (inputValues == null || inputBindings == null || slotsPerGroup < 1)
                return zero;

            var count = Math.Min(inputValues.Count, inputBindings.Count);
            foreach (var group in Enumerable.Range(0, count)
                .GroupBy(index => Math.Max(0, inputBindings[index].InputExpressionGroup))
                .OrderBy(group => group.Key))
            {
                var positions = group.Where(index => !inputBindings[index].IsDirectionalAndCondition)
                    .OrderBy(index => inputBindings[index].InputDirectionSlot > 0
                        ? inputBindings[index].InputDirectionSlot : index + 1).ToList();
                if (positions.Count < slotsPerGroup) continue;

                // An OR branch is eligible only when ALL its independent AND
                // conditions pass. A missing gate binding cannot accidentally
                // activate a route through NOT on an unassigned input.
                var guards = group.Where(index => inputBindings[index].IsDirectionalAndCondition);
                if (guards.Any(index => !EvaluateOrdinaryCondition(
                    inputBindings[index], inputValues[index]))) continue;

                var values = positions.Take(slotsPerGroup).Select(index => inputValues[index]).ToList();
                var bindings = positions.Take(slotsPerGroup)
                    .Select(index => inputBindings[index]).ToList();
                var channels = ComputeDirectionalChannelValues(values, bindings,
                    capacity, inputType, outputType);

                // An inactive or neutral OR set must never suppress later sets.
                if (channels.Any(channel => channel != 0)) return channels;
            }
            return zero;
        }

        internal static List<short> ComputeDirectionalChannelValues(
            IList<short> inputValues, IList<DeviceBinding> inputBindings,
            int capacity, int inputType, int outputType)
        {
            short x = 0, y = 0;
            if (capacity == 1)
                x = ReadDirectionalSource(inputValues, inputBindings, 0,
                    inputType == 3, inputType == 4);
            else if (inputType == 4)
            {
                // Four independent one-sided triggers form opposed X/Y pairs.
                // Normalise each physical signed Range before combining them.
                if (capacity == 4)
                {
                    x = ClampDirectional((int)ReadDirectionalSource(inputValues, inputBindings, 1, false, true) -
                        ReadDirectionalSource(inputValues, inputBindings, 0, false, true));
                    y = ClampDirectional((int)ReadDirectionalSource(inputValues, inputBindings, 3, false, true) -
                        ReadDirectionalSource(inputValues, inputBindings, 2, false, true));
                }
                else
                    x = ReadDirectionalSource(inputValues, inputBindings, 0, false, true);
            }
            else if (inputType == 3)
            {
                // Two opposed digital directions form each logical axis.
                x = (short)Math.Max(short.MinValue, Math.Min(short.MaxValue,
                    (ReadDirectionalSource(inputValues, inputBindings, 1, true) != 0 ? short.MaxValue : 0) -
                    (ReadDirectionalSource(inputValues, inputBindings, 0, true) != 0 ? short.MaxValue : 0)));
                if (capacity == 4)
                    y = (short)Math.Max(short.MinValue, Math.Min(short.MaxValue,
                        (ReadDirectionalSource(inputValues, inputBindings, 3, true) != 0 ? short.MaxValue : 0) -
                        (ReadDirectionalSource(inputValues, inputBindings, 2, true) != 0 ? short.MaxValue : 0)));
            }
            else
            {
                x = ReadDirectionalSource(inputValues, inputBindings, 0, false);
                if (capacity == 4) y = ReadDirectionalSource(inputValues, inputBindings, 1, false);
            }

            var threshold = short.MaxValue / 4;
            var channelValues = new List<short>();
            if (outputType == 4)
            {
                if (capacity == 4)
                {
                    channelValues.Add((short)Math.Min(short.MaxValue, Math.Max(0, -(int)x)));
                    channelValues.Add((short)Math.Max(0, (int)x));
                    channelValues.Add((short)Math.Min(short.MaxValue, Math.Max(0, -(int)y)));
                    channelValues.Add((short)Math.Max(0, (int)y));
                }
                else
                    channelValues.Add((short)Math.Max(0, (int)x));
            }
            else if (outputType == 3 && capacity > 1)
            {
                channelValues.Add(x < -threshold ? (short)1 : (short)0);
                channelValues.Add(x > threshold ? (short)1 : (short)0);
                if (capacity == 4)
                {
                    channelValues.Add(y < -threshold ? (short)1 : (short)0);
                    channelValues.Add(y > threshold ? (short)1 : (short)0);
                }
            }
            else if (outputType == 3)
                channelValues.Add(x != 0 ? (short)1 : (short)0);
            else
            {
                channelValues.Add(x);
                if (capacity == 4) channelValues.Add(y);
            }
            return channelValues;
        }

        private static short ClampDirectional(int value)
        {
            return (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, value));
        }

        // The physical signed range (-32768..32767) represents 0..100%
        // on a trigger, while the same range is -100..100% on a stick.
        // Convert only for a trigger, never for a bidirectional axis.
        internal static short ToLogicalTriggerValue(short physical)
        {
            return (short)(((long)((int)physical - short.MinValue) *
                short.MaxValue + 32767) / 65535);
        }

        internal static short ToPhysicalTriggerValue(short logical)
        {
            var value = Math.Max(0, (int)logical);
            return (short)(((long)value * 65535 + 16383) / short.MaxValue +
                short.MinValue);
        }

        private static short ReadDirectionalSource(IList<short> inputValues,
            IList<DeviceBinding> inputBindings, int index, bool digital,
            bool trigger = false)
        {
            if (index < 0 || index >= inputValues.Count || index >= inputBindings.Count)
                return 0;
            var value = inputValues[index];
            var source = inputBindings[index];
            if (!digital)
            {
                if (!source.IsBound) return 0;
                // A digital button also drives full analogue travel.
                if (source.InputExpressionInputKind == 0)
                    return (short)(value != 0 ? short.MaxValue : 0);
                if (trigger)
                    return ToLogicalTriggerValue(value);
                return value;
            }
            return EvaluateOrdinaryCondition(source, value) ? (short)1 : (short)0;
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

        internal short EvaluateInputExpression(IList<short> values)
        {
            short unused;
            return EvaluateInputSignal(values, false, out unused) ? (short)1 : (short)0;
        }

        internal short EvaluateAnalogueInputExpression(IList<short> values)
        {
            short analogue;
            EvaluateInputSignal(values, true, out analogue);
            return analogue;
        }

        // Physical directions within one logical input are alternatives rather than
        // AND terms. Remaining inputs in the same group are genuine AND gates.
        internal short EvaluateDirectionalAxis(IList<short> values, int axis)
        {
            if (values == null || !UseInputExpression) return 0;
            var count = Math.Min(values.Count, DeviceBindings.Count);
            foreach (var group in Enumerable.Range(0, count)
                .GroupBy(i => Math.Max(0, DeviceBindings[i].InputExpressionGroup)).OrderBy(g => g.Key))
            {
                var directions = group.Where(i => DeviceBindings[i].InputDirectionSlot > 0).ToList();
                if (directions.Count == 0) continue;
                var allowed = group.Where(i => DeviceBindings[i].InputDirectionSlot == 0)
                    .All(i => EvaluateOrdinaryCondition(DeviceBindings[i], values[i]));
                if (!allowed || !IsDirectionalGroupActive(directions, values)) continue;

                int amount = 0;
                foreach (var i in directions)
                {
                    var binding = DeviceBindings[i];
                    var slot = binding.InputDirectionSlot;
                    if (axis == 0 && slot > 2 || axis == 1 && slot < 3) continue;
                    var raw = values[i];
                    if (!IsPhysicalConditionActive(binding, raw)) continue;
                    var magnitude = binding.InputExpressionInputKind == 0
                        ? short.MaxValue : Math.Abs((int)raw);
                    amount += slot == 1 || slot == 3 ? -magnitude : magnitude;
                }
                // A satisfied OR group owns both X and Y, including a neutral
                // component. Do not combine X from one group with Y from another.
                return (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, amount));
            }
            return 0;
        }

        private bool IsDirectionalGroupActive(IList<int> directions, IList<short> values)
        {
            var anyDirection = directions.Any(i => IsPhysicalConditionActive(DeviceBindings[i], values[i]));
            // Group-wide NOT must have a physical source, and it never reverses the direction.
            return DeviceBindings[directions[0]].InputExpressionNegated
                ? !anyDirection && directions.Any(i => DeviceBindings[i].IsBound)
                : anyDirection;
        }

        private static bool IsPhysicalConditionActive(DeviceBinding binding, short raw)
        {
            return binding.IsBound &&
                   AdaptPositionalInput(raw, binding, DeviceBindingCategory.Momentary) != 0;
        }

        private static bool EvaluateOrdinaryCondition(DeviceBinding binding, short raw)
        {
            return binding.IsBound &&
                   IsPhysicalConditionActive(binding, raw) != binding.InputExpressionNegated;
        }

        // The first satisfied OR group wins. A non-negated continuous axis in that group
        // drives analogue outputs; otherwise a true group drives a full-scale axis.
        private bool EvaluateInputSignal(IList<short> values, bool forAnalogue, out short analogue)
        {
            analogue = 0;
            if (!UseInputExpression || values == null || DeviceBindings == null || DeviceBindings.Count == 0)
                return false;

            var count = Math.Min(values.Count, DeviceBindings.Count);
            foreach (var group in Enumerable.Range(0, count)
                .GroupBy(index => Math.Max(0, DeviceBindings[index].InputExpressionGroup))
                .OrderBy(group => group.Key))
            {
                if (group.Any(i => DeviceBindings[i].InputDirectionSlot > 0))
                {
                    var allowed = group.Where(i => DeviceBindings[i].InputDirectionSlot == 0)
                        .All(i => EvaluateOrdinaryCondition(DeviceBindings[i], values[i]));
                    if (!allowed) continue;
                    var directions = group.Where(i => DeviceBindings[i].InputDirectionSlot > 0).ToList();
                    if (!IsDirectionalGroupActive(directions, values)) continue;
                    analogue = forAnalogue ? EvaluateDirectionalAxis(values, 0) : (short)1;
                    return true;
                }

                var groupTrue = true;
                var driverPresent = false;
                short driverValue = 0;

                foreach (var index in group)
                {
                    var binding = DeviceBindings[index];
                    var raw = values[index];
                    var isAnalogueDriver = forAnalogue && binding.InputExpressionInputKind == 3;
                    var termTrue = isAnalogueDriver || IsPhysicalConditionActive(binding, raw);
                    if (!binding.IsBound || termTrue == binding.InputExpressionNegated)
                    {
                        groupTrue = false;
                        break;
                    }
                    if (isAnalogueDriver && !driverPresent)
                    {
                        driverPresent = true;
                        driverValue = raw;
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
            return Plugins != null && Plugins.Count > 0 && Plugins.All(SupportsExpressionInput);
        }

        private static bool SupportsExpressionInput(Plugin plugin)
        {
            return plugin.InputCategories != null && plugin.InputCategories.Count == 1 &&
                   (plugin.InputCategories[0].Category == DeviceBindingCategory.Momentary ||
                    plugin.InputCategories[0].Category == DeviceBindingCategory.Range);
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
            var first = members.FirstOrDefault(b => b.InputDirectionSlot == 1)
                ?? members.FirstOrDefault(b => b.InputDirectionSlot == 0);
            if (first == null) return false;

            foreach (var sibling in members.Where(b => b.InputDirectionSlot > 0 && b != first).ToList())
                DeviceBindings.Remove(sibling);
            if (slots == 0)
            {
                // Single mode is a continuous analogue source, never a leftover
                // digital negative-direction key. Discard the incompatible key.
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

            // Clear the old single-source binding without relying on a live Profile:
            // profile-less mappings are also constructed by import and unit tests.
            first.DeviceConfigurationGuid = Guid.Empty;
            first.KeyType = first.KeyValue = first.KeySubValue = 0;
            first.IsBound = false;
            first.Block = false;
            first.InputExpressionInputKind = 0;
            first.InputExpressionKindConfigured = true;
            first.DeviceBindingCategory = DeviceBindingCategory.Momentary;
            first.InputDirectionSlot = 1;

            var position = DeviceBindings.IndexOf(first);
            for (var slot = 2; slot <= slots; slot++)
            {
                DeviceBindings.Insert(++position, new DeviceBinding(Update, Profile, DeviceIoType.Input)
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
                    plugins = plugins.FindAll(SupportsExpressionInput);
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
            if (UseInputExpression && !SupportsExpressionInput(plugin)) return false;
            if (Plugins.Count > 0 && !plugin.HasSameInputCategories(Plugins[0]) &&
                (!SupportsExpressionInput(plugin) || !EnableInputExpression()))
                return false;
            if (Plugins.Count == 0 && DeviceBindings.Count == 0)
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

            // Removing the final output does not mean removing the input side.
            // Keep all bound inputs/AND groups until the owner explicitly removes
            // the mapping or those individual inputs.
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
            foreach (var bank in InactiveInputRepresentations ?? new List<DirectionalInputBank>())
                foreach (var binding in bank.Bindings ?? new List<DeviceBinding>())
                {
                    binding.Profile = profile;
                    binding.DeviceIoType = DeviceIoType.Input;
                }
            foreach (var bank in InactiveOutputRepresentations ?? new List<DirectionalOutputBank>())
                foreach (var plugin in bank.Plugins ?? new List<Plugin>())
                    plugin.PostLoad(context, profile);
        }
    }
}
