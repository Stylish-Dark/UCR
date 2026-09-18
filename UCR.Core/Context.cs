using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using System.Xml.Serialization;
using HidWizards.IOWrapper.Core;
using HidWizards.UCR.Core.Annotations;
using HidWizards.UCR.Core.Managers;
using HidWizards.UCR.Core.Models;
using HidWizards.UCR.Core.Persistence;
using Mono.Options;
using NLog;

namespace HidWizards.UCR.Core
{
    public sealed class Context : IDisposable
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private const string PluginPath = "Plugins";

        /* Persistence */
        public List<Profile> Profiles { get; set; }
        public List<DeviceAlias> DeviceAliases { get; set; }

        /* Runtime */
        [XmlIgnore] public Profile ActiveProfile { get; internal set; }
        [XmlIgnore] public IReadOnlyList<Profile> ActiveProfiles => _activeProfiles.AsReadOnly();
        private readonly List<Profile> _activeProfiles = new List<Profile>();
        [XmlIgnore] public ProfilesManager ProfilesManager { get; set; }
        [XmlIgnore] public DevicesManager DevicesManager { get; set; }
        [XmlIgnore] public SubscriptionsManager SubscriptionsManager { get; set; }
        [XmlIgnore] public PluginsManager PluginManager { get; set; }
        [XmlIgnore] public BindingManager BindingManager { get; set; }

        public delegate void ActiveProfileChanged(Profile profile);
        public event ActiveProfileChanged ActiveProfileChangedEvent;

        public delegate void DeviceAliasesChanged();
        public event DeviceAliasesChanged DeviceAliasesChangedEvent;
        
        internal bool IsNotSaved { get; private set; }
        internal IOController IOController { get; set; }
        [XmlIgnore] internal ContextStore Store { get; private set; }
        private OptionSet options;

        public Context() : this(ContextStore.CreateDefault())
        {
        }

        internal Context(ContextStore store)
        {
            Store = store ?? throw new ArgumentNullException(nameof(store));
            Init();
            SetCommandLineOptions();
        }

        private void Init()
        {
            IsNotSaved = false;
            Profiles = new List<Profile>();
            DeviceAliases = new List<DeviceAlias>();

            try
            {
                IOController = new IOController();
            }
            catch (DirectoryNotFoundException e)
            {
                Logger.Error(e, "IOWrapper provider directory not found");
            }
            
            ProfilesManager = new ProfilesManager(this, Profiles);
            DevicesManager = new DevicesManager(this);
            SubscriptionsManager = new SubscriptionsManager(this);
            PluginManager = new PluginsManager(PluginPath);
            BindingManager = new BindingManager(this);
        }

        private void SetCommandLineOptions()
        {
            options = new OptionSet {
                { "p|profile=", "The profile to search for", FindAndLoadProfile }
            };
        }

        private void FindAndLoadProfile(string profileString)
        {
            Logger.Debug($"Searching for profile to load: {{{profileString}}}");
            var search = profileString.Split(',').ToList();
            var profile = ProfilesManager.FindProfile(search);
            if (profile != null) SubscriptionsManager.ActivateProfile(profile);
        }

        public void ParseCommandLineArguments(IEnumerable<string> args)
        {
            options.Parse(args);
        }

        public List<Plugin> GetPlugins()
        {
            return PluginManager.Plugins.Where(p => !p.IsDisabled).ToList();
        }

        public void ContextChanged()
        {
            Logger.Trace("Context changed");
            IsNotSaved = true;
        }

        #region Persistence
        
        public bool SaveContext(List<Type> pluginTypes = null)
        {
            Store.Save(this, pluginTypes);
            IsNotSaved = false;
            return true;
        }

        public static Context Load(List<Type> pluginTypes = null)
        {
            return ContextStore.CreateDefault().Load(pluginTypes);
        }

