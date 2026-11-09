using System;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using HidWizards.IOWrapper.DataTransferObjects;
using HidWizards.UCR.Core.Annotations;
using HidWizards.UCR.Core.Models;
using HidWizards.UCR.Core.Models.Binding;
using HidWizards.UCR.Core.Models.Subscription;
using NLog;
using Logger = NLog.Logger;

namespace HidWizards.UCR.Core.Managers
{
    public sealed class SubscriptionsManager : IDisposable, INotifyPropertyChanged
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private bool _profileActive;
        public bool ProfileActive
        {
            get => _profileActive;
            set
            {
                if (_profileActive == value) return;
                _profileActive = value;
                OnPropertyChanged();
            }
        }

        internal SubscriptionState SubscriptionState { get; set; }
        private readonly Context _context;
        private readonly ExclusiveDeviceModeManager _exclusiveMode;
        private readonly Dispatcher _dispatcher;
        private readonly SemaphoreSlim _transitionGate = new SemaphoreSlim(1);
        private readonly List<Func<bool>> _failedUnsubscriptions = new List<Func<bool>>();
        private bool _exclusiveCleanupPending;
        private bool _disposed;
        private const int EmergencyStopHoldMilliseconds = 3000;
        private const string DirectInputProviderName = "SharpDX_DirectInput";
        private readonly object _emergencyStopLock = new object();
        private readonly Dictionary<string, Timer> _emergencyStopTimers = new Dictionary<string, Timer>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _emergencyStopDeviceKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private int _emergencyStopInProgress;
        private int _emergencyStopGeneration;

        public SubscriptionsManager(Context context)
        {
            _context = context;
            _dispatcher = Dispatcher.CurrentDispatcher;
            _exclusiveMode = new ExclusiveDeviceModeManager();
        }

        #region ManagerApi

        public Profile GetActiveProfile()
        {
            return SubscriptionState?.ActiveProfile;
        }

        public IReadOnlyList<Profile> GetActiveProfiles()
        {
            return SubscriptionState?.ActiveProfiles ?? _context.ActiveProfiles;
        }

        public bool IsProfileActive(Guid profileGuid)
        {
            return SubscriptionState != null && SubscriptionState.IsActive &&
                   SubscriptionState.ActiveProfiles.Any(profile => profile.Guid == profileGuid);
        }

        public string LastActivationError { get; private set; }

        public Task RecoverExclusiveModeAsync()
        {
            return _exclusiveMode.RecoverAsync();
        }

        public bool IsTransitionInProgress => _transitionGate.CurrentCount == 0;
        public bool IsAutomaticTransition { get; private set; }
        public bool CanStopProfiles => IsTransitionInProgress || _context.ActiveProfiles.Count > 0 ||
                                       _failedUnsubscriptions.Count > 0 || _exclusiveCleanupPending;

        private int _outputHealthNotificationQueued;

        public bool HasOutputWriteFailures => SubscriptionState?.IsActive == true &&
            SubscriptionState.MappingSubscriptions.Where(mapping => !mapping.Overriden)
                .SelectMany(mapping => mapping.PluginSubscriptions)
                .SelectMany(plugin => plugin.OutputSubscriptions)
                .Any(output => output.WriteFailed);

        internal void NotifyOutputWriteHealthChanged()
        {
            if (_dispatcher.HasShutdownStarted || Interlocked.Exchange(ref _outputHealthNotificationQueued, 1) != 0)
                return;

            _dispatcher.BeginInvoke(new Action(() =>
            {
                Interlocked.Exchange(ref _outputHealthNotificationQueued, 0);
                OnPropertyChanged(nameof(HasOutputWriteFailures));
            }), DispatcherPriority.Background);
        }

        public bool ActivateProfile(Profile profile, bool refreshDevices = true)
        {
            return RunSynchronously(() => ActivateProfileCoreAsync(profile, refreshDevices, false));
        }

        public Task<bool> ActivateProfileAsync(Profile profile, bool refreshDevices = true)
        {
            return RunTransitionAsync(() => ActivateProfileCoreAsync(profile, refreshDevices, true));
        }

        private Task<bool> ActivateProfileCoreAsync(Profile profile, bool refreshDevices, bool runHidHideInBackground)
        {
            if (profile == null) return Task.FromResult(false);
            var activeProfiles = GetActiveProfiles();
            var alreadyExclusive = activeProfiles.Count == 1 && activeProfiles[0].Guid == profile.Guid;
            if (alreadyExclusive && !refreshDevices) return Task.FromResult(true);

            Logger.Info((alreadyExclusive ? "Rebuilding active profile after device refresh: {" : "Activating profile exclusively: {") +
                        profile.ProfileBreadCrumbs() + "}");
            return RebuildActiveProfilesAsync(new List<Profile> { profile }, refreshDevices, profile, runHidHideInBackground);
        }

        public bool ActivateProfileAlongside(Profile profile, bool refreshDevices = true)
        {
            return RunSynchronously(() => ActivateAlongsideCoreAsync(profile, refreshDevices, false));
        }

        public Task<bool> ActivateProfileAlongsideAsync(Profile profile, bool refreshDevices = true)
        {
            return RunTransitionAsync(() => ActivateAlongsideCoreAsync(profile, refreshDevices, true));
        }

        public Task ActivateAutomaticProfileAsync(Profile profile, Func<bool> shouldActivate, Action<bool> completed)
        {
            return RunAutomaticTransitionAsync(shouldActivate, () => ActivateAlongsideCoreAsync(profile, true, true), completed);
        }

        public Task DeactivateAutomaticProfileAsync(Profile profile, Func<bool> shouldDeactivate, Action<bool> completed)
        {
            return RunAutomaticTransitionAsync(shouldDeactivate, () => DeactivateProfileCoreAsync(profile, true), completed);
        }

