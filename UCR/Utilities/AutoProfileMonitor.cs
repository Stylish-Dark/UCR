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
    /// whose argument text contains that string.
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
                var pathNames = new HashSet<string>(enabledRules
                    .Where(rule => RequiresExecutablePath(rule.Executable))
                    .Select(rule => NormalizeExecutableName(rule.Executable)), StringComparer.OrdinalIgnoreCase);

                // Process enumeration (especially Win32_Process/WMI for command lines) can block for
                // noticeable periods. Keep that work off the WPF dispatcher, then resume here for the
                // actual profile state changes and UI-facing events.
                var runningApplications = await Task.Run(() => GetRunningApplications(includeCommandLines, pathNames));
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

                if (GetEligibility(ownedProfile, runningApplications) != MatchStatus.NoMatch && ownedProfile.IsActive())
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
                    (GetEligibility(profile, runningApplications) == MatchStatus.NoMatch || !profile.IsActive()),
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
            return profile != null && !_suppressedProfileGuids.Contains(profile.Guid) &&
                   GetEligibility(profile, runningApplications) == MatchStatus.Match;
        }

        private static MatchStatus GetEligibility(Profile profile, IList<RunningApplication> runningApplications)
        {
            if (profile?.AutoActivateEnabled != true) return MatchStatus.NoMatch;
            var rules = GetRules(profile).Where(IsConfigured).ToList();
            if (rules.Count == 0) return MatchStatus.NoMatch;
            if (runningApplications == null) return MatchStatus.Unknown;

            var result = MatchStatus.NoMatch;
            foreach (var rule in rules)
            foreach (var application in runningApplications)
            {
                var match = GetRuleMatch(rule, application);
                if (match == MatchStatus.Match) return match;
                if (match == MatchStatus.Unknown) result = match;
            }
            return result;
        }

        private void ClearExpiredRuntimeState(IEnumerable<Profile> profiles, IList<RunningApplication> runningApplications)
        {
            var possiblyRunning = new HashSet<Guid>(profiles
                .Where(profile => GetEligibility(profile, runningApplications) != MatchStatus.NoMatch)
                .Select(profile => profile.Guid));
            _suppressedProfileGuids.RemoveWhere(profileGuid => !possiblyRunning.Contains(profileGuid));
            foreach (var profileGuid in _nextActivationAttemptUtc.Keys.ToList())
                if (!possiblyRunning.Contains(profileGuid)) _nextActivationAttemptUtc.Remove(profileGuid);
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
                ExecutablePath = RequiresExecutablePath(executableName) ? executableName : null,
                CommandLine = commandLine
            });
        }

        private static bool RuleMatches(ProfileApplicationRule rule, RunningApplication application)
        {
            return GetRuleMatch(rule, application) == MatchStatus.Match;
        }

        private static MatchStatus GetRuleMatch(ProfileApplicationRule rule, RunningApplication application)
        {
            if (rule == null) return MatchStatus.NoMatch;
            if (application == null) return MatchStatus.Unknown;

            var expectedExecutable = NormalizeExecutableName(rule.Executable);
            if (string.IsNullOrWhiteSpace(expectedExecutable) ||
                !string.Equals(expectedExecutable, application.ExecutableName, StringComparison.OrdinalIgnoreCase))
                return MatchStatus.NoMatch;

            if (RequiresExecutablePath(rule.Executable))
            {
                var observedPath = NormalizeExecutablePath(application.ExecutablePath);
                if (observedPath == null) return MatchStatus.Unknown;
                if (!string.Equals(NormalizeExecutablePath(rule.Executable), observedPath, StringComparison.OrdinalIgnoreCase))
                    return MatchStatus.NoMatch;
            }

            var arguments = (rule.Arguments ?? string.Empty).Trim();
            if (arguments.Length == 0) return MatchStatus.Match;
            if (string.IsNullOrWhiteSpace(application.CommandLine)) return MatchStatus.Unknown;
            var observedArguments = ReadArguments(application);
            if (observedArguments == null) return MatchStatus.Unknown;
            return observedArguments.IndexOf(arguments, StringComparison.OrdinalIgnoreCase) >= 0
                ? MatchStatus.Match : MatchStatus.NoMatch;
        }

        private static string ReadArguments(RunningApplication application)
        {
            var command = application.CommandLine.TrimStart();
            var executablePath = application.ExecutablePath;
            // CreateProcess can supply an unquoted path with spaces when the executable is
            // also given separately. Use the reported executable path to find its boundary.
            if (!string.IsNullOrWhiteSpace(executablePath) &&
                command.Length >= executablePath.Length &&
                (command.Length == executablePath.Length || char.IsWhiteSpace(command[executablePath.Length])) &&
                string.Equals(command.Substring(0, executablePath.Length).Replace('/', '\\'),
                    executablePath.Replace('/', '\\'), StringComparison.OrdinalIgnoreCase))
                return command.Substring(executablePath.Length).TrimStart();

            var quoted = false;
            var index = 0;
            while (index < command.Length)
            {
                if (command[index] == '"') quoted = !quoted;
                else if (!quoted && char.IsWhiteSpace(command[index])) break;
                index++;
            }
            // When executable details are unavailable, an unquoted path with spaces
            // cannot be split at the first space unless that token names this process.
            if (quoted || !string.Equals(NormalizeExecutableName(command.Substring(0, index)),
                    application.ExecutableName, StringComparison.OrdinalIgnoreCase)) return null;
            return command.Substring(index).TrimStart();
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

        private static List<RunningApplication> GetRunningApplications(bool includeCommandLines, ISet<string> pathNames)
        {
            if (includeCommandLines || pathNames?.Count > 0)
            {
                try
                {
                    return GetRunningApplicationsFromWmi(includeCommandLines);
                }
                catch (Exception exception)
                {
                    Logger.Warn("Unable to query process details. Rules requiring unavailable information will wait for a readable process.", exception);
                }
            }

            var result = new List<RunningApplication>();
            Process[] processes;
            try { processes = Process.GetProcesses(); }
            catch (Exception exception)
            {
                Logger.Error("Unable to enumerate running processes for auto-profile detection.", exception);
                return null;
            }

            foreach (var process in processes)
            {
                try
                {
                    var name = NormalizeExecutableName(process.ProcessName);
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    var application = new RunningApplication { ExecutableName = name };
                    if (pathNames?.Contains(name) == true)
                    {
                        try { application.ExecutablePath = process.MainModule?.FileName; }
                        catch (System.ComponentModel.Win32Exception) { }
                        catch (InvalidOperationException) { }
                        catch (NotSupportedException) { }
                    }
                    result.Add(application);
                }
                catch (InvalidOperationException) { } // The process exited during enumeration.
                catch (Exception)
                {
                    // Preserve existing ownership/suppression if one process could not be identified.
                    result.Add(null);
                }
                finally { process.Dispose(); }
            }
            return result;
        }

        private static List<RunningApplication> GetRunningApplicationsFromWmi(bool includeCommandLines)
        {
            var result = new List<RunningApplication>();
            var properties = includeCommandLines ? "Name, ExecutablePath, CommandLine" : "Name, ExecutablePath";
            using (var searcher = new ManagementObjectSearcher("SELECT " + properties + " FROM Win32_Process"))
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
                            ExecutablePath = process["ExecutablePath"] as string,
                            CommandLine = includeCommandLines ? process["CommandLine"] as string : null
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
                var fileName = Path.GetFileName(trimmed.Replace('\\', '/'));
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

        private static bool RequiresExecutablePath(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            var path = value.Trim().Trim('"').Replace('/', '\\');
            return path.StartsWith(@"\\", StringComparison.Ordinal) ||
                   (path.Length > 2 && char.IsLetter(path[0]) && path[1] == ':' && path[2] == '\\');
        }

        private static string NormalizeExecutablePath(string value)
        {
            if (!RequiresExecutablePath(value)) return null;
            var path = value.Trim().Trim('"');
            try
            {
                if (Path.IsPathRooted(path)) path = Path.GetFullPath(path);
            }
            catch (ArgumentException) { return null; }
            catch (NotSupportedException) { return null; }
            catch (PathTooLongException) { return null; }
            return path.Replace('/', '\\');
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _timer.Stop();
            _timer.Tick -= OnTimerTick;
            _context.ActiveProfileChangedEvent -= OnActiveProfileChanged;
        }

        private enum MatchStatus { NoMatch, Unknown, Match }

        private sealed class RunningApplication
        {
            public string ExecutableName { get; set; }
            public string CommandLine { get; set; }
            public string ExecutablePath { get; set; }
        }
    }
}
