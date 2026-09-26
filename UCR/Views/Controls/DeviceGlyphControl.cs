using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using HidWizards.UCR.ViewModels.Presentation;

namespace HidWizards.UCR.Views.Controls
{
    /// <summary>
    /// Resolution-independent monoline pictograms for UCR device rows.
    /// The glyphs are drawn from WPF vector geometry at runtime; there are no bitmap assets.
    /// </summary>
    public sealed class DeviceGlyphControl : FrameworkElement
    {
        public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
            nameof(Kind), typeof(DeviceVisualKind), typeof(DeviceGlyphControl),
            new FrameworkPropertyMetadata(DeviceVisualKind.Unknown, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
            nameof(Stroke), typeof(Brush), typeof(DeviceGlyphControl),
            new FrameworkPropertyMetadata(Brushes.LightGray, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty GlyphStrokeThicknessProperty = DependencyProperty.Register(
            nameof(GlyphStrokeThickness), typeof(double), typeof(DeviceGlyphControl),
            new FrameworkPropertyMetadata(3.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty GlowOpacityProperty = DependencyProperty.Register(
            nameof(GlowOpacity), typeof(double), typeof(DeviceGlyphControl),
            new FrameworkPropertyMetadata(0.16, FrameworkPropertyMetadataOptions.AffectsRender));

        private static readonly Dictionary<string, Geometry> GeometryCache = new Dictionary<string, Geometry>();

        public DeviceVisualKind Kind
        {
            get { return (DeviceVisualKind)GetValue(KindProperty); }
            set { SetValue(KindProperty, value); }
        }

        public Brush Stroke
        {
            get { return (Brush)GetValue(StrokeProperty); }
            set { SetValue(StrokeProperty, value); }
        }

        public double GlyphStrokeThickness
        {
            get { return (double)GetValue(GlyphStrokeThicknessProperty); }
            set { SetValue(GlyphStrokeThicknessProperty, value); }
        }

        public double GlowOpacity
        {
            get { return (double)GetValue(GlowOpacityProperty); }
            set { SetValue(GlowOpacityProperty, value); }
        }

        public DeviceGlyphControl()
        {
            SnapsToDevicePixels = false;
            UseLayoutRounding = false;
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            return new Size(
                double.IsInfinity(availableSize.Width) ? 42 : availableSize.Width,
                double.IsInfinity(availableSize.Height) ? 26 : availableSize.Height);
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            if (ActualWidth <= 0 || ActualHeight <= 0) return;

            // Every glyph lives in the same 100x64 optical canvas. WPF scales the vector directly,
            // so zoom/DPI changes never select another bitmap size or invoke mip mapping.
            var scale = Math.Min(ActualWidth / 100.0, ActualHeight / 64.0);
            var x = (ActualWidth - 100.0 * scale) * 0.5;
            var y = (ActualHeight - 64.0 * scale) * 0.5;

            dc.PushTransform(new TranslateTransform(x, y));
            dc.PushTransform(new ScaleTransform(scale, scale));

            var stroke = Stroke ?? Brushes.LightGray;

            Geometry approvedGeometry;
            if (DeviceGlyphVectors.TryGet(Kind, out approvedGeometry))
            {
                // The approved outer silhouette comes directly from the chosen monoline artwork.
                // Keep the interior intentionally sparse at tiny UI sizes: silhouette first,
                // only the controls needed to identify the controller family.
                dc.DrawGeometry(stroke, null, approvedGeometry);
                DrawApprovedDetails(dc, CreatePen(stroke, Math.Max(1.8, GlyphStrokeThickness)));
            }
            else
            {
                var thickness = Math.Max(1.8, GlyphStrokeThickness);
                DrawGlyph(dc, CreatePen(stroke, thickness));
            }

            dc.Pop();
            dc.Pop();
        }

        private static Brush CreateGlowBrush(Brush brush, double opacity)
        {
            var solid = brush as SolidColorBrush;
            if (solid == null || opacity <= 0) return null;

            var alpha = (byte)Math.Max(0, Math.Min(255, Math.Round(255.0 * Math.Min(0.35, opacity))));
            var glow = new SolidColorBrush(Color.FromArgb(alpha, solid.Color.R, solid.Color.G, solid.Color.B));
            glow.Freeze();
            return glow;
        }

        private static Pen CreatePen(Brush brush, double thickness)
        {
            var pen = new Pen(brush, thickness)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };
            if (pen.CanFreeze) pen.Freeze();
            return pen;
        }

        private void DrawApprovedDetails(DrawingContext dc, Pen pen)
        {
            switch (Kind)
            {
                case DeviceVisualKind.PlayStation1:
                    DPad(dc, pen, 27, 28, 10);
                    FaceButtons(dc, pen, 74, 28, 2.3, 6.0);
                    Line(dc, pen, 45, 30, 48, 30);
                    Line(dc, pen, 52, 30, 55, 30);
                    break;

                case DeviceVisualKind.PlayStation2:
                case DeviceVisualKind.PlayStation3:
                    DPad(dc, pen, 27, 28, 10);
                    FaceButtons(dc, pen, 74, 28, 2.3, 6.0);
                    Circle(dc, pen, 42, 40, 4.4);
                    Circle(dc, pen, 58, 40, 4.4);
                    Line(dc, pen, 46, 29, 49, 29);
                    Line(dc, pen, 51, 29, 54, 29);
                    break;

                case DeviceVisualKind.PlayStation4:
                    DPad(dc, pen, 27, 27, 10);
                    FaceButtons(dc, pen, 74, 27, 2.3, 5.8);
                    Circle(dc, pen, 42, 39, 4.3);
                    Circle(dc, pen, 58, 39, 4.3);
                    dc.DrawRoundedRectangle(null, pen, new Rect(41, 14, 18, 9), 1.6, 1.6);
                    break;

                case DeviceVisualKind.PlayStation5:
                    DPad(dc, pen, 27, 27, 10);
                    FaceButtons(dc, pen, 74, 27, 2.3, 5.8);
                    Circle(dc, pen, 42, 38, 4.3);
                    Circle(dc, pen, 58, 38, 4.3);
                    dc.DrawRoundedRectangle(null, pen, new Rect(40, 13, 20, 10), 1.8, 1.8);
                    break;

                case DeviceVisualKind.XboxOriginal:
                    Circle(dc, pen, 28, 25, 5.3);
                    DPad(dc, pen, 38, 40, 8.5);
                    Circle(dc, pen, 59, 39, 4.5);
                    FaceButtons(dc, pen, 75, 26, 2.2, 5.4);
                    Circle(dc, pen, 50, 21, 3.0);
                    break;

                case DeviceVisualKind.Xbox360:
                    Circle(dc, pen, 28, 25, 4.8);
                    DPad(dc, pen, 37, 40, 8.2);
                    Circle(dc, pen, 60, 39, 4.5);
                    FaceButtons(dc, pen, 74, 26, 2.2, 5.5);
                    Circle(dc, pen, 50, 20, 2.8);
                    break;

                case DeviceVisualKind.XboxOne:
                case DeviceVisualKind.XboxSeries:
                    Circle(dc, pen, 29, 25, 4.8);
                    DPad(dc, pen, 37, 40, 8.2);
                    Circle(dc, pen, 60, 39, 4.5);
                    FaceButtons(dc, pen, 74, 26, 2.2, 5.5);
                    Circle(dc, pen, 50, 20, 2.5);
                    break;

                case DeviceVisualKind.Nintendo64:
                    DPad(dc, pen, 26, 28, 8.8);
                    Circle(dc, pen, 50, 34, 4.2);
                    Circle(dc, pen, 74, 27, 2.5);
                    Circle(dc, pen, 81, 23, 1.7);
                    Circle(dc, pen, 81, 31, 1.7);
                    break;

                case DeviceVisualKind.GameCube:
                    Circle(dc, pen, 27, 25, 5.3);
                    DPad(dc, pen, 31, 43, 6.8);
                    Circle(dc, pen, 60, 42, 3.5);
                    Circle(dc, pen, 73, 29, 5.0);
                    Circle(dc, pen, 82, 24, 2.4);
                    break;

                case DeviceVisualKind.WiiRemote:
                    DPad(dc, pen, 50, 15, 7.5);
                    Circle(dc, pen, 50, 29, 3.0);
                    Circle(dc, pen, 50, 46, 1.2);
                    Circle(dc, pen, 50, 53, 1.2);
                    break;

                case DeviceVisualKind.WiiClassic:
                    DPad(dc, pen, 25, 30, 8.8);
                    Circle(dc, pen, 42, 42, 4.0);
                    Circle(dc, pen, 58, 42, 4.0);
                    FaceButtons(dc, pen, 75, 29, 2.1, 5.2);
                    break;

                case DeviceVisualKind.SwitchPro:
                    Circle(dc, pen, 29, 25, 4.6);
                    DPad(dc, pen, 37, 40, 7.8);
                    Circle(dc, pen, 60, 39, 4.4);
                    FaceButtons(dc, pen, 74, 26, 2.1, 5.3);
                    break;

                case DeviceVisualKind.SwitchJoyCon:
                    Circle(dc, pen, 35, 20, 4.0);
                    FaceButtons(dc, pen, 65, 21, 1.9, 4.3);
                    FaceButtons(dc, pen, 35, 42, 1.7, 4.0);
                    Circle(dc, pen, 65, 43, 4.0);
                    break;

                case DeviceVisualKind.VJoy:
                    DPad(dc, pen, 28, 29, 8.8);
                    Circle(dc, pen, 42, 41, 4.0);
                    Circle(dc, pen, 58, 41, 4.0);
                    FaceButtons(dc, pen, 74, 28, 2.1, 5.2);
                    break;
            }
        }

        private void DrawGlyph(DrawingContext dc, Pen pen)
        {
            switch (Kind)
            {
                case DeviceVisualKind.Keyboard: DrawKeyboard(dc, pen); break;
                case DeviceVisualKind.Mouse: DrawMouse(dc, pen); break;
                case DeviceVisualKind.PlayStation1: DrawPlayStation(dc, pen, 1); break;
                case DeviceVisualKind.PlayStation2: DrawPlayStation(dc, pen, 2); break;
                case DeviceVisualKind.PlayStation3: DrawPlayStation(dc, pen, 3); break;
                case DeviceVisualKind.PlayStation4: DrawPlayStation(dc, pen, 4); break;
                case DeviceVisualKind.PlayStation5: DrawPlayStation(dc, pen, 5); break;
                case DeviceVisualKind.XboxOriginal: DrawXbox(dc, pen, 0); break;
                case DeviceVisualKind.Xbox360: DrawXbox(dc, pen, 360); break;
                case DeviceVisualKind.XboxOne: DrawXbox(dc, pen, 1); break;
                case DeviceVisualKind.XboxSeries: DrawXbox(dc, pen, 2); break;
                case DeviceVisualKind.Nintendo64: DrawNintendo64(dc, pen); break;
                case DeviceVisualKind.GameCube: DrawGameCube(dc, pen); break;
                case DeviceVisualKind.WiiRemote: DrawWiiRemote(dc, pen); break;
                case DeviceVisualKind.WiiClassic: DrawWiiClassic(dc, pen); break;
                case DeviceVisualKind.SwitchPro: DrawSwitchPro(dc, pen); break;
                case DeviceVisualKind.SwitchJoyCon: DrawJoyCons(dc, pen); break;
                case DeviceVisualKind.VJoy: DrawVJoy(dc, pen); break;
                case DeviceVisualKind.ArcadeStick: DrawArcadeStick(dc, pen); break;
                case DeviceVisualKind.Unavailable: DrawUnavailable(dc, pen); break;
                case DeviceVisualKind.Gamepad:
                case DeviceVisualKind.DirectInput:
                case DeviceVisualKind.Unknown:
                default:
                    DrawGenericGamepad(dc, pen);
                    break;
            }
        }

        private static Geometry Path(string key, string data)
        {
            Geometry geometry;
            if (GeometryCache.TryGetValue(key, out geometry)) return geometry;

            geometry = Geometry.Parse(data);
            if (geometry.CanFreeze) geometry.Freeze();
            GeometryCache[key] = geometry;
            return geometry;
        }

        private static void DrawPath(DrawingContext dc, Pen pen, string key, string data)
        {
            dc.DrawGeometry(null, pen, Path(key, data));
        }

        private static void Circle(DrawingContext dc, Pen pen, double x, double y, double radius)
        {
            dc.DrawEllipse(null, pen, new Point(x, y), radius, radius);
        }

        private static void Line(DrawingContext dc, Pen pen, double x1, double y1, double x2, double y2)
        {
            dc.DrawLine(pen, new Point(x1, y1), new Point(x2, y2));
        }

        private static void DPad(DrawingContext dc, Pen pen, double cx, double cy, double size)
        {
            var h = size * 0.5;
            var a = size * 0.18;
            var key = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "dpad_{0}_{1}_{2}", cx, cy, size);
            DrawPath(dc, pen, key,
                string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "M{0},{1} L{2},{1} L{2},{3} L{4},{3} L{4},{5} L{2},{5} L{2},{6} L{0},{6} L{0},{5} L{7},{5} L{7},{3} L{0},{3} Z",
                    cx - a, cy - h, cx + a, cy - a, cx + h, cy + a, cy + h, cx - h));
        }

        private static void FaceButtons(DrawingContext dc, Pen pen, double cx, double cy, double radius, double offset)
        {
            Circle(dc, pen, cx, cy - offset, radius);
            Circle(dc, pen, cx + offset, cy, radius);
            Circle(dc, pen, cx, cy + offset, radius);
            Circle(dc, pen, cx - offset, cy, radius);
        }

        private static void PlayStationFaceButtons(DrawingContext dc, Pen pen, double cx, double cy, double offset)
        {
            // Triangle
            dc.PushTransform(new TranslateTransform(cx, cy - offset));
            dc.DrawGeometry(null, pen, Path("ps_triangle", "M0,-3.8 L3.8,3.2 L-3.8,3.2 Z"));
            dc.Pop();

            // Circle
            Circle(dc, pen, cx + offset, cy, 3.4);

            // Cross
            Line(dc, pen, cx - 2.6, cy + offset - 2.6, cx + 2.6, cy + offset + 2.6);
            Line(dc, pen, cx + 2.6, cy + offset - 2.6, cx - 2.6, cy + offset + 2.6);

            // Square
            dc.DrawRoundedRectangle(null, pen, new Rect(cx - offset - 3.2, cy - 3.2, 6.4, 6.4), 0.7, 0.7);
        }

        private static void DrawGenericGamepad(DrawingContext dc, Pen pen)
        {
            DrawPath(dc, pen, "generic_outer",
                "M18,18 C10,19 7,31 7,44 C7,55 12,60 18,57 L29,47 C34,43 39,44 44,47 C48,50 52,50 56,47 C61,44 66,43 71,47 L82,57 C88,60 93,55 93,44 C93,31 90,19 82,18 C73,16 66,20 60,22 H40 C34,20 27,16 18,18 Z");
            DPad(dc, pen, 28, 31, 13);
            FaceButtons(dc, pen, 73, 30, 2.8, 7.0);
            Circle(dc, pen, 42, 42, 5.0);
            Circle(dc, pen, 58, 42, 5.0);
            Line(dc, pen, 46, 29, 49, 29);
            Line(dc, pen, 51, 29, 54, 29);
        }

        private static void DrawPlayStation(DrawingContext dc, Pen pen, int generation)
        {
            string key;
            string outer;

            if (generation == 1 || generation == 2)
            {
                key = generation == 1 ? "ps1_outer" : "ps2_outer";
                outer = "M18,17 C13,17 10,22 9,30 L6,48 C5,56 10,59 16,55 L28,44 C32,40 37,41 42,44 H58 C63,41 68,40 72,44 L84,55 C90,59 95,56 94,48 L91,30 C90,22 87,17 82,17 C74,16 67,19 61,22 H39 C33,19 26,16 18,17 Z";
            }
            else if (generation == 3)
            {
                key = "ps3_outer";
                outer = "M16,18 C10,20 8,30 7,42 C6,54 10,60 16,57 L28,47 C33,43 38,43 42,46 C47,49 53,49 58,46 C62,43 67,43 72,47 L84,57 C90,60 94,54 93,42 C92,30 90,20 84,18 C76,15 68,19 61,22 H39 C32,19 24,15 16,18 Z";
            }
            else if (generation == 4)
            {
                key = "ps4_outer";
                outer = "M14,19 C8,22 7,34 7,45 C7,56 12,60 18,56 L29,47 C34,43 39,43 44,47 C48,50 52,50 56,47 C61,43 66,43 71,47 L82,56 C88,60 93,56 93,45 C93,34 92,22 86,19 C77,15 69,19 62,21 H38 C31,19 23,15 14,19 Z";
            }
            else
            {
                key = "ps5_outer";
                outer = "M12,18 C7,22 6,35 8,48 C10,59 16,61 22,54 L32,43 C36,39 41,40 45,44 C48,47 52,47 55,44 C59,40 64,39 68,43 L78,54 C84,61 90,59 92,48 C94,35 93,22 88,18 C79,12 70,16 61,20 H39 C30,16 21,12 12,18 Z";
            }

            DrawPath(dc, pen, key, outer);
            DPad(dc, pen, 27, 30, 12);
            PlayStationFaceButtons(dc, pen, 74, 30, 7.0);

            if (generation >= 2)
            {
                Circle(dc, pen, 42, 42, 5.0);
                Circle(dc, pen, 58, 42, 5.0);
            }

            if (generation == 4)
            {
                dc.DrawRoundedRectangle(null, pen, new Rect(40, 18, 20, 10), 2, 2);
                Circle(dc, pen, 50, 33, 1.4);
            }
            else if (generation == 5)
            {
                dc.DrawRoundedRectangle(null, pen, new Rect(39, 17, 22, 11), 2, 2);
                Circle(dc, pen, 50, 33, 1.6);
                Line(dc, pen, 47, 37, 53, 37);
            }
            else
            {
                Line(dc, pen, 45, 30, 48, 30);
                Line(dc, pen, 52, 30, 55, 30);
            }

            // Shoulder caps give the generations a more controller-specific top profile.
            Line(dc, pen, 18, 17, 19, 13);
            Line(dc, pen, 19, 13, 31, 13);
            Line(dc, pen, 69, 13, 81, 13);
            Line(dc, pen, 81, 13, 82, 17);
        }

        private static void DrawXbox(DrawingContext dc, Pen pen, int generation)
        {
            string key;
            string outer;

            if (generation == 0)
            {
                key = "xbox_duke_outer";
                outer = "M13,15 C7,18 5,29 7,42 C9,55 16,61 24,56 L34,48 C39,44 44,45 50,48 C56,45 61,44 66,48 L76,56 C84,61 91,55 93,42 C95,29 93,18 87,15 C78,10 68,15 61,19 H39 C32,15 22,10 13,15 Z";
            }
            else if (generation == 360)
            {
                key = "xbox360_outer";
                outer = "M16,16 C9,19 7,30 8,43 C9,55 14,60 21,56 L31,47 C36,43 41,44 45,47 C49,50 51,50 55,47 C59,44 64,43 69,47 L79,56 C86,60 91,55 92,43 C93,30 91,19 84,16 C75,13 68,18 61,21 H39 C32,18 25,13 16,16 Z";
            }
            else if (generation == 1)
            {
                key = "xboxone_outer";
                outer = "M15,18 C8,21 7,32 8,45 C9,56 14,60 21,55 L31,46 C36,42 41,43 45,46 C49,49 51,49 55,46 C59,43 64,42 69,46 L79,55 C86,60 91,56 92,45 C93,32 92,21 85,18 C76,14 68,18 61,21 H39 C32,18 24,14 15,18 Z";
            }
            else
            {
                key = "xboxseries_outer";
                outer = "M15,17 C8,20 7,31 8,44 C9,56 14,60 21,55 L31,46 C36,42 41,43 45,46 C49,49 51,49 55,46 C59,43 64,42 69,46 L79,55 C86,60 91,56 92,44 C93,31 92,20 85,17 C76,13 68,18 61,21 H39 C32,18 24,13 15,17 Z";
            }

            DrawPath(dc, pen, key, outer);

            if (generation == 0)
            {
                Circle(dc, pen, 28, 29, 6.2);
                DPad(dc, pen, 38, 43, 10);
                Circle(dc, pen, 59, 42, 5.2);
                FaceButtons(dc, pen, 74, 28, 2.7, 6.3);
                Circle(dc, pen, 50, 24, 4.2);
            }
            else
            {
                Circle(dc, pen, 29, 28, 5.2);
                DPad(dc, pen, 37, 42, generation == 2 ? 10.5 : 9.5);
                Circle(dc, pen, 60, 40, 5.2);
                FaceButtons(dc, pen, 74, 28, 2.7, 6.3);
                Circle(dc, pen, 50, 22, generation == 360 ? 4.0 : 3.2);
                Circle(dc, pen, 46, 31, 1.0);
                Circle(dc, pen, 54, 31, 1.0);
                if (generation == 2) Circle(dc, pen, 50, 34, 1.0);
            }
        }

        private static void DrawNintendo64(DrawingContext dc, Pen pen)
        {
            DrawPath(dc, pen, "n64_outer",
                "M18,16 C11,18 8,27 9,37 L12,54 C13,60 18,61 22,56 L34,42 L40,57 C42,62 47,62 49,57 L50,50 L51,57 C53,62 58,62 60,57 L66,42 L78,56 C82,61 87,60 88,54 L91,37 C92,27 89,18 82,16 C74,14 66,18 59,22 H41 C34,18 26,14 18,16 Z");
            DPad(dc, pen, 27, 30, 10.5);
            Circle(dc, pen, 50, 34, 5.0);
            Circle(dc, pen, 74, 28, 3.1);
            Circle(dc, pen, 82, 24, 2.2);
            Circle(dc, pen, 82, 32, 2.2);
            Circle(dc, pen, 68, 38, 2.2);
            Circle(dc, pen, 76, 40, 2.2);
            Circle(dc, pen, 50, 25, 1.4);
        }

        private static void DrawGameCube(DrawingContext dc, Pen pen)
        {
            DrawPath(dc, pen, "gamecube_outer",
                "M14,18 C8,21 6,33 8,45 C10,57 17,61 24,55 L33,46 C38,42 43,44 47,47 C50,49 53,49 57,46 C61,43 66,42 71,46 L80,55 C87,61 93,57 94,45 C96,33 92,21 85,18 C78,14 68,17 61,21 H39 C32,18 21,14 14,18 Z");
            Circle(dc, pen, 28, 27, 6.8);
            DPad(dc, pen, 31, 44, 8);
            Circle(dc, pen, 61, 43, 4.1);
            Circle(dc, pen, 72, 30, 6.4);
            Circle(dc, pen, 83, 25, 2.9);
            Circle(dc, pen, 83, 36, 2.9);
            Circle(dc, pen, 65, 23, 2.1);
        }

        private static void DrawWiiRemote(DrawingContext dc, Pen pen)
        {
            dc.DrawRoundedRectangle(null, pen, new Rect(39, 4, 22, 56), 6, 6);
            DPad(dc, pen, 50, 16, 8);
            Circle(dc, pen, 50, 29, 3.8);
            Line(dc, pen, 46, 39, 54, 39);
            Line(dc, pen, 50, 35, 50, 43);
            Circle(dc, pen, 50, 49, 1.5);
            Circle(dc, pen, 50, 55, 1.5);
        }

        private static void DrawWiiClassic(DrawingContext dc, Pen pen)
        {
            DrawPath(dc, pen, "wii_classic_outer",
                "M12,21 C7,24 7,33 8,41 C9,50 15,54 22,51 L31,46 C36,43 40,44 44,47 C48,50 52,50 56,47 C60,44 64,43 69,46 L78,51 C85,54 91,50 92,41 C93,33 93,24 88,21 C79,17 68,19 61,22 H39 C32,19 21,17 12,21 Z");
            DPad(dc, pen, 26, 32, 11);
            FaceButtons(dc, pen, 74, 31, 2.6, 5.7);
            Circle(dc, pen, 42, 43, 4.5);
            Circle(dc, pen, 58, 43, 4.5);
            Circle(dc, pen, 47, 30, 1.0);
            Circle(dc, pen, 53, 30, 1.0);
        }

        private static void DrawSwitchPro(DrawingContext dc, Pen pen)
        {
            DrawPath(dc, pen, "switch_pro_outer",
                "M15,18 C8,21 7,32 8,44 C9,56 14,60 21,55 L31,46 C36,42 41,43 45,46 C49,49 51,49 55,46 C59,43 64,42 69,46 L79,55 C86,60 91,56 92,44 C93,32 92,21 85,18 C76,14 68,18 61,21 H39 C32,18 24,14 15,18 Z");
            Circle(dc, pen, 29, 28, 5.2);
            DPad(dc, pen, 37, 42, 9.0);
            Circle(dc, pen, 60, 40, 5.2);
            FaceButtons(dc, pen, 73, 28, 2.7, 6.0);
            Line(dc, pen, 44, 26, 48, 26);
            Line(dc, pen, 52, 26, 56, 26);
            Line(dc, pen, 54, 24, 54, 28);
        }

        private static void DrawJoyCons(DrawingContext dc, Pen pen)
        {
            dc.DrawRoundedRectangle(null, pen, new Rect(25, 6, 20, 52), 8, 8);
            dc.DrawRoundedRectangle(null, pen, new Rect(55, 6, 20, 52), 8, 8);
            Circle(dc, pen, 35, 21, 4.5);
            FaceButtons(dc, pen, 65, 22, 2.2, 4.8);
            FaceButtons(dc, pen, 35, 42, 2.0, 4.5);
            Circle(dc, pen, 65, 43, 4.5);
            Line(dc, pen, 31, 10, 37, 10);
            Line(dc, pen, 62, 10, 68, 10);
            Line(dc, pen, 65, 7, 65, 13);
        }

        private static void DrawVJoy(DrawingContext dc, Pen pen)
        {
            DrawPath(dc, pen, "vjoy_outer",
                "M17,18 C10,20 7,31 8,44 C9,55 14,59 21,55 L31,47 C36,43 41,44 45,47 C49,50 51,50 55,47 C59,44 64,43 69,47 L79,55 C86,59 91,55 92,44 C93,31 90,20 83,18 C74,15 67,19 60,22 H40 C33,19 26,15 17,18 Z");
            DPad(dc, pen, 29, 31, 11);
            FaceButtons(dc, pen, 72, 31, 2.6, 6.0);
            Circle(dc, pen, 42, 43, 4.6);
            Circle(dc, pen, 58, 43, 4.6);
            DrawPath(dc, pen, "vjoy_v", "M46,25 L50,32 L54,25");
        }

        private static void DrawArcadeStick(DrawingContext dc, Pen pen)
        {
            dc.DrawRoundedRectangle(null, pen, new Rect(8, 29, 84, 26), 5, 5);
            Line(dc, pen, 31, 30, 31, 16);
            Circle(dc, pen, 31, 12, 4.0);
            Circle(dc, pen, 63, 37, 3.2);
            Circle(dc, pen, 74, 35, 3.2);
            Circle(dc, pen, 84, 33, 3.2);
            Circle(dc, pen, 67, 47, 3.2);
            Circle(dc, pen, 78, 45, 3.2);
        }

        private static void DrawKeyboard(DrawingContext dc, Pen pen)
        {
            dc.DrawRoundedRectangle(null, pen, new Rect(6, 17, 88, 32), 4, 4);
            for (var row = 0; row < 3; row++)
            {
                for (var col = 0; col < 9; col++)
                {
                    var x = 13 + col * 8.4;
                    var y = 23 + row * 7.0;
                    Line(dc, pen, x, y, x + 4.3, y);
                }
            }
            Line(dc, pen, 34, 44, 66, 44);
        }

        private static void DrawMouse(DrawingContext dc, Pen pen)
        {
            dc.DrawRoundedRectangle(null, pen, new Rect(35, 5, 30, 54), 15, 15);
            Line(dc, pen, 50, 6, 50, 27);
            dc.DrawRoundedRectangle(null, pen, new Rect(47.5, 10, 5, 10), 2.5, 2.5);
        }

        private static void DrawUnavailable(DrawingContext dc, Pen pen)
        {
            dc.DrawRoundedRectangle(null, pen, new Rect(18, 16, 48, 32), 5, 5);
            Line(dc, pen, 69, 32, 84, 32);
            Line(dc, pen, 84, 26, 84, 38);
            Line(dc, pen, 14, 55, 88, 9);
        }
    }
}
