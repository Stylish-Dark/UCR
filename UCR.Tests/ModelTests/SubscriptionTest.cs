using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HidWizards.UCR.Core;
using HidWizards.UCR.Core.Models;
using HidWizards.UCR.Core.Models.Subscription;
using HidWizards.UCR.Plugins.Filter;
using HidWizards.UCR.Plugins.Remapper;
using NUnit.Framework;

namespace HidWizards.UCR.Tests.ModelTests
{
    [TestFixture]
    internal class SubscriptionTest
    {

        private Context _context;
        private Profile _profile;
        private string _profileName;

        [SetUp]
        public void Setup()
        {
            _context = new Context();
            _profileName = "Test";
            var profile = _context.ProfilesManager.CreateProfile(_profileName, null, null);
            _context.ProfilesManager.AddProfile(profile);
            _profile = _context.Profiles[0];
        }

        [Test]
        public void TestEmptyProfile()
        {
            Assert.IsTrue(_context.SubscriptionsManager.ActivateProfile(_profile, false));

            var state = getSubscriptionState();
            Assert.IsTrue(state.IsActive);
            Assert.AreEqual(0, state.MappingSubscriptions.Count);
        }

        [Test]
        public void TestOneBindingProfile()
        {
            var mapping = _profile.AddMapping("Button");
            var plugin = new ButtonToButton();
            mapping.AddPlugin(plugin);

            Assert.IsTrue(_context.SubscriptionsManager.ActivateProfile(_profile, false));

            var state = getSubscriptionState();
            Assert.IsTrue(state.IsActive);
            Assert.AreEqual(1, state.MappingSubscriptions.Count);
            Assert.AreEqual(1, state.MappingSubscriptions[0].PluginSubscriptions.Count);
            Assert.AreEqual(plugin, state.MappingSubscriptions[0].PluginSubscriptions[0].Plugin);
        }

        [Test]
        public void FilterRuntimeDictionaryUsesDefinitionsAndProducerCanWriteThem()
        {
            var producerMapping = _profile.AddMapping("Filter producer");
            var producer = new ButtonToFilter { FilterName = "Aim Mode" };
            producerMapping.AddPlugin(producer);

            var consumerMapping = _profile.AddMapping("Consumer");
            var consumer = new ButtonToButton();
            consumerMapping.AddPlugin(consumer);
            consumer.AddFilter("Bogus Reference");

            Assert.IsTrue(_context.SubscriptionsManager.ActivateProfile(_profile, false));
            var state = getSubscriptionState();

            var runtimeKey = Mapping.GetRuntimeFilterKey(_profile.Guid, "aim mode");
            Assert.That(state.FilterState.FilterRuntimeDictionary.ContainsKey(runtimeKey), Is.True);
            Assert.That(state.FilterState.FilterRuntimeDictionary.Keys.Any(key => key.EndsWith(":bogus reference")), Is.False);
            Assert.DoesNotThrow(() => producer.Update(1));
            Assert.That(state.FilterState.FilterRuntimeDictionary[runtimeKey], Is.True);
        }

        [Test]
        public void UndefinedFilterWritesAreIgnoredInsteadOfCrashing()
        {
            var state = new FilterState();

            Assert.DoesNotThrow(() => state.SetFilterState("missing", true));
            Assert.DoesNotThrow(() => state.ToggleFilterState("missing"));
            Assert.That(state.FilterRuntimeDictionary, Is.Empty);
        }

        [Test]
        public void PressingPlayOnActiveProfileRebuildsRuntimeStateAfterDeviceRefresh()
        {
            Assert.IsTrue(_context.SubscriptionsManager.ActivateProfile(_profile, false));
            var originalState = getSubscriptionState();

            Assert.IsTrue(_context.SubscriptionsManager.ActivateProfile(_profile, true));
            var rebuiltState = getSubscriptionState();

            Assert.That(rebuiltState, Is.Not.SameAs(originalState),
                "Play after a device refresh must not leave stale subscriptions attached to the old USB endpoint.");
            Assert.That(rebuiltState.StateGuid, Is.Not.EqualTo(originalState.StateGuid));
            Assert.That(rebuiltState.IsActive, Is.True);
        }

