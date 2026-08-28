using System;
using System.Collections.Generic;
using HidWizards.IOWrapper.DataTransferObjects;
using HidWizards.UCR.Core.Managers;
using HidWizards.UCR.Core.Models;
using HidWizards.UCR.Core.Models.Binding;
using HidWizards.UCR.Tests.Factory;
using HidWizards.UCR.ViewModels.Dashboard;
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
        public void DevicePersistsHidPathReportedByProvider()
        {
            var report = new DeviceReport
            {
                DeviceName = "DirectInput Pad",
                HidPath = @"\\?\hid#vid_1234&pid_5678#physical-a",
                DeviceDescriptor = new DeviceDescriptor
                {
                    DeviceHandle = "VID_1234&PID_5678",
                    DeviceInstance = 0
                }
            };
            var provider = new ProviderReport
            {
                ProviderDescriptor = new ProviderDescriptor { ProviderName = "SharpDX_DirectInput" }
            };

            var device = new Device(report, provider, new List<DeviceBindingNode>());

            Assert.That(device.HidPath, Is.EqualTo(report.HidPath));
        }

        [Test]
        public void StableResolverPrefersHidPathWhenProviderInstanceNumbersSwap()
        {
            var configured = CreateIdentityDevice("Pad A", "SharpDX_DirectInput", "VID_1234&PID_5678", 0,
                @"\\?\hid#vid_1234&pid_5678#physical-a");
            var liveWrongAtOldNumber = CreateIdentityDevice("Pad B", "SharpDX_DirectInput", "VID_1234&PID_5678", 0,
                @"\\?\hid#vid_1234&pid_5678#physical-b");
            var liveCorrectAtNewNumber = CreateIdentityDevice("Pad A", "SharpDX_DirectInput", "VID_1234&PID_5678", 1,
                @"\\?\hid#vid_1234&pid_5678#physical-a");

            var resolved = DevicesManager.ResolveDevice(configured,
                new List<Device> { liveWrongAtOldNumber, liveCorrectAtNewNumber });

            Assert.That(resolved, Is.SameAs(liveCorrectAtNewNumber));
            Assert.That(resolved.DeviceNumber, Is.EqualTo(1));
        }

        [Test]
        public void StableResolverUsesUniqueHardwareHandleWhenInstanceNumberChanges()
        {
            var configured = CreateIdentityDevice("Keyboard", "Core_Interception",
                @"Keyboard\HID\VID_1111&PID_2222", 3, null);
            var live = CreateIdentityDevice("Keyboard", "Core_Interception",
                @"Keyboard\HID\VID_1111&PID_2222", 0, null);

            var resolved = DevicesManager.ResolveDevice(configured, new List<Device> { live });

            Assert.That(resolved, Is.SameAs(live));
        }

        [Test]
        public void StableResolverDoesNotSubstituteDifferentPhysicalHidPathEvenWhenOnlyOneMatchExists()
        {
            var configured = CreateIdentityDevice("Pad", "SharpDX_DirectInput", "VID_1234&PID_5678", 0,
                @"\\?\hid#vid_1234&pid_5678#physical-a");
            var differentPhysicalDevice = CreateIdentityDevice("Pad", "SharpDX_DirectInput", "VID_1234&PID_5678", 0,
                @"\\?\hid#vid_1234&pid_5678#physical-b");

            var resolved = DevicesManager.ResolveDevice(configured, new List<Device> { differentPhysicalDevice });

            Assert.That(resolved, Is.Null);
        }

        [Test]
        public void StableResolverDoesNotGuessWhenPersistedHidPathIsMissingAmongDuplicates()
        {
            var configured = CreateIdentityDevice("Pad", "SharpDX_DirectInput", "VID_1234&PID_5678", 0,
                @"\\?\hid#vid_1234&pid_5678#old-port");
            var first = CreateIdentityDevice("Pad 1", "SharpDX_DirectInput", "VID_1234&PID_5678", 0,
                @"\\?\hid#vid_1234&pid_5678#new-a");
            var second = CreateIdentityDevice("Pad 2", "SharpDX_DirectInput", "VID_1234&PID_5678", 1,
                @"\\?\hid#vid_1234&pid_5678#new-b");

            var resolved = DevicesManager.ResolveDevice(configured, new List<Device> { first, second });

            Assert.That(resolved, Is.Null);
        }

        [Test]
        public void StableResolverRefusesEnumerationOrderForDuplicatePhysicalDevicesWithoutHardwarePath()
        {
            var configured = CreateIdentityDevice("Keyboard #2", "Core_Interception",
                @"Keyboard\HID\VID_1111&PID_2222", 1, null);
            var first = CreateIdentityDevice("Keyboard #1", "Core_Interception",
                @"Keyboard\HID\VID_1111&PID_2222", 0, null);
            var second = CreateIdentityDevice("Keyboard #2", "Core_Interception",
                @"Keyboard\HID\VID_1111&PID_2222", 1, null);

            var resolved = DevicesManager.ResolveDevice(configured, new List<Device> { first, second });

            Assert.That(resolved, Is.Null);
        }

        [Test]
        public void StableResolverRetainsLegacyExactSlotFallbackWhenNoHardwarePathExists()
        {
            var configured = CreateIdentityDevice("Xbox Controller 2", "SharpDX_XInput", "xb360", 1, null);
            var first = CreateIdentityDevice("Xbox Controller 1", "SharpDX_XInput", "xb360", 0, null);
            var second = CreateIdentityDevice("Xbox Controller 2", "SharpDX_XInput", "xb360", 1, null);

            var resolved = DevicesManager.ResolveDevice(configured, new List<Device> { first, second });

            Assert.That(resolved, Is.SameAs(second));
        }

        [Test]
        public void AliasIdentityPrefersPersistedHidPath()
        {
            var device = CreateIdentityDevice("Pad", "SharpDX_DirectInput", "VID_1234&PID_5678", 3,
                @"\\?\hid#vid_1234&pid_5678#physical-a");

            var identity = DevicesManager.BuildAliasIdentity(device);

            Assert.That(identity.IdentityKind, Is.EqualTo(DeviceAliasIdentityKind.HidPath));
            Assert.That(identity.IdentityValue, Is.EqualTo(device.HidPath));
            Assert.That(identity.DeviceNumber, Is.EqualTo(0));
        }

        [Test]
        public void AliasIdentityUsesLogicalSlotForXInputAndViGEm()
        {
            var xinput = CreateIdentityDevice("Xbox Controller 2", "SharpDX_XInput", "xb360", 1, null);
            var vigem = CreateIdentityDevice("ViGEm DS4 Controller 4", "Core_ViGEm", "ds4", 3, null);

            var xinputIdentity = DevicesManager.BuildAliasIdentity(xinput);
            var vigemIdentity = DevicesManager.BuildAliasIdentity(vigem);

            Assert.That(xinputIdentity.IdentityKind, Is.EqualTo(DeviceAliasIdentityKind.LogicalSlot));
            Assert.That(xinputIdentity.IdentityValue, Is.EqualTo("xb360"));
            Assert.That(xinputIdentity.DeviceNumber, Is.EqualTo(1));
            Assert.That(vigemIdentity.IdentityKind, Is.EqualTo(DeviceAliasIdentityKind.LogicalSlot));
            Assert.That(vigemIdentity.IdentityValue, Is.EqualTo("ds4"));
            Assert.That(vigemIdentity.DeviceNumber, Is.EqualTo(3));
        }

        [Test]
        public void AliasIdentityUsesHardwareHandleWithoutStrongerPhysicalIdentity()
        {
            var device = CreateIdentityDevice("Keyboard", "Core_Interception",
                @"Keyboard\HID\VID_1111&PID_2222", 7, null);

            var identity = DevicesManager.BuildAliasIdentity(device);

            Assert.That(identity.IdentityKind, Is.EqualTo(DeviceAliasIdentityKind.HardwareHandle));
            Assert.That(identity.IdentityValue, Is.EqualTo(device.DeviceHandle));
            Assert.That(identity.DeviceNumber, Is.EqualTo(0),
                "Enumeration order must not become part of a physical-device alias identity.");
        }

        [Test]
        public void AliasIdentityComparisonIsCaseInsensitiveButSlotSensitive()
        {
            var first = new DeviceAlias
            {
                ProviderName = "Core_ViGEm",
                IdentityKind = DeviceAliasIdentityKind.LogicalSlot,
                IdentityValue = "DS4",
                DeviceNumber = 1,
                Alias = "Player Two"
            };
            var same = new DeviceAlias
            {
                ProviderName = "core_vigem",
                IdentityKind = DeviceAliasIdentityKind.LogicalSlot,
                IdentityValue = "ds4",
                DeviceNumber = 1,
                Alias = "Other text does not affect identity"
            };
            var differentSlot = same.Clone();
            differentSlot.DeviceNumber = 2;

            Assert.That(DevicesManager.AliasIdentityEquals(first, same), Is.True);
            Assert.That(DevicesManager.AliasIdentityEquals(first, differentSlot), Is.False);
        }

        [Test]
        public void DeviceAliasClonePreservesPresentationPreferences()
        {
            var source = new DeviceAlias
            {
                ProviderName = "SharpDX_XInput",
                IdentityKind = DeviceAliasIdentityKind.LogicalSlot,
                IdentityValue = "xb360",
                DeviceNumber = 2,
                Alias = "Player Three",
                Hidden = true,
                SortOrder = 7
            };

            var clone = source.Clone();

            Assert.That(clone, Is.Not.SameAs(source));
            Assert.That(clone.Alias, Is.EqualTo("Player Three"));
            Assert.That(clone.Hidden, Is.True);
            Assert.That(clone.SortOrder, Is.EqualTo(7));
            Assert.That(clone.HasPresentationSettings, Is.True);
        }

        [Test]
        public void HardwareHandlePresentationRequiresUniquePhysicalDevice()
        {
            var device = CreateIdentityDevice("Keyboard", "Core_Interception",
                @"Keyboard\VID_1111&PID_2222", 0, null);
            var duplicate = CreateIdentityDevice("Keyboard #2", "Core_Interception",
                @"Keyboard\VID_1111&PID_2222", 1, null);
            var manager = new DevicesManager(new HidWizards.UCR.Core.Context());

            Assert.That(manager.CanPersistDeviceAlias(device, new[] { device }), Is.True);
            Assert.That(manager.CanPersistDeviceAlias(device, new[] { device, duplicate }), Is.False);
        }


        [Test]
        public void CacheCopyWithSamePhysicalHandleIsRecognizedAcrossProviderSlotChanges()
        {
            var cached = CreateIdentityDevice("Cached Keyboard", "Core_Interception",
                @"Keyboard\VID_1111&PID_2222", 1, null);
            var live = CreateIdentityDevice("Live Keyboard", "Core_Interception",
                @"Keyboard\VID_1111&PID_2222", 6, null);

            Assert.That(DevicesManager.CacheRepresentsLiveEndpoint(cached, live), Is.True,
                "A changing provider slot must not create another cached copy of the same physical endpoint.");
        }

        [Test]
        public void WindowsDeviceInstanceIdCanBeDerivedFromHidInterfacePath()
        {
            var device = CreateIdentityDevice("Keyboard", "Core_Interception",
                @"Keyboard\VID_1111&PID_2222", 0,
                @"\\?\HID#VID_046D&PID_C52B&MI_00#7&2ABCDEF&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}");

            string instanceId;
            Assert.That(DevicesManager.TryGetWindowsDeviceInstanceId(device, out instanceId), Is.True);
            Assert.That(instanceId, Is.EqualTo(@"HID\VID_046D&PID_C52B&MI_00\7&2ABCDEF&0&0000"));
        }

        [Test]
        public void WindowsDeviceInstanceIdIsUnavailableWithoutADeviceInterfacePath()
        {
            var device = CreateIdentityDevice("Keyboard", "Core_Interception",
                @"Keyboard\VID_1111&PID_2222", 0, null);

            string instanceId;
            Assert.That(DevicesManager.TryGetWindowsDeviceInstanceId(device, out instanceId), Is.False);
            Assert.That(instanceId, Is.Null);
        }

        [Test]
        public void SessionOnlyDeviceManagerEntryRemainsSelectable()
        {
            var device = CreateLiveIdentityDevice("Ambiguous Keyboard", "Core_Interception",
                @"Keyboard\VID_1111&PID_2222", 1, null);

            Assert.That(device.IsCache, Is.False,
                "Regression fixture must be a live device; cached devices intentionally use the cached/disconnected presentation path.");

            var item = new DeviceManagerItemViewModel(device, DeviceIoType.Input,
                false, null, false, "ephemeral");

            Assert.That(item.IsCachedOnly, Is.False);
            Assert.That(item.CanPersist, Is.False);
            Assert.That(item.CanDismiss, Is.True);
            Assert.That(item.CanForget, Is.False);
            Assert.That(item.Hidden, Is.False,
                "A live device with session-only identity must remain usable even when UCR cannot safely persist alias/hide/order metadata for it.");
            Assert.That(item.IdentityNote, Does.Contain("Session identity only"));
        }

        [Test]
        public void BlockableLookupDoesNotDestroySharedBindingMenu()
        {
            var menu = CreateMouseLikeMenu();
            menu[0].ChildrenNodes[0].DeviceBindingInfo.Blockable = true;
            var topCount = menu.Count;
            var childCount = menu[0].ChildrenNodes.Count;

            var blockable = DeviceBinding.IsBlockableInMenu(menu, 1, 0, 0);

            Assert.That(blockable, Is.True);
            Assert.That(menu.Count, Is.EqualTo(topCount));
            Assert.That(menu[0].ChildrenNodes.Count, Is.EqualTo(childCount));
            Assert.That(menu[0].ChildrenNodes[0].Title, Is.EqualTo("Left"));
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

        private static Device CreateLiveIdentityDevice(string title, string providerName, string deviceHandle,
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

        private static Device CreateIdentityDevice(string title, string providerName, string deviceHandle,
            int deviceNumber, string hidPath)
        {
            return new Device(new DeviceCache
            {
                Title = title,
                ProviderName = providerName,
                DeviceHandle = deviceHandle,
                DeviceNumber = deviceNumber,
                HidPath = hidPath,
                DeviceBindingMenu = new List<DeviceBindingNode>()
            });
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
