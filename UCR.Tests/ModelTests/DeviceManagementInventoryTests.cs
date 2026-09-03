using System;
using System.IO;
using HidWizards.UCR.Core;
using HidWizards.UCR.Core.Managers;
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
            var original = Environment.CurrentDirectory;
            var temporary = Path.Combine(Path.GetTempPath(), "ucr-no-cache-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporary);

            try
            {
                Directory.SetCurrentDirectory(temporary);
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
                    "The Devices page must not fabricate disconnected rows from profile configuration when there is no real provider cache.");
                Assert.That(context.DevicesManager.GetManagementDeviceList(DeviceIoType.Output), Is.Empty);
            }
            finally
            {
                Directory.SetCurrentDirectory(original);
                Directory.Delete(temporary, true);
            }
        }

        [Test]
        public void ManagementInventoryUsesPersistedDeviceCacheWhenProvidersAreUnavailable()
        {
            var original = Environment.CurrentDirectory;
            var temporary = Path.Combine(Path.GetTempPath(), "ucr-device-cache-test-" + Guid.NewGuid().ToString("N"));
            var providerDirectory = Path.Combine(temporary, "Cache", "Core_Interception");
            Directory.CreateDirectory(providerDirectory);

            try
            {
                Directory.SetCurrentDirectory(temporary);
                File.WriteAllText(Path.Combine(providerDirectory, "keyboard.json"),
                    "{\"Title\":\"K: Cached Keyboard\",\"ProviderName\":\"Core_Interception\",\"DeviceHandle\":\"Keyboard\\\\Cached\",\"DeviceNumber\":0,\"HidPath\":\"HID\\\\VID_CAFE&PID_BEEF\",\"DeviceBindingMenu\":[]}");

                var context = new Context();
                context.IOController?.Dispose();
                context.IOController = null;

                var devices = context.DevicesManager.GetManagementDeviceList(DeviceIoType.Input);

                Assert.That(devices.Count, Is.EqualTo(1));
                Assert.That(devices[0].Title, Is.EqualTo("K: Cached Keyboard"));
                Assert.That(devices[0].ProviderName, Is.EqualTo("Core_Interception"));
                Assert.That(devices[0].IsCache, Is.True);
                Assert.That(context.DevicesManager.GetManagementDeviceList(DeviceIoType.Output), Is.Empty,
                    "The existing cache format stores input-device binding menus only; it must not fabricate output rows.");
            }
            finally
            {
                Directory.SetCurrentDirectory(original);
                Directory.Delete(temporary, true);
            }
        }

        [Test]
        public void DeviceManagerShowsCachedInventoryInsteadOfBlankPageWhenProvidersAreUnavailable()
        {
            var original = Environment.CurrentDirectory;
            var temporary = Path.Combine(Path.GetTempPath(), "ucr-device-cache-viewmodel-test-" + Guid.NewGuid().ToString("N"));
            var providerDirectory = Path.Combine(temporary, "Cache", "Core_Interception");
            Directory.CreateDirectory(providerDirectory);

            try
            {
                Directory.SetCurrentDirectory(temporary);
                File.WriteAllText(Path.Combine(providerDirectory, "keyboard.json"),
                    "{\"Title\":\"K: Cached Keyboard\",\"ProviderName\":\"Core_Interception\",\"DeviceHandle\":\"Keyboard\\\\Cached\",\"DeviceNumber\":0,\"HidPath\":\"HID\\\\VID_CAFE&PID_BEEF\",\"DeviceBindingMenu\":[]}");

                var context = new Context();
                context.IOController?.Dispose();
                context.IOController = null;

                using (var viewModel = new DeviceManagerViewModel(context.DevicesManager))
                {
                    Assert.That(viewModel.Devices.Count, Is.EqualTo(1),
                        "A provider outage must not collapse the Devices page to a blank list when UCR has a real device cache.");
                    Assert.That(viewModel.Devices[0].ProviderDeviceName, Is.EqualTo("K: Cached Keyboard"));
                    Assert.That(viewModel.DetectionStatus,
                        Is.EqualTo("Showing previously detected devices from UCR's cache; live device providers are currently unavailable."));
                }
            }
            finally
            {
                Directory.SetCurrentDirectory(original);
                Directory.Delete(temporary, true);
            }
        }

        [Test]
        public void ProviderReportCollectionKeepsHealthyProvidersWhenOneProviderThrows()
        {
            var healthyReport = new HidWizards.IOWrapper.DataTransferObjects.ProviderReport
            {
                ProviderDescriptor = new HidWizards.IOWrapper.DataTransferObjects.ProviderDescriptor
                {
                    ProviderName = "Healthy"
                }
            };
            var errors = new System.Collections.Generic.List<string>();
            var probes = new[]
            {
                new System.Collections.Generic.KeyValuePair<string, Func<HidWizards.IOWrapper.DataTransferObjects.ProviderReport>>(
                    "Broken", () => { throw new InvalidOperationException("provider failure"); }),
                new System.Collections.Generic.KeyValuePair<string, Func<HidWizards.IOWrapper.DataTransferObjects.ProviderReport>>(
                    "Healthy", () => healthyReport)
            };

            var reports = DevicesManager.CollectProviderReports(probes,
                (providerName, exception) => errors.Add(providerName + ":" + exception.Message));

            Assert.That(reports.Count, Is.EqualTo(1));
            Assert.That(reports.ContainsKey("Healthy"), Is.True);
            Assert.That(reports["Healthy"], Is.SameAs(healthyReport));
            Assert.That(errors.Count, Is.EqualTo(1));
            Assert.That(errors[0], Does.StartWith("Broken:"));
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
            var original = Environment.CurrentDirectory;
            var temporary = Path.Combine(Path.GetTempPath(), "ucr-empty-inventory-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporary);

            try
            {
                Directory.SetCurrentDirectory(temporary);
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
            finally
            {
                Directory.SetCurrentDirectory(original);
                Directory.Delete(temporary, true);
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
