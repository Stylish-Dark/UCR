using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using HidWizards.UCR.Core;
using HidWizards.UCR.Core.Managers;
using HidWizards.UCR.Core.Models;
using HidWizards.UCR.Core.Models.Binding;
using HidWizards.UCR.Plugins.Remapper;
using NUnit.Framework;

namespace HidWizards.UCR.Tests.ModelTests
{
    [TestFixture]
    internal class ProfileTransferTests
    {
        private readonly List<Type> _pluginTypes = new List<Type> { typeof(ButtonToButton) };
        private readonly List<string> _temporaryFiles = new List<string>();

        [TearDown]
        public void TearDown()
        {
            foreach (var file in _temporaryFiles)
            {
                try
                {
                    if (File.Exists(file)) File.Delete(file);
                }
                catch
                {
                    // Cleanup failure must not hide the test result.
                }
            }
            _temporaryFiles.Clear();
        }

        [Test]
        public void SingleProfileExportFlattensInheritedBehaviourAndImportsSelfContained()
        {
            var source = new Context();
            var root = AddProfile(source, "Root");
            var inheritedInput = AddDeviceConfiguration(root, DeviceIoType.Input, "Keyboard", "Core_Interception", "Keyboard\\A", 0);
            var inheritedOutput = AddDeviceConfiguration(root, DeviceIoType.Output, "Xbox", "Core_ViGEm", "xb360", 0);
            AddButtonMapping(root, "Jump", inheritedInput.Guid, inheritedOutput.Guid, 30, 0);

            var child = source.ProfilesManager.CreateProfile("Child", null, null);
            root.AddChildProfile(child);
            AddButtonMapping(child, "Crouch", inheritedInput.Guid, inheritedOutput.Guid, 31, 1);

            var grandChild = source.ProfilesManager.CreateProfile("Grandchild", null, null);
            child.AddChildProfile(grandChild);

            var file = TempFile(".ucrprofile");
            source.ProfilesManager.ExportProfile(child, file, _pluginTypes);

            var destination = new Context();
            var imported = destination.ProfilesManager.ImportProfile(file, null, _pluginTypes);

            Assert.That(destination.Profiles.Count, Is.EqualTo(1));
            Assert.That(imported.Title, Is.EqualTo("Child"));
            Assert.That(imported.ParentProfile, Is.Null);
            Assert.That(imported.ChildProfiles.Count, Is.EqualTo(1));
            Assert.That(imported.ChildProfiles[0].Title, Is.EqualTo("Grandchild"));
            Assert.That(imported.Mappings.Select(mapping => mapping.Title), Is.EquivalentTo(new[] { "Jump", "Crouch" }));
            Assert.That(imported.InputDeviceConfigurations.Count, Is.EqualTo(1));
            Assert.That(imported.OutputDeviceConfigurations.Count, Is.EqualTo(1));

            AssertAllBindingReferencesResolve(imported);
            Assert.That(imported.InputDeviceConfigurations[0].Guid, Is.Not.EqualTo(inheritedInput.Guid));
            Assert.That(imported.OutputDeviceConfigurations[0].Guid, Is.Not.EqualTo(inheritedOutput.Guid));
        }

        [Test]
        public void SingleProfileExportMatchesCaseSensitiveParentMappingOverrideSemantics()
        {
            var source = new Context();
            var root = AddProfile(source, "Root");
            var input = AddDeviceConfiguration(root, DeviceIoType.Input, "Keyboard", "Core_Interception", "Keyboard\\A", 0);
            var output = AddDeviceConfiguration(root, DeviceIoType.Output, "Xbox", "Core_ViGEm", "xb360", 0);
            AddButtonMapping(root, "Jump", input.Guid, output.Guid, 30, 0);

            var child = source.ProfilesManager.CreateProfile("Child", null, null);
            root.AddChildProfile(child);
            AddButtonMapping(child, "jump", input.Guid, output.Guid, 31, 1);

            var file = TempFile(".ucrprofile");
            source.ProfilesManager.ExportProfile(child, file, _pluginTypes);

            var destination = new Context();
            var imported = destination.ProfilesManager.ImportProfile(file, null, _pluginTypes);

            Assert.That(imported.Mappings.Select(mapping => mapping.Title), Is.EqualTo(new[] { "Jump", "jump" }));
        }

