using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Serialization;
using HidWizards.UCR.Core;
using HidWizards.UCR.Core.Managers;
using HidWizards.UCR.Core.Models;
using HidWizards.UCR.Core.Models.Binding;
using HidWizards.UCR.Core.Persistence;
using HidWizards.UCR.Plugins.Remapper;
using NUnit.Framework;

namespace HidWizards.UCR.Tests.ModelTests
{
    [TestFixture]
    internal class PersistenceTests
    {
        private readonly int _saveReloadTimes = 3;
        private string _root;
        private string _legacyPath;
        private ContextStore _store;

        [SetUp]
        public void SetUp()
        {
            var testRoot = Path.Combine(Path.GetTempPath(), "UCR-PersistenceTests-" + Guid.NewGuid().ToString("N"));
            _root = Path.Combine(testRoot, "Documents", "UCR");
            _legacyPath = Path.Combine(testRoot, "Application", "context.xml");
            Directory.CreateDirectory(Path.GetDirectoryName(_legacyPath));
            _store = new ContextStore(_root, _legacyPath);
        }

        [TearDown]
        public void TearDown()
        {
            var testRoot = Directory.GetParent(Directory.GetParent(_root).FullName).FullName;
            if (Directory.Exists(testRoot)) Directory.Delete(testRoot, true);
        }

        private Context NewContext()
        {
            return new Context(_store);
        }

        private Context Reload(List<Type> pluginTypes = null)
        {
            return Context.Load(_store, pluginTypes);
        }

        [Test]
        public void DefaultStoreUsesUserDocumentsAndOnlyTheRunningExecutableForLegacyMigration()
        {
            var store = ContextStore.CreateDefault();
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (string.IsNullOrWhiteSpace(documents))
            {
                documents = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }

            Assert.That(store.RootPath, Is.EqualTo(Path.GetFullPath(Path.Combine(documents, "UCR"))));
            Assert.That(store.LegacyContextPath, Is.EqualTo(Path.GetFullPath(
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "context.xml"))));
            Assert.That(store.StatePath, Is.EqualTo(Path.Combine(store.RootPath, "state.json")));
            Assert.That(store.DevicesPath, Is.EqualTo(Path.Combine(store.RootPath, "devices.json")));
            Assert.That(store.CacheRoot, Is.EqualTo(Path.Combine(store.RootPath, "Cache")));
        }

        [Test]
        public void BlankContext()
        {
            var context = NewContext();
            context.SaveContext(null);

            Assert.That(File.Exists(_store.StatePath), Is.True);
            Assert.That(File.Exists(_store.DevicesPath), Is.True);
            Assert.That(Directory.Exists(_store.ProfilesRoot), Is.True);
            Assert.That(Directory.GetFiles(_store.ProfilesRoot, "*.json"), Is.Empty);

            for (var i = 0; i < _saveReloadTimes; i++)
            {
                var newcontext = Reload();
                Assert.That(newcontext.IsNotSaved, Is.False);
                Assert.That(newcontext.ActiveProfile, Is.Null);
                Assert.That(newcontext.Profiles, Is.Not.Null.And.Empty);
                newcontext.SaveContext(null);
            }
        }

        [Test]
        public void ProfileContextUsesOneFilePerTopLevelBranchAndPreservesOrder()
        {
            var context = NewContext();
            var first = context.ProfilesManager.CreateProfile("Root Profile", null, null);
            var child = context.ProfilesManager.CreateProfile("Child Profile", null, null);
            var second = context.ProfilesManager.CreateProfile("Second Root", null, null);
            context.ProfilesManager.AddProfile(first);
            context.Profiles[0].AddChildProfile(child);
            context.ProfilesManager.AddProfile(second);
            context.SaveContext(null);

            var profileFiles = Directory.GetFiles(_store.ProfilesRoot, "*.json");
            Assert.That(profileFiles.Length, Is.EqualTo(2), "Child profiles belong inside their top-level branch file.");
            Assert.That(profileFiles.Select(Path.GetFileNameWithoutExtension),
                Does.Contain(first.Guid.ToString("D")).And.Contain(second.Guid.ToString("D")));

            var stateJson = File.ReadAllText(_store.StatePath);
            Assert.That(stateJson, Does.Contain("\"schemaVersion\""));
            Assert.That(stateJson, Does.Contain("\"profileOrder\""));
            Assert.That(stateJson, Does.Not.Contain("\"SchemaVersion\""));

            for (var i = 0; i < _saveReloadTimes; i++)
            {
                var loaded = Reload();
                Assert.That(loaded.Profiles.Select(profile => profile.Title).ToArray(),
                    Is.EqualTo(new[] { "Root Profile", "Second Root" }));
                Assert.That(loaded.Profiles[0].ChildProfiles.Count, Is.EqualTo(1));
                Assert.That(loaded.Profiles[0], Is.EqualTo(loaded.Profiles[0].ChildProfiles[0].ParentProfile));
                loaded.SaveContext(null);
            }
        }

