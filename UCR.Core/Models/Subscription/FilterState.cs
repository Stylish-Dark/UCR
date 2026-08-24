using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HidWizards.UCR.Core.Utilities;

namespace HidWizards.UCR.Core.Models.Subscription
{
    public class FilterState
    {
        public Dictionary<string, bool> FilterRuntimeDictionary { get; set; }

        public delegate void FilterStateChanged(string filterName, bool value);
        public event FilterStateChanged FilterStateChangedEvent;

        public FilterState()
        {
            FilterRuntimeDictionary = new Dictionary<string, bool>();
        }

        public void SetFilterState(string filterName, bool value)
        {
            if (string.IsNullOrWhiteSpace(filterName))
            {
                Logger.Warn("Ignored an attempt to write an unnamed filter state");
                return;
            }

            bool previousValue;
            if (!FilterRuntimeDictionary.TryGetValue(filterName, out previousValue))
            {
                Logger.Error("Ignored an attempt to write undefined filter state: " + filterName, null);
                return;
            }
            if (previousValue == value) return;

            FilterRuntimeDictionary[filterName] = value;
            FilterStateChangedEvent?.Invoke(filterName, value);
        }

        public void ToggleFilterState(string filterName)
        {
            if (string.IsNullOrWhiteSpace(filterName))
            {
                Logger.Warn("Ignored an attempt to toggle an unnamed filter state");
                return;
            }

            bool previousValue;
            if (!FilterRuntimeDictionary.TryGetValue(filterName, out previousValue))
            {
                Logger.Error("Ignored an attempt to toggle undefined filter state: " + filterName, null);
                return;
            }

            FilterRuntimeDictionary[filterName] = !previousValue;
            FilterStateChangedEvent?.Invoke(filterName, FilterRuntimeDictionary[filterName]);
        }
    }
}
