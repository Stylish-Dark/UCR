using HidWizards.UCR.ViewModels.Presentation;

namespace HidWizards.UCR.Views.Controls
{
    internal static partial class DeviceGlyphVectors
    {
        private static string ResolveVJoy(DeviceVisualKind kind)
        {
            switch (kind)
            {
                case DeviceVisualKind.VJoy: return "F0 M5.00,50.80 L5.38,55.73 L7.28,56.49 L6.14,53.08 L6.52,48.90 L10.70,31.43 L14.87,19.66 L19.05,13.58 L24.75,11.68 L29.30,11.68 L32.34,12.44 L36.90,15.48 L63.86,15.48 L69.56,12.06 L76.39,11.68 L82.47,13.96 L85.51,18.90 L92.34,41.30 L93.86,48.90 L93.10,56.49 L94.62,56.49 L94.62,46.24 L88.16,22.32 L85.13,15.10 L82.09,12.06 L80.95,8.65 L77.53,7.13 L68.04,7.89 L66.52,12.06 L63.86,14.34 L36.90,14.34 L34.24,12.06 L33.86,9.78 L31.96,7.51 L24.37,7.13 L20.57,8.27 L19.43,9.41 L19.05,12.06 L15.63,15.10 L12.22,22.70 L8.42,34.85 Z M20.19,11.30 L21.33,9.41 L31.58,9.03 L32.72,10.92 Z M68.04,10.92 L69.18,8.65 L79.43,9.41 L80.57,11.30 L75.63,10.54 Z";
                default: return null;
            }
        }
    }
}
