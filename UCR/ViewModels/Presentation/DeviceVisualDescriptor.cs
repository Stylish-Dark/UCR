using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;
using HidWizards.UCR.Core.Managers;
using HidWizards.UCR.Core.Models;
using HidWizards.UCR.Core.Models.Binding;

namespace HidWizards.UCR.ViewModels.Presentation
{
    public enum DeviceVisualKind
    {
        Unknown,
        Unavailable,
        Keyboard,
        Mouse,
        Gamepad,

        PlayStation1,
        PlayStation2,
        PlayStation3,
        PlayStation4,
        PlayStation5,

        XboxOriginal,
        Xbox360,
        XboxOne,
        XboxSeries,

        Nintendo64,
        GameCube,
        WiiRemote,
        WiiClassic,
        SwitchPro,
        SwitchJoyCon,

        VJoy,
        ArcadeStick,
        DirectInput
    }

    public enum ControlVisualKind
    {
        Unknown,
        DeviceUnavailable,
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
        public Brush BadgeTextBrush { get; set; }
        public Brush OutlineBrush { get; set; }
        public string ToolTip { get; set; }
        public int SlotNumber { get; set; }
        public bool ShowSlotIndicator { get; set; }
        public string BadgeText { get; set; }
        public string SymbolText { get; set; }
        public bool HasSymbol => !string.IsNullOrEmpty(SymbolText);
    }

    public sealed class BindingVisualDescriptor
    {
        public DeviceVisualDescriptor Device { get; set; }
        public ControlVisualKind ControlKind { get; set; }
        public Brush ControlBrush { get; set; }
        public string ControlLabel { get; set; }
        public string JoinSymbol { get; set; } = "+";
        public string ToolTip { get; set; }
        public bool IsBound { get; set; }
        public Guid DeviceConfigurationGuid { get; set; }
        public Guid BindingGuid { get; set; }
        public bool ShowDeviceBadge { get; set; }
        public bool IsBlockedInput { get; set; }
        public bool IsNegatedInput { get; set; }
        public bool IsFilterControl => ControlKind == ControlVisualKind.Filter;
    }

    public static class DeviceVisualCatalog
    {
        // User-editable glyph labels live next to the application, not inside UCR.exe.
        // Invalid or missing overrides never prevent UCR from starting.
        private static readonly Dictionary<string, string> GlyphLabelOverrides = ReadGlyphLabelOverrides();

