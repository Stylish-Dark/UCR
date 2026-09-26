using System.Collections.Generic;
using System.Windows.Media;
using HidWizards.UCR.ViewModels.Presentation;

namespace HidWizards.UCR.Views.Controls
{
    internal static partial class DeviceGlyphVectors
    {
        private static readonly Dictionary<DeviceVisualKind, Geometry> Cache = new Dictionary<DeviceVisualKind, Geometry>();

        public static bool TryGet(DeviceVisualKind kind, out Geometry geometry)
        {
            if (Cache.TryGetValue(kind, out geometry)) return true;

            var data = ResolvePlayStation(kind) ??
                       ResolveXbox(kind) ??
                       ResolveNintendo(kind) ??
                       ResolveVJoy(kind);

            if (data == null)
            {
                geometry = null;
                return false;
            }

            geometry = Geometry.Parse(data);
            if (geometry.CanFreeze) geometry.Freeze();
            Cache[kind] = geometry;
            return true;
        }
    }
}
