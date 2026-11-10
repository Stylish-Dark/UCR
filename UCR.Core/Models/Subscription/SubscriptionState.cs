using System;
using System.Collections.Generic;
using System.Linq;

namespace HidWizards.UCR.Core.Models.Subscription
{
    public class SubscriptionState
    {
        public Guid StateGuid { get; }
        public IReadOnlyList<Profile> ActiveProfiles { get; }
        public Profile ActiveProfile => ActiveProfiles.LastOrDefault();
        public bool IsActive { get; set; }

        public List<DeviceConfigurationSubscription> OutputDeviceConfigurationSubscriptions { get; }
        public List<MappingSubscription> MappingSubscriptions { get; set; }
        public List<InputSubscription> SafetyInputSubscriptions { get; }
        public FilterState FilterState { get; set; }

        public SubscriptionState(Profile profile) : this(profile == null ? new List<Profile>() : new List<Profile> { profile })
        {
        }

        public SubscriptionState(IEnumerable<Profile> profiles)
        {
            StateGuid = Guid.NewGuid();
            ActiveProfiles = (profiles ?? Enumerable.Empty<Profile>())
                .Where(profile => profile != null)
                .GroupBy(profile => profile.Guid)
                .Select(group => group.First())
                .ToList()
                .AsReadOnly();
            OutputDeviceConfigurationSubscriptions = new List<DeviceConfigurationSubscription>();
            MappingSubscriptions = new List<MappingSubscription>();
            SafetyInputSubscriptions = new List<InputSubscription>();
            IsActive = false;
            FilterState = new FilterState();
        }

        public DeviceConfigurationSubscription AddOutputDeviceConfiguration(
            DeviceConfiguration deviceConfiguration, Guid runtimeScopeGuid)
        {
            if (deviceConfiguration == null) return null;

            // Configuration GUIDs were historically copied verbatim by UCR's profile-copy code.
            // They therefore cannot be treated as globally unique once several profiles run at once.
            // Scope output subscriptions to their owning profile runtime instead.
            var existing = OutputDeviceConfigurationSubscriptions.FirstOrDefault(subscription =>
                subscription.RuntimeScopeGuid == runtimeScopeGuid &&
                subscription.DeviceConfiguration.Guid == deviceConfiguration.Guid);
            if (existing != null) return existing;

            var created = new DeviceConfigurationSubscription(deviceConfiguration, runtimeScopeGuid);
            OutputDeviceConfigurationSubscriptions.Add(created);
            return created;
        }

        public void AddMappings(Profile profile, IEnumerable<Mapping> mappings, Guid runtimeScopeGuid,
            List<DeviceConfigurationSubscription> profileOutputDevices)
        {
            if (profile == null || mappings == null) return;
            var profileMappings = new List<MappingSubscription>();

            foreach (var profileMapping in mappings.Where(mapping => mapping != null))
            {
                profileMappings.Add(new MappingSubscription(profile, profileMapping, StateGuid, runtimeScopeGuid, profileOutputDevices));
            }

            OverrideEarlierMappingsInScope(profileMappings, runtimeScopeGuid);

            MappingSubscriptions.AddRange(profileMappings);
            MappingSubscriptions.AddRange(AddShadowMappings(profile, profileMappings, runtimeScopeGuid, profileOutputDevices));
        }

        private IEnumerable<MappingSubscription> AddShadowMappings(Profile profile,
            List<MappingSubscription> profileMappings, Guid runtimeScopeGuid,
            List<DeviceConfigurationSubscription> profileOutputDevices)
        {
            foreach (var mappingSubscription in profileMappings)
            {
                var mapping = mappingSubscription.Mapping;
                foreach (var index in mapping.GetPossibleShadowCloneNumbers(profile))
                {
                    yield return new MappingSubscription(profile, mapping.CreateShadowClone(index),
                        StateGuid, runtimeScopeGuid, profileOutputDevices, mapping);
                }
            }
        }

        private void OverrideEarlierMappingsInScope(IEnumerable<MappingSubscription> newMappings, Guid runtimeScopeGuid)
        {
            foreach (var profileMappingSubscription in newMappings)
            {
                foreach (var subscription in MappingSubscriptions)
                {
                    if (subscription.RuntimeScopeGuid != runtimeScopeGuid ||
                        subscription.Profile.Guid == profileMappingSubscription.Profile.Guid) continue;
                    if (string.Equals(profileMappingSubscription.SourceMapping.Title, subscription.SourceMapping.Title,
                        StringComparison.CurrentCultureIgnoreCase))
                    {
                        subscription.Overriden = true;
                    }
                }
            }
        }
    }
}
