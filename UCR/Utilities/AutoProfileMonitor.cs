using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Threading;
using HidWizards.UCR.Core;
using HidWizards.UCR.Core.Models;
using HidWizards.UCR.Core.Utilities;

namespace HidWizards.UCR.Utilities
{
    /// <summary>
    /// Watches configured executable names and owns only the profile activations that it starts.
    /// Manual profile changes suppress an auto-started profile until its executable exits, so
    /// automatic behaviour never fights a deliberate stop/switch from the user.
    /// </summary>
    public sealed class AutoProfileMonitor : IDisposable
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(1000);
        private static readonly TimeSpan FailedActivationRetryDelay = TimeSpan.FromSeconds(5);

        private readonly Context _context;
        private readonly DispatcherTimer _timer;
        private readonly HashSet<Guid> _suppressedProfileGuids = new HashSet<Guid>();
        private readonly Dictionary<Guid, DateTime> _nextActivationAttemptUtc = new Dictionary<Guid, DateTime>();
        private Guid? _ownedProfileGuid;
        private bool _autoOperationInProgress;
        private bool _disposed;

        public AutoProfileMonitor(Context context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            _context = context;
            _context.ActiveProfileChangedEvent += OnActiveProfileChanged;

            // MainWindow constructs the monitor on WPF's UI thread, so use the same proven
            // DispatcherTimer constructor already used elsewhere in UCR.
            _timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = PollInterval
            };
            _timer.Tick += OnTimerTick;
            _timer.Start();

