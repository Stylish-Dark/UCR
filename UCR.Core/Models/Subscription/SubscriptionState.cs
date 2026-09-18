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

        private List<MappingSubscription> AddShadowMappings(Profile profile,
            List<MappingSubscription> profileMappings, Guid runtimeScopeGuid,
            List<DeviceConfigurationSubscription> profileOutputDevices)
        {
            var result = new List<MappingSubscription>();

            foreach (var mappingSubscription in profileMappings)
            {
                var shadowClones = mappingSubscription.Mapping.PossibleShadowClones;
                if (shadowClones == 0) continue;
                result.AddRange(CloneMappingSubscription(profile, mappingSubscription, runtimeScopeGuid,
                    profileOutputDevices, shadowClones));
            }

            return result;
        }

        private List<MappingSubscription> CloneMappingSubscription(Profile profile,
            MappingSubscription mappingSubscription, Guid runtimeScopeGuid,
            List<DeviceConfigurationSubscription> profileOutputDevices, int shadowClones)
        {
            var result = new List<MappingSubscription>();

            for (var i = 0; i < shadowClones; i++)
            {
                result.Add(new MappingSubscription(profile, mappingSubscription.Mapping.CreateShadowClone(i),
                    StateGuid, runtimeScopeGuid, profileOutputDevices));
            }

            return result;
        }

        private void OverrideEarlierMappingsInScope(IEnumerable<MappingSubscription> newMappings, Guid runtimeScopeGuid)
        {
            foreach (var profileMappingSubscription in newMappings)
            {
                foreach (var subscription in MappingSubscriptions)
                {
                    if (subscription.RuntimeScopeGuid != runtimeScopeGuid) continue;
                    if (string.Equals(profileMappingSubscription.Mapping.Title, subscription.Mapping.Title,
                        StringComparison.CurrentCultureIgnoreCase))
                    {
                        subscription.Overriden = true;
                    }
                }
            }
        }
    }
}
