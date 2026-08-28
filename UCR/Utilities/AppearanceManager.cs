using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using HidWizards.UCR.Core.Utilities;

namespace HidWizards.UCR.Utilities
{
    public sealed class AccentPalette
    {
        public string Name { get; set; }
        public Color Color { get; set; }
        public Color Foreground { get; set; }
    }

    public static class AppearanceManager
    {
        private static readonly List<AccentPalette> Palettes = new List<AccentPalette>
        {
            Palette("Blue", 0x31, 0x52, 0xA3, Colors.White),
            Palette("Green", 0x32, 0x85, 0x48, Colors.White),
            Palette("Yellow", 0xA8, 0x78, 0x18, Color.FromRgb(24, 24, 24)),
            Palette("Pink", 0xB4, 0x47, 0x83, Colors.White),
            Palette("Orange", 0xB8, 0x54, 0x28, Colors.White),
            Palette("Purple", 0x70, 0x4B, 0xB8, Colors.White)
        };

        public static IEnumerable<AccentPalette> AvailablePalettes => Palettes;

        public const double MinimumUiScale = 0.85;
        public const double MaximumUiScale = 1.25;
        public const double UiScaleStep = 0.05;

        public static string CurrentAccentName { get; private set; } = "Blue";
        public static Color CurrentAccentColor => (Find(CurrentAccentName) ?? Find("Blue")).Color;
        public static double CurrentUiScale { get; private set; } = 1.0;

        public static void ApplySavedAccent()
        {
            ApplyAccent(HidWizards.UCR.Properties.Settings.Default.AccentColor, false);
        }

        public static void ApplySavedUiScale()
        {
            ApplyUiScale(HidWizards.UCR.Properties.Settings.Default.UiScale, false);
        }

        public static double AdjustUiScale(int wheelDelta)
        {
            if (wheelDelta == 0) return CurrentUiScale;
            return ApplyUiScale(CurrentUiScale + (wheelDelta > 0 ? UiScaleStep : -UiScaleStep));
        }

        public static double ApplyUiScale(double scale, bool persist = true)
        {
            var normalized = Math.Round(Math.Max(MinimumUiScale, Math.Min(MaximumUiScale, scale)), 2);
            CurrentUiScale = normalized;
            if (Application.Current != null) Application.Current.Resources["UcrUiScale"] = normalized;

            if (!persist) return normalized;
            try
            {
                HidWizards.UCR.Properties.Settings.Default.UiScale = normalized;
                HidWizards.UCR.Properties.Settings.Default.Save();
            }
            catch (Exception exception)
            {
                Logger.Warn("UI scale applied but could not be saved to user settings", exception);
            }
            return normalized;
        }

        public static void ApplyAccent(string name, bool persist = true)
        {
            var palette = Find(name) ?? Find("Blue");
            if (palette == null || Application.Current == null) return;

            CurrentAccentName = palette.Name;
            var light = Blend(palette.Color, Colors.White, 0.12);
            var dark = Blend(palette.Color, Colors.Black, 0.18);

            SetBrushColor("PrimaryHueLightBrush", light);
            SetBrushColor("PrimaryHueMidBrush", palette.Color);
            SetBrushColor("PrimaryHueDarkBrush", dark);
            SetBrushColor("MaterialDesignSelection", palette.Color);
            SetBrushColor("SecondaryAccentBrush", palette.Color);
            SetBrushColor("UcrAccentBrush", palette.Color);

            SetBrushColor("PrimaryHueLightForegroundBrush", palette.Foreground);
            SetBrushColor("PrimaryHueMidForegroundBrush", palette.Foreground);
            SetBrushColor("PrimaryHueDarkForegroundBrush", palette.Foreground);
            SetBrushColor("SecondaryAccentForegroundBrush", palette.Foreground);

            if (!persist) return;
            try
            {
                HidWizards.UCR.Properties.Settings.Default.AccentColor = palette.Name;
                HidWizards.UCR.Properties.Settings.Default.Save();
            }
            catch (Exception exception)
            {
                Logger.Warn("Accent colour applied but could not be saved to user settings", exception);
            }
        }

        public static AccentPalette Find(string name)
        {
            foreach (var palette in Palettes)
            {
                if (string.Equals(palette.Name, name, StringComparison.OrdinalIgnoreCase)) return palette;
            }
            return null;
        }

        public static Brush BrushFor(string name)
        {
            var palette = Find(name) ?? Find("Blue");
            var brush = new SolidColorBrush(palette?.Color ?? Color.FromRgb(49, 82, 163));
            brush.Freeze();
            return brush;
        }

        private static void SetBrushColor(string key, Color color)
        {
            var brush = Application.Current.Resources[key] as SolidColorBrush;
            if (brush != null && !brush.IsFrozen)
            {
                brush.Color = color;
                return;
            }

            Application.Current.Resources[key] = new SolidColorBrush(color);
        }

        private static AccentPalette Palette(string name, byte r, byte g, byte b, Color foreground)
        {
            return new AccentPalette { Name = name, Color = Color.FromRgb(r, g, b), Foreground = foreground };
        }

        private static Color Blend(Color first, Color second, double amount)
        {
            amount = Math.Max(0, Math.Min(1, amount));
            return Color.FromRgb(
                (byte)(first.R + (second.R - first.R) * amount),
                (byte)(first.G + (second.G - first.G) * amount),
                (byte)(first.B + (second.B - first.B) * amount));
        }
    }
}