        [Test]
        public void SingleProfileExportPreservesInputReverseSetting()
        {
            var source = new Context();
            var profile = AddProfile(source, "Reverse setting");
            var input = AddDeviceConfiguration(profile, DeviceIoType.Input, "Keyboard", "Core_Interception", @"Keyboard\A", 0);
            var output = AddDeviceConfiguration(profile, DeviceIoType.Output, "Xbox", "Core_ViGEm", "xb360", 0);
            AddButtonMapping(profile, "Test", input.Guid, output.Guid, 30, 0);
            profile.Mappings[0].DeviceBindings[0].InvertInput = true;

            var file = TempFile(".ucrprofile");
            source.ProfilesManager.ExportProfile(profile, file, _pluginTypes);

            var destination = new Context();
            var imported = destination.ProfilesManager.ImportProfile(file, null, _pluginTypes);

            Assert.That(imported.Mappings[0].DeviceBindings[0].InvertInput, Is.True);
        }

        [Test]
        public void SingleProfileImportRegeneratesPrimaryDeviceReferencesWithConfigurations()
        {
            var source = new Context();
            var profile = AddProfile(source, "Primary devices");
            AddDeviceConfiguration(profile, DeviceIoType.Input, "Keyboard A", "Core_Interception", "Keyboard\\A", 0);
            var inputB = AddDeviceConfiguration(profile, DeviceIoType.Input, "Keyboard B", "Core_Interception", "Keyboard\\B", 1);
            AddDeviceConfiguration(profile, DeviceIoType.Output, "Xbox 1", "Core_ViGEm", "xb360", 0);
            var outputB = AddDeviceConfiguration(profile, DeviceIoType.Output, "Xbox 2", "Core_ViGEm", "xb360", 1);
            profile.SetPrimaryDeviceConfiguration(DeviceIoType.Input, inputB.Guid);
            profile.SetPrimaryDeviceConfiguration(DeviceIoType.Output, outputB.Guid);

            var file = TempFile(".ucrprofile");
            source.ProfilesManager.ExportProfile(profile, file, _pluginTypes);

            var destination = new Context();
            var imported = destination.ProfilesManager.ImportProfile(file, null, _pluginTypes);

            Assert.That(imported.PrimaryInputDeviceConfigurationGuid, Is.Not.EqualTo(inputB.Guid));
            Assert.That(imported.PrimaryOutputDeviceConfigurationGuid, Is.Not.EqualTo(outputB.Guid));
            Assert.That(imported.GetPrimaryDeviceConfiguration(DeviceIoType.Input).Device.Title, Is.EqualTo("Keyboard B"));
            Assert.That(imported.GetPrimaryDeviceConfiguration(DeviceIoType.Output).Device.Title, Is.EqualTo("Xbox 2"));
        }

        [Test]
        public void SingleProfileCanBeImportedMoreThanOnceWithoutIdentifierCollisions()
        {
            var source = new Context();
            var profile = AddProfile(source, "Portable");
            var input = AddDeviceConfiguration(profile, DeviceIoType.Input, "Keyboard", "Core_Interception", "Keyboard\\A", 0);
            var output = AddDeviceConfiguration(profile, DeviceIoType.Output, "Xbox", "Core_ViGEm", "xb360", 0);
            AddButtonMapping(profile, "Jump", input.Guid, output.Guid, 30, 0);

            var file = TempFile(".ucrprofile");
            source.ProfilesManager.ExportProfile(profile, file, _pluginTypes);

            var destination = new Context();
            var first = destination.ProfilesManager.ImportProfile(file, null, _pluginTypes);
            var second = destination.ProfilesManager.ImportProfile(file, null, _pluginTypes);

            Assert.That(destination.Profiles.Count, Is.EqualTo(2));
            Assert.That(first.Guid, Is.Not.EqualTo(second.Guid));
            Assert.That(first.InputDeviceConfigurations[0].Guid, Is.Not.EqualTo(second.InputDeviceConfigurations[0].Guid));
            Assert.That(first.OutputDeviceConfigurations[0].Guid, Is.Not.EqualTo(second.OutputDeviceConfigurations[0].Guid));
            AssertAllBindingReferencesResolve(first);
            AssertAllBindingReferencesResolve(second);
        }