            // Apply an eligible profile immediately if its process was already running before UCR.
            Evaluate();
        }

        private void OnTimerTick(object sender, EventArgs e)
        {
            Evaluate();
        }

        private void Evaluate()
        {
            if (_disposed || _autoOperationInProgress) return;

            var runningExecutables = GetRunningExecutableNames();
            var profiles = EnumerateProfiles(_context.Profiles).ToList();
            ClearExpiredRuntimeState(profiles, runningExecutables);

            var eligibleProfiles = profiles
                .Where(profile => IsEligible(profile, runningExecutables))
                .ToList();

            var activeProfile = _context.ActiveProfile;
            var targetProfile = activeProfile != null
                ? eligibleProfiles.FirstOrDefault(profile => profile.Guid == activeProfile.Guid)
                : null;

            if (targetProfile == null) targetProfile = eligibleProfiles.FirstOrDefault();

            if (targetProfile == null)
            {
                StopOwnedProfileIfNecessary(activeProfile);
                return;
            }

            if (activeProfile != null && activeProfile.Guid == targetProfile.Guid) return;

            ActivateAutomatically(targetProfile);
        }

        private void ActivateAutomatically(Profile profile)
        {
            DateTime nextAttemptUtc;
            if (_nextActivationAttemptUtc.TryGetValue(profile.Guid, out nextAttemptUtc) && DateTime.UtcNow < nextAttemptUtc) return;

            _autoOperationInProgress = true;
            try
            {
                if (_context.SubscriptionsManager.ActivateProfile(profile))
                {
                    _ownedProfileGuid = profile.Guid;
                    _nextActivationAttemptUtc.Remove(profile.Guid);
                    Logger.Info($"Auto-applied profile '{profile.ProfileBreadCrumbs()}' for '{profile.AutoActivateExecutable}'.");
                }
                else
                {
                    _nextActivationAttemptUtc[profile.Guid] = DateTime.UtcNow.Add(FailedActivationRetryDelay);
                    Logger.Warn($"Auto-apply failed for profile '{profile.ProfileBreadCrumbs()}'; retrying in 5 seconds.");
                }
            }
            catch (Exception exception)
            {
                _nextActivationAttemptUtc[profile.Guid] = DateTime.UtcNow.Add(FailedActivationRetryDelay);
                Logger.Error($"Auto-apply failed for profile '{profile.ProfileBreadCrumbs()}'; retrying in 5 seconds.", exception);
            }
            finally
            {
                _autoOperationInProgress = false;
            }
        }

        private void StopOwnedProfileIfNecessary(Profile activeProfile)
        {
            if (!_ownedProfileGuid.HasValue) return;

            if (activeProfile == null || activeProfile.Guid != _ownedProfileGuid.Value)
            {
                _ownedProfileGuid = null;
                return;
            }

            var profileName = activeProfile.ProfileBreadCrumbs();
            _autoOperationInProgress = true;
            try
            {
                var success = _context.SubscriptionsManager.DeactivateCurrentProfile();
                if (success)
                {
                    Logger.Info($"Auto-stopped profile '{profileName}' because its executable is no longer running.");
                }
                else
                {
                    Logger.Warn($"Auto-stop completed with unsubscribe errors for profile '{profileName}'.");
                }
            }
            catch (Exception exception)
            {
                Logger.Error($"Auto-stop failed for profile '{profileName}'.", exception);
            }
            finally
            {
                _ownedProfileGuid = null;
                _autoOperationInProgress = false;
            }
        }

        private void OnActiveProfileChanged(Profile profile)
        {
            if (_disposed || _autoOperationInProgress || !_ownedProfileGuid.HasValue) return;

            // Any manual transition away from an auto-owned profile is intentional. Do not reactivate
            // it every second; suppress it until its configured executable has actually exited.
            if (profile == null || profile.Guid != _ownedProfileGuid.Value)
            {
                _suppressedProfileGuids.Add(_ownedProfileGuid.Value);
                Logger.Info("Manual profile change detected; suppressing the previous auto-profile until its executable exits.");
                _ownedProfileGuid = null;
            }
        }

        private bool IsEligible(Profile profile, HashSet<string> runningExecutables)
        {
            if (profile == null || !profile.AutoActivateEnabled || _suppressedProfileGuids.Contains(profile.Guid)) return false;

            var executableName = NormalizeExecutableName(profile.AutoActivateExecutable);
            return !string.IsNullOrEmpty(executableName) && runningExecutables.Contains(executableName);
        }

        private void ClearExpiredRuntimeState(IEnumerable<Profile> profiles, HashSet<string> runningExecutables)
        {
            var profileList = profiles.ToList();
            var configuredAndRunning = new HashSet<Guid>();

            foreach (var profile in profileList)
            {
                if (!profile.AutoActivateEnabled) continue;
                var executableName = NormalizeExecutableName(profile.AutoActivateExecutable);
                if (!string.IsNullOrEmpty(executableName) && runningExecutables.Contains(executableName))
                {
                    configuredAndRunning.Add(profile.Guid);
                }
            }

            foreach (var profileGuid in _suppressedProfileGuids.ToList())
            {
                if (!configuredAndRunning.Contains(profileGuid)) _suppressedProfileGuids.Remove(profileGuid);
            }

            foreach (var profileGuid in _nextActivationAttemptUtc.Keys.ToList())
            {
                if (!configuredAndRunning.Contains(profileGuid)) _nextActivationAttemptUtc.Remove(profileGuid);
            }
        }

        private static IEnumerable<Profile> EnumerateProfiles(IEnumerable<Profile> roots)
        {
            foreach (var profile in roots ?? Enumerable.Empty<Profile>())
            {
                if (profile == null) continue;
                yield return profile;

                foreach (var child in EnumerateProfiles(profile.ChildProfiles))
                {
                    yield return child;
                }
            }
        }

        private static HashSet<string> GetRunningExecutableNames()
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Process[] processes;
            try
            {
                processes = Process.GetProcesses();
            }
            catch (Exception exception)
            {
                Logger.Error("Unable to enumerate running processes for auto-profile detection.", exception);
                return result;
            }

            foreach (var process in processes)
            {
                try
                {
                    var name = NormalizeExecutableName(process.ProcessName);
                    if (!string.IsNullOrEmpty(name)) result.Add(name);
                }
                catch
                {
                    // A process can exit between enumeration and inspection. Ignore that race.
                }
                finally
                {
                    process.Dispose();
                }
            }

            return result;
        }

        internal static string NormalizeExecutableName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var trimmed = value.Trim().Trim('"');

            try
            {
                var fileName = Path.GetFileName(trimmed);
                if (string.IsNullOrWhiteSpace(fileName)) return null;
                return fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    ? fileName.Substring(0, fileName.Length - 4)
                    : fileName;
            }
            catch (ArgumentException)
            {
                return trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    ? trimmed.Substring(0, trimmed.Length - 4)
                    : trimmed;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _timer.Stop();
            _timer.Tick -= OnTimerTick;
            _context.ActiveProfileChangedEvent -= OnActiveProfileChanged;
        }
    }
}
