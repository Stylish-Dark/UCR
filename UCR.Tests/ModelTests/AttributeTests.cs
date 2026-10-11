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
        public void ReleaseMetadataMatchesAssemblyVersion()
        {
            var assembly = typeof(HidWizards.UCR.App).Assembly;
            var informational = assembly.GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false)
                .Cast<AssemblyInformationalVersionAttribute>()
                .Single();
            var versionInfo = FileVersionInfo.GetVersionInfo(assembly.Location);

            var assemblyVersion = assembly.GetName().Version;
            var informationalVersion = "v" + assemblyVersion.ToString(3);
            Assert.That(versionInfo.FileVersion, Is.EqualTo(assemblyVersion.ToString()));
            Assert.That(informational.InformationalVersion, Is.EqualTo(informationalVersion));
            Assert.That(versionInfo.ProductVersion, Is.EqualTo(informationalVersion));
        }
    }
}