        private static Dictionary<string, string> ReadGlyphLabelOverrides()
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                    "Assets", "Glyphs", "control-labels.ini");
                if (!File.Exists(path)) return result;
                foreach (var line in File.ReadAllLines(path))
                {
                    var trimmed = line.Trim();
                    if (trimmed.Length == 0 || trimmed.StartsWith("#")) continue;
                    var separator = trimmed.IndexOf('=');
                    if (separator <= 0) continue;
                    var key = trimmed.Substring(0, separator).Trim();
                    var label = trimmed.Substring(separator + 1).Trim();
                    if (key.Length > 0 && label.Length > 0 && label.Length <= 12)
                        result[key] = label;
                }
            }
            catch (Exception)
            {
                // The controls retain their built-in labels on file or parse errors.
            }
            return result;
        }

        private static string OverrideGlyphLabel(string category, string key, string fallback)
        {
            var normalized = (key ?? string.Empty).Trim();
            string replacement;
            return GlyphLabelOverrides.TryGetValue(category + "." + normalized, out replacement)
                ? replacement : fallback;
        }

        public static readonly Brush XboxBrush = Freeze(Color.FromRgb(0, 168, 0));
        public static readonly Brush PlayStationBrush = Freeze(Color.FromRgb(0, 105, 255));
        public static readonly Brush VJoyBrush = Freeze(Color.FromRgb(140, 0, 232));
        public static readonly Brush ArcadeBrush = Freeze(Color.FromRgb(216, 0, 0));
        public static readonly Brush NeutralBrush = Freeze(Color.FromRgb(202, 205, 210));
        public static readonly Brush DirectInputBrush = Freeze(Color.FromRgb(150, 166, 184));
        public static readonly Brush FilterBrush = Freeze(Color.FromRgb(0, 168, 42));

        public static DeviceVisualDescriptor Describe(DeviceConfiguration configuration, Profile profile, DeviceIoType ioType)
        {
            if (configuration == null)
            {
                return Unavailable("Device unavailable");
            }

            var descriptor = Describe(configuration.Device, ioType, profile?.Context?.DevicesManager);
            descriptor.ToolTip = configuration.GetFullTitleForProfile(profile);

            // Physical/generic provider device numbers can be large implementation identifiers
            // (Interception keyboards are a common example). Present those as K1/K2, M1/M2,
            // D1/D2, etc. in profile order instead of leaking raw provider numbers into the UI.
            if (profile != null && UsesProfileOrdinal(descriptor.Kind))
            {
                var ordinal = GetProfileOrdinal(configuration, profile, ioType, descriptor.Kind);
                descriptor.SlotNumber = ordinal;
                descriptor.ShowSlotIndicator = true;
                descriptor.BadgeText = BuildBadgeText(descriptor.Kind, ordinal);
            }

            return descriptor;
        }

        public static DeviceVisualDescriptor Describe(Device device, DeviceIoType ioType)
        {
            return Describe(device, ioType, null);
        }

        public static DeviceVisualDescriptor Describe(Device device, DeviceIoType ioType, DevicesManager devicesManager)
        {
            if (device == null) return Unavailable("Device unavailable");

            var provider = device.ProviderName ?? string.Empty;
            var handle = device.DeviceHandle ?? string.Empty;
            var title = (device.DisplayTitle ?? device.Title ?? string.Empty).Trim();
            var hidPath = device.HidPath ?? string.Empty;
            var searchable = (provider + " " + handle + " " + title + " " + hidPath).ToLowerInvariant();

            if (provider.Equals("Core_ViGEm", StringComparison.OrdinalIgnoreCase))
            {
                if (handle.Equals("ds4", StringComparison.OrdinalIgnoreCase))
                    return WithConfiguredPresentation(Build(DeviceVisualKind.PlayStation4, PlayStationBrush, title,
                        device.DeviceNumber + 1, ioType == DeviceIoType.Output), device, devicesManager);

                if (handle.Equals("xb360", StringComparison.OrdinalIgnoreCase))
                    return WithConfiguredPresentation(Build(DeviceVisualKind.Xbox360, XboxBrush, title,
                        device.DeviceNumber + 1, ioType == DeviceIoType.Output), device, devicesManager);
            }

            if (searchable.Contains("vjoy"))
                return WithConfiguredPresentation(Build(DeviceVisualKind.VJoy, VJoyBrush, title,
                    device.DeviceNumber + 1, true), device, devicesManager);

            if (provider.Equals("Core_Interception", StringComparison.OrdinalIgnoreCase))
            {
                if (searchable.Contains("mouse"))
                    return WithConfiguredPresentation(Build(DeviceVisualKind.Mouse, NeutralBrush, title,
                        device.DeviceNumber + 1, true), device, devicesManager);

                return WithConfiguredPresentation(Build(DeviceVisualKind.Keyboard, NeutralBrush, title,
                    device.DeviceNumber + 1, true), device, devicesManager);
            }

            if (searchable.Contains("keyboard"))
                return WithConfiguredPresentation(Build(DeviceVisualKind.Keyboard, NeutralBrush, title,
                    device.DeviceNumber + 1, true), device, devicesManager);
            if (searchable.Contains("mouse"))
                return WithConfiguredPresentation(Build(DeviceVisualKind.Mouse, NeutralBrush, title,
                    device.DeviceNumber + 1, true), device, devicesManager);

            if (ContainsAny(searchable, "dualsense", "ps5", "playstation 5", "cfizct"))
                return WithConfiguredPresentation(Build(DeviceVisualKind.PlayStation5, PlayStationBrush, title, device.DeviceNumber + 1, true), device, devicesManager);
            if (ContainsAny(searchable, "dualshock 4", "ds4", "ps4", "playstation 4", "cuh-zct") ||
                searchable.Contains("wireless controller") && searchable.Contains("vid_054c"))
                return WithConfiguredPresentation(Build(DeviceVisualKind.PlayStation4, PlayStationBrush, title, device.DeviceNumber + 1, true), device, devicesManager);
            if (ContainsAny(searchable, "dualshock 3", "sixaxis", "ps3", "playstation(r)3", "playstation 3"))
                return WithConfiguredPresentation(Build(DeviceVisualKind.PlayStation3, PlayStationBrush, title, device.DeviceNumber + 1, true), device, devicesManager);
            if (ContainsAny(searchable, "dualshock 2", "ps2", "playstation 2"))
                return WithConfiguredPresentation(Build(DeviceVisualKind.PlayStation2, PlayStationBrush, title, device.DeviceNumber + 1, true), device, devicesManager);
            if (ContainsAny(searchable, "ps1", "psx controller", "playstation controller"))
                return WithConfiguredPresentation(Build(DeviceVisualKind.PlayStation1, PlayStationBrush, title, device.DeviceNumber + 1, true), device, devicesManager);
            if (searchable.Contains("vid_054c"))
                return WithConfiguredPresentation(Build(DeviceVisualKind.PlayStation4, PlayStationBrush, title, device.DeviceNumber + 1, true), device, devicesManager);

            if (ContainsAny(searchable, "xbox series", "series x", "series s", "1914"))
                return WithConfiguredPresentation(Build(DeviceVisualKind.XboxSeries, XboxBrush, title, device.DeviceNumber + 1, true), device, devicesManager);
            if (ContainsAny(searchable, "xbox one", "xbox wireless", "1708", "1697", "1537"))
                return WithConfiguredPresentation(Build(DeviceVisualKind.XboxOne, XboxBrush, title, device.DeviceNumber + 1, true), device, devicesManager);
            if (ContainsAny(searchable, "xbox 360", "xbox360", "x360"))
                return WithConfiguredPresentation(Build(DeviceVisualKind.Xbox360, XboxBrush, title, device.DeviceNumber + 1, true), device, devicesManager);
            if (ContainsAny(searchable, "xbox duke", "duke controller", "controller s", "original xbox"))
                return WithConfiguredPresentation(Build(DeviceVisualKind.XboxOriginal, XboxBrush, title, device.DeviceNumber + 1, true), device, devicesManager);
            if (provider.Equals("SharpDX_XInput", StringComparison.OrdinalIgnoreCase) ||
                searchable.Contains("xinput") || searchable.Contains("xbox") || searchable.Contains("vid_045e"))
                return WithConfiguredPresentation(Build(DeviceVisualKind.Xbox360, XboxBrush, title, device.DeviceNumber + 1, true), device, devicesManager);

            if (ContainsAny(searchable, "joy-con", "joycon"))
                return WithConfiguredPresentation(Build(DeviceVisualKind.SwitchJoyCon, DirectInputBrush, title, device.DeviceNumber + 1, true), device, devicesManager);
            if ((searchable.Contains("switch") || searchable.Contains("nintendo")) && searchable.Contains("pro controller"))
                return WithConfiguredPresentation(Build(DeviceVisualKind.SwitchPro, DirectInputBrush, title, device.DeviceNumber + 1, true), device, devicesManager);
            if (ContainsAny(searchable, "wii remote", "wiimote"))
                return WithConfiguredPresentation(Build(DeviceVisualKind.WiiRemote, DirectInputBrush, title, device.DeviceNumber + 1, true), device, devicesManager);
            if (ContainsAny(searchable, "wii classic", "classic controller"))
                return WithConfiguredPresentation(Build(DeviceVisualKind.WiiClassic, DirectInputBrush, title, device.DeviceNumber + 1, true), device, devicesManager);
            if (ContainsAny(searchable, "gamecube", "game cube"))
                return WithConfiguredPresentation(Build(DeviceVisualKind.GameCube, DirectInputBrush, title, device.DeviceNumber + 1, true), device, devicesManager);
            if (ContainsAny(searchable, "nintendo 64", "n64"))
                return WithConfiguredPresentation(Build(DeviceVisualKind.Nintendo64, DirectInputBrush, title, device.DeviceNumber + 1, true), device, devicesManager);

            if (ContainsAny(searchable, "arcade", "fightstick", "fight stick"))
                return WithConfiguredPresentation(Build(DeviceVisualKind.ArcadeStick, ArcadeBrush, title, device.DeviceNumber + 1, true), device, devicesManager);

            if (ContainsAny(searchable, "gamepad", "controller", "joypad"))
                return WithConfiguredPresentation(Build(DeviceVisualKind.Gamepad, DirectInputBrush, title, device.DeviceNumber + 1, true), device, devicesManager);

            if (provider.Equals("SharpDX_DirectInput", StringComparison.OrdinalIgnoreCase) || searchable.Contains("directinput"))
                return WithConfiguredPresentation(Build(DeviceVisualKind.DirectInput, DirectInputBrush, title,
                    device.DeviceNumber + 1, true), device, devicesManager);

            return WithConfiguredPresentation(Build(DeviceVisualKind.Unknown, DirectInputBrush, title,
                device.DeviceNumber + 1, true), device, devicesManager);
        }

        private static bool ContainsAny(string value, params string[] fragments)
        {
            foreach (var fragment in fragments)
            {
                if (value.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        private static DeviceVisualDescriptor WithConfiguredPresentation(
            DeviceVisualDescriptor descriptor, Device device, DevicesManager devicesManager)
        {
            ApplyConfiguredBadgeTextColor(descriptor, device, devicesManager);
            return descriptor;
        }

        private static void ApplyConfiguredBadgeTextColor(
            DeviceVisualDescriptor descriptor, Device device, DevicesManager devicesManager)
        {
            if (descriptor == null || device == null) return;
            var manager = devicesManager ?? device.Profile?.Context?.DevicesManager;
            if (manager == null) return;

            // Device family owns the badge outline and semantic accent permanently. The persisted
            // colour choice (historically named OutlineColor in the XML model) now customizes only
            // the badge text, preserving existing user settings without changing the file format.
            var choice = manager.GetDeviceOutlineColor(device);
            if (choice == DeviceOutlineColor.Default) return;

            var brush = BrushFromHex(DeviceOutlineColors.GetPresetHex(choice));
            if (brush != null) descriptor.BadgeTextBrush = brush;
        }

        private static Brush BrushFromHex(string value)
        {
            var hex = DeviceOutlineColors.NormalizeHex(value);
            if (hex == null) return null;
            try
            {
                return Freeze(Color.FromRgb(
                    Convert.ToByte(hex.Substring(1, 2), 16),
                    Convert.ToByte(hex.Substring(3, 2), 16),
                    Convert.ToByte(hex.Substring(5, 2), 16)));
            }
            catch
            {
                return null;
            }
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
                    IsBound = false,
                    DeviceConfigurationGuid = Guid.Empty,
                    BindingGuid = Guid.Empty,
                    ShowDeviceBadge = false,
                    IsBlockedInput = false
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
                IsBound = binding.IsBound,
                DeviceConfigurationGuid = binding.DeviceConfigurationGuid,
                BindingGuid = binding.Guid,
                ShowDeviceBadge = false,
                IsBlockedInput = binding.DeviceIoType == DeviceIoType.Input && binding.Block
            };

            if (!binding.IsBound) return result;

            if (deviceDescriptor.Kind == DeviceVisualKind.Unavailable)
            {
                result.ControlKind = ControlVisualKind.DeviceUnavailable;
                result.ControlBrush = NeutralBrush;
                result.ControlLabel = string.Empty;
                return result;
            }

            if (deviceDescriptor.Kind == DeviceVisualKind.Unknown)
            {
                result.ControlKind = ControlVisualKind.Unknown;
                result.ControlBrush = DirectInputBrush;
                result.ControlLabel = "?";
                return result;
            }

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
                ToolTip = "Creates/defines filter — " + cleanName,
                IsBound = true,
                DeviceConfigurationGuid = Guid.Empty,
                BindingGuid = Guid.Empty,
                ShowDeviceBadge = false,
                IsBlockedInput = false
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
                result.ControlLabel = OverrideGlyphLabel("Keyboard", leaf, CleanKeyboardLabel(leaf));
                result.ControlBrush = result.Device?.BadgeTextBrush ?? NeutralBrush;
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

            if (IsXbox(kind) || IsPlayStation(kind))
            {
                PopulateKnownControllerControl(result, binding, category, leaf, kind);
                return;
            }

            if (lower.Contains("dpad") || lower.Contains("pov") ||
                lower == "up" || lower == "down" || lower == "left" || lower == "right")
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
                var axisLabel = KnownAxisLabelFromName(lower, deviceKind) ?? KnownAxisLabel(binding.KeyValue, deviceKind);
                result.ControlKind = axisLabel == "LT" || axisLabel == "RT" || axisLabel == "L2" || axisLabel == "R2"
                    ? ControlVisualKind.Trigger
                    : ControlVisualKind.StickAxis;
                result.ControlLabel = axisLabel;
                result.ControlBrush = result.Device.AccentBrush;
                return;
            }

            var button = KnownButtonLabelFromName(lower, deviceKind) ?? KnownButtonLabel(binding.KeyValue, deviceKind);
            result.ControlLabel = button;
            if (IsXbox(deviceKind) && binding.KeyValue >= 0 && binding.KeyValue <= 3)
            {
                result.ControlKind = ControlVisualKind.XboxFaceButton;
                result.ControlBrush = XboxFaceBrush(button);
                return;
            }
            if (IsPlayStation(deviceKind) && binding.KeyValue >= 0 && binding.KeyValue <= 3)
            {
                result.ControlKind = ControlVisualKind.PlayStationFaceButton;
                result.ControlBrush = PlayStationFaceBrush(button);
                return;
            }

            if (binding.KeyValue == 4 || binding.KeyValue == 5)
            {
                result.ControlKind = ControlVisualKind.ShoulderButton;
            }
            else if ((IsPlayStation(deviceKind) && (binding.KeyValue == 10 || binding.KeyValue == 11)) ||
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

        private static string KnownButtonLabelFromName(string lower, DeviceVisualKind kind)
        {
            if (string.IsNullOrWhiteSpace(lower)) return null;

            if (IsPlayStation(kind))
            {
                if (lower.Contains("cross")) return "×";
                if (lower.Contains("circle")) return "○";
                if (lower.Contains("square")) return "□";
                if (lower.Contains("triangle")) return "△";
                if (lower.Contains("l1")) return "L1";
                if (lower.Contains("r1")) return "R1";
                if (lower.Contains("l2")) return "L2";
                if (lower.Contains("r2")) return "R2";
                if (lower.Contains("l3")) return "L3";
                if (lower.Contains("r3")) return "R3";
                if (lower.Contains("share")) return "SH";
                if (lower.Contains("option")) return "OP";
                if (lower.Contains("touch")) return "TP";
                if (lower == "ps" || lower.Contains("ps button")) return "PS";
            }
            else
            {
                if (lower == "a" || lower.Contains("button a")) return "A";
                if (lower == "b" || lower.Contains("button b")) return "B";
                if (lower == "x" || lower.Contains("button x")) return "X";
                if (lower == "y" || lower.Contains("button y")) return "Y";
                if (lower.Contains("left shoulder") || lower.Contains("left bumper") || lower == "lb") return "LB";
                if (lower.Contains("right shoulder") || lower.Contains("right bumper") || lower == "rb") return "RB";
                if (lower.Contains("left thumb") || lower.Contains("left stick button") || lower == "ls") return "LS";
                if (lower.Contains("right thumb") || lower.Contains("right stick button") || lower == "rs") return "RS";
                if (lower.Contains("back") || lower.Contains("view")) return "BK";
                if (lower.Contains("start") || lower.Contains("menu")) return "ST";
            }
            return null;
        }

        private static string KnownAxisLabelFromName(string lower, DeviceVisualKind kind)
        {
            if (string.IsNullOrWhiteSpace(lower)) return null;
            // Triggers are analog pressure controls, never left/right stick axes.
            if (lower.Contains("left trigger") || lower == "lt" || lower == "l2" ||
                lower.Contains("trigger l") || lower.Contains("trigger left"))
                return IsPlayStation(kind) ? "L2" : "LT";
            if (lower.Contains("right trigger") || lower == "rt" || lower == "r2" ||
                lower.Contains("trigger r") || lower.Contains("trigger right"))
                return IsPlayStation(kind) ? "R2" : "RT";
            if ((lower.Contains("left") && lower.Contains("x")) || lower == "lx") return "LX";
            if ((lower.Contains("left") && lower.Contains("y")) || lower == "ly") return "LY";
            if ((lower.Contains("right") && lower.Contains("x")) || lower == "rx") return "RX";
            if ((lower.Contains("right") && lower.Contains("y")) || lower == "ry") return "RY";
            return null;
        }

        private static string KnownButtonLabel(int keyValue, DeviceVisualKind kind)
        {
            if (IsPlayStation(kind))
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
                case 4: return IsPlayStation(kind) ? "L2" : "LT";
                case 5: return IsPlayStation(kind) ? "R2" : "RT";
                default: return "A" + (keyValue + 1);
            }
        }

        private static Brush XboxFaceBrush(string label)
        {
            switch (label)
            {
                case "A": return Freeze(Color.FromRgb(0, 190, 80));
                case "B": return Freeze(Color.FromRgb(238, 55, 55));
                case "X": return Freeze(Color.FromRgb(40, 145, 255));
                case "Y": return Freeze(Color.FromRgb(238, 184, 0));
                default: return XboxBrush;
            }
        }

        private static Brush PlayStationFaceBrush(string label)
        {
            switch (label)
            {
                case "×": return Freeze(Color.FromRgb(70, 145, 255));
                case "○": return Freeze(Color.FromRgb(240, 80, 105));
                case "□": return Freeze(Color.FromRgb(225, 92, 190));
                case "△": return Freeze(Color.FromRgb(50, 205, 135));
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

        // The displayed label is intentionally distinct from the original binding name.
        // Every key uses the same square glyph; long names become symbols or two lines.
        // Tooltips still contain the full provider key name.
        public static string FormatKeyboardGlyphLabel(string leaf)
        {
            if (string.IsNullOrWhiteSpace(leaf)) return "KEY";
            var value = leaf.Trim();
            var normalized = System.Text.RegularExpressions.Regex.Replace(
                value.ToUpperInvariant(), @"[\s_\-]+", string.Empty);

            // Check the keypad BEFORE Enter, Home, End, Insert, arrows, etc.:
            // keypad keys can have those very same names without being the main key.
            var keypad = normalized.StartsWith("NUMPAD", StringComparison.Ordinal) ||
                         normalized.StartsWith("KEYPAD", StringComparison.Ordinal) ||
                         normalized.StartsWith("NUM", StringComparison.Ordinal) ||
                         normalized.StartsWith("KP", StringComparison.Ordinal);
            if (keypad)
            {
                var tail = normalized.StartsWith("NUMPAD") ? normalized.Substring(6) :
                    normalized.StartsWith("KEYPAD") ? normalized.Substring(6) :
                    normalized.StartsWith("NUM") ? normalized.Substring(3) : normalized.Substring(2);
                if (tail == "LOCK") return "NUM\nLK";
                if (tail.Length == 1 && char.IsDigit(tail[0])) return "NUM\n" + tail;
                if (tail == "RETURN" || tail == "ENTER") return "NUM\nENT";
                if (tail == "ADD" || tail == "PLUS" || tail == "+") return "NUM\n+";
                if (tail == "SUBTRACT" || tail == "MINUS" || tail == "-") return "NUM\n−";
                if (tail == "MULTIPLY" || tail == "ASTERISK" || tail == "*") return "NUM\n×";
                if (tail == "DIVIDE" || tail == "SLASH" || tail == "/") return "NUM\n÷";
                if (tail == "DECIMAL" || tail == "DOT" || tail == "DELETE" || tail == ".") return "NUM\n.";
                if (tail == "EQUALS" || tail == "=") return "NUM\n=";
                if (tail == "SEPARATOR" || tail == ",") return "NUM\n,";
            }

            var left = normalized.StartsWith("LEFT", StringComparison.Ordinal) ||
                       normalized.StartsWith("LCTRL", StringComparison.Ordinal) ||
                       normalized.StartsWith("LCONTROL", StringComparison.Ordinal) ||
                       normalized.StartsWith("LSHIFT", StringComparison.Ordinal) ||
                       normalized.StartsWith("LALT", StringComparison.Ordinal) ||
                       normalized.StartsWith("LWIN", StringComparison.Ordinal) ||
                       normalized.EndsWith("LEFT", StringComparison.Ordinal);
            var right = normalized.StartsWith("RIGHT", StringComparison.Ordinal) ||
                        normalized.StartsWith("RCTRL", StringComparison.Ordinal) ||
                        normalized.StartsWith("RCONTROL", StringComparison.Ordinal) ||
                        normalized.StartsWith("RSHIFT", StringComparison.Ordinal) ||
                        normalized.StartsWith("RALT", StringComparison.Ordinal) ||
                        normalized.StartsWith("RWIN", StringComparison.Ordinal) ||
                        normalized.EndsWith("RIGHT", StringComparison.Ordinal);
            var side = left ? "L" : right ? "R" : null;

            if (normalized.Contains("SHIFT")) return side == null ? "⇧" : side + "⇧";
            if (normalized.Contains("CONTROL") || normalized.Contains("CTRL"))
                return side == null ? "CTRL" : side + "\nCTRL";
            if (normalized.Contains("ALTGR")) return "ALT\nGR";
            if (normalized.Contains("ALT")) return side == null ? "ALT" : side + "\nALT";
            if (normalized.Contains("WINDOW") || normalized == "LWIN" || normalized == "RWIN" ||
                normalized == "WIN") return side == null ? "WIN" : side + "\nWIN";

            if (normalized == "RETURN" || normalized == "ENTER" || normalized == "RETURNKEY") return "↵";
            if (normalized == "BACKSPACE" || normalized == "BACK") return "⌫";
            if (normalized == "TAB") return "⇥";
            if (normalized == "SPACE" || normalized == "SPACEBAR") return "SPC";
            if (normalized == "ESCAPE" || normalized == "ESC") return "ESC";
            if (normalized == "CAPSLOCK") return "CAPS";
            if (normalized == "SCROLLLOCK") return "SCR\nLK";
            if (normalized == "PRINTSCREEN" || normalized == "PRTSC" || normalized == "SYSRQ") return "PRT\nSC";
            if (normalized == "PAGEUP" || normalized == "PGUP" || normalized == "PRIOR") return "PG\nUP";
            if (normalized == "PAGEDOWN" || normalized == "PGDN" || normalized == "NEXT") return "PG\nDN";
            if (normalized == "INSERT" || normalized == "INS") return "INS";
            if (normalized == "DELETE" || normalized == "DEL") return "DEL";
            if (normalized == "HOME") return "HOME";
            if (normalized == "END") return "END";
            if (normalized == "PAUSE" || normalized == "BREAK") return "PAUS";
            if (normalized == "APPLICATION" || normalized == "APPLICATIONS" || normalized == "MENUKEY")
                return "MENU";
            if (normalized == "ARROWUP" || normalized == "UP") return "↑";
            if (normalized == "ARROWRIGHT" || normalized == "RIGHT") return "→";
            if (normalized == "ARROWDOWN" || normalized == "DOWN") return "↓";
            if (normalized == "ARROWLEFT" || normalized == "LEFT") return "←";
            if (normalized == "COMMA" || normalized == "OEMCOMMA" || value == ",") return ",";
            if (normalized == "PERIOD" || normalized == "OEMPERIOD") return ".";
            if (normalized == "SEMICOLON" || normalized == "OEMSEMICOLON") return ";";

            value = value.Replace("Keyboard ", string.Empty).Replace("Key ", string.Empty).Trim();
            return value.Length <= 6 ? value.ToUpperInvariant() : value.Substring(0, 6).ToUpperInvariant();
        }

        private static string CleanKeyboardLabel(string leaf)
        {
            return FormatKeyboardGlyphLabel(leaf);
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
            // A comma key is serialized by the provider as "Keys,,".
            // Removing empty comma-delimited segments falsely identifies it as "Keys".
            if (boundName.TrimEnd().EndsWith(",,", StringComparison.Ordinal)) return ",";
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

        private static bool UsesProfileOrdinal(DeviceVisualKind kind)
        {
            return !IsXbox(kind) &&
                   !IsPlayStation(kind) &&
                   kind != DeviceVisualKind.VJoy &&
                   kind != DeviceVisualKind.Unavailable;
        }

        private static bool IsXbox(DeviceVisualKind kind)
        {
            return kind == DeviceVisualKind.XboxOriginal ||
                   kind == DeviceVisualKind.Xbox360 ||
                   kind == DeviceVisualKind.XboxOne ||
                   kind == DeviceVisualKind.XboxSeries;
        }

        private static bool IsPlayStation(DeviceVisualKind kind)
        {
            return kind == DeviceVisualKind.PlayStation1 ||
                   kind == DeviceVisualKind.PlayStation2 ||
                   kind == DeviceVisualKind.PlayStation3 ||
                   kind == DeviceVisualKind.PlayStation4 ||
                   kind == DeviceVisualKind.PlayStation5;
        }

        private static int GetProfileOrdinal(DeviceConfiguration target, Profile profile, DeviceIoType ioType, DeviceVisualKind kind)
        {
            var ordinal = 0;
            foreach (var configuration in profile.GetDeviceConfigurationList(ioType))
            {
                if (configuration?.Device == null) continue;
                var candidate = Describe(configuration.Device, ioType);
                if (candidate.Kind != kind) continue;
                ordinal++;
                if (configuration.Guid == target.Guid) return Math.Max(1, ordinal);
            }
            return 1;
        }

        private static DeviceVisualDescriptor Build(DeviceVisualKind kind, Brush brush, string tooltip, int slotNumber, bool showSlot)
        {
            var normalizedSlot = Math.Max(1, slotNumber);
            return new DeviceVisualDescriptor
            {
                Kind = kind,
                AccentBrush = brush,
                BadgeTextBrush = brush,
                OutlineBrush = brush,
                ToolTip = string.IsNullOrWhiteSpace(tooltip) ? "Device" : tooltip,
                SlotNumber = normalizedSlot,
                ShowSlotIndicator = showSlot,
                BadgeText = BuildBadgeText(kind, normalizedSlot),
                SymbolText = BuildSymbolText(kind)
            };
        }

        private static string BuildSymbolText(DeviceVisualKind kind)
        {
            if (kind == DeviceVisualKind.Keyboard) return "\uE001";
            if (IsXbox(kind)) return "\uE002";
            if (IsPlayStation(kind)) return "\uE003";
            if (kind == DeviceVisualKind.ArcadeStick) return "\uE005";

            if (kind == DeviceVisualKind.Gamepad ||
                kind == DeviceVisualKind.DirectInput ||
                kind == DeviceVisualKind.VJoy ||
                kind == DeviceVisualKind.Nintendo64 ||
                kind == DeviceVisualKind.GameCube ||
                kind == DeviceVisualKind.WiiRemote ||
                kind == DeviceVisualKind.WiiClassic ||
                kind == DeviceVisualKind.SwitchPro ||
                kind == DeviceVisualKind.SwitchJoyCon ||
                kind == DeviceVisualKind.Unknown)
            {
                return "\uE004";
            }

            return string.Empty;
        }

        private static string BuildBadgeText(DeviceVisualKind kind, int slotNumber)
        {
            var prefix = "U";
            if (kind == DeviceVisualKind.Keyboard) prefix = "K";
            else if (kind == DeviceVisualKind.Mouse) prefix = "M";
            else if (IsXbox(kind)) prefix = "X";
            else if (IsPlayStation(kind)) prefix = "P";
            else if (kind == DeviceVisualKind.VJoy) prefix = "V";
            else if (kind == DeviceVisualKind.ArcadeStick) prefix = "A";
            else if (kind == DeviceVisualKind.DirectInput || kind == DeviceVisualKind.Gamepad) prefix = "D";
            else if (kind == DeviceVisualKind.Nintendo64 || kind == DeviceVisualKind.GameCube ||
                     kind == DeviceVisualKind.WiiRemote || kind == DeviceVisualKind.WiiClassic ||
                     kind == DeviceVisualKind.SwitchPro || kind == DeviceVisualKind.SwitchJoyCon) prefix = "N";
            return prefix + Math.Max(1, slotNumber);
        }

        private static DeviceVisualDescriptor Unavailable(string tooltip)
        {
            return Build(DeviceVisualKind.Unavailable, NeutralBrush, tooltip, 0, false);
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