        [Test]
        public void MultipleProfilesShareOneCompositeRuntimeUntilIndividuallyStopped()
        {
            var second = _context.ProfilesManager.CreateProfile("Second", null, null);
            _context.ProfilesManager.AddProfile(second);
            _profile.AddMapping("First mapping");
            second.AddMapping("Second mapping");

            Assert.IsTrue(_context.SubscriptionsManager.ActivateProfile(_profile, false));
            Assert.IsTrue(_context.SubscriptionsManager.ActivateProfile(second, false));

            var combined = getSubscriptionState();
            Assert.That(combined.ActiveProfiles.Select(profile => profile.Guid),
                Is.EquivalentTo(new[] { _profile.Guid, second.Guid }));
            Assert.That(combined.MappingSubscriptions.Count, Is.EqualTo(2));
            Assert.That(_profile.IsActive(), Is.True);
            Assert.That(second.IsActive(), Is.True);

            Assert.IsTrue(_context.SubscriptionsManager.DeactivateProfile(second));
            var remaining = getSubscriptionState();
            Assert.That(remaining.ActiveProfiles.Select(profile => profile.Guid), Is.EquivalentTo(new[] { _profile.Guid }));
            Assert.That(_profile.IsActive(), Is.True);
            Assert.That(second.IsActive(), Is.False);
        }

        [Test]
        public void RemovingOneActiveProfileKeepsTheOtherProfileRunning()
        {
            var second = _context.ProfilesManager.CreateProfile("Second", null, null);
            _context.ProfilesManager.AddProfile(second);
            _profile.AddMapping("First mapping");
            second.AddMapping("Second mapping");

            Assert.IsTrue(_context.SubscriptionsManager.ActivateProfile(_profile, false));
            Assert.IsTrue(_context.SubscriptionsManager.ActivateProfile(second, false));

            _profile.Remove();

            Assert.That(_context.Profiles.Any(profile => profile.Guid == _profile.Guid), Is.False);
            Assert.That(_profile.IsActive(), Is.False);
            Assert.That(second.IsActive(), Is.True);
            Assert.That(getSubscriptionState().ActiveProfiles.Select(profile => profile.Guid),
                Is.EquivalentTo(new[] { second.Guid }));
        }

        [Test]
        public void EnabledMappingGroupsJoinTheProfileRuntimeAndDisabledGroupsDoNot()
        {
            _profile.AddMapping("Main");
            var playerTwo = _profile.AddMappingGroup("Player 2");
            playerTwo.AddMapping("P2 Attack");
            playerTwo.Enabled = false;

            Assert.IsTrue(_context.SubscriptionsManager.ActivateProfile(_profile, false));
            Assert.That(getSubscriptionState().MappingSubscriptions.Select(subscription => subscription.Mapping.Title),
                Is.EquivalentTo(new[] { "Main" }));

            Assert.IsTrue(_context.SubscriptionsManager.DeactivateProfile(_profile));
            playerTwo.Enabled = true;
            Assert.IsTrue(_context.SubscriptionsManager.ActivateProfile(_profile, false));
            Assert.That(getSubscriptionState().MappingSubscriptions.Select(subscription => subscription.Mapping.Title),
                Is.EquivalentTo(new[] { "Main", "P2 Attack" }));
        }