        private Task RunAutomaticTransitionAsync(Func<bool> shouldRun, Func<Task<bool>> operation, Action<bool> completed)
        {
            return RunTransitionAsync(async () =>
            {
                if (!shouldRun()) return false;
                IsAutomaticTransition = true;
                try
                {
                    var success = await operation();
                    completed(success);
                    return success;
                }
                finally
                {
                    IsAutomaticTransition = false;
                }
            });
        }

        private Task<bool> ActivateAlongsideCoreAsync(Profile profile, bool refreshDevices, bool runHidHideInBackground)
        {
            if (profile == null) return Task.FromResult(false);
            var targetProfiles = GetActiveProfiles().ToList();
            var alreadyActive = targetProfiles.Any(active => active.Guid == profile.Guid);
            if (!alreadyActive) targetProfiles.Add(profile);
            else if (!refreshDevices) return Task.FromResult(true);

            Logger.Info((alreadyActive ? "Rebuilding multi-profile runtime: {" : "Adding profile alongside active set: {") +
                        profile.ProfileBreadCrumbs() + "}");
            return RebuildActiveProfilesAsync(targetProfiles, refreshDevices, profile, runHidHideInBackground);
        }

        public bool RefreshActiveProfile(Profile profile)
        {
            return RunSynchronously(() => RefreshActiveProfileCoreAsync(profile, false));
        }

        public Task<bool> RefreshActiveProfileAsync(Profile profile)
        {
            return RunTransitionAsync(() => RefreshActiveProfileCoreAsync(profile, true));
        }

        private Task<bool> RefreshActiveProfileCoreAsync(Profile profile, bool runHidHideInBackground)
        {
            if (profile == null || !IsProfileActive(profile.Guid)) return Task.FromResult(false);
            Logger.Info("Refreshing active profile after runtime configuration change: {" +
                        profile.ProfileBreadCrumbs() + "}");
            return RebuildActiveProfilesAsync(GetActiveProfiles().ToList(), false, profile, runHidHideInBackground);
        }

        public bool DeactivateProfile(Profile profile)
        {
            return RunSynchronously(() => DeactivateProfileCoreAsync(profile, false));
        }

        public Task<bool> DeactivateProfileAsync(Profile profile)
        {
            return RunTransitionAsync(() => DeactivateProfileCoreAsync(profile, true));
        }

        private async Task<bool> DeactivateProfileCoreAsync(Profile profile, bool runHidHideInBackground)
        {
            if (profile == null) return true;
            var activeProfiles = GetActiveProfiles();
            var targetProfiles = activeProfiles.Where(active => active.Guid != profile.Guid).ToList();
            if (targetProfiles.Count == activeProfiles.Count)
            {
                // The logical profile may already be inactive after an incomplete stop. Retry any
                // outstanding provider cleanup and reconcile HidHide if the runtime is empty.
                var cleaned = RetryFailedUnsubscriptions();
                return (targetProfiles.Count > 0 || await ReleaseExclusiveModeAsync(runHidHideInBackground)) && cleaned;
            }

            Logger.Info("Removing profile from active set: {" + profile.ProfileBreadCrumbs() + "}");
            var stopped = await RebuildActiveProfilesAsync(targetProfiles, false, profile, runHidHideInBackground);
            return stopped && _failedUnsubscriptions.Count == 0;
        }

        public bool DeactivateCurrentProfile()
        {
            return RunSynchronously(() => StopAllProfilesCoreAsync(false));
        }

        public Task<bool> DeactivateCurrentProfileAsync()
        {
            return RunTransitionAsync(() => StopAllProfilesCoreAsync(true));
        }

        internal Task<T> StopBeforeReplacingProfilesAsync<T>(Func<T> replace)
        {
            return RunTransitionAsync(async () =>
            {
                if (!await StopAllProfilesCoreAsync(true))
                    throw new InvalidOperationException("The active profiles could not be deactivated before replacing the profile list.");
                return replace();
            });
        }

        private Task<bool> StopAllProfilesCoreAsync(bool runHidHideInBackground)
        {
            CancelEmergencyStopTimers();
            return RebuildActiveProfilesAsync(new List<Profile>(), false, null, runHidHideInBackground);
        }

        private bool RunSynchronously(Func<Task<bool>> operation)
        {
            _dispatcher.VerifyAccess();
            if (_disposed || !_transitionGate.Wait(0)) return false;
            try
            {
                return operation().GetAwaiter().GetResult();
            }
            finally
            {
                _transitionGate.Release();
            }
        }

        private async Task<T> RunTransitionAsync<T>(Func<Task<T>> operation)
        {
            _dispatcher.VerifyAccess();
            await _transitionGate.WaitAsync();
            try
            {
                if (_disposed) return default(T);
                NotifyTransitionState();
                return await operation();
            }
            finally
            {
                _transitionGate.Release();
                NotifyTransitionState();
            }
        }

        private void NotifyTransitionState()
        {
            OnPropertyChanged(nameof(IsTransitionInProgress));
            OnPropertyChanged(nameof(CanStopProfiles));
        }

        private Task<bool> ApplyExclusiveModeAsync(IList<Profile> profiles, bool runInBackground)
        {
            return runInBackground ? _exclusiveMode.ApplyAsync(profiles) : Task.FromResult(_exclusiveMode.Apply(profiles));
        }

        private async Task<bool> ReleaseExclusiveModeAsync(bool runInBackground)
        {
            var released = await ApplyExclusiveModeAsync(new List<Profile>(), runInBackground);
            _exclusiveCleanupPending = !released;
            return released;
        }

