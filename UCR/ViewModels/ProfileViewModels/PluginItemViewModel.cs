using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using HidWizards.UCR.Core;
using HidWizards.UCR.Core.Annotations;
using HidWizards.UCR.Core.Models;

namespace HidWizards.UCR.ViewModels.ProfileViewModels
{
    public class PluginItemViewModel : INotifyPropertyChanged, IDisposable
    {
        public string Name => Plugin.PluginName;
        public string Description => Plugin.Description;
        public Visibility SeparatorVisibility => FirstElement ? Visibility.Collapsed : Visibility.Visible;
        public bool FirstElement { get; set; }
        public bool IsEnabled => !_profile.IsActive();

        public Plugin Plugin { get; }

        private readonly Profile _profile;
        private bool _disposed;

        public PluginItemViewModel(Profile profile, Plugin plugin)
        {
            Plugin = plugin;
            _profile = profile;
            profile.Context.ActiveProfileChangedEvent += ContextOnActiveProfileChangedEvent;
        }

        private void ContextOnActiveProfileChangedEvent(Profile profile)
        {
            OnPropertyChanged(nameof(IsEnabled));
        }


        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _profile.Context.ActiveProfileChangedEvent -= ContextOnActiveProfileChangedEvent;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}