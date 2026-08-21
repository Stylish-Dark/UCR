using System;
using System.Collections.Generic;
using HidWizards.IOWrapper.DataTransferObjects;
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

        [Test]
        public void ViGEmCommonButtonsTransferXboxToDs4AndBack()
        {
            var xbox = CreateCachedDevice("ViGEm Xbox 360 Controller 1", "Core_ViGEm", "xb360", CreateViGEmMenu(false));
            var ds4 = CreateCachedDevice("ViGEm DS4 Controller 1", "Core_ViGEm", "ds4", CreateViGEmMenu(true));

            for (var buttonIndex = 0; buttonIndex <= 9; buttonIndex++)
            {
                var binding = CreateBoundBinding((int)BindingType.Button, buttonIndex, 0);

                var xboxToDs4 = DeviceBindingCompatibility.EvaluateTransfer(
                    xbox, ds4, null, DeviceIoType.Output, binding, DeviceBindingCategory.Momentary);
                Assert.That(xboxToDs4.Compatibility, Is.EqualTo(DeviceBindingTransferCompatibility.Compatible),
                    "Xbox button index " + buttonIndex + " should transfer to its DS4 semantic equivalent.");
                Assert.That(xboxToDs4.KeyType, Is.EqualTo((int)BindingType.Button));
                Assert.That(xboxToDs4.KeyValue, Is.EqualTo(buttonIndex));

                var ds4ToXbox = DeviceBindingCompatibility.EvaluateTransfer(
                    ds4, xbox, null, DeviceIoType.Output, binding, DeviceBindingCategory.Momentary);
                Assert.That(ds4ToXbox.Compatibility, Is.EqualTo(DeviceBindingTransferCompatibility.Compatible),
                    "DS4 button index " + buttonIndex + " should transfer to its Xbox semantic equivalent.");
                Assert.That(ds4ToXbox.KeyType, Is.EqualTo((int)BindingType.Button));
                Assert.That(ds4ToXbox.KeyValue, Is.EqualTo(buttonIndex));
            }
        }

        [Test]
        public void ViGEmAxesAndDpadTransferAcrossXboxAndDs4()
        {
            var xbox = CreateCachedDevice("ViGEm Xbox 360 Controller 1", "Core_ViGEm", "xb360", CreateViGEmMenu(false));
            var ds4 = CreateCachedDevice("ViGEm DS4 Controller 1", "Core_ViGEm", "ds4", CreateViGEmMenu(true));

            for (var axisIndex = 0; axisIndex <= 5; axisIndex++)
            {
                var binding = CreateBoundBinding((int)BindingType.Axis, axisIndex, 0);
                var result = DeviceBindingCompatibility.EvaluateTransfer(
                    xbox, ds4, null, DeviceIoType.Output, binding, DeviceBindingCategory.Range);

                Assert.That(result.Compatibility, Is.EqualTo(DeviceBindingTransferCompatibility.Compatible),
                    "Axis index " + axisIndex + " should retain its semantic position.");
                Assert.That(result.KeyValue, Is.EqualTo(axisIndex));
            }

            for (var povIndex = 0; povIndex <= 3; povIndex++)
            {
                var binding = CreateBoundBinding((int)BindingType.POV, povIndex, 0);
                var result = DeviceBindingCompatibility.EvaluateTransfer(
                    ds4, xbox, null, DeviceIoType.Output, binding, DeviceBindingCategory.Momentary);

                Assert.That(result.Compatibility, Is.EqualTo(DeviceBindingTransferCompatibility.Compatible),
                    "DPad index " + povIndex + " should retain its direction.");
                Assert.That(result.KeyValue, Is.EqualTo(povIndex));
            }
        }

        [Test]
        public void ViGEmDs4OnlyButtonsDoNotGetCoercedIntoXboxControls()
        {
            var xbox = CreateCachedDevice("ViGEm Xbox 360 Controller 1", "Core_ViGEm", "xb360", CreateViGEmMenu(false));
            var ds4 = CreateCachedDevice("ViGEm DS4 Controller 1", "Core_ViGEm", "ds4", CreateViGEmMenu(true));

            for (var ds4OnlyIndex = 10; ds4OnlyIndex <= 13; ds4OnlyIndex++)
            {
                var binding = CreateBoundBinding((int)BindingType.Button, ds4OnlyIndex, 0);
                var result = DeviceBindingCompatibility.EvaluateTransfer(
                    ds4, xbox, null, DeviceIoType.Output, binding, DeviceBindingCategory.Momentary);

                Assert.That(result.Compatibility, Is.EqualTo(DeviceBindingTransferCompatibility.Incompatible),
                    "DS4-only button index " + ds4OnlyIndex + " must require explicit rebinding.");
            }
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
            return CreateCachedDevice(title, providerName, title, menu);
        }

        private static Device CreateCachedDevice(string title, string providerName, string deviceHandle, List<DeviceBindingNode> menu)
        {
            return new Device(new DeviceCache
            {
                Title = title,
                ProviderName = providerName,
                DeviceHandle = deviceHandle,
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

        private static List<DeviceBindingNode> CreateViGEmMenu(bool ds4)
        {
            var axes = new DeviceBindingNode
            {
                Title = "Axes",
                ChildrenNodes = new List<DeviceBindingNode>()
            };
            for (var i = 0; i <= 5; i++)
            {
                axes.ChildrenNodes.Add(CreateBindingNode(
                    "Axis " + i,
                    (int)BindingType.Axis,
                    i,
                    0,
                    DeviceBindingCategory.Range));
            }

            var buttons = new DeviceBindingNode
            {
                Title = "Buttons",
                ChildrenNodes = new List<DeviceBindingNode>()
            };
            var maxButton = ds4 ? 13 : 9;
            for (var i = 0; i <= maxButton; i++)
            {
                buttons.ChildrenNodes.Add(CreateBindingNode(
                    "Button " + i,
                    (int)BindingType.Button,
                    i,
                    0,
                    DeviceBindingCategory.Momentary));
            }

            var dpad = new DeviceBindingNode
            {
                Title = "DPad",
                ChildrenNodes = new List<DeviceBindingNode>()
            };
            for (var i = 0; i <= 3; i++)
            {
                dpad.ChildrenNodes.Add(CreateBindingNode(
                    "DPad " + i,
                    (int)BindingType.POV,
                    i,
                    0,
                    DeviceBindingCategory.Momentary));
            }

            return new List<DeviceBindingNode> { axes, buttons, dpad };
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
