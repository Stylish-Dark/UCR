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
