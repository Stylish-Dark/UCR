using HidWizards.UCR.Core.Models;
using HidWizards.UCR.ViewModels.Presentation;
using NUnit.Framework;

namespace HidWizards.UCR.Tests.UiTests
{
    [TestFixture]
    internal class DeviceVisualCatalogTests
    {
        [TestCase("PSX Controller Adapter", "SharpDX_DirectInput", "hid", DeviceVisualKind.PlayStation1)]
        [TestCase("DualShock 2 USB Adapter", "SharpDX_DirectInput", "hid", DeviceVisualKind.PlayStation2)]
        [TestCase("PLAYSTATION(R)3 Controller", "SharpDX_DirectInput", "hid", DeviceVisualKind.PlayStation3)]
        [TestCase("DUALSHOCK 4 Controller", "SharpDX_DirectInput", "hid", DeviceVisualKind.PlayStation4)]
        [TestCase("Sony DualSense Wireless Controller", "SharpDX_DirectInput", "hid", DeviceVisualKind.PlayStation5)]
        [TestCase("Original Xbox Duke Controller", "SharpDX_DirectInput", "hid", DeviceVisualKind.XboxOriginal)]
        [TestCase("Xbox 360 Controller", "SharpDX_XInput", "xinput", DeviceVisualKind.Xbox360)]
        [TestCase("Xbox One Controller", "SharpDX_XInput", "xinput", DeviceVisualKind.XboxOne)]
        [TestCase("Xbox Series Controller", "SharpDX_XInput", "xinput", DeviceVisualKind.XboxSeries)]
        [TestCase("Nintendo 64 Controller", "SharpDX_DirectInput", "hid", DeviceVisualKind.Nintendo64)]
        [TestCase("GameCube Controller", "SharpDX_DirectInput", "hid", DeviceVisualKind.GameCube)]
        [TestCase("Nintendo Wii Remote", "SharpDX_DirectInput", "hid", DeviceVisualKind.WiiRemote)]
        [TestCase("Wii Classic Controller", "SharpDX_DirectInput", "hid", DeviceVisualKind.WiiClassic)]
        [TestCase("Nintendo Switch Pro Controller", "SharpDX_DirectInput", "hid", DeviceVisualKind.SwitchPro)]
        [TestCase("Nintendo Joy-Con Pair", "SharpDX_DirectInput", "hid", DeviceVisualKind.SwitchJoyCon)]
        [TestCase("vJoy Device", "Core_vJoy", "vjoy", DeviceVisualKind.VJoy)]
        public void CatalogClassifiesDeviceFamilies(
            string title, string provider, string handle, DeviceVisualKind expected)
        {
            var visual = DeviceVisualCatalog.Describe(
                new Device(title, provider, handle, 0), DeviceIoType.Input);

            Assert.That(visual.Kind, Is.EqualTo(expected));
        }

        [TestCase("Laptop Keyboard", "Core_Interception", "kbd", DeviceIoType.Input, "\uE001")]
        [TestCase("Xbox 360 Controller", "Core_ViGEm", "xb360", DeviceIoType.Output, "\uE002")]
        [TestCase("DualShock 4 Controller", "Core_ViGEm", "ds4", DeviceIoType.Output, "\uE003")]
        [TestCase("Generic USB Gamepad", "SharpDX_DirectInput", "hid", DeviceIoType.Input, "\uE004")]
        [TestCase("Arcade Stick", "SharpDX_DirectInput", "hid", DeviceIoType.Input, "\uE005")]
        public void CatalogMapsDeviceFamiliesToIconFontCharacters(
            string title, string provider, string handle, DeviceIoType ioType, string expected)
        {
            var visual = DeviceVisualCatalog.Describe(new Device(title, provider, handle, 0), ioType);
            Assert.That(visual.SymbolText, Is.EqualTo(expected));
        }

        [TestCase("Enter", "↵")]
        [TestCase("Return", "↵")]
        [TestCase("Num Enter", "NUM\nENT")]
        [TestCase("Numpad Enter", "NUM\nENT")]
        [TestCase("NUM 6", "NUM\n6")]
        [TestCase("Numpad6", "NUM\n6")]
        [TestCase("Keypad 0", "NUM\n0")]
        [TestCase("NumPad Add", "NUM\n+")]
        [TestCase("Numpad Divide", "NUM\n÷")]
        [TestCase("Numpad Decimal", "NUM\n.")]
        [TestCase("Num Lock", "NUM\nLK")]
        [TestCase("Left Shift", "L⇧")]
        [TestCase("RShift", "R⇧")]
        [TestCase("Shift", "⇧")]
        [TestCase("Left Control", "L\nCTRL")]
        [TestCase("Right Ctrl", "R\nCTRL")]
        [TestCase("LAlt", "L\nALT")]
        [TestCase("Right Windows", "R\nWIN")]
        [TestCase("Backspace", "⌫")]
        [TestCase("Tab", "⇥")]
        [TestCase("Pause", "PAUS")]
        [TestCase("Home", "HOME")]
        [TestCase("Page Down", "PG\nDN")]
        [TestCase("Print Screen", "PRT\nSC")]
        [TestCase("F24", "F24")]
        [TestCase("B", "B")]
        public void KeyboardGlyphLabelsRemainReadableInsideFixedKeycaps(string keyName, string expected)
        {
            Assert.That(DeviceVisualCatalog.FormatKeyboardGlyphLabel(keyName), Is.EqualTo(expected));
        }

        [Test]
        public void ViGEmOutputsUseTheControllerTheyActuallyEmulate()
        {
            var ds4 = DeviceVisualCatalog.Describe(
                new Device("ViGEm DualShock 4", "Core_ViGEm", "ds4", 0), DeviceIoType.Output);
            var xbox = DeviceVisualCatalog.Describe(
                new Device("ViGEm Xbox 360", "Core_ViGEm", "xb360", 0), DeviceIoType.Output);

            Assert.That(ds4.Kind, Is.EqualTo(DeviceVisualKind.PlayStation4));
            Assert.That(xbox.Kind, Is.EqualTo(DeviceVisualKind.Xbox360));
        }
    }
}
