using System;
using System.IO;
using HidWizards.UCR.Core;
using HidWizards.UCR.Core.Managers;
using HidWizards.UCR.Core.Models;
using HidWizards.UCR.Core.Persistence;
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
                var context = CreateIsolatedContext(temporary);
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
            var providerDirectory = Path.Combine(temporary, "Data", "Cache", "Core_Interception");
            Directory.CreateDirectory(providerDirectory);

            try
            {
                Directory.SetCurrentDirectory(temporary);
                File.WriteAllText(Path.Combine(providerDirectory, "keyboard.json"),
                    "{\"Title\":\"K: Cached Keyboard\",\"ProviderName\":\"Core_Interception\",\"DeviceHandle\":\"Keyboard\\\\Cached\",\"DeviceNumber\":0,\"HidPath\":\"HID\\\\VID_CAFE&PID_BEEF\",\"DeviceBindingMenu\":[]}");

                var context = CreateIsolatedContext(temporary);
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
            var providerDirectory = Path.Combine(temporary, "Data", "Cache", "Core_Interception");
            Directory.CreateDirectory(providerDirectory);

            try
            {
                Directory.SetCurrentDirectory(temporary);
                File.WriteAllText(Path.Combine(providerDirectory, "keyboard.json"),
                    "{\"Title\":\"K: Cached Keyboard\",\"ProviderName\":\"Core_Interception\",\"DeviceHandle\":\"Keyboard\\\\Cached\",\"DeviceNumber\":0,\"HidPath\":\"HID\\\\VID_CAFE&PID_BEEF\",\"DeviceBindingMenu\":[]}");

                var context = CreateIsolatedContext(temporary);
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
        public void DeviceManagerRefreshPreservesPendingFriendlyNameWithoutApplyingIt()
        {
            var original = Environment.CurrentDirectory;
            var temporary = Path.Combine(Path.GetTempPath(), "ucr-device-pending-alias-test-" + Guid.NewGuid().ToString("N"));
            var providerDirectory = Path.Combine(temporary, "Data", "Cache", "Core_Interception");
            Directory.CreateDirectory(providerDirectory);

            try
            {
                Directory.SetCurrentDirectory(temporary);
                File.WriteAllText(Path.Combine(providerDirectory, "keyboard.json"),
                    "{\"Title\":\"K: Cached Keyboard\",\"ProviderName\":\"Core_Interception\",\"DeviceHandle\":\"Keyboard\\\\Cached\",\"DeviceNumber\":0,\"HidPath\":\"HID\\\\VID_CAFE&PID_BEEF\",\"DeviceBindingMenu\":[]}");

                var context = CreateIsolatedContext(temporary);
                context.IOController?.Dispose();
                context.IOController = null;

                using (var viewModel = new DeviceManagerViewModel(context.DevicesManager))
                {
                    Assert.That(viewModel.Devices.Count, Is.EqualTo(1));
                    viewModel.Devices[0].Alias = "Typed but not saved";
                    viewModel.Devices[0].TextColor = DeviceOutlineColor.Cyan;

                    viewModel.Refresh();

                    Assert.That(viewModel.Devices.Count, Is.EqualTo(1));
                    Assert.That(viewModel.Devices[0].Alias, Is.EqualTo("Typed but not saved"),
                        "Refreshing/re-detecting device inventory must not discard friendly names still being edited in the Devices page.");
                    Assert.That(viewModel.Devices[0].TextColor, Is.EqualTo(DeviceOutlineColor.Cyan),
                        "Pending presentation edits should survive the same inventory reconciliation.");
                    Assert.That(context.DeviceAliases, Is.Empty,
                        "Preserving an in-progress editor value must not silently turn Refresh/Detect into Save.");

                    string applyError;
                    Assert.That(viewModel.Apply(out applyError), Is.True, applyError);
                    Assert.That(context.DeviceAliases.Count, Is.EqualTo(1));
                    Assert.That(context.DeviceAliases[0].Alias, Is.EqualTo("Typed but not saved"),
                        "The preserved editor value must still save normally when the user explicitly applies it.");
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
        public void ProviderRefreshKeepsHealthyProvidersWhenOneProviderThrows()
        {
            var healthyRefreshes = 0;
            var errors = new System.Collections.Generic.List<string>();
            var refreshes = new[]
            {
                new System.Collections.Generic.KeyValuePair<string, Action>(
                    "Broken", () => { throw new InvalidOperationException("provider refresh failure"); }),
                new System.Collections.Generic.KeyValuePair<string, Action>(
                    "Healthy", () => healthyRefreshes++)
            };

            DevicesManager.RefreshProviders(refreshes,
                (providerName, exception) => errors.Add(providerName + ":" + exception.Message));

            Assert.That(healthyRefreshes, Is.EqualTo(1),
                "A broken provider must not prevent healthy providers from refreshing after USB hotplug.");
            Assert.That(errors.Count, Is.EqualTo(1));
            Assert.That(errors[0], Does.StartWith("Broken:"));
        }

        [Test]
        public void CrashReportAlsoWritesStableLastCrashFile()
        {
            Logger.InitializeSession();

            var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
            var logDirectoryField = typeof(Logger).GetField("_logDirectory", flags);
            var crashFlagField = typeof(Logger).GetField("_crashReportWritten", flags);
            var lastCrashField = typeof(Logger).GetField("<LastCrashReportPath>k__BackingField", flags);
            Assert.That(logDirectoryField, Is.Not.Null);
            Assert.That(crashFlagField, Is.Not.Null);

            var previousDirectory = (string)logDirectoryField.GetValue(null);
            var previousCrashFlag = (int)crashFlagField.GetValue(null);
            var previousLastCrash = lastCrashField?.GetValue(null);
            var temporary = Path.Combine(Path.GetTempPath(), "ucr-crash-log-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporary);

            try
            {
                logDirectoryField.SetValue(null, temporary);
                crashFlagField.SetValue(null, 0);

                var path = Logger.WriteCrashReport("regression-test",
                    new InvalidOperationException("simulated crash diagnostics"));

                Assert.That(path, Is.Not.Null.And.Not.Empty);
                Assert.That(File.Exists(path), Is.True);

                var stablePath = Path.Combine(temporary, "LAST-CRASH.txt");
                Assert.That(File.Exists(stablePath), Is.True,
                    "There must always be one predictable crash-report filename.");
                Assert.That(File.ReadAllText(stablePath), Does.Contain("simulated crash diagnostics"));
            }
            finally
            {
                logDirectoryField.SetValue(null, previousDirectory);
                crashFlagField.SetValue(null, previousCrashFlag);
                lastCrashField?.SetValue(null, previousLastCrash);
                if (Directory.Exists(temporary)) Directory.Delete(temporary, true);
            }
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
                var context = CreateIsolatedContext(temporary);
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

        private static Context CreateIsolatedContext(string temporaryRoot)
        {
            return new Context(new ContextStore(Path.Combine(temporaryRoot, "Data"), null));
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