        [Test]
        public void FullProfileListReplacePreservesBackupIdentifiers()
        {
            var source = new Context();
            var profile = AddProfile(source, "Backup Root");
            var input = AddDeviceConfiguration(profile, DeviceIoType.Input, "Keyboard", "Core_Interception", "Keyboard\\A", 0);
            var output = AddDeviceConfiguration(profile, DeviceIoType.Output, "Xbox", "Core_ViGEm", "xb360", 0);
            AddButtonMapping(profile, "Jump", input.Guid, output.Guid, 30, 0);

            var originalProfileGuid = profile.Guid;
            var originalInputGuid = input.Guid;
            var originalOutputGuid = output.Guid;
            var file = TempFile(".ucrprofiles");
            source.ProfilesManager.ExportProfileList(file, _pluginTypes);

            var destination = new Context();
            AddProfile(destination, "Existing");
            var importedCount = destination.ProfilesManager.ImportProfileList(file, ProfileListImportMode.Replace, _pluginTypes);

            Assert.That(importedCount, Is.EqualTo(1));
            Assert.That(destination.Profiles.Count, Is.EqualTo(1));
            Assert.That(destination.Profiles[0].Guid, Is.EqualTo(originalProfileGuid));
            Assert.That(destination.Profiles[0].InputDeviceConfigurations[0].Guid, Is.EqualTo(originalInputGuid));
            Assert.That(destination.Profiles[0].OutputDeviceConfigurations[0].Guid, Is.EqualTo(originalOutputGuid));
            AssertAllBindingReferencesResolve(destination.Profiles[0]);
        }

        [Test]
        public void FullProfileListMergeRegeneratesIdentifiers()
        {
            var context = new Context();
            var profile = AddProfile(context, "Merge Root");
            var input = AddDeviceConfiguration(profile, DeviceIoType.Input, "Keyboard", "Core_Interception", "Keyboard\\A", 0);
            var output = AddDeviceConfiguration(profile, DeviceIoType.Output, "Xbox", "Core_ViGEm", "xb360", 0);
            AddButtonMapping(profile, "Jump", input.Guid, output.Guid, 30, 0);

            var file = TempFile(".ucrprofiles");
            context.ProfilesManager.ExportProfileList(file, _pluginTypes);
            context.ProfilesManager.ImportProfileList(file, ProfileListImportMode.Merge, _pluginTypes);

            Assert.That(context.Profiles.Count, Is.EqualTo(2));
            Assert.That(context.Profiles[0].Guid, Is.Not.EqualTo(context.Profiles[1].Guid));
            Assert.That(context.Profiles[0].InputDeviceConfigurations[0].Guid,
                Is.Not.EqualTo(context.Profiles[1].InputDeviceConfigurations[0].Guid));
            Assert.That(context.Profiles[0].OutputDeviceConfigurations[0].Guid,
                Is.Not.EqualTo(context.Profiles[1].OutputDeviceConfigurations[0].Guid));
            AssertAllBindingReferencesResolve(context.Profiles[1]);
        }

        [Test]
        public void FullProfileListMergeHandlesDuplicateConfigurationIdsAcrossIndependentRoots()
        {
            var source = new Context();
            var first = AddProfile(source, "First");
            var second = AddProfile(source, "Second");
            var sharedOldGuid = Guid.NewGuid();

            var firstInput = AddDeviceConfiguration(first, DeviceIoType.Input, "Keyboard A", "Core_Interception", "Keyboard\\A", 0);
            var firstOutput = AddDeviceConfiguration(first, DeviceIoType.Output, "Xbox 1", "Core_ViGEm", "xb360", 0);
            var secondInput = AddDeviceConfiguration(second, DeviceIoType.Input, "Keyboard B", "Core_Interception", "Keyboard\\B", 1);
            var secondOutput = AddDeviceConfiguration(second, DeviceIoType.Output, "Xbox 2", "Core_ViGEm", "xb360", 1);
            firstInput.Guid = sharedOldGuid;
            secondInput.Guid = sharedOldGuid;

            AddButtonMapping(first, "First Mapping", firstInput.Guid, firstOutput.Guid, 30, 0);
            AddButtonMapping(second, "Second Mapping", secondInput.Guid, secondOutput.Guid, 31, 1);

            var file = TempFile(".ucrprofiles");
            source.ProfilesManager.ExportProfileList(file, _pluginTypes);

            var destination = new Context();
            destination.ProfilesManager.ImportProfileList(file, ProfileListImportMode.Merge, _pluginTypes);

            Assert.That(destination.Profiles.Count, Is.EqualTo(2));
            Assert.That(destination.Profiles[0].InputDeviceConfigurations[0].Guid,
                Is.Not.EqualTo(destination.Profiles[1].InputDeviceConfigurations[0].Guid));
            AssertAllBindingReferencesResolve(destination.Profiles[0]);
            AssertAllBindingReferencesResolve(destination.Profiles[1]);
        }

