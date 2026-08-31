using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Xml.Serialization;
using HidWizards.IOWrapper.DataTransferObjects;
using HidWizards.UCR.Core.Models.Binding;
using NLog;

namespace HidWizards.UCR.Core.Models
{
    public enum DeviceIoType
    {
        Input,
        Output
    }

    public enum DeviceBindingTransferCompatibility
    {
        Unknown,
        Compatible,
        Incompatible
    }

    public enum DeviceAliasIdentityKind
    {
        HidPath,
        HardwareHandle,
        LogicalSlot
    }

    public enum DeviceOutlineColor
    {
        Default,
        Red,
        Green,
        Blue,
        Yellow,
        Cyan,
        Pink,
        Orange,
        Purple,
        White
    }

    public static class DeviceOutlineColors
    {
        public static readonly DeviceOutlineColor[] Options =
        {
            DeviceOutlineColor.Default,
            DeviceOutlineColor.Red,
            DeviceOutlineColor.Green,
            DeviceOutlineColor.Blue,
            DeviceOutlineColor.Yellow,
            DeviceOutlineColor.Cyan,
            DeviceOutlineColor.Pink,
            DeviceOutlineColor.Orange,
            DeviceOutlineColor.Purple,
            DeviceOutlineColor.White
        };

        public static string GetPresetHex(DeviceOutlineColor color)
        {
            switch (color)
            {
                case DeviceOutlineColor.Red: return "#E53935";
                case DeviceOutlineColor.Green: return "#00B34A";
                case DeviceOutlineColor.Blue: return "#1976FF";
                case DeviceOutlineColor.Yellow: return "#FFD600";
                case DeviceOutlineColor.Cyan: return "#00D5FF";
                case DeviceOutlineColor.Pink: return "#FF4081";
                case DeviceOutlineColor.Orange: return "#FF8A00";
                case DeviceOutlineColor.Purple: return "#9C4DFF";
                case DeviceOutlineColor.White: return "#FFFFFF";
                default: return null;
            }
        }

        public static string NormalizeHex(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var text = value.Trim();
            if (text.Length != 7 || text[0] != '#') return null;
            for (var i = 1; i < text.Length; i++)
            {
                var c = text[i];
                var isHex = c >= '0' && c <= '9' || c >= 'a' && c <= 'f' || c >= 'A' && c <= 'F';
                if (!isHex) return null;
            }
            return text.ToUpperInvariant();
        }

        public static string GenerateUniqueDefault(string stableKey, ISet<string> usedColors)
        {
            var used = usedColors ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var hash = StableHash(stableKey ?? string.Empty);

            // Work through a large deterministic colour space. Hue is deliberately stepped by a
            // prime-ish offset so adjacent devices do not bunch together visually. Saturation and
            // lightness vary too, giving us far more than the small named-preset palette.
            for (var attempt = 0; attempt < 4096; attempt++)
            {
                var hue = (int)((hash + (uint)(attempt * 137)) % 360);
                var saturation = 0.66 + (((hash >> 9) + (uint)(attempt * 17)) % 19) / 100.0;
                var lightness = 0.48 + (((hash >> 17) + (uint)(attempt * 11)) % 17) / 100.0;
                var candidate = HslToHex(hue, saturation, lightness);
                if (!used.Contains(candidate)) return candidate;
            }

            // This is practically unreachable for a device list, but retain deterministic
            // behaviour rather than silently sharing a colour if the normal space is exhausted.
            for (var value = 0; value <= 0xFFFFFF; value++)
            {
                var candidate = "#" + value.ToString("X6");
                if (!used.Contains(candidate)) return candidate;
            }
            return "#FFFFFF";
        }