        [Test]
        public void MappingContextRoundTripsPluginsBindingsAndBlockState()
        {
            var context = NewContext();
            var pluginTypes = new List<Type> { typeof(ButtonToButton) };
            var rootProfile = context.ProfilesManager.CreateProfile("Root Profile", null, null);
            context.ProfilesManager.AddProfile(rootProfile);
            var mapping = rootProfile.AddMapping("Jump");
            rootProfile.AddPlugin(mapping, new ButtonToButton());
            rootProfile.AddPlugin(mapping, new ButtonToButton());

            for (var i = 0; i < mapping.DeviceBindings.Count; i++)
            {
                SetDeviceBindingValues(mapping.DeviceBindings[i], i + 1);
            }
            mapping.DeviceBindings[0].Block = true;
            mapping.DeviceBindings[0].InvertInput = true;

            var originalBindings = mapping.DeviceBindings.ToList();
            context.SaveContext(pluginTypes);

            var profileJson = File.ReadAllText(Path.Combine(_store.ProfilesRoot, rootProfile.Guid.ToString("D") + ".json"));
            Assert.That(profileJson, Does.Contain("\"pluginType\""));
            Assert.That(profileJson, Does.Not.Contain("\"$type\""));
            Assert.That(profileJson, Does.Contain("\"block\": true"));
            Assert.That(profileJson, Does.Contain("\"invertInput\": true"));

            for (var i = 0; i < _saveReloadTimes; i++)
            {
                var newcontext = Reload(pluginTypes);
                var newMapping = newcontext.Profiles[0].Mappings[0];
                var newBindings = newMapping.DeviceBindings;
                Assert.That(newMapping.Title, Is.EqualTo(mapping.Title));
                Assert.That(newMapping.Plugins.Count, Is.EqualTo(mapping.Plugins.Count));
                Assert.That(newMapping.Plugins[0].Outputs.Count, Is.EqualTo(1));

                for (var j = 0; j < originalBindings.Count; j++)
                {
                    Assert.That(newBindings[j].Profile.Guid, Is.EqualTo(originalBindings[j].Profile.Guid));
                    Assert.That(newBindings[j].DeviceIoType, Is.EqualTo(DeviceIoType.Input));
                    Assert.That(newBindings[j].Guid, Is.Not.EqualTo(originalBindings[j].Guid));
                    Assert.That(newBindings[j].IsBound, Is.EqualTo(originalBindings[j].IsBound));
                    Assert.That(newBindings[j].DeviceConfigurationGuid, Is.EqualTo(originalBindings[j].DeviceConfigurationGuid));
                    Assert.That(newBindings[j].KeyType, Is.EqualTo(originalBindings[j].KeyType));
                    Assert.That(newBindings[j].KeyValue, Is.EqualTo(originalBindings[j].KeyValue));
                    Assert.That(newBindings[j].KeySubValue, Is.EqualTo(originalBindings[j].KeySubValue));
                }
                Assert.That(newBindings[0].Block, Is.True);
                Assert.That(newBindings[0].InvertInput, Is.True);
                newcontext.SaveContext(pluginTypes);
            }
        }