        [Test]
        public void SingleProfileImportKeepsDanglingBindingUnresolvedWhenImportedAsChild()
        {
            var source = new Context();
            var profile = AddProfile(source, "Portable");
            var output = AddDeviceConfiguration(profile, DeviceIoType.Output, "Xbox", "Core_ViGEm", "xb360", 0);
            var missingInputGuid = Guid.NewGuid();
            AddButtonMapping(profile, "Missing input", missingInputGuid, output.Guid, 30, 0);

            var file = TempFile(".ucrprofile");
            source.ProfilesManager.ExportProfile(profile, file, _pluginTypes);

            var destination = new Context();
            var parent = AddProfile(destination, "Destination parent");
            var unrelatedInput = AddDeviceConfiguration(
                parent, DeviceIoType.Input, "Unrelated keyboard", "Core_Interception", "Keyboard\\Trap", 0);
            unrelatedInput.Guid = missingInputGuid;

            var imported = destination.ProfilesManager.ImportProfile(file, parent, _pluginTypes);
            var importedBinding = imported.Mappings[0].DeviceBindings[0];

            Assert.That(importedBinding.DeviceConfigurationGuid, Is.Not.EqualTo(Guid.Empty));
            Assert.That(importedBinding.DeviceConfigurationGuid, Is.Not.EqualTo(missingInputGuid),
                "A dangling imported binding must not attach itself to an unrelated parent configuration.");
            Assert.That(imported.GetDeviceConfiguration(DeviceIoType.Input, importedBinding.DeviceConfigurationGuid), Is.Null,
                "The unavailable device reference should remain unavailable after import.");
        }

        [Test]
        public void ProfileExportImportPreservesPersistedHardwarePath()
        {
            var source = new Context();
            var profile = AddProfile(source, "Hardware identity");
            var input = AddDeviceConfiguration(profile, DeviceIoType.Input, "DirectInput Pad",
                "SharpDX_DirectInput", "VID_1234&PID_5678", 0);
            input.Device.HidPath = @"\\?\hid#vid_1234&pid_5678#physical-a";

            var file = TempFile(".ucrprofile");
            source.ProfilesManager.ExportProfile(profile, file, _pluginTypes);

            var destination = new Context();
            var imported = destination.ProfilesManager.ImportProfile(file, null, _pluginTypes);

            Assert.That(imported.InputDeviceConfigurations[0].Device.HidPath,
                Is.EqualTo(input.Device.HidPath));
        }

        [Test]
        public void SingleProfileExportCarriesReferencedDeviceAliasWithoutOverwritingLocalAlias()
        {
            var source = new Context();
            var profile = AddProfile(source, "Alias source");
            var input = AddDeviceConfiguration(profile, DeviceIoType.Input, "DirectInput Pad",
                "SharpDX_DirectInput", "VID_1234&PID_5678", 0);
            input.Device.HidPath = @"\\?\hid#vid_1234&pid_5678#physical-a";

            var identity = DevicesManager.BuildAliasIdentity(input.Device);
            identity.Alias = "Arcade Stick";
            source.DeviceAliases.Add(identity);

            var file = TempFile(".ucrprofile");
            source.ProfilesManager.ExportProfile(profile, file, _pluginTypes);

            var destination = new Context();
            var localIdentity = identity.Clone();
            localIdentity.Alias = "My Local Stick Name";
            destination.DeviceAliases.Add(localIdentity);

            destination.ProfilesManager.ImportProfile(file, null, _pluginTypes);

            Assert.That(destination.DeviceAliases.Count, Is.EqualTo(1));
            Assert.That(destination.DeviceAliases[0].Alias, Is.EqualTo("My Local Stick Name"),
                "Importing a portable profile must not silently overwrite the destination user's global device name.");
        }

