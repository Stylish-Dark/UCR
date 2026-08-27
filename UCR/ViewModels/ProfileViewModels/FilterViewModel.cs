using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using HidWizards.UCR.Core.Annotations;
using HidWizards.UCR.Core.Models;
using HidWizards.UCR.Core.Models.Subscription;
using MaterialDesignThemes.Wpf;

namespace HidWizards.UCR.ViewModels.ProfileViewModels
{
    public class FilterViewModel : INotifyPropertyChanged, IDisposable
    {

        public string Name => Filter.Name;
        public bool Negative => Filter.Negative;
        public Filter Filter { get; }
        public PackIconKind ChipIcon => GetFilterIcon();

        public bool IsEnabled => !_pluginViewModel.MappingViewModel.ProfileViewModel.Profile.IsActive();
        public double ChipOpacity => GetFilterState() ? 1.0 : 0.26;
        private readonly PluginViewModel _pluginViewModel;
        private FilterState _subscribedFilterState;

        public FilterViewModel(PluginViewModel pluginViewModel, Filter filter)
        {
            _pluginViewModel = pluginViewModel;
            Filter = filter;
            _pluginViewModel.MappingViewModel.ProfileViewModel.Profile.Context.ActiveProfileChangedEvent += ContextOnActiveProfileChangedEvent;

            SubscribeFilters();
        }

        private void SubscribeFilters()
        {
            if (_subscribedFilterState != null)
            {
                _subscribedFilterState.FilterStateChangedEvent -= OnFilterStateChanged;
                _subscribedFilterState = null;
            }

            var subscriptionState = GetSubscriptionState();
            if (subscriptionState != null && subscriptionState.IsActive && subscriptionState.ActiveProfile.Equals(_pluginViewModel.Plugin.Profile))
            {
                _subscribedFilterState = subscriptionState.FilterState;
                _subscribedFilterState.FilterStateChangedEvent += OnFilterStateChanged;
            }
        }

        private SubscriptionState GetSubscriptionState()
        {
            return _pluginViewModel.MappingViewModel.ProfileViewModel.Profile.Context.SubscriptionsManager.SubscriptionState;
        }

        private void OnFilterStateChanged(string filterName, bool value)
        {
            if (!string.Equals(filterName, Name, StringComparison.InvariantCultureIgnoreCase)) return;
            OnPropertyChanged(nameof(ChipIcon));
            OnPropertyChanged(nameof(ChipOpacity));
            OnPropertyChanged(nameof(Name));
        }

        private PackIconKind GetFilterIcon()
        {
            if (_pluginViewModel.Plugin.Profile.IsActive()) return GetFilterState() ? PackIconKind.Check : PackIconKind.Close;
            return Negative ? PackIconKind.Minus : PackIconKind.Plus;
        }

        private bool GetFilterState()
        {
            var state = GetSubscriptionState();
            if (state == null || string.IsNullOrWhiteSpace(Filter.Name)) return false;

            bool value;
            if (!state.FilterState.FilterRuntimeDictionary.TryGetValue(Filter.Name.ToLowerInvariant(), out value)) return false;
            return value ^ Negative;
        }

        public void RefreshName()
        {
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(ChipIcon));
            OnPropertyChanged(nameof(ChipOpacity));
        }

        private void ContextOnActiveProfileChangedEvent(Profile profile)
        {
            OnPropertyChanged(nameof(IsEnabled));
            SubscribeFilters();
            OnPropertyChanged(nameof(ChipIcon));
            OnPropertyChanged(nameof(ChipOpacity));
        }

        public void ToggleFilter()
        {
            _pluginViewModel.ToggleFilter(this);
            OnPropertyChanged(nameof(ChipIcon));
        }

        public void RemoveFilter()
        {
            _pluginViewModel.RemoveFilter(this);
        }

        public void Dispose()
        {
            if (_subscribedFilterState != null)
            {
                _subscribedFilterState.FilterStateChangedEvent -= OnFilterStateChanged;
                _subscribedFilterState = null;
            }
            _pluginViewModel.MappingViewModel.ProfileViewModel.Profile.Context.ActiveProfileChangedEvent -= ContextOnActiveProfileChangedEvent;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}