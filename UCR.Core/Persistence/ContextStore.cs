using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HidWizards.UCR.Core.Managers;
using HidWizards.UCR.Core.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;

namespace HidWizards.UCR.Core.Persistence
{
    internal sealed class ContextStore
    {
        private const int SchemaVersion = 1;
        private const int BackupLimitPerFile = 5;
        private const string StateFileName = "state.json";
        private const string DevicesFileName = "devices.json";
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public string RootPath { get; }
        public string ProfilesRoot { get; }
        public string CacheRoot { get; }
        public string BackupsRoot { get; }
        public string StatePath { get; }
        public string DevicesPath { get; }
        public string LegacyContextPath { get; }

        public ContextStore(string rootPath, string legacyContextPath)
        {
            if (string.IsNullOrWhiteSpace(rootPath)) throw new ArgumentException("UCR data root is required.", nameof(rootPath));
            RootPath = Path.GetFullPath(rootPath);
            ProfilesRoot = Path.Combine(RootPath, "Profiles");
            CacheRoot = Path.Combine(RootPath, "Cache");
            BackupsRoot = Path.Combine(RootPath, "Backups");
            StatePath = Path.Combine(RootPath, StateFileName);
            DevicesPath = Path.Combine(RootPath, DevicesFileName);
            LegacyContextPath = string.IsNullOrWhiteSpace(legacyContextPath) ? null : Path.GetFullPath(legacyContextPath);
        }

        public static ContextStore CreateDefault()
        {
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (string.IsNullOrWhiteSpace(documents))
            {
                documents = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }
            if (string.IsNullOrWhiteSpace(documents))
            {
                throw new InvalidOperationException("Windows did not provide a usable user Documents directory for UCR data.");
            }

            var root = Path.Combine(documents, "UCR");
            var executableDirectory = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory);
            var legacyContext = Path.Combine(executableDirectory, "context.xml");
            return new ContextStore(root, legacyContext);
        }

        public Context Load(List<Type> additionalPluginTypes)
        {
            var pluginTypes = GetPluginTypes(additionalPluginTypes);
            var serializer = new UcrJsonSerializer(pluginTypes);

            if (File.Exists(StatePath) || GetBackupFiles(StatePath).Any())
            {
                return LoadLive(serializer);
            }

            if (HasNewStoreConfigurationEvidence())
            {
                throw new InvalidDataException(
                    "UCR found JSON configuration data but state.json and its backups are missing. " +
                    "Refusing to resurrect an adjacent legacy context.xml over newer data.");
            }

            if (!string.IsNullOrWhiteSpace(LegacyContextPath) && File.Exists(LegacyContextPath))
            {
                return MigrateAdjacentLegacyContext(serializer, additionalPluginTypes);
            }

            return new Context(this);
        }

        public bool Save(Context context, List<Type> additionalPluginTypes)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            var pluginTypes = GetPluginTypes(additionalPluginTypes);
            var serializer = new UcrJsonSerializer(pluginTypes);

            Directory.CreateDirectory(RootPath);
            Directory.CreateDirectory(ProfilesRoot);
            Directory.CreateDirectory(CacheRoot);
            Directory.CreateDirectory(BackupsRoot);

            var profileIds = ValidateProfileIds(context.Profiles);
            foreach (var profile in context.Profiles)
            {
                var record = new ProfileFile
                {
                    SchemaVersion = SchemaVersion,
                    Profile = profile
                };
                var path = GetProfilePath(profile.Guid);
                AtomicWrite(path, serializer.Serialize(record), json =>
                {
                    ValidateProfileJsonShape(json);
                    ValidateProfileFile(serializer.Deserialize<ProfileFile>(json), profile.Guid);
                });
            }

            var aliases = CleanDeviceAliases(context.DeviceAliases);

            var devices = new DevicesFile
            {
                SchemaVersion = SchemaVersion,
                DeviceAliases = aliases
            };
            AtomicWrite(DevicesPath, serializer.Serialize(devices), json => ValidateDevicesFile(serializer.Deserialize<DevicesFile>(json)));

