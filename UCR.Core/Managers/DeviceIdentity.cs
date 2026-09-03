using System;
using System.Text.RegularExpressions;
using HidWizards.UCR.Core.Models;

namespace HidWizards.UCR.Core.Managers
{
    /// <summary>
    /// Canonical device identity policy. Provider descriptors, persisted physical identity,
    /// logical user-facing identity and alias identity are deliberately distinct concepts;
    /// this class is the single place that defines how each one is compared or constructed.
    /// </summary>
    public static class DeviceIdentity
    {
        private static readonly Regex CoreInterceptionSlotSuffix =
            new Regex(@"\s+#\d+\s*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static string GetLogicalTitle(Device device)
        {
            if (device == null) return string.Empty;
            var title = (device.Title ?? string.Empty).Trim();
            if (!string.Equals(device.ProviderName, "Core_Interception", StringComparison.OrdinalIgnoreCase))
            {
                return title;
            }

            return CoreInterceptionSlotSuffix.Replace(title, string.Empty).Trim();
        }

        public static string BuildLogicalKey(Device device)
        {
            if (device == null || string.IsNullOrWhiteSpace(device.ProviderName)) return null;
            var provider = device.ProviderName.Trim();

            if (string.Equals(provider, "Core_Interception", StringComparison.OrdinalIgnoreCase))
            {
                var handle = (device.DeviceHandle ?? string.Empty).Trim();
                var title = GetLogicalTitle(device);
                var family = title.StartsWith("K:", StringComparison.OrdinalIgnoreCase) ||
                             handle.StartsWith("Keyboard", StringComparison.OrdinalIgnoreCase)
                    ? "keyboard"
                    : title.StartsWith("M:", StringComparison.OrdinalIgnoreCase) ||
                      handle.StartsWith("Mouse", StringComparison.OrdinalIgnoreCase)
                        ? "mouse"
                        : "input";
                var hardwareIdentity = !string.IsNullOrWhiteSpace(handle) ? handle : title;
                return provider + "|logical-physical|" + family + "|" + hardwareIdentity;
            }

            if (!string.IsNullOrWhiteSpace(device.HidPath))
                return provider + "|hid|" + device.HidPath.Trim();

            if (UsesLogicalSlotIdentity(provider))
                return provider + "|slot|" + (device.DeviceHandle ?? string.Empty).Trim() + "|" + device.DeviceNumber;

            if (!string.IsNullOrWhiteSpace(device.DeviceHandle))
                return provider + "|handle|" + device.DeviceHandle.Trim();

            return provider + "|descriptor|" + device.DeviceNumber + "|" + GetLogicalTitle(device);
        }

        public static string BuildRuntimeEndpointKey(Device device)
        {
            if (device == null) return null;
            return (device.ProviderName ?? string.Empty) + "|" +
                   (device.DeviceHandle ?? string.Empty) + "|" + device.DeviceNumber + "|" +
                   (device.HidPath ?? string.Empty);
        }

        public static bool DescriptorEquals(Device left, Device right)
        {
            if (left == null || right == null) return false;
            return string.Equals(left.ProviderName, right.ProviderName, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(left.DeviceHandle, right.DeviceHandle, StringComparison.OrdinalIgnoreCase)
                   && left.DeviceNumber == right.DeviceNumber;
        }

        public static bool PersistedEquals(Device left, Device right)
        {
            if (left == null || right == null) return false;
            if (!string.Equals(left.ProviderName, right.ProviderName, StringComparison.OrdinalIgnoreCase)) return false;

            if (!string.IsNullOrEmpty(left.HidPath) && !string.IsNullOrEmpty(right.HidPath))
            {
                return string.Equals(left.HidPath, right.HidPath, StringComparison.OrdinalIgnoreCase);
            }

            return DescriptorEquals(left, right);
        }

        public static bool LogicalEquals(Device left, Device right)
        {
            if (left == null || right == null) return false;
            if (!string.Equals(left.ProviderName, right.ProviderName, StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.Equals(left.ProviderName, "Core_Interception", StringComparison.OrdinalIgnoreCase))
                return PersistedEquals(left, right);

            var leftKey = BuildLogicalKey(left);
            var rightKey = BuildLogicalKey(right);
            return leftKey != null && rightKey != null &&
                   string.Equals(leftKey, rightKey, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Equality used by user-selection surfaces. Exact provider descriptors match immediately;
        /// otherwise a reconciled logical device must also have the same logical ordinal.
        /// </summary>
        public static bool SelectionEquals(Device left, Device right)
        {
            if (left == null || right == null) return false;
            return DescriptorEquals(left, right) ||
                   (LogicalEquals(left, right) && left.LogicalInstanceNumber == right.LogicalInstanceNumber);
        }

        public static bool CacheRepresentsLiveEndpoint(Device cachedDevice, Device liveDevice)
        {
            if (cachedDevice == null || liveDevice == null) return false;
            if (!string.Equals(cachedDevice.ProviderName, liveDevice.ProviderName, StringComparison.OrdinalIgnoreCase)) return false;

            if (!string.IsNullOrWhiteSpace(cachedDevice.HidPath) && !string.IsNullOrWhiteSpace(liveDevice.HidPath))
            {
                return string.Equals(cachedDevice.HidPath, liveDevice.HidPath, StringComparison.OrdinalIgnoreCase);
            }

            return !string.IsNullOrWhiteSpace(cachedDevice.DeviceHandle) &&
                   string.Equals(cachedDevice.DeviceHandle, liveDevice.DeviceHandle, StringComparison.OrdinalIgnoreCase);
        }

        public static bool UsesLogicalSlotIdentity(string providerName)
        {
            return string.Equals(providerName, "SharpDX_XInput", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(providerName, "Core_ViGEm", StringComparison.OrdinalIgnoreCase);
        }

        public static bool AliasEquals(DeviceAlias left, DeviceAlias right)
        {
            if (left == null || right == null) return false;
            return left.IdentityKind == right.IdentityKind
                   && left.DeviceNumber == right.DeviceNumber
                   && string.Equals(left.ProviderName, right.ProviderName, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(left.IdentityValue, right.IdentityValue, StringComparison.OrdinalIgnoreCase);
        }

        public static DeviceAlias BuildAliasIdentity(Device device)
        {
            if (device == null || string.IsNullOrWhiteSpace(device.ProviderName)) return null;

            if (string.Equals(device.ProviderName, "Core_Interception", StringComparison.OrdinalIgnoreCase))
            {
                var logicalKey = BuildLogicalKey(device);
                if (string.IsNullOrWhiteSpace(logicalKey)) return null;
                return new DeviceAlias
                {
                    ProviderName = device.ProviderName,
                    IdentityKind = DeviceAliasIdentityKind.HardwareHandle,
                    IdentityValue = logicalKey,
                    DeviceNumber = Math.Max(0, device.LogicalInstanceNumber - 1)
                };
            }

            if (!string.IsNullOrWhiteSpace(device.HidPath))
            {
                return new DeviceAlias
                {
                    ProviderName = device.ProviderName,
                    IdentityKind = DeviceAliasIdentityKind.HidPath,
                    IdentityValue = device.HidPath.Trim(),
                    DeviceNumber = 0
                };
            }

            if (string.IsNullOrWhiteSpace(device.DeviceHandle)) return null;

            if (UsesLogicalSlotIdentity(device.ProviderName))
            {
                return new DeviceAlias
                {
                    ProviderName = device.ProviderName,
                    IdentityKind = DeviceAliasIdentityKind.LogicalSlot,
                    IdentityValue = device.DeviceHandle.Trim(),
                    DeviceNumber = device.DeviceNumber
                };
            }

            return new DeviceAlias
            {
                ProviderName = device.ProviderName,
                IdentityKind = DeviceAliasIdentityKind.HardwareHandle,
                IdentityValue = device.DeviceHandle.Trim(),
                DeviceNumber = 0
            };
        }
    }
}
