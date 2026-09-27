using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HidWizards.UCR.ViewModels.Presentation;

namespace HidWizards.UCR.Views.Controls
{
    /// <summary>
    /// High-resolution raster device artwork for UCR device rows.
    /// The master PNGs are recoloured at runtime so device/player colours remain fully dynamic.
    /// </summary>
    public sealed class DeviceGlyphControl : FrameworkElement
    {
        public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
            nameof(Kind), typeof(DeviceVisualKind), typeof(DeviceGlyphControl),
            new FrameworkPropertyMetadata(DeviceVisualKind.Unknown, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
            nameof(Stroke), typeof(Brush), typeof(DeviceGlyphControl),
            new FrameworkPropertyMetadata(Brushes.LightGray, FrameworkPropertyMetadataOptions.AffectsRender));

        // Kept for XAML/backwards compatibility. Raster artwork has its line weight baked into the source image.
        public static readonly DependencyProperty GlyphStrokeThicknessProperty = DependencyProperty.Register(
            nameof(GlyphStrokeThickness), typeof(double), typeof(DeviceGlyphControl),
            new FrameworkPropertyMetadata(3.0, FrameworkPropertyMetadataOptions.AffectsRender));

        // Kept for XAML/backwards compatibility. The raster renderer intentionally stays crisp and does not blur/glow.
        public static readonly DependencyProperty GlowOpacityProperty = DependencyProperty.Register(
            nameof(GlowOpacity), typeof(double), typeof(DeviceGlyphControl),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        private static readonly object CacheLock = new object();
        private static readonly Dictionary<string, BitmapSource> SourceCache =
            new Dictionary<string, BitmapSource>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, BitmapSource> TintCache =
            new Dictionary<string, BitmapSource>(StringComparer.OrdinalIgnoreCase);

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
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            return new Size(
                double.IsInfinity(availableSize.Width) ? 42 : availableSize.Width,
                double.IsInfinity(availableSize.Height) ? 26 : availableSize.Height);
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);

            if (ActualWidth <= 0 || ActualHeight <= 0) return;

            var assetName = AssetFor(Kind);
            var source = LoadSource(assetName);
            if (source == null || source.PixelWidth <= 0 || source.PixelHeight <= 0) return;

            var solid = Stroke as SolidColorBrush;
            var color = solid != null ? solid.Color : Colors.LightGray;
            var opacity = solid != null ? Math.Max(0.0, Math.Min(1.0, solid.Opacity)) : 1.0;
            var image = GetTinted(assetName, source, color, opacity);

            var scale = Math.Min(ActualWidth / image.PixelWidth, ActualHeight / image.PixelHeight);
            var width = image.PixelWidth * scale;
            var height = image.PixelHeight * scale;
            var x = (ActualWidth - width) * 0.5;
            var y = (ActualHeight - height) * 0.5;

            drawingContext.DrawImage(image, new Rect(x, y, width, height));
        }

        internal static string AssetFor(DeviceVisualKind kind)
        {
            switch (kind)
            {
                case DeviceVisualKind.Xbox360:
                    return "xbox360.png";

                case DeviceVisualKind.PlayStation4:
                    return "dualshock4.png";

                case DeviceVisualKind.Keyboard:
                    return "keyboard.png";

                case DeviceVisualKind.VJoy:
                    return "vjoy.png";

                // These are deliberately neutral until each family gets its own finished raster artwork.
                // Showing a generic device is better than showing the wrong controller.
                case DeviceVisualKind.Mouse:
                case DeviceVisualKind.Gamepad:
                case DeviceVisualKind.PlayStation1:
                case DeviceVisualKind.PlayStation2:
                case DeviceVisualKind.PlayStation3:
                case DeviceVisualKind.PlayStation5:
                case DeviceVisualKind.XboxOriginal:
                case DeviceVisualKind.XboxOne:
                case DeviceVisualKind.XboxSeries:
                case DeviceVisualKind.Nintendo64:
                case DeviceVisualKind.GameCube:
                case DeviceVisualKind.WiiRemote:
                case DeviceVisualKind.WiiClassic:
                case DeviceVisualKind.SwitchPro:
                case DeviceVisualKind.SwitchJoyCon:
                case DeviceVisualKind.ArcadeStick:
                case DeviceVisualKind.DirectInput:
                case DeviceVisualKind.Unavailable:
                case DeviceVisualKind.Unknown:
                default:
                    return "device-generic.png";
            }
        }

        private static BitmapSource LoadSource(string assetName)
        {
            lock (CacheLock)
            {
                BitmapSource cached;
                if (SourceCache.TryGetValue(assetName, out cached)) return cached;

                try
                {
                    var uri = new Uri(
                        "pack://application:,,,/UCR;component/Assets/DeviceGlyphs/" + assetName,
                        UriKind.Absolute);

                    var decoded = new BitmapImage();
                    decoded.BeginInit();
                    decoded.CacheOption = BitmapCacheOption.OnLoad;
                    decoded.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                    decoded.UriSource = uri;
                    decoded.EndInit();
                    if (decoded.CanFreeze) decoded.Freeze();

                    SourceCache[assetName] = decoded;
                    return decoded;
                }
                catch
                {
                    return null;
                }
            }
        }

        private static BitmapSource GetTinted(
            string assetName, BitmapSource source, Color color, double brushOpacity)
        {
            var opacityByte = (byte)Math.Round(255.0 * brushOpacity);
            var cacheKey = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0}|{1:X2}{2:X2}{3:X2}{4:X2}|{5:X2}",
                assetName, color.A, color.R, color.G, color.B, opacityByte);

            lock (CacheLock)
            {
                BitmapSource cached;
                if (TintCache.TryGetValue(cacheKey, out cached)) return cached;

                BitmapSource formatted = source;
                if (source.Format != PixelFormats.Bgra32)
                {
                    var converted = new FormatConvertedBitmap(
                        source, PixelFormats.Bgra32, null, 0.0);
                    if (converted.CanFreeze) converted.Freeze();
                    formatted = converted;
                }

                var stride = formatted.PixelWidth * 4;
                var pixels = new byte[stride * formatted.PixelHeight];
                formatted.CopyPixels(pixels, stride, 0);

                var tintAlpha = (color.A / 255.0) * brushOpacity;
                for (var index = 0; index < pixels.Length; index += 4)
                {
                    var sourceAlpha = pixels[index + 3];
                    if (sourceAlpha == 0) continue;

                    pixels[index] = color.B;
                    pixels[index + 1] = color.G;
                    pixels[index + 2] = color.R;
                    pixels[index + 3] = (byte)Math.Round(sourceAlpha * tintAlpha);
                }

                var tinted = BitmapSource.Create(
                    formatted.PixelWidth,
                    formatted.PixelHeight,
                    formatted.DpiX > 0 ? formatted.DpiX : 96.0,
                    formatted.DpiY > 0 ? formatted.DpiY : 96.0,
                    PixelFormats.Bgra32,
                    null,
                    pixels,
                    stride);

                if (tinted.CanFreeze) tinted.Freeze();
                TintCache[cacheKey] = tinted;
                return tinted;
            }
        }
    }
}
