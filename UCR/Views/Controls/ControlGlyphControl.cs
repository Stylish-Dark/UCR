using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using HidWizards.UCR.ViewModels.Presentation;

namespace HidWizards.UCR.Views.Controls
{
    public class ControlGlyphControl : FrameworkElement
    {
        public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
            nameof(Kind), typeof(ControlVisualKind), typeof(ControlGlyphControl),
            new FrameworkPropertyMetadata(ControlVisualKind.Unknown, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty AccentBrushProperty = DependencyProperty.Register(
            nameof(AccentBrush), typeof(Brush), typeof(ControlGlyphControl),
            new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
            nameof(Label), typeof(string), typeof(ControlGlyphControl),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

        public ControlVisualKind Kind
        {
            get => (ControlVisualKind)GetValue(KindProperty);
            set => SetValue(KindProperty, value);
        }

        public Brush AccentBrush
        {
            get => (Brush)GetValue(AccentBrushProperty);
            set => SetValue(AccentBrushProperty, value);
        }

        public string Label
        {
            get => (string)GetValue(LabelProperty);
            set => SetValue(LabelProperty, value);
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            return new Size(double.IsInfinity(availableSize.Width) ? 34 : availableSize.Width,
                double.IsInfinity(availableSize.Height) ? 24 : availableSize.Height);
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            var b = new Rect(0, 0, Math.Max(1, ActualWidth), Math.Max(1, ActualHeight));
            var accent = AccentBrush ?? Brushes.Gray;
            switch (Kind)
            {
                case ControlVisualKind.Key:
                    DrawKey(dc, b, accent, Label);
                    break;
                case ControlVisualKind.MouseButton:
                    DrawMouseButton(dc, b, accent, Label);
                    break;
                case ControlVisualKind.XboxFaceButton:
                    DrawFaceButton(dc, b, accent, Label, true);
                    break;
                case ControlVisualKind.PlayStationFaceButton:
                    DrawFaceButton(dc, b, accent, Label, false);
                    break;
                case ControlVisualKind.DPad:
                    DrawDPad(dc, b, accent, Label);
                    break;
                case ControlVisualKind.ShoulderButton:
                case ControlVisualKind.Trigger:
                    DrawPill(dc, b, accent, Label, Kind == ControlVisualKind.Trigger);
                    break;
                case ControlVisualKind.StickAxis:
                    DrawStickAxis(dc, b, accent, Label);
                    break;
                case ControlVisualKind.Axis:
                    DrawAxis(dc, b, accent, Label);
                    break;
                case ControlVisualKind.Button:
                    DrawGenericButton(dc, b, accent, Label);
                    break;
                case ControlVisualKind.Filter:
                    DrawFilter(dc, b, accent);
                    break;
                case ControlVisualKind.DeviceUnavailable:
                    DrawDeviceUnavailable(dc, b, accent);
                    break;
                case ControlVisualKind.Unbound:
                    DrawUnbound(dc, b, accent);
                    break;
                case ControlVisualKind.Unknown:
                    DrawUnknown(dc, b, accent);
                    break;
                default:
                    DrawGenericButton(dc, b, accent, string.IsNullOrEmpty(Label) ? "?" : Label);
                    break;
            }
        }

        private static void DrawKey(DrawingContext dc, Rect b, Brush accent, string label)
        {
            var rect = new Rect(b.Left + 1, b.Top + 2, Math.Max(1, b.Width - 2), Math.Max(1, b.Height - 4));
            var fill = new SolidColorBrush(Color.FromRgb(48, 48, 48));
            dc.DrawRoundedRectangle(fill, Pen(accent, 1.1), rect, 3, 3);
            DeviceGlyphControl.DrawCenteredText(dc, label, Brushes.White, rect, FontFor(label, b.Height * 0.48), FontWeights.SemiBold);
        }

        private static void DrawMouseButton(DrawingContext dc, Rect b, Brush accent, string label)
        {
            var w = Math.Min(b.Width * 0.62, b.Height * 0.68);
            var mouse = new Rect(b.Left + 1, b.Top + b.Height * 0.12, w, b.Height * 0.76);
            dc.DrawRoundedRectangle(null, Pen(accent, 1.55), mouse, w * 0.4, w * 0.4);
            dc.DrawLine(Pen(accent, 1.35), new Point(mouse.Left + mouse.Width / 2.0, mouse.Top), new Point(mouse.Left + mouse.Width / 2.0, mouse.Top + mouse.Height * 0.36));
            dc.DrawLine(Pen(accent, 1.35), new Point(mouse.Left, mouse.Top + mouse.Height * 0.37), new Point(mouse.Right, mouse.Top + mouse.Height * 0.37));
            var labelRect = new Rect(mouse.Right + 2, b.Top, Math.Max(1, b.Right - mouse.Right - 2), b.Height);
            DeviceGlyphControl.DrawCenteredText(dc, label, accent, labelRect, FontFor(label, b.Height * 0.46), FontWeights.Bold);
        }

        private static void DrawFaceButton(DrawingContext dc, Rect b, Brush accent, string label, bool filled)
        {
            var radius = Math.Min(b.Width, b.Height) * 0.39;
            var center = new Point(b.Left + b.Width / 2.0, b.Top + b.Height / 2.0);
            if (filled)
            {
                dc.DrawEllipse(accent, null, center, radius, radius);
                DeviceGlyphControl.DrawCenteredText(dc, label, Brushes.White,
                    new Rect(center.X - radius, center.Y - radius, radius * 2, radius * 2),
                    FontFor(label, radius * 1.12), FontWeights.Bold);
            }
            else
            {
                dc.DrawEllipse(null, Pen(accent, 1.9), center, radius, radius);
                DeviceGlyphControl.DrawCenteredText(dc, label, accent,
                    new Rect(center.X - radius, center.Y - radius, radius * 2, radius * 2),
                    FontFor(label, radius * 1.15), FontWeights.Bold);
            }
        }

        private static void DrawDPad(DrawingContext dc, Rect b, Brush accent, string label)
        {
            var cx = b.Left + b.Width * 0.42;
            var cy = b.Top + b.Height * 0.5;
            var r = Math.Min(b.Width, b.Height) * 0.28;
            var pen = Pen(accent, 1.55);
            dc.DrawLine(pen, new Point(cx - r, cy), new Point(cx + r, cy));
            dc.DrawLine(pen, new Point(cx, cy - r), new Point(cx, cy + r));
            var labelRect = new Rect(b.Left + b.Width * 0.62, b.Top, b.Width * 0.38, b.Height);
            DeviceGlyphControl.DrawCenteredText(dc, label, accent, labelRect, FontFor(label, b.Height * 0.52), FontWeights.Bold);
        }

        private static void DrawPill(DrawingContext dc, Rect b, Brush accent, string label, bool stronger)
        {
            var rect = new Rect(b.Left + 1, b.Top + b.Height * 0.18, Math.Max(1, b.Width - 2), b.Height * 0.64);
            var fill = stronger ? new SolidColorBrush(Color.FromArgb(42, 255, 255, 255)) : null;
            dc.DrawRoundedRectangle(fill, Pen(accent, 1.55), rect, rect.Height * 0.32, rect.Height * 0.32);
            DeviceGlyphControl.DrawCenteredText(dc, label, accent, rect, FontFor(label, b.Height * 0.44), FontWeights.Bold);
        }

        private static void DrawStickAxis(DrawingContext dc, Rect b, Brush accent, string label)
        {
            var center = new Point(b.Left + b.Width * 0.28, b.Top + b.Height * 0.5);
            var radius = Math.Min(b.Width, b.Height) * 0.19;
            dc.DrawEllipse(null, Pen(accent, 1.5), center, radius, radius);
            dc.DrawLine(Pen(accent, 1.4), new Point(center.X - radius * 0.7, center.Y), new Point(center.X + radius * 0.7, center.Y));
            dc.DrawLine(Pen(accent, 1.4), new Point(center.X, center.Y - radius * 0.7), new Point(center.X, center.Y + radius * 0.7));
            DeviceGlyphControl.DrawCenteredText(dc, label, accent,
                new Rect(b.Left + b.Width * 0.48, b.Top, b.Width * 0.52, b.Height),
                FontFor(label, b.Height * 0.45), FontWeights.Bold);
        }

        private static void DrawAxis(DrawingContext dc, Rect b, Brush accent, string label)
        {
            var y = b.Top + b.Height * 0.5;
            var x1 = b.Left + 2;
            var x2 = b.Left + b.Width * 0.43;
            dc.DrawLine(Pen(accent, 1.55), new Point(x1, y), new Point(x2, y));
            dc.DrawLine(Pen(accent, 1.4), new Point(x1, y), new Point(x1 + 3, y - 3));
            dc.DrawLine(Pen(accent, 1.4), new Point(x1, y), new Point(x1 + 3, y + 3));
            dc.DrawLine(Pen(accent, 1.4), new Point(x2, y), new Point(x2 - 3, y - 3));
            dc.DrawLine(Pen(accent, 1.4), new Point(x2, y), new Point(x2 - 3, y + 3));
            DeviceGlyphControl.DrawCenteredText(dc, label, accent,
                new Rect(b.Left + b.Width * 0.48, b.Top, b.Width * 0.52, b.Height),
                FontFor(label, b.Height * 0.44), FontWeights.Bold);
        }

        private static void DrawGenericButton(DrawingContext dc, Rect b, Brush accent, string label)
        {
            var radius = Math.Min(b.Width, b.Height) * 0.35;
            var center = new Point(b.Left + b.Width / 2.0, b.Top + b.Height / 2.0);
            dc.DrawEllipse(null, Pen(accent, 1.55), center, radius, radius);
            DeviceGlyphControl.DrawCenteredText(dc, label, accent,
                new Rect(center.X - radius, center.Y - radius, radius * 2, radius * 2),
                FontFor(label, radius), FontWeights.Bold);
        }

        private static void DrawFilter(DrawingContext dc, Rect b, Brush accent)
        {
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(new Point(b.Left + b.Width * 0.16, b.Top + b.Height * 0.22), false, false);
                ctx.LineTo(new Point(b.Right - b.Width * 0.16, b.Top + b.Height * 0.22), true, false);
                ctx.LineTo(new Point(b.Left + b.Width * 0.58, b.Top + b.Height * 0.52), true, false);
                ctx.LineTo(new Point(b.Left + b.Width * 0.58, b.Top + b.Height * 0.78), true, false);
                ctx.LineTo(new Point(b.Left + b.Width * 0.42, b.Top + b.Height * 0.86), true, false);
                ctx.LineTo(new Point(b.Left + b.Width * 0.42, b.Top + b.Height * 0.52), true, false);
                ctx.LineTo(new Point(b.Left + b.Width * 0.16, b.Top + b.Height * 0.22), true, false);
            }
            geometry.Freeze();
            dc.DrawGeometry(accent, null, geometry);
        }


        private static void DrawUnknown(DrawingContext dc, Rect b, Brush accent)
        {
            var size = Math.Min(b.Width, b.Height) * 0.68;
            var center = new Point(b.Left + b.Width / 2.0, b.Top + b.Height / 2.0);
            var half = size / 2.0;
            var diamond = new StreamGeometry();
            using (var ctx = diamond.Open())
            {
                ctx.BeginFigure(new Point(center.X, center.Y - half), false, true);
                ctx.LineTo(new Point(center.X + half, center.Y), true, false);
                ctx.LineTo(new Point(center.X, center.Y + half), true, false);
                ctx.LineTo(new Point(center.X - half, center.Y), true, false);
            }
            diamond.Freeze();
            dc.DrawGeometry(null, Pen(accent, 1.45), diamond);
            DeviceGlyphControl.DrawCenteredText(dc, "?", accent,
                new Rect(center.X - half, center.Y - half - 0.5, size, size + 1),
                Math.Max(8, b.Height * 0.48), FontWeights.Bold);
        }

        private static void DrawDeviceUnavailable(DrawingContext dc, Rect b, Brush accent)
        {
            var device = new Rect(b.Left + b.Width * 0.12, b.Top + b.Height * 0.22,
                b.Width * 0.48, b.Height * 0.56);
            dc.DrawRoundedRectangle(null, Pen(accent, 1.35), device, 2.2, 2.2);

            var cableY = b.Top + b.Height * 0.5;
            var cableStart = device.Right + b.Width * 0.05;
            dc.DrawLine(Pen(accent, 1.35), new Point(cableStart, cableY),
                new Point(b.Left + b.Width * 0.78, cableY));
            dc.DrawLine(Pen(accent, 1.2), new Point(b.Left + b.Width * 0.78, cableY - 3),
                new Point(b.Left + b.Width * 0.78, cableY + 3));
            dc.DrawLine(Pen(accent, 1.2), new Point(b.Left + b.Width * 0.84, cableY - 3),
                new Point(b.Left + b.Width * 0.84, cableY + 3));

            var unavailable = new SolidColorBrush(Color.FromRgb(220, 92, 92));
            unavailable.Freeze();
            dc.DrawLine(Pen(unavailable, 1.8),
                new Point(b.Left + b.Width * 0.12, b.Bottom - b.Height * 0.12),
                new Point(b.Right - b.Width * 0.08, b.Top + b.Height * 0.12));
        }

        private static void DrawUnbound(DrawingContext dc, Rect b, Brush accent)
        {
            var rect = new Rect(b.Left + 2, b.Top + 3, Math.Max(1, b.Width - 4), Math.Max(1, b.Height - 6));
            var pen = Pen(new SolidColorBrush(Color.FromRgb(105, 105, 105)), 1.0);
            dc.DrawRoundedRectangle(null, pen, rect, 3, 3);
            DeviceGlyphControl.DrawCenteredText(dc, "?", new SolidColorBrush(Color.FromRgb(160, 160, 160)), rect, b.Height * 0.48, FontWeights.SemiBold);
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

        private static double FontFor(string label, double normal)
        {
            if (string.IsNullOrEmpty(label)) return normal;
            if (label.Length >= 4) return normal * 0.72;
            if (label.Length == 3) return normal * 0.84;
            return normal;
        }
    }
}
