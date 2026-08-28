using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Serialization;
using HidWizards.UCR.Core.Models;
using HidWizards.UCR.Core.Models.Binding;
using NLog;

namespace HidWizards.UCR.Core.Managers
{
    public enum ProfileExportKind
    {
        Profile,
        ProfileList
    }

    public enum ProfileListImportMode
    {
        Merge,
        Replace
    }

    [XmlRoot("UcrExport")]
    public class ProfileExportPackage
    {
        [XmlAttribute]
        public int FormatVersion { get; set; }

        [XmlAttribute]
        public ProfileExportKind Kind { get; set; }

        [XmlArray("Profiles")]
        [XmlArrayItem("Profile")]
        public List<Profile> Profiles { get; set; }

        [XmlArray("DeviceAliases")]
        [XmlArrayItem("DeviceAlias")]
        public List<DeviceAlias> DeviceAliases { get; set; }

        public ProfileExportPackage()
        {
            Profiles = new List<Profile>();
            DeviceAliases = new List<DeviceAlias>();
        }
    }

    public class ProfilesManager
    {
        private const int ExportFormatVersion = 1;
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly Context _context;
        private readonly List<Profile> _profiles;

        public ProfilesManager(Context context, List<Profile> profiles)
        {
            _context = context;
            _profiles = profiles;
        }

        public Profile CreateProfile(string title, List<DeviceConfiguration> inputDevices, List<DeviceConfiguration> outputDevices)
        {
            return Profile.CreateProfile(_context, title, inputDevices, outputDevices);
        }

        public bool AddProfile(Profile newProfile, Profile parentProfile = null)
        {
            if (parentProfile != null)
            {
                parentProfile.AddChildProfile(newProfile);
            }
            else
            {
                _profiles.Add(newProfile);
            }

            _context.ContextChanged();
            return true;
        }

        public bool CopyProfile(Profile profile, string title = "Untitled")
        {
            var newProfile = Context.DeepXmlClone<Profile>(profile);
            newProfile.Title = title;
            newProfile.Guid = Guid.NewGuid();
            newProfile.PostLoad(_context, profile.ParentProfile);

            if (profile.ParentProfile != null)
            {
                profile.ParentProfile.AddChildProfile(newProfile);
            }
            else
            {
                _profiles.Add(newProfile);
            }

            // TODO Fix Configuration Guid and referenced DeviceBinding Guids
            //newProfile.InputDeviceConfigurations.ForEach(configuration => configuration.Guid = Guid.NewGuid());
            //newProfile.OutputDeviceConfigurations.ForEach(configuration => configuration.Guid = Guid.NewGuid());

            _context.ContextChanged();

            return true;
        }

        #region Import / Export

        /// <summary>
        /// Exports one profile branch as a self-contained package. If the selected profile inherits
        /// mappings or devices from ancestors, those effective inherited dependencies are flattened
        /// into the exported root so that importing it as a standalone profile preserves behaviour.
        /// Child profiles are included.
        /// </summary>
        public void ExportProfile(Profile profile, string filePath, List<Type> pluginTypes = null)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            ValidateFilePath(filePath);

            var portableProfile = CreatePortableProfileClone(profile, pluginTypes);
            var package = new ProfileExportPackage
            {
                FormatVersion = ExportFormatVersion,
                Kind = ProfileExportKind.Profile,
                Profiles = new List<Profile> { portableProfile },
                DeviceAliases = CollectAliasesForProfiles(new[] { portableProfile })
            };

            ValidatePackage(package, ProfileExportKind.Profile);
            SerializePackage(filePath, package, pluginTypes);
        }

        /// <summary>
        /// Imports a single profile branch. Imported profile and device-configuration identifiers are
        /// regenerated so the same package can safely be imported more than once into one context.
        /// </summary>
        public Profile ImportProfile(string filePath, Profile parentProfile = null, List<Type> pluginTypes = null)
        {
            ValidateFilePath(filePath);
            var package = DeserializePackage(filePath, pluginTypes);
            ValidatePackage(package, ProfileExportKind.Profile);

            RegenerateIdentities(package.Profiles);

            var profile = package.Profiles[0];
            profile.PostLoad(_context, parentProfile);
            AddProfile(profile, parentProfile);
            _context.DevicesManager.MergeDeviceAliases(package.DeviceAliases, false);
            return profile;
        }

