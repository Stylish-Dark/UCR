using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HidWizards.UCR.Core;
using HidWizards.UCR.Core.Managers;
using HidWizards.UCR.Core.Models;
using HidWizards.UCR.Core.Models.Binding;
using HidWizards.UCR.Plugins.Filter;
using HidWizards.UCR.Plugins.Remapper;
using HidWizards.UCR.Tests.Factory;
using HidWizards.UCR.ViewModels.ProfileViewModels;
using HidWizards.UCR.ViewModels.Dashboard;
using NUnit.Framework;

namespace HidWizards.UCR.Tests.ModelTests
{
    [TestFixture]
    internal class ProfileTests
    {
        private Context _context;
        private Profile _profile;
        private Mapping _mapping;
        private string _profileName;

        [SetUp]
        public void Setup()
        {
            _context = new Context();
            var profile = _context.ProfilesManager.CreateProfile("Base Profile", null, null);
            _context.ProfilesManager.AddProfile(profile);
            _profile = _context.Profiles[0];
            _mapping = _profile.AddMapping("Test mapping");
            _profileName = "Test";
        }

        [Test]
        public void AddChildProfile()
        {
            Assert.That(_profile.ChildProfiles.Count, Is.EqualTo(0));
            var childProfile = _context.ProfilesManager.CreateProfile(_profileName, null, null);
            _profile.AddChildProfile(childProfile);
            Assert.That(_profile.ChildProfiles.Count, Is.EqualTo(1));
            Assert.That(_profile.ChildProfiles[0].Title, Is.EqualTo(_profileName));
            Assert.That(_profile.ChildProfiles[0].ParentProfile, Is.EqualTo(_profile));
            Assert.That(_profile.ChildProfiles[0].Guid, Is.Not.EqualTo(Guid.Empty));
            Assert.That(_profile.IsActive, Is.Not.True);
            Assert.That(_context.IsNotSaved, Is.True);
        }
        
        [Test]
        public void RemoveChildProfile()
        {
            Assert.That(_profile.ChildProfiles.Count, Is.EqualTo(0));
            var childProfile = _context.ProfilesManager.CreateProfile(_profileName, null, null);
            _profile.AddChildProfile(childProfile);
            Assert.That(_profile.ChildProfiles.Count, Is.EqualTo(1));
            Assert.That(_profile.ChildProfiles[0].Title, Is.EqualTo(_profileName));
            _profile.ChildProfiles[0].Remove();
            Assert.That(_profile.ChildProfiles.Count, Is.EqualTo(0));
            Assert.That(_context.IsNotSaved, Is.True);
        }


        [Test]
        public void NewMappingGroupsStartEnabled()
        {
            var group = _profile.AddMappingGroup("Extras");

            Assert.That(group.Enabled, Is.True);
        }

        [Test]
        public void MappingGroupsBelongOnlyToTheirProfileAndCopyIndependently()
        {
            var group = _profile.AddMappingGroup("Player 2");
            var original = group.AddMapping("Attack");
            original.AddPlugin(new ButtonToButton());

            var copy = _profile.CopyMappingGroup(group, "Player 2 Copy");

            Assert.That(_profile.MappingGroups.Count, Is.EqualTo(2));
            Assert.That(copy.Title, Is.EqualTo("Player 2 Copy"));
            Assert.That(copy.Mappings.Count, Is.EqualTo(1));
            Assert.That(copy.Mappings[0], Is.Not.SameAs(original));
            Assert.That(copy.Mappings[0].Title, Is.EqualTo("Attack"));
            copy.Mappings[0].Rename("Changed");
            Assert.That(original.Title, Is.EqualTo("Attack"));
        }

