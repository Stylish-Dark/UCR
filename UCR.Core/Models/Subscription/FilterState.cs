using System;
using System.Collections.Generic;
using System.Threading;
using HidWizards.UCR.Core.Utilities;

namespace HidWizards.UCR.Core.Models.Subscription
{
    public class FilterState
    {
        private readonly object _gate = new object();
        private readonly Dictionary<string, bool> _values = new Dictionary<string, bool>();
        private readonly Dictionary<string, bool> _deliveredValues = new Dictionary<string, bool>();
        [ThreadStatic]
        private static FilterState _deliveringState;
        private readonly Queue<KeyValuePair<string, bool>> _pendingChanges = new Queue<KeyValuePair<string, bool>>();
        private bool _dispatching;

        public delegate void FilterStateChanged(string filterName, bool value);
        public event FilterStateChanged FilterStateChangedEvent;

        internal void Define(string filterName)
        {
            lock (_gate)
            {
                if (_values.ContainsKey(filterName)) return;
                _values.Add(filterName, false);
                _deliveredValues.Add(filterName, false);
            }
        }

        public bool TryGetValue(string filterName, out bool value)
        {
            lock (_gate) return _values.TryGetValue(filterName, out value);
        }

        internal bool TryGetRoutingValue(string filterName, out bool value)
        {
            // Replaying a transition uses the full filter state at that point in the
            // queue. Direct input and UI reads still see the latest written state.
            lock (_gate)
            {
                var values = ReferenceEquals(_deliveringState, this) ? _deliveredValues : _values;
                return values.TryGetValue(filterName, out value);
            }
        }

        public void SetFilterState(string filterName, bool value)
        {
            ChangeState(filterName, value);
        }

        public void ToggleFilterState(string filterName)
        {
            ChangeState(filterName, null);
        }

        private void ChangeState(string filterName, bool? requestedValue)
        {
            if (string.IsNullOrWhiteSpace(filterName))
            {
                Logger.Warn("Ignored an attempt to change an unnamed filter state");
                return;
            }

            lock (_gate)
            {
                if (!_values.TryGetValue(filterName, out var previousValue))
                {
                    Logger.Error("Ignored an attempt to change undefined filter state: " + filterName, null);
                    return;
                }

                var value = requestedValue ?? !previousValue;
                if (value == previousValue) return;
                _values[filterName] = value;
                if (!_dispatching && FilterStateChangedEvent == null)
                {
                    _deliveredValues[filterName] = value;
                    return;
                }
                _pendingChanges.Enqueue(new KeyValuePair<string, bool>(filterName, value));
                if (_dispatching) return;
                _dispatching = true;
            }

            // Input providers call mappings synchronously. A filter writer must not
            // take another mapping's lock from inside that provider's callback.
            ThreadPool.QueueUserWorkItem(_ => DispatchChanges());
        }

        private void DispatchChanges()
        {
            while (true)
            {
                KeyValuePair<string, bool> change;
                lock (_gate)
                {
                    if (_pendingChanges.Count == 0)
                    {
                        _dispatching = false;
                        return;
                    }
                    change = _pendingChanges.Dequeue();
                    _deliveredValues[change.Key] = change.Value;
                }

                var observers = FilterStateChangedEvent?.GetInvocationList();
                if (observers == null) continue;
                var previousState = _deliveringState;
                _deliveringState = this;
                try
                {
                    foreach (FilterStateChanged observer in observers)
                    {
                        try
                        {
                            observer(change.Key, change.Value);
                        }
                        catch (Exception exception)
                        {
                            Logger.Error("A filter-state observer failed: " + change.Key, exception);
                        }
                    }
                }
                finally
                {
                    _deliveringState = previousState;
                }
            }
        }
    }
}