        /// <summary>
        /// Exports the complete top-level profile list, including all child profiles, mappings,
        /// configured devices and bindings.
        /// </summary>
        public void ExportProfileList(string filePath, List<Type> pluginTypes = null)
        {
            ValidateFilePath(filePath);

            var package = new ProfileExportPackage
            {
                FormatVersion = ExportFormatVersion,
                Kind = ProfileExportKind.ProfileList,
                Profiles = _profiles,
                DeviceAliases = (_context.DeviceAliases ?? new List<DeviceAlias>())
                    .Where(alias => alias != null)
                    .Select(alias => alias.Clone())
                    .ToList()
            };

            ValidatePackage(package, ProfileExportKind.ProfileList);
            SerializePackage(filePath, package, pluginTypes);
        }

        /// <summary>
        /// Imports a complete profile-list package. Replace preserves the package identifiers exactly,
        /// making it suitable for backup/restore. Merge regenerates all imported identifiers before
        /// adding them so collisions with existing profiles/configurations cannot occur.
        /// </summary>
        public int ImportProfileList(string filePath, ProfileListImportMode mode, List<Type> pluginTypes = null)
        {
            ValidateFilePath(filePath);
            var package = DeserializePackage(filePath, pluginTypes);
            ValidatePackage(package, ProfileExportKind.ProfileList);

            if (mode == ProfileListImportMode.Merge)
            {
                RegenerateIdentities(package.Profiles);
                _context.DevicesManager.MergeDeviceAliases(package.DeviceAliases, false);
                foreach (var profile in package.Profiles)
                {
                    profile.PostLoad(_context);
                    _profiles.Add(profile);
                }
            }
            else if (mode == ProfileListImportMode.Replace)
            {
                if (_context.ActiveProfile != null && !_context.SubscriptionsManager.DeactivateCurrentProfile())
                {
                    throw new InvalidOperationException("The active profile could not be deactivated before replacing the profile list.");
                }

                _profiles.Clear();
                _context.DevicesManager.ReplaceDeviceAliases(package.DeviceAliases);
                foreach (var profile in package.Profiles)
                {
                    profile.PostLoad(_context);
                    _profiles.Add(profile);
                }
            }
            else
            {
                throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown profile-list import mode.");
            }

            _context.ContextChanged();
            return package.Profiles.Count;
        }

        private List<DeviceAlias> CollectAliasesForProfiles(IEnumerable<Profile> profileRoots)
        {
            var result = new List<DeviceAlias>();
            if (profileRoots == null || _context.DeviceAliases == null) return result;

            foreach (var profile in EnumerateProfiles(profileRoots))
            {
                foreach (var configuration in EnumerateLocalConfigurations(profile))
                {
                    AddAliasForDevice(configuration?.Device, result);
                    if (configuration?.ShadowDevices == null) continue;
                    foreach (var shadowDevice in configuration.ShadowDevices)
                    {
                        AddAliasForDevice(shadowDevice, result);
                    }
                }
            }

            return result;
        }

        private void AddAliasForDevice(Device device, ICollection<DeviceAlias> aliases)
        {
            var identity = DevicesManager.BuildAliasIdentity(device);
            if (identity == null) return;

            var alias = _context.DeviceAliases.FirstOrDefault(candidate =>
                DevicesManager.AliasIdentityEquals(candidate, identity));
            if (alias == null || string.IsNullOrWhiteSpace(alias.Alias) ||
                aliases.Any(candidate => DevicesManager.AliasIdentityEquals(candidate, alias))) return;

            // Single-profile portability carries the friendly name only. Hide/order are local UI
            // preferences and must not unexpectedly rearrange another machine when a profile is imported.
            var portableAlias = alias.Clone();
            portableAlias.Hidden = false;
            portableAlias.Removed = false;
            portableAlias.SortOrder = int.MaxValue;
            aliases.Add(portableAlias);
        }

