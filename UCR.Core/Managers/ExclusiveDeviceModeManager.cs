using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using HidWizards.UCR.Core.Models;
using NLog;

namespace HidWizards.UCR.Core.Managers
{
    /// <summary>
    /// Scoped HidHide integration. Never wipes another application's whitelist or hidden devices.
    /// The on-disk ownership journal allows cleanup after an abnormal UCR termination on next launch.
    /// HidHideCLI is required; UCR does not install drivers.
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
        private bool _disposed;

        public ExclusiveDeviceModeManager()
        {
            try { RecoverStaleSession(); }
            catch (Exception exception) { Logger.Error(exception, "Could not recover previous HidHide session"); }
        }

        public bool Apply(IEnumerable<Profile> profiles)
        {
            var desired = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var profile in profiles ?? Enumerable.Empty<Profile>())
            {
                foreach (var config in profile.GetDeviceConfigurationList(DeviceIoType.Input))
                {
                    if (config?.ExclusiveMode != true) continue;
                    var device = config.Device;
                    if (device == null || device.IsCache ||
                        device.ProviderName.StartsWith("Core_Interception", StringComparison.OrdinalIgnoreCase))
                    {
                        Logger.Error("Exclusive mode requires a live physical HID controller, not an Interception keyboard.");
                        return false;
                    }
                    var instance = NormalizeInstancePath(device.HidPath);
                    if (instance == null)
                    {
                        Logger.Error("Exclusive mode has no usable HID instance path for " + device.DisplayTitle);
                        return false;
                    }
                    desired.Add(instance);
                }
            }

            // Never hide a whole device on behalf of a transient, unconfigured profile.
            if (desired.Count == 0 && _owned.Count == 0 && !_addedApplication && !_enabledCloak)
                return true;
            var toRelease = _owned.Where(path => !desired.Contains(path)).ToList();
            var toAcquire = desired.Where(path => !_owned.Contains(path)).ToList();
            if (toAcquire.Count == 0 && toRelease.Count == 0 && !_addedApplication && !_enabledCloak && desired.Count == 0) return true;
            try
            {
                var cli = FindCli();
                var snapshot = Invoke(cli, "--cloak-state --inv-state --dev-list --app-list");
                if (snapshot.IndexOf("--inv-on", StringComparison.OrdinalIgnoreCase) >= 0)
                    throw new InvalidOperationException("HidHide inverse application mode is enabled. Disable it in HidHide first.");

                var currentlyHidden = ReadLines(snapshot, "--dev-hide");
                var newlyOwned = toAcquire.Where(p => !currentlyHidden.Contains(p)).ToList();
                var app = Process.GetCurrentProcess().MainModule.FileName;
                var allowed = ReadLines(snapshot, "--app-reg");
                var addApp = desired.Count > 0 && !_addedApplication && !allowed.Contains(app);
                var cloakOn = snapshot.IndexOf("--cloak-on", StringComparison.OrdinalIgnoreCase) >= 0;
                var enableCloak = desired.Count > 0 && !_enabledCloak && !cloakOn;

                // Journal first, then apply: if UCR crashes during a CLI call,
                // the next launch can undo only UCR-owned registrations.
                var nextOwned = new HashSet<string>(_owned, StringComparer.OrdinalIgnoreCase);
                foreach (var p in toRelease) nextOwned.Remove(p);
                foreach (var p in newlyOwned) nextOwned.Add(p);
                var nextAddedApp = _addedApplication || addApp;
                var nextEnabledCloak = _enabledCloak || enableCloak;
                WriteJournal(nextOwned, nextAddedApp, nextEnabledCloak, app);

                var commands = new List<string>();
                if (addApp) commands.Add("--app-reg " + Quote(app));
                foreach (var p in newlyOwned) commands.Add("--dev-hide " + Quote(p));
                foreach (var p in toRelease) commands.Add("--dev-unhide " + Quote(p));
                if (enableCloak) commands.Add("--cloak-on");
                if (desired.Count == 0)
                {
                    if (_addedApplication) commands.Add("--app-unreg " + Quote(app));
                    if (_enabledCloak) commands.Add("--cloak-off");
                }

                if (commands.Count != 0) Invoke(cli, string.Join(" ", commands));
                _owned.Clear();
                foreach (var p in nextOwned) _owned.Add(p);
                _addedApplication = desired.Count > 0 && nextAddedApp;
                _enabledCloak = desired.Count > 0 && nextEnabledCloak;
                WriteJournal(_owned, _addedApplication, _enabledCloak, app);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Exclusive Device Mode could not change HidHide state.");
                return false;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Apply(Enumerable.Empty<Profile>());
        }

