using System;
using System.Collections.Generic;
using System.IO;
using HidWizards.IOWrapper.DataTransferObjects;
using HidWizards.UCR.Core.Managers;
using HidWizards.UCR.Core.Models;
using HidWizards.UCR.Core.Models.Binding;
using NUnit.Framework;

namespace HidWizards.UCR.Tests.ModelTests
{
    [TestFixture]
    public class DeviceCacheStoreTests
    {
        private string _cacheRoot;

        [SetUp]
        public void SetUp()
        {
            _cacheRoot = Path.Combine(Path.GetTempPath(), "UCR-DeviceCacheStoreTests-" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_cacheRoot)) Directory.Delete(_cacheRoot, true);
        }

        [Test]
        public void SaveAndLoadProviderRoundTripsPersistedIdentity()
        {
            var store = new DeviceCacheStore(_cacheRoot);
            var live = CreateLiveDevice("DirectInput Pad", "SharpDX_DirectInput", "VID_1234&PID_5678", 3,
                @"\\?\hid#vid_1234&pid_5678#physical-a");

            Assert.That(store.Save(live, new List<DeviceBindingNode>()), Is.True);
            store.ClearMemoryCache();

            var loaded = store.LoadProvider("SharpDX_DirectInput");
            Assert.That(loaded.Count, Is.EqualTo(1));
            Assert.That(DeviceIdentity.PersistedEquals(loaded[0], live), Is.True);
            Assert.That(loaded[0].IsCache, Is.True);
        }

        [Test]
        public void InvalidCacheFileIsIgnoredAndRemoved()
        {
            var providerDirectory = Path.Combine(_cacheRoot, "Core_Interception");
            Directory.CreateDirectory(providerDirectory);
            var invalidPath = Path.Combine(providerDirectory, "invalid.json");
            File.WriteAllText(invalidPath, string.Empty);
            var store = new DeviceCacheStore(_cacheRoot);

            Assert.That(store.LoadProvider("Core_Interception"), Is.Empty);
            Assert.That(File.Exists(invalidPath), Is.False);
        }

        [Test]
        public void RemoveOverlappingDeletesCachedRecordRepresentedByLiveEndpoint()
        {
            var store = new DeviceCacheStore(_cacheRoot);
            var live = CreateLiveDevice("Keyboard", "Core_Interception", @"Keyboard\VID_1111&PID_2222", 4,
                @"\\?\hid#vid_1111&pid_2222#physical-a");
            Assert.That(store.Save(live, new List<DeviceBindingNode>()), Is.True);
            store.ClearMemoryCache();
            Assert.That(store.LoadProvider("Core_Interception").Count, Is.EqualTo(1));

            Assert.That(store.RemoveOverlapping(new[] { live }), Is.EqualTo(1));
            store.ClearMemoryCache();
            Assert.That(store.LoadProvider("Core_Interception"), Is.Empty);
        }

        private static Device CreateLiveDevice(string title, string providerName, string deviceHandle,
            int deviceNumber, string hidPath)
        {
            return new Device(
                new DeviceReport
                {
                    DeviceName = title,
                    HidPath = hidPath,
                    DeviceDescriptor = new DeviceDescriptor
                    {
                        DeviceHandle = deviceHandle,
                        DeviceInstance = deviceNumber
                    }
                },
                new ProviderReport
                {
                    ProviderDescriptor = new ProviderDescriptor { ProviderName = providerName }
                },
                new List<DeviceBindingNode>());
        }
    }
}
