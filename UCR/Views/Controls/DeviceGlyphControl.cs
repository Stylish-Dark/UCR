using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using HidWizards.UCR.ViewModels.Presentation;

namespace HidWizards.UCR.Views.Controls
{
    /// <summary>
    /// Compact visual identifiers for device families. At the sizes used in UCR these are
    /// deliberately solid silhouettes with only the details that survive at a glance.
    /// Colour identifies the family; the shape confirms it.
    /// </summary>
    public class DeviceGlyphControl : FrameworkElement
    {
        public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
            nameof(Kind), typeof(DeviceVisualKind), typeof(DeviceGlyphControl),
            new FrameworkPropertyMetadata(DeviceVisualKind.Unknown, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty AccentBrushProperty = DependencyProperty.Register(
            nameof(AccentBrush), typeof(Brush), typeof(DeviceGlyphControl),
            new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty SlotNumberProperty = DependencyProperty.Register(
            nameof(SlotNumber), typeof(int), typeof(DeviceGlyphControl),
            new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ShowSlotIndicatorProperty = DependencyProperty.Register(
            nameof(ShowSlotIndicator), typeof(bool), typeof(DeviceGlyphControl),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

        private static readonly Brush DetailDark = Solid(31, 33, 36);
        private static readonly Brush DetailMid = Solid(79, 82, 87);
        private static readonly Brush DetailLight = Solid(235, 237, 240);
        private static readonly Brush XboxA = Solid(65, 177, 83);
        private static readonly Brush XboxB = Solid(218, 72, 68);
        private static readonly Brush XboxX = Solid(74, 139, 226);
        private static readonly Brush XboxY = Solid(236, 190, 65);
        private static readonly Brush PsTriangle = Solid(86, 190, 129);
        private static readonly Brush PsCircle = Solid(231, 96, 107);
        private static readonly Brush PsCross = Solid(104, 149, 231);
        private static readonly Brush PsSquare = Solid(218, 113, 181);

        public DeviceVisualKind Kind
        {
            get => (DeviceVisualKind)GetValue(KindProperty);
            set => SetValue(KindProperty, value);
        }

        public Brush AccentBrush
        {
            get => (Brush)GetValue(AccentBrushProperty);
            set => SetValue(AccentBrushProperty, value);
        }

        public int SlotNumber
        {
            get => (int)GetValue(SlotNumberProperty);
            set => SetValue(SlotNumberProperty, value);
        }

        public bool ShowSlotIndicator
        {
            get => (bool)GetValue(ShowSlotIndicatorProperty);
            set => SetValue(ShowSlotIndicatorProperty, value);
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            return new Size(double.IsInfinity(availableSize.Width) ? 34 : availableSize.Width,
                double.IsInfinity(availableSize.Height) ? 24 : availableSize.Height);
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            var width = Math.Max(1, ActualWidth);
            var height = Math.Max(1, ActualHeight);
            var accent = AccentBrush ?? Brushes.Gray;

            // P1 stays implicit. P2 is shown as two devices with the first one subdued.
            // 3+ is represented by one device plus a compact numeric slot badge.
            if (ShowSlotIndicator && SlotNumber == 2)
            {
                var gap = Math.Max(1.4, width * 0.04);
                var iconWidth = (width - gap) / 2.0;
                DrawDevice(dc, new Rect(0, height * 0.10, iconWidth, height * 0.80), Kind,
                    Solid(88, 90, 94), 0.62);
                DrawDevice(dc, new Rect(iconWidth + gap, 0, iconWidth, height), Kind, accent, 1.0);
                return;
            }

            var badgeSpace = ShowSlotIndicator && SlotNumber >= 3 ? Math.Min(11, width * 0.30) : 0;
            DrawDevice(dc, new Rect(0, 0, width - badgeSpace * 0.45, height), Kind, accent, 1.0);
            if (badgeSpace > 0)
            {
                DrawSlotBadge(dc, width - badgeSpace, 0, badgeSpace, SlotNumber);
            }
        }

        private static void DrawDevice(DrawingContext dc, Rect bounds, DeviceVisualKind kind, Brush accent, double opacity)
        {
            dc.PushOpacity(opacity);
            switch (kind)
            {
                case DeviceVisualKind.Keyboard:
                    DrawKeyboard(dc, bounds, accent);
                    break;
                case DeviceVisualKind.Mouse:
                    DrawMouse(dc, bounds, accent);
                    break;
                case DeviceVisualKind.VJoy:
                    DrawVJoy(dc, bounds, accent);
                    break;
                case DeviceVisualKind.ArcadeStick:
                    DrawArcadeStick(dc, bounds, accent);
                    break;
                case DeviceVisualKind.Xbox:
                    DrawXboxController(dc, bounds, accent);
                    break;
                case DeviceVisualKind.PlayStation:
                    DrawPlayStationController(dc, bounds, accent);
                    break;
                case DeviceVisualKind.DirectInput:
                    DrawGenericController(dc, bounds, accent);
                    break;
                case DeviceVisualKind.Unavailable:
                    DrawUnavailable(dc, bounds, accent);
                    break;
                default:
                    DrawUnknown(dc, bounds, accent);
                    break;
            }
            dc.Pop();
        }

        private static void DrawKeyboard(DrawingContext dc, Rect b, Brush accent)
        {
            var outer = new Rect(b.Left + b.Width * 0.04, b.Top + b.Height * 0.13,
                b.Width * 0.92, b.Height * 0.74);
            dc.DrawRoundedRectangle(accent, null, outer, Math.Max(2, b.Height * 0.11), Math.Max(2, b.Height * 0.11));

            var gapX = outer.Width * 0.035;
            var gapY = outer.Height * 0.10;
            var keyW = (outer.Width - gapX * 7) / 6.0;
            var keyH = outer.Height * 0.18;
            var key = DetailDark;

            for (var row = 0; row < 2; row++)
            {
                for (var col = 0; col < 6; col++)
                {
                    var x = outer.Left + gapX + col * (keyW + gapX);
                    var y = outer.Top + gapY + row * (keyH + gapY);
                    dc.DrawRoundedRectangle(key, null, new Rect(x, y, keyW, keyH), 0.6, 0.6);
                }
            }

            var bottomY = outer.Bottom - gapY - keyH * 0.86;
            dc.DrawRoundedRectangle(key, null,
                new Rect(outer.Left + outer.Width * 0.24, bottomY, outer.Width * 0.52, keyH * 0.72), 0.7, 0.7);
        }

        private static void DrawMouse(DrawingContext dc, Rect b, Brush accent)
        {
            var w = Math.Min(b.Width * 0.56, b.Height * 0.72);
            var body = new Rect(b.Left + (b.Width - w) / 2.0, b.Top + b.Height * 0.04, w, b.Height * 0.92);
            dc.DrawRoundedRectangle(accent, null, body, w * 0.46, w * 0.46);
            dc.DrawLine(NewPen(DetailDark, Math.Max(0.9, b.Height * 0.045)),
                new Point(body.Left + body.Width / 2.0, body.Top + 1),
                new Point(body.Left + body.Width / 2.0, body.Top + body.Height * 0.36));
            dc.DrawLine(NewPen(DetailDark, Math.Max(0.9, b.Height * 0.045)),
                new Point(body.Left + 1, body.Top + body.Height * 0.37),
                new Point(body.Right - 1, body.Top + body.Height * 0.37));
            dc.DrawRoundedRectangle(DetailDark, null,
                new Rect(body.Left + body.Width * 0.43, body.Top + body.Height * 0.12,
                    body.Width * 0.14, body.Height * 0.15), 1.2, 1.2);
        }

        private static void DrawXboxController(DrawingContext dc, Rect b, Brush accent)
        {
            var body = ControllerBody(b, false);
            dc.DrawGeometry(accent, NewPen(Darken(accent, 0.68), Math.Max(0.7, b.Height * 0.035)), body);

            // Offset stick layout is the quickest small-scale Xbox identifier.
            DrawStick(dc, P(b, 0.31, 0.40), b.Height * 0.074);
            DrawDPad(dc, P(b, 0.34, 0.64), b.Height * 0.085, DetailDark);
            DrawStick(dc, P(b, 0.58, 0.64), b.Height * 0.068);
            DrawXboxButtons(dc, P(b, 0.74, 0.40), b.Height * 0.044);
            dc.DrawEllipse(DetailLight, null, P(b, 0.50, 0.39), b.Height * 0.032, b.Height * 0.032);
        }

        private static void DrawPlayStationController(DrawingContext dc, Rect b, Brush accent)
        {
            var body = ControllerBody(b, true);
            dc.DrawGeometry(accent, NewPen(Darken(accent, 0.68), Math.Max(0.7, b.Height * 0.035)), body);

            DrawDPad(dc, P(b, 0.27, 0.44), b.Height * 0.082, DetailDark);
            DrawPlayStationButtons(dc, P(b, 0.73, 0.44), b.Height * 0.041);
            DrawStick(dc, P(b, 0.42, 0.66), b.Height * 0.066);
            DrawStick(dc, P(b, 0.58, 0.66), b.Height * 0.066);

            var touch = new Rect(b.Left + b.Width * 0.395, b.Top + b.Height * 0.27, b.Width * 0.21, b.Height * 0.18);
            dc.DrawRoundedRectangle(DetailDark, null, touch, 1.5, 1.5);
            dc.DrawRoundedRectangle(Tint(DetailLight, 58), null,
                new Rect(touch.Left + 1, touch.Top + 1, Math.Max(0, touch.Width - 2), Math.Max(0, touch.Height - 2)), 1, 1);
        }

        private static void DrawGenericController(DrawingContext dc, Rect b, Brush accent)
        {
            var body = ControllerBody(b, false);
            dc.DrawGeometry(accent, NewPen(Darken(accent, 0.70), Math.Max(0.7, b.Height * 0.035)), body);
            DrawDPad(dc, P(b, 0.29, 0.50), b.Height * 0.082, DetailDark);
            DrawFaceDots(dc, P(b, 0.72, 0.48), b.Height * 0.041, DetailDark);
            DrawStick(dc, P(b, 0.45, 0.65), b.Height * 0.058);
            DrawStick(dc, P(b, 0.57, 0.65), b.Height * 0.058);
        }

        private static Geometry ControllerBody(Rect b, bool playStation)
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(P(b, 0.09, playStation ? 0.39 : 0.36), true, true);
                c.BezierTo(P(b, 0.13, 0.22), P(b, 0.25, 0.17), P(b, 0.37, 0.21), true, false);
                c.BezierTo(P(b, 0.43, 0.24), P(b, 0.46, playStation ? 0.27 : 0.24), P(b, 0.50, playStation ? 0.27 : 0.25), true, false);
                c.BezierTo(P(b, 0.54, playStation ? 0.27 : 0.24), P(b, 0.57, 0.24), P(b, 0.63, 0.21), true, false);
                c.BezierTo(P(b, 0.75, 0.17), P(b, 0.87, 0.22), P(b, 0.91, playStation ? 0.39 : 0.36), true, false);
                c.BezierTo(P(b, 0.96, 0.52), P(b, 0.98, 0.70), P(b, 0.90, 0.84), true, false);
                c.BezierTo(P(b, 0.85, 0.92), P(b, 0.78, 0.86), P(b, 0.72, 0.72), true, false);
                c.BezierTo(P(b, 0.66, 0.60), P(b, 0.60, 0.58), P(b, 0.50, 0.60), true, false);
                c.BezierTo(P(b, 0.40, 0.58), P(b, 0.34, 0.60), P(b, 0.28, 0.72), true, false);
                c.BezierTo(P(b, 0.22, 0.86), P(b, 0.15, 0.92), P(b, 0.10, 0.84), true, false);
                c.BezierTo(P(b, 0.02, 0.70), P(b, 0.04, 0.52), P(b, 0.09, playStation ? 0.39 : 0.36), true, false);
            }
            g.Freeze();
            return g;
        }

        private static void DrawVJoy(DrawingContext dc, Rect b, Brush accent)
        {
            // vJoy is software, so use a compact app-style mark rather than pretending it is a
            // physical controller. The white joystick remains legible even at 18px high.
            var tile = new Rect(b.Left + b.Width * 0.16, b.Top + b.Height * 0.08,
                b.Width * 0.68, b.Height * 0.84);
            dc.DrawRoundedRectangle(accent, null, tile, Math.Max(2.2, b.Height * 0.18), Math.Max(2.2, b.Height * 0.18));

            var centerX = tile.Left + tile.Width * 0.48;
            var baseY = tile.Top + tile.Height * 0.72;
            dc.DrawRoundedRectangle(DetailLight, null,
                new Rect(tile.Left + tile.Width * 0.22, baseY, tile.Width * 0.56, tile.Height * 0.11), 1.2, 1.2);
            dc.DrawLine(NewPen(DetailLight, Math.Max(1.2, b.Height * 0.07)),
                new Point(centerX, baseY), new Point(centerX + tile.Width * 0.08, tile.Top + tile.Height * 0.34));
            dc.DrawEllipse(DetailLight, null,
                new Point(centerX + tile.Width * 0.08, tile.Top + tile.Height * 0.29),
                tile.Height * 0.10, tile.Height * 0.10);
        }

        private static void DrawArcadeStick(DrawingContext dc, Rect b, Brush accent)
        {
            var panel = new StreamGeometry();
            using (var c = panel.Open())
            {
                c.BeginFigure(P(b, 0.08, 0.43), true, true);
                c.LineTo(P(b, 0.89, 0.43), true, false);
                c.LineTo(P(b, 0.96, 0.82), true, false);
                c.LineTo(P(b, 0.03, 0.82), true, false);
            }
            panel.Freeze();
            dc.DrawGeometry(accent, NewPen(Darken(accent, 0.70), Math.Max(0.7, b.Height * 0.035)), panel);

            var stickX = b.Left + b.Width * 0.30;
            dc.DrawLine(NewPen(DetailDark, Math.Max(1.2, b.Height * 0.065)),
                new Point(stickX, b.Top + b.Height * 0.51),
                new Point(stickX + b.Width * 0.035, b.Top + b.Height * 0.22));
            dc.DrawEllipse(DetailDark, null, P(b, 0.335, 0.18), b.Height * 0.105, b.Height * 0.105);

            var r = b.Height * 0.052;
            dc.DrawEllipse(DetailLight, null, P(b, 0.62, 0.57), r, r);
            dc.DrawEllipse(DetailLight, null, P(b, 0.75, 0.54), r, r);
            dc.DrawEllipse(DetailLight, null, P(b, 0.68, 0.69), r, r);
            dc.DrawEllipse(DetailLight, null, P(b, 0.81, 0.66), r, r);
        }

        private static void DrawStick(DrawingContext dc, Point center, double radius)
        {
            dc.DrawEllipse(DetailDark, null, center, radius, radius);
            dc.DrawEllipse(Tint(DetailLight, 52), null, center, radius * 0.48, radius * 0.48);
        }

        private static void DrawDPad(DrawingContext dc, Point center, double radius, Brush brush)
        {
            var thickness = Math.Max(1.0, radius * 0.60);
            var pen = NewPen(brush, thickness);
            dc.DrawLine(pen, new Point(center.X - radius, center.Y), new Point(center.X + radius, center.Y));
            dc.DrawLine(pen, new Point(center.X, center.Y - radius), new Point(center.X, center.Y + radius));
        }

        private static void DrawXboxButtons(DrawingContext dc, Point center, double radius)
        {
            var d = radius * 1.75;
            dc.DrawEllipse(XboxY, null, new Point(center.X, center.Y - d), radius, radius);
            dc.DrawEllipse(XboxB, null, new Point(center.X + d, center.Y), radius, radius);
            dc.DrawEllipse(XboxA, NewPen(DetailDark, 0.45), new Point(center.X, center.Y + d), radius, radius);
            dc.DrawEllipse(XboxX, null, new Point(center.X - d, center.Y), radius, radius);
        }

        private static void DrawPlayStationButtons(DrawingContext dc, Point center, double radius)
        {
            var d = radius * 1.85;
            dc.DrawEllipse(PsTriangle, null, new Point(center.X, center.Y - d), radius, radius);
            dc.DrawEllipse(PsCircle, null, new Point(center.X + d, center.Y), radius, radius);
            dc.DrawEllipse(PsCross, null, new Point(center.X, center.Y + d), radius, radius);
            dc.DrawEllipse(PsSquare, null, new Point(center.X - d, center.Y), radius, radius);
        }

        private static void DrawFaceDots(DrawingContext dc, Point center, double radius, Brush brush)
        {
            var d = radius * 1.8;
            dc.DrawEllipse(brush, null, new Point(center.X, center.Y - d), radius, radius);
            dc.DrawEllipse(brush, null, new Point(center.X + d, center.Y), radius, radius);
            dc.DrawEllipse(brush, null, new Point(center.X, center.Y + d), radius, radius);
            dc.DrawEllipse(brush, null, new Point(center.X - d, center.Y), radius, radius);
        }

        private static void DrawUnknown(DrawingContext dc, Rect b, Brush accent)
        {
            var center = new Point(b.Left + b.Width / 2.0, b.Top + b.Height / 2.0);
            var radius = Math.Min(b.Width, b.Height) * 0.34;
            dc.DrawEllipse(null, NewPen(accent, Math.Max(1.0, b.Height * 0.055)), center, radius, radius);
            DrawCenteredText(dc, "?", accent,
                new Rect(center.X - radius, center.Y - radius - 0.5, radius * 2, radius * 2 + 1),
                Math.Max(8, b.Height * 0.46), FontWeights.Bold);
        }

        private static void DrawUnavailable(DrawingContext dc, Rect b, Brush accent)
        {
            var rect = new Rect(b.Left + b.Width * 0.12, b.Top + b.Height * 0.18, b.Width * 0.63, b.Height * 0.64);
            dc.DrawRoundedRectangle(null, NewPen(accent, Math.Max(1.0, b.Height * 0.05)), rect, 2.5, 2.5);
            dc.DrawEllipse(accent, null, new Point(rect.Right - b.Width * 0.08, rect.Top + b.Height * 0.10),
                Math.Max(1.2, b.Height * 0.045), Math.Max(1.2, b.Height * 0.045));

            var unavailable = Solid(220, 92, 92);
            dc.DrawLine(NewPen(unavailable, Math.Max(1.3, b.Height * 0.07)),
                new Point(b.Left + b.Width * 0.12, b.Bottom - b.Height * 0.12),
                new Point(b.Right - b.Width * 0.08, b.Top + b.Height * 0.10));
        }

        private static void DrawSlotBadge(DrawingContext dc, double x, double y, double size, int slot)
        {
            var center = new Point(x + size / 2.0, y + size / 2.0);
            dc.DrawEllipse(DetailDark, NewPen(DetailMid, 0.8), center, size / 2.0, size / 2.0);
            DrawCenteredText(dc, slot.ToString(CultureInfo.InvariantCulture), DetailLight,
                new Rect(x, y - 0.5, size, size + 1), Math.Max(6.5, size * 0.68), FontWeights.Bold);
        }

        private static Point P(Rect b, double x, double y)
        {
            return new Point(b.Left + b.Width * x, b.Top + b.Height * y);
        }

        private static Brush Tint(Brush source, byte alpha)
        {
            var solid = source as SolidColorBrush;
            var color = solid?.Color ?? Color.FromRgb(150, 150, 150);
            var brush = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
            brush.Freeze();
            return brush;
        }

        private static Brush Darken(Brush source, double multiplier)
        {
            var solid = source as SolidColorBrush;
            var color = solid?.Color ?? Color.FromRgb(150, 150, 150);
            var brush = new SolidColorBrush(Color.FromRgb(
                (byte)Math.Max(0, Math.Min(255, color.R * multiplier)),
                (byte)Math.Max(0, Math.Min(255, color.G * multiplier)),
                (byte)Math.Max(0, Math.Min(255, color.B * multiplier))));
            brush.Freeze();
            return brush;
        }

        private static Brush Solid(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        private static Pen NewPen(Brush brush, double thickness)
        {
            var pen = new Pen(brush, thickness)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };
            pen.Freeze();
            return pen;
        }

        internal static void DrawCenteredText(DrawingContext dc, string text, Brush brush, Rect rect, double fontSize, FontWeight weight)
        {
            if (string.IsNullOrEmpty(text)) return;
#pragma warning disable 618
            var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, weight, FontStretches.Normal),
                fontSize, brush);
#pragma warning restore 618
            var point = new Point(rect.Left + (rect.Width - formatted.Width) / 2.0,
                rect.Top + (rect.Height - formatted.Height) / 2.0);
            dc.DrawText(formatted, point);
        }
    }
}
