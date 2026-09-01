using System;
using System.IO;
using System.Xml.Serialization;
using HidWizards.UCR.Core.Models;
using HidWizards.UCR.ViewModels.Dashboard;
using HidWizards.UCR.ViewModels.Presentation;
using NUnit.Framework;

namespace HidWizards.UCR.Tests.ModelTests
{
    [TestFixture]
    internal class DeviceOutlineColorTests
    {
        [Test]
        public void RequestedOutlineColorOptionsAreAvailable()
        {
            CollectionAssert.AreEqual(new[]
            {
                DeviceOutlineColor.Default,
                DeviceOutlineColor.Red,
                DeviceOutlineColor.Green,
                DeviceOutlineColor.Blue,
                DeviceOutlineColor.Yellow,
                DeviceOutlineColor.Cyan,
                DeviceOutlineColor.Pink,
                DeviceOutlineColor.Orange,
                DeviceOutlineColor.Purple,
                DeviceOutlineColor.White
            }, DeviceOutlineColors.Options);
        }

        [Test]
        public void SemanticDeviceColoursRemainExactlyAsBeforeOutlineOverrides()
        {
            Assert.That(DeviceVisualCatalog.XboxBrush.ToString(), Is.EqualTo("#FF00A800"));
            Assert.That(DeviceVisualCatalog.PlayStationBrush.ToString(), Is.EqualTo("#FF0069FF"));
            Assert.That(DeviceVisualCatalog.VJoyBrush.ToString(), Is.EqualTo("#FF8C00E8"));
            Assert.That(DeviceVisualCatalog.NeutralBrush.ToString(), Is.EqualTo("#FFCACDD2"));
        }

        [Test]
        public void DefaultOutlineKeepsOriginalSemanticDeviceColour()
        {
            var context = new HidWizards.UCR.Core.Context();
            var profile = new Profile(context);
            var xbox = new Device("ViGEm Xbox 360 Controller 1", "Core_ViGEm", "xb360", 0)
            {
                Profile = profile
            };
            context.DeviceAliases.Add(new DeviceAlias
            {
                ProviderName = "Core_ViGEm",
                IdentityKind = DeviceAliasIdentityKind.LogicalSlot,
                IdentityValue = "xb360",
                DeviceNumber = 0,
                OutlineColor = DeviceOutlineColor.Default,
                // 0.9.9q may have written this field. It must no longer affect Default.
                DefaultOutlineColor = "#FF00FF"
            });

            var visual = DeviceVisualCatalog.Describe(xbox, DeviceIoType.Output);

            Assert.That(visual.AccentBrush, Is.SameAs(DeviceVisualCatalog.XboxBrush));
            Assert.That(visual.OutlineBrush, Is.SameAs(DeviceVisualCatalog.XboxBrush));
        }

        [Test]
        public void OutlineOverrideChangesOnlyTheOutline()
        {
            var context = new HidWizards.UCR.Core.Context();
            var profile = new Profile(context);
            var xbox = new Device("ViGEm Xbox 360 Controller 1", "Core_ViGEm", "xb360", 0)
            {
                Profile = profile
            };
            context.DeviceAliases.Add(new DeviceAlias
            {
                ProviderName = "Core_ViGEm",
                IdentityKind = DeviceAliasIdentityKind.LogicalSlot,
                IdentityValue = "xb360",
                DeviceNumber = 0,
                OutlineColor = DeviceOutlineColor.Red
            });

            var visual = DeviceVisualCatalog.Describe(xbox, DeviceIoType.Output);

            Assert.That(visual.AccentBrush, Is.SameAs(DeviceVisualCatalog.XboxBrush),
                "Badge/text colour must stay at the original semantic device colour.");
            Assert.That(visual.OutlineBrush, Is.Not.SameAs(DeviceVisualCatalog.XboxBrush));
            Assert.That(visual.OutlineBrush.ToString(), Is.EqualTo("#FFE53935"));
        }

        [Test]
        public void DeviceManagerOffersTenVisualSwatchesWithSemanticDefault()
        {
            var xbox = new Device("ViGEm Xbox 360 Controller 1", "Core_ViGEm", "xb360", 0);
            var item = new DeviceManagerItemViewModel(xbox, DeviceIoType.Output, true, null, false,
                "xbox", DeviceOutlineColor.Default);

            Assert.That(item.AvailableOutlineColors.Length, Is.EqualTo(10));
            Assert.That(item.AvailableOutlineColors[0].Value, Is.EqualTo(DeviceOutlineColor.Default));
            Assert.That(item.AvailableOutlineColors[0].Brush, Is.SameAs(DeviceVisualCatalog.XboxBrush));
            foreach (var choice in item.AvailableOutlineColors)
            {
                Assert.That(choice.Brush, Is.Not.Null);
            }
        }

        [Test]
        public void DeviceAliasClonePreservesOutlinePresentation()
        {
            var alias = new DeviceAlias
            {
                ProviderName = "Core_ViGEm",
                IdentityKind = DeviceAliasIdentityKind.LogicalSlot,
                IdentityValue = "xb360",
                DeviceNumber = 0,
                OutlineColor = DeviceOutlineColor.Cyan,
                DefaultOutlineColor = "#123ABC"
            };

            var clone = alias.Clone();

            Assert.That(clone.OutlineColor, Is.EqualTo(DeviceOutlineColor.Cyan));
            Assert.That(clone.DefaultOutlineColor, Is.EqualTo("#123ABC"));
        }

        [Test]
        public void DeviceAliasXmlRoundTripPreservesOutlinePresentation()
        {
            var serializer = new XmlSerializer(typeof(DeviceAlias));
            var alias = new DeviceAlias
            {
                ProviderName = "Core_ViGEm",
                IdentityKind = DeviceAliasIdentityKind.LogicalSlot,
                IdentityValue = "ds4",
                DeviceNumber = 0,
                OutlineColor = DeviceOutlineColor.Purple,
                DefaultOutlineColor = "#ABC123"
            };

            string xml;
            using (var writer = new StringWriter())
            {
                serializer.Serialize(writer, alias);
                xml = writer.ToString();
            }

            DeviceAlias restored;
            using (var reader = new StringReader(xml))
            {
                restored = (DeviceAlias)serializer.Deserialize(reader);
            }

            Assert.That(restored.OutlineColor, Is.EqualTo(DeviceOutlineColor.Purple));
            Assert.That(restored.DefaultOutlineColor, Is.EqualTo("#ABC123"));
        }
    }
}
