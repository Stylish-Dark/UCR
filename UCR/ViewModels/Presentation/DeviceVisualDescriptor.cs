using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using HidWizards.UCR.Core.Models;
using HidWizards.UCR.Core.Models.Binding;

namespace HidWizards.UCR.ViewModels.Presentation
{
    public enum DeviceVisualKind
    {
        Unknown,
        Keyboard,
        Mouse,
        Xbox,
        PlayStation,
        VJoy,
        ArcadeStick,
        DirectInput
    }

    public enum ControlVisualKind
    {
        Unknown,
        Unbound,
        Key,
        MouseButton,
        XboxFaceButton,
        PlayStationFaceButton,
        DPad,
        ShoulderButton,
        Trigger,
        StickAxis,
        Axis,
        Button,
        Filter
    }

    public sealed class DeviceVisualDescriptor
    {
        public DeviceVisualKind Kind { get; set; }
        public Brush AccentBrush { get; set; }
        public string ToolTip { get; set; }
        public int SlotNumber { get; set; }
        public bool ShowSlotIndicator { get; set; }
    }

    public sealed class BindingVisualDescriptor
    {
        public DeviceVisualDescriptor Device { get; set; }
        public ControlVisualKind ControlKind { get; set; }
        public Brush ControlBrush { get; set; }
        public string ControlLabel { get; set; }
        public string ToolTip { get; set; }
        public bool IsBound { get; set; }
    }

    public static class DeviceVisualCatalog
    {
        public static readonly Brush XboxBrush = Freeze(Color.FromRgb(70, 166, 70));
        public static readonly Brush PlayStationBrush = Freeze(Color.FromRgb(68, 126, 220));
        public static readonly Brush VJoyBrush = Freeze(Color.FromRgb(155, 102, 221));
        public static readonly Brush ArcadeBrush = Freeze(Color.FromRgb(214, 72, 72));
        public static readonly Brush NeutralBrush = Freeze(Color.FromRgb(202, 205, 210));
        public static readonly Brush DirectInputBrush = Freeze(Color.FromRgb(125, 139, 154));
        public static readonly Brush FilterBrush = Freeze(Color.FromRgb(102, 187, 106));

        public static DeviceVisualDescriptor Describe(DeviceConfiguration configuration, Profile profile, DeviceIoType ioType)
        {
            if (configuration == null)
            {
                return Unknown("Device unavailable");
            }

            var descriptor = Describe(configuration.Device, ioType);
            descriptor.ToolTip = configuration.GetFullTitleForProfile(profile);
            return descriptor;
        }

        public static DeviceVisualDescriptor Describe(Device device, DeviceIoType ioType)
        {
            if (device == null) return Unknown("Device unavailable");

            var provider = device.ProviderName ?? string.Empty;
            var handle = device.DeviceHandle ?? string.Empty;
            var title = (device.DisplayTitle ?? device.Title ?? string.Empty).Trim();
            var hidPath = device.HidPath ?? string.Empty;
            var searchable = (provider + " " + handle + " " + title + " " + hidPath).ToLowerInvariant();

            if (provider.Equals("Core_ViGEm", StringComparison.OrdinalIgnoreCase))
            {
                if (handle.Equals("ds4", StringComparison.OrdinalIgnoreCase))
                {
                    return Build(DeviceVisualKind.PlayStation, PlayStationBrush, title, device.DeviceNumber + 1, ioType == DeviceIoType.Output);
                }
                if (handle.Equals("xb360", StringComparison.OrdinalIgnoreCase))
                {
                    return Build(DeviceVisualKind.Xbox, XboxBrush, title, device.DeviceNumber + 1, ioType == DeviceIoType.Output);
                }
            }

            if (provider.Equals("SharpDX_XInput", StringComparison.OrdinalIgnoreCase) ||
                searchable.Contains("xinput") || searchable.Contains("xbox") || searchable.Contains("vid_045e"))
            {
                return Build(DeviceVisualKind.Xbox, XboxBrush, title, device.DeviceNumber + 1, false);
            }

            if (searchable.Contains("dualshock") || searchable.Contains("dualsense") ||
                searchable.Contains("playstation") || searchable.Contains("vid_054c"))
            {
                return Build(DeviceVisualKind.PlayStation, PlayStationBrush, title, device.DeviceNumber + 1, false);
            }

            if (searchable.Contains("vjoy"))
            {
                return Build(DeviceVisualKind.VJoy, VJoyBrush, title, device.DeviceNumber + 1, false);
            }

            if (searchable.Contains("arcade") || searchable.Contains("fightstick") || searchable.Contains("fight stick"))
            {
                return Build(DeviceVisualKind.ArcadeStick, ArcadeBrush, title, 0, false);
            }

            if (provider.Equals("Core_Interception", StringComparison.OrdinalIgnoreCase))
            {
                if (searchable.Contains("mouse")) return Build(DeviceVisualKind.Mouse, NeutralBrush, title, 0, false);
                return Build(DeviceVisualKind.Keyboard, NeutralBrush, title, 0, false);
            }

            if (searchable.Contains("keyboard")) return Build(DeviceVisualKind.Keyboard, NeutralBrush, title, 0, false);
            if (searchable.Contains("mouse")) return Build(DeviceVisualKind.Mouse, NeutralBrush, title, 0, false);

            if (provider.Equals("SharpDX_DirectInput", StringComparison.OrdinalIgnoreCase) || searchable.Contains("directinput"))
            {
                return Build(DeviceVisualKind.DirectInput, DirectInputBrush, title, device.DeviceNumber + 1, false);
            }

            return Build(DeviceVisualKind.Unknown, DirectInputBrush, title, 0, false);
        }

