using System;
using System.Collections.Generic;
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

    /// <summary>
    /// Determines whether an existing binding can be carried to another device without rebinding.
    /// Build A intentionally limits automatic transfer to devices from the same provider that expose
    /// the same binding schema. Cross-provider/controller-family translation is handled separately.
    /// </summary>
    public static class DeviceBindingCompatibility
    {
        public static DeviceBindingTransferCompatibility Evaluate(Device sourceDevice, Device targetDevice,
            Context context, DeviceIoType deviceIoType, DeviceBinding deviceBinding,
            DeviceBindingCategory? expectedCategory = null)
        {
            if (targetDevice == null || deviceBinding == null || !deviceBinding.IsBound)
            {
                return DeviceBindingTransferCompatibility.Unknown;
            }

            if (sourceDevice == null)
            {
                return DeviceBindingTransferCompatibility.Unknown;
            }

            if (object.ReferenceEquals(sourceDevice, targetDevice) || sourceDevice.Equals(targetDevice))
            {
                return DeviceBindingTransferCompatibility.Compatible;
            }

            if (!string.Equals(sourceDevice.ProviderName, targetDevice.ProviderName, StringComparison.OrdinalIgnoreCase))
            {
                return DeviceBindingTransferCompatibility.Incompatible;
            }

            var sourceBindings = FlattenBindings(sourceDevice.GetDeviceBindingMenu(context, deviceIoType));
            var targetBindings = FlattenBindings(targetDevice.GetDeviceBindingMenu(context, deviceIoType));

            // A disconnected device without a usable cache cannot be classified safely. Preserve rather
            // than destructively discarding the user's binding; it remains unresolved until available.
            if (sourceBindings.Count == 0 || targetBindings.Count == 0)
            {
                return DeviceBindingTransferCompatibility.Unknown;
            }

            if (!HaveSameSchema(sourceBindings, targetBindings))
            {
                return DeviceBindingTransferCompatibility.Incompatible;
            }

            foreach (var bindingInfo in targetBindings)
            {
                if (bindingInfo.KeyType != deviceBinding.KeyType ||
                    bindingInfo.KeyValue != deviceBinding.KeyValue ||
                    bindingInfo.KeySubValue != deviceBinding.KeySubValue) continue;

                if (expectedCategory.HasValue && bindingInfo.DeviceBindingCategory != expectedCategory.Value) continue;
                return DeviceBindingTransferCompatibility.Compatible;
            }

            return DeviceBindingTransferCompatibility.Incompatible;
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

        /* Runtime */
        [XmlIgnore]
        private List<DeviceBindingNode> DeviceBindingMenu { get; set; }
        [XmlIgnore] public Profile Profile { get; set; }
        [XmlIgnore] public bool IsCache { get; set; }

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
            DeviceBindingMenu = deviceBindingMenu;
            IsCache = false;
        }

        public Device(DeviceCache deviceCache)
        {
            Title = deviceCache.Title;
            ProviderName = deviceCache.ProviderName;
            DeviceHandle = deviceCache.DeviceHandle;
            DeviceNumber = deviceCache.DeviceNumber;
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
            return $"Device:{{{Title}}} Provider:{{{ProviderName}}} Handle:{{{DeviceHandle}}} Num:{{{DeviceNumber}}}";
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