        [Test]
        public void CopyingProfileRegeneratesDeviceConfigurationIdsAndBindingReferences()
        {
            var input = new DeviceConfiguration(new Device("Keyboard", "Core_Interception", "kbd", 0));
            var output = new DeviceConfiguration(new Device("Controller", "Core_ViGEm", "pad", 0));
            var source = _context.ProfilesManager.CreateProfile("Source",
                new List<DeviceConfiguration> { input }, new List<DeviceConfiguration> { output });
            _context.ProfilesManager.AddProfile(source);

            var mapping = source.AddMapping("Bound mapping");
            var plugin = new ButtonToButton();
            mapping.AddPlugin(plugin);
            mapping.DeviceBindings.Single().DeviceConfigurationGuid = input.Guid;
            mapping.DeviceBindings.Single().IsBound = true;
            plugin.Outputs.Single().DeviceConfigurationGuid = output.Guid;
            plugin.Outputs.Single().IsBound = true;

            var sourceGroup = source.AddMappingGroup("Extras");
            var groupMapping = sourceGroup.AddMapping("Grouped binding");
            var groupPlugin = new ButtonToButton();
            groupMapping.AddPlugin(groupPlugin);
            groupMapping.DeviceBindings.Single().DeviceConfigurationGuid = input.Guid;
            groupMapping.DeviceBindings.Single().IsBound = true;
            groupPlugin.Outputs.Single().DeviceConfigurationGuid = output.Guid;
            groupPlugin.Outputs.Single().IsBound = true;

            Assert.That(_context.ProfilesManager.CopyProfile(source, "Copy"), Is.True);
            var copy = _context.Profiles.Last();

            Assert.That(copy.Guid, Is.Not.EqualTo(source.Guid));
            Assert.That(copy.InputDeviceConfigurations.Single().Guid, Is.Not.EqualTo(input.Guid));
            Assert.That(copy.OutputDeviceConfigurations.Single().Guid, Is.Not.EqualTo(output.Guid));
            Assert.That(copy.MappingGroups.Single().Guid, Is.Not.EqualTo(sourceGroup.Guid));
            Assert.That(copy.Mappings.Single().DeviceBindings.Single().DeviceConfigurationGuid,
                Is.EqualTo(copy.InputDeviceConfigurations.Single().Guid));
            Assert.That(copy.Mappings.Single().Plugins.Single().Outputs.Single().DeviceConfigurationGuid,
                Is.EqualTo(copy.OutputDeviceConfigurations.Single().Guid));
            Assert.That(copy.MappingGroups.Single().Mappings.Single().DeviceBindings.Single().DeviceConfigurationGuid,
                Is.EqualTo(copy.InputDeviceConfigurations.Single().Guid));
            Assert.That(copy.MappingGroups.Single().Mappings.Single().Plugins.Single().Outputs.Single().DeviceConfigurationGuid,
                Is.EqualTo(copy.OutputDeviceConfigurations.Single().Guid));
        }

        [Test]
        public void MappingGroupCopyAcrossProfilesRemapsMatchingConfiguredDevices()
        {
            var sourceInput = new DeviceConfiguration(new Device("Keyboard", "Core_Interception", "kbd", 0));
            var sourceOutput = new DeviceConfiguration(new Device("Controller", "Core_ViGEm", "pad", 1));
            var source = _context.ProfilesManager.CreateProfile("Source",
                new List<DeviceConfiguration> { sourceInput }, new List<DeviceConfiguration> { sourceOutput });
            _context.ProfilesManager.AddProfile(source);

            var targetInput = new DeviceConfiguration(new Device("Keyboard", "Core_Interception", "kbd", 0));
            var targetOutput = new DeviceConfiguration(new Device("Controller", "Core_ViGEm", "pad", 1));
            var target = _context.ProfilesManager.CreateProfile("Target",
                new List<DeviceConfiguration> { targetInput }, new List<DeviceConfiguration> { targetOutput });
            _context.ProfilesManager.AddProfile(target);

            var group = source.AddMappingGroup("Player 2");
            var mapping = group.AddMapping("Attack");
            var plugin = new ButtonToButton();
            mapping.AddPlugin(plugin);
            mapping.DeviceBindings.Single().DeviceConfigurationGuid = sourceInput.Guid;
            mapping.DeviceBindings.Single().IsBound = true;
            plugin.Outputs.Single().DeviceConfigurationGuid = sourceOutput.Guid;
            plugin.Outputs.Single().IsBound = true;

            var copy = target.CopyMappingGroup(group, source, "Player 2");

            Assert.That(copy.Mappings.Single().DeviceBindings.Single().DeviceConfigurationGuid, Is.EqualTo(targetInput.Guid));
            Assert.That(copy.Mappings.Single().Plugins.Single().Outputs.Single().DeviceConfigurationGuid, Is.EqualTo(targetOutput.Guid));
        }

