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
    public class Mapping
    {
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
            FilterState = filterState;
            RuntimeScopeGuid = runtimeScopeGuid;
            Plugins.ForEach(p => p.RuntimeMapping = this);
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
                    if (plugin.IsFiltered()) continue;
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

            // One input expression feeds heterogeneous output plugins. Momentary consumers
            // receive its Boolean state; range consumers receive an analogue driver, or
            // full-scale activation when the expression has only discrete conditions.
            var digital = EvaluateInputExpression(InputCache);
            var analogue = EvaluateAnalogueInputExpression(InputCache);
            var digitalChanged = !_lastInputExpressionValue.HasValue ||
                                 _lastInputExpressionValue.Value != digital;
            _lastInputExpressionValue = digital;

            foreach (var plugin in Plugins)
            {
                if (plugin.IsFiltered()) continue;
                if (plugin.InputCategories.Count != 1) continue;
                var isRange = plugin.InputCategories[0].Category == DeviceBindingCategory.Range;
                if (!isRange && !digitalChanged) continue;
                plugin.Update(new[] { isRange ? analogue : digital });
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

            var template = DeviceBindings.LastOrDefault(item => Math.Max(0, item.InputExpressionGroup) == group)
                           ?? DeviceBindings.LastOrDefault();
            if (template != null) binding.DeviceConfigurationGuid = template.DeviceConfigurationGuid;

            var insertAfter = DeviceBindings.FindLastIndex(item =>
                Math.Max(0, item.InputExpressionGroup) == group);
            if (insertAfter >= 0 && insertAfter < DeviceBindings.Count - 1)
                DeviceBindings.Insert(insertAfter + 1, binding);
            else
                DeviceBindings.Add(binding);

            Profile?.Context?.ContextChanged();
            return binding;
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
            binding.SetInputExpressionNegated(negated);
            return true;
        }

        public bool RemoveExpressionInput(DeviceBinding binding)
        {
            if (!UseInputExpression || binding == null || DeviceBindings == null || DeviceBindings.Count <= 1)
                return false;
            if (!DeviceBindings.Remove(binding)) return false;
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
                        DeviceConfigurationGuid = Profile.GetPrimaryDeviceConfiguration(DeviceIoType.Input)?.Guid ?? Guid.Empty,
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

            foreach (var deviceBinding in DeviceBindings)
            {
                deviceBinding.Profile = profile;
                deviceBinding.DeviceIoType = DeviceIoType.Input;
                if (UseInputExpression || deviceBinding.InputExpressionKindConfigured)
                    deviceBinding.DeviceBindingCategory = deviceBinding.InputExpressionInputKind == 0
                        ? DeviceBindingCategory.Momentary
                        : DeviceBindingCategory.Range;
            }

            foreach (var plugin in Plugins)
            {
                plugin.PostLoad(context, profile);
            }
        }
    }
}
