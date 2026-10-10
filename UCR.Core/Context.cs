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
        [XmlIgnore] public IReadOnlyList<Profile> ActiveProfiles => _activeProfilesView ?? (_activeProfilesView = _activeProfiles.AsReadOnly());
        private readonly List<Profile> _activeProfiles = new List<Profile>();
        private IReadOnlyList<Profile> _activeProfilesView;
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
        private const int HistoryLimit = 10;
        private readonly List<string> _undoHistory = new List<string>();
        private readonly List<string> _redoHistory = new List<string>();
        private UcrJsonSerializer _historySerializer;
        private string _historyCurrentSnapshot;
        private string _historySavedSnapshot;
        private bool _restoringHistory;

        public bool CanUndo => _undoHistory.Count > 0;
        public bool CanRedo => _redoHistory.Count > 0;


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
            if (_restoringHistory) return;
            IsNotSaved = true;
            if (_historySerializer == null) return;
            try
            {
                var current = CaptureHistorySnapshot();
                if (string.Equals(current, _historyCurrentSnapshot, StringComparison.Ordinal))
                {
                    IsNotSaved = _historySavedSnapshot == null ||
                        !string.Equals(current, _historySavedSnapshot, StringComparison.Ordinal);
                    return;
                }
                PushHistory(_undoHistory, _historyCurrentSnapshot);
                _historyCurrentSnapshot = current;
                _redoHistory.Clear();
                IsNotSaved = _historySavedSnapshot == null ||
                    !string.Equals(current, _historySavedSnapshot, StringComparison.Ordinal);
            }
            catch (Exception exception)
            {
                Logger.Warn(exception, "Could not record an edit in undo history.");
                IsNotSaved = true;
            }
        }

        // History contains in-memory configuration only. It never creates files.
        // Keep at most ten completed configuration states in either direction.
        public void InitializeEditHistory()
        {
            _historySerializer = new UcrJsonSerializer(
                PluginManager.Plugins.Select(plugin => plugin.GetType()).Distinct());
            _undoHistory.Clear();
            _redoHistory.Clear();
            _historyCurrentSnapshot = CaptureHistorySnapshot();
            _historySavedSnapshot = IsNotSaved ? null : _historyCurrentSnapshot;
        }

        private string CaptureHistorySnapshot()
        {
            return _historySerializer.Serialize(new ProfileExportPackage
            {
                Profiles = Profiles,
                DeviceAliases = DeviceAliases
            });
        }

        private static void PushHistory(List<string> history, string snapshot)
        {
            if (snapshot == null) return;
            history.Add(snapshot);
            if (history.Count > HistoryLimit) history.RemoveAt(0);
        }

        public bool Undo()
        {
            return ApplyHistory(_undoHistory, _redoHistory);
        }

        public bool Redo()
        {
            return ApplyHistory(_redoHistory, _undoHistory);
        }

        private bool ApplyHistory(List<string> from, List<string> to)
        {
            if (ActiveProfiles.Count > 0 || from.Count == 0 || _historySerializer == null)
                return false;
            var snapshot = from[from.Count - 1];
            var restored = _historySerializer.Deserialize<ProfileExportPackage>(snapshot);
            if (restored?.Profiles == null || restored.DeviceAliases == null)
                throw new InvalidDataException("Undo snapshot contains incomplete configuration.");

            var oldProfiles = Profiles.ToList();
            var oldAliases = DeviceAliases.ToList();
            _restoringHistory = true;
            try
            {
                Profiles.Clear();
                Profiles.AddRange(restored.Profiles);
                DeviceAliases.Clear();
                DeviceAliases.AddRange(restored.DeviceAliases);
                PostLoad();
                OnDeviceAliasesChangedEvent();
            }
            catch
            {
                Profiles.Clear();
                Profiles.AddRange(oldProfiles);
                DeviceAliases.Clear();
                DeviceAliases.AddRange(oldAliases);
                PostLoad();
                throw;
            }
            finally
            {
                _restoringHistory = false;
            }

            PushHistory(to, _historyCurrentSnapshot);
            from.RemoveAt(from.Count - 1);
            _historyCurrentSnapshot = snapshot;
            IsNotSaved = _historySavedSnapshot == null ||
                !string.Equals(snapshot, _historySavedSnapshot, StringComparison.Ordinal);
            return true;
        }

        internal bool HasUnsavedPersistentChanges(List<Type> pluginTypes = null)
        {
            if (!IsNotSaved) return false;

            try
            {
                if (Store.MatchesPersistedConfiguration(this, pluginTypes))
                {
                    // Some UI/runtime paths conservatively call ContextChanged even when the
                    // serialized configuration is unchanged. Do not turn that into a fake save prompt.
                    IsNotSaved = false;
                    Logger.Trace("Dirty flag cleared because persisted configuration is unchanged.");
                    return false;
                }
            }
            catch (Exception exception)
            {
                // If comparison itself fails, keep the conservative dirty result rather than risk
                // silently dropping a genuine user edit.
                Logger.Warn(exception, "Unable to verify whether the dirty configuration differs from disk.");
            }

            return true;
        }

        #region Persistence
        
        public bool SaveContext(List<Type> pluginTypes = null)
        {
            Store.Save(this, pluginTypes);
            IsNotSaved = false;
            if (_historySerializer != null)
            {
                _historyCurrentSnapshot = CaptureHistorySnapshot();
                _historySavedSnapshot = _historyCurrentSnapshot;
            }
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

        public static bool PersistentXmlEquivalent<T>(T left, T right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (ReferenceEquals(left, null) || ReferenceEquals(right, null)) return false;

            var pluginTypes = new HashSet<Type>(GetClonePluginTypes(left));
            foreach (var type in GetClonePluginTypes(right)) pluginTypes.Add(type);
            var formatter = GetXmlSerializer(pluginTypes.ToList(), typeof(T));

            byte[] leftBytes;
            byte[] rightBytes;
            using (var leftStream = new MemoryStream())
            {
                formatter.Serialize(leftStream, left);
                leftBytes = leftStream.ToArray();
            }
            using (var rightStream = new MemoryStream())
            {
                formatter.Serialize(rightStream, right);
                rightBytes = rightStream.ToArray();
            }

            if (leftBytes.Length != rightBytes.Length) return false;
            for (var i = 0; i < leftBytes.Length; i++)
            {
                if (leftBytes[i] != rightBytes[i]) return false;
            }
            return true;
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