        [Test]
        public void ProfileViewModelAddsMappingsToTheSelectedLocalGroup()
        {
            var viewModel = new ProfileViewModel(_profile);
            var group = viewModel.AddMappingGroup("Extras");
            viewModel.SelectedMappingSection = group;

            var added = viewModel.AddMappingToSelectedSection("Utility mapping");

            Assert.That(added, Is.Not.Null);
            Assert.That(_profile.Mappings.Any(mapping => mapping.Title == "Utility mapping"), Is.False);
            Assert.That(group.Model.Mappings.Single().Title, Is.EqualTo("Utility mapping"));
            viewModel.Dispose();
        }

        [Test]
        public void LegacyChildBecomesDisabledLocalMappingGroupOnPostLoad()
        {
            var secondOutput = new DeviceConfiguration(new Device("P2 Pad", "Core_ViGEm", "p2", 1));
            var child = _context.ProfilesManager.CreateProfile("Player 2", null,
                new List<DeviceConfiguration> { secondOutput });
            child.AddMapping("P2 Attack");
            _profile.AddChildProfile(child);

            _profile.PostLoad(_context);

            Assert.That(_profile.ChildProfiles, Is.Empty);
            Assert.That(_profile.MappingGroups.Count, Is.EqualTo(1));
            Assert.That(_profile.MappingGroups[0].Title, Is.EqualTo("Player 2"));
            Assert.That(_profile.MappingGroups[0].Enabled, Is.False);
            Assert.That(_profile.MappingGroups[0].Mappings.Select(mapping => mapping.Title), Is.EquivalentTo(new[] { "P2 Attack" }));
            Assert.That(_profile.OutputDeviceConfigurations.Any(configuration => configuration.Guid == secondOutput.Guid), Is.True);
        }

        [Test]
        public void DashboardProfileListIsFlatAfterChildMigration()
        {
            var child = _context.ProfilesManager.CreateProfile("Player 2", null, null);
            _profile.AddChildProfile(child);
            _profile.PostLoad(_context);

            var profiles = ProfileItem.GetProfileTree(_context.Profiles);

            Assert.That(profiles.Count, Is.EqualTo(1));
            Assert.That(profiles[0].Items, Is.Empty);
            Assert.That(profiles[0].HasChildren, Is.False);
        }

        [Test]
        public void RenameProfile()
        {
            var newName = "Renamed Profile";
            Assert.That(_profile.Rename(newName), Is.True);
            Assert.That(_profile.Title, Is.EqualTo(newName));
            Assert.That(_context.IsNotSaved, Is.True);
        }

        [Test]
        public void DeviceAliasBecomesProfileDisplayNameUnlessConfigurationNameOverridesIt()
        {
            var device = new Device("Provider Keyboard", "Core_Interception", @"Keyboard\VID_1111&PID_2222", 0);
            var configuration = new DeviceConfiguration(device);
            _profile.AddDeviceConfigurations(new List<DeviceConfiguration> { configuration }, DeviceIoType.Input);

            var alias = DevicesManager.BuildAliasIdentity(device);
            alias.Alias = "Desk Keyboard";
            _context.DeviceAliases.Add(alias);

            Assert.That(configuration.GetFullTitleForProfile(_profile), Is.EqualTo("Desk Keyboard"));

            configuration.ChangeConfigurationName("Movement Keys");
            Assert.That(configuration.GetFullTitleForProfile(_profile), Is.EqualTo("Movement Keys"));
        }

        [Test]
        public void InheritedDeviceTitleDoesNotExposeParentProfileName()
        {
            var device = new Device("Laptop KB", "Core_Interception", @"Keyboard\VID_1111&PID_2222", 0);
            var configuration = new DeviceConfiguration(device);
            _profile.AddDeviceConfigurations(new List<DeviceConfiguration> { configuration }, DeviceIoType.Input);
            var child = _context.ProfilesManager.CreateProfile("Devil May Cry 3 - Player 2", null, null);
            _profile.AddChildProfile(child);

            Assert.That(configuration.GetFullTitleForProfile(child), Is.EqualTo("Laptop KB (Inherited)"));
        }