        [Test]
        public void DeviceAliasesLiveInDevicesFileAndLegacyColourMetadataIsScrubbed()
        {
            var context = NewContext();
            context.DeviceAliases.Add(new DeviceAlias
            {
                ProviderName = "Core_Interception",
                IdentityKind = DeviceAliasIdentityKind.HardwareHandle,
                IdentityValue = "keyboard-1",
                DeviceNumber = 4,
                Alias = "Desk Keyboard",
                Hidden = true,
                SortOrder = 2,
                OutlineColor = DeviceOutlineColor.Cyan,
                DefaultOutlineColor = "#123456"
            });

            context.SaveContext();
            var json = File.ReadAllText(_store.DevicesPath);
            Assert.That(json, Does.Contain("\"deviceAliases\""));
            Assert.That(json, Does.Contain("Desk Keyboard"));
            Assert.That(json, Does.Not.Contain("DefaultOutlineColor"));
            Assert.That(json, Does.Not.Contain("defaultOutlineColor"));

            var loaded = Reload();
            Assert.That(loaded.DeviceAliases.Count, Is.EqualTo(1));
            Assert.That(loaded.DeviceAliases[0].Alias, Is.EqualTo("Desk Keyboard"));
            Assert.That(loaded.DeviceAliases[0].OutlineColor, Is.EqualTo(DeviceOutlineColor.Cyan));
            Assert.That(loaded.DeviceAliases[0].DefaultOutlineColor, Is.Null);
        }

        [Test]
        public void AdjacentLegacyContextMigratesOnlyWhenNewStateDoesNotExist()
        {
            var pluginTypes = new List<Type> { typeof(ButtonToButton) };
            WriteLegacyContext("Legacy Profile", pluginTypes);

            var migrated = Reload(pluginTypes);
            Assert.That(migrated.Profiles.Count, Is.EqualTo(1));
            Assert.That(migrated.Profiles[0].Title, Is.EqualTo("Legacy Profile"));
            Assert.That(File.Exists(_store.StatePath), Is.True);
            Assert.That(Directory.GetFiles(Path.Combine(_store.BackupsRoot, "Legacy"), "context-*.xml").Length,
                Is.EqualTo(1));
            Assert.That(File.Exists(_legacyPath), Is.True, "Migration must not destroy the user's original legacy file.");

            // Once the new manifest exists the adjacent legacy file is no longer consulted.
            WriteLegacyContext("Should Be Ignored", pluginTypes);
            var loadedAgain = Reload(pluginTypes);
            Assert.That(loadedAgain.Profiles[0].Title, Is.EqualTo("Legacy Profile"));
        }

        [Test]
        public void SameFolderLegacyMigrationCarriesForwardExistingDeviceCacheWithoutDeletingTheOriginal()
        {
            WriteLegacyContext("Legacy With Cache", null);
            var legacyCacheFile = Path.Combine(Path.GetDirectoryName(_legacyPath), "Cache", "Core_Interception", "keyboard.json");
            Directory.CreateDirectory(Path.GetDirectoryName(legacyCacheFile));
            File.WriteAllText(legacyCacheFile, "{\"Title\":\"Cached Keyboard\"}");

            Reload();

            var migratedCacheFile = Path.Combine(_store.CacheRoot, "Core_Interception", "keyboard.json");
            Assert.That(File.Exists(migratedCacheFile), Is.True,
                "The adjacent legacy Cache folder is user data too and should move with a same-folder migration.");
            Assert.That(File.ReadAllText(migratedCacheFile), Is.EqualTo(File.ReadAllText(legacyCacheFile)));
            Assert.That(File.Exists(legacyCacheFile), Is.True, "Migration must leave the old application-folder cache intact.");
        }

