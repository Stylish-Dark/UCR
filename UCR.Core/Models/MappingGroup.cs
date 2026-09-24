using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Xml.Serialization;

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
            set
            {
                if (_enabled == value) return;
                _enabled = value;
                var context = Profile?.Context;
                context?.ContextChanged();

                // Mapping groups are runtime switches, not just editor metadata. If their profile is
                // already running, rebuild the composite subscription state without refreshing devices
                // so a dashboard toggle takes effect immediately and does not disturb unrelated devices.
                if (Profile != null && context?.SubscriptionsManager != null &&
                    context.SubscriptionsManager.IsProfileActive(Profile.Guid))
                {
                    context.SubscriptionsManager.RefreshActiveProfile(Profile);
                }
            }
        }

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
