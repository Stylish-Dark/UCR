using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Xml.Serialization;
using HidWizards.UCR.Core.Attributes;
using HidWizards.UCR.Core.Models.Binding;
using HidWizards.UCR.Core.Models.Subscription;

namespace HidWizards.UCR.Core.Models
{
    [InheritedExport(typeof(Plugin))]
    public abstract class Plugin : IComparable<Plugin>
    {
        /* Persistence */
        public List<DeviceBinding> Outputs { get; }
        public List<Filter> Filters { get; set; }
        
        /* Runtime */
        internal Profile Profile { get; set; }
        private List<IODefinition> _inputCategories;
        private List<IODefinition> _outputCategories;
        private List<PluginPropertyGroup> _pluginPropertyGroups;
        internal FilterState FilterState { get; set; }
        internal Mapping RuntimeMapping { get; set; }

        public event Action<string, string> FilterDefinitionChanged;

        #region Properties

        [XmlIgnore]
        public List<IODefinition> InputCategories
        {
            get
            {
                if (_inputCategories != null) return _inputCategories;
                _inputCategories = GetIODefinitions(DeviceIoType.Input);
                return _inputCategories;
            }
        }

        [XmlIgnore]
        public List<IODefinition> OutputCategories
        {
            get
            {
                if (_outputCategories != null) return _outputCategories;
                _outputCategories = GetIODefinitions(DeviceIoType.Output);
                return _outputCategories;
            }
        }

        [XmlIgnore]
        public List<PluginPropertyGroup> PluginPropertyGroups
        {
            get
            {
                if (_pluginPropertyGroups != null) return _pluginPropertyGroups;
                _pluginPropertyGroups = GetGuiMatrix();
                return _pluginPropertyGroups;
            }
        }

        [XmlIgnore]
        public string PluginName =>  GetPluginAttribute().Name;
        [XmlIgnore]
        public string Description => GetPluginAttribute().Description;
        [XmlIgnore]
        public string Group => GetPluginAttribute().Group;
        [XmlIgnore]
        public bool IsDisabled => GetPluginAttribute().Disabled;

        #endregion

        public struct IODefinition
        {
            public string Name;
            public DeviceBindingCategory Category;
            public string GroupName;
        }

        public enum FilterMode
        {
            Active,
            Inactive,
            Toggle,
            Unchanged
        }

        protected Plugin()
        {
            Outputs = new List<DeviceBinding>();
            foreach (var _ in OutputCategories)
            {
                Outputs.Add(new DeviceBinding(null, Profile, DeviceIoType.Output));
            }

            Filters = new List<Filter>();
        }

        #region Life cycle
        
        public virtual void OnActivate()
        {

        }

        public virtual void OnPropertyChanged()
        {

        }

        /*
         * Called before a plugin OnActivate and OnPropertyChanged to cache run-time values
         */
        public virtual void InitializeCacheValues()
        {

        }

        public virtual void Update(params short[] values)
        {

        }

        public virtual void OnDeactivate()
        {

        }

        #endregion

        #region Plugin methods

        protected void WriteOutput(int number, short value)
        {
            Outputs[number].WriteOutput(value);
        }

        protected long ReadOutput(int number)
        {
            return Outputs[number].CurrentValue;
        }

        protected void WriteFilterState(string filterName, bool value)
        {
            var runtimeName = GetFilterName(filterName);
            if (runtimeName == null) return;
            RuntimeMapping.FilterState.SetFilterState(runtimeName, value);
        }

        protected void ToggleFilterState(string filterName)
        {
            var runtimeName = GetFilterName(filterName);
            if (runtimeName == null) return;
            RuntimeMapping.FilterState.ToggleFilterState(runtimeName);
        }

        private string GetFilterName(string filterName)
        {
            if (string.IsNullOrWhiteSpace(filterName) || RuntimeMapping == null) return null;
            var filter = RuntimeMapping.GetRuntimeFilterKey(filterName);
            return RuntimeMapping.IsShadowMapping
                ? Filter.GetShadowName(filter, RuntimeMapping.ShadowDeviceNumber)
                : filter;
        }