        [Test]
        public void LegacyMigrationPreservesMappingsPluginStateAndDeviceAliases()
        {
            var pluginTypes = new List<Type> { typeof(ButtonToButton) };
            var source = NewContext();
            var profile = source.ProfilesManager.CreateProfile("Legacy Full Profile", null, null);
            source.ProfilesManager.AddProfile(profile);
            var mapping = profile.AddMapping("Legacy Mapping");
            profile.AddPlugin(mapping, new ButtonToButton());
            mapping.DeviceBindings[0].Block = true;
            mapping.DeviceBindings[0].InvertInput = true;
            source.DeviceAliases.Add(new DeviceAlias
            {
                ProviderName = "Core_Interception",
                IdentityKind = DeviceAliasIdentityKind.HardwareHandle,
                IdentityValue = "legacy-keyboard",
                DeviceNumber = 3,
                Alias = "Legacy Keyboard",
                Hidden = true,
                SortOrder = 7,
                OutlineColor = DeviceOutlineColor.Purple,
                DefaultOutlineColor = "#123456"
            });

            WriteLegacyPackage(new LegacyContextImportPackage
            {
                Profiles = source.Profiles,
                DeviceAliases = source.DeviceAliases
            }, pluginTypes);

            var migrated = Reload(pluginTypes);
            var migratedMapping = migrated.Profiles.Single().Mappings.Single();
            Assert.That(migratedMapping.Title, Is.EqualTo("Legacy Mapping"));
            Assert.That(migratedMapping.Plugins.Count, Is.EqualTo(1));
            Assert.That(migratedMapping.Plugins[0].Outputs.Count, Is.EqualTo(1));
            Assert.That(migratedMapping.DeviceBindings[0].Block, Is.True);
            Assert.That(migratedMapping.DeviceBindings[0].InvertInput, Is.True);
            Assert.That(migrated.DeviceAliases.Single().Alias, Is.EqualTo("Legacy Keyboard"));
            Assert.That(migrated.DeviceAliases.Single().OutlineColor, Is.EqualTo(DeviceOutlineColor.Purple));
            Assert.That(migrated.DeviceAliases.Single().DefaultOutlineColor, Is.Null,
                "The obsolete legacy-only default colour metadata should not enter the new live store.");
        }

        [Test]
        public void DevicesFileMissingAliasCollectionIsRejectedInsteadOfSilentlyResettingIt()
        {
            var context = NewContext();
            context.DeviceAliases.Add(new DeviceAlias
            {
                ProviderName = "Core_Interception",
                IdentityKind = DeviceAliasIdentityKind.HardwareHandle,
                IdentityValue = "keyboard-critical",
                Alias = "Must Survive"
            });
            context.SaveContext();

            File.WriteAllText(_store.DevicesPath, "{\"schemaVersion\":1}");

            Assert.Throws<InvalidDataException>(() => Reload(),
                "A truncated devices file must not silently erase device presentation state.");
        }

        [Test]
        public void MissingDevicesFileWithoutBackupIsRejectedInsteadOfSilentlyResettingAliases()
        {
            var context = NewContext();
            context.DeviceAliases.Add(new DeviceAlias
            {
                ProviderName = "Core_Interception",
                IdentityKind = DeviceAliasIdentityKind.HardwareHandle,
                IdentityValue = "keyboard-missing-file",
                Alias = "Must Survive"
            });
            context.SaveContext();

            File.Delete(_store.DevicesPath);

            Assert.Throws<FileNotFoundException>(() => Reload(),
                "Once state.json exists, devices.json is part of that store and must not disappear into an implicit empty list.");
        }

        [Test]
        public void StateManifestMissingProfileOrderIsRejectedInsteadOfLoadingAsEmpty()
        {
            var context = NewContext();
            var profile = context.ProfilesManager.CreateProfile("Must Survive", null, null);
            context.ProfilesManager.AddProfile(profile);
            context.SaveContext();

            File.WriteAllText(_store.StatePath, "{\"schemaVersion\":1}");

            Assert.Throws<InvalidDataException>(() => Reload(),
                "A truncated manifest must never be interpreted as an intentionally empty profile list.");
        }