        [Test]
        public void DuplicateHistoricalOutputConfigurationGuidsStayScopedToTheirProfiles()
        {
            var second = _context.ProfilesManager.CreateProfile("Second", null, null);
            _context.ProfilesManager.AddProfile(second);
            var sharedLegacyGuid = Guid.NewGuid();

            var firstOutput = new DeviceConfiguration(new Device("First pad", "Core_ViGEm", "pad-one", 0))
            {
                Guid = sharedLegacyGuid
            };
            var secondOutput = new DeviceConfiguration(new Device("Second pad", "Core_ViGEm", "pad-two", 1))
            {
                Guid = sharedLegacyGuid
            };
            _profile.AddDeviceConfigurations(new List<DeviceConfiguration> { firstOutput }, DeviceIoType.Output);
            second.AddDeviceConfigurations(new List<DeviceConfiguration> { secondOutput }, DeviceIoType.Output);

            var firstMapping = _profile.AddMapping("First route");
            var firstPlugin = new ButtonToButton();
            firstMapping.AddPlugin(firstPlugin);
            firstPlugin.Outputs.Single().DeviceConfigurationGuid = sharedLegacyGuid;
            firstPlugin.Outputs.Single().IsBound = true;

            var secondMapping = second.AddMapping("Second route");
            var secondPlugin = new ButtonToButton();
            secondMapping.AddPlugin(secondPlugin);
            secondPlugin.Outputs.Single().DeviceConfigurationGuid = sharedLegacyGuid;
            secondPlugin.Outputs.Single().IsBound = true;

            var state = new SubscriptionState(new[] { _profile, second });
            var firstSubscription = state.AddOutputDeviceConfiguration(firstOutput, _profile.Guid);
            var secondSubscription = state.AddOutputDeviceConfiguration(secondOutput, second.Guid);
            state.AddMappings(_profile, _profile.Mappings, _profile.Guid,
                new List<DeviceConfigurationSubscription> { firstSubscription });
            state.AddMappings(second, second.Mappings, second.Guid,
                new List<DeviceConfigurationSubscription> { secondSubscription });

            Assert.That(state.OutputDeviceConfigurationSubscriptions.Count, Is.EqualTo(2));
            Assert.That(state.MappingSubscriptions[0].PluginSubscriptions.Single().OutputSubscriptions.Single()
                    .DeviceSubscription.Device.Title, Is.EqualTo("First pad"));
            Assert.That(state.MappingSubscriptions[1].PluginSubscriptions.Single().OutputSubscriptions.Single()
                    .DeviceSubscription.Device.Title, Is.EqualTo("Second pad"));
        }

        [Test]
        public void SameNamedFiltersAreScopedPerActiveProfile()
        {
            var second = _context.ProfilesManager.CreateProfile("Second", null, null);
            _context.ProfilesManager.AddProfile(second);

            var firstFilter = new ButtonToFilter { FilterName = "Mode" };
            _profile.AddMapping("First filter").AddPlugin(firstFilter);
            var secondFilter = new ButtonToFilter { FilterName = "Mode" };
            second.AddMapping("Second filter").AddPlugin(secondFilter);

            Assert.IsTrue(_context.SubscriptionsManager.ActivateProfile(_profile, false));
            Assert.IsTrue(_context.SubscriptionsManager.ActivateProfile(second, false));
            var state = getSubscriptionState();

            Assert.That(state.FilterState.FilterRuntimeDictionary.Keys.Count(key => key.EndsWith(":mode")), Is.EqualTo(2));
            firstFilter.Update(1);
            var firstKey = Mapping.GetRuntimeFilterKey(_profile.Guid, "mode");
            var secondKey = Mapping.GetRuntimeFilterKey(second.Guid, "mode");
            Assert.That(state.FilterState.FilterRuntimeDictionary[firstKey], Is.True);
            Assert.That(state.FilterState.FilterRuntimeDictionary[secondKey], Is.False);
        }

        [Test]
        public void SameNamedMappingsFromDifferentActiveProfilesBothRemainLive()
        {
            var second = _context.ProfilesManager.CreateProfile("Second", null, null);
            _context.ProfilesManager.AddProfile(second);
            _profile.AddMapping("Shared name");
            second.AddMapping("Shared name");

            Assert.IsTrue(_context.SubscriptionsManager.ActivateProfile(_profile, false));
            Assert.IsTrue(_context.SubscriptionsManager.ActivateProfile(second, false));

            var subscriptions = getSubscriptionState().MappingSubscriptions
                .Where(subscription => subscription.Mapping.Title == "Shared name").ToList();
            Assert.That(subscriptions.Count, Is.EqualTo(2));
            Assert.That(subscriptions.All(subscription => !subscription.Overriden), Is.True);
        }

        [Test]
        public void EnabledGroupCanOverrideSameNamedMainMappingInsideItsOwnProfile()
        {
            _profile.AddMapping("Shared name");
            var group = _profile.AddMappingGroup("Player 2");
            group.Enabled = true;
            group.AddMapping("Shared name");

            Assert.IsTrue(_context.SubscriptionsManager.ActivateProfile(_profile, false));

            var subscriptions = getSubscriptionState().MappingSubscriptions
                .Where(subscription => subscription.Mapping.Title == "Shared name").ToList();
            Assert.That(subscriptions.Count, Is.EqualTo(2));
            Assert.That(subscriptions[0].Overriden, Is.True);
            Assert.That(subscriptions[1].Overriden, Is.False);
        }

        private SubscriptionState getSubscriptionState()
        {
            return _context.SubscriptionsManager.SubscriptionState;
        }
    }
}
