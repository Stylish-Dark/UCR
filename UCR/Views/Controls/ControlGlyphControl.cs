using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using HidWizards.UCR.ViewModels.Presentation;

namespace HidWizards.UCR.Views.Controls
{
    public class ControlGlyphControl : FrameworkElement
    {
        private static readonly FontFamily TextFontFamily = new FontFamily("Segoe UI");
        private static readonly Typeface NormalTypeface = new Typeface(
            TextFontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        private static readonly Typeface SemiboldTypeface = new Typeface(
            TextFontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        private static readonly Typeface BoldTypeface = new Typeface(
            TextFontFamily, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

        private static Typeface TextTypeface(FontWeight weight)
        {
            if (weight == FontWeights.Normal) return NormalTypeface;
            if (weight == FontWeights.SemiBold) return SemiboldTypeface;
            if (weight == FontWeights.Bold) return BoldTypeface;
            return new Typeface(TextFontFamily, FontStyles.Normal, weight, FontStretches.Normal);
        }

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
                case ControlVisualKind.CompoundKeys:
                    DrawCompoundKeys(dc, b, accent, Label, false);
                    break;
                case ControlVisualKind.CompoundFaceButtons:
                    DrawCompoundKeys(dc, b, accent, Label, true);
                    break;
                case ControlVisualKind.CompoundStick:
                    DrawCompoundStick(dc, b, accent, Label);
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
                    // No synthetic warning icon for a temporarily unavailable device.
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
            // Keycap geometry is invariant, including in zoomed or grouped previews.
            var side = Math.Max(1, Math.Min(b.Width - 2, b.Height - 4));
            var rect = new Rect(b.Left + (b.Width - side) / 2.0,
                b.Top + (b.Height - side) / 2.0, side, side);
            // Keyboard caps keep a neutral black face; device identity belongs to the outline and legend.
            dc.DrawRoundedRectangle(Brushes.Black, Pen(accent, 1.1), rect, 1.5, 1.5);

            if (string.IsNullOrEmpty(label)) return;
            var inner = new Rect(rect.Left + 3, rect.Top + 2,
                Math.Max(1, rect.Width - 6), Math.Max(1, rect.Height - 4));
            // Old/custom "NUM 6" overrides are also given the stacked treatment.
            var display = label.StartsWith("NUM ", StringComparison.OrdinalIgnoreCase)
                ? "NUM\n" + label.Substring(4).Trim() : label;
            var lines = display.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

            dc.PushClip(new RectangleGeometry(rect));
            if (lines.Length == 2)
            {
                var top = new Rect(inner.Left, inner.Top, inner.Width, inner.Height * 0.43);
                var bottom = new Rect(inner.Left, inner.Top + inner.Height * 0.43,
                    inner.Width, inner.Height * 0.57);
                DrawFittedKeyText(dc, lines[0], accent, top,
                    inner.Height * 0.36, FontWeights.SemiBold);
                DrawFittedKeyText(dc, lines[1], accent, bottom,
                    inner.Height * 0.54, FontWeights.SemiBold);
            }
            else
            {
                // The descending tail of Q makes it appear below the centre at normal glyph sizes.
                // A small optical correction applies to Q alone, not to every keyboard legend.
                var labelArea = string.Equals(display, "Q", StringComparison.OrdinalIgnoreCase)
                    ? new Rect(inner.Left, inner.Top - 1.25, inner.Width, inner.Height)
                    : inner;
                DrawFittedKeyText(dc, display.Replace("\n", " "), accent, labelArea,
                    b.Height * (display.Length == 1 ? 0.55 : 0.48), FontWeights.SemiBold);
            }
            dc.Pop();
        }

        private static void DrawFittedKeyText(DrawingContext dc, string text, Brush brush,
            Rect rect, double preferredSize, FontWeight weight)
        {
            if (string.IsNullOrWhiteSpace(text) || rect.Width <= 0 || rect.Height <= 0) return;
#pragma warning disable 618
            var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                TextTypeface(weight),
                preferredSize, brush);
#pragma warning restore 618
            // Measure actual text, not string length. ENTER, PAUSE and numpad labels
            // must never bleed beyond the common fixed-width square.
            if (formatted.Width > 0 && formatted.Height > 0)
            {
                var ratio = Math.Min(1.0, Math.Min(
                    rect.Width * 0.94 / formatted.Width, rect.Height * 0.94 / formatted.Height));
                if (ratio < 1.0) formatted.SetFontSize(preferredSize * ratio);
            }
            dc.DrawText(formatted, new Point(
                rect.Left + (rect.Width - formatted.Width) / 2.0,
                rect.Top + (rect.Height - formatted.Height) / 2.0));
        }

        private static void DrawMouseButton(DrawingContext dc, Rect b, Brush accent, string label)
        {
            var w = Math.Min(b.Width * 0.62, b.Height * 0.68);
            var mouse = new Rect(b.Left + 1, b.Top + b.Height * 0.12, w, b.Height * 0.76);
            dc.DrawRoundedRectangle(null, Pen(accent, 1.55), mouse, w * 0.4, w * 0.4);
            dc.DrawLine(Pen(accent, 1.35), new Point(mouse.Left + mouse.Width / 2.0, mouse.Top), new Point(mouse.Left + mouse.Width / 2.0, mouse.Top + mouse.Height * 0.36));
            dc.DrawLine(Pen(accent, 1.35), new Point(mouse.Left, mouse.Top + mouse.Height * 0.37), new Point(mouse.Right, mouse.Top + mouse.Height * 0.37));
            var labelRect = new Rect(mouse.Right + 2, b.Top, Math.Max(1, b.Right - mouse.Right - 2), b.Height);
            DrawCenteredText(dc, label, accent, labelRect, FontFor(label, b.Height * 0.46), FontWeights.Bold);
        }

        private static void DrawFaceButton(DrawingContext dc, Rect b, Brush accent, string label, bool filled)
        {
            var radius = Math.Min(b.Width, b.Height) * 0.39;
            var center = new Point(b.Left + b.Width / 2.0, b.Top + b.Height / 2.0);
            if (filled)
            {
                dc.DrawEllipse(accent, null, center, radius, radius);
                DrawCenteredText(dc, label, Brushes.White,
                    new Rect(center.X - radius, center.Y - radius, radius * 2, radius * 2),
                    FontFor(label, radius * 1.12), FontWeights.Bold);
                return;
            }

            // PlayStation face buttons use a common outer ring, but draw the symbol geometry
            // directly so Cross/Square/Triangle do not depend on font glyph proportions.
            dc.DrawEllipse(null, Pen(accent, 1.9), center, radius, radius);
            var symbolPen = Pen(accent, 1.75);

            switch (label)
            {
                case "×":
                {
                    var d = radius * 0.43;
                    dc.DrawLine(symbolPen, new Point(center.X - d, center.Y - d),
                        new Point(center.X + d, center.Y + d));
                    dc.DrawLine(symbolPen, new Point(center.X + d, center.Y - d),
                        new Point(center.X - d, center.Y + d));
                    return;
                }
                case "□":
                {
                    var half = radius * 0.43;
                    dc.DrawRectangle(null, symbolPen,
                        new Rect(center.X - half, center.Y - half, half * 2, half * 2));
                    return;
                }
                case "△":
                {
                    var top = new Point(center.X, center.Y - radius * 0.50);
                    var left = new Point(center.X - radius * 0.49, center.Y + radius * 0.40);
                    var right = new Point(center.X + radius * 0.49, center.Y + radius * 0.40);
                    var triangle = new StreamGeometry();
                    using (var ctx = triangle.Open())
                    {
                        ctx.BeginFigure(top, false, true);
                        ctx.LineTo(right, true, false);
                        ctx.LineTo(left, true, false);
                    }
                    triangle.Freeze();
                    dc.DrawGeometry(null, symbolPen, triangle);
                    return;
                }
                default:
                    // Circle already reads cleanly with the existing glyph.
                    DrawCenteredText(dc, label, accent,
                        new Rect(center.X - radius, center.Y - radius, radius * 2, radius * 2),
                        FontFor(label, radius * 1.15), FontWeights.Bold);
                    return;
            }
        }

        // Four physical directions appear as ONE compound header glyph.
        // The labels encode Left|Right|Up|Down, preserving their spatial meaning.
        private static void DrawCompoundKeys(DrawingContext dc, Rect b,
            Brush accent, string labels, bool faceButtons)
        {
            var parts = (labels ?? string.Empty).Split('|');
            Func<int, string> key = index =>
                index < parts.Length && !string.IsNullOrWhiteSpace(parts[index])
                    ? parts[index] : "·";
            var centerX = b.Left + b.Width / 2.0;
            var centerY = b.Top + b.Height / 2.0;
            if (!faceButtons)
            {
                // A keyboard preset is laid out like the actual WASD keys:
                //         W
                //       A S D
                // The visual's logical channel order remains Left, Right,
                // Up, Down; only their physical keycap positions change.
                const double gap = 2.0;
                var side = Math.Max(1, Math.Min(
                    (b.Width - 8 - 2 * gap) / 3.0,
                    (b.Height - 8 - gap) / 2.0));
                var leftX = centerX - (3 * side + 2 * gap) / 2.0;
                var topY = centerY - (2 * side + gap) / 2.0;
                var bottomY = topY + side + gap;
                DrawKey(dc, new Rect(leftX, bottomY, side, side), accent, key(0));
                DrawKey(dc, new Rect(leftX + 2 * (side + gap), bottomY, side, side), accent, key(1));
                DrawKey(dc, new Rect(leftX + side + gap, topY, side, side), accent, key(2));
                DrawKey(dc, new Rect(leftX + side + gap, bottomY, side, side), accent, key(3));
                return;
            }
            // Face buttons, unlike keyboard caps, retain their diamond cross.
            var size = Math.Max(1, Math.Min((b.Width - 6) / 3.0, (b.Height - 6) / 3.0));
            var left = new Rect(centerX - size * 1.50, centerY - size * 0.50, size, size);
            var right = new Rect(centerX + size * 0.50, centerY - size * 0.50, size, size);
            var up = new Rect(centerX - size * 0.50, centerY - size * 1.50, size, size);
            var down = new Rect(centerX - size * 0.50, centerY + size * 0.50, size, size);
            DrawFaceButton(dc, left, accent, key(0), true);
            DrawFaceButton(dc, right, accent, key(1), true);
            DrawFaceButton(dc, up, accent, key(2), true);
            DrawFaceButton(dc, down, accent, key(3), true);
        }

        private static void DrawCompoundStick(DrawingContext dc, Rect b,
            Brush accent, string label)
        {
            var center = new Point(b.Left + b.Width * 0.42, b.Top + b.Height * 0.50);
            var radius = Math.Min(b.Height, b.Width) * 0.37;
            var baseBrush = new SolidColorBrush(Color.FromRgb(31, 31, 31));
            var capBrush = new SolidColorBrush(Color.FromRgb(59, 59, 59));
            dc.DrawEllipse(baseBrush, Pen(accent, 1.8), center, radius, radius);
            dc.DrawEllipse(capBrush, Pen(accent, 1.35), center, radius * 0.70, radius * 0.70);
            var span = radius * 0.36;
            dc.DrawLine(Pen(accent, 1.55),
                new Point(center.X - span, center.Y), new Point(center.X + span, center.Y));
            dc.DrawLine(Pen(accent, 1.55),
                new Point(center.X, center.Y - span), new Point(center.X, center.Y + span));
            if (!string.IsNullOrWhiteSpace(label))
                DrawCenteredText(dc, label, accent,
                    new Rect(b.Left + b.Width * 0.79, b.Top, b.Width * 0.20, b.Height),
                    b.Height * 0.36, FontWeights.Bold);
        }

        private static void DrawDPad(DrawingContext dc, Rect b, Brush accent, string label)
        {
            var size = Math.Min(b.Width, b.Height) * 0.78;
            var cx = b.Left + b.Width / 2.0;
            var cy = b.Top + b.Height / 2.0;
            var extent = size / 2.0;
            var halfArm = size * 0.17;

            var cross = new StreamGeometry();
            using (var ctx = cross.Open())
            {
                ctx.BeginFigure(new Point(cx - halfArm, cy - extent), true, true);
                ctx.LineTo(new Point(cx + halfArm, cy - extent), true, false);
                ctx.LineTo(new Point(cx + halfArm, cy - halfArm), true, false);
                ctx.LineTo(new Point(cx + extent, cy - halfArm), true, false);
                ctx.LineTo(new Point(cx + extent, cy + halfArm), true, false);
                ctx.LineTo(new Point(cx + halfArm, cy + halfArm), true, false);
                ctx.LineTo(new Point(cx + halfArm, cy + extent), true, false);
                ctx.LineTo(new Point(cx - halfArm, cy + extent), true, false);
                ctx.LineTo(new Point(cx - halfArm, cy + halfArm), true, false);
                ctx.LineTo(new Point(cx - extent, cy + halfArm), true, false);
                ctx.LineTo(new Point(cx - extent, cy - halfArm), true, false);
                ctx.LineTo(new Point(cx - halfArm, cy - halfArm), true, false);
            }
            cross.Freeze();

            var neutralFill = new SolidColorBrush(Color.FromRgb(43, 43, 43));
            var neutralStroke = new SolidColorBrush(Color.FromRgb(105, 105, 105));
            neutralFill.Freeze();
            neutralStroke.Freeze();

            dc.DrawGeometry(neutralFill, null, cross);

            var direction = (label ?? string.Empty).Trim();
            Rect active = Rect.Empty;

            if (direction == "↑" || direction.Equals("UP", StringComparison.OrdinalIgnoreCase))
                active = new Rect(cx - halfArm, cy - extent, halfArm * 2, extent - halfArm);
            else if (direction == "→" || direction.Equals("RIGHT", StringComparison.OrdinalIgnoreCase))
                active = new Rect(cx + halfArm, cy - halfArm, extent - halfArm, halfArm * 2);
            else if (direction == "↓" || direction.Equals("DOWN", StringComparison.OrdinalIgnoreCase))
                active = new Rect(cx - halfArm, cy + halfArm, halfArm * 2, extent - halfArm);
            else if (direction == "←" || direction.Equals("LEFT", StringComparison.OrdinalIgnoreCase))
                active = new Rect(cx - extent, cy - halfArm, extent - halfArm, halfArm * 2);

            if (!active.IsEmpty)
                dc.DrawRectangle(accent, null, active);

            dc.DrawGeometry(null, Pen(neutralStroke, 1.2), cross);

            // Keep the centre visually connected while leaving the selected arm unmistakable.
            dc.DrawRectangle(neutralFill, null,
                new Rect(cx - halfArm + 0.6, cy - halfArm + 0.6,
                    Math.Max(1, (halfArm * 2) - 1.2), Math.Max(1, (halfArm * 2) - 1.2)));
        }

        private static void DrawPill(DrawingContext dc, Rect b, Brush accent, string label, bool stronger)
        {
            var rect = new Rect(b.Left + 1, b.Top + b.Height * 0.18, Math.Max(1, b.Width - 2), b.Height * 0.64);
            var fill = stronger ? new SolidColorBrush(Color.FromArgb(42, 255, 255, 255)) : null;
            dc.DrawRoundedRectangle(fill, Pen(accent, 1.55), rect, rect.Height * 0.32, rect.Height * 0.32);
            DrawCenteredText(dc, label, accent, rect, FontFor(label, b.Height * 0.44), FontWeights.Bold);
        }

        private static void DrawStickAxis(DrawingContext dc, Rect b, Brush accent, string label)
        {
            // One crisp stick outline and directional axis mark, with its label kept outside
            // the circular glyph. Avoid concentric rings that turn into visual noise at scale.
            var center = new Point(b.Left + b.Width * 0.31, b.Top + b.Height * 0.50);
            var radius = Math.Min(b.Height * 0.37, b.Width * 0.24);
            var housing = new SolidColorBrush(Color.FromRgb(30, 30, 30));
            var cap = new SolidColorBrush(Color.FromRgb(54, 54, 54));

            dc.DrawEllipse(housing, Pen(accent, 1.6), center, radius, radius);
            dc.DrawEllipse(cap, Pen(accent, 1.15), center, radius * 0.69, radius * 0.69);

            var upper = (label ?? string.Empty).ToUpperInvariant();
            var vertical = upper.EndsWith("Y", StringComparison.Ordinal);
            var axisSpan = radius * 0.40;
            var axisPen = Pen(accent, 2.2);
            if (vertical)
            {
                dc.DrawLine(axisPen, new Point(center.X, center.Y - axisSpan),
                    new Point(center.X, center.Y + axisSpan));
                dc.DrawEllipse(accent, null, new Point(center.X, center.Y - axisSpan), 1.7, 1.7);
                dc.DrawEllipse(accent, null, new Point(center.X, center.Y + axisSpan), 1.7, 1.7);
            }
            else
            {
                dc.DrawLine(axisPen, new Point(center.X - axisSpan, center.Y),
                    new Point(center.X + axisSpan, center.Y));
                dc.DrawEllipse(accent, null, new Point(center.X - axisSpan, center.Y), 1.7, 1.7);
                dc.DrawEllipse(accent, null, new Point(center.X + axisSpan, center.Y), 1.7, 1.7);
            }

            DrawCenteredText(dc, label, accent,
                new Rect(b.Left + b.Width * 0.57, b.Top, b.Width * 0.43, b.Height),
                FontFor(label, b.Height * 0.42), FontWeights.Bold);
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
            DrawCenteredText(dc, label, accent,
                new Rect(b.Left + b.Width * 0.48, b.Top, b.Width * 0.52, b.Height),
                FontFor(label, b.Height * 0.44), FontWeights.Bold);
        }

        private static void DrawGenericButton(DrawingContext dc, Rect b, Brush accent, string label)
        {
            if (label == "OP" || label == "SH")
            {
                var rect = new Rect(
                    b.Left + b.Width * 0.13,
                    b.Top + b.Height * 0.22,
                    b.Width * 0.74,
                    b.Height * 0.56);
                dc.DrawRoundedRectangle(null, Pen(accent, 1.45), rect, 3.5, 3.5);
                DrawCenteredText(dc, label, accent, rect,
                    Math.Max(8, b.Height * 0.36), FontWeights.SemiBold);
                return;
            }

            var radius = Math.Min(b.Width, b.Height) * 0.35;
            var center = new Point(b.Left + b.Width / 2.0, b.Top + b.Height / 2.0);
            dc.DrawEllipse(null, Pen(accent, 1.55), center, radius, radius);
            DrawCenteredText(dc, label, accent,
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
            DrawCenteredText(dc, "?", accent,
                new Rect(center.X - half, center.Y - half - 0.5, size, size + 1),
                Math.Max(8, b.Height * 0.48), FontWeights.Bold);
        }

        private static void DrawUnbound(DrawingContext dc, Rect b, Brush accent)
        {
            var rect = new Rect(b.Left + 2, b.Top + 3, Math.Max(1, b.Width - 4), Math.Max(1, b.Height - 6));
            var pen = Pen(new SolidColorBrush(Color.FromRgb(105, 105, 105)), 1.0);
            dc.DrawRoundedRectangle(null, pen, rect, 3, 3);
            // Unassigned quick glyphs retain the question mark; Bind belongs to editor actions.
            DrawCenteredText(dc, "?", new SolidColorBrush(Color.FromRgb(188, 188, 188)), rect,
                Math.Min(13, b.Height * 0.35), FontWeights.SemiBold);
        }

        private static void DrawCenteredText(DrawingContext dc, string text, Brush brush, Rect rect, double fontSize, FontWeight weight)
        {
            if (string.IsNullOrEmpty(text)) return;
#pragma warning disable 618
            var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                TextTypeface(weight),
                fontSize, brush);
#pragma warning restore 618
            var point = new Point(rect.Left + (rect.Width - formatted.Width) / 2.0,
                rect.Top + (rect.Height - formatted.Height) / 2.0);
            dc.DrawText(formatted, point);
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