        [Test]
        public void MissingLiveStateRecoversFromBackupBeforeConsideringAdjacentLegacyContext()
        {
            var context = NewContext();
            var first = context.ProfilesManager.CreateProfile("Stored Profile", null, null);
            context.ProfilesManager.AddProfile(first);
            context.SaveContext();

            var second = context.ProfilesManager.CreateProfile("Second Profile", null, null);
            context.ProfilesManager.AddProfile(second);
            context.SaveContext();

            var stateBackupRoot = Path.Combine(_store.BackupsRoot, "state");
            Assert.That(Directory.GetFiles(stateBackupRoot, "*.json").Length, Is.GreaterThanOrEqualTo(1));

            File.Delete(_store.StatePath);
            WriteLegacyContext("Legacy Must Not Win", null);

            var recovered = Reload();
            Assert.That(recovered.Profiles.Select(profile => profile.Title).ToArray(),
                Is.EqualTo(new[] { "Stored Profile" }),
                "A valid new-store manifest backup must take precedence over adjacent legacy migration.");
            Assert.That(Directory.Exists(Path.Combine(_store.BackupsRoot, "Legacy")), Is.False,
                "Recovering the new store must not be misreported as a legacy migration.");
        }

        [Test]
        public void MissingStateWithoutBackupDoesNotFallBackToLegacyWhenNewStoreDataExists()
        {
            var context = NewContext();
            var profile = context.ProfilesManager.CreateProfile("New Store Profile", null, null);
            context.ProfilesManager.AddProfile(profile);
            context.SaveContext();

            Assert.That(Directory.Exists(Path.Combine(_store.BackupsRoot, "state")), Is.False,
                "The first manifest write intentionally has no previous state backup.");
            File.Delete(_store.StatePath);
            WriteLegacyContext("Stale Legacy Profile", null);

            Assert.Throws<InvalidDataException>(() => Reload(),
                "Evidence of the new JSON store must prevent stale adjacent context.xml from being resurrected.");
            Assert.That(Directory.Exists(Path.Combine(_store.BackupsRoot, "Legacy")), Is.False);
        }

        [Test]
        public void DuplicateTopLevelProfileIdentifiersAreRejectedBecauseTheyWouldShareOneFile()
        {
            var context = NewContext();
            var first = context.ProfilesManager.CreateProfile("First", null, null);
            var second = context.ProfilesManager.CreateProfile("Second", null, null);
            second.Guid = first.Guid;
            context.ProfilesManager.AddProfile(first);
            context.ProfilesManager.AddProfile(second);

            Assert.Throws<InvalidDataException>(() => context.SaveContext());
        }

        [Test]
        public void ExistingCopyProfileBehaviourWithNestedIdentifiersCanStillBeSaved()
        {
            var context = NewContext();
            var root = context.ProfilesManager.CreateProfile("Original", null, null);
            var child = context.ProfilesManager.CreateProfile("Child", null, null);
            context.ProfilesManager.AddProfile(root);
            root.AddChildProfile(child);

            context.ProfilesManager.CopyProfile(root, "Copy");

            Assert.DoesNotThrow(() => context.SaveContext(),
                "The new persistence format must not make an existing Copy Profile workflow unsaveable.");
            Assert.That(Directory.GetFiles(_store.ProfilesRoot, "*.json").Length, Is.EqualTo(2));
        }

