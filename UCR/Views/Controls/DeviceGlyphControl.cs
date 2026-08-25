using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using HidWizards.UCR.ViewModels.Presentation;

namespace HidWizards.UCR.Views.Controls
{
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

            if (ShowSlotIndicator && SlotNumber == 2)
            {
                var gap = Math.Max(1.5, width * 0.05);
                var iconWidth = (width - gap) / 2.0;
                DrawDevice(dc, new Rect(0, height * 0.08, iconWidth, height * 0.84), Kind, new SolidColorBrush(Color.FromRgb(92, 92, 92)), 0.82);
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
                    DrawJoystick(dc, bounds, accent, false);
                    break;
                case DeviceVisualKind.ArcadeStick:
                    DrawJoystick(dc, bounds, accent, true);
                    break;
                case DeviceVisualKind.Xbox:
                case DeviceVisualKind.PlayStation:
                case DeviceVisualKind.DirectInput:
                    DrawGamepad(dc, bounds, accent, kind);
                    break;
                default:
                    DrawUnknown(dc, bounds, accent);
                    break;
            }
            dc.Pop();
        }

        private static void DrawKeyboard(DrawingContext dc, Rect b, Brush accent)
        {
            var pen = NewPen(accent, Math.Max(1.2, b.Height * 0.07));
            var rect = Inset(b, b.Width * 0.04, b.Height * 0.18);
            dc.DrawRoundedRectangle(null, pen, rect, Math.Max(2, b.Height * 0.12), Math.Max(2, b.Height * 0.12));
            var keyPen = NewPen(accent, Math.Max(0.8, b.Height * 0.045));
            for (var row = 0; row < 2; row++)
            {
                for (var col = 0; col < 4; col++)
                {
                    var x = rect.Left + rect.Width * (0.13 + col * 0.23);
                    var y = rect.Top + rect.Height * (0.32 + row * 0.34);
                    dc.DrawLine(keyPen, new Point(x - rect.Width * 0.055, y), new Point(x + rect.Width * 0.055, y));
                }
            }
        }

        private static void DrawMouse(DrawingContext dc, Rect b, Brush accent)
        {
            var pen = NewPen(accent, Math.Max(1.2, b.Height * 0.065));
            var w = Math.Min(b.Width * 0.58, b.Height * 0.72);
            var rect = new Rect(b.Left + (b.Width - w) / 2.0, b.Top + b.Height * 0.06, w, b.Height * 0.88);
            dc.DrawRoundedRectangle(null, pen, rect, w * 0.42, w * 0.42);
            dc.DrawLine(pen, new Point(rect.Left + rect.Width / 2.0, rect.Top), new Point(rect.Left + rect.Width / 2.0, rect.Top + rect.Height * 0.37));
            dc.DrawLine(pen, new Point(rect.Left, rect.Top + rect.Height * 0.38), new Point(rect.Right, rect.Top + rect.Height * 0.38));
            dc.DrawRoundedRectangle(accent, null,
                new Rect(rect.Left + rect.Width * 0.43, rect.Top + rect.Height * 0.12, rect.Width * 0.14, rect.Height * 0.14), 1.5, 1.5);
        }

        private static void DrawGamepad(DrawingContext dc, Rect b, Brush accent, DeviceVisualKind kind)
        {
            var pen = NewPen(accent, Math.Max(1.15, b.Height * 0.065));
            var body = new Rect(b.Left + b.Width * 0.06, b.Top + b.Height * 0.18, b.Width * 0.88, b.Height * 0.62);
            dc.DrawRoundedRectangle(null, pen, body, body.Height * 0.34, body.Height * 0.34);

            var leftX = body.Left + body.Width * 0.27;
            var centerY = body.Top + body.Height * 0.5;
            var d = body.Height * 0.22;
            dc.DrawLine(pen, new Point(leftX - d, centerY), new Point(leftX + d, centerY));
            dc.DrawLine(pen, new Point(leftX, centerY - d), new Point(leftX, centerY + d));

            var rightX = body.Left + body.Width * 0.72;
            var dotRadius = Math.Max(1.3, body.Height * 0.075);
            dc.DrawEllipse(accent, null, new Point(rightX, centerY - body.Height * 0.12), dotRadius, dotRadius);
            dc.DrawEllipse(accent, null, new Point(rightX + body.Width * 0.09, centerY), dotRadius, dotRadius);
            dc.DrawEllipse(accent, null, new Point(rightX, centerY + body.Height * 0.12), dotRadius, dotRadius);
            dc.DrawEllipse(accent, null, new Point(rightX - body.Width * 0.09, centerY), dotRadius, dotRadius);

            if (kind == DeviceVisualKind.PlayStation)
            {
                dc.DrawLine(NewPen(accent, Math.Max(0.9, b.Height * 0.045)),
                    new Point(body.Left + body.Width * 0.43, body.Top + body.Height * 0.27),
                    new Point(body.Left + body.Width * 0.57, body.Top + body.Height * 0.27));
            }
        }

        private static void DrawJoystick(DrawingContext dc, Rect b, Brush accent, bool arcade)
        {
            var pen = NewPen(accent, Math.Max(1.2, b.Height * 0.065));
            var baseRect = new Rect(b.Left + b.Width * 0.08, b.Top + b.Height * 0.61, b.Width * 0.84, b.Height * 0.24);
            dc.DrawRoundedRectangle(null, pen, baseRect, 2, 2);

            var pivotX = b.Left + b.Width * (arcade ? 0.36 : 0.5);
            var pivotY = baseRect.Top;
            var topY = b.Top + b.Height * 0.2;
            dc.DrawLine(pen, new Point(pivotX, pivotY), new Point(pivotX + b.Width * 0.04, topY + b.Height * 0.08));
            dc.DrawEllipse(accent, null, new Point(pivotX + b.Width * 0.04, topY), b.Height * 0.11, b.Height * 0.11);

            if (arcade)
            {
                dc.DrawEllipse(accent, null, new Point(baseRect.Left + baseRect.Width * 0.68, baseRect.Top + baseRect.Height * 0.45), b.Height * 0.055, b.Height * 0.055);
                dc.DrawEllipse(accent, null, new Point(baseRect.Left + baseRect.Width * 0.82, baseRect.Top + baseRect.Height * 0.45), b.Height * 0.055, b.Height * 0.055);
            }
        }

        private static void DrawUnknown(DrawingContext dc, Rect b, Brush accent)
        {
            var pen = NewPen(accent, Math.Max(1.1, b.Height * 0.06));
            var rect = Inset(b, b.Width * 0.16, b.Height * 0.15);
            dc.DrawRoundedRectangle(null, pen, rect, 3, 3);
            DrawCenteredText(dc, "?", accent, rect, Math.Max(8, b.Height * 0.52), FontWeights.SemiBold);
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

        private static Pen NewPen(Brush brush, double thickness)
        {
            var pen = new Pen(brush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            pen.Freeze();
            return pen;
        }

        private static Rect Inset(Rect rect, double x, double y)
        {
            return new Rect(rect.Left + x, rect.Top + y, Math.Max(1, rect.Width - 2 * x), Math.Max(1, rect.Height - 2 * y));
        }

        internal static void DrawCenteredText(DrawingContext dc, string text, Brush brush, Rect rect, double fontSize, FontWeight weight)
        {
            if (string.IsNullOrEmpty(text)) return;
#pragma warning disable 618
            var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, weight, FontStretches.Normal),
                fontSize, brush);
#pragma warning restore 618
            var point = new Point(rect.Left + (rect.Width - formatted.Width) / 2.0, rect.Top + (rect.Height - formatted.Height) / 2.0);
            dc.DrawText(formatted, point);
        }
    }
}
