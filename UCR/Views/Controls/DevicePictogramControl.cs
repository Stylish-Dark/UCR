using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using HidWizards.UCR.ViewModels.Presentation;

namespace HidWizards.UCR.Views.Controls
{
    /// <summary>
    /// Precision-built compact device pictograms.
    /// Shape identifies the device family; the configured device colour identifies the instance.
    /// All geometry uses a 56 x 32 design grid and renders as vectors at the final UI size.
    /// </summary>
    public sealed class DevicePictogramControl : FrameworkElement
    {
        private const double DesignWidth = 56.0;
        private const double DesignHeight = 32.0;

        public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
            nameof(Kind), typeof(DeviceVisualKind), typeof(DevicePictogramControl),
            new FrameworkPropertyMetadata(DeviceVisualKind.Unknown, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
            nameof(Fill), typeof(Brush), typeof(DevicePictogramControl),
            new FrameworkPropertyMetadata(Brushes.LightGray, FrameworkPropertyMetadataOptions.AffectsRender));

        private static readonly object ShapeLock = new object();
        private static readonly Dictionary<DeviceVisualKind, Geometry> ShapeCache =
            new Dictionary<DeviceVisualKind, Geometry>();

        public DeviceVisualKind Kind
        {
            get { return (DeviceVisualKind)GetValue(KindProperty); }
            set { SetValue(KindProperty, value); }
        }

        public Brush Fill
        {
            get { return (Brush)GetValue(FillProperty); }
            set { SetValue(FillProperty, value); }
        }

        public DevicePictogramControl()
        {
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            return new Size(
                double.IsInfinity(availableSize.Width) ? 48 : availableSize.Width,
                double.IsInfinity(availableSize.Height) ? 28 : availableSize.Height);
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);
            if (ActualWidth <= 0 || ActualHeight <= 0) return;

            var geometry = GeometryFor(Kind);
            if (geometry == null) return;

            var scale = Math.Min(ActualWidth / DesignWidth, ActualHeight / DesignHeight);
            var x = (ActualWidth - DesignWidth * scale) * 0.5;
            var y = (ActualHeight - DesignHeight * scale) * 0.5;

            drawingContext.PushTransform(new MatrixTransform(scale, 0, 0, scale, x, y));
            drawingContext.DrawGeometry(Fill ?? Brushes.LightGray, null, geometry);
            drawingContext.Pop();
        }

        private static Geometry GeometryFor(DeviceVisualKind kind)
        {
            lock (ShapeLock)
            {
                Geometry geometry;
                if (ShapeCache.TryGetValue(kind, out geometry)) return geometry;
                geometry = Build(kind);
                if (geometry != null && geometry.CanFreeze) geometry.Freeze();
                ShapeCache[kind] = geometry;
                return geometry;
            }
        }

        private static Geometry Build(DeviceVisualKind kind)
        {
            switch (kind)
            {
                case DeviceVisualKind.Keyboard: return Keyboard();
                case DeviceVisualKind.Mouse: return Mouse();

                case DeviceVisualKind.PlayStation1:
                case DeviceVisualKind.PlayStation2:
                case DeviceVisualKind.PlayStation3:
                case DeviceVisualKind.PlayStation4:
                case DeviceVisualKind.PlayStation5:
                    return PlayStation();

                case DeviceVisualKind.XboxOriginal:
                case DeviceVisualKind.Xbox360:
                case DeviceVisualKind.XboxOne:
                case DeviceVisualKind.XboxSeries:
                    return Xbox();

                case DeviceVisualKind.Nintendo64: return Nintendo64();
                case DeviceVisualKind.GameCube: return GameCube();
                case DeviceVisualKind.WiiRemote: return WiiRemote();
                case DeviceVisualKind.WiiClassic: return WiiClassic();
                case DeviceVisualKind.SwitchPro: return SwitchPro();
                case DeviceVisualKind.SwitchJoyCon: return JoyCons();
                case DeviceVisualKind.ArcadeStick: return ArcadeStick();
                case DeviceVisualKind.VJoy: return VirtualGamepad();

                default: return GenericGamepad();
            }
        }

        private static Geometry Keyboard()
        {
            var holes = new List<Geometry>();
            for (var row = 0; row < 2; row++)
            {
                for (var column = 0; column < 9; column++)
                {
                    holes.Add(Rect(6 + column * 5, 8 + row * 6, 3, 3, 0.55));
                }
            }

            holes.Add(Rect(6, 20, 3, 3, 0.55));
            holes.Add(Rect(11, 20, 3, 3, 0.55));
            holes.Add(Rect(16, 20, 3, 3, 0.55));
            holes.Add(Rect(21, 20, 18, 3, 0.75));
            holes.Add(Rect(42, 20, 3, 3, 0.55));
            holes.Add(Rect(47, 20, 3, 3, 0.55));

            return Punch(Rect(2, 4, 52, 24, 3.2), holes);
        }

        private static Geometry Mouse()
        {
            return Punch(Rect(18, 2, 20, 28, 10), new[]
            {
                Rect(27.25, 3.5, 1.5, 10.5, 0.5),
                Rect(26.2, 7.0, 3.6, 6.0, 1.6)
            });
        }

