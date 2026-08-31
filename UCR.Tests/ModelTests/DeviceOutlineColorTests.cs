using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using HidWizards.UCR.Core.Models;
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
        public void DefaultColorsDoNotReuseReservedColor()
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var first = DeviceOutlineColors.GenerateUniqueDefault("device-a", used);
            used.Add(first);
            var second = DeviceOutlineColors.GenerateUniqueDefault("device-a", used);

            Assert.That(DeviceOutlineColors.NormalizeHex(first), Is.EqualTo(first));
            Assert.That(DeviceOutlineColors.NormalizeHex(second), Is.EqualTo(second));
            Assert.That(string.Equals(second, first, StringComparison.OrdinalIgnoreCase), Is.False);
        }

        [Test]
        public void ManyDefaultDevicesReceiveDistinctColors()
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < 128; i++)
            {
                var color = DeviceOutlineColors.GenerateUniqueDefault("device-" + i, used);
                Assert.That(DeviceOutlineColors.NormalizeHex(color), Is.EqualTo(color));
                Assert.That(used.Add(color), Is.True, "Default colour was reused at device " + i);
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