        [Test]
        public void SingleProfileExportCarriesAliasButNotLocalHideOrOrderPreferences()
        {
            var source = new Context();
            var profile = AddProfile(source, "Portable alias");
            var input = AddDeviceConfiguration(profile, DeviceIoType.Input, "DirectInput Pad",
                "SharpDX_DirectInput", "VID_1234&PID_5678", 0);
            input.Device.HidPath = @"\\?\hid#vid_1234&pid_5678#physical-a";

            var identity = DevicesManager.BuildAliasIdentity(input.Device);
            identity.Alias = "Arcade Stick";
            identity.Hidden = true;
            identity.SortOrder = 2;
            source.DeviceAliases.Add(identity);

            var file = TempFile(".ucrprofile");
            source.ProfilesManager.ExportProfile(profile, file, _pluginTypes);

            var destination = new Context();
            destination.ProfilesManager.ImportProfile(file, null, _pluginTypes);

            Assert.That(destination.DeviceAliases.Count, Is.EqualTo(1));
            Assert.That(destination.DeviceAliases[0].Alias, Is.EqualTo("Arcade Stick"));
            Assert.That(destination.DeviceAliases[0].Hidden, Is.False);
            Assert.That(destination.DeviceAliases[0].SortOrder, Is.EqualTo(int.MaxValue));
        }

        [Test]
        public void FullProfileListReplaceRestoresDeviceAliasesAsBackupState()
        {
            var source = new Context();
            AddProfile(source, "Backup");
            source.DeviceAliases.Add(new DeviceAlias
            {
                ProviderName = "SharpDX_XInput",
                IdentityKind = DeviceAliasIdentityKind.LogicalSlot,
                IdentityValue = "xb360",
                DeviceNumber = 0,
                Alias = "Player One Pad",
                Hidden = true,
                SortOrder = 3
            });
            source.DeviceAliases.Add(new DeviceAlias
            {
                ProviderName = "Core_Interception",
                IdentityKind = DeviceAliasIdentityKind.HardwareHandle,
                IdentityValue = @"Keyboard\VID_1111&PID_2222",
                DeviceNumber = 0,
                Alias = "Main Keyboard"
            });

            var file = TempFile(".ucrprofiles");
            source.ProfilesManager.ExportProfileList(file, _pluginTypes);

            var destination = new Context();
            destination.DeviceAliases.Add(new DeviceAlias
            {
                ProviderName = "Core_ViGEm",
                IdentityKind = DeviceAliasIdentityKind.LogicalSlot,
                IdentityValue = "ds4",
                DeviceNumber = 3,
                Alias = "Should disappear"
            });

            destination.ProfilesManager.ImportProfileList(file, ProfileListImportMode.Replace, _pluginTypes);

            Assert.That(destination.DeviceAliases.Count, Is.EqualTo(2));
            Assert.That(destination.DeviceAliases.Select(alias => alias.Alias),
                Is.EquivalentTo(new[] { "Player One Pad", "Main Keyboard" }));
            var restoredPlayerOne = destination.DeviceAliases.Single(alias =>
                alias.ProviderName.Equals("SharpDX_XInput", StringComparison.OrdinalIgnoreCase));
            Assert.That(restoredPlayerOne.Hidden, Is.True);
            Assert.That(restoredPlayerOne.SortOrder, Is.EqualTo(3));
        }

