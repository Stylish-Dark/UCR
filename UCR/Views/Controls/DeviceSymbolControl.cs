using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;

namespace HidWizards.UCR.Views.Controls
{
    /// <summary>
    /// Renders one glyph directly from the bundled DeviceSymbols-v7 font.
    /// The font is embedded inside UCR.exe, copied to a private local cache, then loaded as a GlyphTypeface.
    /// This avoids WPF font-family fallback, missing sidecar files, and Windows blocking downloaded font files.
    /// </summary>
    public sealed class DeviceSymbolControl : FrameworkElement
    {
        private const string FontResourceName = "HidWizards.UCR.Assets.Fonts.DeviceSymbols-v7.ttf";
        private const string FontFileName = "DeviceSymbols-v7.ttf";

        private static readonly object TypefaceLock = new object();
        private static GlyphTypeface _typeface;
        private static bool _fontLoadFailed;

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

            try
            {
                var typeface = GetTypeface();
                if (typeface == null) return;

                var codePoint = char.ConvertToUtf32(SymbolText, 0);
                ushort glyphIndex;
                if (!typeface.CharacterToGlyphMap.TryGetValue(codePoint, out glyphIndex)) return;

                var emSize = Math.Max(1.0, GlyphSize);
                var outline = typeface.GetGlyphOutline(glyphIndex, emSize, emSize);
                if (outline == null) return;

                var bounds = outline.Bounds;
                if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0) return;

                const double padding = 1.5;
                var availableWidth = Math.Max(1.0, ActualWidth - padding * 2.0);
                var availableHeight = Math.Max(1.0, ActualHeight - padding * 2.0);
                var scale = Math.Min(1.0,
                    Math.Min(availableWidth / bounds.Width, availableHeight / bounds.Height));

                var scaledWidth = bounds.Width * scale;
                var scaledHeight = bounds.Height * scale;
                var translateX = (ActualWidth - scaledWidth) * 0.5 - bounds.X * scale;
                var translateY = (ActualHeight - scaledHeight) * 0.5 - bounds.Y * scale;

                var transform = new TransformGroup();
                if (Math.Abs(scale - 1.0) > 0.0001)
                    transform.Children.Add(new ScaleTransform(scale, scale));
                transform.Children.Add(new TranslateTransform(translateX, translateY));

                var rendered = outline.CloneCurrentValue();
                rendered.Transform = transform;
                drawingContext.DrawGeometry(Foreground ?? Brushes.White, null, rendered);
            }
            catch (Exception exception)
            {
                // A decorative device icon must never be able to take UCR down.
                _fontLoadFailed = true;
                Debug.WriteLine("Device symbol rendering disabled: " + exception);
            }
        }

        private static GlyphTypeface GetTypeface()
        {
            if (_typeface != null) return _typeface;
            if (_fontLoadFailed) return null;

            lock (TypefaceLock)
            {
                if (_typeface != null) return _typeface;
                if (_fontLoadFailed) return null;

                try
                {
                    // Prefer the editable font shipped with UCR. Embedded original
                    // remains a fallback for old portable installations.
                    var fontPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                        "Assets", "Fonts", FontFileName);
                    if (!File.Exists(fontPath)) fontPath = MaterializeEmbeddedFont();
                    var typeface = new GlyphTypeface(new Uri(fontPath, UriKind.Absolute));

                    for (var codePoint = 0xE001; codePoint <= 0xE005; codePoint++)
                    {
                        if (!typeface.CharacterToGlyphMap.ContainsKey(codePoint))
                            throw new InvalidOperationException("Bundled Device Symbols font is missing U+" + codePoint.ToString("X4") + ".");
                    }

                    _typeface = typeface;
                    return _typeface;
                }
                catch (Exception exception)
                {
                    _fontLoadFailed = true;
                    Debug.WriteLine("Unable to load bundled device-symbol font: " + exception);
                    return null;
                }
            }
        }

        private static string MaterializeEmbeddedFont()
        {
            var assembly = typeof(DeviceSymbolControl).Assembly;
            using (var stream = assembly.GetManifestResourceStream(FontResourceName))
            {
                if (stream == null)
                    throw new InvalidOperationException("Embedded font resource was not found: " + FontResourceName);

                byte[] bytes;
                using (var memory = new MemoryStream())
                {
                    stream.CopyTo(memory);
                    bytes = memory.ToArray();
                }

                if (bytes.Length < 1024)
                    throw new InvalidOperationException("Embedded device-symbol font is unexpectedly small.");

                var cacheDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "UCR",
                    "FontCache");

                Directory.CreateDirectory(cacheDirectory);
                var fontPath = Path.Combine(cacheDirectory, FontFileName);

                var needsWrite = true;
                if (File.Exists(fontPath))
                {
                    try
                    {
                        var existing = File.ReadAllBytes(fontPath);
                        needsWrite = !ByteArraysEqual(existing, bytes);
                    }
                    catch
                    {
                        needsWrite = true;
                    }
                }

                if (needsWrite)
                {
                    var temporaryPath = fontPath + ".new";
                    File.WriteAllBytes(temporaryPath, bytes);

                    if (File.Exists(fontPath))
                        File.Delete(fontPath);

                    File.Move(temporaryPath, fontPath);
                }

                return fontPath;
            }
        }

        private static bool ByteArraysEqual(byte[] left, byte[] right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left == null || right == null || left.Length != right.Length) return false;

            for (var index = 0; index < left.Length; index++)
            {
                if (left[index] != right[index]) return false;
            }

            return true;
        }
    }
}
