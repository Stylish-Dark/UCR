using System;
using System.IO;
using HidWizards.UCR.Core;
using HidWizards.UCR.Core.Models;
using HidWizards.UCR.Core.Utilities;
using HidWizards.UCR.ViewModels.Dashboard;
using NUnit.Framework;

namespace HidWizards.UCR.Tests.ModelTests
{
    [TestFixture]
    [NonParallelizable]
    internal class DeviceManagementInventoryTests
    {
        [Test]
        public void ManagementInventoryDoesNotFabricateConfiguredDevicesWhenProvidersAreUnavailable()
        {
            var context = new Context();
            context.IOController?.Dispose();
            context.IOController = null;

            var profile = new Profile(context) { Title = "Configured" };
            profile.InputDeviceConfigurations.Add(new DeviceConfiguration(
                new Device("Configured Keyboard", "Core_Interception", @"Keyboard\HID\VID_1234&PID_5678", 0)));
            profile.OutputDeviceConfigurations.Add(new DeviceConfiguration(
                new Device("ViGEm Xbox 360 Controller 1", "Core_ViGEm", "xb360", 0)));
            context.Profiles.Add(profile);

            Assert.That(context.DevicesManager.GetManagementDeviceList(DeviceIoType.Input), Is.Empty,
                "The Devices page must represent runtime inventory, not fabricate disconnected rows from profile configuration.");
            Assert.That(context.DevicesManager.GetManagementDeviceList(DeviceIoType.Output), Is.Empty);
        }

        [Test]
        public void ProviderHealthCheckReportsUnavailableControllerInsteadOfPretendingRuntimeIsHealthy()
        {
            var context = new Context();
            context.IOController?.Dispose();
            context.IOController = null;

            Assert.That(context.DevicesManager.HasLoadedProviderReports(), Is.False);
        }

        [Test]
        public void DeviceManagerViewModelExplainsEmptyInventoryWhenProvidersUnavailable()
        {
            var context = new Context();
            context.IOController?.Dispose();
            context.IOController = null;

            var viewModel = new DeviceManagerViewModel(context.DevicesManager);
            try
            {
                Assert.That(viewModel.Devices, Is.Empty);
                Assert.That(viewModel.DetectionStatus,
                    Is.EqualTo("Device providers are unavailable. Check the UCR log or restart and accept the unblock prompt if offered."));
            }
            finally
            {
                viewModel.Dispose();
            }
        }

        [Test]
        public void RuntimePathManagerNormalizesRelativeRuntimePathsToExecutableDirectory()
        {
            var original = Environment.CurrentDirectory;
            var temporary = Path.Combine(Path.GetTempPath(), "ucr-cwd-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporary);

            try
            {
                Directory.SetCurrentDirectory(temporary);
                var applicationDirectory = RuntimePathManager.NormalizeWorkingDirectory();

                var expectedDirectory = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                Assert.That(
                    Path.GetFullPath(Environment.CurrentDirectory)
                        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    Is.EqualTo(expectedDirectory));
                Assert.That(
                    Path.GetFullPath(applicationDirectory)
                        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    Is.EqualTo(expectedDirectory));
            }
            finally
            {
                Directory.SetCurrentDirectory(original);
                Directory.Delete(temporary, true);
            }
        }
    }
}
