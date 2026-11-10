using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Serialization;
using HidWizards.UCR.Core.Utilities;

namespace HidWizards.UCR.Core.Models
{
    public class MappingGroup
    {
        private bool _enabled;

        [XmlAttribute]
        public Guid Guid { get; set; }

        [XmlAttribute]
        public string Title { get; set; }

        [XmlAttribute]
        [DefaultValue(true)]
        public bool Enabled
        {
            get => _enabled;
            set => _ = SetEnabledAsync(value);
        }

        internal Task<bool> SetEnabledAsync(bool enabled)
        {
            return ChangeEnabledAsync(Profile, enabled, () => _enabled, value => _enabled = value);
        }

        internal static async Task<bool> ChangeEnabledAsync(Profile profile, bool enabled, Func<bool> getEnabled,
            Action<bool> setEnabled)
        {
            var context = profile?.Context;
            var subscriptions = context?.SubscriptionsManager;
            if (subscriptions == null ||
                (!subscriptions.IsTransitionInProgress && !subscriptions.IsProfileLayerActive(profile.Guid)))
            {
                if (getEnabled() == enabled) return true;
                setEnabled(enabled);
                context?.ContextChanged();
                return true;
            }

            try
            {
                if (await subscriptions.ChangeMappingGroupEnabledAsync(profile, enabled, getEnabled, setEnabled))
                    return true;
                Logger.Warn("The mapping group change could not be applied: " + subscriptions.LastActivationError);
            }
            catch (Exception exception)
            {
                Logger.Error("Refreshing the active profile after a mapping-group change failed.", exception);
            }
            return false;
        }

        [XmlAttribute]
        [DefaultValue(false)]
        public bool BlockInputs { get; set; }

        public List<Mapping> Mappings { get; set; }

        [XmlIgnore]
        public Profile Profile { get; internal set; }

        public MappingGroup()
        {
            Guid = Guid.NewGuid();
            Title = "Group";
            _enabled = true;
            Mappings = new List<Mapping>();
        }

        internal MappingGroup(Profile profile, string title) : this()
        {
            Profile = profile;
            Title = string.IsNullOrWhiteSpace(title) ? "Group" : title.Trim();
        }

        public Mapping AddMapping(string title)
        {
            if (Profile == null) throw new InvalidOperationException("Mapping group is not attached to a profile.");
            var mapping = new Mapping(Profile, title);
            Mappings.Add(mapping);
            Profile.Context?.ContextChanged();
            return mapping;
        }

        public bool Rename(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return false;
            Title = title.Trim();
            Profile?.Context?.ContextChanged();
            return true;
        }

        internal void PostLoad(Context context, Profile profile)
        {
            Profile = profile;
            if (Guid == Guid.Empty) Guid = Guid.NewGuid();
            if (Mappings == null) Mappings = new List<Mapping>();
            foreach (var mapping in Mappings.Where(mapping => mapping != null)) mapping.PostLoad(context, profile);
        }
    }
}