        [Test]
        public void DashboardProfileListStaysFlatAndDoesNotExposeLegacyChildren()
        {
            var child = _context.ProfilesManager.CreateProfile("Child", null, null);
            _profile.AddChildProfile(child);
            child.AddMapping("Child mapping");
            _profile.PostLoad(_context);

            var list = ProfileItem.GetProfileTree(_context.Profiles);

            Assert.That(list.Count, Is.EqualTo(1));
            Assert.That(list[0].Depth, Is.EqualTo(0));
            Assert.That(list[0].HasChildren, Is.False);
            Assert.That(list[0].Items, Is.Empty);
            Assert.That(_profile.MappingGroups.Single().Title, Is.EqualTo("Child"));
        }

        [Test]
        public void DashboardTracksSeveralActiveProfilesAtOnce()
        {
            var second = _context.ProfilesManager.CreateProfile("Second", null, null);
            _context.ProfilesManager.AddProfile(second);
            var list = ProfileItem.GetProfileTree(_context.Profiles);

            Assert.That(_context.SubscriptionsManager.ActivateProfile(_profile, false), Is.True);
            Assert.That(_context.SubscriptionsManager.ActivateProfile(second, false), Is.True);
            ProfileItem.SetActiveProfiles(list);

            Assert.That(list.Count, Is.EqualTo(2));
            Assert.That(list.All(item => item.IsActive), Is.True);
        }

        [Test]
        public void DashboardKeepsPlayAvailableForAnActiveProfileHotplugRebuild()
        {
            var dashboard = new DashboardViewModel(_context);
            dashboard.SelectedProfileItem = dashboard.ProfileList.First();

            Assert.That(_context.SubscriptionsManager.ActivateProfile(_profile, false), Is.True);
            Assert.That(dashboard.CanActivateProfile, Is.True,
                "Play must remain available so the active profile can rebuild subscriptions after device changes.");
        }

        [Test]
        public void ProfileViewModelExplainsWhyEditingIsLockedWhileRunning()
        {
            var viewModel = new ProfileViewModel(_profile);
            Assert.That(viewModel.IsProfileActive, Is.False);
            Assert.That(viewModel.EditLockReason, Is.Null);

            Assert.That(_context.SubscriptionsManager.ActivateProfile(_profile, false), Is.True);

            Assert.That(viewModel.IsProfileActive, Is.True);
            Assert.That(viewModel.CanEditProfile, Is.False);
            Assert.That(viewModel.CanActivateProfile, Is.True,
                "Play remains available while active so USB hotplug can rebuild the composite runtime.");
            Assert.That(viewModel.EditLockReason, Is.EqualTo("Profile is running — stop it to edit mappings."));
            viewModel.Dispose();
        }

        [Test]
        public void AddOutputMenuUsesConciseDestinationNameForSimpleRoutes()
        {
            var option = new SimplePluginViewModel(new ButtonToFilter());

            Assert.That(option.OutputType, Is.EqualTo("Filter"));
            Assert.That(option.MenuLabel, Is.EqualTo("Filter"));
        }

        [Test]
        public void PluginEditorViewModelsAreDeferredUntilRequested()
        {
            var producerMapping = _profile.AddMapping("Producer");
            _profile.AddPlugin(producerMapping, new ButtonToFilter { FilterName = "Mode" });

            var consumerMapping = _profile.AddMapping("Consumer");
            var consumer = new ButtonToButton();
            _profile.AddPlugin(consumerMapping, consumer);
            consumer.AddFilter("Mode");

            var profileViewModel = new ProfileViewModel(_profile);
            var consumerViewModel = profileViewModel.MappingsList
                .Single(mapping => ReferenceEquals(mapping.Mapping, consumerMapping));
            var pluginViewModel = consumerViewModel.Plugins.Single();

            Assert.That(pluginViewModel.Filters, Is.Empty,
                "Collapsed mappings should not construct filter editor view-models.");
            pluginViewModel.EnsureEditorInitialized();
            Assert.That(pluginViewModel.Filters.Select(filter => filter.Name), Is.EqualTo(new[] { "Mode" }));

            profileViewModel.Dispose();
        }