        [Test]
        public void StructurallyTruncatedProfileJsonRecoversFromPreviousProfileBackup()
        {
            var context = NewContext();
            var profile = context.ProfilesManager.CreateProfile("v1", null, null);
            context.ProfilesManager.AddProfile(profile);
            context.SaveContext();

            profile.Title = "v2";
            context.ContextChanged();
            context.SaveContext();

            var path = Path.Combine(_store.ProfilesRoot, profile.Guid.ToString("D") + ".json");
            var json = File.ReadAllText(path);
            var truncated = System.Text.RegularExpressions.Regex.Replace(
                json, @"""mappings""\s*:\s*\[\s*\]\s*,", string.Empty, 1);
            Assert.That(truncated, Is.Not.EqualTo(json), "Test fixture could not remove the mappings collection.");
            File.WriteAllText(path, truncated);

            var recovered = Reload();
            Assert.That(recovered.Profiles.Single().Title, Is.EqualTo("v1"),
                "A parseable but structurally incomplete live profile must not silently erase data; UCR should use its valid backup.");
        }

        [Test]
        public void FailedLegacyMigrationDoesNotLeavePartialJsonThatBlocksARetry()
        {
            WriteLegacyContext("Retryable Legacy", null);
            Directory.CreateDirectory(_store.DevicesPath); // Force the devices.json commit to fail after profile JSON was written.

            Assert.Throws<IOException>(() => Reload());
            Assert.That(Directory.Exists(_store.ProfilesRoot)
                ? Directory.GetFiles(_store.ProfilesRoot, "*.json").Length
                : 0, Is.EqualTo(0),
                "A failed one-time migration must roll back the partial live JSON it created.");
            Assert.That(File.Exists(_store.StatePath), Is.False);

            Directory.Delete(_store.DevicesPath);
            var retried = Reload();
            Assert.That(retried.Profiles.Single().Title, Is.EqualTo("Retryable Legacy"),
                "After the write obstruction is removed, the unchanged adjacent legacy file should be migratable on the next launch.");
        }

        [Test]
        public void StoreDoesNotSearchForLegacyContextOutsideItsExplicitAdjacentPath()
        {
            var elsewhere = Path.Combine(Path.GetDirectoryName(_legacyPath), "OldCopy", "context.xml");
            Directory.CreateDirectory(Path.GetDirectoryName(elsewhere));
            File.WriteAllText(elsewhere, "<Context />");

            var loaded = Reload();
            Assert.That(loaded.Profiles, Is.Empty);
            Assert.That(File.Exists(_store.StatePath), Is.False,
                "Merely finding a context.xml elsewhere must not trigger automatic migration.");
        }

        [Test]
        public void BackupsAreBoundedPerLiveFile()
        {
            var context = NewContext();
            var profile = context.ProfilesManager.CreateProfile("v0", null, null);
            context.ProfilesManager.AddProfile(profile);
            context.SaveContext();

            for (var i = 1; i <= 9; i++)
            {
                profile.Title = "v" + i;
                context.ContextChanged();
                context.SaveContext();
            }

            var profileBackupRoot = Path.Combine(_store.BackupsRoot, "Profiles", profile.Guid.ToString("D"));
            Assert.That(Directory.GetFiles(profileBackupRoot, "*.json").Length, Is.EqualTo(5));
            Assert.That(Directory.Exists(Path.Combine(_store.BackupsRoot, "state")), Is.False,
                "Unchanged manifests should not be rewritten or backed up on every save.");
            Assert.That(Directory.Exists(Path.Combine(_store.BackupsRoot, "devices")), Is.False,
                "Unchanged device state should not be rewritten or backed up on every save.");
        }

        [Test]
        public void UnknownPluginTypeIsRejectedInsteadOfLoadingArbitraryDotNetType()
        {
            var pluginTypes = new List<Type> { typeof(ButtonToButton) };
            var context = NewContext();
            var profile = context.ProfilesManager.CreateProfile("Plugin", null, null);
            context.ProfilesManager.AddProfile(profile);
            var mapping = profile.AddMapping("Mapping");
            profile.AddPlugin(mapping, new ButtonToButton());
            context.SaveContext(pluginTypes);

            var path = Path.Combine(_store.ProfilesRoot, profile.Guid.ToString("D") + ".json");
            var json = File.ReadAllText(path);
            json = json.Replace(typeof(ButtonToButton).FullName, "System.Diagnostics.Process");
            File.WriteAllText(path, json);

            Assert.Throws<InvalidDataException>(() => Reload(pluginTypes));
        }

        private void WriteLegacyContext(string title, List<Type> pluginTypes)
        {
            var legacy = new LegacyContextImportPackage();
            legacy.Profiles.Add(Profile.CreateProfile(null, title, null, null));
            WriteLegacyPackage(legacy, pluginTypes);
        }

        private void WriteLegacyPackage(LegacyContextImportPackage legacy, List<Type> pluginTypes)
        {
            var serializer = Context.GetXmlSerializer(pluginTypes, typeof(LegacyContextImportPackage));
            using (var stream = new FileStream(_legacyPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                serializer.Serialize(writer, legacy);
            }
        }

        private static void SetDeviceBindingValues(DeviceBinding deviceBinding, int value)
        {
            deviceBinding.DeviceConfigurationGuid = Guid.NewGuid();
            deviceBinding.KeyType = value;
            deviceBinding.KeyValue = value;
            deviceBinding.KeySubValue = value;
        }
    }
}