        private async Task<bool> RebuildActiveProfilesAsync(IList<Profile> targetProfiles, bool refreshDevices,
            Profile changedProfile, bool runHidHideInBackground)
        {
            LastActivationError = null;
            targetProfiles = NormalizeProfiles(targetProfiles);
            var previousProfiles = NormalizeProfiles(GetActiveProfiles().ToList());
            var previousState = SubscriptionState;
            var pendingCleanupSuccess = RetryFailedUnsubscriptions();

            if (refreshDevices)
            {
                Logger.Debug("Refreshing device providers before rebuilding active profiles");
                _context.DevicesManager.RefreshDeviceList();
            }

            if (targetProfiles.Count == 0)
            {
                var stopSuccess = true;
                if (previousState != null && previousState.IsActive)
                {
                    stopSuccess = DeactivateProfile(previousState);
                    if (!stopSuccess)
                        Logger.Warn("One or more subscriptions could not be removed while stopping the active profile set.");
                }
                SubscriptionState = null;
                _context.SetActiveProfiles(Enumerable.Empty<Profile>());
                ProfileActive = false;
                var released = await ReleaseExclusiveModeAsync(runHidHideInBackground);
                if (!released) Logger.Error("Exclusive Device Mode cleanup failed on profile stop.");
                _context.OnActiveProfileChangedEvent(changedProfile);
                return stopSuccess && released && pendingCleanupSuccess;
            }

            foreach (var profile in targetProfiles)
            {
                if (!profile.PruneUndefinedFilterReferencesRecursive()) continue;
                Logger.Warn("Removed undefined filter references before activating profile: {" + profile.ProfileBreadCrumbs() + "}");
                _context.ContextChanged();
            }

            // Build and validate the candidate state before disturbing the currently running set.
            // This catches missing mappings/filter failures without dropping a working profile.
            var candidate = BuildSubscriptionState(targetProfiles);
            if (candidate == null)
            {
                LastActivationError = "Could not prepare the mappings, device bindings, or filters. Check that every required device is connected and its provider is available.";
                Logger.Error("Unable to build candidate active profile state; existing profiles remain running.");
                return false;
            }

            var unsubscribeSuccess = true;
            if (previousState != null && previousState.IsActive)
            {
                unsubscribeSuccess = DeactivateProfile(previousState);
                if (!unsubscribeSuccess)
                    Logger.Warn("One or more subscriptions could not be removed while switching active profiles.");
            }
            SubscriptionState = null;

            if (!ActivateSubscriptionState(candidate))
            {
                LastActivationError = "A device subscription could not start. A configured input or output device may be disconnected, unavailable, or already in use.";
                Logger.Error("Failed to activate candidate profile set; restoring the previous active profiles.");
                if (candidate.IsActive) DeactivateProfile(candidate);
                SubscriptionState = null;

                if (previousProfiles.Count > 0 && await RestorePreviousProfilesAsync(previousProfiles, changedProfile, runHidHideInBackground))
                {
                    Logger.Warn("Previous active profile set restored after activation failure.");
                }
                else
                {
                    if (previousProfiles.Count > 0) LastActivationError += " The previous profiles could not be restored.";
                    await ClearFailedStateAsync(changedProfile, runHidHideInBackground);
                }
                return false;
            }

            // Hide the physical controller only after its input subscriptions exist.
            // If HidHide is unavailable, do not claim the requested exclusive profile started.
            if (!await ApplyExclusiveModeAsync(targetProfiles, runHidHideInBackground))
            {
                LastActivationError = "Exclusive Device Mode could not be applied. Check HidHide availability and device permissions.";
                Logger.Error("Unable to apply Exclusive Device Mode; restoring the previous profile.");
                DeactivateProfile(candidate);
                SubscriptionState = null;
                if (previousProfiles.Count > 0)
                {
                    if (await RestorePreviousProfilesAsync(previousProfiles, changedProfile, runHidHideInBackground)) return false;
                    LastActivationError += " The previous profiles could not be restored.";
                }
                await ClearFailedStateAsync(changedProfile, runHidHideInBackground);
                return false;
            }
            FinalizeNewState(targetProfiles, candidate, changedProfile);
            // The candidate is live. Teardown warnings from the old state are forensic only and must
            // not make the UI claim this successful switch failed.
            return true;
        }

        private static List<Profile> NormalizeProfiles(IEnumerable<Profile> profiles)
        {
            return (profiles ?? Enumerable.Empty<Profile>())
                .Where(profile => profile != null)
                .GroupBy(profile => profile.Guid)
                .Select(group => group.First())
                .ToList();
        }

        private SubscriptionState BuildSubscriptionState(IList<Profile> profiles)
        {
            var state = new SubscriptionState(profiles);
            foreach (var profile in profiles)
            {
                var populatedLayers = new HashSet<Guid>();
                var profileOutputDevices = new List<DeviceConfigurationSubscription>();
                if (!PopulateSubscriptionStateForProfile(state, profile, profile.Guid, populatedLayers, profileOutputDevices))
                {
                    Logger.Error("Failed to populate SubscriptionState for profile: " + profile.ProfileBreadCrumbs());
                    return null;
                }
            }

            Logger.Debug("Successfully populated composite subscription state");
            if (ConfigureFiltersForState(state)) return state;

            Logger.Error("Failed to configure filters for composite subscription state");
            return null;
        }

        private async Task<bool> RestorePreviousProfilesAsync(IList<Profile> previousProfiles, Profile changedProfile,
            bool runHidHideInBackground)
        {
            var rollback = BuildSubscriptionState(previousProfiles);
            if (rollback == null) return false;
            if (!ActivateSubscriptionState(rollback))
            {
                if (rollback.IsActive) DeactivateProfile(rollback);
                return false;
            }

            if (!await ApplyExclusiveModeAsync(previousProfiles, runHidHideInBackground))
            {
                Logger.Error("Unable to restore Exclusive Device Mode for previous profiles.");
                DeactivateProfile(rollback);
                return false;
            }
            FinalizeNewState(previousProfiles, rollback, changedProfile);
            return true;
        }

