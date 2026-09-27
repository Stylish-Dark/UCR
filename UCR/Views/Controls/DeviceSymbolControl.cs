using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;

namespace HidWizards.UCR.Views.Controls
{
    /// <summary>
    /// Renders one glyph directly from DeviceSymbols-v7.ttf.
    /// This deliberately bypasses WPF FontFamily fallback, which can turn private-use glyphs
    /// into empty boxes even when the TTF itself is present and valid.
    /// </summary>
    public sealed class DeviceSymbolControl : FrameworkElement
    {
        private static readonly object TypefaceLock = new object();
        private static GlyphTypeface _typeface;

        public static readonly DependencyProperty SymbolTextProperty = DependencyProperty.Register(
            nameof(SymbolText), typeof(string), typeof(DeviceSymbolControl),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(
            nameof(Foreground), typeof(Brush), typeof(DeviceSymbolControl),
            new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty GlyphSizeProperty = DependencyProperty.Register(
            nameof(GlyphSize), typeof(double), typeof(DeviceSymbolControl),
            new FrameworkPropertyMetadata(29.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public string SymbolText
        {
            get { return (string)GetValue(SymbolTextProperty); }
            set { SetValue(SymbolTextProperty, value); }
        }

        public Brush Foreground
        {
            get { return (Brush)GetValue(ForegroundProperty); }
            set { SetValue(ForegroundProperty, value); }
        }

        public double GlyphSize
        {
            get { return (double)GetValue(GlyphSizeProperty); }
            set { SetValue(GlyphSizeProperty, value); }
        }

        public DeviceSymbolControl()
        {
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            return new Size(
                double.IsInfinity(availableSize.Width) ? 48 : availableSize.Width,
                double.IsInfinity(availableSize.Height) ? 30 : availableSize.Height);
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);

            if (string.IsNullOrEmpty(SymbolText) || ActualWidth <= 0 || ActualHeight <= 0) return;

            var typeface = GetTypeface();
            var codePoint = char.ConvertToUtf32(SymbolText, 0);
            ushort glyphIndex;
            if (!typeface.CharacterToGlyphMap.TryGetValue(codePoint, out glyphIndex))
                throw new InvalidOperationException("Device Symbols v7 does not contain U+" + codePoint.ToString("X4") + ".");

            var emSize = Math.Max(1.0, GlyphSize);
            var outline = typeface.GetGlyphOutline(glyphIndex, emSize, emSize);
            if (outline == null || outline.Bounds.IsEmpty) return;

            var bounds = outline.Bounds;
            var dx = (ActualWidth - bounds.Width) * 0.5 - bounds.Left;
            var dy = (ActualHeight - bounds.Height) * 0.5 - bounds.Top;

            drawingContext.PushTransform(new TranslateTransform(dx, dy));
            drawingContext.DrawGeometry(Foreground ?? Brushes.White, null, outline);
            drawingContext.Pop();
        }

        private static GlyphTypeface GetTypeface()
        {
            if (_typeface != null) return _typeface;

            lock (TypefaceLock)
            {
                if (_typeface != null) return _typeface;

                var assemblyDirectory = Path.GetDirectoryName(typeof(DeviceSymbolControl).Assembly.Location);
                var fontPath = Path.Combine(assemblyDirectory, "Assets", "Fonts", "DeviceSymbols-v7.ttf");

                if (!File.Exists(fontPath))
                    throw new FileNotFoundException("DeviceSymbols-v7.ttf was not deployed with UCR.", fontPath);

                var typeface = new GlyphTypeface(new Uri(fontPath, UriKind.Absolute));
                for (var codePoint = 0xE001; codePoint <= 0xE005; codePoint++)
                {
                    if (!typeface.CharacterToGlyphMap.ContainsKey(codePoint))
                        throw new InvalidOperationException("Device Symbols v7 is missing U+" + codePoint.ToString("X4") + ".");
                }

                _typeface = typeface;
                return _typeface;
            }
        }
    }
}