        private static IEnumerable<Profile> EnumerateProfiles(IEnumerable<Profile> roots)
        {
            foreach (var profile in roots ?? Enumerable.Empty<Profile>())
            {
                if (profile == null) continue;
                yield return profile;
                foreach (var child in EnumerateProfiles(profile.ChildProfiles))
                {
                    yield return child;
                }
            }
        }

        private Profile CreatePortableProfileClone(Profile profile, List<Type> pluginTypes)
        {
            var clone = Clone(profile, pluginTypes);

            // A child profile is only meaningful together with its ancestors. Flatten the effective
            // device configuration set and effective mapping set into the exported root. Descendants
            // remain nested under that root and therefore inherit the same effective state after import.
            clone.InputDeviceConfigurations = profile.GetDeviceConfigurationList(DeviceIoType.Input)
                .Select(configuration => Clone(configuration, pluginTypes))
                .ToList();
            clone.OutputDeviceConfigurations = profile.GetDeviceConfigurationList(DeviceIoType.Output)
                .Select(configuration => Clone(configuration, pluginTypes))
                .ToList();
            clone.Mappings = GetEffectiveMappings(profile)
                .Select(mapping => Clone(mapping, pluginTypes))
                .ToList();

            clone.PostLoad(_context);
            return clone;
        }

        private static List<Mapping> GetEffectiveMappings(Profile profile)
        {
            var hierarchy = new List<Profile>();
            var current = profile;
            while (current != null)
            {
                hierarchy.Add(current);
                current = current.ParentProfile;
            }
            hierarchy.Reverse();

            var effectiveMappings = new List<Mapping>();
            foreach (var level in hierarchy)
            {
                var overriddenTitles = new HashSet<string>(
                    level.Mappings.Select(mapping => mapping.Title ?? string.Empty),
                    StringComparer.Ordinal);

                effectiveMappings.RemoveAll(mapping => overriddenTitles.Contains(mapping.Title ?? string.Empty));
                effectiveMappings.AddRange(level.Mappings);
            }

            return effectiveMappings;
        }

        private static T Clone<T>(T value, List<Type> pluginTypes)
        {
            if (value == null) return default(T);

            var serializer = Context.GetXmlSerializer(pluginTypes, typeof(T));
            using (var stream = new MemoryStream())
            {
                serializer.Serialize(stream, value);
                stream.Position = 0;
                return (T)serializer.Deserialize(stream);
            }
        }

