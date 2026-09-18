using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Serialization;
using HidWizards.UCR.Core.Annotations;
using HidWizards.UCR.Core.Managers;
using HidWizards.UCR.Core.Models.Binding;
using NLog;

namespace HidWizards.UCR.Core.Models
{
    public class Profile : INotifyPropertyChanged
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        /* Persistence */
        [XmlAttribute]
        public string Title { get; set; }
        [XmlAttribute]
        public Guid Guid { get; set; }
        // Legacy child profiles remain deserializable so old contexts/imports can be migrated safely.
        public List<Profile> ChildProfiles { get; set; }
        public List<Mapping> Mappings { get; set; }
        public List<MappingGroup> MappingGroups { get; set; }

        public List<DeviceConfiguration> InputDeviceConfigurations { get; set; }
        public List<DeviceConfiguration> OutputDeviceConfigurations { get; set; }

        private bool _autoActivateEnabled;
        private string _autoActivateExecutable;
        private ObservableCollection<ProfileApplicationRule> _autoActivateApplications;

        public ObservableCollection<ProfileApplicationRule> AutoActivateApplications
        {
            get => _autoActivateApplications;
            set
            {
                if (ReferenceEquals(_autoActivateApplications, value)) return;
                if (_autoActivateApplications != null) _autoActivateApplications.CollectionChanged -= AutoActivateApplicationsOnCollectionChanged;
                _autoActivateApplications = value ?? new ObservableCollection<ProfileApplicationRule>();
                foreach (var rule in _autoActivateApplications) rule?.Attach(this);
                _autoActivateApplications.CollectionChanged += AutoActivateApplicationsOnCollectionChanged;
                OnPropertyChanged();
            }
        }

        [XmlAttribute]
        public bool AutoActivateEnabled
        {
            get => _autoActivateEnabled;
            set
            {
                if (_autoActivateEnabled == value) return;
                _autoActivateEnabled = value;
                OnPropertyChanged();
                Context?.ContextChanged();
            }
        }

        [XmlAttribute]
        public string AutoActivateExecutable
        {
            get => _autoActivateExecutable;
            set
            {
                if (string.Equals(_autoActivateExecutable, value, StringComparison.Ordinal)) return;
                _autoActivateExecutable = value;
                OnPropertyChanged();
                Context?.ContextChanged();
            }
        }

        [XmlAttribute]
        public Guid PrimaryInputDeviceConfigurationGuid { get; set; }

        [XmlAttribute]
        public Guid PrimaryOutputDeviceConfigurationGuid { get; set; }


        /* Runtime */
        [XmlIgnore]
        public Context Context;
        [XmlIgnore]
        public Profile ParentProfile { get; set; }

        #region Constructors

        public Profile()
        {
            Init();
        }

        public Profile(Context context)
        {
            Context = context;
            Init();
        }

        private void Init()
        {
            Guid = Guid.NewGuid();
            ChildProfiles = new List<Profile>();
            Mappings = new List<Mapping>();
            MappingGroups = new List<MappingGroup>();
            InputDeviceConfigurations = new List<DeviceConfiguration>();
            OutputDeviceConfigurations = new List<DeviceConfiguration>();
            AutoActivateApplications = new ObservableCollection<ProfileApplicationRule>();
        }

        public Profile(Context context, Profile parentProfile = null) : this(context)
        {
            ParentProfile = parentProfile;
        }

        #endregion

        #region Actions

        public static Profile CreateProfile(Context context, string title, List<DeviceConfiguration> inputDevices,
            List<DeviceConfiguration> outputDevices, Profile parent = null)
        {
            var profile = new Profile(context, parent)
            {
                Title = title,
                InputDeviceConfigurations = inputDevices ?? new List<DeviceConfiguration>(),
                OutputDeviceConfigurations = outputDevices ?? new List<DeviceConfiguration>()
            };

            return profile;
        }

        public void AddChildProfile(Profile profile)
        {
            if (ChildProfiles == null) ChildProfiles = new List<Profile>();
            profile.Context = Context;
            profile.ParentProfile = this;
            ChildProfiles.Add(profile);
            Context.ContextChanged();
        }

        public bool Rename(string title)
        {
            Title = title;
            Context.ContextChanged();
            return true;
        }