        public static BindingVisualDescriptor DescribeBinding(DeviceBinding binding, DeviceBindingCategory category, Profile profile)
        {
            if (binding == null)
            {
                return new BindingVisualDescriptor
                {
                    Device = Unknown("Device unavailable"),
                    ControlKind = ControlVisualKind.Unbound,
                    ControlBrush = NeutralBrush,
                    ControlLabel = "?",
                    ToolTip = "No binding",
                    IsBound = false
                };
            }

            var configuration = profile?.GetDeviceConfiguration(binding.DeviceIoType, binding.DeviceConfigurationGuid);
            var deviceDescriptor = Describe(configuration, profile, binding.DeviceIoType);
            var boundName = binding.IsBound ? SafeBoundName(binding) : "Not bound";
            var result = new BindingVisualDescriptor
            {
                Device = deviceDescriptor,
                ControlKind = ControlVisualKind.Unbound,
                ControlBrush = NeutralBrush,
                ControlLabel = "?",
                ToolTip = deviceDescriptor.ToolTip + " — " + boundName,
                IsBound = binding.IsBound
            };

            if (!binding.IsBound) return result;

            var leaf = ExtractLeaf(boundName);
            PopulateControl(result, binding, category, leaf, configuration?.Device);
            return result;
        }

        public static BindingVisualDescriptor Filter(string name)
        {
            var cleanName = string.IsNullOrWhiteSpace(name) ? "Filter" : name.Trim();
            return new BindingVisualDescriptor
            {
                Device = null,
                ControlKind = ControlVisualKind.Filter,
                ControlBrush = FilterBrush,
                ControlLabel = cleanName,
                ToolTip = "Filter — " + cleanName,
                IsBound = true
            };
        }

        private static void PopulateControl(BindingVisualDescriptor result, DeviceBinding binding,
            DeviceBindingCategory category, string leaf, Device device)
        {
            var kind = result.Device?.Kind ?? DeviceVisualKind.Unknown;
            var lower = (leaf ?? string.Empty).Trim().ToLowerInvariant();

            if (kind == DeviceVisualKind.Keyboard)
            {
                result.ControlKind = ControlVisualKind.Key;
                result.ControlLabel = CleanKeyboardLabel(leaf);
                result.ControlBrush = NeutralBrush;
                return;
            }

            if (kind == DeviceVisualKind.Mouse)
            {
                if (category == DeviceBindingCategory.Delta || category == DeviceBindingCategory.Range)
                {
                    result.ControlKind = ControlVisualKind.Axis;
                    result.ControlLabel = ShortAxisLabel(leaf);
                    result.ControlBrush = NeutralBrush;
                }
                else
                {
                    result.ControlKind = ControlVisualKind.MouseButton;
                    result.ControlLabel = ShortMouseLabel(leaf);
                    result.ControlBrush = NeutralBrush;
                }
                return;
            }

            if (kind == DeviceVisualKind.Xbox || kind == DeviceVisualKind.PlayStation)
            {
                PopulateKnownControllerControl(result, binding, category, leaf, kind);
                return;
            }

            if (lower.Contains("dpad") || lower.Contains("pov"))
            {
                result.ControlKind = ControlVisualKind.DPad;
                result.ControlLabel = DirectionLabel(leaf, binding.KeyValue);
                result.ControlBrush = result.Device?.AccentBrush ?? NeutralBrush;
                return;
            }

            if (category == DeviceBindingCategory.Range || category == DeviceBindingCategory.Delta)
            {
                result.ControlKind = ControlVisualKind.Axis;
                result.ControlLabel = ShortAxisLabel(leaf);
                result.ControlBrush = result.Device?.AccentBrush ?? NeutralBrush;
                return;
            }

            result.ControlKind = ControlVisualKind.Button;
            result.ControlLabel = CleanGenericLabel(leaf, binding.KeyValue);
            result.ControlBrush = result.Device?.AccentBrush ?? NeutralBrush;
        }

