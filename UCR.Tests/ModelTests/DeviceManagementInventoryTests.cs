using System;
using System.IO;
using System.Linq;
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
        public void ManagementInventoryKeepsConfiguredDevicesWhenIoControllerIsUnavailable()
        {
            var context = new Context();
            context.IOController?.Dispose();
            context.IOController = null;

            var profile = new Profile(context) { Title = "Configured" };
            var keyboard = new Device("K: Configured Keyboard", "Core_Interception",
                @"Keyboard\HID\VID_1111&PID_2222", 4)
            {
                HidPath = @"\\?\HID#VID_1111&PID_2222#configured-keyboard"
            };
            var controller = new Device("ViGEm Xbox 360 Controller 1", "Core_ViGEm", "xb360", 0);
            profile.InputDeviceConfigurations.Add(new DeviceConfiguration(keyboard));
            profile.OutputDeviceConfigurations.Add(new DeviceConfiguration(controller));
            context.Profiles.Add(profile);

            var inputs = context.DevicesManager.GetManagementDeviceList(DeviceIoType.Input);
            var outputs = context.DevicesManager.GetManagementDeviceList(DeviceIoType.Output);

            Assert.That(inputs.Any(device => device.ProviderName == "Core_Interception" &&
                                             device.DeviceHandle == keyboard.DeviceHandle), Is.True,
                "The Devices page must not become blank merely because live provider enumeration is unavailable.");
            Assert.That(outputs.Any(device => device.ProviderName == "Core_ViGEm" &&
                                              device.DeviceHandle == "xb360"), Is.True,
                "Persisted output devices must remain manageable while provider enumeration is unavailable.");
        }

        [Test]
        public void ManagementInventoryIncludesNestedProfileAndShadowDevicesWithoutDuplicatingPrimaryDevice()
        {
            var context = new Context();
            context.IOController?.Dispose();
            context.IOController = null;

            var parent = new Profile(context) { Title = "Parent" };
            var child = new Profile(context, parent) { Title = "Child" };
            parent.ChildProfiles.Add(child);
            context.Profiles.Add(parent);

            var primary = new Device("Primary Keyboard", "Core_Interception",
                @"Keyboard\HID\VID_AAAA&PID_BBBB", 1)
            {
                HidPath = @"\\?\HID#VID_AAAA&PID_BBBB#primary"
            };
            var shadow = new Device("Shadow Keyboard", "Core_Interception",
                @"Keyboard\HID\VID_CCCC&PID_DDDD", 2)
            {
                HidPath = @"\\?\HID#VID_CCCC&PID_DDDD#shadow"
            };
            var configuration = new DeviceConfiguration(primary);
            configuration.ShadowDevices.Add(shadow);
            child.InputDeviceConfigurations.Add(configuration);

            var devices = context.DevicesManager.GetManagementDeviceList(DeviceIoType.Input);

            Assert.That(devices.Count(device => device.DeviceHandle == primary.DeviceHandle), Is.EqualTo(1));
            Assert.That(devices.Count(device => device.DeviceHandle == shadow.DeviceHandle), Is.EqualTo(1));
        }

        [Test]
        public void ManagementInventoryKeepsDistinctLogicalOutputSlots()
        {
            var context = new Context();
            context.IOController?.Dispose();
            context.IOController = null;

            var profile = new Profile(context) { Title = "Two controllers" };
            profile.OutputDeviceConfigurations.Add(new DeviceConfiguration(
                new Device("ViGEm Xbox 360 Controller 1", "Core_ViGEm", "xb360", 0)));
            profile.OutputDeviceConfigurations.Add(new DeviceConfiguration(
                new Device("ViGEm Xbox 360 Controller 2", "Core_ViGEm", "xb360", 1)));
            context.Profiles.Add(profile);

            var outputs = context.DevicesManager.GetManagementDeviceList(DeviceIoType.Output)
                .Where(device => device.ProviderName == "Core_ViGEm" && device.DeviceHandle == "xb360")
                .ToList();

            Assert.That(outputs.Count, Is.EqualTo(2),
                "Management fallback must not collapse distinct numbered output slots just because their handles match.");
            Assert.That(outputs.Select(device => device.DeviceNumber), Is.EquivalentTo(new[] { 0, 1 }));
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
        public void DeviceManagerViewModelUsesConfiguredFallbackWhenIoControllerIsUnavailable()
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

            var viewModel = new DeviceManagerViewModel(context.DevicesManager);
            try
            {
                Assert.That(viewModel.Devices.Any(item => item.Device.ProviderName == "Core_Interception"), Is.True);
                Assert.That(viewModel.Devices.Any(item => item.Device.ProviderName == "Core_ViGEm"), Is.True);
            }
            finally
            {
                viewModel.Dispose();
            }
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

                Assert.That(Path.GetFullPath(Environment.CurrentDirectory),
                    Is.EqualTo(Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory)));
                Assert.That(Path.GetFullPath(applicationDirectory),
                    Is.EqualTo(Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory)));
            }
            finally
            {
                Directory.SetCurrentDirectory(original);
                Directory.Delete(temporary, true);
            }
        }
    }
}
