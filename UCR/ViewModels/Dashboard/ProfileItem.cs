using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using HidWizards.UCR.Core.Managers;
using HidWizards.UCR.Core.Models;
using HidWizards.UCR.ViewModels.Presentation;

namespace HidWizards.UCR.ViewModels.Dashboard
{

    public sealed class ProfileInputGroupKey
    {
        public string IdentityKey { get; set; }
        public string Name { get; set; }
        public DeviceVisualDescriptor Visual { get; set; }

        public override bool Equals(object obj)
        {
            var other = obj as ProfileInputGroupKey;
            if (other == null) return false;
            return string.Equals(IdentityKey, other.IdentityKey, StringComparison.OrdinalIgnoreCase);
        }

        public override int GetHashCode()
        {
            return (IdentityKey ?? string.Empty).ToLowerInvariant().GetHashCode();
        }

        public override string ToString()
        {
            return Name ?? string.Empty;
        }
    }

    public class ProfileItem
    {
        public ProfileItem()
        {
            Items = new ObservableCollection<ProfileItem>();
            InputVisuals = new ObservableCollection<DeviceVisualDescriptor>();
            OutputVisuals = new ObservableCollection<DeviceVisualDescriptor>();
        }

        public string Title { get; set; }
        public Guid Id { get; set; }
        public Profile Profile { get; set; }
        public ObservableCollection<ProfileItem> Items { get; set; }
        public ObservableCollection<DeviceVisualDescriptor> InputVisuals { get; set; }
        public ObservableCollection<DeviceVisualDescriptor> OutputVisuals { get; set; }
        public int AdditionalInputCount { get; set; }
        public int AdditionalOutputCount { get; set; }
        public bool HasAdditionalInputs => AdditionalInputCount > 0;
        public bool HasAdditionalOutputs => AdditionalOutputCount > 0;
        public string AdditionalInputLabel => AdditionalInputCount > 0 ? "+" + AdditionalInputCount : string.Empty;
        public string AdditionalOutputLabel => AdditionalOutputCount > 0 ? "+" + AdditionalOutputCount : string.Empty;
        public string InputGroupName { get; set; }
        public DeviceVisualDescriptor InputGroupVisual { get; set; }
        public ProfileInputGroupKey InputGroup { get; set; }

        public void RefreshPresentation()
        {
            InputVisuals.Clear();
            OutputVisuals.Clear();
            PopulatePresentation(this, Profile);
        }

        public static ObservableCollection<ProfileItem> GetProfileTree(List<Profile> profiles)
        {
            var profileItems = new ObservableCollection<ProfileItem>();
            if (profiles == null) return profileItems;

            foreach (var profile in profiles)
            {
                var item = new ProfileItem
                {
                    Title = profile.Title,
                    Id = profile.Guid,
                    Items = GetProfileTree(profile.ChildProfiles),
                    Profile = profile
                };
                PopulatePresentation(item, profile);
                profileItems.Add(item);
            }

            return profileItems;
        }

        private static void PopulatePresentation(ProfileItem item, Profile profile)
        {
            var primaryInput = profile.GetPrimaryDeviceConfiguration(DeviceIoType.Input);
            var primaryOutput = profile.GetPrimaryDeviceConfiguration(DeviceIoType.Output);

            var inputs = profile.GetDeviceConfigurationList(DeviceIoType.Input)
                .Where(configuration => configuration != null)
                .OrderBy(configuration => primaryInput != null && configuration.Guid == primaryInput.Guid ? 0 : 1)
                .ToList();
            var outputs = profile.GetDeviceConfigurationList(DeviceIoType.Output)
                .Where(configuration => configuration != null)
                .ToList();

            // The profile browser is intentionally a strict three-column summary:
            // primary input | profile name | primary output. Additional devices belong in
            // the profile's device lists, not in the row summary.
            if (primaryInput != null)
            {
                item.InputVisuals.Add(DeviceVisualCatalog.Describe(primaryInput, profile, DeviceIoType.Input));
            }
            if (primaryOutput != null)
            {
                item.OutputVisuals.Add(DeviceVisualCatalog.Describe(primaryOutput, profile, DeviceIoType.Output));
            }

            item.AdditionalInputCount = Math.Max(0, inputs.Count - (primaryInput != null ? 1 : 0));
            item.AdditionalOutputCount = Math.Max(0, outputs.Count - (primaryOutput != null ? 1 : 0));

            if (inputs.Count == 0)
            {
                item.InputGroupName = "No input device";
                item.InputGroupVisual = DeviceVisualCatalog.Describe((Device)null, DeviceIoType.Input);
            }
            else if (inputs.Count == 1)
            {
                // Group headings identify the actual device, not a profile-configuration label.
                // Prefer the persistent friendly alias and keep the raw hardware title in the tooltip.
                item.InputGroupName = profile.Context?.DevicesManager?.GetDisplayTitle(inputs[0].Device)
                                      ?? inputs[0].Device?.DisplayTitle
                                      ?? inputs[0].Device?.Title
                                      ?? "Input device";
                item.InputGroupVisual = DeviceVisualCatalog.Describe(inputs[0], profile, DeviceIoType.Input);
            }
            else
            {
                item.InputGroupName = "Multiple input devices";
                item.InputGroupVisual = DeviceVisualCatalog.Describe(inputs[0], profile, DeviceIoType.Input);
            }

            item.InputGroup = new ProfileInputGroupKey
            {
                IdentityKey = BuildInputGroupIdentity(inputs),
                Name = item.InputGroupName,
                Visual = item.InputGroupVisual
            };
        }

        private static string BuildInputGroupIdentity(IList<DeviceConfiguration> inputs)
        {
            if (inputs == null || inputs.Count == 0) return "none";

            var identities = inputs
                .Select(configuration => BuildDeviceIdentity(configuration?.Device))
                .OrderBy(identity => identity, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return string.Join("||", identities);
        }

        private static string BuildDeviceIdentity(Device device)
        {
            if (device == null) return "unknown";

            var identity = DevicesManager.BuildAliasIdentity(device);
            if (identity != null)
            {
                return string.Join("|", new[]
                {
                    identity.ProviderName ?? string.Empty,
                    identity.IdentityKind.ToString(),
                    identity.IdentityValue ?? string.Empty,
                    identity.DeviceNumber.ToString()
                });
            }

            return string.Join("|", new[]
            {
                device.ProviderName ?? string.Empty,
                device.DeviceHandle ?? string.Empty,
                device.DeviceNumber.ToString(),
                device.HidPath ?? string.Empty
            });
        }
    }
}