        [Test]
        public void BindingDeviceListCanRefreshAfterProfileDevicesChange()
        {
            var first = new DeviceConfiguration(new Device("Keyboard A", "Core_Interception", "kbd-a", 0));
            var second = new DeviceConfiguration(new Device("Keyboard B", "Core_Interception", "kbd-b", 1));
            _profile.AddDeviceConfigurations(new List<DeviceConfiguration> { first }, DeviceIoType.Input);

            var binding = new DeviceBinding(value => { }, _profile, DeviceIoType.Input)
            {
                DeviceBindingCategory = DeviceBindingCategory.Momentary
            };
            binding.SetDeviceConfigurationGuid(first.Guid, false);
            var viewModel = new DeviceBindingViewModel(binding);

            Assert.That(viewModel.Devices, Is.Empty,
                "Collapsed mappings should not build a device dropdown until its editor is shown.");
            viewModel.EnsureDeviceListLoaded();
            Assert.That(viewModel.Devices.Count, Is.EqualTo(1));

            _profile.AddDeviceConfigurations(new List<DeviceConfiguration> { second }, DeviceIoType.Input);
            viewModel.RefreshDeviceList();

            Assert.That(viewModel.Devices.Count, Is.EqualTo(2));
            Assert.That(viewModel.Devices.Any(item => item.Value == second.Guid), Is.True);
            Assert.That(viewModel.Devices.All(item => item.Visual != null), Is.True,
                "Every real device choice should carry its semantic visual shorthand.");
            viewModel.Dispose();
        }

        [Test]
        public void DeviceBindingCurrentValueOnlyNotifiesWhenTheValueActuallyChanges()
        {
            var binding = new DeviceBinding(value => { }, _profile, DeviceIoType.Input);
            var notifications = 0;
            binding.PropertyChanged += (sender, args) =>
            {
                if (args.PropertyName == nameof(DeviceBinding.CurrentValue)) notifications++;
            };

            binding.CurrentValue = 123;
            binding.CurrentValue = 123;
            binding.CurrentValue = 456;

            Assert.That(notifications, Is.EqualTo(2));
        }

        [Test]
        public void GuiInvalidationIsConsumedAfterOneRefresh()
        {
            var binding = new DeviceBinding(value => { }, _profile, DeviceIoType.Input)
            {
                DeviceBindingCategory = DeviceBindingCategory.Momentary
            };
            var viewModel = new DeviceBindingViewModel(binding);
            var currentValueNotifications = 0;
            var previewNotifications = 0;
            var previewVisibilityNotifications = 0;
            viewModel.PropertyChanged += (sender, args) =>
            {
                if (args.PropertyName == nameof(DeviceBindingViewModel.CurrentValue)) currentValueNotifications++;
                if (args.PropertyName == nameof(DeviceBindingViewModel.PreviewValue)) previewNotifications++;
                if (args.PropertyName == nameof(DeviceBindingViewModel.ShowButtonPreview)) previewVisibilityNotifications++;
            };

            viewModel.CurrentValue = 1;
            viewModel.CurrentValueChanged();
            viewModel.CurrentValueChanged();

            Assert.That(currentValueNotifications, Is.EqualTo(1));
            Assert.That(previewNotifications, Is.EqualTo(1));
            Assert.That(previewVisibilityNotifications, Is.EqualTo(0),
                "Input value changes must not invalidate preview visibility; visibility depends only on bind/profile state.");
            viewModel.Dispose();
        }

        [Test]
        public void BindModeChangeInvalidatesPreviewVisibility()
        {
            var binding = new DeviceBinding(value => { }, _profile, DeviceIoType.Input)
            {
                DeviceBindingCategory = DeviceBindingCategory.Momentary
            };
            var viewModel = new DeviceBindingViewModel(binding);
            var previewVisibilityNotifications = 0;
            viewModel.PropertyChanged += (sender, args) =>
            {
                if (args.PropertyName == nameof(DeviceBindingViewModel.ShowButtonPreview)) previewVisibilityNotifications++;
            };

            var setter = typeof(DeviceBinding).GetProperty(nameof(DeviceBinding.IsInBindMode)).GetSetMethod(true);
            setter.Invoke(binding, new object[] { true });

            Assert.That(previewVisibilityNotifications, Is.EqualTo(1));
            viewModel.Dispose();
        }