        internal static Context Load(ContextStore store, List<Type> pluginTypes = null)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            return store.Load(pluginTypes);
        }

        internal void PostLoad()
        {
            if (Profiles == null) Profiles = new List<Profile>();
            if (DeviceAliases == null) DeviceAliases = new List<DeviceAlias>();

            foreach (var profile in Profiles)
            {
                profile.PostLoad(this);
            }

            // Child profiles are a legacy persistence concept now. Convert them immediately after
            // loading so every runtime/UI consumer sees the flat profile + local mapping-group model.
            ProfilesManager.MigrateLegacyChildrenToMappingGroups();
        }

        internal static XmlSerializer GetXmlSerializer(List<Type> additionalPluginTypes)
        {
            return GetXmlSerializer(additionalPluginTypes, typeof(Context));
        }

        internal static XmlSerializer GetXmlSerializer(List<Type> additionalPluginTypes, Type type)
        {
            var plugins = new PluginsManager(PluginPath);
            var pluginTypes = plugins.Plugins.Select(p => p.GetType()).ToList();
            if (additionalPluginTypes != null) pluginTypes.AddRange(additionalPluginTypes);
            return new XmlSerializer(type, pluginTypes.ToArray());
        }

        #endregion

        private string GetVersion()
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            var fileVersionInfo = FileVersionInfo.GetVersionInfo(assembly.Location);

            return fileVersionInfo.ProductVersion;
        }

        public void Dispose()
        {
            DevicesManager?.CancelInputDeviceDetection();
            BindingManager?.Dispose();
            SubscriptionsManager.Dispose();
            IOController?.Dispose();
        }

        public static T DeepClone<T>(T obj)
        {
            using (var ms = new MemoryStream())
            {
                var formatter = new BinaryFormatter();
                formatter.Serialize(ms, obj);
                ms.Position = 0;

                return (T)formatter.Deserialize(ms);
            }
        }

        public static T DeepXmlClone<T>(T obj)
        {
            using (var ms = new MemoryStream())
            {
                // XmlSerializer must be told about every concrete Plugin subclass present in the
                // object graph. This matters for profile/group copies and legacy-child migration:
                // those operations can clone mappings containing plugins even when the Plugins
                // directory is not populated (for example in tests or portable tooling).
                var formatter = GetXmlSerializer(GetClonePluginTypes(obj), typeof(T));
                formatter.Serialize(ms, obj);
                ms.Position = 0;

                return (T)formatter.Deserialize(ms);
            }
        }

        private static List<Type> GetClonePluginTypes(object value)
        {
            var result = new HashSet<Type>();
            CollectClonePluginTypes(value, result);
            return result.ToList();
        }

        private static void CollectClonePluginTypes(object value, ISet<Type> result)
        {
            if (value == null || result == null) return;

            var plugin = value as Plugin;
            if (plugin != null)
            {
                result.Add(plugin.GetType());
                return;
            }

            var mapping = value as Mapping;
            if (mapping != null)
            {
                foreach (var item in mapping.Plugins ?? new List<Plugin>())
                {
                    CollectClonePluginTypes(item, result);
                }
                return;
            }

            var group = value as MappingGroup;
            if (group != null)
            {
                foreach (var item in group.Mappings ?? new List<Mapping>())
                {
                    CollectClonePluginTypes(item, result);
                }
                return;
            }

            var profile = value as Profile;
            if (profile == null) return;

            foreach (var item in profile.Mappings ?? new List<Mapping>())
            {
                CollectClonePluginTypes(item, result);
            }
            foreach (var item in profile.MappingGroups ?? new List<MappingGroup>())
            {
                CollectClonePluginTypes(item, result);
            }
            foreach (var child in profile.ChildProfiles ?? new List<Profile>())
            {
                CollectClonePluginTypes(child, result);
            }
        }

        internal void SetActiveProfiles(IEnumerable<Profile> profiles)
        {
            _activeProfiles.Clear();
            if (profiles != null)
            {
                foreach (var profile in profiles.Where(profile => profile != null))
                {
                    if (_activeProfiles.Any(active => active.Guid == profile.Guid)) continue;
                    _activeProfiles.Add(profile);
                }
            }
            ActiveProfile = _activeProfiles.LastOrDefault();
        }

        public void OnActiveProfileChangedEvent(Profile profile)
        {
            ActiveProfileChangedEvent?.Invoke(profile);
        }

        public void OnDeviceAliasesChangedEvent()
        {
            DeviceAliasesChangedEvent?.Invoke();
        }
    }
}