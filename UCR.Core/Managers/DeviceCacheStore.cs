using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HidWizards.UCR.Core.Models;
using HidWizards.UCR.Core.Models.Binding;
using Newtonsoft.Json;
using NLog;

namespace HidWizards.UCR.Core.Managers
{
    /// <summary>
    /// Filesystem persistence for provider-generated device cache records.
    /// Device enumeration and binding-menu resolution remain DevicesManager responsibilities.
    /// </summary>
    internal sealed class DeviceCacheStore
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly string _cacheRoot;
        private readonly Dictionary<string, List<Device>> _providerCache =
            new Dictionary<string, List<Device>>(StringComparer.OrdinalIgnoreCase);

        public DeviceCacheStore(string cacheRoot)
        {
            if (string.IsNullOrWhiteSpace(cacheRoot)) throw new ArgumentException("Cache root is required.", nameof(cacheRoot));
            _cacheRoot = cacheRoot;
        }

        public List<Device> LoadAllProviders()
        {
            if (!Directory.Exists(_cacheRoot)) return new List<Device>();

            string[] providerDirectories;
            try
            {
                providerDirectories = Directory.GetDirectories(_cacheRoot, "*", SearchOption.TopDirectoryOnly);
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "Unable to enumerate provider cache directories");
                return new List<Device>();
            }

            var devices = new List<Device>();
            foreach (var providerDirectory in providerDirectories)
            {
                var providerName = new DirectoryInfo(providerDirectory).Name;
                if (string.IsNullOrWhiteSpace(providerName)) continue;
                try
                {
                    devices.AddRange(LoadProvider(providerName));
                }
                catch (Exception exception)
                {
                    Logger.Error(exception, "Unable to load cached devices for provider: " + providerName);
                }
            }
            return devices;
        }

        public List<Device> LoadProvider(string provider)
        {
            if (string.IsNullOrWhiteSpace(provider)) return new List<Device>();
            if (_providerCache.TryGetValue(provider, out var cached)) return cached;

            var result = new List<Device>();
            string[] files;
            try
            {
                files = Directory.GetFiles(GetProviderDirectory(provider), "*.json", SearchOption.TopDirectoryOnly);
            }
            catch (DirectoryNotFoundException)
            {
                return result;
            }

            foreach (var file in files)
            {
                var device = Read(provider, file);
                if (device != null) result.Add(device);
            }

            _providerCache[provider] = result;
            return result;
        }

        public bool Save(Device device, List<DeviceBindingNode> bindingMenu)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            var providerDirectory = GetProviderDirectory(device.ProviderName);
            Directory.CreateDirectory(providerDirectory);

            using (var streamWriter = new StreamWriter(GetDevicePath(device)))
            {
                var cache = new DeviceCache
                {
                    Title = device.Title,
                    ProviderName = device.ProviderName,
                    DeviceHandle = device.DeviceHandle,
                    DeviceNumber = device.DeviceNumber,
                    HidPath = device.HidPath,
                    DeviceBindingMenu = bindingMenu
                };
                new JsonSerializer().Serialize(streamWriter, cache);
            }

            _providerCache.Remove(device.ProviderName);
            return true;
        }

        public int RemoveOverlapping(IEnumerable<Device> liveDevices)
        {
            var live = (liveDevices ?? Enumerable.Empty<Device>()).Where(device => device != null).ToList();
            if (live.Count == 0) return 0;

            var removed = 0;
            var providers = live.Select(device => device.ProviderName)
                .Where(provider => !string.IsNullOrWhiteSpace(provider))
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var provider in providers)
            {
                var directory = GetProviderDirectory(provider);
                if (!Directory.Exists(directory)) continue;

                foreach (var path in Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
                {
                    var cached = Read(provider, path);
                    if (cached == null) continue;
                    if (!live.Any(liveDevice => DeviceIdentity.CacheRepresentsLiveEndpoint(cached, liveDevice))) continue;

                    try
                    {
                        File.Delete(path);
                        removed++;
                    }
                    catch (Exception exception)
                    {
                        Logger.Error(exception, "Failed to remove stale device cache: " + path);
                    }
                }

                _providerCache.Remove(provider);
            }

            return removed;
        }

        public void ClearMemoryCache()
        {
            _providerCache.Clear();
        }

        private Device Read(string provider, string path)
        {
            if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(path)) return null;

            try
            {
                using (var fileStream = new FileStream(path, FileMode.Open))
                using (var reader = new StreamReader(fileStream))
                {
                    var cache = JsonConvert.DeserializeObject<DeviceCache>(reader.ReadToEnd());
                    if (cache == null) throw new JsonSerializationException("Device cache file contained no device record.");
                    return new Device(cache);
                }
            }
            catch (IOException exception)
            {
                Logger.Error($"Failed to load Cache for Provider: {provider}. Path: {path}", exception);
            }
            catch (InvalidOperationException exception)
            {
                Logger.Error($"Errors processing provider cache: {provider}. Path: {path}", exception);
            }
            catch (JsonException exception)
            {
                Logger.Error($"Invalid JSON in provider cache: {provider}. Path: {path}", exception);
            }

            try
            {
                File.Delete(path);
            }
            catch (Exception exception)
            {
                Logger.Error($"Failed to delete invalid cache file: {path}", exception);
            }
            return null;
        }

        private string GetDevicePath(Device device)
        {
            return Path.Combine(GetProviderDirectory(device.ProviderName), device.GetHashCode() + ".json");
        }

        private string GetProviderDirectory(string provider)
        {
            return Path.Combine(_cacheRoot, provider ?? string.Empty);
        }
    }
}