        private static void PopulateKnownControllerControl(BindingVisualDescriptor result, DeviceBinding binding,
            DeviceBindingCategory category, string leaf, DeviceVisualKind deviceKind)
        {
            var lower = (leaf ?? string.Empty).ToLowerInvariant();
            if (lower.Contains("dpad") || lower.Contains("pov") || lower == "up" || lower == "right" || lower == "down" || lower == "left")
            {
                result.ControlKind = ControlVisualKind.DPad;
                result.ControlLabel = DirectionLabel(leaf, binding.KeyValue);
                result.ControlBrush = result.Device.AccentBrush;
                return;
            }

            if (category == DeviceBindingCategory.Range)
            {
                var axisLabel = KnownAxisLabel(binding.KeyValue, deviceKind);
                result.ControlKind = axisLabel == "LT" || axisLabel == "RT" || axisLabel == "L2" || axisLabel == "R2"
                    ? ControlVisualKind.Trigger
                    : ControlVisualKind.StickAxis;
                result.ControlLabel = axisLabel;
                result.ControlBrush = result.Device.AccentBrush;
                return;
            }

            var button = KnownButtonLabel(binding.KeyValue, deviceKind);
            result.ControlLabel = button;
            if (deviceKind == DeviceVisualKind.Xbox && binding.KeyValue >= 0 && binding.KeyValue <= 3)
            {
                result.ControlKind = ControlVisualKind.XboxFaceButton;
                result.ControlBrush = XboxFaceBrush(button);
                return;
            }
            if (deviceKind == DeviceVisualKind.PlayStation && binding.KeyValue >= 0 && binding.KeyValue <= 3)
            {
                result.ControlKind = ControlVisualKind.PlayStationFaceButton;
                result.ControlBrush = PlayStationFaceBrush(button);
                return;
            }

            if (binding.KeyValue == 4 || binding.KeyValue == 5)
            {
                result.ControlKind = ControlVisualKind.ShoulderButton;
            }
            else if ((deviceKind == DeviceVisualKind.PlayStation && (binding.KeyValue == 10 || binding.KeyValue == 11)) ||
                     lower.Contains("trigger"))
            {
                result.ControlKind = ControlVisualKind.Trigger;
            }
            else
            {
                result.ControlKind = ControlVisualKind.Button;
            }
            result.ControlBrush = result.Device.AccentBrush;
        }

        private static string KnownButtonLabel(int keyValue, DeviceVisualKind kind)
        {
            if (kind == DeviceVisualKind.PlayStation)
            {
                switch (keyValue)
                {
                    case 0: return "×";
                    case 1: return "○";
                    case 2: return "□";
                    case 3: return "△";
                    case 4: return "L1";
                    case 5: return "R1";
                    case 6: return "L3";
                    case 7: return "R3";
                    case 8: return "SH";
                    case 9: return "OP";
                    case 10: return "L2";
                    case 11: return "R2";
                    case 12: return "PS";
                    case 13: return "TP";
                    default: return "B" + (keyValue + 1);
                }
            }

            switch (keyValue)
            {
                case 0: return "A";
                case 1: return "B";
                case 2: return "X";
                case 3: return "Y";
                case 4: return "LB";
                case 5: return "RB";
                case 6: return "LS";
                case 7: return "RS";
                case 8: return "BK";
                case 9: return "ST";
                default: return "B" + (keyValue + 1);
            }
        }

        private static string KnownAxisLabel(int keyValue, DeviceVisualKind kind)
        {
            switch (keyValue)
            {
                case 0: return "LX";
                case 1: return "LY";
                case 2: return "RX";
                case 3: return "RY";
                case 4: return kind == DeviceVisualKind.PlayStation ? "L2" : "LT";
                case 5: return kind == DeviceVisualKind.PlayStation ? "R2" : "RT";
                default: return "A" + (keyValue + 1);
            }
        }