        private async Task ClearFailedStateAsync(Profile changedProfile, bool runHidHideInBackground)
        {
            // A failed driver operation may have changed the cloak before it failed. Recover the
            // ownership journal before reporting that no profiles are active.
            if (!await ReleaseExclusiveModeAsync(runHidHideInBackground))
                Logger.Error("Exclusive Device Mode cleanup failed after activation rollback.");
            SubscriptionState = null;
            _context.SetActiveProfiles(Enumerable.Empty<Profile>());
            ProfileActive = false;
            _context.OnActiveProfileChangedEvent(changedProfile);
        }

        private void FinalizeNewState(IList<Profile> profiles, SubscriptionState subscriptionState, Profile changedProfile)
        {
            _exclusiveCleanupPending = false;
            SubscriptionState = subscriptionState;
            _context.SetActiveProfiles(profiles);

            foreach (var mapping in subscriptionState.MappingSubscriptions)
            {
                if (mapping.Overriden) continue;
                if (mapping.PluginSubscriptions.Any(subscription => subscription.Plugin.InitializeFromAxisSnapshot))
                    SeedDirectInputAxisSnapshots(mapping);
                foreach (var pluginSubscription in mapping.PluginSubscriptions)
                {
                    pluginSubscription.Plugin.InitializeCacheValues();
                    pluginSubscription.Plugin.OnActivate();
                }
                mapping.Mapping.StartFilterRouting();
            }

            ProfileActive = profiles.Count > 0;
            _context.OnActiveProfileChangedEvent(changedProfile);
            Logger.Info("Active profile set rebuilt: " + string.Join(", ", profiles.Select(profile => profile.Title)));
        }

        private void SeedDirectInputAxisSnapshots(MappingSubscription mapping)
        {
            var axes = mapping.DeviceBindingSubscriptions.Where(subscription =>
            {
                var request = subscription.SubscribedRequest;
                return request != null && request.BindingDescriptor.Type == BindingType.Axis &&
                       string.Equals(request.ProviderDescriptor.ProviderName, DirectInputProviderName,
                           StringComparison.OrdinalIgnoreCase);
            }).ToList();
            if (axes.Count == 0) return;

            // The snapshot API is additive. Older DirectInput builds continue to supply inputs
            // through the existing callbacks, with no dependency on this optional method.
            var provider = _context.IOController.GetProvider(axes[0].SubscribedRequest.ProviderDescriptor.ProviderName);
            var readAxis = provider?.GetType().GetMethod("TryGetCurrentAxis", new[]
            {
                typeof(DeviceDescriptor), typeof(BindingDescriptor), typeof(short).MakeByRefType()
            });
            if (readAxis == null || readAxis.ReturnType != typeof(bool)) return;

            foreach (var axis in axes)
            {
                var request = axis.SubscribedRequest;
                var arguments = new object[] { request.DeviceDescriptor, request.BindingDescriptor, (short)0 };
                short? value = null;
                try
                {
                    if ((bool)readAxis.Invoke(provider, arguments)) value = (short)arguments[2];
                }
                catch (Exception exception)
                {
                    Logger.Warn(exception, "Unable to read the initial DirectInput axis value.");
                }

                // A live report takes precedence. If neither source has a sample, leave the
                // filter uninitialised until its first real report instead of toggling on zero.
                mapping.Mapping.SeedInitialAxisSnapshot(axis.DeviceBinding, value);
            }
        }

        public bool DeactivateProfile(SubscriptionState state)
        {
            if (state == null) return true;
            var success = true;
            CancelEmergencyStopTimers();

            // Safety subscriptions include the global Esc rescue key and generated blockers for
            // Block Unmapped Inputs. Remove these first so normal input is restored immediately.
            foreach (var safetySubscription in state.SafetyInputSubscriptions.ToList())
            {
                success &= UnsubscribeDeviceBindingInput(safetySubscription);
            }
            state.SafetyInputSubscriptions.Clear();

            foreach (var mappingSubscription in state.MappingSubscriptions)
            {
                if (mappingSubscription.Overriden) continue;

                mappingSubscription.Mapping.StopFilterRouting();
                foreach (var deviceBindingSubscription in mappingSubscription.DeviceBindingSubscriptions)
                {
                    success &= UnsubscribeDeviceBindingInput(deviceBindingSubscription);
                }

                foreach (var pluginSubscription in mappingSubscription.PluginSubscriptions)
                {
                    pluginSubscription.Plugin.OnDeactivate();
                    pluginSubscription.DetachOutputs();
                }
            }

            foreach (var deviceConfigurationSubscription in state.OutputDeviceConfigurationSubscriptions)
            {
                foreach (var shadowDeviceSubscription in deviceConfigurationSubscription.ShadowDeviceSubscriptions)
                {
                    success &= UnsubscribeOutput(shadowDeviceSubscription);
                }

                success &= UnsubscribeOutput(deviceConfigurationSubscription.DeviceSubscription);
            }

            state.IsActive = false;
            return success;
        }

        #endregion

        private bool PopulateSubscriptionStateForProfile(SubscriptionState state, Profile profile,
            Guid runtimeScopeGuid, ISet<Guid> populatedLayers,
            ICollection<DeviceConfigurationSubscription> profileOutputDevices)
        {
            if (profile == null) return true;
            if (populatedLayers.Contains(profile.Guid)) return true;

            var success = true;
            profile.PrepareProfile();

            if (profile.ParentProfile != null)
            {
                success &= PopulateSubscriptionStateForProfile(state, profile.ParentProfile, runtimeScopeGuid,
                    populatedLayers, profileOutputDevices);
            }

            foreach (var deviceConfiguration in profile.OutputDeviceConfigurations ?? new List<DeviceConfiguration>())
            {
                var subscription = state.AddOutputDeviceConfiguration(deviceConfiguration, runtimeScopeGuid);
                if (subscription != null && !profileOutputDevices.Contains(subscription))
                    profileOutputDevices.Add(subscription);
            }

            var scopedOutputs = profileOutputDevices.ToList();
            if (profile.MainMappingsEnabled)
                state.AddMappings(profile, profile.Mappings ?? new List<Mapping>(), runtimeScopeGuid, scopedOutputs);
            foreach (var group in profile.MappingGroups ?? new List<MappingGroup>())
            {
                if (group == null || !group.Enabled) continue;
                state.AddMappings(profile, group.Mappings ?? new List<Mapping>(), runtimeScopeGuid, scopedOutputs);
            }

            populatedLayers.Add(profile.Guid);
            return success;
        }