        [Test]
        public void PrimaryDeviceDefaultsToFirstAndCanBeChangedPerProfile()
        {
            var first = new DeviceConfiguration(new Device("Keyboard A", "Core_Interception", "kbd-a", 0));
            var second = new DeviceConfiguration(new Device("Keyboard B", "Core_Interception", "kbd-b", 1));
            _profile.AddDeviceConfigurations(new List<DeviceConfiguration> { first, second }, DeviceIoType.Input);

            Assert.That(_profile.GetPrimaryDeviceConfiguration(DeviceIoType.Input), Is.SameAs(first));
            Assert.That(_profile.SetPrimaryDeviceConfiguration(DeviceIoType.Input, second.Guid), Is.True);
            Assert.That(_profile.GetPrimaryDeviceConfiguration(DeviceIoType.Input), Is.SameAs(second));
            Assert.That(_profile.PrimaryInputDeviceConfigurationGuid, Is.EqualTo(second.Guid));
        }

        [Test]
        public void ChildCanChooseInheritedPrimaryWithoutChangingParent()
        {
            var first = new DeviceConfiguration(new Device("Pad A", "Core_ViGEm", "pad-a", 0));
            var second = new DeviceConfiguration(new Device("Pad B", "Core_ViGEm", "pad-b", 1));
            _profile.AddDeviceConfigurations(new List<DeviceConfiguration> { first, second }, DeviceIoType.Output);
            _profile.SetPrimaryDeviceConfiguration(DeviceIoType.Output, first.Guid);

            var child = _context.ProfilesManager.CreateProfile("Child", null, null);
            _profile.AddChildProfile(child);
            Assert.That(child.GetPrimaryDeviceConfiguration(DeviceIoType.Output), Is.SameAs(first),
                "A child without an override should inherit its parent's primary device.");
            Assert.That(child.SetPrimaryDeviceConfiguration(DeviceIoType.Output, second.Guid), Is.True);

            Assert.That(_profile.GetPrimaryDeviceConfiguration(DeviceIoType.Output), Is.SameAs(first));
            Assert.That(child.GetPrimaryDeviceConfiguration(DeviceIoType.Output), Is.SameAs(second));
        }

        [Test]
        public void MoveMappingChangesPersistedMappingOrder()
        {
            _mapping.Rename("First");
            var second = _profile.AddMapping("Second");
            var third = _profile.AddMapping("Third");

            Assert.That(_profile.MoveMapping(third, 0), Is.True);
            Assert.That(_profile.Mappings, Is.EqualTo(new[] { third, _mapping, second }));
            Assert.That(_profile.Mappings.Select(mapping => mapping.Title),
                Is.EqualTo(new[] { "Third", "First", "Second" }));
        }

        [Test]
        public void InputAxisReverseIsAppliedBeforeMappingCallback()
        {
            short callbackValue = 0;
            var binding = new DeviceBinding(value => callbackValue = value, _profile, DeviceIoType.Input)
            {
                DeviceBindingCategory = DeviceBindingCategory.Range
            };

            binding.SetInvertInput(true);
            binding.Callback(short.MinValue);

            Assert.That(callbackValue, Is.EqualTo(short.MaxValue));
            Assert.That(binding.CurrentValue, Is.EqualTo(short.MaxValue));
        }

        [Test]
        public void AddPlugin()
        {
            _profile.AddPlugin(_mapping, new ButtonToButton());
            var plugin = _mapping.Plugins[0];

            Assert.That(plugin, Is.Not.Null);
            Assert.That(plugin.Outputs, Is.Not.Null);
            Assert.That(plugin.Profile, Is.EqualTo(_profile));
            Assert.That(_context.IsNotSaved, Is.True);
        }

