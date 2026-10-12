using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using HidWizards.UCR.Core.Models.Binding;
using HidWizards.UCR.Plugins.Remapper;
using NUnit.Framework;

namespace HidWizards.UCR.Tests.ModelTests
{
    [TestFixture]
    class AttributeTests
    {
        [Test]
        public void ButtonToButtonIoTest()
        {
            var plugin = new ButtonToButton();
            var inputs = plugin.InputCategories;
            var outputs = plugin.OutputCategories;

            Assert.That(inputs.Count, Is.EqualTo(1));
            Assert.That(inputs[0].Category, Is.EqualTo(DeviceBindingCategory.Momentary));
            Assert.That(inputs[0].Name, Is.EqualTo("Button"));
            Assert.That(outputs.Count, Is.EqualTo(1));
            Assert.That(outputs[0].Category, Is.EqualTo(DeviceBindingCategory.Momentary));
            Assert.That(outputs[0].Name, Is.EqualTo("Button"));
        }

        [Test]
        public void ButtonToButtonGuiMatrixTest()
        {
            var plugin = new ButtonToButton();

            var guiMatrix = plugin.GetGuiMatrix();
            var invertProperty = guiMatrix[0].PluginProperties[0];

            Assert.AreEqual(invertProperty.Name, "Invert");
        }

        [Test]
        public void FourWayDPadUsesOneOutputPerDirectionAndSupportsInversion()
        {
            var plugin = new ButtonsToDPad();
            Assert.That(plugin.InputCategories.Count, Is.EqualTo(4));
            Assert.That(plugin.OutputCategories.Count, Is.EqualTo(4));
            Assert.That(plugin.OutputCategories.All(category => category.Category == DeviceBindingCategory.Momentary), Is.True);
            plugin.Update(1, 0, 0, 0);
            Assert.That(plugin.Outputs[0].CurrentValue, Is.EqualTo(1));
            Assert.That(plugin.Outputs[1].CurrentValue, Is.EqualTo(0));
            plugin.InvertY = true;
            plugin.Update(1, 0, 0, 0);
            Assert.That(plugin.Outputs[0].CurrentValue, Is.EqualTo(0));
            Assert.That(plugin.Outputs[1].CurrentValue, Is.EqualTo(1));
        }

        [Test]
        public void ReleaseMetadataIdentifiesCurrentVersion()
        {
            var assembly = typeof(HidWizards.UCR.App).Assembly;
            var informational = assembly.GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false)
                .Cast<AssemblyInformationalVersionAttribute>()
                .Single();
            var versionInfo = FileVersionInfo.GetVersionInfo(assembly.Location);

            Assert.That(assembly.GetName().Version, Is.EqualTo(new Version(0, 9, 17, 0)));
            Version releaseBuild;
            Assert.That(Version.TryParse(versionInfo.FileVersion, out releaseBuild), Is.True);
            Assert.That(releaseBuild.Major, Is.EqualTo(0));
            Assert.That(releaseBuild.Minor, Is.EqualTo(9));
            // The file build/revision fields encode UTC days since 2020-01-01
            // and minute-of-day; the human-readable product version carries
            // the full UTC timestamp rather than a per-workflow counter.
            var match = System.Text.RegularExpressions.Regex.Match(
                informational.InformationalVersion,
                @"^v0\.9\.17-(\d{8})\.(\d{6})Z$");
            Assert.That(match.Success, Is.True);
            var timestamp = DateTime.ParseExact(match.Groups[1].Value + match.Groups[2].Value,
                "yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture);
            var expectedDay = (int)(timestamp.Date - new DateTime(2020, 1, 1)).TotalDays;
            Assert.That(releaseBuild.Build, Is.EqualTo(expectedDay));
            Assert.That(releaseBuild.Revision, Is.EqualTo(timestamp.Hour * 60 + timestamp.Minute));
            Assert.That(versionInfo.ProductVersion, Is.EqualTo(informational.InformationalVersion));
        }
    }
}