            // State is deliberately committed last. It is the manifest that defines the ordered live set.
            var state = new StateFile
            {
                SchemaVersion = SchemaVersion,
                ProfileOrder = profileIds
            };
            AtomicWrite(StatePath, serializer.Serialize(state), json => ValidateStateFile(serializer.Deserialize<StateFile>(json)));

            CleanupOrphanProfiles(profileIds);
            return true;
        }

        private bool HasNewStoreConfigurationEvidence()
        {
            if (File.Exists(DevicesPath)) return true;
            if (Directory.Exists(ProfilesRoot) && Directory.GetFiles(ProfilesRoot, "*.json", SearchOption.TopDirectoryOnly).Length > 0) return true;

            if (!Directory.Exists(BackupsRoot)) return false;
            // A directory by itself is not configuration evidence; users may legitimately clear backup
            // files while leaving the folder behind. Only actual recoverable backup files count.
            if (GetBackupFiles(DevicesPath).Any()) return true;

            var profileBackups = Path.Combine(BackupsRoot, "Profiles");
            return Directory.Exists(profileBackups) && Directory.GetFiles(profileBackups, "*.json", SearchOption.AllDirectories).Length > 0;
        }

        private Context LoadLive(UcrJsonSerializer serializer)
        {
            var state = ReadWithBackup(StatePath, serializer.Deserialize<StateFile>, ValidateStateFile);
            var profiles = new List<Profile>();
            foreach (var profileId in state.ProfileOrder)
            {
                var path = GetProfilePath(profileId);
                var record = ReadWithBackup(path, json =>
                {
                    ValidateProfileJsonShape(json);
                    return serializer.Deserialize<ProfileFile>(json);
                }, value => ValidateProfileFile(value, profileId));
                profiles.Add(record.Profile);
            }
            ValidateProfileIds(profiles);

            // devices.json is written before state.json on every successful save. Once a manifest exists,
            // a missing devices file is corruption, not an intentionally empty alias list.
            var devices = ReadWithBackup(DevicesPath, serializer.Deserialize<DevicesFile>, ValidateDevicesFile);

            var context = new Context(this);
            context.Profiles.Clear();
            context.Profiles.AddRange(profiles);
            context.DeviceAliases.Clear();
            context.DeviceAliases.AddRange(devices.DeviceAliases ?? new List<DeviceAlias>());
            context.PostLoad();
            return context;
        }

        private Context MigrateAdjacentLegacyContext(UcrJsonSerializer serializer, List<Type> additionalPluginTypes)
        {
            Logger.Info("Migrating adjacent legacy UCR context: " + LegacyContextPath);
            var xmlSerializer = Context.GetXmlSerializer(additionalPluginTypes, typeof(LegacyContextImportPackage));
            LegacyContextImportPackage legacy;
            using (var fileStream = new FileStream(LegacyContextPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                legacy = (LegacyContextImportPackage)xmlSerializer.Deserialize(fileStream);
            }
            if (legacy == null) throw new InvalidDataException("Legacy context.xml contained no UCR context data.");

            var migrated = new Context(this);
            migrated.Profiles.Clear();
            migrated.Profiles.AddRange(legacy.Profiles ?? new List<Profile>());
            migrated.DeviceAliases.Clear();
            migrated.DeviceAliases.AddRange(legacy.DeviceAliases ?? new List<DeviceAlias>());
            migrated.PostLoad();

            try
            {
                Save(migrated, additionalPluginTypes);

                // Do not call this migration complete until the new live store can actually be read back.
                var verified = LoadLive(serializer);
                ValidateMigrationRoundTrip(migrated, verified, serializer);

                MigrateLegacyCacheBestEffort();
                ArchiveLegacyContextBestEffort();
                return verified;
            }
            catch
            {
                // This path is entered only when no prior JSON configuration existed. Keep the original
                // adjacent context.xml untouched and remove any partial live JSON so the migration can
                // be retried on the next launch instead of poisoning startup permanently.
                RollbackFailedLegacyMigrationBestEffort();
                throw;
            }
        }

        private void RollbackFailedLegacyMigrationBestEffort()
        {
            var candidates = new List<string> { StatePath, DevicesPath };
            if (Directory.Exists(ProfilesRoot))
            {
                candidates.AddRange(Directory.GetFiles(ProfilesRoot, "*.json", SearchOption.TopDirectoryOnly));
            }

            foreach (var path in candidates)
            {
                try
                {
                    if (File.Exists(path)) File.Delete(path);
                }
                catch (Exception exception)
                {
                    Logger.Warn(exception, "Failed to roll back partial UCR migration file: " + path);
                }
            }
        }

        private void MigrateLegacyCacheBestEffort()
        {
            try
            {
                var legacyDirectory = Path.GetDirectoryName(LegacyContextPath);
                if (string.IsNullOrWhiteSpace(legacyDirectory)) return;
                var legacyCacheRoot = Path.Combine(legacyDirectory, "Cache");
                if (!Directory.Exists(legacyCacheRoot)) return;

                var sourceRoot = EnsureTrailingSeparator(Path.GetFullPath(legacyCacheRoot));
                var destinationRoot = EnsureTrailingSeparator(Path.GetFullPath(CacheRoot));
                if (string.Equals(sourceRoot, destinationRoot, StringComparison.OrdinalIgnoreCase)) return;

                foreach (var sourceFile in Directory.GetFiles(legacyCacheRoot, "*", SearchOption.AllDirectories))
                {
                    var fullSource = Path.GetFullPath(sourceFile);
                    if (!fullSource.StartsWith(sourceRoot, StringComparison.OrdinalIgnoreCase)) continue;
                    var relative = fullSource.Substring(sourceRoot.Length);
                    var destination = Path.Combine(CacheRoot, relative);
                    if (File.Exists(destination)) continue;
                    var destinationDirectory = Path.GetDirectoryName(destination);
                    if (!string.IsNullOrWhiteSpace(destinationDirectory)) Directory.CreateDirectory(destinationDirectory);
                    File.Copy(fullSource, destination, false);
                }
            }
            catch (Exception exception)
            {
                // Cache is reconstructable and must never invalidate an otherwise verified profile migration.
                Logger.Warn(exception, "UCR migrated context.xml but could not carry forward the adjacent device cache");
            }
        }

        private void ArchiveLegacyContextBestEffort()
        {
            try
            {
                var directory = Path.Combine(BackupsRoot, "Legacy");
                Directory.CreateDirectory(directory);
                var destination = Path.Combine(directory,
                    "context-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + ".xml");
                File.Copy(LegacyContextPath, destination, false);
            }
            catch (Exception exception)
            {
                // The new store has already been written and verified. Failure to make an extra archival
                // copy must not invalidate the migration or cause UCR to fall back to the old live format.
                Logger.Warn(exception, "UCR migrated context.xml but could not create the legacy backup copy");
            }
        }

        private static List<Guid> ValidateProfileIds(IEnumerable<Profile> profiles)
        {
            var topLevelIds = new List<Guid>();
            var topLevelSeen = new HashSet<Guid>();
            foreach (var profile in profiles ?? Enumerable.Empty<Profile>())
            {
                ValidateProfileBranch(profile);
                if (!topLevelSeen.Add(profile.Guid))
                {
                    throw new InvalidDataException("Duplicate top-level UCR profile identifier: " + profile.Guid);
                }
                topLevelIds.Add(profile.Guid);
            }
            return topLevelIds;
        }

        private static void ValidateProfileBranch(Profile profile)
        {
            if (profile == null) throw new InvalidDataException("UCR profile tree contains a null profile.");
            if (profile.Guid == Guid.Empty) throw new InvalidDataException("A UCR profile has an empty identifier.");
            if (profile.ChildProfiles == null) throw new InvalidDataException("UCR profile is missing its child profile collection: " + profile.Guid);
            if (profile.Mappings == null) throw new InvalidDataException("UCR profile is missing its mapping collection: " + profile.Guid);
            if (profile.InputDeviceConfigurations == null) throw new InvalidDataException("UCR profile is missing its input device collection: " + profile.Guid);
            if (profile.OutputDeviceConfigurations == null) throw new InvalidDataException("UCR profile is missing its output device collection: " + profile.Guid);
            if (profile.AutoActivateApplications == null) throw new InvalidDataException("UCR profile is missing its auto-activation collection: " + profile.Guid);

            foreach (var mapping in profile.Mappings)
            {
                if (mapping == null) throw new InvalidDataException("UCR profile contains a null mapping: " + profile.Guid);
                if (mapping.DeviceBindings == null) throw new InvalidDataException("UCR mapping is missing its input bindings: " + (mapping.Title ?? "<untitled>"));
                if (mapping.Plugins == null) throw new InvalidDataException("UCR mapping is missing its plugins: " + (mapping.Title ?? "<untitled>"));
                if (mapping.DeviceBindings.Any(binding => binding == null)) throw new InvalidDataException("UCR mapping contains a null input binding: " + (mapping.Title ?? "<untitled>"));
                foreach (var plugin in mapping.Plugins)
                {
                    if (plugin == null) throw new InvalidDataException("UCR mapping contains a null plugin: " + (mapping.Title ?? "<untitled>"));
                    if (plugin.Outputs == null) throw new InvalidDataException("UCR plugin is missing its output bindings: " + plugin.GetType().FullName);
                    if (plugin.Filters == null) throw new InvalidDataException("UCR plugin is missing its filter collection: " + plugin.GetType().FullName);
                    if (plugin.Outputs.Any(binding => binding == null)) throw new InvalidDataException("UCR plugin contains a null output binding: " + plugin.GetType().FullName);
                }
            }

            ValidateDeviceConfigurations(profile.InputDeviceConfigurations, profile.Guid, "input");
            ValidateDeviceConfigurations(profile.OutputDeviceConfigurations, profile.Guid, "output");

            foreach (var child in profile.ChildProfiles)
            {
                ValidateProfileBranch(child);
            }
        }

        private static void ValidateDeviceConfigurations(IEnumerable<DeviceConfiguration> configurations, Guid profileId, string kind)
        {
            foreach (var configuration in configurations)
            {
                if (configuration == null) throw new InvalidDataException("UCR profile contains a null " + kind + " device configuration: " + profileId);
                if (configuration.Guid == Guid.Empty) throw new InvalidDataException("UCR " + kind + " device configuration has an empty identifier in profile: " + profileId);
                if (configuration.Device == null) throw new InvalidDataException("UCR " + kind + " device configuration is missing its device in profile: " + profileId);
                if (configuration.ShadowDevices == null) throw new InvalidDataException("UCR " + kind + " device configuration is missing its shadow-device collection in profile: " + profileId);
                if (configuration.ShadowDevices.Any(device => device == null)) throw new InvalidDataException("UCR " + kind + " device configuration contains a null shadow device in profile: " + profileId);
            }
        }

        private static void ValidateMigrationRoundTrip(Context source, Context verified, UcrJsonSerializer serializer)
        {
            if (source == null || verified == null || serializer == null)
            {
                throw new InvalidDataException("Migrated UCR data failed post-write verification.");
            }

            if (source.Profiles.Count != verified.Profiles.Count)
            {
                throw new InvalidDataException("Migrated UCR profile count failed post-write verification.");
            }

            for (var i = 0; i < source.Profiles.Count; i++)
            {
                var sourceRecord = new ProfileFile { SchemaVersion = SchemaVersion, Profile = source.Profiles[i] };
                var verifiedRecord = new ProfileFile { SchemaVersion = SchemaVersion, Profile = verified.Profiles[i] };
                var sourceJson = JToken.Parse(serializer.Serialize(sourceRecord));
                var verifiedJson = JToken.Parse(serializer.Serialize(verifiedRecord));
                if (!JToken.DeepEquals(sourceJson, verifiedJson))
                {
                    throw new InvalidDataException("Migrated UCR profile data failed post-write verification: " + source.Profiles[i].Guid);
                }
            }

            var sourceDevices = new DevicesFile
            {
                SchemaVersion = SchemaVersion,
                DeviceAliases = CleanDeviceAliases(source.DeviceAliases)
            };
            var verifiedDevices = new DevicesFile
            {
                SchemaVersion = SchemaVersion,
                DeviceAliases = CleanDeviceAliases(verified.DeviceAliases)
            };
            if (!JToken.DeepEquals(
                    JToken.Parse(serializer.Serialize(sourceDevices)),
                    JToken.Parse(serializer.Serialize(verifiedDevices))))
            {
                throw new InvalidDataException("Migrated UCR device alias data failed post-write verification.");
            }
        }

        private static List<DeviceAlias> CleanDeviceAliases(IEnumerable<DeviceAlias> aliases)
        {
            return (aliases ?? Enumerable.Empty<DeviceAlias>())
                .Where(alias => alias != null)
                .Select(alias =>
                {
                    var clean = alias.Clone();
                    clean.DefaultOutlineColor = null;
                    return clean;
                })
                .ToList();
        }

        private static void ValidateProfileJsonShape(string json)
        {
            JObject root;
            try
            {
                root = JObject.Parse(json);
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException("Profile JSON is not a valid JSON object.", exception);
            }

            var profile = RequireObject(root, "profile", "profile file");
            ValidateProfileJsonObject(profile);
        }

        private static void ValidateProfileJsonObject(JObject profile)
        {
            RequireValue(profile, "guid", "profile");
            var children = RequireArray(profile, "childProfiles", "profile");
            var mappings = RequireArray(profile, "mappings", "profile");
            var inputs = RequireArray(profile, "inputDeviceConfigurations", "profile");
            var outputs = RequireArray(profile, "outputDeviceConfigurations", "profile");
            RequireArray(profile, "autoActivateApplications", "profile");

            foreach (var child in children)
            {
                var childObject = child as JObject;
                if (childObject == null) throw new InvalidDataException("Profile JSON childProfiles contains a non-object value.");
                ValidateProfileJsonObject(childObject);
            }

            foreach (var mappingToken in mappings)
            {
                var mapping = mappingToken as JObject;
                if (mapping == null) throw new InvalidDataException("Profile JSON mappings contains a non-object value.");
                RequireArray(mapping, "deviceBindings", "mapping");
                var plugins = RequireArray(mapping, "plugins", "mapping");
                foreach (var pluginToken in plugins)
                {
                    var wrapper = pluginToken as JObject;
                    if (wrapper == null) throw new InvalidDataException("Profile JSON plugins contains a non-object value.");
                    RequireValue(wrapper, "pluginType", "plugin");
                    var data = RequireObject(wrapper, "data", "plugin");
                    RequireArray(data, "outputs", "plugin data");
                    RequireArray(data, "filters", "plugin data");
                }
            }

            ValidateDeviceConfigurationJson(inputs, "inputDeviceConfigurations");
            ValidateDeviceConfigurationJson(outputs, "outputDeviceConfigurations");
        }

        private static void ValidateDeviceConfigurationJson(IEnumerable<JToken> configurations, string collectionName)
        {
            foreach (var token in configurations)
            {
                var configuration = token as JObject;
                if (configuration == null) throw new InvalidDataException("Profile JSON " + collectionName + " contains a non-object value.");
                RequireValue(configuration, "guid", collectionName);
                RequireObject(configuration, "device", collectionName);
                RequireArray(configuration, "shadowDevices", collectionName);
            }
        }

        private static JObject RequireObject(JObject owner, string propertyName, string ownerName)
        {
            var value = owner[propertyName] as JObject;
            if (value == null) throw new InvalidDataException("Profile JSON " + ownerName + " is missing object '" + propertyName + "'.");
            return value;
        }

        private static JArray RequireArray(JObject owner, string propertyName, string ownerName)
        {
            var value = owner[propertyName] as JArray;
            if (value == null) throw new InvalidDataException("Profile JSON " + ownerName + " is missing array '" + propertyName + "'.");
            return value;
        }

        private static void RequireValue(JObject owner, string propertyName, string ownerName)
        {
            var value = owner[propertyName];
            if (value == null || value.Type == JTokenType.Null)
            {
                throw new InvalidDataException("Profile JSON " + ownerName + " is missing value '" + propertyName + "'.");
            }
        }

        private static void ValidateStateFile(StateFile state)
        {
            if (state == null) throw new InvalidDataException("state.json contained no data.");
            if (state.SchemaVersion != SchemaVersion) throw new InvalidDataException("Unsupported UCR state schema version: " + state.SchemaVersion);
            if (state.ProfileOrder == null) throw new InvalidDataException("state.json is missing profileOrder.");
            if (state.ProfileOrder.Any(id => id == Guid.Empty)) throw new InvalidDataException("state.json contains an empty profile identifier.");
            if (state.ProfileOrder.Distinct().Count() != state.ProfileOrder.Count) throw new InvalidDataException("state.json contains duplicate profile identifiers.");
        }

        private static void ValidateProfileFile(ProfileFile record, Guid expectedId)
        {
            if (record == null) throw new InvalidDataException("Profile JSON contained no data.");
            if (record.SchemaVersion != SchemaVersion) throw new InvalidDataException("Unsupported UCR profile schema version: " + record.SchemaVersion);
            if (record.Profile == null) throw new InvalidDataException("Profile JSON is missing its profile object.");
            if (record.Profile.Guid != expectedId) throw new InvalidDataException("Profile JSON identifier does not match its file name.");
            ValidateProfileBranch(record.Profile);
        }

        private static void ValidateDevicesFile(DevicesFile devices)
        {
            if (devices == null) throw new InvalidDataException("devices.json contained no data.");
            if (devices.SchemaVersion != SchemaVersion) throw new InvalidDataException("Unsupported UCR devices schema version: " + devices.SchemaVersion);
            if (devices.DeviceAliases == null) throw new InvalidDataException("devices.json is missing deviceAliases.");
        }

        private string GetProfilePath(Guid profileId)
        {
            return Path.Combine(ProfilesRoot, profileId.ToString("D") + ".json");
        }

        private T ReadWithBackup<T>(string livePath, Func<string, T> deserialize, Action<T> validate)
        {
            Exception liveFailure = null;
            if (File.Exists(livePath))
            {
                try
                {
                    var live = deserialize(File.ReadAllText(livePath, Encoding.UTF8));
                    validate(live);
                    return live;
                }
                catch (Exception exception)
                {
                    liveFailure = exception;
                    Logger.Error(exception, "Failed to read UCR data file; trying backups: " + livePath);
                }
            }

            foreach (var backup in GetBackupFiles(livePath))
            {
                try
                {
                    var value = deserialize(File.ReadAllText(backup, Encoding.UTF8));
                    validate(value);
                    Logger.Warn("Recovered UCR data from backup: " + backup);
                    return value;
                }
                catch (Exception exception)
                {
                    Logger.Warn(exception, "Invalid UCR backup ignored: " + backup);
                }
            }

            if (liveFailure != null)
            {
                throw new InvalidDataException("UCR could not load the live data file or any valid backup: " + livePath, liveFailure);
            }
            throw new FileNotFoundException("Required UCR data file and backups are missing.", livePath);
        }

        private void AtomicWrite(string livePath, string json, Action<string> validateJson)
        {
            var directory = Path.GetDirectoryName(livePath);
            if (string.IsNullOrWhiteSpace(directory)) throw new InvalidOperationException("UCR persistence path has no directory: " + livePath);
            Directory.CreateDirectory(directory);

            var tempPath = livePath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                {
                    writer.Write(json);
                    writer.Flush();
                    stream.Flush(true);
                }

                var written = File.ReadAllText(tempPath, Encoding.UTF8);
                validateJson(written);

                if (File.Exists(livePath))
                {
                    var current = File.ReadAllText(livePath, Encoding.UTF8);
                    if (string.Equals(current, written, StringComparison.Ordinal))
                    {
                        File.Delete(tempPath);
                        return;
                    }

                    BackupExisting(livePath);
                    File.Replace(tempPath, livePath, null);
                }
                else
                {
                    File.Move(tempPath, livePath);
                }
            }
            finally
            {
                try
                {
                    if (File.Exists(tempPath)) File.Delete(tempPath);
                }
                catch (Exception exception)
                {
                    Logger.Warn(exception, "Failed to remove UCR temporary persistence file: " + tempPath);
                }
            }
        }

        private void BackupExisting(string livePath)
        {
            if (!File.Exists(livePath)) return;
            var directory = GetBackupDirectory(livePath);
            Directory.CreateDirectory(directory);
            var extension = Path.GetExtension(livePath);
            var backupPath = Path.Combine(directory,
                DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + extension);
            File.Copy(livePath, backupPath, false);
            PruneBackups(directory);
        }

        private IEnumerable<string> GetBackupFiles(string livePath)
        {
            var directory = GetBackupDirectory(livePath);
            if (!Directory.Exists(directory)) return Enumerable.Empty<string>();
            return Directory.GetFiles(directory, "*" + Path.GetExtension(livePath), SearchOption.TopDirectoryOnly)
                .OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private string GetBackupDirectory(string livePath)
        {
            var fullLive = Path.GetFullPath(livePath);
            var fullProfilesRoot = EnsureTrailingSeparator(Path.GetFullPath(ProfilesRoot));
            if (fullLive.StartsWith(fullProfilesRoot, StringComparison.OrdinalIgnoreCase))
            {
                return Path.Combine(BackupsRoot, "Profiles", Path.GetFileNameWithoutExtension(fullLive));
            }
            return Path.Combine(BackupsRoot, Path.GetFileNameWithoutExtension(fullLive));
        }

        private static string EnsureTrailingSeparator(string path)
        {
            if (path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)) return path;
            return path + Path.DirectorySeparatorChar;
        }

        private static void PruneBackups(string directory)
        {
            var backups = Directory.GetFiles(directory, "*", SearchOption.TopDirectoryOnly)
                .OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (var stale in backups.Skip(BackupLimitPerFile))
            {
                try
                {
                    File.Delete(stale);
                }
                catch (Exception exception)
                {
                    Logger.Warn(exception, "Failed to prune old UCR backup: " + stale);
                }
            }
        }

        private void CleanupOrphanProfiles(ICollection<Guid> liveProfileIds)
        {
            if (!Directory.Exists(ProfilesRoot)) return;
            var expected = new HashSet<string>(liveProfileIds.Select(id => id.ToString("D") + ".json"), StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.GetFiles(ProfilesRoot, "*.json", SearchOption.TopDirectoryOnly))
            {
                if (expected.Contains(Path.GetFileName(file))) continue;
                try
                {
                    BackupExisting(file);
                    File.Delete(file);
                }
                catch (Exception exception)
                {
                    // The manifest has already committed successfully. An obsolete locked file is harmless
                    // and must not turn a successful save into a user-visible failure.
                    Logger.Warn(exception, "Failed to remove obsolete UCR profile file: " + file);
                }
            }
        }

        private static List<Type> GetPluginTypes(List<Type> additionalPluginTypes)
        {
            var plugins = new PluginsManager("Plugins");
            var types = plugins.Plugins.Select(plugin => plugin.GetType()).ToList();
            if (additionalPluginTypes != null) types.AddRange(additionalPluginTypes);
            return types.Where(type => type != null && typeof(Plugin).IsAssignableFrom(type) && !type.IsAbstract)
                .Distinct()
                .ToList();
        }

        internal sealed class StateFile
        {
            public int SchemaVersion { get; set; }
            public List<Guid> ProfileOrder { get; set; }
        }

        internal sealed class DevicesFile
        {
            public int SchemaVersion { get; set; }
            public List<DeviceAlias> DeviceAliases { get; set; }
        }

        internal sealed class ProfileFile
        {
            public int SchemaVersion { get; set; }
            public Profile Profile { get; set; }
        }
    }
}