        public string GetDefinedFilterName()
        {
            if (!string.Equals(Group, "Filter", StringComparison.OrdinalIgnoreCase)) return null;
            var property = GetType().GetProperty("FilterName", BindingFlags.Instance | BindingFlags.Public);
            if (property == null || property.PropertyType != typeof(string)) return null;
            var value = property.GetValue(this) as string;
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        internal void OnFilterDefinitionChanged(string oldName, string newName)
        {
            FilterDefinitionChanged?.Invoke(oldName, newName);
        }

        #endregion

        #region Filters

        internal Filter AddFilter(string name, bool negative = false)
        {
            var existingFilter = Filters.Find(f => string.Equals(f.Name, name, StringComparison.CurrentCultureIgnoreCase));

            if (existingFilter != null)
            {
                existingFilter.Negative = negative;
                ContextChanged();
                return existingFilter;
            }
            
            var filter = new Filter()
            {
                Name = name,
                Negative = negative
            };
            
            Filters.Add(filter);
            ContextChanged();
            return filter;
        }

        internal bool RemoveFilter(Filter filter)
        {
            var success = Filters.Remove(filter);
            if (success) ContextChanged();
            return success;
        }

        internal void ToggleFilter(Filter filter)
        {
            var existingFilter = Filters.Find(f => string.Equals(f.Name, filter.Name, StringComparison.InvariantCultureIgnoreCase));
            if (existingFilter != null) AddFilter(existingFilter.Name, !existingFilter.Negative);
        }

        #endregion

        public void SetProfile(Profile profile)
        {
            Profile = profile;
            Outputs.ForEach(o => o.Profile = profile);
        }

        public Plugin Duplicate()
        {
            var newPlugin = Context.DeepXmlClone(this);
            newPlugin.PostLoad(Profile.Context, Profile);
            return newPlugin;
        }

        public void ContextChanged()
        {
            Profile?.Context?.ContextChanged();
        }

        #region Loading

        public void PostLoad(Context context, Profile parentProfile)
        {
            SetProfile(parentProfile);
            NormalizeOutputBindings(parentProfile);
        }

        private void NormalizeOutputBindings(Profile parentProfile)
        {
            var expectedCount = OutputCategories.Count;
            if (expectedCount == 0)
            {
                Outputs.Clear();
                return;
            }

            // XmlSerializer populates this getter-only list on top of the bindings created by the
            // plugin constructor. Historically UCR therefore had [defaults][persisted] here and
            // "zipped" the second half into the first. Make that repair idempotent: PostLoad may be
            // called more than once, and old broken group migrations can have persisted zero or only
            // some of the expected outputs.
            if (Outputs.Count > expectedCount)
            {
                var persistedCount = Math.Min(expectedCount, Outputs.Count - expectedCount);
                for (var index = 0; index < persistedCount; index++)
                {
                    CopyBindingState(Outputs[expectedCount + index], Outputs[index]);
                }

                for (var index = Outputs.Count - 1; index >= expectedCount; index--)
                    Outputs.RemoveAt(index);
            }

            while (Outputs.Count < expectedCount)
                Outputs.Add(new DeviceBinding(null, parentProfile, DeviceIoType.Output));

            foreach (var output in Outputs)
            {
                output.Profile = parentProfile;
                output.DeviceIoType = DeviceIoType.Output;
            }
        }

        private static void CopyBindingState(DeviceBinding source, DeviceBinding target)
        {
            if (source == null || target == null) return;
            target.IsBound = source.IsBound;
            target.DeviceConfigurationGuid = source.DeviceConfigurationGuid;
            target.KeyType = source.KeyType;
            target.KeyValue = source.KeyValue;
            target.KeySubValue = source.KeySubValue;
            target.Block = source.Block;
            target.InvertInput = source.InvertInput;
        }

        #endregion
        
        #region Comparison

        public int CompareTo(Plugin other)
        {
            return string.Compare(PluginName, other.PluginName, StringComparison.Ordinal);
        }

        public bool HasSameInputCategories(Plugin other)
        {
            if (InputCategories.Count > other.InputCategories.Count) return false;
            for (var i = 0; i < InputCategories.Count; i++)
            {
                if (InputCategories[i].Category != other.InputCategories[i].Category) return false;
            }

            return true;
        }

        #endregion

        #region Attributes

        private PluginAttribute GetPluginAttribute()
        {
            var pluginAttribute = (PluginAttribute)Attribute.GetCustomAttribute(GetType(), typeof(PluginAttribute));

            return pluginAttribute ?? new PluginAttribute("Invalid plugin");
        }
        
        private List<IODefinition> GetIODefinitions(DeviceIoType deviceIoType)
        {
            var attributes = (PluginIoAttribute[])Attribute.GetCustomAttributes(GetType(), typeof(PluginIoAttribute));
            
            return attributes.Where(a => a.DeviceIoType == deviceIoType).Select(a => new IODefinition()
            {
                Category = a.DeviceBindingCategory,
                Name = a.Name,
                GroupName = a.Group
            }).ToList();
        }

        private List<PluginProperty> GetGuiProperties()
        {
            var properties = from p in GetType().GetProperties()
                let attr = p.GetCustomAttributes(typeof(PluginGuiAttribute), true)
                where attr.Length == 1
                select new { Property = p, Attribute = attr.First() as PluginGuiAttribute };


            return properties.Select(prop => new PluginProperty(this, prop.Property, prop.Attribute.Name, prop.Attribute.Order, prop.Attribute.Group)).ToList();
        }

        private List<PluginGroupAttribute> GetPluginGroups()
        {
            return GetType().GetCustomAttributes(typeof(PluginGroupAttribute), true).ToList()
                .Select(a => ((PluginGroupAttribute) a)).ToList();
        }

        private List<string> GetPluginOutputGroups()
        {
            return GetType().GetCustomAttributes(typeof(PluginOutput), true).ToList()
                .Select(a => ((PluginOutput)a).Group).Distinct().ToList();
        }

        public List<PluginPropertyGroup> GetGuiMatrix()
        {
            var result = new List<PluginPropertyGroup>();
            
            var guiProperties = GetGuiProperties();
            guiProperties.Sort();

            var ungroupedProperties = guiProperties.FindAll(p => p.Group == null);
            if (ungroupedProperties.Count > 0) { 
                result.Add(new PluginPropertyGroup()
                {
                    Title = "Settings",
                    GroupName = "Settings",
                    GroupType = PluginPropertyGroup.GroupTypes.Settings,
                    PluginProperties = ungroupedProperties
                });
            }

            foreach (var group in GetPluginGroups())
            {
                if (group.Group == null) continue;

                var properties = guiProperties.FindAll(p => group.Group.Equals(p.Group));
                result.Add(new PluginPropertyGroup()
                {
                    Title = group.Name,
                    GroupName = group.Group,
                    GroupType = GetPluginOutputGroups().Contains(group.Group) 
                        ? PluginPropertyGroup.GroupTypes.Output 
                        : PluginPropertyGroup.GroupTypes.Settings,
                    PluginProperties = properties
                });
            }

            foreach (var pluginGroup in result)
            {
                pluginGroup.PluginProperties.Sort((x, y) => x.Order.CompareTo(y.Order));
            }

            return result;
        }

        #endregion

        public virtual PropertyValidationResult Validate(PropertyInfo propertyInfo, dynamic value)
        {
            return PropertyValidationResult.ValidResult;
        }

        public bool IsFiltered()
        {
            if (Filters.Count == 0) return false;

            foreach (var filter in Filters)
            {
                var runtimeName = GetFilterName(filter.Name);
                if (runtimeName == null) continue;
                RuntimeMapping.FilterState.FilterRuntimeDictionary.TryGetValue(runtimeName, out var filterValue);
                if (!(filterValue ^ filter.Negative)) return true;
            }

            return false;
        }
    }
}