        [Test]
        public void FilterDefinitionsComeFromFilterMappingsNotConsumerReferences()
        {
            var producerMapping = _profile.AddMapping("Filter producer");
            var producer = new ButtonToFilter { FilterName = "Aim Mode" };
            _profile.AddPlugin(producerMapping, producer);

            var consumerMapping = _profile.AddMapping("Consumer");
            var consumer = new ButtonToButton();
            _profile.AddPlugin(consumerMapping, consumer);
            consumer.AddFilter("Not A Definition");

            var definitions = _profile.GetFilters();

            Assert.That(definitions, Does.Contain("Aim Mode"));
            Assert.That(definitions, Does.Not.Contain("Not A Definition"));
        }

        [Test]
        public void RenamingFilterDefinitionRenamesExistingReferences()
        {
            var producerMapping = _profile.AddMapping("Filter producer");
            var producer = new ButtonToFilter { FilterName = "Aim Mode" };
            _profile.AddPlugin(producerMapping, producer);

            var consumerMapping = _profile.AddMapping("Consumer");
            var consumer = new ButtonToButton();
            _profile.AddPlugin(consumerMapping, consumer);
            consumer.AddFilter("Aim Mode");

            var filterNameProperty = producer.PluginPropertyGroups
                .SelectMany(group => group.PluginProperties)
                .Single(property => property.PropertyInfo.Name == nameof(ButtonToFilter.FilterName));
            filterNameProperty.Property = "Precision Mode";

            Assert.That(consumer.Filters.Single().Name, Is.EqualTo("Precision Mode"));
            Assert.That(_profile.GetFilters(), Does.Contain("Precision Mode"));
            Assert.That(_profile.GetFilters(), Does.Not.Contain("Aim Mode"));
        }

        [Test]
        public void RenamingOneOfDuplicateFilterDefinitionsDoesNotStealExistingReferences()
        {
            var firstMapping = _profile.AddMapping("First producer");
            var first = new ButtonToFilter { FilterName = "Shared" };
            _profile.AddPlugin(firstMapping, first);

            var secondMapping = _profile.AddMapping("Second producer");
            var second = new ButtonToFilter { FilterName = "Shared" };
            _profile.AddPlugin(secondMapping, second);

            var consumerMapping = _profile.AddMapping("Consumer");
            var consumer = new ButtonToButton();
            _profile.AddPlugin(consumerMapping, consumer);
            consumer.AddFilter("Shared");

            var filterNameProperty = first.PluginPropertyGroups
                .SelectMany(group => group.PluginProperties)
                .Single(property => property.PropertyInfo.Name == nameof(ButtonToFilter.FilterName));
            filterNameProperty.Property = "Renamed";

            Assert.That(consumer.Filters.Single().Name, Is.EqualTo("Shared"));
            Assert.That(_profile.GetFilters(), Does.Contain("Shared"));
            Assert.That(_profile.GetFilters(), Does.Contain("Renamed"));
        }

        [Test]
        public void CopyProfile()
        {
            var profileManager = new ProfilesManager(_context, _context.Profiles);
            var profile = _context.Profiles[0];
            profileManager.CopyProfile(profile, "Copy");
            var newProfile = _context.Profiles[1];

            Assert.That(newProfile.Guid, Is.Not.EqualTo(profile.Guid));
            Assert.That(newProfile.Title, Is.EqualTo("Copy"));
            Assert.That(newProfile.ParentProfile, Is.Null);
            Assert.That(newProfile.Context, Is.Not.Null);
        }

        [Test]
        public void CopyChildProfile()
        {
            var profileManager = new ProfilesManager(_context, _context.Profiles);
            var parentProfile = _context.Profiles[0];
            var childProfile = _context.ProfilesManager.CreateProfile("Child", null, null);
            parentProfile.AddChildProfile(childProfile);
            var profile = parentProfile.ChildProfiles[0];
            profileManager.CopyProfile(profile, "Copy");
            var newProfile = parentProfile.ChildProfiles[1];

            Assert.That(newProfile.Guid, Is.Not.EqualTo(profile.Guid));
            Assert.That(newProfile.Title, Is.EqualTo("Copy"));
            Assert.That(newProfile.ParentProfile.Guid, Is.EqualTo(parentProfile.Guid));
            Assert.That(newProfile.Context, Is.Not.Null);
        }
    }
}
