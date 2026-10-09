using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HidWizards.UCR.Core.Models;
using Newtonsoft.Json.Linq;
using NLog;

namespace HidWizards.UCR.Core.Managers
{
    /// <summary>
    /// Scoped controller isolation using the installed HidHide driver.
    /// UCR only removes entries it added, verifies every configuration change,
    /// and journals pending changes so a crash cannot silently strand a device.
    /// Driver configuration alone cannot prove that an already-open device stack
    /// has been rebuilt; reconnecting the controller may still be required.
    /// </summary>
    internal sealed class ExclusiveDeviceModeManager : IDisposable
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly HashSet<string> _owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly string _journal = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "UCR", "HidHideExclusiveSession.txt");
        private bool _addedApplication;
        private bool _enabledCloak;
        private bool _recoveryFailed;
        private bool _disposed;

        public ExclusiveDeviceModeManager()
        {
            try { RecoverStaleSession(); }
            catch (Exception exception)
            {
                _recoveryFailed = true;
                Logger.Error(exception, "Could not recover previous HidHide session; exclusive mode is unavailable until recovery succeeds.");
            }
        }

        public bool Apply(IEnumerable<Profile> profiles)
        {
            try
            {
                // Never overwrite an unrecovered ownership journal. Doing so could
                // permanently strand a previously hidden controller.
                if (_recoveryFailed)
                {
                    RecoverStaleSession();
                    _owned.Clear();
                    _addedApplication = false;
                    _enabledCloak = false;
                    _recoveryFailed = false;
                }

                var requested = new List<string>();
                foreach (var profile in profiles ?? Enumerable.Empty<Profile>())
                {
                    if (profile == null) continue;
                    foreach (var config in profile.GetDeviceConfigurationList(DeviceIoType.Input))
                    {
                        if (config?.ExclusiveMode != true) continue;
                        var device = config.Device;
                        if (device == null || device.IsCache ||
                            device.ProviderName.StartsWith("Core_Interception", StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException("Exclusive mode requires a live physical HID controller.");

                        var instance = NormalizeInstancePath(device.HidPath);
                        if (instance == null || !instance.StartsWith(@"HID\", StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException("Exclusive mode cannot identify the HID instance for " + device.DisplayTitle);
                        requested.Add(instance);
                    }
                }

                if (requested.Count == 0 && _owned.Count == 0 && !_addedApplication && !_enabledCloak)
                    return true;

                var cli = FindCli();
                var desired = requested.Count == 0
                    ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    : ResolveDevicePaths(cli, requested);
                var before = ReadSnapshot(cli);
                if (before.Inverse)
                    throw new InvalidOperationException("HidHide inverse application mode is enabled. Disable it before using Exclusive Mode.");

                var app = Process.GetCurrentProcess().MainModule.FileName;
                var newlyOwned = desired.Where(p => !before.Hidden.Contains(p)).ToList();
                var toRelease = _owned.Where(p => !desired.Contains(p) && before.Hidden.Contains(p)).ToList();
                var addApp = desired.Count > 0 && !before.Allowed.Contains(app);
                var enableCloak = desired.Count > 0 && !before.CloakOn;
                if (enableCloak && before.Hidden.Except(desired, StringComparer.OrdinalIgnoreCase).Any())
                    throw new InvalidOperationException(
                        "HidHide already contains unrelated hidden devices while its global cloak is disabled. " +
                        "Enable cloaking deliberately in HidHide before starting this profile.");
                var removeApp = desired.Count == 0 && _addedApplication && before.Allowed.Contains(app);

                // The global cloak must remain enabled if any unrelated hidden
                // devices exist, even if UCR was the application that enabled it.
                var disableCloak = desired.Count == 0 && _enabledCloak && before.CloakOn &&
                    !before.Hidden.Except(_owned, StringComparer.OrdinalIgnoreCase).Any();

                // Journal the UNION of old and new ownership BEFORE writing to the
                // driver. If the process crashes while releasing old entries, they
                // must still be recorded for the next launch to clean up.
                var pendingOwned = _owned.Union(newlyOwned, StringComparer.OrdinalIgnoreCase).ToList();
                WriteJournal(pendingOwned, _addedApplication || addApp,
                    _enabledCloak || enableCloak, app);

                var commands = new List<string>();
                if (addApp) commands.Add("--app-reg " + Quote(app));
                foreach (var p in newlyOwned) commands.Add("--dev-hide " + Quote(p));
                foreach (var p in toRelease) commands.Add("--dev-unhide " + Quote(p));
                if (enableCloak) commands.Add("--cloak-on");
                if (removeApp) commands.Add("--app-unreg " + Quote(app));
                if (disableCloak) commands.Add("--cloak-off");
                if (commands.Count > 0) Invoke(cli, string.Join(" ", commands));

                // A successful CLI exit does not mean its requested configuration
                // is present. Read it back before accepting profile activation.
                var after = ReadSnapshot(cli);
                if (after.Inverse || desired.Any(p => !after.Hidden.Contains(p)) ||
                    toRelease.Any(p => after.Hidden.Contains(p)) ||
                    (desired.Count > 0 && (!after.CloakOn || !after.Allowed.Contains(app))) ||
                    (removeApp && after.Allowed.Contains(app)) ||
                    (disableCloak && after.CloakOn))
                    throw new InvalidOperationException("HidHide did not retain the requested isolation configuration.");

                _owned.Clear();
                foreach (var p in pendingOwned.Where(desired.Contains)) _owned.Add(p);
                _addedApplication = desired.Count > 0 && (_addedApplication || addApp);
                _enabledCloak = desired.Count > 0 && (_enabledCloak || enableCloak);
                WriteJournal(_owned, _addedApplication, _enabledCloak, app);

                if (newlyOwned.Count > 0)
                    Logger.Info("HidHide rules were verified. A controller already connected before the filter attached may need to be reconnected.");
                return true;
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "Exclusive Device Mode failed. Profile activation must not be reported as successful.");
                // Retain the journal for recovery. Do not try to guess which
                // partial CLI writes succeeded after a failed invocation.
                if (File.Exists(_journal)) _recoveryFailed = true;
                return false;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (!Apply(Enumerable.Empty<Profile>()))
                Logger.Error("HidHide cleanup failed; recovery journal retained for next launch.");
        }

        private static HashSet<string> ResolveDevicePaths(string cli, IEnumerable<string> requested)
        {
            // HidHide's inventory correlates HID children with their XUSB and USB
            // container paths. Hiding only the HID child leaves many XInput pads
            // visible to games. Never guess a USB parent from a VID/PID alone.
            var gaming = ReadInventory(cli, "--dev-gaming");
            JArray all = null;
            var desired = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var hidPath in requested.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var device = FindDevice(gaming, hidPath);
                if (device == null)
                {
                    if (all == null) all = ReadInventory(cli, "--dev-all");
                    device = FindDevice(all, hidPath);
                }
                if (device == null)
                    throw new InvalidOperationException("HidHide cannot identify the connected controller: " + hidPath);

                foreach (var instance in ResolveControllerPaths(
                    hidPath,
                    (string)device["baseContainerDeviceInstancePath"],
                    (string)device["xusbDeviceInstancePath"],
                    (int?)device["baseContainerDeviceCount"] ?? 1))
                    desired.Add(instance);
            }
            return desired;
        }

        private static HashSet<string> ResolveControllerPaths(
            string hidPath, string parentRaw, string xusbRaw, int functionCount)
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { hidPath };
            var isXinput = !string.IsNullOrWhiteSpace(xusbRaw) ||
                hidPath.IndexOf("&IG_", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!isXinput) return paths;

            var parent = NormalizeInstancePath(parentRaw);
            var xusb = NormalizeInstancePath(xusbRaw);

            // A composite USB parent may also carry a keyboard or another
            // unrelated function. Never hide such a parent automatically.
            if (parent == null || !parent.StartsWith(@"USB\", StringComparison.OrdinalIgnoreCase) ||
                functionCount > 1 || (!string.IsNullOrWhiteSpace(xusbRaw) && xusb == null))
                throw new InvalidOperationException(
                    "Cannot safely isolate the XInput controller's USB container: " + hidPath);

            paths.Add(parent);
            if (xusb != null) paths.Add(xusb);
            return paths;
        }

        private static JArray ReadInventory(string cli, string command)
        {
            return JArray.Parse(Invoke(cli, command));
        }

        private static JObject FindDevice(JArray inventory, string path)
        {
            foreach (var container in inventory.OfType<JObject>())
            {
                var devices = container["devices"] as JArray;
                if (devices == null) continue;
                foreach (var device in devices.OfType<JObject>())
                {
                    var instance = NormalizeInstancePath((string)device["deviceInstancePath"]);
                    var link = NormalizeInstancePath((string)device["symbolicLink"]);
                    if (string.Equals(path, instance, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(path, link, StringComparison.OrdinalIgnoreCase))
                        return device;
                }
            }
            return null;
        }

        private sealed class Snapshot
        {
            public HashSet<string> Hidden;
            public HashSet<string> Allowed;
            public bool CloakOn;
            public bool Inverse;
        }

        private static Snapshot ReadSnapshot(string cli)
        {
            var text = Invoke(cli, "--cloak-state --inv-state --dev-list --app-list");
            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim()).ToArray();
            if (!lines.Contains("--cloak-on") && !lines.Contains("--cloak-off"))
                throw new InvalidOperationException("HidHide did not report its cloak state.");
            if (!lines.Contains("--inv-on") && !lines.Contains("--inv-off"))
                throw new InvalidOperationException("HidHide did not report its application-list mode.");
            return new Snapshot
            {
                Hidden = ReadLines(text, "--dev-hide"),
                Allowed = ReadLines(text, "--app-reg"),
                CloakOn = lines.Contains("--cloak-on"),
                Inverse = lines.Contains("--inv-on")
            };
        }

        private void RecoverStaleSession()
        {
            if (!File.Exists(_journal)) return;
            var lines = File.ReadAllLines(_journal);
            var paths = new HashSet<string>(lines.Where(x => x.StartsWith("device=", StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Substring(7)), StringComparer.OrdinalIgnoreCase);
            var appLine = lines.FirstOrDefault(x => x.StartsWith("app=", StringComparison.OrdinalIgnoreCase));
            var app = appLine == null ? null : appLine.Substring(4);
            var addedApp = lines.Contains("app-owned=true");
            var cloakOwned = lines.Contains("cloak-owned=true");
            var cli = FindCli();
            var before = ReadSnapshot(cli);
            if (before.Inverse)
                throw new InvalidOperationException("Cannot safely recover HidHide while inverse mode is enabled.");

            var commands = paths.Where(before.Hidden.Contains).Select(p => "--dev-unhide " + Quote(p)).ToList();
            if (addedApp && !string.IsNullOrWhiteSpace(app) && before.Allowed.Contains(app))
                commands.Add("--app-unreg " + Quote(app));
            var disableCloak = cloakOwned && before.CloakOn &&
                !before.Hidden.Except(paths, StringComparer.OrdinalIgnoreCase).Any();
            if (disableCloak) commands.Add("--cloak-off");
            if (commands.Count > 0) Invoke(cli, string.Join(" ", commands));

            var after = ReadSnapshot(cli);
            if (paths.Any(after.Hidden.Contains) ||
                (addedApp && !string.IsNullOrWhiteSpace(app) && after.Allowed.Contains(app)) ||
                (disableCloak && after.CloakOn))
                throw new InvalidOperationException("HidHide recovery could not verify cleanup.");

            File.Delete(_journal);
        }

        private void WriteJournal(IEnumerable<string> paths, bool appOwned, bool cloakOwned, string app)
        {
            var directory = Path.GetDirectoryName(_journal);
            Directory.CreateDirectory(directory);
            var ownedPaths = paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (ownedPaths.Count == 0 && !appOwned && !cloakOwned)
            {
                if (File.Exists(_journal)) File.Delete(_journal);
                return;
            }

            var contents = new List<string> { "app=" + app,
                "app-owned=" + appOwned.ToString().ToLowerInvariant(),
                "cloak-owned=" + cloakOwned.ToString().ToLowerInvariant() };
            contents.AddRange(ownedPaths.Select(p => "device=" + p));
            var temp = _journal + ".tmp";
            File.WriteAllLines(temp, contents);
            if (File.Exists(_journal)) File.Replace(temp, _journal, null);
            else File.Move(temp, _journal);
        }

        private static string NormalizeInstancePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            var value = path.Trim();
            if (value.StartsWith(@"\\?\", StringComparison.Ordinal) ||
                value.StartsWith(@"\\.\", StringComparison.Ordinal))
                value = value.Substring(4);
            var classMarker = value.IndexOf("#{", StringComparison.Ordinal);
            if (classMarker >= 0) value = value.Substring(0, classMarker);
            value = value.Replace('#', '\\');
            if (value.IndexOf('"') >= 0 || value.IndexOf('\r') >= 0 || value.IndexOf('\n') >= 0)
                return null;
            if (!(value.StartsWith(@"HID\", StringComparison.OrdinalIgnoreCase) ||
                  value.StartsWith(@"USB\", StringComparison.OrdinalIgnoreCase) ||
                  value.StartsWith(@"XUSB\", StringComparison.OrdinalIgnoreCase)) ||
                value.IndexOf("VID_", StringComparison.OrdinalIgnoreCase) < 0)
                return null;
            return value;
        }

        private static HashSet<string> ReadLines(string text, string prefix)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var clean = line.Trim();
                if (!clean.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase)) continue;
                var value = clean.Substring(prefix.Length).Trim().Trim('"');
                if (value.Length > 0) result.Add(value);
            }
            return result;
        }

        private static string Quote(string value)
        {
            if (value == null || value.IndexOf('"') >= 0 ||
                value.IndexOf('\r') >= 0 || value.IndexOf('\n') >= 0)
                throw new ArgumentException("Invalid HidHide argument.");
            return "\"" + value + "\"";
        }

        private static string FindCli()
        {
            foreach (var basePath in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
            {
                foreach (var relative in new[] {
                    Path.Combine("Nefarius Software Solutions", "HidHide", "HidHideCLI.exe"),
                    Path.Combine("Nefarius Software Solutions", "HidHide", "x64", "HidHideCLI.exe") })
                {
                    var candidate = Path.Combine(basePath, relative);
                    if (File.Exists(candidate)) return candidate;
                }
            }
            throw new FileNotFoundException("HidHide is not installed. Exclusive Mode requires its driver and command-line client.");
        }

        private static string Invoke(string executable, string arguments)
        {
            using (var process = new Process())
            {
                process.StartInfo = new ProcessStartInfo(executable, arguments)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                process.Start();
                // Drain both pipes concurrently to avoid deadlocking on verbose
                // inventory output or an error from the HidHide driver.
                Task<string> stdout = process.StandardOutput.ReadToEndAsync();
                Task<string> stderr = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(10000))
                {
                    try { process.Kill(); } catch { }
                    throw new TimeoutException("HidHideCLI did not respond within ten seconds.");
                }
                Task.WaitAll(stdout, stderr);
                if (process.ExitCode != 0)
                    throw new InvalidOperationException("HidHideCLI failed (" + process.ExitCode + "): " + stderr.Result);
                return stdout.Result;
            }
        }
    }
}