        private static uint StableHash(string value)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (var c in value)
                {
                    hash ^= char.ToUpperInvariant(c);
                    hash *= 16777619;
                }
                return hash;
            }
        }

        private static string HslToHex(double hue, double saturation, double lightness)
        {
            var chroma = (1 - Math.Abs(2 * lightness - 1)) * saturation;
            var h = hue / 60.0;
            var x = chroma * (1 - Math.Abs(h % 2 - 1));
            double r1 = 0, g1 = 0, b1 = 0;
            if (h < 1) { r1 = chroma; g1 = x; }
            else if (h < 2) { r1 = x; g1 = chroma; }
            else if (h < 3) { g1 = chroma; b1 = x; }
            else if (h < 4) { g1 = x; b1 = chroma; }
            else if (h < 5) { r1 = x; b1 = chroma; }
            else { r1 = chroma; b1 = x; }
            var m = lightness - chroma / 2;
            var r = ClampByte((r1 + m) * 255);
            var g = ClampByte((g1 + m) * 255);
            var b = ClampByte((b1 + m) * 255);
            return string.Format("#{0:X2}{1:X2}{2:X2}", r, g, b);
        }

        private static int ClampByte(double value)
        {
            return Math.Max(0, Math.Min(255, (int)Math.Round(value)));
        }
    }

    public class DeviceAlias
    {
        [XmlAttribute]
        public string ProviderName { get; set; }
        [XmlAttribute]
        public DeviceAliasIdentityKind IdentityKind { get; set; }
        [XmlAttribute]
        public string IdentityValue { get; set; }
        [XmlAttribute]
        public int DeviceNumber { get; set; }
        [XmlAttribute]
        public string Alias { get; set; }
        [XmlAttribute]
        [DefaultValue(false)]
        public bool Hidden { get; set; }
        [XmlAttribute]
        [DefaultValue(false)]
        public bool Removed { get; set; }
        [XmlAttribute]
        [DefaultValue(int.MaxValue)]
        public int SortOrder { get; set; } = int.MaxValue;
        [XmlAttribute]
        [DefaultValue(DeviceOutlineColor.Default)]
        public DeviceOutlineColor OutlineColor { get; set; } = DeviceOutlineColor.Default;
        [XmlAttribute]
        public string DefaultOutlineColor { get; set; }

        [XmlIgnore]
        public bool HasPresentationSettings => !string.IsNullOrWhiteSpace(Alias) || Hidden || Removed ||
                                               SortOrder != int.MaxValue || OutlineColor != DeviceOutlineColor.Default ||
                                               !string.IsNullOrWhiteSpace(DefaultOutlineColor);

        public DeviceAlias Clone()
        {
            return new DeviceAlias
            {
                ProviderName = ProviderName,
                IdentityKind = IdentityKind,
                IdentityValue = IdentityValue,
                DeviceNumber = DeviceNumber,
                Alias = Alias,
                Hidden = Hidden,
                Removed = Removed,
                SortOrder = SortOrder,
                OutlineColor = OutlineColor,
                DefaultOutlineColor = DefaultOutlineColor
            };
        }
    }

    public sealed class DeviceBindingTransferResult
    {
        public DeviceBindingTransferCompatibility Compatibility { get; private set; }
        public int KeyType { get; private set; }
        public int KeyValue { get; private set; }
        public int KeySubValue { get; private set; }

        private DeviceBindingTransferResult(DeviceBindingTransferCompatibility compatibility,
            int keyType, int keyValue, int keySubValue)
        {
            Compatibility = compatibility;
            KeyType = keyType;
            KeyValue = keyValue;
            KeySubValue = keySubValue;
        }

        public static DeviceBindingTransferResult For(DeviceBindingTransferCompatibility compatibility,
            DeviceBinding binding)
        {
            return new DeviceBindingTransferResult(
                compatibility,
                binding?.KeyType ?? 0,
                binding?.KeyValue ?? 0,
                binding?.KeySubValue ?? 0);
        }

        public static DeviceBindingTransferResult Compatible(int keyType, int keyValue, int keySubValue)
        {
            return new DeviceBindingTransferResult(
                DeviceBindingTransferCompatibility.Compatible,
                keyType,
                keyValue,
                keySubValue);
        }
    }

    /// <summary>
    /// Determines whether an existing binding can be carried to another device without rebinding.
    /// Build A preserves bindings across devices with the same provider/schema. Build B additionally
    /// understands the confirmed semantic equivalence between the pinned Core_ViGEm Xbox 360 and
    /// DualShock 4 output layouts.
    /// </summary>
    public static class DeviceBindingCompatibility
    {
        private const string ViGEmProvider = "Core_ViGEm";
        private const string ViGEmXbox360 = "xb360";
        private const string ViGEmDs4 = "ds4";

        public static DeviceBindingTransferCompatibility Evaluate(Device sourceDevice, Device targetDevice,
            Context context, DeviceIoType deviceIoType, DeviceBinding deviceBinding,
            DeviceBindingCategory? expectedCategory = null)
        {
            return EvaluateTransfer(sourceDevice, targetDevice, context, deviceIoType, deviceBinding, expectedCategory)
                .Compatibility;
        }

        public static DeviceBindingTransferResult EvaluateTransfer(Device sourceDevice, Device targetDevice,
            Context context, DeviceIoType deviceIoType, DeviceBinding deviceBinding,
            DeviceBindingCategory? expectedCategory = null)
        {
            if (targetDevice == null || deviceBinding == null || !deviceBinding.IsBound)
            {
                return DeviceBindingTransferResult.For(DeviceBindingTransferCompatibility.Unknown, deviceBinding);
            }

            if (sourceDevice == null)
            {
                return DeviceBindingTransferResult.For(DeviceBindingTransferCompatibility.Unknown, deviceBinding);
            }

            if (object.ReferenceEquals(sourceDevice, targetDevice) || sourceDevice.Equals(targetDevice))
            {
                return DeviceBindingTransferResult.Compatible(
                    deviceBinding.KeyType, deviceBinding.KeyValue, deviceBinding.KeySubValue);
            }

            var sourceBindings = FlattenBindings(sourceDevice.GetDeviceBindingMenu(context, deviceIoType));
            var targetBindings = FlattenBindings(targetDevice.GetDeviceBindingMenu(context, deviceIoType));

            var semanticControllerTransfer = EvaluateKnownControllerTransfer(
                sourceDevice,
                targetDevice,
                sourceBindings,
                targetBindings,
                deviceBinding,
                expectedCategory);
            if (semanticControllerTransfer != null)
            {
                return semanticControllerTransfer;
            }

            if (!string.Equals(sourceDevice.ProviderName, targetDevice.ProviderName, StringComparison.OrdinalIgnoreCase))
            {
                return DeviceBindingTransferResult.For(DeviceBindingTransferCompatibility.Incompatible, deviceBinding);
            }

            // A disconnected device without a usable cache cannot be classified safely. Preserve rather
            // than destructively discarding the user's binding; it remains unresolved until available.
            if (sourceBindings.Count == 0 || targetBindings.Count == 0)
            {
                return DeviceBindingTransferResult.For(DeviceBindingTransferCompatibility.Unknown, deviceBinding);
            }

            if (!HaveSameSchema(sourceBindings, targetBindings))
            {
                return DeviceBindingTransferResult.For(DeviceBindingTransferCompatibility.Incompatible, deviceBinding);
            }

            if (ContainsBinding(targetBindings, deviceBinding.KeyType, deviceBinding.KeyValue,
                deviceBinding.KeySubValue, expectedCategory))
            {
                return DeviceBindingTransferResult.Compatible(
                    deviceBinding.KeyType, deviceBinding.KeyValue, deviceBinding.KeySubValue);
            }

            return DeviceBindingTransferResult.For(DeviceBindingTransferCompatibility.Incompatible, deviceBinding);
        }

        private static DeviceBindingTransferResult EvaluateKnownControllerTransfer(
            Device sourceDevice,
            Device targetDevice,
            List<DeviceBindingInfo> sourceBindings,
            List<DeviceBindingInfo> targetBindings,
            DeviceBinding deviceBinding,
            DeviceBindingCategory? expectedCategory)
        {
            if (!IsViGEmController(sourceDevice) || !IsViGEmController(targetDevice))
            {
                return null;
            }

            var sourceIsXbox = string.Equals(sourceDevice.DeviceHandle, ViGEmXbox360, StringComparison.OrdinalIgnoreCase);
            var targetIsXbox = string.Equals(targetDevice.DeviceHandle, ViGEmXbox360, StringComparison.OrdinalIgnoreCase);
            var sourceIsDs4 = string.Equals(sourceDevice.DeviceHandle, ViGEmDs4, StringComparison.OrdinalIgnoreCase);
            var targetIsDs4 = string.Equals(targetDevice.DeviceHandle, ViGEmDs4, StringComparison.OrdinalIgnoreCase);

            // Same-family ViGEm changes are handled by the ordinary exact-schema path.
            if ((sourceIsXbox && targetIsXbox) || (sourceIsDs4 && targetIsDs4))
            {
                return null;
            }

            if (!((sourceIsXbox && targetIsDs4) || (sourceIsDs4 && targetIsXbox)))
            {
                return null;
            }

            if (sourceBindings.Count == 0 || targetBindings.Count == 0)
            {
                return DeviceBindingTransferResult.For(DeviceBindingTransferCompatibility.Unknown, deviceBinding);
            }

            if (!ContainsBinding(sourceBindings, deviceBinding.KeyType, deviceBinding.KeyValue,
                deviceBinding.KeySubValue, expectedCategory))
            {
                return DeviceBindingTransferResult.For(DeviceBindingTransferCompatibility.Incompatible, deviceBinding);
            }

            var translatedKeyType = deviceBinding.KeyType;
            var translatedKeyValue = deviceBinding.KeyValue;
            var translatedKeySubValue = deviceBinding.KeySubValue;

            var bindingType = (BindingType)deviceBinding.KeyType;
            switch (bindingType)
            {
                case BindingType.Axis:
                    // Both pinned ViGEm layouts expose LX, LY, RX, RY and the two analogue triggers
                    // in indexes 0..5. The labels differ for the DS4 triggers, but their semantics do not.
                    if (deviceBinding.KeyValue < 0 || deviceBinding.KeyValue > 5)
                    {
                        return DeviceBindingTransferResult.For(DeviceBindingTransferCompatibility.Incompatible, deviceBinding);
                    }
                    break;

                case BindingType.Button:
                    // Common positional controls are deliberately aligned by the pinned provider:
                    // A/Cross, B/Circle, X/Square, Y/Triangle, LB/L1, RB/R1, LS, RS,
                    // Back/Share and Start/Options occupy indexes 0..9 in both layouts.
                    if (deviceBinding.KeyValue < 0 || deviceBinding.KeyValue > 9)
                    {
                        // DS4-only L2/R2 digital buttons, PS and TouchPad Click have no safe Xbox
                        // button-category equivalent. Do not silently coerce those to an axis or another key.
                        return DeviceBindingTransferResult.For(DeviceBindingTransferCompatibility.Incompatible, deviceBinding);
                    }
                    break;

                case BindingType.POV:
                    // Up, Right, Down, Left use indexes 0..3 in both layouts.
                    if (deviceBinding.KeyValue < 0 || deviceBinding.KeyValue > 3)
                    {
                        return DeviceBindingTransferResult.For(DeviceBindingTransferCompatibility.Incompatible, deviceBinding);
                    }
                    break;

                default:
                    return DeviceBindingTransferResult.For(DeviceBindingTransferCompatibility.Incompatible, deviceBinding);
            }

            if (!ContainsBinding(targetBindings, translatedKeyType, translatedKeyValue,
                translatedKeySubValue, expectedCategory))
            {
                return DeviceBindingTransferResult.For(DeviceBindingTransferCompatibility.Incompatible, deviceBinding);
            }

            return DeviceBindingTransferResult.Compatible(
                translatedKeyType, translatedKeyValue, translatedKeySubValue);
        }

        private static bool IsViGEmController(Device device)
        {
            if (device == null ||
                !string.Equals(device.ProviderName, ViGEmProvider, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return string.Equals(device.DeviceHandle, ViGEmXbox360, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(device.DeviceHandle, ViGEmDs4, StringComparison.OrdinalIgnoreCase);
        }

        private static bool ContainsBinding(IEnumerable<DeviceBindingInfo> bindings,
            int keyType, int keyValue, int keySubValue, DeviceBindingCategory? expectedCategory)
        {
            foreach (var bindingInfo in bindings)
            {
                if (bindingInfo.KeyType != keyType ||
                    bindingInfo.KeyValue != keyValue ||
                    bindingInfo.KeySubValue != keySubValue) continue;

                if (expectedCategory.HasValue && bindingInfo.DeviceBindingCategory != expectedCategory.Value) continue;
                return true;
            }

            return false;
        }

        private static bool HaveSameSchema(List<DeviceBindingInfo> sourceBindings, List<DeviceBindingInfo> targetBindings)
        {
            var sourceSchema = BuildSchema(sourceBindings);
            var targetSchema = BuildSchema(targetBindings);
            return sourceSchema.SetEquals(targetSchema);
        }

        private static HashSet<string> BuildSchema(IEnumerable<DeviceBindingInfo> bindings)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            foreach (var binding in bindings)
            {
                result.Add(BuildSchemaKey(binding));
            }
            return result;
        }

        private static string BuildSchemaKey(DeviceBindingInfo binding)
        {
            return ((int)binding.DeviceBindingCategory) + ":" + binding.KeyType + ":" + binding.KeyValue + ":" + binding.KeySubValue;
        }

        private static List<DeviceBindingInfo> FlattenBindings(IEnumerable<DeviceBindingNode> nodes)
        {
            var result = new List<DeviceBindingInfo>();
            if (nodes == null) return result;

            foreach (var node in nodes)
            {
                if (node == null) continue;
                if (node.DeviceBindingInfo != null) result.Add(node.DeviceBindingInfo);
                if (node.ChildrenNodes != null) result.AddRange(FlattenBindings(node.ChildrenNodes));
            }

            return result;
        }
    }

    public class Device
    {
        private static readonly NLog.Logger Logger = LogManager.GetCurrentClassLogger();

        /* Persistence */
        [XmlAttribute]
        public string Title { get; set; }
        [XmlAttribute]
        public string ProviderName { get; set; }
        [XmlAttribute]
        public string DeviceHandle { get; set; }
        [XmlAttribute]
        public int DeviceNumber { get; set; }
        [XmlAttribute]
        public string HidPath { get; set; }

        /* Runtime */
        [XmlIgnore]
        public string Alias { get; set; }
        [XmlIgnore]
        public string DisplayTitle => string.IsNullOrWhiteSpace(Alias) ? Title : Alias;
        [XmlIgnore]
        private List<DeviceBindingNode> DeviceBindingMenu { get; set; }
        [XmlIgnore] public Profile Profile { get; set; }
        [XmlIgnore] public bool IsCache { get; set; }
        // UCR logical ordinal for otherwise-identical physical devices. Provider DeviceNumber remains
        // untouched because IOWrapper still needs the raw endpoint slot. Old profiles omit this attribute
        // and therefore remain logical instance 1.
        [XmlAttribute]
        [DefaultValue(1)]
        public int LogicalInstanceNumber { get; set; } = 1;

        #region Constructors

        public Device()
        {
        }

        public Device(string title, string providerName, string deviceHandle, int deviceNumber)
        {
            Title = title;
            ProviderName = providerName;
            DeviceHandle = deviceHandle;
            DeviceNumber = deviceNumber;
        }

        public Device(DeviceReport device, ProviderReport providerReport, List<DeviceBindingNode> deviceBindingMenu) : this()
        {
            Title = device.DeviceName;
            ProviderName = providerReport.ProviderDescriptor.ProviderName;
            DeviceHandle = device.DeviceDescriptor.DeviceHandle;
            DeviceNumber = device.DeviceDescriptor.DeviceInstance;
            HidPath = device.HidPath;
            DeviceBindingMenu = deviceBindingMenu;
            IsCache = false;
        }

        public Device(DeviceCache deviceCache)
        {
            Title = deviceCache.Title;
            ProviderName = deviceCache.ProviderName;
            DeviceHandle = deviceCache.DeviceHandle;
            DeviceNumber = deviceCache.DeviceNumber;
            HidPath = deviceCache.HidPath;
            DeviceBindingMenu = deviceCache.DeviceBindingMenu;
            IsCache = true;
        }

        #endregion
        
        public string GetBindingName(DeviceBinding deviceBinding)
        {
            if (!deviceBinding.IsBound) return "Not bound";
            return GetBindingName(deviceBinding, GetDeviceBindingMenu(deviceBinding.Profile.Context, deviceBinding.DeviceIoType)) ?? "Unknown input";
        }

        private static string GetBindingName(DeviceBinding deviceBinding, List<DeviceBindingNode> deviceBindingNodes)
        {
            if (deviceBindingNodes == null) return null;
            foreach (var deviceBindingNode in deviceBindingNodes)
            {
                if (deviceBindingMatchesNode(deviceBinding, deviceBindingNode))
                {
                    return deviceBindingNode.Title;
                }
                var name = GetBindingName(deviceBinding, deviceBindingNode.ChildrenNodes);
                if (name != null)
                {
                    return deviceBindingNode.Title + ", " + name;
                }
            }
            return null;
        }

        private static bool deviceBindingMatchesNode(DeviceBinding deviceBinding, DeviceBindingNode deviceBindingNode)
        {
            return deviceBindingNode.IsBinding && 
                   deviceBindingNode.DeviceBindingInfo.KeyType == deviceBinding.KeyType &&
                   deviceBindingNode.DeviceBindingInfo.KeySubValue == deviceBinding.KeySubValue &&
                   deviceBindingNode.DeviceBindingInfo.KeyValue == deviceBinding.KeyValue;
        }

        public List<DeviceBindingNode> GetDeviceBindingMenu()
        {
            if (DeviceBindingMenu != null && DeviceBindingMenu.Count != 0) return DeviceBindingMenu;

            return new List<DeviceBindingNode>
            {
                new DeviceBindingNode()
                {
                    Title = "Device not connected",
                }
            };
        }

        public List<DeviceBindingNode> GetDeviceBindingMenu(Context context, DeviceIoType type)
        {
            if (DeviceBindingMenu != null && DeviceBindingMenu.Count != 0) return DeviceBindingMenu;

            return context.DevicesManager.GetDeviceBindingMenu(this, type);
        }

        public string LogName()
        {
            return $"Device:{{{Title}}} Alias:{{{Alias}}} Provider:{{{ProviderName}}} Handle:{{{DeviceHandle}}} Num:{{{DeviceNumber}}} HidPath:{{{HidPath}}}";
        }

        public override bool Equals(Object other)
        {
            if ((other == null) || GetType() != other.GetType()) return false;
            var otherDevice = other as Device;
            return string.Equals(ProviderName, otherDevice.ProviderName) && string.Equals(DeviceHandle, otherDevice.DeviceHandle) && DeviceNumber == otherDevice.DeviceNumber;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (ProviderName != null ? ProviderName.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (DeviceHandle != null ? DeviceHandle.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ DeviceNumber;
                return hashCode;
            }
        }
    }
}