        private static Geometry PlayStation()
        {
            var body = Path(
                "M10,7 C14,4.8 18.5,5.2 22,7 H34 C37.5,5.2 42,4.8 46,7 " +
                "C49.2,9.2 51.4,15.5 52.2,21.5 C52.8,26.7 50.1,29.6 47.2,28.5 " +
                "C44.7,27.5 42.2,22.7 39.2,19.6 H16.8 C13.8,22.7 11.3,27.5 8.8,28.5 " +
                "C5.9,29.6 3.2,26.7 3.8,21.5 C4.6,15.5 6.8,9.2 10,7 Z");

            return Punch(body, new[]
            {
                Rect(21, 8.1, 14, 5.3, 1.25),
                Cross(14.0, 13.2, 4.4, 2.3),
                Circle(42.0, 10.2, 1.75), Circle(45.2, 13.2, 1.75),
                Circle(42.0, 16.2, 1.75), Circle(38.8, 13.2, 1.75),
                Circle(22.4, 19.6, 3.15), Circle(33.6, 19.6, 3.15),
                Rect(27, 15.4, 2, 1.3, 0.55)
            });
        }

        private static Geometry Xbox()
        {
            var body = Path(
                "M9.5,7.2 C13.2,4.8 18.7,5 23.2,6.7 C26.3,7.9 29.7,7.9 32.8,6.7 " +
                "C37.3,5 42.8,4.8 46.5,7.2 C50.2,9.8 52.5,17.3 52.4,23.8 " +
                "C52.3,28.1 49.4,30 46.6,28.2 L38.2,21.4 H17.8 L9.4,28.2 " +
                "C6.6,30 3.7,28.1 3.6,23.8 C3.5,17.3 5.8,9.8 9.5,7.2 Z");

            return Punch(body, new[]
            {
                Circle(16, 11.5, 3.35), Circle(34.2, 20, 3.25),
                Cross(19.3, 20, 4.3, 2.25),
                Circle(44, 9.4, 1.65), Circle(47.1, 12.5, 1.65),
                Circle(44, 15.6, 1.65), Circle(40.9, 12.5, 1.65),
                Circle(28, 10.6, 2.1),
                Rect(24.2, 14, 2.6, 1.2, 0.55), Rect(29.2, 14, 2.6, 1.2, 0.55)
            });
        }

        private static Geometry Nintendo64()
        {
            var body = Path(
                "M11,7 C14.5,5 19,5.5 23,8 C25.8,6.5 30.2,6.5 33,8 " +
                "C37,5.5 41.5,5 45,7 C49.5,10 51.5,17 50.5,22.5 " +
                "C49.6,27.2 46.8,29.2 44.3,27 L36.2,18.4 L33,28.5 " +
                "C32.4,30.6 29.3,31 28,28.3 L24.8,18.4 L16.7,27 " +
                "C14.2,29.2 11.4,27.2 10.5,22.5 C9.5,17 6.5,10 11,7 Z");

            return Punch(body, new[]
            {
                Cross(16, 13, 4, 2.1), Circle(28, 16.5, 3.1), Circle(41, 12, 2),
                Circle(44.8, 15, 1.45), Circle(44.8, 9, 1.45), Circle(48, 12, 1.45)
            });
        }

        private static Geometry GameCube()
        {
            var body = Path(
                "M9.5,7 C13,5 18.5,5.2 22.5,7.5 C26,6.4 30,6.4 33.5,7.5 " +
                "C37.5,5.2 43,5 46.5,7 C50.2,9.7 52.5,17.2 52,23.8 " +
                "C51.7,28.2 48.8,30 46,28 L38,21 H18 L10,28 " +
                "C7.2,30 4.3,28.2 4,23.8 C3.5,17.2 5.8,9.7 9.5,7 Z");

            return Punch(body, new[]
            {
                Circle(15.5, 11.5, 3.25), Cross(19, 20, 3.8, 2),
                Circle(39.5, 14, 3.7), Circle(45, 10, 1.55),
                Circle(46.8, 16.5, 1.55), Circle(34, 21, 2.5)
            });
        }

        private static Geometry WiiRemote()
        {
            return Punch(Rect(20.5, 1.5, 15, 29, 3.1), new[]
            {
                Cross(28, 7, 3.5, 1.9), Circle(28, 14, 2.1),
                Circle(28, 20, 1.15), Circle(28, 24, 1.15),
                Rect(25.5, 27, 5, 1.5, 0.6)
            });
        }

        private static Geometry WiiClassic()
        {
            var body = Path(
                "M8,8 C13,5 19,5 24,7 H32 C37,5 43,5 48,8 " +
                "C51,10 53,16 52,21 C51,26 47,28 43,26 L37,21 H19 L13,26 " +
                "C9,28 5,26 4,21 C3,16 5,10 8,8 Z");

            return Punch(body, new[]
            {
                Cross(14.5, 13, 4.2, 2.1), Circle(23, 20, 2.8), Circle(33, 20, 2.8),
                Circle(43, 10.5, 1.45), Circle(46, 13.5, 1.45),
                Circle(43, 16.5, 1.45), Circle(40, 13.5, 1.45),
                Circle(26, 12, 0.9), Circle(30, 12, 0.9)
            });
        }