        public void Remove()
        {
            // A profile can now be one member of a composite runtime. Never leave subscriptions
            // pointing at a profile that has already been removed from the persistent profile list.
            if (IsActive() && !Context.SubscriptionsManager.DeactivateProfile(this)) return;

            if (ParentProfile == null)
            {
                Context.Profiles.Remove(this);
            }
            else
            {
                ParentProfile.ChildProfiles.Remove(this);
            }
            Context.ContextChanged();
        }

        public bool ActivateProfile()
        {
            return Context.SubscriptionsManager.ActivateProfile(this);
        }

        public bool Deactivate()
        {
            return Context.SubscriptionsManager.DeactivateProfile(this);
        }

        internal void PrepareProfile()
        {
            
        }


        public ProfileApplicationRule AddAutoActivateApplication(string executable = null, string arguments = null)
        {
            var rule = new ProfileApplicationRule(executable, arguments);
            rule.Attach(this);
            AutoActivateApplications.Add(rule);
            return rule;
        }

        public bool RemoveAutoActivateApplication(ProfileApplicationRule rule)
        {
            if (rule == null || AutoActivateApplications == null) return false;
            return AutoActivateApplications.Remove(rule);
        }

        private void AutoActivateApplicationsOnCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e?.NewItems != null)
            {
                foreach (var rule in e.NewItems.OfType<ProfileApplicationRule>()) rule.Attach(this);
            }
            Context?.ContextChanged();
            OnPropertyChanged(nameof(AutoActivateApplications));
        }

        #endregion

        #region Mapping

        public Mapping AddMapping(string title)
        {
            var mapping = new Mapping(this, title);
            Mappings.Add(mapping);
            Context?.ContextChanged();
            return mapping;
        }

        public MappingGroup AddMappingGroup(string title)
        {
            if (MappingGroups == null) MappingGroups = new List<MappingGroup>();
            var group = new MappingGroup(this, GetUniqueMappingGroupTitle(title));
            MappingGroups.Add(group);
            Context?.ContextChanged();
            OnPropertyChanged(nameof(MappingGroups));
            return group;
        }

        public MappingGroup CopyMappingGroup(MappingGroup source, string title = null)
        {
            return CopyMappingGroup(source, source?.Profile, title);
        }

        public MappingGroup CopyMappingGroup(MappingGroup source, Profile sourceProfile, string title = null)
        {
            if (source == null) return null;
            if (MappingGroups == null) MappingGroups = new List<MappingGroup>();

            var clone = HidWizards.UCR.Core.Context.DeepXmlClone<MappingGroup>(source);
            clone.Guid = Guid.NewGuid();
            clone.Title = GetUniqueMappingGroupTitle(string.IsNullOrWhiteSpace(title) ? source.Title + " Copy" : title);
            clone.Enabled = false;

            // Copy/paste is allowed between profiles. Preserve bindings when the destination profile
            // has the same configured device under a different configuration GUID; otherwise retain
            // the old GUID so UCR presents the binding as unavailable instead of silently guessing.
            RemapCopiedGroupBindings(clone, sourceProfile);
            clone.PostLoad(Context, this);
            MappingGroups.Add(clone);
            Context?.ContextChanged();
            OnPropertyChanged(nameof(MappingGroups));
            return clone;
        }

        private void RemapCopiedGroupBindings(MappingGroup group, Profile sourceProfile)
        {
            if (group?.Mappings == null || sourceProfile == null || ReferenceEquals(sourceProfile, this)) return;

            foreach (var mapping in group.Mappings.Where(mapping => mapping != null))
            {
                foreach (var binding in mapping.DeviceBindings ?? new List<DeviceBinding>())
                {
                    RemapCopiedBinding(binding, DeviceIoType.Input, sourceProfile);
                }

                foreach (var plugin in mapping.Plugins ?? new List<Plugin>())
                {
                    if (plugin?.Outputs == null) continue;
                    foreach (var binding in plugin.Outputs)
                    {
                        RemapCopiedBinding(binding, DeviceIoType.Output, sourceProfile);
                    }
                }
            }
        }

        private void RemapCopiedBinding(DeviceBinding binding, DeviceIoType deviceIoType, Profile sourceProfile)
        {
            if (binding == null || binding.DeviceConfigurationGuid == Guid.Empty) return;

            var existing = GetDeviceConfiguration(deviceIoType, binding.DeviceConfigurationGuid);
            if (existing != null) return;

            var sourceConfiguration = sourceProfile.GetDeviceConfiguration(deviceIoType, binding.DeviceConfigurationGuid);
            if (sourceConfiguration?.Device == null) return;

            var target = GetDeviceConfigurationList(deviceIoType).FirstOrDefault(configuration =>
                configuration?.Device != null &&
                DevicesManager.PersistedIdentityEquals(configuration.Device, sourceConfiguration.Device));
            if (target != null) binding.DeviceConfigurationGuid = target.Guid;
        }

        public bool RemoveMappingGroup(MappingGroup group)
        {
            if (group == null || MappingGroups == null || !MappingGroups.Remove(group)) return false;
            PruneUndefinedFilterReferencesRecursive();
            Context?.ContextChanged();
            OnPropertyChanged(nameof(MappingGroups));
            return true;
        }

        public IEnumerable<Mapping> GetAllMappings()
        {
            foreach (var mapping in Mappings ?? new List<Mapping>()) yield return mapping;
            foreach (var group in MappingGroups ?? new List<MappingGroup>())
            {
                if (group?.Mappings == null) continue;
                foreach (var mapping in group.Mappings) yield return mapping;
            }
        }

        public IEnumerable<Mapping> GetRuntimeMappings()
        {
            foreach (var mapping in Mappings ?? new List<Mapping>()) yield return mapping;
            foreach (var group in MappingGroups ?? new List<MappingGroup>())
            {
                if (group == null || !group.Enabled || group.Mappings == null) continue;
                foreach (var mapping in group.Mappings) yield return mapping;
            }
        }

        public MappingGroup GetMappingGroup(Mapping mapping)
        {
            if (mapping == null || MappingGroups == null) return null;
            return MappingGroups.FirstOrDefault(group => group?.Mappings != null && group.Mappings.Contains(mapping));
        }

        public bool RemoveMapping(Mapping mapping)
        {
            if (mapping == null) return false;
            var removed = Mappings.Remove(mapping);
            if (!removed)
            {
                var group = GetMappingGroup(mapping);
                removed = group != null && group.Mappings.Remove(mapping);
            }
            if (!removed) return false;
            PruneUndefinedFilterReferencesRecursive();
            Context?.ContextChanged();
            return true;
        }

        public bool MoveMapping(Mapping mapping, int targetIndex)
        {
            if (mapping == null) return false;
            var container = Mappings.Contains(mapping)
                ? Mappings
                : GetMappingGroup(mapping)?.Mappings;
            if (container == null) return false;
            var sourceIndex = container.IndexOf(mapping);
            if (sourceIndex < 0 || targetIndex < 0 || targetIndex >= container.Count || targetIndex == sourceIndex) return false;

            container.RemoveAt(sourceIndex);
            container.Insert(targetIndex, mapping);
            Context?.ContextChanged();
            return true;
        }

        private string GetUniqueMappingGroupTitle(string requested)
        {
            var baseTitle = string.IsNullOrWhiteSpace(requested) ? "Group" : requested.Trim();
            var title = baseTitle;
            var number = 2;
            while ((MappingGroups ?? new List<MappingGroup>()).Any(group =>
                       group != null && string.Equals(group.Title, title, StringComparison.CurrentCultureIgnoreCase)))
            {
                title = baseTitle + " " + number++;
            }
            return title;
        }

        internal bool MigrateLegacyChildProfilesToMappingGroups()
        {
            if (ChildProfiles == null || ChildProfiles.Count == 0 || Context == null) return false;
            if (MappingGroups == null) MappingGroups = new List<MappingGroup>();

            var changed = false;
            foreach (var child in ChildProfiles.Where(profile => profile != null).ToList())
            {
                changed |= AddLegacyGroupsForDescendant(child, child.Title, new List<Profile>());
            }
            ChildProfiles.Clear();
            return changed;
        }

        private bool AddLegacyGroupsForDescendant(Profile descendant, string relativeTitle, List<Profile> inheritedPath)
        {
            if (descendant == null) return false;
            descendant.Context = Context;

            var path = new List<Profile>(inheritedPath) { descendant };
            var effectiveMappings = new List<Mapping>();
            foreach (var layer in path)
            {
                foreach (var sourceMapping in layer.Mappings ?? new List<Mapping>())
                {
                    var previous = effectiveMappings.FirstOrDefault(mapping =>
                        string.Equals(mapping.Title, sourceMapping.Title, StringComparison.CurrentCultureIgnoreCase));
                    if (previous != null) effectiveMappings.Remove(previous);
                    // MappingGroup.PostLoad below attaches each cloned mapping exactly once.
                    // Calling Mapping.PostLoad here as well used to normalize plugin output bindings
                    // twice and could strip them completely before the editor opened.
                    var clone = HidWizards.UCR.Core.Context.DeepXmlClone<Mapping>(sourceMapping);
                    effectiveMappings.Add(clone);
                }
            }

            var group = new MappingGroup(this, GetUniqueMappingGroupTitle(relativeTitle))
            {
                Enabled = false,
                Mappings = effectiveMappings,
                // Preserve the child's private devices with the Player 2 / Extras section instead
                // of making the parent profile suddenly look as though those devices were its own.
                InputDeviceConfigurations = (descendant.InputDeviceConfigurations ?? new List<DeviceConfiguration>()).ToList(),
                OutputDeviceConfigurations = (descendant.OutputDeviceConfigurations ?? new List<DeviceConfiguration>()).ToList()
            };
            group.PostLoad(Context, this);
            MappingGroups.Add(group);

            foreach (var child in descendant.ChildProfiles ?? new List<Profile>())
            {
                AddLegacyGroupsForDescendant(child, relativeTitle + " / " + child.Title, path);
            }
            return true;
        }

        private bool RepairPreviouslyMergedGroupDevices()
        {
            var changed = false;
            changed |= RepairPreviouslyMergedGroupDevices(DeviceIoType.Input);
            changed |= RepairPreviouslyMergedGroupDevices(DeviceIoType.Output);
            return changed;
        }

        private bool RepairPreviouslyMergedGroupDevices(DeviceIoType deviceIoType)
        {
            if (MappingGroups == null || MappingGroups.Count == 0) return false;

            var profileDevices = deviceIoType == DeviceIoType.Input
                ? InputDeviceConfigurations
                : OutputDeviceConfigurations;
            if (profileDevices == null || profileDevices.Count == 0) return false;

            var mainReferences = GetReferencedDeviceConfigurationGuids(Mappings, deviceIoType);
            var referencesByGroup = new Dictionary<Guid, List<MappingGroup>>();
            foreach (var group in MappingGroups.Where(group => group != null))
            {
                foreach (var guid in GetReferencedDeviceConfigurationGuids(group.Mappings, deviceIoType))
                {
                    List<MappingGroup> owners;
                    if (!referencesByGroup.TryGetValue(guid, out owners))
                    {
                        owners = new List<MappingGroup>();
                        referencesByGroup.Add(guid, owners);
                    }
                    if (!owners.Contains(group)) owners.Add(group);
                }
            }

            var changed = false;
            foreach (var pair in referencesByGroup)
            {
                if (pair.Key == Guid.Empty || pair.Value.Count != 1 || mainReferences.Contains(pair.Key)) continue;
                var configuration = profileDevices.FirstOrDefault(candidate => candidate != null && candidate.Guid == pair.Key);
                if (configuration == null) continue;

                var owner = pair.Value[0];
                var target = deviceIoType == DeviceIoType.Input
                    ? owner.InputDeviceConfigurations
                    : owner.OutputDeviceConfigurations;
                if (target == null)
                {
                    target = new List<DeviceConfiguration>();
                    if (deviceIoType == DeviceIoType.Input) owner.InputDeviceConfigurations = target;
                    else owner.OutputDeviceConfigurations = target;
                }
                if (target.Any(existing => existing != null && existing.Guid == configuration.Guid)) continue;

                profileDevices.Remove(configuration);
                target.Add(configuration);
                if (configuration.Device != null) configuration.Device.Profile = this;
                changed = true;
                Logger.Info("Restored group-private " + deviceIoType + " device to mapping group '" + owner.Title + "'.");
            }
            return changed;
        }

        private static HashSet<Guid> GetReferencedDeviceConfigurationGuids(IEnumerable<Mapping> mappings, DeviceIoType deviceIoType)
        {
            var result = new HashSet<Guid>();
            foreach (var mapping in mappings ?? Enumerable.Empty<Mapping>())
            {
                if (mapping == null) continue;
                if (deviceIoType == DeviceIoType.Input)
                {
                    foreach (var binding in mapping.DeviceBindings ?? new List<DeviceBinding>())
                    {
                        if (binding != null && binding.DeviceConfigurationGuid != Guid.Empty)
                            result.Add(binding.DeviceConfigurationGuid);
                    }
                    continue;
                }

                foreach (var plugin in mapping.Plugins ?? new List<Plugin>())
                {
                    if (plugin == null) continue;
                    foreach (var binding in plugin.Outputs ?? new List<DeviceBinding>())
                    {
                        if (binding != null && binding.DeviceConfigurationGuid != Guid.Empty)
                            result.Add(binding.DeviceConfigurationGuid);
                    }
                }
            }
            return result;
        }

        #endregion

        #region Device

        public DeviceConfiguration GetDeviceConfiguration(DeviceIoType deviceIoType, Guid deviceConfigurationGuid)
        {
            var deviceList = GetDeviceConfigurationList(deviceIoType);
            return deviceList.FirstOrDefault(configuration => configuration.Guid == deviceConfigurationGuid);
        }

        public List<DeviceConfiguration> GetDeviceConfigurationList(DeviceIoType deviceIoType)
        {
            var result = GetProfileDeviceConfigurationList(deviceIoType);
            foreach (var group in MappingGroups ?? new List<MappingGroup>())
            {
                if (group == null) continue;
                var devices = deviceIoType == DeviceIoType.Input
                    ? group.InputDeviceConfigurations
                    : group.OutputDeviceConfigurations;
                foreach (var configuration in devices ?? new List<DeviceConfiguration>())
                {
                    if (configuration == null || result.Any(existing => existing.Guid == configuration.Guid)) continue;
                    if (configuration.Device != null) configuration.Device.Profile = this;
                    result.Add(configuration);
                }
            }
            return result;
        }

        public List<DeviceConfiguration> GetProfileDeviceConfigurationList(DeviceIoType deviceIoType)
        {
            var result = new List<DeviceConfiguration>();
            if (ParentProfile != null) result.AddRange(ParentProfile.GetProfileDeviceConfigurationList(deviceIoType));

            var devices = deviceIoType == DeviceIoType.Input ? InputDeviceConfigurations : OutputDeviceConfigurations;
            foreach (var configuration in devices ?? new List<DeviceConfiguration>())
            {
                if (configuration == null || result.Any(existing => existing.Guid == configuration.Guid)) continue;
                if (configuration.Device != null) configuration.Device.Profile = this;
                result.Add(configuration);
            }
            return result;
        }

        public DeviceConfiguration GetPrimaryDeviceConfiguration(DeviceIoType deviceIoType)
        {
            // Group-private devices are peripheral to the parent profile and must never silently
            // become its primary dashboard/device-list identity.
            var devices = GetProfileDeviceConfigurationList(deviceIoType);
            if (devices.Count == 0) return null;

            var primaryGuid = deviceIoType == DeviceIoType.Input
                ? PrimaryInputDeviceConfigurationGuid
                : PrimaryOutputDeviceConfigurationGuid;

            if (primaryGuid != Guid.Empty)
            {
                var configuredPrimary = devices.FirstOrDefault(configuration => configuration.Guid == primaryGuid);
                if (configuredPrimary != null) return configuredPrimary;
            }

            if (ParentProfile != null)
            {
                var inheritedPrimary = ParentProfile.GetPrimaryDeviceConfiguration(deviceIoType);
                if (inheritedPrimary != null)
                {
                    var inheritedMatch = devices.FirstOrDefault(configuration => configuration.Guid == inheritedPrimary.Guid);
                    if (inheritedMatch != null) return inheritedMatch;
                }
            }

            return devices[0];
        }

        public bool SetPrimaryDeviceConfiguration(DeviceIoType deviceIoType, Guid deviceConfigurationGuid)
        {
            if (deviceConfigurationGuid != Guid.Empty &&
                GetDeviceConfigurationList(deviceIoType).All(configuration => configuration.Guid != deviceConfigurationGuid))
            {
                return false;
            }

            if (deviceIoType == DeviceIoType.Input)
            {
                if (PrimaryInputDeviceConfigurationGuid == deviceConfigurationGuid) return true;
                PrimaryInputDeviceConfigurationGuid = deviceConfigurationGuid;
                OnPropertyChanged(nameof(PrimaryInputDeviceConfigurationGuid));
            }
            else
            {
                if (PrimaryOutputDeviceConfigurationGuid == deviceConfigurationGuid) return true;
                PrimaryOutputDeviceConfigurationGuid = deviceConfigurationGuid;
                OnPropertyChanged(nameof(PrimaryOutputDeviceConfigurationGuid));
            }

            Context?.ContextChanged();
            return true;
        }

        public List<Device> GetMissingDeviceList(DeviceIoType deviceIoType)
        {
            Context.DevicesManager.RefreshDeviceList();
            var availableDeviceList = Context.DevicesManager.GetVisibleDeviceList(deviceIoType);
            var profileDeviceList = GetDeviceConfigurationList(deviceIoType);

            foreach (var deviceConfiguration in profileDeviceList)
            {
                var resolvedDevice = Context.DevicesManager.ResolveDevice(deviceConfiguration.Device, deviceIoType);
                if (resolvedDevice != null)
                {
                    availableDeviceList.RemoveAll(d => DevicesManager.DescriptorEquals(d, resolvedDevice)
                                                       || DevicesManager.PersistedIdentityEquals(d, deviceConfiguration.Device));
                }
                else
                {
                    availableDeviceList.RemoveAll(d => DevicesManager.PersistedIdentityEquals(d, deviceConfiguration.Device));
                }
            }

            return availableDeviceList;
        }

        public void AddDeviceConfigurations(List<DeviceConfiguration> deviceConfigurations, DeviceIoType deviceIoType)
        {
            deviceConfigurations.ForEach(configuration => configuration.Device.Profile = this);
            var deviceList = deviceIoType == DeviceIoType.Input ? InputDeviceConfigurations : OutputDeviceConfigurations;

            deviceList.AddRange(deviceConfigurations);
            OnPropertyChanged(deviceIoType == DeviceIoType.Input ? nameof(InputDeviceConfigurations) : nameof(OutputDeviceConfigurations));
            Context.ContextChanged();
        }

        public bool RemoveDeviceConfiguration(DeviceConfiguration device)
        {
            var success = InputDeviceConfigurations.Remove(device) || OutputDeviceConfigurations.Remove(device);
            if (success)
            {
                OnPropertyChanged(nameof(InputDeviceConfigurations));
                OnPropertyChanged(nameof(OutputDeviceConfigurations));
                Context.ContextChanged();
            }

            return success;
        }

        public bool CanRemoveDeviceConfiguration(DeviceConfiguration device)
        {
            return InputDeviceConfigurations.Contains(device) || OutputDeviceConfigurations.Contains(device);

        }
        #endregion

        #region Plugin

        public bool AddNewPlugin(Mapping mapping, Plugin plugin)
        {
            return AddPlugin(mapping, (Plugin)Activator.CreateInstance(plugin.GetType()));
        }

        public bool AddPlugin(Mapping mapping, Plugin plugin)
        {
            if (!OwnsMapping(mapping)) return false;
            mapping.AddPlugin(plugin);
            return true;
        }

        public bool RemovePlugin(Mapping mapping, Plugin plugin)
        {
            if (!OwnsMapping(mapping)) return false;
            mapping.Plugins.Remove(plugin);
            PruneUndefinedFilterReferencesRecursive();
            Context.ContextChanged();
            return true;
        }

        private bool OwnsMapping(Mapping mapping)
        {
            return mapping != null && (Mappings.Contains(mapping) || GetMappingGroup(mapping) != null);
        }

        #endregion

        public HashSet<string> GetFilters()
        {
            // Filter definitions are created by "... to Filter" plugins. A plugin's Filters list
            // contains references to those definitions; it must never create definitions by itself.
            var result = ParentProfile != null
                ? ParentProfile.GetFilters()
                : new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);

            foreach (var mapping in GetAllMappings())
            {
                foreach (var plugin in mapping.Plugins)
                {
                    var definedFilterName = plugin.GetDefinedFilterName();
                    if (!string.IsNullOrWhiteSpace(definedFilterName)) result.Add(definedFilterName);
                }
            }

            return result;
        }

        internal void RenameFilterReferences(string oldName, string newName)
        {
            if (string.IsNullOrWhiteSpace(oldName)) return;
            var replacement = string.IsNullOrWhiteSpace(newName) ? null : newName.Trim();

            RenameFilterReferencesRecursive(this, oldName.Trim(), replacement);
        }

        private static void RenameFilterReferencesRecursive(Profile profile, string oldName, string newName)
        {
            // If another definition with the old name is still visible at this profile level,
            // existing references remain valid and must not be silently redirected.
            if (profile.GetFilters().Contains(oldName)) return;

            foreach (var mapping in profile.GetAllMappings())
            {
                foreach (var plugin in mapping.Plugins)
                {
                    if (newName == null)
                    {
                        plugin.Filters.RemoveAll(filter => string.Equals(filter.Name, oldName, StringComparison.InvariantCultureIgnoreCase));
                        continue;
                    }

                    foreach (var filter in plugin.Filters)
                    {
                        if (!string.Equals(filter.Name, oldName, StringComparison.InvariantCultureIgnoreCase)) continue;
                        filter.Name = newName;
                    }
                }
            }

            foreach (var child in profile.ChildProfiles)
            {
                RenameFilterReferencesRecursive(child, oldName, newName);
            }
        }

        internal bool PruneUndefinedFilterReferencesRecursive()
        {
            return PruneUndefinedFilterReferencesRecursive(this);
        }

        private static bool PruneUndefinedFilterReferencesRecursive(Profile profile)
        {
            var changed = false;
            var validNames = profile.GetFilters();
            foreach (var mapping in profile.GetAllMappings())
            {
                foreach (var plugin in mapping.Plugins)
                {
                    changed |= plugin.Filters.RemoveAll(filter => filter == null || string.IsNullOrWhiteSpace(filter.Name) || !validNames.Contains(filter.Name)) > 0;
                }
            }

            foreach (var child in profile.ChildProfiles)
            {
                changed |= PruneUndefinedFilterReferencesRecursive(child);
            }
            return changed;
        }

        #region Helpers

        public string ProfileBreadCrumbs()
        {
            return ParentProfile != null ? ParentProfile.ProfileBreadCrumbs() + " > " + Title : Title;
        }

        /// <summary>
        /// Returns true if bindings are currently subscribed to the backend
        /// </summary>
        /// <returns></returns>
        public bool IsActive()
        {
            return Context?.SubscriptionsManager != null && Context.SubscriptionsManager.IsProfileActive(Guid);
        }

        #endregion

        internal void PostLoad(Context context, Profile parentProfile = null)
        {
            ParentProfile = parentProfile;
            if (ChildProfiles == null) ChildProfiles = new List<Profile>();
            if (Mappings == null) Mappings = new List<Mapping>();
            if (MappingGroups == null) MappingGroups = new List<MappingGroup>();
            if (InputDeviceConfigurations == null) InputDeviceConfigurations = new List<DeviceConfiguration>();
            if (OutputDeviceConfigurations == null) OutputDeviceConfigurations = new List<DeviceConfiguration>();

            if (AutoActivateApplications == null) AutoActivateApplications = new ObservableCollection<ProfileApplicationRule>();
            foreach (var rule in AutoActivateApplications) rule?.Attach(this);
            if (AutoActivateApplications.Count == 0 && !string.IsNullOrWhiteSpace(_autoActivateExecutable))
            {
                // Migrate the legacy single-executable field without making a freshly loaded
                // context look user-modified simply because it was opened by a newer UCR.
                AutoActivateApplications.CollectionChanged -= AutoActivateApplicationsOnCollectionChanged;
                try
                {
                    var legacyRule = new ProfileApplicationRule(_autoActivateExecutable);
                    legacyRule.Attach(this);
                    AutoActivateApplications.Add(legacyRule);
                    _autoActivateExecutable = null;
                }
                finally
                {
                    AutoActivateApplications.CollectionChanged += AutoActivateApplicationsOnCollectionChanged;
                }
            }

            Context = context;

            foreach (var profile in ChildProfiles)
            {
                profile.PostLoad(context, this);
            }

            foreach (var mapping in Mappings)
            {
                mapping.PostLoad(context, this);
            }
            foreach (var group in MappingGroups.Where(group => group != null))
            {
                group.PostLoad(context, this);
            }

            // Repair data saved by the first multi-profile build, which moved child-only devices
            // into the parent profile. A device referenced only by one local group belongs with that
            // group; shared/main devices remain at profile scope.
            if (RepairPreviouslyMergedGroupDevices()) Context?.ContextChanged();

            // Top-level legacy trees are converted as part of attachment, before any dashboard or
            // runtime code can observe them. ParentProfile is retained only for old import paths.
            if (parentProfile == null && MigrateLegacyChildProfilesToMappingGroups()) Context?.ContextChanged();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}