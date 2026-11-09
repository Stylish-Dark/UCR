using System;
using System.Collections.Generic;
using HidWizards.UCR.Core.Models.Binding;

namespace HidWizards.UCR.Core.Models
{   
    public class CallbackMultiplexer
    {
        private DeviceBinding.ValueChanged _mappingUpdate;
        private readonly int _index;
        private readonly List<short> _cache;
        private readonly object _initialValueLock = new object();
        private volatile bool _hasLiveValue;
        private volatile bool _awaitingInitialValue;

        internal bool AwaitingInitialValue => _awaitingInitialValue;

        public CallbackMultiplexer(List<short> cache, int index, DeviceBinding.ValueChanged mappingUpdate)
        {
            _mappingUpdate = mappingUpdate;
            _index = index;
            _cache = cache;
        }

        internal void SeedInitialValue(short? value)
        {
            lock (_initialValueLock)
            {
                if (_hasLiveValue) return;
                if (value.HasValue) _cache[_index] = value.Value;
                _awaitingInitialValue = !value.HasValue;
            }
        }

        public void Update(short value)
        {
            if (_hasLiveValue)
            {
                _cache[_index] = value;
            }
            else
            {
                lock (_initialValueLock)
                {
                    _cache[_index] = value;
                    _awaitingInitialValue = false;
                    _hasLiveValue = true;
                }
            }
            _mappingUpdate(value);
        }

        ~CallbackMultiplexer()
        {
            _mappingUpdate = null;
        }
    }
}