        private static Geometry SwitchPro()
        {
            var body = Path(
                "M9.5,7 C13.5,4.8 18.5,5.2 23,7 C26,8.2 30,8.2 33,7 " +
                "C37.5,5.2 42.5,4.8 46.5,7 C50,9.5 52.5,17 52,23.5 " +
                "C51.7,28 48.7,30 46,28 L38,21 H18 L10,28 C7.3,30 4.3,28 4,23.5 " +
                "C3.5,17 6,9.5 9.5,7 Z");

            return Punch(body, new[]
            {
                Circle(16, 11, 3.2), Cross(19, 20, 4.1, 2.15), Circle(35, 20, 3),
                Circle(44, 9.6, 1.55), Circle(47, 12.6, 1.55),
                Circle(44, 15.6, 1.55), Circle(41, 12.6, 1.55),
                Rect(24, 10.7, 3, 1.2, 0.5),
                Union(Rect(30, 10.7, 3, 1.2, 0.5), Rect(30.9, 9.8, 1.2, 3, 0.5))
            });
        }

        private static Geometry JoyCons()
        {
            var body = Union(Rect(10, 2, 15, 28, 6.5), Rect(31, 2, 15, 28, 6.5));
            return Punch(body, new[]
            {
                Circle(18, 9, 3), Circle(38, 20, 3), Cross(18, 21, 3.4, 1.8),
                Circle(39, 7.8, 1.35), Circle(42, 10.8, 1.35),
                Circle(39, 13.8, 1.35), Circle(36, 10.8, 1.35),
                Rect(23, 13, 1.4, 6, 0.4), Rect(31.6, 13, 1.4, 6, 0.4)
            });
        }

        private static Geometry ArcadeStick()
        {
            var deck = Path(
                "M6,9 C6,6.8 7.8,5.5 10,5.5 H47 C49.4,5.5 51.2,7 51.5,9.2 " +
                "L53,24 C53.3,27 51.3,29 48.3,29 H8.7 C5.7,29 3.7,27 4,24 Z");
            var body = Union(deck, Rect(13, 2.8, 2.6, 10, 1), Circle(14.3, 3.2, 3.2));

            return Punch(body, new[]
            {
                Circle(33, 12, 1.9), Circle(39, 11, 1.9), Circle(45, 12, 1.9),
                Circle(33, 18, 1.9), Circle(39, 17, 1.9), Circle(45, 18, 1.9),
                Circle(18, 18, 2.8)
            });
        }

        private static Geometry VirtualGamepad()
        {
            var body = Path(
                "M8,9 C11,6 16,6 20,8 H36 C40,6 45,6 48,9 " +
                "C51,12 52,18 51,23 C50,27 47,29 44,27 L37,21 H19 L12,27 " +
                "C9,29 6,27 5,23 C4,18 5,12 8,9 Z");

            return Punch(body, new[]
            {
                Cross(15, 14, 3.9, 2), Circle(28, 15, 4.1),
                Circle(42, 11, 1.4), Circle(46, 14, 1.4), Circle(42, 17, 1.4)
            });
        }

        private static Geometry GenericGamepad()
        {
            return Punch(Rect(4, 7, 48, 19, 8), new[]
            {
                Cross(15, 16.5, 4.1, 2.1),
                Circle(39, 13.5, 1.6), Circle(44, 16.5, 1.6), Circle(39, 19.5, 1.6),
                Rect(26, 15.8, 4, 1.5, 0.65)
            });
        }

        private static Geometry Punch(Geometry body, IEnumerable<Geometry> holes)
        {
            var result = body;
            foreach (var hole in holes)
            {
                result = new CombinedGeometry(GeometryCombineMode.Exclude, result, hole);
            }
            return result;
        }

        private static Geometry Rect(double x, double y, double width, double height, double radius)
        {
            return new RectangleGeometry(new Rect(x, y, width, height), radius, radius);
        }

        private static Geometry Circle(double x, double y, double radius)
        {
            return new EllipseGeometry(new Point(x, y), radius, radius);
        }

        private static Geometry Cross(double x, double y, double arm, double thickness)
        {
            return Union(
                Rect(x - arm, y - thickness * 0.5, arm * 2, thickness, thickness * 0.35),
                Rect(x - thickness * 0.5, y - arm, thickness, arm * 2, thickness * 0.35));
        }

        private static Geometry Union(params Geometry[] geometries)
        {
            if (geometries == null || geometries.Length == 0) return Geometry.Empty;
            var result = geometries[0];
            for (var index = 1; index < geometries.Length; index++)
            {
                result = new CombinedGeometry(GeometryCombineMode.Union, result, geometries[index]);
            }
            return result;
        }

        private static Geometry Path(string data)
        {
            return Geometry.Parse(data);
        }
    }
}