        [Test]
        public void FullProfileListMergeAddsMissingAliasesButPreservesDestinationConflicts()
        {
            var source = new Context();
            AddProfile(source, "Merge source");
            source.DeviceAliases.Add(new DeviceAlias
            {
                ProviderName = "SharpDX_XInput",
                IdentityKind = DeviceAliasIdentityKind.LogicalSlot,
                IdentityValue = "xb360",
                DeviceNumber = 0,
                Alias = "Source P1",
                Hidden = true,
                SortOrder = 1
            });
            source.DeviceAliases.Add(new DeviceAlias
            {
                ProviderName = "Core_ViGEm",
                IdentityKind = DeviceAliasIdentityKind.LogicalSlot,
                IdentityValue = "ds4",
                DeviceNumber = 1,
                Alias = "Imported DS4"
            });

            var file = TempFile(".ucrprofiles");
            source.ProfilesManager.ExportProfileList(file, _pluginTypes);

            var destination = new Context();
            destination.DeviceAliases.Add(new DeviceAlias
            {
                ProviderName = "sharpdx_xinput",
                IdentityKind = DeviceAliasIdentityKind.LogicalSlot,
                IdentityValue = "XB360",
                DeviceNumber = 0,
                Alias = "Destination P1",
                Hidden = false,
                SortOrder = 8
            });

            destination.ProfilesManager.ImportProfileList(file, ProfileListImportMode.Merge, _pluginTypes);

            Assert.That(destination.DeviceAliases.Count, Is.EqualTo(2));
            var destinationP1 = destination.DeviceAliases.Single(alias =>
                alias.ProviderName.Equals("sharpdx_xinput", StringComparison.OrdinalIgnoreCase));
            Assert.That(destinationP1.Alias, Is.EqualTo("Destination P1"));
            Assert.That(destinationP1.Hidden, Is.False);
            Assert.That(destinationP1.SortOrder, Is.EqualTo(8));
            Assert.That(destination.DeviceAliases.Single(alias => alias.ProviderName.Equals("Core_ViGEm", StringComparison.OrdinalIgnoreCase)).Alias,
                Is.EqualTo("Imported DS4"));
        }

        [Test]
        public void FullProfileListReplaceRestoresPresentationOnlyDevicePreference()
        {
            var source = new Context();
            AddProfile(source, "Backup");
            source.DeviceAliases.Add(new DeviceAlias
            {
                ProviderName = "Core_ViGEm",
                IdentityKind = DeviceAliasIdentityKind.LogicalSlot,
                IdentityValue = "ds4",
                DeviceNumber = 2,
                Alias = null,
                Hidden = true,
                SortOrder = 6
            });

            var file = TempFile(".ucrprofiles");
            source.ProfilesManager.ExportProfileList(file, _pluginTypes);

            var destination = new Context();
            destination.ProfilesManager.ImportProfileList(file, ProfileListImportMode.Replace, _pluginTypes);

            Assert.That(destination.DeviceAliases.Count, Is.EqualTo(1));
            Assert.That(destination.DeviceAliases[0].Alias, Is.Null);
            Assert.That(destination.DeviceAliases[0].Hidden, Is.True);
            Assert.That(destination.DeviceAliases[0].SortOrder, Is.EqualTo(6));
        }

        [Test]
        public void ProfileImporterRejectsNegativeDeviceDisplayOrder()
        {
            var source = new Context();
            var profile = AddProfile(source, "Invalid preference");
            var package = new ProfileExportPackage
            {
                FormatVersion = 1,
                Kind = ProfileExportKind.Profile,
                Profiles = new List<Profile> { profile },
                DeviceAliases = new List<DeviceAlias>
                {
                    new DeviceAlias
                    {
                        ProviderName = "SharpDX_XInput",
                        IdentityKind = DeviceAliasIdentityKind.LogicalSlot,
                        IdentityValue = "xb360",
                        DeviceNumber = 0,
                        Alias = "Player One",
                        SortOrder = -1
                    }
                }
            };

            var file = TempFile(".ucrprofile");
            var serializer = new XmlSerializer(typeof(ProfileExportPackage), _pluginTypes.ToArray());
            using (var writer = new StreamWriter(file)) serializer.Serialize(writer, package);

            var destination = new Context();
            Assert.Throws<InvalidDataException>(() =>
                destination.ProfilesManager.ImportProfile(file, null, _pluginTypes));
            Assert.That(destination.Profiles.Count, Is.EqualTo(0));
            Assert.That(destination.DeviceAliases.Count, Is.EqualTo(0));
        }

