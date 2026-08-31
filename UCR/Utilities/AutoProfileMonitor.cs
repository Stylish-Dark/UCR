using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Windows.Threading;
using HidWizards.UCR.Core;
using HidWizards.UCR.Core.Models;
using HidWizards.UCR.Core.Utilities;

namespace HidWizards.UCR.Utilities
{
    /// <summary>
    /// Watches configured application rules and owns only the profile activations that it starts.
    /// A profile may match any of several executables. Optional arguments narrow a rule to processes
    /// whose command line contains that argument string.
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
            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = PollInterval };
            _timer.Tick += OnTimerTick;
            _timer.Start();
            Evaluate();
        }

        private void OnTimerTick(object sender, EventArgs e) => Evaluate();

        private void Evaluate()
        {
            if (_disposed || _autoOperationInProgress) return;

            var profiles = EnumerateProfiles(_context.Profiles).ToList();
            var rules = profiles.SelectMany(GetRules).Where(IsConfigured).ToList();
            var runningApplications = GetRunningApplications(rules.Any(rule => !string.IsNullOrWhiteSpace(rule.Arguments)));
            ClearExpiredRuntimeState(profiles, runningApplications);

            var eligibleProfiles = profiles.Where(profile => IsEligible(profile, runningApplications)).ToList();
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
                    Logger.Info("Auto-applied profile '" + profile.ProfileBreadCrumbs() + "'.");
                }
                else
                {
                    _nextActivationAttemptUtc[profile.Guid] = DateTime.UtcNow.Add(FailedActivationRetryDelay);
                    Logger.Warn("Auto-apply failed for profile '" + profile.ProfileBreadCrumbs() + "'; retrying in 5 seconds.");
                }
            }
            catch (Exception exception)
            {
                _nextActivationAttemptUtc[profile.Guid] = DateTime.UtcNow.Add(FailedActivationRetryDelay);
                Logger.Error("Auto-apply failed for profile '" + profile.ProfileBreadCrumbs() + "'; retrying in 5 seconds.", exception);
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
                if (success) Logger.Info("Auto-stopped profile '" + profileName + "' because none of its application rules are running.");
                else Logger.Warn("Auto-stop completed with unsubscribe errors for profile '" + profileName + "'.");
            }
            catch (Exception exception)
            {
                Logger.Error("Auto-stop failed for profile '" + profileName + "'.", exception);
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
            if (profile == null || profile.Guid != _ownedProfileGuid.Value)
            {
                _suppressedProfileGuids.Add(_ownedProfileGuid.Value);
                Logger.Info("Manual profile change detected; suppressing the previous auto-profile until all matching applications exit.");
                _ownedProfileGuid = null;
            }
        }

        private bool IsEligible(Profile profile, IList<RunningApplication> runningApplications)
        {
            if (profile == null || !profile.AutoActivateEnabled || _suppressedProfileGuids.Contains(profile.Guid)) return false;
            return GetRules(profile).Any(rule => IsConfigured(rule) && runningApplications.Any(app => RuleMatches(rule, app)));
        }

        private void ClearExpiredRuntimeState(IEnumerable<Profile> profiles, IList<RunningApplication> runningApplications)
        {
            var configuredAndRunning = new HashSet<Guid>();
            foreach (var profile in profiles)
            {
                if (!profile.AutoActivateEnabled) continue;
                if (GetRules(profile).Any(rule => IsConfigured(rule) && runningApplications.Any(app => RuleMatches(rule, app))))
                    configuredAndRunning.Add(profile.Guid);
            }

            foreach (var profileGuid in _suppressedProfileGuids.ToList())
                if (!configuredAndRunning.Contains(profileGuid)) _suppressedProfileGuids.Remove(profileGuid);
            foreach (var profileGuid in _nextActivationAttemptUtc.Keys.ToList())
                if (!configuredAndRunning.Contains(profileGuid)) _nextActivationAttemptUtc.Remove(profileGuid);
        }

        internal static IEnumerable<ProfileApplicationRule> GetRules(Profile profile)
        {
            if (profile?.AutoActivateApplications != null && profile.AutoActivateApplications.Count > 0)
                return profile.AutoActivateApplications;

            if (!string.IsNullOrWhiteSpace(profile?.AutoActivateExecutable))
                return new[] { new ProfileApplicationRule(profile.AutoActivateExecutable) };

            return Enumerable.Empty<ProfileApplicationRule>();
        }

        private static bool IsConfigured(ProfileApplicationRule rule)
        {
            return rule != null && !string.IsNullOrWhiteSpace(NormalizeExecutableName(rule.Executable));
        }

        internal static bool RuleMatches(ProfileApplicationRule rule, string executableName, string commandLine)
        {
            return RuleMatches(rule, new RunningApplication
            {
                ExecutableName = NormalizeExecutableName(executableName),
                CommandLine = commandLine ?? string.Empty
            });
        }

        private static bool RuleMatches(ProfileApplicationRule rule, RunningApplication application)
        {
            if (rule == null || application == null) return false;
            var expectedExecutable = NormalizeExecutableName(rule.Executable);
            if (string.IsNullOrWhiteSpace(expectedExecutable) ||
                !string.Equals(expectedExecutable, application.ExecutableName, StringComparison.OrdinalIgnoreCase)) return false;

            var arguments = (rule.Arguments ?? string.Empty).Trim();
            if (arguments.Length == 0) return true;
            return (application.CommandLine ?? string.Empty).IndexOf(arguments, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static IEnumerable<Profile> EnumerateProfiles(IEnumerable<Profile> roots)
        {
            foreach (var profile in roots ?? Enumerable.Empty<Profile>())
            {
                if (profile == null) continue;
                yield return profile;
                foreach (var child in EnumerateProfiles(profile.ChildProfiles)) yield return child;
            }
        }

        private static List<RunningApplication> GetRunningApplications(bool includeCommandLines)
        {
            if (includeCommandLines)
            {
                try
                {
                    return GetRunningApplicationsWithCommandLines();
                }
                catch (Exception exception)
                {
                    Logger.Warn("Unable to query process command lines; falling back to executable-only matching.", exception);
                }
            }

            var result = new List<RunningApplication>();
            Process[] processes;
            try { processes = Process.GetProcesses(); }
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
                    if (!string.IsNullOrWhiteSpace(name)) result.Add(new RunningApplication { ExecutableName = name, CommandLine = string.Empty });
                }
                catch { }
                finally { process.Dispose(); }
            }
            return result;
        }

        private static List<RunningApplication> GetRunningApplicationsWithCommandLines()
        {
            var result = new List<RunningApplication>();
            using (var searcher = new ManagementObjectSearcher("SELECT Name, CommandLine FROM Win32_Process"))
            {
                var collection = searcher.Get();
                foreach (ManagementObject process in collection)
                {
                    var name = NormalizeExecutableName(process["Name"] as string);
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    result.Add(new RunningApplication
                    {
                        ExecutableName = name,
                        CommandLine = process["CommandLine"] as string ?? string.Empty
                    });
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

        private sealed class RunningApplication
        {
            public string ExecutableName { get; set; }
            public string CommandLine { get; set; }
        }
    }
}