        private bool ConfigureFiltersForState(SubscriptionState state)
        {
            var uniqueFilters = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);
            foreach (var mappingSubscription in state.MappingSubscriptions)
            {
                if (mappingSubscription.Overriden) continue;
                var runtimeMapping = mappingSubscription.Mapping;
                foreach (var plugin in runtimeMapping.Plugins)
                {
                    var definition = plugin.GetDefinedFilterName();
                    if (string.IsNullOrWhiteSpace(definition)) continue;

                    var key = Mapping.GetRuntimeFilterKey(mappingSubscription.RuntimeScopeGuid, definition);
                    if (runtimeMapping.IsShadowMapping)
                    {
                        key = Filter.GetShadowName(key, runtimeMapping.ShadowDeviceNumber);
                    }
                    uniqueFilters.Add(key);
                }
            }

            foreach (var uniqueFilter in uniqueFilters)
                state.FilterState.Define(uniqueFilter);

            return true;
        }

        // Subscribes the backend when it is built
        private bool ActivateSubscriptionState(SubscriptionState state)
        {
            var success = true;
            if (state.IsActive) return true;

            // Most mappings in a profile point at the same one or two configured devices. Resolve a
            // configured Device object once per rebuild instead of re-enumerating providers for every
            // individual binding that happens to use it.
            var inputResolutionCache = new Dictionary<Device, Device>(DeviceReferenceComparer.Instance);
            var outputResolutionCache = new Dictionary<Device, Device>(DeviceReferenceComparer.Instance);

            foreach (var deviceConfigurationSubscription in state.OutputDeviceConfigurationSubscriptions)
            {
                success &= SubscribeOutput(state, deviceConfigurationSubscription.DeviceSubscription,
                    outputResolutionCache);

                foreach (var shadowDeviceSubscription in deviceConfigurationSubscription.ShadowDeviceSubscriptions)
                {
                    success &= SubscribeOutput(state, shadowDeviceSubscription, outputResolutionCache);
                }
            }

            // Install the hardwired rescue listener before any profile input subscription can start
            // suppressing keys. It is deliberately outside the mapping engine.
            success &= SubscribeEmergencyStopInputs(state, inputResolutionCache);

            foreach (var mappingSubscription in state.MappingSubscriptions)
            {
                if (mappingSubscription.Overriden) continue;

                mappingSubscription.Mapping.PrepareMapping(state.FilterState, mappingSubscription.RuntimeScopeGuid);

                foreach (var deviceBindingSubscription in mappingSubscription.DeviceBindingSubscriptions)
                {
                    success &= SubscribeDeviceBindingInput(state, deviceBindingSubscription, inputResolutionCache);
                }
            }

            success &= SubscribeUnmappedInputBlockers(state, inputResolutionCache);

            // Candidate construction must never replace the old profile's output sinks.
            // Install callbacks only after the new output devices and input subscriptions
            // have been established. A bound output with no target is a failed activation,
            // not a silently successful profile that produces no output.
            if (success)
            {
                foreach (var mappingSubscription in state.MappingSubscriptions)
                {
                    if (mappingSubscription.Overriden) continue;
                    foreach (var pluginSubscription in mappingSubscription.PluginSubscriptions)
                    {
                        var expected = pluginSubscription.Plugin.Outputs.Count(binding => binding.IsBound);
                        var attached = pluginSubscription.OutputSubscriptions.Count;
                        if (expected != attached)
                        {
                            Logger.Error("Cannot activate mapping '{0}': {1} bound output(s), but only {2} resolved output subscription(s).",
                                mappingSubscription.Mapping.Title, expected, attached);
                            success = false;
                            continue;
                        }
                        foreach (var outputSubscription in pluginSubscription.OutputSubscriptions)
                        {
                            if (outputSubscription.DeviceSubscription?.SubscribedOutputRequest == null)
                            {
                                Logger.Error("Cannot activate mapping '{0}': destination output device is not subscribed.",
                                    mappingSubscription.Mapping.Title);
                                success = false;
                            }
                        }
                    }
                }
            }

            if (success)
            {
                foreach (var mappingSubscription in state.MappingSubscriptions)
                {
                    if (mappingSubscription.Overriden) continue;
                    foreach (var pluginSubscription in mappingSubscription.PluginSubscriptions)
                        foreach (var outputSubscription in pluginSubscription.OutputSubscriptions)
                            outputSubscription.Attach();
                }
            }

            // Mark as active so unsuccessful candidates can be torn down correctly;
            // the caller checks success and rolls back to the previous profile.
            state.IsActive = true;
            return success;
        }

        private bool SubscribeEmergencyStopInputs(SubscriptionState state,
            IDictionary<Device, Device> resolutionCache)
        {
            var success = true;
            List<Device> devices;
            try
            {
                devices = _context.DevicesManager.GetAvailableDeviceList(DeviceIoType.Input, false);
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "Unable to enumerate keyboards for the emergency stop");
                return false;
            }

            foreach (var configuredDevice in devices.Where(device => device != null && !device.IsCache))
            {
                var runtimeDevice = ResolveRuntimeDevice(configuredDevice, DeviceIoType.Input, resolutionCache);
                if (runtimeDevice == null) continue;

                var escape = FindEscapeBinding(runtimeDevice);
                if (escape == null) continue;

                var deviceKey = GetRuntimeDeviceKey(runtimeDevice);
                var binding = CreateSafetyBinding(state.ActiveProfile, escape, false,
                    value => HandleEmergencyEscape(deviceKey, value));
                var subscription = new InputSubscription(binding, state.ActiveProfile, state.StateGuid, runtimeDevice);
                subscription.DeviceSubscription.ResolvedDevice = runtimeDevice;
                state.SafetyInputSubscriptions.Add(subscription);
                var subscribed = SubscribeDeviceBindingInput(state, subscription, resolutionCache);
                success &= subscribed;
                if (subscribed)
                {
                    lock (_emergencyStopLock) _emergencyStopDeviceKeys.Add(deviceKey);
                }
            }

            return success;
        }

        private bool SubscribeUnmappedInputBlockers(SubscriptionState state,
            IDictionary<Device, Device> resolutionCache)
        {
            var success = true;
            var mappedByDevice = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var mappingSubscription in state.MappingSubscriptions)
            {
                if (mappingSubscription.Overriden) continue;
                foreach (var inputSubscription in mappingSubscription.DeviceBindingSubscriptions)
                {
                    var runtimeDevice = inputSubscription.DeviceSubscription?.ResolvedDevice;
                    var binding = inputSubscription.DeviceBinding;
                    if (runtimeDevice == null || binding == null || !binding.IsBound) continue;

                    var deviceKey = GetRuntimeDeviceKey(runtimeDevice);
                    HashSet<string> mapped;
                    if (!mappedByDevice.TryGetValue(deviceKey, out mapped))
                    {
                        mapped = new HashSet<string>(StringComparer.Ordinal);
                        mappedByDevice[deviceKey] = mapped;
                    }
                    mapped.Add(GetBindingKey(binding.KeyType, binding.KeyValue, binding.KeySubValue));
                }
            }

            var processedDevices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var profile in state.ActiveProfiles)
            {
                if (profile == null) continue;

                foreach (var configuration in profile.GetDeviceConfigurationList(DeviceIoType.Input)
                    .Where(item => item != null && item.Device != null && _context.DevicesManager.GetDeviceBlockUnmappedInputs(item.Device)))
                {
                    var runtimeDevice = ResolveRuntimeDevice(configuration.Device, DeviceIoType.Input, resolutionCache);
                    if (runtimeDevice == null)
                    {
                        Logger.Error("Block Unmapped Inputs could not resolve device: {" + configuration.Device.LogName() + "}");
                        success = false;
                        continue;
                    }

                    var deviceKey = GetRuntimeDeviceKey(runtimeDevice);
                    if (!processedDevices.Add(deviceKey)) continue;

                    // A keyboard may only enter Block Unmapped mode after its hardwired Esc listener
                    // is confirmed live. Failure is deliberately fail-open.
                    if (FindEscapeBinding(runtimeDevice) != null)
                    {
                        lock (_emergencyStopLock)
                        {
                            if (!_emergencyStopDeviceKeys.Contains(deviceKey))
                            {
                                Logger.Error("Refusing Block Unmapped Inputs because the emergency Esc listener is unavailable for: {" +
                                             runtimeDevice.LogName() + "}");
                                success = false;
                                continue;
                            }
                        }
                    }

                    HashSet<string> mapped;
                    if (!mappedByDevice.TryGetValue(deviceKey, out mapped))
                        mapped = new HashSet<string>(StringComparer.Ordinal);

                    foreach (var node in FlattenBindingNodes(
                        _context.DevicesManager.GetDeviceBindingMenu(runtimeDevice, DeviceIoType.Input, false)))
                    {
                        var info = node?.DeviceBindingInfo;
                        if (info == null || !info.Blockable) continue;
                        if (mapped.Contains(GetBindingKey(info.KeyType, info.KeyValue, info.KeySubValue))) continue;

                        var binding = CreateSafetyBinding(profile, info, true, value => { });
                        var subscription = new InputSubscription(binding, profile, state.StateGuid, runtimeDevice);
                        subscription.DeviceSubscription.ResolvedDevice = runtimeDevice;
                        state.SafetyInputSubscriptions.Add(subscription);
                        success &= SubscribeDeviceBindingInput(state, subscription, resolutionCache);
                    }
                }
            }

            return success;
        }

        private DeviceBindingInfo FindEscapeBinding(Device runtimeDevice)
        {
            var nodes = FlattenBindingNodes(
                _context.DevicesManager.GetDeviceBindingMenu(runtimeDevice, DeviceIoType.Input, false)).ToList();

            var named = nodes.FirstOrDefault(node =>
                node?.DeviceBindingInfo != null &&
                (string.Equals(node.Title, "Esc", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(node.Title, "Escape", StringComparison.OrdinalIgnoreCase)));
            if (named != null) return named.DeviceBindingInfo;

            // Interception exposes keyboard scan code 1 as button index 0. Keep this fallback
            // deliberately keyboard-specific; mouse button zero must never become a rescue key.
            if (string.Equals(runtimeDevice.ProviderName, "Core_Interception", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(runtimeDevice.Title) &&
                runtimeDevice.Title.IndexOf("keyboard", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return nodes.Select(node => node?.DeviceBindingInfo)
                    .FirstOrDefault(info => info != null &&
                                            info.DeviceBindingCategory == DeviceBindingCategory.Momentary &&
                                            info.KeyValue == 0 && info.KeySubValue == 0);
            }

            return null;
        }

        private static DeviceBinding CreateSafetyBinding(Profile profile, DeviceBindingInfo info, bool block,
            Action<short> callback)
        {
            return new DeviceBinding(callback ?? (value => { }), profile, DeviceIoType.Input)
            {
                IsBound = true,
                DeviceBindingCategory = info.DeviceBindingCategory,
                KeyType = info.KeyType,
                KeyValue = info.KeyValue,
                KeySubValue = info.KeySubValue,
                Block = block
            };
        }

        private void HandleEmergencyEscape(string deviceKey, short value)
        {
            if (string.IsNullOrWhiteSpace(deviceKey)) return;

            lock (_emergencyStopLock)
            {
                Timer existing;
                if (value != 0)
                {
                    if (_emergencyStopTimers.ContainsKey(deviceKey)) return;
                    var generation = _emergencyStopGeneration;
                    Timer timer = null;
                    timer = new Timer(ignored => QueueEmergencyStop(deviceKey, timer, generation), null,
                        Timeout.Infinite, Timeout.Infinite);
                    _emergencyStopTimers.Add(deviceKey, timer);
                    timer.Change(EmergencyStopHoldMilliseconds, Timeout.Infinite);
                    return;
                }

                if (!_emergencyStopTimers.TryGetValue(deviceKey, out existing)) return;
                _emergencyStopTimers.Remove(deviceKey);
                existing.Dispose();
            }
        }

        private void QueueEmergencyStop(string deviceKey, Timer expiredTimer, int generation)
        {
            lock (_emergencyStopLock)
            {
                Timer current;
                if (!_emergencyStopTimers.TryGetValue(deviceKey, out current) || !ReferenceEquals(current, expiredTimer)) return;
                _emergencyStopTimers.Remove(deviceKey);
                expiredTimer.Dispose();
            }

            _dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() => TriggerEmergencyStopAsync(generation)));
        }

        private async void TriggerEmergencyStopAsync(int generation)
        {
            lock (_emergencyStopLock)
            {
                if (generation != _emergencyStopGeneration) return;
            }

            if (Interlocked.Exchange(ref _emergencyStopInProgress, 1) != 0) return;
            try
            {
                Logger.Warn("Emergency stop triggered: Esc held for three seconds.");
                if (!await DeactivateCurrentProfileAsync())
                    Logger.Error("Emergency stop could not cleanly stop all profiles.");
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "Emergency stop failed while stopping active profiles");
            }
            finally
            {
                Interlocked.Exchange(ref _emergencyStopInProgress, 0);
            }
        }

        private void CancelEmergencyStopTimers()
        {
            lock (_emergencyStopLock)
            {
                _emergencyStopGeneration++;
                foreach (var timer in _emergencyStopTimers.Values) timer.Dispose();
                _emergencyStopTimers.Clear();
                _emergencyStopDeviceKeys.Clear();
            }
        }

        private static IEnumerable<DeviceBindingNode> FlattenBindingNodes(IEnumerable<DeviceBindingNode> nodes)
        {
            if (nodes == null) yield break;
            foreach (var node in nodes)
            {
                if (node == null) continue;
                if (node.IsBinding) yield return node;
                foreach (var child in FlattenBindingNodes(node.ChildrenNodes)) yield return child;
            }
        }

        private static string GetBindingKey(int keyType, int keyValue, int keySubValue)
        {
            return keyType + ":" + keyValue + ":" + keySubValue;
        }

        private static string GetRuntimeDeviceKey(Device device)
        {
            if (device == null) return string.Empty;
            return (device.ProviderName ?? string.Empty) + "\u001f" +
                   (device.DeviceHandle ?? string.Empty) + "\u001f" + device.DeviceNumber;
        }


        #region Subscriber Actions
        
        private bool SubscribeDeviceBindingInput(SubscriptionState state, InputSubscription deviceBindingSubscription,
            IDictionary<Device, Device> resolutionCache)
        {
            if (deviceBindingSubscription?.DeviceBinding == null) return false;
            if (!deviceBindingSubscription.DeviceBinding.IsBound) return true;

            var deviceSubscription = deviceBindingSubscription.DeviceSubscription;
            var configuredDevice = deviceSubscription?.Device;
            if (configuredDevice == null)
            {
                Logger.Error("Failed to subscribe input because its configured device is unavailable.");
                return false;
            }

            try
            {
                var runtimeDevice = ResolveRuntimeDevice(configuredDevice, DeviceIoType.Input, resolutionCache);
                if (runtimeDevice == null)
                {
                    Logger.Error($"Failed to resolve input device safely: {{{configuredDevice.LogName()}}}");
                    return false;
                }

                deviceSubscription.ResolvedDevice = runtimeDevice;
                var request = GetInputSubscriptionRequest(state, deviceBindingSubscription);
                if (!_context.IOController.SubscribeInput(request)) return false;
                deviceBindingSubscription.SubscribedRequest = request;
                return true;
            }
            catch (Exception e)
            {
                Logger.Error(e, "Failed to subscribe input");
                return false;
            }
        }

        private bool UnsubscribeDeviceBindingInput(InputSubscription deviceBindingSubscription)
        {
            var request = deviceBindingSubscription?.SubscribedRequest;
            if (request == null) return true;
            deviceBindingSubscription.SubscribedRequest = null;
            return UnsubscribeOrQueue(() => _context.IOController.UnsubscribeInput(request));
        }

        private bool SubscribeOutput(SubscriptionState state, DeviceSubscription deviceSubscription,
            IDictionary<Device, Device> resolutionCache)
        {
            var configuredDevice = deviceSubscription?.Device;
            if (configuredDevice == null)
            {
                Logger.Error("Failed to subscribe output because its configured device is unavailable.");
                return false;
            }

            Logger.Debug($"Subscribing output device: {{{configuredDevice.LogName()}}}");
            if (string.IsNullOrEmpty(configuredDevice.ProviderName) || string.IsNullOrEmpty(configuredDevice.DeviceHandle))
            {
                Logger.Error($"Failed to subscribe output device. Providername or devicehandle missing from: {{{configuredDevice.LogName()}}}");
                return false;
            }

            var runtimeDevice = ResolveRuntimeDevice(configuredDevice, DeviceIoType.Output, resolutionCache);
            if (runtimeDevice == null)
            {
                Logger.Error($"Failed to resolve output device safely: {{{configuredDevice.LogName()}}}");
                return false;
            }

            deviceSubscription.ResolvedDevice = runtimeDevice;
            var request = GetOutputSubscriptionRequest(state.StateGuid, deviceSubscription);
            var success = _context.IOController.SubscribeOutput(request);
            if (success) deviceSubscription.SubscribedOutputRequest = request;
            else Logger.Error($"Failed to subscribe output device. Provider might be unavailable: {{{configuredDevice.LogName()}}}");
            return success;
        }

        private Device ResolveRuntimeDevice(Device configuredDevice, DeviceIoType type,
            IDictionary<Device, Device> resolutionCache)
        {
            if (configuredDevice == null) return null;

            Device resolvedDevice;
            if (resolutionCache != null && resolutionCache.TryGetValue(configuredDevice, out resolvedDevice))
                return resolvedDevice;

            resolvedDevice = _context.DevicesManager.ResolveDevice(configuredDevice, type);
            if (resolutionCache != null) resolutionCache[configuredDevice] = resolvedDevice;
            return resolvedDevice;
        }

        private bool UnsubscribeOutput(DeviceSubscription deviceSubscription)
        {
            var request = deviceSubscription?.SubscribedOutputRequest;
            if (request == null) return true;
            Logger.Debug($"Unsubscribing output device: {{{deviceSubscription.Device.LogName()}}}");
            deviceSubscription.SubscribedOutputRequest = null;
            return UnsubscribeOrQueue(() => _context.IOController.UnsubscribeOutput(request));
        }

        private bool UnsubscribeOrQueue(Func<bool> unsubscribe)
        {
            if (TryUnsubscribe(unsubscribe)) return true;
            _failedUnsubscriptions.Add(unsubscribe);
            return false;
        }

        private bool RetryFailedUnsubscriptions()
        {
            _failedUnsubscriptions.RemoveAll(unsubscribe => TryUnsubscribe(unsubscribe));
            return _failedUnsubscriptions.Count == 0;
        }

        private static bool TryUnsubscribe(Func<bool> unsubscribe)
        {
            try
            {
                return unsubscribe();
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "Failed to remove a device subscription");
                return false;
            }
        }

        #endregion

        #region DescriptionHelpers

        private InputSubscriptionRequest GetInputSubscriptionRequest(SubscriptionState state, InputSubscription deviceBindingSubscription)
        {
            var device = deviceBindingSubscription.DeviceSubscription.GetRuntimeDevice();
            return new InputSubscriptionRequest()
            {
                ProviderDescriptor = GetProviderDescriptor(device),
                DeviceDescriptor = GetDeviceDescriptor(device),
                SubscriptionDescriptor = GetSubscriptionDescriptor(deviceBindingSubscription.DeviceBindingSubscriptionGuid, state.StateGuid),
                BindingDescriptor = GetBindingDescriptor(deviceBindingSubscription.DeviceBinding),
                Callback = deviceBindingSubscription.DeviceBinding.Callback,
                Block = deviceBindingSubscription.DeviceBinding.Block
            };
        }

        public static OutputSubscriptionRequest GetOutputSubscriptionRequest(Guid subscriptionStateGuid, DeviceSubscription deviceSubscription)
        {
            return new OutputSubscriptionRequest()
            {
                ProviderDescriptor = GetProviderDescriptor(deviceSubscription.GetRuntimeDevice()),
                DeviceDescriptor = GetDeviceDescriptor(deviceSubscription.GetRuntimeDevice()),
                SubscriptionDescriptor = GetSubscriptionDescriptor(deviceSubscription.DeviceSubscriptionGuid, subscriptionStateGuid)
            };
        }

        private static ProviderDescriptor GetProviderDescriptor(Device device)
        {
            return new ProviderDescriptor()
            {
                ProviderName = device.ProviderName
            };
        }

        private static DeviceDescriptor GetDeviceDescriptor(Device device)
        {
            return new DeviceDescriptor()
            {
                DeviceHandle = device.DeviceHandle,
                DeviceInstance = device.DeviceNumber
            };
        }

        private static SubscriptionDescriptor GetSubscriptionDescriptor(Guid subscriberGuid, Guid profileGuid)
        {
            return new SubscriptionDescriptor()
            {
                SubscriberGuid = subscriberGuid,
                ProfileGuid = profileGuid
            };
        }

        public static BindingDescriptor GetBindingDescriptor(DeviceBinding deviceBinding)
        {
            return new BindingDescriptor()
            {
                Type = (BindingType)deviceBinding.KeyType,
                Index = deviceBinding.KeyValue,
                SubIndex = deviceBinding.KeySubValue
            };
        }

        #endregion

        public Task DisposeAsync()
        {
            return RunTransitionAsync(async () =>
            {
                try
                {
                    await StopAllProfilesCoreAsync(true);
                }
                finally
                {
                    _disposed = true;
                    await Task.Run(() => _exclusiveMode.Dispose());
                }
                return true;
            });
        }

        public void Dispose()
        {
            if (_disposed) return;
            if (SubscriptionState != null || _failedUnsubscriptions.Count != 0 || _exclusiveCleanupPending)
            {
                DeactivateCurrentProfile();
            }
            _disposed = true;
            _exclusiveMode.Dispose();
        }

        private sealed class DeviceReferenceComparer : IEqualityComparer<Device>
        {
            public static readonly DeviceReferenceComparer Instance = new DeviceReferenceComparer();

            public bool Equals(Device x, Device y)
            {
                return ReferenceEquals(x, y);
            }

            public int GetHashCode(Device obj)
            {
                return obj == null ? 0 : RuntimeHelpers.GetHashCode(obj);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        [NotifyPropertyChangedInvocator]
        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