        [Test]
        public void LegacyVersionOneProfilePackageWithoutAliasCollectionStillImports()
        {
            var source = new Context();
            var profile = AddProfile(source, "Legacy compatible");
            var package = new ProfileExportPackage
            {
                FormatVersion = 1,
                Kind = ProfileExportKind.Profile,
                Profiles = new List<Profile> { profile },
                DeviceAliases = null
            };

            var file = TempFile(".ucrprofile");
            var serializer = new XmlSerializer(typeof(ProfileExportPackage), _pluginTypes.ToArray());
            using (var writer = new StreamWriter(file))
            {
                serializer.Serialize(writer, package);
            }

            var destination = new Context();
            var imported = destination.ProfilesManager.ImportProfile(file, null, _pluginTypes);

            Assert.That(imported.Title, Is.EqualTo("Legacy compatible"));
            Assert.That(destination.DeviceAliases, Is.Not.Null);
            Assert.That(destination.DeviceAliases.Count, Is.EqualTo(0));
        }

        [Test]
        public void ProfileImporterRejectsProfileListPackageWithoutMutatingContext()
        {
            var source = new Context();
            AddProfile(source, "Source");
            var file = TempFile(".ucrprofiles");
            source.ProfilesManager.ExportProfileList(file, _pluginTypes);

            var destination = new Context();
            AddProfile(destination, "Existing");

            Assert.Throws<InvalidDataException>(() => destination.ProfilesManager.ImportProfile(file, null, _pluginTypes));
            Assert.That(destination.Profiles.Count, Is.EqualTo(1));
            Assert.That(destination.Profiles[0].Title, Is.EqualTo("Existing"));
        }

        private string TempFile(string extension)
        {
            var path = Path.Combine(Path.GetTempPath(), "UCR-ProfileTransfer-" + Guid.NewGuid().ToString("N") + extension);
            _temporaryFiles.Add(path);
            return path;
        }

        private static Profile AddProfile(Context context, string title)
        {
            var profile = context.ProfilesManager.CreateProfile(title, null, null);
            context.ProfilesManager.AddProfile(profile);
            return profile;
        }

        private static DeviceConfiguration AddDeviceConfiguration(Profile profile, DeviceIoType direction,
            string title, string providerName, string deviceHandle, int deviceNumber)
        {
            var configuration = new DeviceConfiguration(new Device(title, providerName, deviceHandle, deviceNumber));
            profile.AddDeviceConfigurations(new List<DeviceConfiguration> { configuration }, direction);
            return configuration;
        }

        private static void AddButtonMapping(Profile profile, string title, Guid inputConfigurationGuid,
            Guid outputConfigurationGuid, int inputKeyValue, int outputKeyValue)
        {
            var mapping = profile.AddMapping(title);
            profile.AddPlugin(mapping, new ButtonToButton());

            var input = mapping.DeviceBindings[0];
            input.DeviceConfigurationGuid = inputConfigurationGuid;
            input.KeyType = 1;
            input.KeyValue = inputKeyValue;
            input.KeySubValue = 0;
            input.IsBound = true;

            var output = mapping.Plugins[0].Outputs[0];
            output.DeviceConfigurationGuid = outputConfigurationGuid;
            output.KeyType = 1;
            output.KeyValue = outputKeyValue;
            output.KeySubValue = 0;
            output.IsBound = true;
        }

        private static void AssertAllBindingReferencesResolve(Profile root)
        {
            AssertProfileBindingReferencesResolve(root);
        }

        private static void AssertProfileBindingReferencesResolve(Profile profile)
        {
            foreach (var mapping in profile.Mappings)
            {
                foreach (var binding in mapping.DeviceBindings.Where(binding => binding.DeviceConfigurationGuid != Guid.Empty))
                {
                    Assert.That(profile.GetDeviceConfiguration(DeviceIoType.Input, binding.DeviceConfigurationGuid), Is.Not.Null,
                        "Input binding should reference an available imported configuration.");
                }

                foreach (var plugin in mapping.Plugins)
                {
                    foreach (var binding in plugin.Outputs.Where(binding => binding.DeviceConfigurationGuid != Guid.Empty))
                    {
                        Assert.That(profile.GetDeviceConfiguration(DeviceIoType.Output, binding.DeviceConfigurationGuid), Is.Not.Null,
                            "Output binding should reference an available imported configuration.");
                    }
                }
            }

            foreach (var child in profile.ChildProfiles)
            {
                AssertProfileBindingReferencesResolve(child);
            }
        }
    }
}
