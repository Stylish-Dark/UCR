using System;
using System.Collections.Generic;
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

        Nes,
        Snes,
        Nintendo64,
        GameCube,
        Dreamcast,
        WiiRemote,
        WiiClassic,
        WiiUPro,
        SwitchJoyCon,
        SwitchPro,
        SteamController,

        MegaDrive3,
        MegaDrive6,
        SegaSaturn,
        MasterSystem,
        AtariJoystick,

        VJoy,
        ArcadeStick,
        SteeringWheel,
        FlightStick,
        Pedals,
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
        public Brush GlyphBrush { get; set; }
        public Brush OutlineBrush { get; set; }
        public string ToolTip { get; set; }
        public int SlotNumber { get; set; }
        public bool ShowSlotIndicator { get; set; }
        public string BadgeText { get; set; }
    }

    public sealed class BindingVisualDescriptor
    {
        public DeviceVisualDescriptor Device { get; set; }
        public ControlVisualKind ControlKind { get; set; }
        public Brush ControlBrush { get; set; }
        public string ControlLabel { get; set; }
        public string ToolTip { get; set; }
        public bool IsBound { get; set; }
        public Guid DeviceConfigurationGuid { get; set; }
        public Guid BindingGuid { get; set; }
        public bool ShowDeviceBadge { get; set; }
        public bool IsBlockedInput { get; set; }
        public bool IsFilterControl => ControlKind == ControlVisualKind.Filter;
    }

    public static class DeviceVisualCatalog
    {
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

            // Virtual devices have authoritative provider/handle identities.
            if (provider.Equals("Core_ViGEm", StringComparison.OrdinalIgnoreCase))
            {
                if (handle.Equals("ds4", StringComparison.OrdinalIgnoreCase))
                {
                    return WithConfiguredPresentation(Build(DeviceVisualKind.PlayStation4, PlayStationBrush, title,
                        device.DeviceNumber + 1, ioType == DeviceIoType.Output), device, devicesManager);
                }
                if (handle.Equals("xb360", StringComparison.OrdinalIgnoreCase))
                {
                    return WithConfiguredPresentation(Build(DeviceVisualKind.Xbox360, XboxBrush, title,
                        device.DeviceNumber + 1, ioType == DeviceIoType.Output), device, devicesManager);
                }
            }

            if (searchable.Contains("vjoy"))
            {
                return WithConfiguredPresentation(Build(DeviceVisualKind.VJoy, VJoyBrush, title,
                    device.DeviceNumber + 1, true), device, devicesManager);
            }

            // PC input devices.
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

            // Sony generations. Keep specific generations ahead of the generic VID match.
            if (ContainsAny(searchable, "dualsense", "ps5", "playstation 5", "cfizct"))
                return WithConfiguredPresentation(Build(DeviceVisualKind.PlayStation5, PlayStationBrush, title, device.DeviceNumber + 1, true), device, devicesManager);
            if (ContainsAny(searchable, "dualshock 4", "ds4", "ps4", "playstation 4", "cuh-zct", "wireless controller"))
                return WithConfiguredPresentation(Build(DeviceVisualKind.PlayStation4, PlayStationBrush, title, device.DeviceNumber + 1, true), device, devicesManager);
            if (ContainsAny(searchable, "dualshock 3", "sixaxis", "ps3", "playstation(r)3", "playstation 3"))
                return WithConfiguredPresentation(Build(DeviceVisualKind.PlayStation3, PlayStationBrush, title, device.DeviceNumber + 1, true), device, devicesManager);
            if (ContainsAny(searchable, "dualshock 2", "ps2", "playstation 2"))
                return WithConfiguredPresentation(Build(DeviceVisualKind.PlayStation2, PlayStationBrush, title, device.DeviceNumber + 1, true), device, devicesManager);
            if (ContainsAny(searchable, "ps1", "psx controller", "playstation controller"))
                return WithConfiguredPresentation(Build(DeviceVisualKind.PlayStation1, PlayStationBrush, title, device.DeviceNumber + 1, true), device, devicesManager);
            if (searchable.Contains("vid_054c"))
                return WithConfiguredPresentation(Build(DeviceVisualKind.PlayStation4, PlayStationBrush, title, device.DeviceNumber + 1, true), device, devicesManager);

            // Xbox generations.
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

            // Nintendo.
            if (ContainsAny(searchable, "joy-con", "joycon"))
                return WithConfiguredPresentation(Build(DeviceVisualKind.SwitchJoyCon, DirectInputBrush, title, device.DeviceNumber + 1, true), device, devicesManager);
            if ((searchable.Contains("switch") || searchable.Contains("nintendo")) && searchable.Contains("pro controller"))
                return WithConfiguredPresentation(Build(DeviceVisualKind.SwitchPro, DirectInputBrush, title, device.DeviceNumber + 1, true), device, devicesManager);
            if (ContainsAny(searchable, "wii u pro", "wiiu pro"))
                return WithConfiguredPresentation(Build(DeviceVisualKind.WiiUPro, DirectInputBrush, title, device.DeviceNumber + 1, true), device, devicesManager);
            if (ContainsAny(searchable, "wii remote", "wiimote"))
                return WithConfiguredPresentation(Build(DeviceVisualKind.WiiRemote, DirectInputBrush, title, device.DeviceNumber + 1, true), device, devicesManager);
            if (ContainsAny(searchable, "wii classic", "classic controller"))
                return WithConfiguredPresentation(Build(DeviceVisualKind.WiiClassic, DirectInputBrush, title, device.DeviceNumber + 1, true), device, devicesManager);
            if (ContainsAny(searchable, "gamecube", "game cube"))
                return WithConfiguredPresentation(Build(DeviceVisualKind.GameCube, DirectInputBrush, title, device.DeviceNumber + 1, true), device, devicesManager);
            if (ContainsAny(searchable, "nintendo 64", "n64"))
                return WithConfiguredPresentation(Build(DeviceVisualKind.Nintendo64, DirectInputBrush, title, device.DeviceNumber + 1, true), device, devicesManager);
            if (ContainsAny(searchable, "super nintendo", "snes"))
                return WithConfiguredPresentation(Build(DeviceVisualKind.Snes, DirectInputBrush, title, device.DeviceNumber + 1, true), device, devicesManager);
            if (ContainsAny(searchable, "nintendo entertainment", "nes controller", "nes gamepad"))
                return WithConfiguredPresentation(Build(DeviceVisualKind.Nes, DirectInputBrush, title, device.DeviceNumber + 1, true), device, devicesManager);

            // Sega / retro.
            if (searchable.Contains("dreamcast"))
                return WithConfiguredPresentation(Build(DeviceVisualKind.Dreamcast, DirectInputBrush, title, device.DeviceNumber + 1, true), device, devicesManager);
            if (searchable.Contains("saturn"))
                return WithConfiguredPresentation(Build(DeviceVisualKind.SegaSaturn, DirectInputBrush, title, device.DeviceNumber + 1, true), device, devicesManager);
            if (ContainsAny(searchable, "mega drive", "megadrive", "genesis"))
                return WithConfiguredPresentation(Build(ContainsAny(searchable, "6 button", "six button") ? DeviceVisualKind.MegaDrive6 : DeviceVisualKind.MegaDrive3,
                    DirectInputBrush, title, device.DeviceNumber + 1, true), device, devicesManager);
            if (searchable.Contains("master system"))
                return WithConfiguredPresentation(Build(DeviceVisualKind.MasterSystem, DirectInputBrush, title, device.DeviceNumber + 1, true), device, devicesManager);
            if (ContainsAny(searchable, "atari joystick", "atari 2600"))
                return WithConfiguredPresentation(Build(DeviceVisualKind.AtariJoystick, DirectInputBrush, title, device.DeviceNumber + 1, true), device, devicesManager);

            if (searchable.Contains("steam controller"))
                return WithConfiguredPresentation(Build(DeviceVisualKind.SteamController, DirectInputBrush, title, device.DeviceNumber + 1, true), device, devicesManager);
            if (ContainsAny(searchable, "arcade", "fightstick", "fight stick"))
                return WithConfiguredPresentation(Build(DeviceVisualKind.ArcadeStick, ArcadeBrush, title, device.DeviceNumber + 1, true), device, devicesManager);
            if (ContainsAny(searchable, "steering wheel", "racing wheel", "wheel"))
                return WithConfiguredPresentation(Build(DeviceVisualKind.SteeringWheel, DirectInputBrush, title, device.DeviceNumber + 1, true), device, devicesManager);
            if (ContainsAny(searchable, "hotas", "flight stick", "flightstick", "joystick"))
                return WithConfiguredPresentation(Build(DeviceVisualKind.FlightStick, DirectInputBrush, title, device.DeviceNumber + 1, true), device, devicesManager);
            if (ContainsAny(searchable, "pedals", "pedal"))
                return WithConfiguredPresentation(Build(DeviceVisualKind.Pedals, DirectInputBrush, title, device.DeviceNumber + 1, true), device, devicesManager);

            if (ContainsAny(searchable, "gamepad", "controller", "joypad"))
                return WithConfiguredPresentation(Build(DeviceVisualKind.Gamepad, DirectInputBrush, title, device.DeviceNumber + 1, true), device, devicesManager);

            if (provider.Equals("SharpDX_DirectInput", StringComparison.OrdinalIgnoreCase) || searchable.Contains("directinput"))
                return WithConfiguredPresentation(Build(DeviceVisualKind.DirectInput, DirectInputBrush, title, device.DeviceNumber + 1, true), device, devicesManager);

            return WithConfiguredPresentation(Build(DeviceVisualKind.Unknown, DirectInputBrush, title, device.DeviceNumber + 1, true), device, devicesManager);
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
            if (brush != null)
            {
                descriptor.BadgeTextBrush = brush;
                descriptor.GlyphBrush = brush;
            }
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

        private static string KnownButtonLabelFromName(string lower, DeviceVisualKind kind)
        {
            if (string.IsNullOrWhiteSpace(lower)) return null;

            if (kind == DeviceVisualKind.PlayStation)
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
            if ((lower.Contains("left") && lower.Contains("x")) || lower.Contains("lx")) return "LX";
            if ((lower.Contains("left") && lower.Contains("y")) || lower.Contains("ly")) return "LY";
            if ((lower.Contains("right") && lower.Contains("x")) || lower.Contains("rx")) return "RX";
            if ((lower.Contains("right") && lower.Contains("y")) || lower.Contains("ry")) return "RY";
            if (lower.Contains("left trigger") || lower.Contains("l2")) return kind == DeviceVisualKind.PlayStation ? "L2" : "LT";
            if (lower.Contains("right trigger") || lower.Contains("r2")) return kind == DeviceVisualKind.PlayStation ? "R2" : "RT";
            return null;
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

        private static string CleanKeyboardLabel(string leaf)
        {
            if (string.IsNullOrWhiteSpace(leaf)) return "KEY";
            var value = leaf.Trim();
            var lower = value.ToLowerInvariant();

            if (lower.Contains("window") || lower == "lwin" || lower == "rwin" || lower.Contains(" win")) return "WIN";
            if (lower.Contains("control") || lower == "lctrl" || lower == "rctrl") return "CTRL";
            if (lower.Contains("shift")) return "SHIFT";
            if (lower.Contains("altgr")) return "ALTGR";
            if (lower.Contains(" alt") || lower.StartsWith("alt") || lower == "lalt" || lower == "ralt") return "ALT";
            if (lower.Contains("escape")) return "ESC";
            if (lower.Contains("backspace")) return "BKSP";
            if (lower == "return" || lower.Contains("enter")) return "ENTER";
            if (lower.Contains("space")) return "SPACE";
            if (lower == "tab" || lower.Contains(" tab")) return "TAB";
            if (lower.Contains("caps lock")) return "CAPS";
            if (lower.Contains("num lock")) return "NUM";
            if (lower.Contains("scroll lock")) return "SCRL";
            if (lower.Contains("print screen") || lower.Contains("printscreen")) return "PRTSC";
            if (lower.Contains("page up")) return "PGUP";
            if (lower.Contains("page down")) return "PGDN";
            if (lower == "insert" || lower.EndsWith(" insert")) return "INS";
            if (lower == "delete" || lower.EndsWith(" delete")) return "DEL";
            if (lower == "home" || lower.EndsWith(" home")) return "HOME";
            if (lower == "end" || lower.EndsWith(" end")) return "END";
            if (lower.Contains("arrow up") || lower == "up") return "↑";
            if (lower.Contains("arrow right") || lower == "right") return "→";
            if (lower.Contains("arrow down") || lower == "down") return "↓";
            if (lower.Contains("arrow left") || lower == "left") return "←";
            if (lower.Contains("applications") || lower.Contains("menu key")) return "MENU";

            var numMatch = System.Text.RegularExpressions.Regex.Match(lower, @"(?:num(?:pad)?|keypad)\s*([0-9])");
            if (numMatch.Success) return "NUM " + numMatch.Groups[1].Value;
            if (lower.Contains("numpad") || lower.Contains("keypad") || lower.StartsWith("num "))
            {
                if (lower.Contains("add") || lower.Contains("plus")) return "NUM +";
                if (lower.Contains("subtract") || lower.Contains("minus")) return "NUM -";
                if (lower.Contains("multiply")) return "NUM ×";
                if (lower.Contains("divide")) return "NUM /";
                if (lower.Contains("decimal")) return "NUM .";
            }

            value = value.Replace("Keyboard ", string.Empty).Replace("Key ", string.Empty).Trim();
            if (value.Length <= 6) return value.ToUpperInvariant();
            return value.Substring(0, 6).ToUpperInvariant();
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
            var glyphBrush = kind == DeviceVisualKind.Unavailable ? NeutralBrush : SlotBrush(normalizedSlot);
            return new DeviceVisualDescriptor
            {
                Kind = kind,
                AccentBrush = brush,
                BadgeTextBrush = glyphBrush,
                GlyphBrush = glyphBrush,
                OutlineBrush = brush,
                ToolTip = string.IsNullOrWhiteSpace(tooltip) ? "Device" : tooltip,
                SlotNumber = normalizedSlot,
                ShowSlotIndicator = showSlot,
                BadgeText = BuildBadgeText(kind, normalizedSlot)
            };
        }

        private static Brush SlotBrush(int slotNumber)
        {
            switch (((Math.Max(1, slotNumber) - 1) % 8) + 1)
            {
                case 1: return Freeze(Color.FromRgb(77, 163, 255));   // player/device 1 — blue
                case 2: return Freeze(Color.FromRgb(255, 93, 93));   // 2 — red
                case 3: return Freeze(Color.FromRgb(89, 201, 109));  // 3 — green
                case 4: return Freeze(Color.FromRgb(241, 200, 75));  // 4 — yellow
                case 5: return Freeze(Color.FromRgb(177, 116, 255)); // 5 — violet
                case 6: return Freeze(Color.FromRgb(77, 213, 220));  // 6 — cyan
                case 7: return Freeze(Color.FromRgb(255, 151, 72));  // 7 — orange
                default: return Freeze(Color.FromRgb(239, 113, 188));// 8 — pink
            }
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
            else if (kind == DeviceVisualKind.SteeringWheel) prefix = "W";
            else if (kind == DeviceVisualKind.FlightStick) prefix = "F";
            else if (kind == DeviceVisualKind.Pedals) prefix = "PD";
            else if (kind == DeviceVisualKind.DirectInput || kind == DeviceVisualKind.Gamepad) prefix = "D";
            else if (kind == DeviceVisualKind.Nintendo64 || kind == DeviceVisualKind.Nes ||
                     kind == DeviceVisualKind.Snes || kind == DeviceVisualKind.GameCube ||
                     kind == DeviceVisualKind.WiiRemote || kind == DeviceVisualKind.WiiClassic ||
                     kind == DeviceVisualKind.WiiUPro || kind == DeviceVisualKind.SwitchJoyCon ||
                     kind == DeviceVisualKind.SwitchPro) prefix = "N";
            else if (kind == DeviceVisualKind.Dreamcast || kind == DeviceVisualKind.MegaDrive3 ||
                     kind == DeviceVisualKind.MegaDrive6 || kind == DeviceVisualKind.SegaSaturn ||
                     kind == DeviceVisualKind.MasterSystem) prefix = "S";
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