        private void RecoverStaleSession()
        {
            if (!File.Exists(_journal)) return;
            var lines = File.ReadAllLines(_journal);
            var paths = lines.Where(x => x.StartsWith("device=", StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Substring(7)).ToList();
            var app = lines.FirstOrDefault(x => x.StartsWith("app=", StringComparison.OrdinalIgnoreCase));
            var addedApp = lines.Contains("app-owned=true");
            var cloakOwned = lines.Contains("cloak-owned=true");
            var commands = paths.Select(p => "--dev-unhide " + Quote(p)).ToList();
            if (addedApp && app != null) commands.Add("--app-unreg " + Quote(app.Substring(4)));
            if (cloakOwned) commands.Add("--cloak-off");
            if (commands.Count > 0) Invoke(FindCli(), string.Join(" ", commands));
            File.Delete(_journal);
        }

        private void WriteJournal(IEnumerable<string> paths, bool appOwned, bool cloakOwned, string app)
        {
            var directory = Path.GetDirectoryName(_journal);
            Directory.CreateDirectory(directory);
            var contents = new List<string> { "app=" + app, "app-owned=" + appOwned.ToString().ToLowerInvariant(),
                "cloak-owned=" + cloakOwned.ToString().ToLowerInvariant() };
            contents.AddRange(paths.Select(p => "device=" + p));
            if (contents.Count == 3 && !appOwned && !cloakOwned)
            {
                if (File.Exists(_journal)) File.Delete(_journal);
                return;
            }
            File.WriteAllLines(_journal, contents);
        }

        private static string NormalizeInstancePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            var value = path.Trim();
            if (value.StartsWith(@"\\?\", StringComparison.Ordinal))
                value = value.Substring(4);
            var classMarker = value.IndexOf("#{", StringComparison.Ordinal);
            if (classMarker >= 0) value = value.Substring(0, classMarker);
            value = value.Replace('#', '\\');
            if (!value.StartsWith(@"HID\", StringComparison.OrdinalIgnoreCase) ||
                value.IndexOf("VID_", StringComparison.OrdinalIgnoreCase) < 0 ||
                value.IndexOf('"') >= 0) return null;
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

        private static string Quote(string s)
        {
            if (s == null || s.Contains("\"")) throw new ArgumentException("Invalid HidHide argument");
            return "\"" + s + "\"";
        }

        private static string FindCli()
        {
            foreach (var basePath in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                       Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
            {
                var candidate = Path.Combine(basePath, "Nefarius Software Solutions", "HidHide", "HidHideCLI.exe");
                if (File.Exists(candidate)) return candidate;
            }
            throw new FileNotFoundException("HidHideCLI.exe not found. Install HidHide before using Exclusive Mode.");
        }

        private static string Invoke(string executable, string arguments)
        {
            using (var process = new Process())
            {
                process.StartInfo = new ProcessStartInfo(executable, arguments)
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true
                };
                process.Start();
                var stdout = process.StandardOutput.ReadToEnd();
                var stderr = process.StandardError.ReadToEnd();
                if (!process.WaitForExit(10000))
                {
                    try { process.Kill(); } catch { }
                    throw new TimeoutException("HidHideCLI did not respond within ten seconds.");
                }
                if (process.ExitCode != 0)
                    throw new InvalidOperationException("HidHideCLI failed (" + process.ExitCode + "): " + stderr);
                return stdout;
            }
        }
    }
}
