using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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
        private readonly object _cacheLock = new object();
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
            lock (_cacheLock)
            {
                if (_providerCache.TryGetValue(provider, out var cached)) return cached;

                var result = new Dictionary<string, Device>(StringComparer.OrdinalIgnoreCase);
                string[] files;
                try
                {
                    files = Directory.GetFiles(GetProviderDirectory(provider), "*.json", SearchOption.TopDirectoryOnly);
                }
                catch (DirectoryNotFoundException)
                {
                    return new List<Device>();
                }

                foreach (var file in files)
                {
                    var device = Read(provider, file);
                    if (device == null) continue;
                    var canonicalName = GetDeviceFileName(device);
                    if (!result.ContainsKey(canonicalName) ||
                        string.Equals(Path.GetFileName(file), canonicalName, StringComparison.OrdinalIgnoreCase))
                    {
                        result[canonicalName] = device;
                    }
                }

                return _providerCache[provider] = result.Values.ToList();
            }
        }

        public bool Save(Device device, List<DeviceBindingNode> bindingMenu)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            var providerDirectory = GetProviderDirectory(device.ProviderName);
            Directory.CreateDirectory(providerDirectory);

            var path = Path.Combine(providerDirectory, GetDeviceFileName(device));
            var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            lock (_cacheLock)
            {
                try
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
                    using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    using (var writer = new StreamWriter(stream))
                    {
                        new JsonSerializer().Serialize(writer, cache);
                        writer.Flush();
                        stream.Flush(true);
                    }

                    if (File.Exists(path))
                        File.Replace(temporary, path, null);
                    else
                        File.Move(temporary, path);

                    _providerCache.Remove(device.ProviderName);
                    return true;
                }
                finally
                {
                    try
                    {
                        File.Delete(temporary);
                    }
                    catch (Exception exception)
                    {
                        Logger.Warn(exception, "Failed to remove temporary device cache file: " + temporary);
                    }
                }
            }
        }

        public int RemoveOverlapping(IEnumerable<Device> liveDevices)
        {
            var removed = 0;
            var providers = (liveDevices ?? Enumerable.Empty<Device>())
                .Where(device => !string.IsNullOrWhiteSpace(device?.ProviderName))
                .GroupBy(device => device.ProviderName, StringComparer.OrdinalIgnoreCase);

            lock (_cacheLock)
            {
                foreach (var provider in providers)
                {
                    var directory = GetProviderDirectory(provider.Key);
                    if (!Directory.Exists(directory)) continue;

                    var replacements = provider.Select(device => new { Device = device, Name = GetDeviceFileName(device) })
                        .Where(record => File.Exists(Path.Combine(directory, record.Name))).ToList();
                    var current = new HashSet<string>(replacements.Select(record => record.Name), StringComparer.OrdinalIgnoreCase);
                    foreach (var path in Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
                    {
                        if (current.Contains(Path.GetFileName(path))) continue;
                        var cached = Read(provider.Key, path);
                        if (cached == null || !replacements.Any(record => RepresentsSameEndpoint(cached, record.Device))) continue;

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

                    _providerCache.Remove(provider.Key);
                }
            }

            return removed;
        }

        public void ClearMemoryCache()
        {
            lock (_cacheLock)
            {
                _providerCache.Clear();
            }
        }

        private static bool RepresentsSameEndpoint(Device cached, Device live)
        {
            if (!string.IsNullOrWhiteSpace(cached.HidPath) && !string.IsNullOrWhiteSpace(live.HidPath))
                return DeviceIdentity.CacheRepresentsLiveEndpoint(cached, live);

            return DeviceIdentity.DescriptorEquals(cached, live);
        }

        private Device Read(string provider, string path)
        {
            if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(path)) return null;

            try
            {
                using (var fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
                using (var reader = new StreamReader(fileStream))
                {
                    var cache = JsonConvert.DeserializeObject<DeviceCache>(reader.ReadToEnd());
                    if (cache == null) throw new JsonSerializationException("Device cache file contained no device record.");
                    return new Device(cache);
                }
            }
            catch (IOException exception)
            {
                Logger.Error(exception, $"Failed to load Cache for Provider: {provider}. Path: {path}");
            }
            catch (InvalidOperationException exception)
            {
                Logger.Error(exception, $"Errors processing provider cache: {provider}. Path: {path}");
            }
            catch (JsonException exception)
            {
                Logger.Error(exception, $"Invalid JSON in provider cache: {provider}. Path: {path}");
            }

            return null;
        }

        private static string GetDeviceFileName(Device device)
        {
            var identity = JsonConvert.SerializeObject(new
            {
                Handle = device.DeviceHandle,
                Number = device.DeviceNumber,
                HidPath = device.HidPath?.ToUpperInvariant()
            });
            using (var sha256 = SHA256.Create())
            {
                return BitConverter.ToString(sha256.ComputeHash(Encoding.UTF8.GetBytes(identity)))
                    .Replace("-", string.Empty) + ".json";
            }
        }

        private string GetProviderDirectory(string provider)
        {
            return Path.Combine(_cacheRoot, provider ?? string.Empty);
        }
    }
}
