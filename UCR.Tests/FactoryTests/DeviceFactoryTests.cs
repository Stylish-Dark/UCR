using System;
using System.Collections.Generic;
using HidWizards.UCR.Core.Models;
using HidWizards.UCR.Core.Models.Binding;
using HidWizards.UCR.Tests.Factory;
using NUnit.Framework;

namespace HidWizards.UCR.Tests.FactoryTests
{
    [TestFixture]
    class DeviceFactoryTests
    {
        [Test]
        public void CreateDevice()
        {
            var title = "Test device";
            var providerName = "Test provider";
            var deviceNumber = 0;
            var device = DeviceFactory.CreateDevice(title, providerName, deviceNumber.ToString(), deviceNumber);
            Assert.That(device.Title, Is.EqualTo(title));
            Assert.That(device.ProviderName, Is.EqualTo(providerName));
            Assert.That(device.DeviceHandle, Is.EqualTo(deviceNumber.ToString()));
        }

        [Test]
        public void CreateDeviceList()
        {
            var title = "Test device";
            var providerName = "Test provider";
            var deviceList = DeviceFactory.CreateDeviceList(title, providerName, 4);
            Assert.That(deviceList.Count, Is.EqualTo(4));
            Assert.That(deviceList[0].Title, Is.Not.EqualTo(deviceList[1].Title));
            Assert.That(deviceList[0].ProviderName, Is.EqualTo(deviceList[1].ProviderName));
            Assert.That(deviceList[0].DeviceHandle, Is.EqualTo(0.ToString()));
            Assert.That(deviceList[3].DeviceHandle, Is.EqualTo(3.ToString()));
        }

        [Test]
        public void CompatibleBindingTransfersBetweenSameProviderAndSchema()
        {
            var source = CreateCachedDevice("Keyboard A", "Core_Interception", CreateKeyboardLikeMenu());
            var target = CreateCachedDevice("Keyboard B", "Core_Interception", CreateKeyboardLikeMenu());
            var binding = CreateBoundBinding(1, 45, 0);

            var result = DeviceBindingCompatibility.Evaluate(source, target, null, DeviceIoType.Input, binding, DeviceBindingCategory.Momentary);

            Assert.That(result, Is.EqualTo(DeviceBindingTransferCompatibility.Compatible));
        }

        [Test]
        public void BindingDoesNotTransferAcrossProviders()
        {
            var source = CreateCachedDevice("Keyboard", "Core_Interception", CreateKeyboardLikeMenu());
            var target = CreateCachedDevice("Controller", "OtherProvider", CreateKeyboardLikeMenu());
            var binding = CreateBoundBinding(1, 45, 0);

            var result = DeviceBindingCompatibility.Evaluate(source, target, null, DeviceIoType.Input, binding, DeviceBindingCategory.Momentary);

            Assert.That(result, Is.EqualTo(DeviceBindingTransferCompatibility.Incompatible));
        }

        [Test]
        public void BindingDoesNotTransferAcrossDifferentSchemasOnSameProvider()
        {
            var source = CreateCachedDevice("Keyboard", "Core_Interception", CreateKeyboardLikeMenu());
            var target = CreateCachedDevice("Mouse", "Core_Interception", CreateMouseLikeMenu());
            var binding = CreateBoundBinding(1, 45, 0);

            var result = DeviceBindingCompatibility.Evaluate(source, target, null, DeviceIoType.Input, binding, DeviceBindingCategory.Momentary);

            Assert.That(result, Is.EqualTo(DeviceBindingTransferCompatibility.Incompatible));
        }

        [Test]
        public void MissingDeviceSchemaIsNonDestructiveUnknown()
        {
            var source = CreateCachedDevice("Keyboard A", "Core_Interception", CreateKeyboardLikeMenu());
            var target = CreateCachedDevice("Keyboard B", "Core_Interception", new List<DeviceBindingNode>
            {
                new DeviceBindingNode { Title = "Device not connected" }
            });
            var binding = CreateBoundBinding(1, 45, 0);

            var result = DeviceBindingCompatibility.Evaluate(source, target, null, DeviceIoType.Input, binding, DeviceBindingCategory.Momentary);

            Assert.That(result, Is.EqualTo(DeviceBindingTransferCompatibility.Unknown));
        }

        private static DeviceBinding CreateBoundBinding(int keyType, int keyValue, int keySubValue)
        {
            return new DeviceBinding
            {
                IsBound = true,
                KeyType = keyType,
                KeyValue = keyValue,
                KeySubValue = keySubValue
            };
        }

        private static Device CreateCachedDevice(string title, string providerName, List<DeviceBindingNode> menu)
        {
            return new Device(new DeviceCache
            {
                Title = title,
                ProviderName = providerName,
                DeviceHandle = title,
                DeviceNumber = 0,
                DeviceBindingMenu = menu
            });
        }

        private static List<DeviceBindingNode> CreateKeyboardLikeMenu()
        {
            return new List<DeviceBindingNode>
            {
                new DeviceBindingNode
                {
                    Title = "Keys",
                    ChildrenNodes = new List<DeviceBindingNode>
                    {
                        CreateBindingNode("X", 1, 45, 0, DeviceBindingCategory.Momentary),
                        CreateBindingNode("Y", 1, 21, 0, DeviceBindingCategory.Momentary)
                    }
                }
            };
        }

        private static List<DeviceBindingNode> CreateMouseLikeMenu()
        {
            return new List<DeviceBindingNode>
            {
                new DeviceBindingNode
                {
                    Title = "Buttons",
                    ChildrenNodes = new List<DeviceBindingNode>
                    {
                        CreateBindingNode("Left", 1, 0, 0, DeviceBindingCategory.Momentary),
                        CreateBindingNode("Right", 1, 1, 0, DeviceBindingCategory.Momentary)
                    }
                }
            };
        }

        private static DeviceBindingNode CreateBindingNode(string title, int keyType, int keyValue, int keySubValue, DeviceBindingCategory category)
        {
            return new DeviceBindingNode
            {
                Title = title,
                DeviceBindingInfo = new DeviceBindingInfo
                {
                    KeyType = keyType,
                    KeyValue = keyValue,
                    KeySubValue = keySubValue,
                    DeviceBindingCategory = category
                }
            };
        }
    }
}
