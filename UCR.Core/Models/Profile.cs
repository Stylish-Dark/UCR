using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
        public List<Profile> ChildProfiles { get; set; }
        public List<Mapping> Mappings { get; set; }

        public List<DeviceConfiguration> InputDeviceConfigurations { get; set; }
        public List<DeviceConfiguration> OutputDeviceConfigurations { get; set; }

        private bool _autoActivateEnabled;
        private string _autoActivateExecutable;

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
            InputDeviceConfigurations = new List<DeviceConfiguration>();
            OutputDeviceConfigurations = new List<DeviceConfiguration>();
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
            return Context.SubscriptionsManager.DeactivateCurrentProfile();
        }

        internal void PrepareProfile()
        {
            
        }

        #endregion

        #region Mapping

        public Mapping AddMapping(string title)
        {
            var mapping = new Mapping(this, title);
            Mappings.Add(mapping);
            Context.ContextChanged();
            return mapping;
        }

        public bool RemoveMapping(Mapping mapping)
        {
            if (!Mappings.Remove(mapping)) return false;
            PruneUndefinedFilterReferencesRecursive();
            Context.ContextChanged();
            return true;
        }

        public bool MoveMapping(Mapping mapping, int targetIndex)
        {
            if (mapping == null) return false;
            var sourceIndex = Mappings.IndexOf(mapping);
            if (sourceIndex < 0) return false;
            if (targetIndex < 0 || targetIndex >= Mappings.Count || targetIndex == sourceIndex) return false;

            Mappings.RemoveAt(sourceIndex);
            Mappings.Insert(targetIndex, mapping);
            Context.ContextChanged();
            return true;
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
            var result = new List<DeviceConfiguration>();
            if (ParentProfile != null) result.AddRange(ParentProfile.GetDeviceConfigurationList(deviceIoType));

            var devices = deviceIoType == DeviceIoType.Input ? InputDeviceConfigurations : OutputDeviceConfigurations;
            devices.ForEach(d => d.Device.Profile = this);
            result.AddRange(devices);

            return result;
        }

        public DeviceConfiguration GetPrimaryDeviceConfiguration(DeviceIoType deviceIoType)
        {
            var devices = GetDeviceConfigurationList(deviceIoType);
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
            if (!Mappings.Contains(mapping)) return false;
            mapping.AddPlugin(plugin);
            return true;
        }

        public bool RemovePlugin(Mapping mapping, Plugin plugin)
        {
            if (!Mappings.Contains(mapping)) return false;
            mapping.Plugins.Remove(plugin);
            PruneUndefinedFilterReferencesRecursive();
            Context.ContextChanged();
            return true;
        }

        #endregion

        public HashSet<string> GetFilters()
        {
            // Filter definitions are created by "... to Filter" plugins. A plugin's Filters list
            // contains references to those definitions; it must never create definitions by itself.
            var result = ParentProfile != null
                ? ParentProfile.GetFilters()
                : new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);

            foreach (var mapping in Mappings)
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

            foreach (var mapping in profile.Mappings)
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
            foreach (var mapping in profile.Mappings)
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
            return Context.SubscriptionsManager.GetActiveProfile() != null && Context.SubscriptionsManager.GetActiveProfile().Guid == Guid;
        }

        #endregion

        internal void PostLoad(Context context, Profile parentProfile = null)
        {
            Context = context;
            ParentProfile = parentProfile;

            foreach (var profile in ChildProfiles)
            {
                profile.PostLoad(context, this);
            }

            foreach (var mapping in Mappings)
            {
                mapping.PostLoad(context, this);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}