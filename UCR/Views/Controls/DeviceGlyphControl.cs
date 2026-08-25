using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using HidWizards.UCR.ViewModels.Presentation;

namespace HidWizards.UCR.Views.Controls
{
    /// <summary>
    /// Small, deliberately restrained device silhouettes used throughout UCR.
    /// These are identity marks rather than decorative illustrations: enough shape and colour
    /// to recognise a device family at a glance without turning the UI into game artwork.
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

            // P1 stays implicit. P2 is the one special visual case requested: two devices,
            // first subdued and second active. 3+ is represented by a compact number badge.
            if (ShowSlotIndicator && SlotNumber == 2)
            {
                var gap = Math.Max(1.2, width * 0.045);
                var iconWidth = (width - gap) / 2.0;
                DrawDevice(dc, new Rect(0, height * 0.08, iconWidth, height * 0.84), Kind,
                    new SolidColorBrush(Color.FromRgb(92, 92, 92)), 0.72);
                DrawDevice(dc, new Rect(iconWidth + gap, 0, iconWidth, height), Kind, accent, 1.0);
                return;
            }

            var badgeSpace = ShowSlotIndicator && SlotNumber >= 3 ? Math.Min(10, width * 0.28) : 0;
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
                default:
                    DrawUnknown(dc, bounds, accent);
                    break;
            }
            dc.Pop();
        }

        private static void DrawKeyboard(DrawingContext dc, Rect b, Brush accent)
        {
            var pen = NewPen(accent, Math.Max(1.0, b.Height * 0.055));
            var rect = new Rect(b.Left + b.Width * 0.035, b.Top + b.Height * 0.16,
                b.Width * 0.93, b.Height * 0.68);
            dc.DrawRoundedRectangle(Tint(accent, 16), pen, rect, Math.Max(2, b.Height * 0.10), Math.Max(2, b.Height * 0.10));

            var keyFill = Tint(accent, 190);
            var gapX = rect.Width * 0.055;
            var gapY = rect.Height * 0.12;
            var keyW = (rect.Width - gapX * 6) / 5.0;
            var keyH = rect.Height * 0.20;

            for (var row = 0; row < 2; row++)
            {
                for (var col = 0; col < 5; col++)
                {
                    var x = rect.Left + gapX + col * (keyW + gapX);
                    var y = rect.Top + gapY + row * (keyH + gapY);
                    dc.DrawRoundedRectangle(keyFill, null, new Rect(x, y, keyW, keyH), 0.8, 0.8);
                }
            }

            var spaceY = rect.Bottom - gapY - keyH * 0.75;
            dc.DrawRoundedRectangle(keyFill, null,
                new Rect(rect.Left + rect.Width * 0.27, spaceY, rect.Width * 0.46, keyH * 0.62), 0.8, 0.8);
        }

        private static void DrawMouse(DrawingContext dc, Rect b, Brush accent)
        {
            var pen = NewPen(accent, Math.Max(1.1, b.Height * 0.06));
            var w = Math.Min(b.Width * 0.54, b.Height * 0.69);
            var rect = new Rect(b.Left + (b.Width - w) / 2.0, b.Top + b.Height * 0.055, w, b.Height * 0.89);
            dc.DrawRoundedRectangle(Tint(accent, 14), pen, rect, w * 0.44, w * 0.44);
            dc.DrawLine(NewPen(accent, Math.Max(0.8, b.Height * 0.04)),
                new Point(rect.Left + rect.Width / 2.0, rect.Top + 0.8),
                new Point(rect.Left + rect.Width / 2.0, rect.Top + rect.Height * 0.35));
            dc.DrawLine(NewPen(accent, Math.Max(0.8, b.Height * 0.04)),
                new Point(rect.Left + 1, rect.Top + rect.Height * 0.36),
                new Point(rect.Right - 1, rect.Top + rect.Height * 0.36));
            dc.DrawRoundedRectangle(accent, null,
                new Rect(rect.Left + rect.Width * 0.44, rect.Top + rect.Height * 0.12,
                    rect.Width * 0.12, rect.Height * 0.13), 1.4, 1.4);
        }

        private static void DrawXboxController(DrawingContext dc, Rect b, Brush accent)
        {
            var body = ControllerBody(b, false);
            dc.DrawGeometry(Tint(accent, 18), NewPen(accent, Math.Max(1.05, b.Height * 0.055)), body);

            // Xbox layout: offset sticks, d-pad low-left, face cluster high-right.
            var leftStick = P(b, 0.32, 0.39);
            var rightStick = P(b, 0.58, 0.61);
            var stickRadius = Math.Max(1.1, b.Height * 0.065);
            dc.DrawEllipse(null, NewPen(accent, Math.Max(0.75, b.Height * 0.036)), leftStick, stickRadius, stickRadius);
            dc.DrawEllipse(null, NewPen(accent, Math.Max(0.75, b.Height * 0.036)), rightStick, stickRadius, stickRadius);

            DrawDPad(dc, P(b, 0.34, 0.62), b.Height * 0.085, accent);
            DrawFaceCluster(dc, P(b, 0.73, 0.39), b.Height * 0.052, accent);
            dc.DrawEllipse(Tint(accent, 160), null, P(b, 0.50, 0.39), b.Height * 0.035, b.Height * 0.035);
        }

        private static void DrawPlayStationController(DrawingContext dc, Rect b, Brush accent)
        {
            var body = ControllerBody(b, true);
            dc.DrawGeometry(Tint(accent, 18), NewPen(accent, Math.Max(1.05, b.Height * 0.055)), body);

            // DualShock-style layout: symmetrical sticks and a clear central touch-pad.
            DrawDPad(dc, P(b, 0.27, 0.43), b.Height * 0.085, accent);
            DrawFaceCluster(dc, P(b, 0.73, 0.43), b.Height * 0.052, accent);

            var stickRadius = Math.Max(1.05, b.Height * 0.058);
            dc.DrawEllipse(null, NewPen(accent, Math.Max(0.75, b.Height * 0.035)), P(b, 0.42, 0.64), stickRadius, stickRadius);
            dc.DrawEllipse(null, NewPen(accent, Math.Max(0.75, b.Height * 0.035)), P(b, 0.58, 0.64), stickRadius, stickRadius);

            var touch = new Rect(b.Left + b.Width * 0.405, b.Top + b.Height * 0.28, b.Width * 0.19, b.Height * 0.18);
            dc.DrawRoundedRectangle(null, NewPen(accent, Math.Max(0.65, b.Height * 0.03)), touch, 1.5, 1.5);
        }

        private static void DrawGenericController(DrawingContext dc, Rect b, Brush accent)
        {
            var body = ControllerBody(b, false);
            dc.DrawGeometry(Tint(accent, 12), NewPen(accent, Math.Max(1.0, b.Height * 0.052)), body);
            DrawDPad(dc, P(b, 0.29, 0.48), b.Height * 0.082, accent);
            DrawFaceCluster(dc, P(b, 0.72, 0.48), b.Height * 0.048, accent);
            dc.DrawEllipse(null, NewPen(accent, Math.Max(0.7, b.Height * 0.032)), P(b, 0.45, 0.62), b.Height * 0.05, b.Height * 0.05);
            dc.DrawEllipse(null, NewPen(accent, Math.Max(0.7, b.Height * 0.032)), P(b, 0.57, 0.62), b.Height * 0.05, b.Height * 0.05);
        }

        private static Geometry ControllerBody(Rect b, bool playStation)
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(P(b, 0.08, 0.37), true, true);
                c.BezierTo(P(b, 0.12, 0.23), P(b, 0.24, 0.18), P(b, 0.36, playStation ? 0.22 : 0.20), true, false);
                c.BezierTo(P(b, 0.43, playStation ? 0.25 : 0.22), P(b, 0.46, 0.27), P(b, 0.50, 0.27), true, false);
                c.BezierTo(P(b, 0.54, 0.27), P(b, 0.57, playStation ? 0.25 : 0.22), P(b, 0.64, playStation ? 0.22 : 0.20), true, false);
                c.BezierTo(P(b, 0.76, 0.18), P(b, 0.88, 0.23), P(b, 0.92, 0.37), true, false);
                c.BezierTo(P(b, 0.96, 0.49), P(b, 0.99, 0.67), P(b, 0.91, 0.82), true, false);
                c.BezierTo(P(b, 0.86, 0.91), P(b, 0.79, 0.85), P(b, 0.73, 0.72), true, false);
                c.BezierTo(P(b, 0.67, 0.60), P(b, 0.61, 0.58), P(b, 0.50, 0.59), true, false);
                c.BezierTo(P(b, 0.39, 0.58), P(b, 0.33, 0.60), P(b, 0.27, 0.72), true, false);
                c.BezierTo(P(b, 0.21, 0.85), P(b, 0.14, 0.91), P(b, 0.09, 0.82), true, false);
                c.BezierTo(P(b, 0.01, 0.67), P(b, 0.04, 0.49), P(b, 0.08, 0.37), true, false);
            }
            g.Freeze();
            return g;
        }

        private static void DrawVJoy(DrawingContext dc, Rect b, Brush accent)
        {
            // A classic virtual-joystick silhouette: round pedestal rather than an arcade panel.
            var pen = NewPen(accent, Math.Max(1.1, b.Height * 0.055));
            var baseRect = new Rect(b.Left + b.Width * 0.14, b.Top + b.Height * 0.62, b.Width * 0.72, b.Height * 0.22);
            dc.DrawEllipse(Tint(accent, 16), pen,
                new Point(baseRect.Left + baseRect.Width / 2.0, baseRect.Top + baseRect.Height / 2.0),
                baseRect.Width / 2.0, baseRect.Height / 2.0);

            var pivot = P(b, 0.50, 0.66);
            var top = P(b, 0.55, 0.24);
            dc.DrawLine(NewPen(accent, Math.Max(1.25, b.Height * 0.065)), pivot, top);
            dc.DrawEllipse(accent, null, P(b, 0.55, 0.19), b.Height * 0.115, b.Height * 0.115);
            dc.DrawEllipse(Tint(accent, 110), null, P(b, 0.50, 0.71), b.Height * 0.045, b.Height * 0.045);
        }

        private static void DrawArcadeStick(DrawingContext dc, Rect b, Brush accent)
        {
            var pen = NewPen(accent, Math.Max(1.05, b.Height * 0.052));
            var panel = new StreamGeometry();
            using (var c = panel.Open())
            {
                c.BeginFigure(P(b, 0.08, 0.48), true, true);
                c.LineTo(P(b, 0.88, 0.48), true, false);
                c.LineTo(P(b, 0.96, 0.80), true, false);
                c.LineTo(P(b, 0.03, 0.80), true, false);
            }
            panel.Freeze();
            dc.DrawGeometry(Tint(accent, 16), pen, panel);

            var stickX = b.Left + b.Width * 0.30;
            dc.DrawLine(NewPen(accent, Math.Max(1.15, b.Height * 0.06)),
                new Point(stickX, b.Top + b.Height * 0.53), new Point(stickX + b.Width * 0.035, b.Top + b.Height * 0.25));
            dc.DrawEllipse(accent, null, P(b, 0.335, 0.20), b.Height * 0.105, b.Height * 0.105);

            var r = b.Height * 0.052;
            dc.DrawEllipse(accent, null, P(b, 0.62, 0.60), r, r);
            dc.DrawEllipse(accent, null, P(b, 0.75, 0.57), r, r);
            dc.DrawEllipse(accent, null, P(b, 0.68, 0.70), r, r);
            dc.DrawEllipse(accent, null, P(b, 0.81, 0.67), r, r);
        }

        private static void DrawDPad(DrawingContext dc, Point center, double radius, Brush accent)
        {
            var pen = NewPen(accent, Math.Max(0.8, radius * 0.34));
            dc.DrawLine(pen, new Point(center.X - radius, center.Y), new Point(center.X + radius, center.Y));
            dc.DrawLine(pen, new Point(center.X, center.Y - radius), new Point(center.X, center.Y + radius));
        }

        private static void DrawFaceCluster(DrawingContext dc, Point center, double radius, Brush accent)
        {
            var d = radius * 1.7;
            dc.DrawEllipse(accent, null, new Point(center.X, center.Y - d), radius, radius);
            dc.DrawEllipse(accent, null, new Point(center.X + d, center.Y), radius, radius);
            dc.DrawEllipse(accent, null, new Point(center.X, center.Y + d), radius, radius);
            dc.DrawEllipse(accent, null, new Point(center.X - d, center.Y), radius, radius);
        }

        private static void DrawUnknown(DrawingContext dc, Rect b, Brush accent)
        {
            var pen = NewPen(accent, Math.Max(1.0, b.Height * 0.05));
            var rect = new Rect(b.Left + b.Width * 0.12, b.Top + b.Height * 0.17, b.Width * 0.76, b.Height * 0.66);
            dc.DrawRoundedRectangle(Tint(accent, 10), pen, rect, 3, 3);
            DrawCenteredText(dc, "?", accent, rect, Math.Max(8, b.Height * 0.48), FontWeights.SemiBold);
        }

        private static void DrawSlotBadge(DrawingContext dc, double x, double y, double size, int slot)
        {
            var background = new SolidColorBrush(Color.FromRgb(48, 48, 48));
            var foreground = Brushes.White;
            var center = new Point(x + size / 2.0, y + size / 2.0);
            dc.DrawEllipse(background, NewPen(new SolidColorBrush(Color.FromRgb(120, 120, 120)), 0.8), center, size / 2.0, size / 2.0);
            DrawCenteredText(dc, slot.ToString(CultureInfo.InvariantCulture), foreground,
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
