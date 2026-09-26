using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using HidWizards.UCR.ViewModels.Presentation;

namespace HidWizards.UCR.Views.Controls
{
    /// <summary>
    /// Resolution-independent device glyphs. Every icon is built from WPF vector geometry;
    /// there are deliberately no PNG/bitmap assets in the device badge system.
    /// </summary>
    public sealed class DeviceGlyphControl : FrameworkElement
    {
        public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
            nameof(Kind), typeof(DeviceVisualKind), typeof(DeviceGlyphControl),
            new FrameworkPropertyMetadata(DeviceVisualKind.Unknown, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty GlyphBrushProperty = DependencyProperty.Register(
            nameof(GlyphBrush), typeof(Brush), typeof(DeviceGlyphControl),
            new FrameworkPropertyMetadata(Brushes.LightGray, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty DetailBrushProperty = DependencyProperty.Register(
            nameof(DetailBrush), typeof(Brush), typeof(DeviceGlyphControl),
            new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(0x19, 0x19, 0x19)),
                FrameworkPropertyMetadataOptions.AffectsRender));

        private static readonly Dictionary<string, Geometry> GeometryCache = new Dictionary<string, Geometry>();

        public DeviceVisualKind Kind
        {
            get => (DeviceVisualKind)GetValue(KindProperty);
            set => SetValue(KindProperty, value);
        }

        public Brush GlyphBrush
        {
            get => (Brush)GetValue(GlyphBrushProperty);
            set => SetValue(GlyphBrushProperty, value);
        }

        public Brush DetailBrush
        {
            get => (Brush)GetValue(DetailBrushProperty);
            set => SetValue(DetailBrushProperty, value);
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            return new Size(double.IsInfinity(availableSize.Width) ? 42 : availableSize.Width,
                double.IsInfinity(availableSize.Height) ? 26 : availableSize.Height);
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            var width = Math.Max(1, ActualWidth);
            var height = Math.Max(1, ActualHeight);
            var scale = Math.Min(width / 100.0, height / 64.0);
            var x = (width - 100.0 * scale) / 2.0;
            var y = (height - 64.0 * scale) / 2.0;

            dc.PushTransform(new TranslateTransform(x, y));
            dc.PushTransform(new ScaleTransform(scale, scale));
            DrawGlyph(dc, GlyphBrush ?? Brushes.LightGray,
                DetailBrush ?? new SolidColorBrush(Color.FromRgb(0x19, 0x19, 0x19)));
            dc.Pop();
            dc.Pop();
        }

        private void DrawGlyph(DrawingContext dc, Brush fill, Brush detail)
        {
            switch (Kind)
            {
                case DeviceVisualKind.Keyboard: DrawKeyboard(dc, fill, detail); break;
                case DeviceVisualKind.Mouse: DrawMouse(dc, fill, detail); break;
                case DeviceVisualKind.PlayStation1: DrawPlayStation(dc, fill, detail, 1); break;
                case DeviceVisualKind.PlayStation2: DrawPlayStation(dc, fill, detail, 2); break;
                case DeviceVisualKind.PlayStation3: DrawPlayStation(dc, fill, detail, 3); break;
                case DeviceVisualKind.PlayStation4: DrawPlayStation(dc, fill, detail, 4); break;
                case DeviceVisualKind.PlayStation5: DrawPlayStation(dc, fill, detail, 5); break;
                case DeviceVisualKind.XboxOriginal: DrawXbox(dc, fill, detail, 0); break;
                case DeviceVisualKind.Xbox360: DrawXbox(dc, fill, detail, 360); break;
                case DeviceVisualKind.XboxOne: DrawXbox(dc, fill, detail, 1); break;
                case DeviceVisualKind.XboxSeries: DrawXbox(dc, fill, detail, 2); break;
                case DeviceVisualKind.Nes: DrawNes(dc, fill, detail); break;
                case DeviceVisualKind.Snes: DrawSnes(dc, fill, detail); break;
                case DeviceVisualKind.Nintendo64: DrawNintendo64(dc, fill, detail); break;
                case DeviceVisualKind.GameCube: DrawGameCube(dc, fill, detail); break;
                case DeviceVisualKind.Dreamcast: DrawDreamcast(dc, fill, detail); break;
                case DeviceVisualKind.WiiRemote: DrawWiiRemote(dc, fill, detail); break;
                case DeviceVisualKind.WiiClassic: DrawWiiClassic(dc, fill, detail); break;
                case DeviceVisualKind.WiiUPro: DrawWiiUPro(dc, fill, detail); break;
                case DeviceVisualKind.SwitchJoyCon: DrawJoyCons(dc, fill, detail); break;
                case DeviceVisualKind.SwitchPro: DrawSwitchPro(dc, fill, detail); break;
                case DeviceVisualKind.SteamController: DrawSteamController(dc, fill, detail); break;
                case DeviceVisualKind.MegaDrive3: DrawMegaDrive(dc, fill, detail, false); break;
                case DeviceVisualKind.MegaDrive6: DrawMegaDrive(dc, fill, detail, true); break;
                case DeviceVisualKind.SegaSaturn: DrawSaturn(dc, fill, detail); break;
                case DeviceVisualKind.MasterSystem: DrawMasterSystem(dc, fill, detail); break;
                case DeviceVisualKind.AtariJoystick: DrawAtariJoystick(dc, fill, detail); break;
                case DeviceVisualKind.VJoy: DrawVJoy(dc, fill, detail); break;
                case DeviceVisualKind.ArcadeStick: DrawArcadeStick(dc, fill, detail); break;
                case DeviceVisualKind.SteeringWheel: DrawWheel(dc, fill, detail); break;
                case DeviceVisualKind.FlightStick: DrawFlightStick(dc, fill, detail); break;
                case DeviceVisualKind.Pedals: DrawPedals(dc, fill, detail); break;
                case DeviceVisualKind.Unavailable: DrawUnavailable(dc, fill); break;
                case DeviceVisualKind.DirectInput:
                case DeviceVisualKind.Gamepad:
                case DeviceVisualKind.Unknown:
                default:
                    DrawGenericGamepad(dc, fill, detail);
                    break;
            }
        }

        private static Geometry G(string key, string data)
        {
            Geometry geometry;
            if (GeometryCache.TryGetValue(key, out geometry)) return geometry;
            geometry = Geometry.Parse(data);
            if (geometry.CanFreeze) geometry.Freeze();
            GeometryCache[key] = geometry;
            return geometry;
        }

        private static void DrawGenericGamepad(DrawingContext dc, Brush fill, Brush detail)
        {
            dc.DrawGeometry(fill, null, G("pad",
                "M18,17 C10,18 7,29 5,45 C3,56 9,61 16,57 L29,45 C35,48 41,50 50,50 C59,50 65,48 71,45 L84,57 C91,61 97,56 95,45 C93,29 90,18 82,17 C72,15 66,19 60,21 L40,21 C34,19 28,15 18,17 Z"));
            DPad(dc, detail, 28, 31, 13);
            FaceButtons(dc, detail, 73, 31, 4.0, 8.0);
            Stick(dc, detail, 42, 40, 4.5);
            Stick(dc, detail, 58, 40, 4.5);
        }

        private static void DrawPlayStation(DrawingContext dc, Brush fill, Brush detail, int generation)
        {
            var key = generation <= 2 ? "ps12" : generation == 3 ? "ps3" : generation == 4 ? "ps4" : "ps5";
            var path = generation <= 2
                ? "M18,17 L33,17 L38,22 H62 L67,17 H82 C88,18 91,27 93,39 L96,53 C97,59 92,61 87,57 L74,45 C70,42 65,42 60,45 C53,49 47,49 40,45 C35,42 30,42 26,45 L13,57 C8,61 3,59 4,53 L7,39 C9,27 12,18 18,17 Z"
                : generation == 3
                    ? "M17,18 C10,19 8,29 6,42 C4,55 8,61 14,58 L28,46 C33,42 38,43 42,46 C47,49 53,49 58,46 C62,43 67,42 72,46 L86,58 C92,61 96,55 94,42 C92,29 90,19 83,18 C74,16 67,20 61,22 H39 C33,20 26,16 17,18 Z"
                    : generation == 4
                        ? "M14,19 C8,22 6,34 5,46 C4,58 10,61 17,56 L29,46 C35,42 40,44 44,47 C48,50 52,50 56,47 C60,44 65,42 71,46 L83,56 C90,61 96,58 95,46 C94,34 92,22 86,19 C77,15 69,19 62,21 H38 C31,19 23,15 14,19 Z"
                        : "M13,18 C7,22 5,35 6,48 C7,59 13,61 19,55 L31,43 C35,40 40,41 44,44 C48,48 52,48 56,44 C60,41 65,40 69,43 L81,55 C87,61 93,59 94,48 C95,35 93,22 87,18 C78,13 69,17 61,20 H39 C31,17 22,13 13,18 Z";
            dc.DrawGeometry(fill, null, G(key, path));

            DPad(dc, detail, 27, 30, 12);
            FaceButtons(dc, detail, 74, 30, 3.6, 7.4);

            if (generation >= 2)
            {
                Stick(dc, detail, 42, 41, 4.6);
                Stick(dc, detail, 58, 41, 4.6);
            }

            if (generation == 4 || generation == 5)
            {
                dc.DrawRoundedRectangle(detail, null, new Rect(41, 20, 18, generation == 5 ? 9 : 8), 2, 2);
                SmallDot(dc, detail, 50, 33, 1.4);
            }
            else
            {
                dc.DrawRoundedRectangle(detail, null, new Rect(45, 27, 3, 2.2), 1, 1);
                dc.DrawRoundedRectangle(detail, null, new Rect(52, 27, 3, 2.2), 1, 1);
            }
        }

        private static void DrawXbox(DrawingContext dc, Brush fill, Brush detail, int generation)
        {
            string path;
            string key;
            if (generation == 0)
            {
                key = "xboxDuke";
                path = "M14,14 C6,18 5,30 7,45 C9,57 16,61 24,55 L33,47 C37,44 43,46 50,48 C57,46 63,44 67,47 L76,55 C84,61 91,57 93,45 C95,30 94,18 86,14 C76,9 67,15 61,19 H39 C33,15 24,9 14,14 Z";
            }
            else if (generation == 360)
            {
                key = "xbox360";
                path = "M16,16 C9,18 7,29 7,43 C7,55 13,60 20,56 L31,47 C36,43 41,44 45,47 C48,49 52,49 55,47 C59,44 64,43 69,47 L80,56 C87,60 93,55 93,43 C93,29 91,18 84,16 C75,13 68,18 61,21 H39 C32,18 25,13 16,16 Z";
            }
            else
            {
                key = generation == 1 ? "xboxOne" : "xboxSeries";
                path = generation == 1
                    ? "M15,18 C8,21 7,32 8,45 C9,56 14,60 21,55 L31,46 C36,42 41,43 45,46 C49,49 51,49 55,46 C59,43 64,42 69,46 L79,55 C86,60 91,56 92,45 C93,32 92,21 85,18 C76,14 68,18 61,21 H39 C32,18 24,14 15,18 Z"
                    : "M15,17 C8,20 7,31 8,44 C9,56 14,60 21,55 L31,46 C36,42 41,43 45,46 C49,49 51,49 55,46 C59,43 64,42 69,46 L79,55 C86,60 91,56 92,44 C93,31 92,20 85,17 C76,13 68,18 61,21 H39 C32,18 24,13 15,17 Z";
            }

            dc.DrawGeometry(fill, null, G(key, path));
            Stick(dc, detail, 29, 29, generation == 0 ? 6 : 5);
            DPad(dc, detail, generation == 0 ? 39 : 38, 41, generation == 2 ? 12 : 10);
            Stick(dc, detail, 60, 40, 5);
            FaceButtons(dc, detail, 74, 28, generation == 0 ? 3.5 : 3.8, 7.2);
            SmallDot(dc, detail, 50, generation == 0 ? 25 : 22, generation == 0 ? 5 : 3);
            if (generation >= 1)
            {
                SmallDot(dc, detail, 46, 30, 1.2);
                SmallDot(dc, detail, 54, 30, 1.2);
            }
        }

        private static void DrawNes(DrawingContext dc, Brush fill, Brush detail)
        {
            dc.DrawRoundedRectangle(fill, null, new Rect(7, 18, 86, 32), 3, 3);
            DPad(dc, detail, 26, 34, 15);
            dc.DrawRoundedRectangle(detail, null, new Rect(43, 34, 8, 3), 1, 1);
            dc.DrawRoundedRectangle(detail, null, new Rect(54, 34, 8, 3), 1, 1);
            SmallDot(dc, detail, 74, 34, 5.2);
            SmallDot(dc, detail, 86, 34, 5.2);
        }

        private static void DrawSnes(DrawingContext dc, Brush fill, Brush detail)
        {
            dc.DrawGeometry(fill, null, G("snes",
                "M15,19 C9,20 6,27 7,36 C8,46 14,50 23,48 L77,48 C86,50 92,46 93,36 C94,27 91,20 85,19 C76,17 66,20 59,23 H41 C34,20 24,17 15,19 Z"));
            DPad(dc, detail, 27, 34, 13);
            FaceButtons(dc, detail, 74, 33, 3.5, 7.0);
            dc.DrawRoundedRectangle(detail, null, new Rect(44, 34, 5, 2.5), 1, 1);
            dc.DrawRoundedRectangle(detail, null, new Rect(52, 34, 5, 2.5), 1, 1);
        }

        private static void DrawNintendo64(DrawingContext dc, Brush fill, Brush detail)
        {
            dc.DrawGeometry(fill, null, G("n64",
                "M18,16 C10,18 7,27 8,37 L11,54 C12,60 17,61 21,56 L34,42 L39,57 C41,62 47,62 49,57 L50,51 L51,57 C53,62 59,62 61,57 L66,42 L79,56 C83,61 88,60 89,54 L92,37 C93,27 90,18 82,16 C73,14 65,18 58,22 H42 C35,18 27,14 18,16 Z"));
            DPad(dc, detail, 26, 30, 11);
            Stick(dc, detail, 50, 34, 5);
            FaceButtons(dc, detail, 75, 28, 3.2, 6.0);
            SmallDot(dc, detail, 68, 39, 2.3);
            SmallDot(dc, detail, 76, 40, 2.3);
        }

        private static void DrawGameCube(DrawingContext dc, Brush fill, Brush detail)
        {
            dc.DrawGeometry(fill, null, G("gamecube",
                "M14,18 C7,22 6,35 8,47 C10,58 17,60 24,54 L33,45 C38,42 43,44 47,47 C50,49 53,49 57,46 C61,43 66,42 71,46 L80,54 C87,60 93,57 94,46 C95,34 92,22 85,18 C78,14 68,17 61,21 H39 C32,18 21,14 14,18 Z"));
            Stick(dc, detail, 28, 27, 7.5);
            DPad(dc, detail, 31, 43, 8);
            Stick(dc, detail, 61, 43, 4.3);
            SmallDot(dc, detail, 72, 30, 7.2);
            SmallDot(dc, detail, 83, 25, 3.4);
            SmallDot(dc, detail, 83, 36, 3.4);
        }

        private static void DrawDreamcast(DrawingContext dc, Brush fill, Brush detail)
        {
            dc.DrawGeometry(fill, null, G("dreamcast",
                "M18,16 C10,18 8,29 9,42 C10,54 16,60 23,55 L33,47 C38,43 43,44 47,47 C49,49 51,49 53,47 C57,44 62,43 67,47 L77,55 C84,60 90,54 91,42 C92,29 90,18 82,16 C74,13 66,17 60,20 H40 C34,17 26,13 18,16 Z"));
            dc.DrawRoundedRectangle(detail, null, new Rect(42, 18, 16, 13), 2, 2);
            Stick(dc, detail, 26, 30, 5.5);
            DPad(dc, detail, 31, 43, 9);
            FaceButtons(dc, detail, 75, 31, 3.3, 6.5);
            SmallDot(dc, detail, 50, 38, 1.8);
        }

        private static void DrawWiiRemote(DrawingContext dc, Brush fill, Brush detail)
        {
            dc.DrawRoundedRectangle(fill, null, new Rect(39, 5, 22, 54), 5, 5);
            DPad(dc, detail, 50, 16, 8);
            SmallDot(dc, detail, 50, 29, 4.2);
            SmallDot(dc, detail, 50, 39, 1.7);
            SmallDot(dc, detail, 50, 45, 1.7);
            for (var i = 0; i < 4; i++) SmallDot(dc, detail, 44 + i * 4, 53, 0.9);
        }

        private static void DrawWiiClassic(DrawingContext dc, Brush fill, Brush detail)
        {
            dc.DrawGeometry(fill, null, G("wiiClassic",
                "M13,20 C7,23 6,32 7,41 C8,50 14,54 21,51 L31,46 C36,43 40,44 44,47 C48,50 52,50 56,47 C60,44 64,43 69,46 L79,51 C86,54 92,50 93,41 C94,32 93,23 87,20 C78,16 68,19 61,22 H39 C32,19 22,16 13,20 Z"));
            DPad(dc, detail, 26, 32, 11);
            FaceButtons(dc, detail, 74, 31, 3.1, 6.1);
            Stick(dc, detail, 42, 42, 4.3);
            Stick(dc, detail, 58, 42, 4.3);
            SmallDot(dc, detail, 47, 30, 1.2);
            SmallDot(dc, detail, 53, 30, 1.2);
        }

        private static void DrawWiiUPro(DrawingContext dc, Brush fill, Brush detail)
        {
            DrawGenericGamepad(dc, fill, detail);
            Stick(dc, detail, 28, 28, 5.5);
            Stick(dc, detail, 72, 28, 5.5);
            DPad(dc, detail, 33, 42, 9);
            FaceButtons(dc, detail, 68, 42, 3.0, 5.6);
        }

        private static void DrawJoyCons(DrawingContext dc, Brush fill, Brush detail)
        {
            dc.DrawRoundedRectangle(fill, null, new Rect(24, 7, 21, 50), 8, 8);
            dc.DrawRoundedRectangle(fill, null, new Rect(55, 7, 21, 50), 8, 8);
            Stick(dc, detail, 34.5, 21, 5);
            FaceButtons(dc, detail, 65.5, 22, 2.6, 5.2);
            FaceButtons(dc, detail, 34.5, 42, 2.3, 4.8);
            Stick(dc, detail, 65.5, 43, 5);
            dc.DrawRoundedRectangle(detail, null, new Rect(29, 10, 6, 2), 1, 1);
            dc.DrawRoundedRectangle(detail, null, new Rect(64, 9, 2, 6), 1, 1);
            dc.DrawRoundedRectangle(detail, null, new Rect(62, 11, 6, 2), 1, 1);
        }

        private static void DrawSwitchPro(DrawingContext dc, Brush fill, Brush detail)
        {
            DrawGenericGamepad(dc, fill, detail);
            Stick(dc, detail, 29, 28, 5.3);
            DPad(dc, detail, 37, 42, 9);
            Stick(dc, detail, 60, 40, 5.3);
            FaceButtons(dc, detail, 73, 28, 3.1, 6.0);
            SmallDot(dc, detail, 46, 28, 1.2);
            SmallDot(dc, detail, 54, 28, 1.2);
        }

        private static void DrawSteamController(DrawingContext dc, Brush fill, Brush detail)
        {
            DrawGenericGamepad(dc, fill, detail);
            Stick(dc, detail, 29, 28, 9);
            Stick(dc, detail, 71, 28, 9);
            SmallDot(dc, detail, 50, 29, 2.4);
            DPad(dc, detail, 42, 43, 7);
            FaceButtons(dc, detail, 60, 42, 2.5, 4.8);
        }

        private static void DrawMegaDrive(DrawingContext dc, Brush fill, Brush detail, bool sixButton)
        {
            dc.DrawGeometry(fill, null, G(sixButton ? "md6" : "md3",
                "M12,24 C7,27 7,36 11,42 C15,48 23,49 31,45 C38,42 44,43 50,44 C56,43 62,42 69,45 C77,49 85,48 89,42 C93,36 93,27 88,24 C79,19 68,21 59,24 H41 C32,21 21,19 12,24 Z"));
            DPad(dc, detail, 27, 34, 11);
            if (sixButton)
            {
                FaceButtons(dc, detail, 70, 31, 2.7, 5.6);
                SmallDot(dc, detail, 64, 41, 2.2);
                SmallDot(dc, detail, 70, 42, 2.2);
                SmallDot(dc, detail, 76, 41, 2.2);
            }
            else
            {
                SmallDot(dc, detail, 66, 36, 4);
                SmallDot(dc, detail, 75, 33, 4);
                SmallDot(dc, detail, 83, 29, 4);
            }
        }

        private static void DrawSaturn(DrawingContext dc, Brush fill, Brush detail)
        {
            dc.DrawRoundedRectangle(fill, null, new Rect(8, 21, 84, 27), 12, 12);
            DPad(dc, detail, 27, 34, 10);
            FaceButtons(dc, detail, 70, 31, 2.7, 5.5);
            SmallDot(dc, detail, 64, 41, 2.1);
            SmallDot(dc, detail, 70, 42, 2.1);
            SmallDot(dc, detail, 76, 41, 2.1);
        }

        private static void DrawMasterSystem(DrawingContext dc, Brush fill, Brush detail)
        {
            dc.DrawRoundedRectangle(fill, null, new Rect(9, 20, 82, 29), 3, 3);
            DPad(dc, detail, 28, 34, 12);
            SmallDot(dc, detail, 70, 35, 5);
            SmallDot(dc, detail, 83, 35, 5);
        }

        private static void DrawAtariJoystick(DrawingContext dc, Brush fill, Brush detail)
        {
            dc.DrawRoundedRectangle(fill, null, new Rect(20, 43, 60, 14), 3, 3);
            dc.DrawRoundedRectangle(fill, null, new Rect(46, 14, 8, 31), 4, 4);
            SmallDot(dc, fill, 50, 13, 7);
            SmallDot(dc, detail, 68, 49, 3.4);
        }

        private static void DrawVJoy(DrawingContext dc, Brush fill, Brush detail)
        {
            dc.DrawGeometry(fill, null, G("vjoyBase", "M18,47 H82 L75,58 H25 Z"));
            dc.DrawRoundedRectangle(fill, null, new Rect(47, 16, 7, 32), 3.5, 3.5);
            SmallDot(dc, fill, 50.5, 15, 8);
            // The V cutout makes vJoy unmistakable without baking text or a raster logo into the UI.
            dc.DrawGeometry(detail, null, G("vjoyV", "M31,49 L39,49 L50,55 L61,49 L69,49 L50,59 Z"));
        }

        private static void DrawArcadeStick(DrawingContext dc, Brush fill, Brush detail)
        {
            dc.DrawRoundedRectangle(fill, null, new Rect(8, 28, 84, 27), 6, 6);
            dc.DrawRoundedRectangle(fill, null, new Rect(28, 13, 6, 25), 3, 3);
            SmallDot(dc, fill, 31, 12, 7);
            SmallDot(dc, detail, 63, 37, 4);
            SmallDot(dc, detail, 74, 35, 4);
            SmallDot(dc, detail, 84, 33, 4);
            SmallDot(dc, detail, 67, 47, 4);
            SmallDot(dc, detail, 78, 45, 4);
        }

        private static void DrawWheel(DrawingContext dc, Brush fill, Brush detail)
        {
            dc.DrawEllipse(null, Pen(fill, 7), new Point(50, 32), 24, 24);
            dc.DrawLine(Pen(fill, 6), new Point(50, 32), new Point(31, 20));
            dc.DrawLine(Pen(fill, 6), new Point(50, 32), new Point(69, 20));
            dc.DrawLine(Pen(fill, 6), new Point(50, 32), new Point(50, 53));
            SmallDot(dc, fill, 50, 32, 6);
            SmallDot(dc, detail, 50, 32, 2.5);
        }

        private static void DrawFlightStick(DrawingContext dc, Brush fill, Brush detail)
        {
            dc.DrawGeometry(fill, null, G("flightStick",
                "M43,13 C42,9 46,6 51,7 C57,8 61,13 59,18 L55,32 C54,36 52,39 49,42 H42 L39,35 L42,30 Z"));
            dc.DrawRoundedRectangle(fill, null, new Rect(30, 42, 40, 8), 3, 3);
            dc.DrawRoundedRectangle(fill, null, new Rect(22, 50, 56, 9), 3, 3);
            SmallDot(dc, detail, 53, 16, 2.2);
        }

        private static void DrawPedals(DrawingContext dc, Brush fill, Brush detail)
        {
            dc.DrawRoundedRectangle(fill, null, new Rect(17, 49, 66, 8), 2, 2);
            dc.DrawRoundedRectangle(fill, null, new Rect(23, 17, 14, 34), 3, 3);
            dc.DrawRoundedRectangle(fill, null, new Rect(43, 13, 14, 38), 3, 3);
            dc.DrawRoundedRectangle(fill, null, new Rect(63, 19, 14, 32), 3, 3);
            for (var x = 29; x <= 70; x += 20)
            {
                SmallDot(dc, detail, x, 29, 1.5);
                SmallDot(dc, detail, x, 38, 1.5);
            }
        }

        private static void DrawKeyboard(DrawingContext dc, Brush fill, Brush detail)
        {
            dc.DrawRoundedRectangle(fill, null, new Rect(7, 18, 86, 31), 5, 5);
            for (var row = 0; row < 3; row++)
            {
                for (var col = 0; col < 8; col++)
                {
                    dc.DrawRoundedRectangle(detail, null, new Rect(14 + col * 9, 23 + row * 7, 6, 4), 1, 1);
                }
            }
            dc.DrawRoundedRectangle(detail, null, new Rect(33, 43, 34, 3.5), 1, 1);
        }

        private static void DrawMouse(DrawingContext dc, Brush fill, Brush detail)
        {
            dc.DrawRoundedRectangle(fill, null, new Rect(34, 6, 32, 53), 16, 16);
            dc.DrawRoundedRectangle(detail, null, new Rect(48, 10, 4, 13), 2, 2);
            dc.DrawRoundedRectangle(detail, null, new Rect(49, 25, 2, 8), 1, 1);
        }

        private static void DrawUnavailable(DrawingContext dc, Brush fill)
        {
            dc.DrawRoundedRectangle(null, Pen(fill, 4), new Rect(18, 16, 48, 32), 5, 5);
            dc.DrawLine(Pen(fill, 4), new Point(69, 32), new Point(84, 32));
            dc.DrawLine(Pen(fill, 3), new Point(84, 26), new Point(84, 38));
            dc.DrawLine(Pen(fill, 5), new Point(14, 55), new Point(88, 9));
        }

        private static void DPad(DrawingContext dc, Brush brush, double cx, double cy, double size)
        {
            var arm = size * 0.34;
            dc.DrawRoundedRectangle(brush, null, new Rect(cx - arm, cy - size / 2, arm * 2, size), arm * 0.28, arm * 0.28);
            dc.DrawRoundedRectangle(brush, null, new Rect(cx - size / 2, cy - arm, size, arm * 2), arm * 0.28, arm * 0.28);
        }

        private static void FaceButtons(DrawingContext dc, Brush brush, double cx, double cy, double radius, double offset)
        {
            SmallDot(dc, brush, cx, cy - offset, radius);
            SmallDot(dc, brush, cx + offset, cy, radius);
            SmallDot(dc, brush, cx, cy + offset, radius);
            SmallDot(dc, brush, cx - offset, cy, radius);
        }

        private static void Stick(DrawingContext dc, Brush brush, double cx, double cy, double radius)
        {
            dc.DrawEllipse(brush, null, new Point(cx, cy), radius, radius);
        }

        private static void SmallDot(DrawingContext dc, Brush brush, double cx, double cy, double radius)
        {
            dc.DrawEllipse(brush, null, new Point(cx, cy), radius, radius);
        }

        private static Pen Pen(Brush brush, double thickness)
        {
            return new Pen(brush, thickness)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };
        }
    }
}
