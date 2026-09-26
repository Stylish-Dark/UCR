using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HidWizards.UCR.Core.Models;
using HidWizards.UCR.ViewModels.Presentation;
using HidWizards.UCR.Views.Controls;
using NUnit.Framework;

namespace HidWizards.UCR.Tests.UiTests
{
    [TestFixture]
    [NonParallelizable]
    internal class DeviceGlyphTests
    {
        [TestCase("Sony DualSense Wireless Controller", "SharpDX_DirectInput", "hid", DeviceVisualKind.PlayStation5)]
        [TestCase("DUALSHOCK 4 Controller", "SharpDX_DirectInput", "hid", DeviceVisualKind.PlayStation4)]
        [TestCase("PLAYSTATION(R)3 Controller", "SharpDX_DirectInput", "hid", DeviceVisualKind.PlayStation3)]
        [TestCase("DualShock 2 USB Adapter", "SharpDX_DirectInput", "hid", DeviceVisualKind.PlayStation2)]
        [TestCase("PSX Controller Adapter", "SharpDX_DirectInput", "hid", DeviceVisualKind.PlayStation1)]
        [TestCase("Xbox Series Controller", "SharpDX_XInput", "xinput", DeviceVisualKind.XboxSeries)]
        [TestCase("Xbox One Controller", "SharpDX_XInput", "xinput", DeviceVisualKind.XboxOne)]
        [TestCase("Xbox 360 Controller", "SharpDX_XInput", "xinput", DeviceVisualKind.Xbox360)]
        [TestCase("Original Xbox Duke Controller", "SharpDX_DirectInput", "hid", DeviceVisualKind.XboxOriginal)]
        [TestCase("Nintendo Switch Pro Controller", "SharpDX_DirectInput", "hid", DeviceVisualKind.SwitchPro)]
        [TestCase("Nintendo Joy-Con Pair", "SharpDX_DirectInput", "hid", DeviceVisualKind.SwitchJoyCon)]
        [TestCase("Nintendo 64 Controller", "SharpDX_DirectInput", "hid", DeviceVisualKind.Nintendo64)]
        [TestCase("GameCube Controller", "SharpDX_DirectInput", "hid", DeviceVisualKind.GameCube)]
        [TestCase("Nintendo Wii Remote", "SharpDX_DirectInput", "hid", DeviceVisualKind.WiiRemote)]
        [TestCase("Wii Classic Controller", "SharpDX_DirectInput", "hid", DeviceVisualKind.WiiClassic)]
        [TestCase("Dreamcast Controller", "SharpDX_DirectInput", "hid", DeviceVisualKind.Dreamcast)]
        [TestCase("Steam Controller", "SharpDX_DirectInput", "hid", DeviceVisualKind.SteamController)]
        [TestCase("vJoy Device", "Core_vJoy", "vjoy", DeviceVisualKind.VJoy)]
        public void CatalogClassifiesRepresentativeControllerFamilies(
            string title, string provider, string handle, DeviceVisualKind expected)
        {
            var device = new Device(title, provider, handle, 0);
            var visual = DeviceVisualCatalog.Describe(device, DeviceIoType.Input);

            Assert.That(visual.Kind, Is.EqualTo(expected));
            Assert.That(visual.GlyphBrush, Is.Not.Null);
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

        [Test]
        public void DeviceSlotsReceiveDistinctDefaultGlyphColours()
        {
            var first = DeviceVisualCatalog.Describe(
                new Device("Xbox 360 Controller", "SharpDX_XInput", "xinput", 0), DeviceIoType.Input);
            var second = DeviceVisualCatalog.Describe(
                new Device("Xbox 360 Controller", "SharpDX_XInput", "xinput", 1), DeviceIoType.Input);

            var firstBrush = first.GlyphBrush as SolidColorBrush;
            var secondBrush = second.GlyphBrush as SolidColorBrush;

            Assert.That(firstBrush, Is.Not.Null);
            Assert.That(secondBrush, Is.Not.Null);
            Assert.That(firstBrush.Color, Is.Not.EqualTo(secondBrush.Color));
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void EveryDeviceGlyphRendersFromVectorGeometry()
        {
            foreach (var kind in Enum.GetValues(typeof(DeviceVisualKind)).Cast<DeviceVisualKind>())
            {
                var glyph = new DeviceGlyphControl
                {
                    Kind = kind,
                    GlyphBrush = Brushes.LightGray,
                    DetailBrush = Brushes.Black,
                    Width = 100,
                    Height = 64
                };

                glyph.Measure(new Size(100, 64));
                glyph.Arrange(new Rect(0, 0, 100, 64));

                var target = new RenderTargetBitmap(100, 64, 96, 96, PixelFormats.Pbgra32);
                Assert.DoesNotThrow(() => target.Render(glyph), "Vector renderer failed for " + kind);
            }
        }
    }
}
