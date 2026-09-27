using System;
using System.Windows;
using System.Windows.Media;
using HidWizards.UCR.ViewModels.Presentation;

namespace HidWizards.UCR.Views.Controls
{
    /// <summary>
    /// Hand-authored, optically-tuned glyphs for the core devices that must look excellent
    /// at UCR's real UI sizes. Coordinates use the same 100x64 canvas as DeviceGlyphControl.
    /// </summary>
    internal static class PremiumDeviceGlyphRenderer
    {
        private static Geometry _dualShock4Body;
        private static Geometry _vJoyBody;
        private static Geometry _genericDeviceBody;
        private static Geometry _xbox360WithoutOldDpad;

        public static bool TryDraw(DrawingContext dc, DeviceVisualKind kind, Brush brush, double requestedThickness)
        {
            if (dc == null || brush == null) return false;

            var thickness = Math.Max(1.9, requestedThickness);
            var pen = CreatePen(brush, thickness);

            switch (kind)
            {
                case DeviceVisualKind.Xbox360:
                    DrawXbox360(dc, brush, pen);
                    return true;

                case DeviceVisualKind.PlayStation4:
                    DrawDualShock4(dc, pen);
                    return true;

                case DeviceVisualKind.Keyboard:
                    DrawKeyboard(dc, pen);
                    return true;

                case DeviceVisualKind.VJoy:
                    DrawVJoy(dc, pen);
                    return true;

                case DeviceVisualKind.DirectInput:
                case DeviceVisualKind.Unknown:
                    DrawGenericDevice(dc, pen);
                    return true;

                default:
                    return false;
            }
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

        private static Pen CreateFinePen(Brush brush, double thickness)
        {
            return CreatePen(brush, Math.Max(1.35, thickness * 0.72));
        }

        private static void DrawXbox360(DrawingContext dc, Brush brush, Pen pen)
        {
            // The existing premium trace already has the Xbox 360 silhouette and controls nailed.
            // Its only bad element is the circular lower-left control where the d-pad belongs.
            // Remove that local trace and replace it with a precise d-pad, leaving the rest intact.
            if (_xbox360WithoutOldDpad == null)
            {
                Geometry source;
                if (DeviceGlyphVectors.TryGet(DeviceVisualKind.Xbox360, out source))
                {
                    var eraseOldControl = new EllipseGeometry(new Point(37.2, 36.1), 8.4, 8.4);
                    var cleaned = new CombinedGeometry(GeometryCombineMode.Exclude, source, eraseOldControl);
                    if (cleaned.CanFreeze) cleaned.Freeze();
                    _xbox360WithoutOldDpad = cleaned;
                }
            }

            if (_xbox360WithoutOldDpad != null)
            {
                dc.DrawGeometry(brush, null, _xbox360WithoutOldDpad);
            }
            else
            {
                // Defensive fallback if the premium source ever disappears.
                DrawControllerBody(dc, pen,
                    "M18,16 C13,17 10,22 9,29 L6,45 C5,52 7,58 12,59 " +
                    "C16,60 19,56 22,52 L29,45 C33,41 37,41 42,44 " +
                    "C47,47 53,47 58,44 C63,41 67,41 71,45 L78,52 " +
                    "C81,56 84,60 88,59 C93,58 95,52 94,45 L91,29 " +
                    "C90,22 87,17 82,16 C75,14 68,17 62,20 C58,22 42,22 38,20 " +
                    "C32,17 25,14 18,16 Z");
                Circle(dc, pen, 27, 25, 4.8);
                Circle(dc, pen, 60, 40, 4.5);
                FaceButtons(dc, pen, 74, 30, 2.0, 5.2);
                Circle(dc, pen, 50, 22, 3.0);
            }

            DrawDPad(dc, pen, 37.2, 36.1, 11.2);
        }

        private static void DrawDualShock4(DrawingContext dc, Pen pen)
        {
            if (_dualShock4Body == null)
            {
                _dualShock4Body = Geometry.Parse(
                    "M18,18 " +
                    "C13,18 10,22 9,29 " +
                    "L5,46 " +
                    "C4,53 7,59 13,59 " +
                    "C17,59 20,56 23,52 " +
                    "L30,44 " +
                    "C33,41 37,41 41,44 " +
                    "C46,48 54,48 59,44 " +
                    "C63,41 67,41 70,44 " +
                    "L77,52 " +
                    "C80,56 83,59 87,59 " +
                    "C93,59 96,53 95,46 " +
                    "L91,29 " +
                    "C90,22 87,18 82,18 " +
                    "C75,17 69,19 63,22 " +
                    "L37,22 " +
                    "C31,19 25,17 18,18 Z");
                if (_dualShock4Body.CanFreeze) _dualShock4Body.Freeze();
            }

            dc.DrawGeometry(null, pen, _dualShock4Body);

            // Touch pad: large, clean and unmistakably DS4.
            dc.DrawRoundedRectangle(null, pen, new Rect(40, 18.5, 20, 10.5), 2.2, 2.2);

            // D-pad and analogue sticks are deliberately simple at this scale.
            DrawDPad(dc, pen, 27, 31, 10.5);
            Circle(dc, pen, 42, 42, 4.4);
            Circle(dc, pen, 58, 42, 4.4);

            // Real PlayStation face symbols instead of four anonymous circles.
            DrawTriangle(dc, pen, 74, 25.5, 4.0);
            Circle(dc, pen, 80, 31.5, 2.9);
            DrawCross(dc, pen, 74, 37.5, 4.3);
            dc.DrawRoundedRectangle(null, pen, new Rect(65.1, 28.6, 5.8, 5.8), 0.7, 0.7);

            // Share / Options and PS button, kept understated.
            Line(dc, pen, 34.5, 29.5, 37.5, 29.5);
            Line(dc, pen, 62.5, 29.5, 65.5, 29.5);
            Circle(dc, pen, 50, 34.5, 1.6);
        }

        private static void DrawVJoy(DrawingContext dc, Pen pen)
        {
            if (_vJoyBody == null)
            {
                _vJoyBody = Geometry.Parse(
                    "M18,18 " +
                    "C12,19 9,25 8,33 " +
                    "L6,46 " +
                    "C5,53 8,58 13,59 " +
                    "C17,60 20,56 23,52 " +
                    "L30,45 " +
                    "C34,41 38,41 42,44 " +
                    "C47,48 53,48 58,44 " +
                    "C62,41 66,41 70,45 " +
                    "L77,52 " +
                    "C80,56 83,60 87,59 " +
                    "C92,58 95,53 94,46 " +
                    "L92,33 " +
                    "C91,25 88,19 82,18 " +
                    "C75,16 68,19 62,22 " +
                    "L38,22 " +
                    "C32,19 25,16 18,18 Z");
                if (_vJoyBody.CanFreeze) _vJoyBody.Freeze();
            }

            dc.DrawGeometry(null, pen, _vJoyBody);
            DrawDPad(dc, pen, 27, 31.5, 10.5);
            Circle(dc, pen, 41.5, 42, 4.2);
            Circle(dc, pen, 58.5, 42, 4.2);
            FaceButtons(dc, pen, 74, 31.5, 2.0, 5.2);

            // A subtle V mark makes this a vJoy glyph without pretending vJoy is N64 hardware.
            Line(dc, pen, 46, 28.5, 50, 34.5);
            Line(dc, pen, 50, 34.5, 54, 28.5);
            Circle(dc, pen, 50, 23.5, 2.2);
        }

        private static void DrawKeyboard(DrawingContext dc, Pen pen)
        {
            var fine = CreateFinePen(pen.Brush, pen.Thickness);

            dc.DrawRoundedRectangle(null, pen, new Rect(7.5, 16.5, 85, 32), 5.0, 5.0);

            // Three disciplined rows. Fewer, larger keycaps read much better than a noisy traced grid.
            for (var i = 0; i < 8; i++)
            {
                dc.DrawRoundedRectangle(null, fine, new Rect(13 + i * 9.2, 21.0, 6.3, 4.7), 0.8, 0.8);
            }

            for (var i = 0; i < 7; i++)
            {
                dc.DrawRoundedRectangle(null, fine, new Rect(13 + i * 9.2, 29.5, 6.3, 4.7), 0.8, 0.8);
            }
            dc.DrawRoundedRectangle(null, fine, new Rect(77.4, 29.5, 9.6, 4.7), 0.8, 0.8);

            dc.DrawRoundedRectangle(null, fine, new Rect(13, 38.0, 10.0, 4.7), 0.8, 0.8);
            dc.DrawRoundedRectangle(null, fine, new Rect(26, 38.0, 7.0, 4.7), 0.8, 0.8);
            dc.DrawRoundedRectangle(null, fine, new Rect(36, 38.0, 30.0, 4.7), 0.8, 0.8);
            dc.DrawRoundedRectangle(null, fine, new Rect(69, 38.0, 7.0, 4.7), 0.8, 0.8);
            dc.DrawRoundedRectangle(null, fine, new Rect(79, 38.0, 8.0, 4.7), 0.8, 0.8);
        }

        private static void DrawGenericDevice(DrawingContext dc, Pen pen)
        {
            if (_genericDeviceBody == null)
            {
                _genericDeviceBody = new RectangleGeometry(new Rect(13, 14, 74, 38), 7, 7);
                if (_genericDeviceBody.CanFreeze) _genericDeviceBody.Freeze();
            }

            var fine = CreateFinePen(pen.Brush, pen.Thickness);
            dc.DrawGeometry(null, pen, _genericDeviceBody);

            // Neutral hardware/module language: clearly a device, deliberately not a controller.
            dc.DrawRoundedRectangle(null, fine, new Rect(22, 22, 38, 14), 3, 3);
            Circle(dc, fine, 71, 26, 2.1);
            Circle(dc, fine, 78, 26, 2.1);
            Line(dc, fine, 24, 44, 46, 44);
            Line(dc, fine, 53, 44, 61, 44);
            dc.DrawRoundedRectangle(null, fine, new Rect(69, 40.5, 10, 7), 1.2, 1.2);
        }

        private static void DrawControllerBody(DrawingContext dc, Pen pen, string data)
        {
            var geometry = Geometry.Parse(data);
            if (geometry.CanFreeze) geometry.Freeze();
            dc.DrawGeometry(null, pen, geometry);
        }

        private static void DrawDPad(DrawingContext dc, Pen pen, double cx, double cy, double size)
        {
            var half = size * 0.5;
            var arm = size * 0.18;
            var data = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "M{0},{1} L{2},{1} L{2},{3} L{4},{3} L{4},{5} L{2},{5} L{2},{6} L{0},{6} L{0},{5} L{7},{5} L{7},{3} L{0},{3} Z",
                cx - arm, cy - half,
                cx + arm, cy - arm,
                cx + half, cy + arm,
                cy + half, cx - half);

            // The generic formatter above is easy to misread; use an explicit StreamGeometry instead.
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(cx - arm, cy - half), false, true);
                ctx.LineTo(new Point(cx + arm, cy - half), true, false);
                ctx.LineTo(new Point(cx + arm, cy - arm), true, false);
                ctx.LineTo(new Point(cx + half, cy - arm), true, false);
                ctx.LineTo(new Point(cx + half, cy + arm), true, false);
                ctx.LineTo(new Point(cx + arm, cy + arm), true, false);
                ctx.LineTo(new Point(cx + arm, cy + half), true, false);
                ctx.LineTo(new Point(cx - arm, cy + half), true, false);
                ctx.LineTo(new Point(cx - arm, cy + arm), true, false);
                ctx.LineTo(new Point(cx - half, cy + arm), true, false);
                ctx.LineTo(new Point(cx - half, cy - arm), true, false);
                ctx.LineTo(new Point(cx - arm, cy - arm), true, false);
            }
            if (g.CanFreeze) g.Freeze();
            dc.DrawGeometry(null, pen, g);
        }

        private static void FaceButtons(DrawingContext dc, Pen pen, double cx, double cy, double radius, double offset)
        {
            Circle(dc, pen, cx, cy - offset, radius);
            Circle(dc, pen, cx + offset, cy, radius);
            Circle(dc, pen, cx, cy + offset, radius);
            Circle(dc, pen, cx - offset, cy, radius);
        }

        private static void DrawTriangle(DrawingContext dc, Pen pen, double cx, double cy, double radius)
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(cx, cy - radius), false, true);
                ctx.LineTo(new Point(cx + radius * 0.9, cy + radius * 0.75), true, false);
                ctx.LineTo(new Point(cx - radius * 0.9, cy + radius * 0.75), true, false);
            }
            if (g.CanFreeze) g.Freeze();
            dc.DrawGeometry(null, pen, g);
        }

        private static void DrawCross(DrawingContext dc, Pen pen, double cx, double cy, double size)
        {
            var h = size * 0.5;
            Line(dc, pen, cx - h, cy - h, cx + h, cy + h);
            Line(dc, pen, cx + h, cy - h, cx - h, cy + h);
        }

        private static void Circle(DrawingContext dc, Pen pen, double x, double y, double radius)
        {
            dc.DrawEllipse(null, pen, new Point(x, y), radius, radius);
        }

        private static void Line(DrawingContext dc, Pen pen, double x1, double y1, double x2, double y2)
        {
            dc.DrawLine(pen, new Point(x1, y1), new Point(x2, y2));
        }
    }
}