        private static Brush XboxFaceBrush(string label)
        {
            switch (label)
            {
                case "A": return Freeze(Color.FromRgb(76, 175, 80));
                case "B": return Freeze(Color.FromRgb(229, 72, 72));
                case "X": return Freeze(Color.FromRgb(66, 147, 213));
                case "Y": return Freeze(Color.FromRgb(236, 188, 54));
                default: return XboxBrush;
            }
        }

        private static Brush PlayStationFaceBrush(string label)
        {
            switch (label)
            {
                case "×": return Freeze(Color.FromRgb(92, 145, 230));
                case "○": return Freeze(Color.FromRgb(229, 105, 118));
                case "□": return Freeze(Color.FromRgb(220, 123, 190));
                case "△": return Freeze(Color.FromRgb(89, 192, 142));
                default: return PlayStationBrush;
            }
        }

        private static string DirectionLabel(string leaf, int keyValue)
        {
            var lower = (leaf ?? string.Empty).ToLowerInvariant();
            if (lower.Contains("up")) return "↑";
            if (lower.Contains("right")) return "→";
            if (lower.Contains("down")) return "↓";
            if (lower.Contains("left")) return "←";
            switch (keyValue)
            {
                case 0: return "↑";
                case 1: return "→";
                case 2: return "↓";
                case 3: return "←";
                default: return "D";
            }
        }

        private static string CleanKeyboardLabel(string leaf)
        {
            if (string.IsNullOrWhiteSpace(leaf)) return "KEY";
            var value = leaf.Trim();
            value = value.Replace("Keyboard ", string.Empty).Replace("Key ", string.Empty);
            if (value.Length <= 5) return value.ToUpperInvariant();
            return value.Substring(0, 5).ToUpperInvariant();
        }

        private static string ShortMouseLabel(string leaf)
        {
            var lower = (leaf ?? string.Empty).ToLowerInvariant();
            if (lower.Contains("left")) return "L";
            if (lower.Contains("right")) return "R";
            if (lower.Contains("middle")) return "M";
            if (lower.Contains("xbutton1") || lower.Contains("button 4") || lower.EndsWith("4")) return "4";
            if (lower.Contains("xbutton2") || lower.Contains("button 5") || lower.EndsWith("5")) return "5";
            return CleanGenericLabel(leaf, 0);
        }

        private static string ShortAxisLabel(string leaf)
        {
            if (string.IsNullOrWhiteSpace(leaf)) return "AX";
            var value = leaf.Trim();
            value = value.Replace("Axis ", string.Empty).Replace("Axes ", string.Empty);
            if (value.Length <= 4) return value.ToUpperInvariant();
            return value.Substring(0, 4).ToUpperInvariant();
        }

        private static string CleanGenericLabel(string leaf, int keyValue)
        {
            if (string.IsNullOrWhiteSpace(leaf)) return (keyValue + 1).ToString();
            var value = leaf.Trim();
            value = value.Replace("Button ", "B").Replace("Buttons ", "B");
            if (value.Length <= 4) return value.ToUpperInvariant();
            return value.Substring(0, 4).ToUpperInvariant();
        }

        private static string ExtractLeaf(string boundName)
        {
            if (string.IsNullOrWhiteSpace(boundName)) return string.Empty;
            var parts = boundName.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 0 ? boundName.Trim() : parts[parts.Length - 1].Trim();
        }

        private static string SafeBoundName(DeviceBinding binding)
        {
            try
            {
                return binding.BoundName();
            }
            catch
            {
                return "Bound control";
            }
        }

        private static DeviceVisualDescriptor Build(DeviceVisualKind kind, Brush brush, string tooltip, int slotNumber, bool showSlot)
        {
            return new DeviceVisualDescriptor
            {
                Kind = kind,
                AccentBrush = brush,
                ToolTip = string.IsNullOrWhiteSpace(tooltip) ? "Device" : tooltip,
                SlotNumber = Math.Max(0, slotNumber),
                ShowSlotIndicator = showSlot
            };
        }

        private static DeviceVisualDescriptor Unknown(string tooltip)
        {
            return Build(DeviceVisualKind.Unknown, DirectInputBrush, tooltip, 0, false);
        }

        private static Brush Freeze(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}
