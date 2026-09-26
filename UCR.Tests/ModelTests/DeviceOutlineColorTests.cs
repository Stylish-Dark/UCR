using System;
using System.IO;
using System.Xml.Serialization;
using HidWizards.UCR.Core.Managers;
using HidWizards.UCR.Core.Models;
using HidWizards.UCR.ViewModels.Dashboard;
using HidWizards.UCR.ViewModels.DeviceViewModels;
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
        public void ConfiguredColourChangesDeviceGlyphAndKeepsSemanticOutline()
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
                "Semantic device accent must remain controller-family aligned.");
            Assert.That(visual.OutlineBrush, Is.SameAs(DeviceVisualCatalog.XboxBrush),
                "Badge outline must remain controller-family aligned.");
            Assert.That(visual.BadgeTextBrush.ToString(), Is.EqualTo("#FFE53935"));
            Assert.That(visual.GlyphBrush.ToString(), Is.EqualTo("#FFE53935"),
                "The per-device colour choice should tint the vector glyph.");
        }

        [Test]
        public void AddDevicePickerRowsExposeSemanticDeviceIcons()
        {
            var playStation = new Device("PS4 DualShock", "Core_ViGEm", "ds4", 0);
            var xbox = new Device("X360 Controller", "Core_ViGEm", "xb360", 0);

            Assert.That(new DeviceViewModel(playStation, DeviceIoType.Output).Visual.Kind,
                Is.EqualTo(DeviceVisualKind.PlayStation4));
            Assert.That(new DeviceViewModel(xbox, DeviceIoType.Output).Visual.Kind,
                Is.EqualTo(DeviceVisualKind.Xbox360));
        }


        [Test]
        public void AddDevicePickerUsesStoredGlyphColourWithoutChangingSemanticOutline()
        {
            var context = new HidWizards.UCR.Core.Context();
            var keyboard = new Device("Kayla's KB", "Core_Interception", @"Keyboard\HID\VID_046D&PID_C534", 0);
            var alias = DevicesManager.BuildAliasIdentity(keyboard);
            Assert.That(alias, Is.Not.Null);
            alias.OutlineColor = DeviceOutlineColor.Pink;
            context.DeviceAliases.Add(alias);

            var item = new DeviceViewModel(keyboard, DeviceIoType.Input, context.DevicesManager);

            Assert.That(item.Visual.OutlineBrush, Is.SameAs(DeviceVisualCatalog.NeutralBrush));
            Assert.That(item.Visual.BadgeTextBrush.ToString(), Is.EqualTo("#FFFF4081"));
            Assert.That(item.Visual.GlyphBrush.ToString(), Is.EqualTo("#FFFF4081"));
        }

        [Test]
        public void DeviceManagerOffersTenGlyphColourSwatchesWithSlotDefault()
        {
            var xbox = new Device("ViGEm Xbox 360 Controller 1", "Core_ViGEm", "xb360", 0);
            var item = new DeviceManagerItemViewModel(xbox, DeviceIoType.Output, true, null, false,
                "xbox", DeviceOutlineColor.Default);

            Assert.That(item.AvailableTextColors.Length, Is.EqualTo(10));
            Assert.That(item.AvailableTextColors[0].Value, Is.EqualTo(DeviceOutlineColor.Default));
            Assert.That(item.AvailableTextColors[0].Brush.ToString(), Is.EqualTo("#FF4DA3FF"));
            foreach (var choice in item.AvailableTextColors)
            {
                Assert.That(choice.Brush, Is.Not.Null);
            }
        }

        [Test]
        public void DeviceManagerCurrentColourTracksSelectedGlyphColour()
        {
            var xbox = new Device("ViGEm Xbox 360 Controller 1", "Core_ViGEm", "xb360", 0);
            var item = new DeviceManagerItemViewModel(xbox, DeviceIoType.Output, true, null, false,
                "xbox", DeviceOutlineColor.Default);

            Assert.That(item.CurrentTextBrush.ToString(), Is.EqualTo("#FF4DA3FF"));

            item.TextColor = DeviceOutlineColor.Red;

            Assert.That(item.CurrentTextBrush.ToString(), Is.EqualTo("#FFE53935"));
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
