using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
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
        private readonly HashSet<Guid> _ownedProfileGuids = new HashSet<Guid>();
        private readonly HashSet<Guid> _failedStopGuids = new HashSet<Guid>();
        private bool _evaluationInProgress;
        private bool _disposed;

        public AutoProfileMonitor(Context context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            _context = context;
            _context.ActiveProfileChangedEvent += OnActiveProfileChanged;
            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = PollInterval };
            _timer.Tick += OnTimerTick;
            _timer.Start();
            OnTimerTick(this, EventArgs.Empty);
        }

        private async void OnTimerTick(object sender, EventArgs e)
        {
            if (_disposed || _evaluationInProgress) return;

            _evaluationInProgress = true;
            try
            {
                var profiles = EnumerateProfiles(_context.Profiles).ToList();
                var enabledRules = profiles
                    .Where(profile => profile != null && profile.AutoActivateEnabled)
                    .SelectMany(GetRules)
                    .Where(IsConfigured)
                    .ToList();

                // Do not enumerate every process on the machine when auto-apply is unused.
                if (enabledRules.Count == 0)
                {
                    await ApplyEvaluationAsync(profiles, new List<RunningApplication>());
                    return;
                }

                var includeCommandLines = enabledRules.Any(rule => !string.IsNullOrWhiteSpace(rule.Arguments));

                // Process enumeration (especially Win32_Process/WMI for command lines) can block for
                // noticeable periods. Keep that work off the WPF dispatcher, then resume here for the
                // actual profile state changes and UI-facing events.
                var runningApplications = await Task.Run(() => GetRunningApplications(includeCommandLines));
                if (_disposed) return;

                // The dispatcher was free while the scan ran, so profiles/rules may have been edited
                // or removed. Re-resolve the live profile set before applying any state changes.
                profiles = EnumerateProfiles(_context.Profiles).ToList();
                await ApplyEvaluationAsync(profiles, runningApplications);
            }
            catch (Exception exception)
            {
                Logger.Error("Auto-profile evaluation failed.", exception);
            }
            finally
            {
                _evaluationInProgress = false;
            }
        }

        private async Task ApplyEvaluationAsync(IList<Profile> profiles, IList<RunningApplication> runningApplications)
        {
            if (_disposed) return;

            ClearExpiredRuntimeState(profiles, runningApplications);

            foreach (var ownedGuid in _ownedProfileGuids.ToList())
            {
                if (_disposed) return;
                var ownedProfile = profiles.FirstOrDefault(profile => profile.Guid == ownedGuid);
                if (ownedProfile == null)
                {
                    _ownedProfileGuids.Remove(ownedGuid);
                    _failedStopGuids.Remove(ownedGuid);
                    continue;
                }

                if (IsEligible(ownedProfile, runningApplications) && ownedProfile.IsActive())
                {
                    _failedStopGuids.Remove(ownedGuid);
                    continue;
                }

                await StopOwnedProfileAsync(ownedProfile, runningApplications);
            }

            foreach (var profile in profiles)
            {
                if (_disposed) return;
                if (profile.IsActive() || _failedStopGuids.Contains(profile.Guid) || !IsEligible(profile, runningApplications)) continue;
                await ActivateAutomaticallyAsync(profile, runningApplications);
            }
        }

        private async Task ActivateAutomaticallyAsync(Profile profile, IList<RunningApplication> runningApplications)
        {
            DateTime nextAttemptUtc;
            if (_nextActivationAttemptUtc.TryGetValue(profile.Guid, out nextAttemptUtc) && DateTime.UtcNow < nextAttemptUtc) return;

            try
            {
                await _context.SubscriptionsManager.ActivateAutomaticProfileAsync(profile, () =>
                    !_disposed && !profile.IsActive() && IsEligible(profile, runningApplications) &&
                    EnumerateProfiles(_context.Profiles).Contains(profile), success =>
                    {
                        if (_disposed) return;
                        if (success)
                        {
                            _ownedProfileGuids.Add(profile.Guid);
                            _nextActivationAttemptUtc.Remove(profile.Guid);
                            Logger.Info("Auto-applied profile '" + profile.ProfileBreadCrumbs() + "'.");
                        }
                        else
                        {
                            _nextActivationAttemptUtc[profile.Guid] = DateTime.UtcNow.Add(FailedActivationRetryDelay);
                            Logger.Warn("Auto-apply failed for profile '" + profile.ProfileBreadCrumbs() + "'; retrying in 5 seconds.");
                        }
                    });
            }
            catch (Exception exception)
            {
                _nextActivationAttemptUtc[profile.Guid] = DateTime.UtcNow.Add(FailedActivationRetryDelay);
                Logger.Error("Auto-apply failed for profile '" + profile.ProfileBreadCrumbs() + "'; retrying in 5 seconds.", exception);
            }
        }

        private async Task StopOwnedProfileAsync(Profile profile, IList<RunningApplication> runningApplications)
        {
            try
            {
                await _context.SubscriptionsManager.DeactivateAutomaticProfileAsync(profile, () =>
                    !_disposed && _ownedProfileGuids.Contains(profile.Guid) &&
                    (!IsEligible(profile, runningApplications) || (_failedStopGuids.Contains(profile.Guid) && !profile.IsActive())),
                    success =>
                    {
                        if (success)
                        {
                            _ownedProfileGuids.Remove(profile.Guid);
                            _failedStopGuids.Remove(profile.Guid);
                            Logger.Info("Auto-stopped profile '" + profile.ProfileBreadCrumbs() + "'.");
                        }
                        else
                        {
                            _failedStopGuids.Add(profile.Guid);
                            Logger.Warn("Auto-stop could not cleanly stop profile '" + profile.ProfileBreadCrumbs() + "'; it will retry.");
                        }
                    });
            }
            catch (Exception exception)
            {
                if (_ownedProfileGuids.Contains(profile.Guid)) _failedStopGuids.Add(profile.Guid);
                Logger.Error("Auto-stop failed for profile '" + profile.ProfileBreadCrumbs() + "'.", exception);
            }
        }

        private void OnActiveProfileChanged(Profile profile)
        {
            if (_disposed || _context.SubscriptionsManager.IsAutomaticTransition || _ownedProfileGuids.Count == 0) return;

            var manuallyStopped = _ownedProfileGuids
                .Where(guid => !_failedStopGuids.Contains(guid) && !_context.ActiveProfiles.Any(active => active.Guid == guid))
                .ToList();
            foreach (var guid in manuallyStopped)
            {
                _suppressedProfileGuids.Add(guid);
                _ownedProfileGuids.Remove(guid);
                Logger.Info("Manual profile stop detected; suppressing that auto-profile until all matching applications exit.");
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
                CommandLine = commandLine
            });
        }

        private static bool RuleMatches(ProfileApplicationRule rule, RunningApplication application)
        {
            if (rule == null || application == null) return false;
            var expectedExecutable = NormalizeExecutableName(rule.Executable);
            if (string.IsNullOrWhiteSpace(expectedExecutable) ||
                !string.Equals(expectedExecutable, application.ExecutableName, StringComparison.OrdinalIgnoreCase)) return false;

            var arguments = (rule.Arguments ?? string.Empty).Trim();
            if (arguments.Length == 0 || application.ExecutableOnlyFallback) return true;
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
            var executableOnlyFallback = false;
            if (includeCommandLines)
            {
                try
                {
                    return GetRunningApplicationsWithCommandLines();
                }
                catch (Exception exception)
                {
                    Logger.Warn("Unable to query process command lines; falling back to executable-only matching.", exception);
                    executableOnlyFallback = true;
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
                    if (!string.IsNullOrWhiteSpace(name))
                        result.Add(new RunningApplication { ExecutableName = name, ExecutableOnlyFallback = executableOnlyFallback });
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
            using (var collection = searcher.Get())
            {
                foreach (ManagementObject process in collection)
                {
                    try
                    {
                        var name = NormalizeExecutableName(process["Name"] as string);
                        if (string.IsNullOrWhiteSpace(name)) continue;
                        result.Add(new RunningApplication
                        {
                            ExecutableName = name,
                            CommandLine = process["CommandLine"] as string ?? string.Empty
                        });
                    }
                    finally
                    {
                        process.Dispose();
                    }
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
            public bool ExecutableOnlyFallback { get; set; }
        }
    }
}