        private static void SerializePackage(string filePath, ProfileExportPackage package, List<Type> pluginTypes)
        {
            var serializer = Context.GetXmlSerializer(pluginTypes, typeof(ProfileExportPackage));
            using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(fileStream, new UTF8Encoding(false)))
            {
                serializer.Serialize(writer, package);
            }
        }

        private static ProfileExportPackage DeserializePackage(string filePath, List<Type> pluginTypes)
        {
            var serializer = Context.GetXmlSerializer(pluginTypes, typeof(ProfileExportPackage));
            using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                return (ProfileExportPackage)serializer.Deserialize(fileStream);
            }
        }

        private static void ValidateFilePath(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("A file path is required.", nameof(filePath));
            }
        }

        private static void ValidatePackage(ProfileExportPackage package, ProfileExportKind expectedKind)
        {
            if (package == null) throw new InvalidDataException("The UCR export file is empty or invalid.");
            if (package.FormatVersion != ExportFormatVersion)
            {
                throw new InvalidDataException(
                    $"Unsupported UCR export format version {package.FormatVersion}. Expected version {ExportFormatVersion}.");
            }
            if (package.Kind != expectedKind)
            {
                throw new InvalidDataException($"This file contains {package.Kind}, not {expectedKind} data.");
            }
            if (package.Profiles == null)
            {
                throw new InvalidDataException("The UCR export contains no profile collection.");
            }
            if (expectedKind == ProfileExportKind.Profile && package.Profiles.Count != 1)
            {
                throw new InvalidDataException("A single-profile export must contain exactly one profile root.");
            }
            if (package.Profiles.Any(profile => profile == null))
            {
                throw new InvalidDataException("The UCR export contains a null profile entry.");
            }

            if (package.DeviceAliases == null) package.DeviceAliases = new List<DeviceAlias>();
            ValidateAliases(package.DeviceAliases);
            ValidateProfileGraph(package.Profiles);
        }

        private static void ValidateAliases(IEnumerable<DeviceAlias> aliases)
        {
            var seen = new List<DeviceAlias>();
            foreach (var alias in aliases)
            {
                if (alias == null || string.IsNullOrWhiteSpace(alias.ProviderName) ||
                    string.IsNullOrWhiteSpace(alias.IdentityValue) || !alias.HasPresentationSettings)
                {
                    throw new InvalidDataException("The UCR export contains invalid device presentation settings.");
                }
                if (alias.IdentityKind == DeviceAliasIdentityKind.LogicalSlot && alias.DeviceNumber < 0)
                {
                    throw new InvalidDataException("The UCR export contains an invalid logical-slot device alias.");
                }
                if (alias.SortOrder < 0)
                {
                    throw new InvalidDataException("The UCR export contains an invalid device display order.");
                }
                if (seen.Any(existing => DevicesManager.AliasIdentityEquals(existing, alias)))
                {
                    throw new InvalidDataException("The UCR export contains duplicate device alias identities.");
                }
                seen.Add(alias);
            }
        }

        private static void ValidateProfileGraph(IEnumerable<Profile> profileRoots)
        {
            foreach (var root in profileRoots)
            {
                ValidateProfileStructure(root);
            }
        }

        private static void ValidateProfileStructure(Profile profile)
        {
            if (profile == null) throw new InvalidDataException("The UCR export contains a null profile.");
            if (profile.ChildProfiles == null) throw new InvalidDataException("A profile has no child-profile collection.");
            if (profile.Mappings == null) throw new InvalidDataException("A profile has no mapping collection.");
            if (profile.InputDeviceConfigurations == null || profile.OutputDeviceConfigurations == null)
            {
                throw new InvalidDataException("A profile has an invalid device-configuration collection.");
            }

            foreach (var configuration in EnumerateLocalConfigurations(profile))
            {
                if (configuration == null || configuration.Device == null)
                {
                    throw new InvalidDataException("The UCR export contains an invalid device configuration.");
                }
            }

            foreach (var mapping in profile.Mappings)
            {
                if (mapping == null || mapping.DeviceBindings == null || mapping.Plugins == null)
                {
                    throw new InvalidDataException("The UCR export contains an invalid mapping.");
                }
                if (mapping.DeviceBindings.Any(binding => binding == null))
                {
                    throw new InvalidDataException("The UCR export contains a null input binding.");
                }
                foreach (var plugin in mapping.Plugins)
                {
                    if (plugin == null || plugin.Outputs == null || plugin.Outputs.Any(binding => binding == null))
                    {
                        throw new InvalidDataException("The UCR export contains an invalid mapping plugin.");
                    }
                }
            }

            foreach (var child in profile.ChildProfiles)
            {
                ValidateProfileStructure(child);
            }
        }

        private static IEnumerable<DeviceConfiguration> EnumerateLocalConfigurations(Profile profile)
        {
            if (profile.InputDeviceConfigurations != null)
            {
                foreach (var configuration in profile.InputDeviceConfigurations) yield return configuration;
            }
            if (profile.OutputDeviceConfigurations != null)
            {
                foreach (var configuration in profile.OutputDeviceConfigurations) yield return configuration;
            }
        }

        private static void RegenerateIdentities(IEnumerable<Profile> profileRoots)
        {
            foreach (var root in profileRoots)
            {
                var bindingTargets = new Dictionary<DeviceBinding, DeviceConfiguration>();
                var unresolvedBindingTargets = new Dictionary<DeviceBinding, Guid>();
                var unresolvedGuidMap = new Dictionary<Guid, Guid>();
                var primaryInputTargets = new Dictionary<Profile, DeviceConfiguration>();
                var primaryOutputTargets = new Dictionary<Profile, DeviceConfiguration>();

                ResolveBindingTargets(
                    root,
                    new Dictionary<Guid, DeviceConfiguration>(),
                    new Dictionary<Guid, DeviceConfiguration>(),
                    bindingTargets,
                    unresolvedBindingTargets,
                    unresolvedGuidMap);
                CapturePrimaryDeviceTargets(
                    root,
                    new Dictionary<Guid, DeviceConfiguration>(),
                    new Dictionary<Guid, DeviceConfiguration>(),
                    primaryInputTargets,
                    primaryOutputTargets);

                RegenerateProfileAndConfigurationGuids(root);

                foreach (var bindingTarget in bindingTargets)
                {
                    bindingTarget.Key.DeviceConfigurationGuid = bindingTarget.Value.Guid;
                }
                foreach (var unresolvedBindingTarget in unresolvedBindingTargets)
                {
                    unresolvedBindingTarget.Key.DeviceConfigurationGuid = unresolvedBindingTarget.Value;
                }
                foreach (var primaryInputTarget in primaryInputTargets)
                {
                    primaryInputTarget.Key.PrimaryInputDeviceConfigurationGuid = primaryInputTarget.Value.Guid;
                }
                foreach (var primaryOutputTarget in primaryOutputTargets)
                {
                    primaryOutputTarget.Key.PrimaryOutputDeviceConfigurationGuid = primaryOutputTarget.Value.Guid;
                }
            }
        }

        private static void ResolveBindingTargets(Profile profile,
            IDictionary<Guid, DeviceConfiguration> inheritedInputs,
            IDictionary<Guid, DeviceConfiguration> inheritedOutputs,
            IDictionary<DeviceBinding, DeviceConfiguration> bindingTargets,
            IDictionary<DeviceBinding, Guid> unresolvedBindingTargets,
            IDictionary<Guid, Guid> unresolvedGuidMap)
        {
            var inputs = new Dictionary<Guid, DeviceConfiguration>(inheritedInputs);
            var outputs = new Dictionary<Guid, DeviceConfiguration>(inheritedOutputs);

            // Match Profile.GetDeviceConfigurationList + FirstOrDefault semantics: inherited configurations
            // appear first, so an inherited identifier wins over a duplicate identifier declared lower down.
            foreach (var configuration in profile.InputDeviceConfigurations)
            {
                if (!inputs.ContainsKey(configuration.Guid)) inputs.Add(configuration.Guid, configuration);
            }
            foreach (var configuration in profile.OutputDeviceConfigurations)
            {
                if (!outputs.ContainsKey(configuration.Guid)) outputs.Add(configuration.Guid, configuration);
            }

            foreach (var mapping in profile.Mappings)
            {
                foreach (var binding in mapping.DeviceBindings)
                {
                    CaptureBindingTarget(binding, inputs, bindingTargets, unresolvedBindingTargets, unresolvedGuidMap);
                }
                foreach (var plugin in mapping.Plugins)
                {
                    foreach (var binding in plugin.Outputs)
                    {
                        CaptureBindingTarget(binding, outputs, bindingTargets, unresolvedBindingTargets, unresolvedGuidMap);
                    }
                }
            }

            foreach (var child in profile.ChildProfiles)
            {
                ResolveBindingTargets(child, inputs, outputs, bindingTargets, unresolvedBindingTargets, unresolvedGuidMap);
            }
        }

        private static void CapturePrimaryDeviceTargets(Profile profile,
            IDictionary<Guid, DeviceConfiguration> inheritedInputs,
            IDictionary<Guid, DeviceConfiguration> inheritedOutputs,
            IDictionary<Profile, DeviceConfiguration> primaryInputTargets,
            IDictionary<Profile, DeviceConfiguration> primaryOutputTargets)
        {
            var inputs = new Dictionary<Guid, DeviceConfiguration>(inheritedInputs);
            var outputs = new Dictionary<Guid, DeviceConfiguration>(inheritedOutputs);

            foreach (var configuration in profile.InputDeviceConfigurations)
            {
                if (!inputs.ContainsKey(configuration.Guid)) inputs.Add(configuration.Guid, configuration);
            }
            foreach (var configuration in profile.OutputDeviceConfigurations)
            {
                if (!outputs.ContainsKey(configuration.Guid)) outputs.Add(configuration.Guid, configuration);
            }

            DeviceConfiguration primaryInput;
            if (profile.PrimaryInputDeviceConfigurationGuid != Guid.Empty &&
                inputs.TryGetValue(profile.PrimaryInputDeviceConfigurationGuid, out primaryInput))
            {
                primaryInputTargets[profile] = primaryInput;
            }
            else if (profile.PrimaryInputDeviceConfigurationGuid != Guid.Empty)
            {
                profile.PrimaryInputDeviceConfigurationGuid = Guid.Empty;
            }

            DeviceConfiguration primaryOutput;
            if (profile.PrimaryOutputDeviceConfigurationGuid != Guid.Empty &&
                outputs.TryGetValue(profile.PrimaryOutputDeviceConfigurationGuid, out primaryOutput))
            {
                primaryOutputTargets[profile] = primaryOutput;
            }
            else if (profile.PrimaryOutputDeviceConfigurationGuid != Guid.Empty)
            {
                profile.PrimaryOutputDeviceConfigurationGuid = Guid.Empty;
            }

            foreach (var child in profile.ChildProfiles)
            {
                CapturePrimaryDeviceTargets(child, inputs, outputs, primaryInputTargets, primaryOutputTargets);
            }
        }

        private static void CaptureBindingTarget(DeviceBinding binding,
            IDictionary<Guid, DeviceConfiguration> availableConfigurations,
            IDictionary<DeviceBinding, DeviceConfiguration> bindingTargets,
            IDictionary<DeviceBinding, Guid> unresolvedBindingTargets,
            IDictionary<Guid, Guid> unresolvedGuidMap)
        {
            if (binding.DeviceConfigurationGuid == Guid.Empty) return;

            DeviceConfiguration target;
            if (availableConfigurations.TryGetValue(binding.DeviceConfigurationGuid, out target))
            {
                bindingTargets.Add(binding, target);
                return;
            }

            // UCR intentionally tolerates unavailable/removed devices. Keep such a binding unresolved,
            // but give the missing reference a fresh identifier so importing as a child cannot
            // accidentally bind it to an unrelated configuration in the destination hierarchy.
            Guid replacementGuid;
            if (!unresolvedGuidMap.TryGetValue(binding.DeviceConfigurationGuid, out replacementGuid))
            {
                replacementGuid = Guid.NewGuid();
                unresolvedGuidMap.Add(binding.DeviceConfigurationGuid, replacementGuid);
            }
            unresolvedBindingTargets.Add(binding, replacementGuid);
        }

        private static void RegenerateProfileAndConfigurationGuids(Profile profile)
        {
            profile.Guid = Guid.NewGuid();
            foreach (var configuration in EnumerateLocalConfigurations(profile))
            {
                configuration.Guid = Guid.NewGuid();
            }
            foreach (var child in profile.ChildProfiles)
            {
                RegenerateProfileAndConfigurationGuids(child);
            }
        }

        #endregion

        /// <summary>
        /// Breadth-first search for nested profiles
        /// Find first search result and looks for the next result in the children
        /// </summary>
        /// <param name="search">List of profiles to search for nested under each other</param>
        /// <returns>The most specific profile found in the chain, otherwise null</returns>
        public Profile FindProfile(List<string> search)
        {
            Logger.Debug($"Searching for profile: {{{string.Join(",", search)}}}");
            Profile foundProfile = null;
            if (search?.Count == 0) return null;
            var queue = new List<Profile>();
            queue.AddRange(_profiles);
            while (queue.Count > 0)
            {
                var profile = queue[0];
                queue.RemoveAt(0);
                if (profile.Title.ToLower().Equals(search.First().ToLower()))
                {
                    if (search.Count == 1)
                    {
                        Logger.Debug($"Found profile: {{{profile.ProfileBreadCrumbs()}}}");
                        return profile;
                    }
                    foundProfile = profile;
                    search.RemoveAt(0);
                    Logger.Trace($"Found intermediate profile: {{{profile.ProfileBreadCrumbs()}}}. Remaining search: {{{string.Join(",", search)}}}");
                    queue.Clear();
                }
                if (profile.ChildProfiles != null) queue.AddRange(profile.ChildProfiles);

            }
            if (foundProfile == null) Logger.Debug($"No profile found for {{{string.Join(",", search)}}}");
            return foundProfile;
        }
    }
}
