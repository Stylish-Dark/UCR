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

            Assert.That(state.FilterState.FilterRuntimeDictionary.ContainsKey("aim mode"), Is.True);
            Assert.That(state.FilterState.FilterRuntimeDictionary.ContainsKey("bogus reference"), Is.False);
            Assert.DoesNotThrow(() => producer.Update(1));
            Assert.That(state.FilterState.FilterRuntimeDictionary["aim mode"], Is.True);
        }

        [Test]
        public void UndefinedFilterWritesAreIgnoredInsteadOfCrashing()
        {
            var state = new FilterState();

            Assert.DoesNotThrow(() => state.SetFilterState("missing", true));
            Assert.DoesNotThrow(() => state.ToggleFilterState("missing"));
            Assert.That(state.FilterRuntimeDictionary, Is.Empty);
        }

        private SubscriptionState getSubscriptionState()
        {
            return _context.SubscriptionsManager.SubscriptionState;
        }
    }
}
