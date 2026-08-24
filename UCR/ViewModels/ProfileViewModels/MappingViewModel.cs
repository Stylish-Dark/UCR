using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using HidWizards.UCR.Core.Annotations;
using HidWizards.UCR.Core.Models;
using HidWizards.UCR.Views.Dialogs;
using MaterialDesignThemes.Wpf;

namespace HidWizards.UCR.ViewModels.ProfileViewModels
{
    public sealed class MappingHeaderToken
    {
        public string Text { get; }
        public string TypeLabel { get; }

        public MappingHeaderToken(string text, string typeLabel)
        {
            Text = text;
            TypeLabel = typeLabel;
        }
    }

    public class MappingViewModel : INotifyPropertyChanged
    {
        public string MappingTitle => Mapping.FullTitle;
        public ProfileViewModel ProfileViewModel { get; }
        public Mapping Mapping { get; set; }
        public ObservableCollection<PluginViewModel> Plugins { get; set; }
        public ObservableCollection<DeviceBindingViewModel> DeviceBindings { get; set; }
        public bool ButtonsEnabled => !ProfileViewModel.Profile.IsActive();
        public bool CanMoveUp => ButtonsEnabled && ProfileViewModel.MappingsList != null && ProfileViewModel.MappingsList.IndexOf(this) > 0;
        public bool CanMoveDown => ButtonsEnabled && ProfileViewModel.MappingsList != null &&
                                   ProfileViewModel.MappingsList.IndexOf(this) >= 0 &&
                                   ProfileViewModel.MappingsList.IndexOf(this) < ProfileViewModel.MappingsList.Count - 1;
        public string MappingRoute => Mapping != null && Mapping.Plugins.Count > 0 ? Mapping.Plugins[0].PluginName : "No plugin";
        public List<MappingHeaderToken> MappingRouteTokens => BuildMappingRouteTokens(MappingRoute);
        public bool HasFilters => Mapping != null && Mapping.Plugins != null &&
                                  Mapping.Plugins.Any(plugin => plugin.Filters != null && plugin.Filters.Count > 0);

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded == value) return;
                _isExpanded = value;
                OnPropertyChanged();
            }
        }

        public MappingViewModel(ProfileViewModel profileViewModel, Mapping mapping)
        {
            ProfileViewModel = profileViewModel;
            Mapping = mapping;
            IsExpanded = false;
            profileViewModel.Profile.Context.ActiveProfileChangedEvent += ContextOnActiveProfileChangedEvent;
            DeviceBindings = new ObservableCollection<DeviceBindingViewModel>();
            PopulateDeviceBindingsViewModels();
            PopulatePlugins(mapping);
        }

        private void ContextOnActiveProfileChangedEvent(Profile profile)
        {
            OnPropertyChanged(nameof(ButtonsEnabled));
            OnPropertyChanged(nameof(CanMoveUp));
            OnPropertyChanged(nameof(CanMoveDown));
        }

        public void RefreshPositionState()
        {
            OnPropertyChanged(nameof(CanMoveUp));
            OnPropertyChanged(nameof(CanMoveDown));
        }

        public void MoveUp()
        {
            ProfileViewModel.MoveMapping(this, -1);
        }

        public void MoveDown()
        {
            ProfileViewModel.MoveMapping(this, 1);
        }

        public void Remove()
        {
            ProfileViewModel.RemoveMapping(this);
        }

        public void AddPlugin(Plugin plugin)
        {
            var newPlugin = ProfileViewModel.Profile.Context.PluginManager.GetNewPlugin(plugin);
            if (!Mapping.AddPlugin(newPlugin)) return;

            Plugins.Add(new PluginViewModel(this, newPlugin));
            RefreshHeaderState();
            if (Plugins.Count != 1) return;
            
            PopulateDeviceBindingsViewModels();
        }

        private void PopulateDeviceBindingsViewModels()
        {
            if (Mapping.Plugins.Count == 0) return;

            var plugin = Mapping.Plugins[0];
            for (var i = 0; i < plugin.InputCategories.Count; i++)
            {
                DeviceBindings.Add(new DeviceBindingViewModel(Mapping.DeviceBindings[i])
                {
                    DeviceBindingName = plugin.InputCategories[i].Name,
                    DeviceBindingCategory = plugin.InputCategories[i].Category
                });
            }
        }

        public void RemovePlugin(PluginViewModel pluginViewModel)
        {
            if (!Mapping.RemovePlugin(pluginViewModel.Plugin)) return;

            Plugins.Remove(pluginViewModel);
            RefreshHeaderState();
            ProfileViewModel.RefreshFilterNames();
            if (Plugins.Count == 0) DeviceBindings.Clear();
        }

        private void PopulatePlugins(Mapping mapping)
        {
            Plugins = new ObservableCollection<PluginViewModel>();
            foreach (var mappingPlugin in mapping.Plugins)
            {
                Plugins.Add(new PluginViewModel(this, mappingPlugin));
            }
        }

        public void RefreshFilterIndicator()
        {
            OnPropertyChanged(nameof(HasFilters));
        }

        private void RefreshHeaderState()
        {
            OnPropertyChanged(nameof(MappingRoute));
            OnPropertyChanged(nameof(MappingRouteTokens));
            OnPropertyChanged(nameof(HasFilters));
        }

        private static List<MappingHeaderToken> BuildMappingRouteTokens(string route)
        {
            var result = new List<MappingHeaderToken>();
            if (string.IsNullOrEmpty(route)) return result;

            var parts = Regex.Split(route, @"\b(Button|Buttons|Axis|Axes|Filter|Event|Events|Delta|Deltas|Value|Values|Multiple|None)\b", RegexOptions.IgnoreCase);
            foreach (var part in parts)
            {
                if (string.IsNullOrEmpty(part)) continue;
                result.Add(new MappingHeaderToken(part, IsMappingTypeWord(part) ? part : null));
            }
            return result;
        }

        private static bool IsMappingTypeWord(string value)
        {
            switch (value.ToLowerInvariant())
            {
                case "button":
                case "buttons":
                case "axis":
                case "axes":
                case "filter":
                case "event":
                case "events":
                case "delta":
                case "deltas":
                case "value":
                case "values":
                case "multiple":
                case "none":
                    return true;
                default:
                    return false;
            }
        }

        public async void Rename()
        {
            var dialog = new StringDialog("Rename mapping", "Mapping name", Mapping.Title);
            var result = (bool?)await DialogHost.Show(dialog, ProfileViewModel.ProfileDialogIdentifier);
            if (result == null || !result.Value) return;

            Mapping.Rename(dialog.Value);
            OnPropertyChanged(nameof(MappingTitle));
        }

        public async void AddPlugin()
        {
            var dialog = new AddMappingPluginDialog(this);
            var result = (AddMappingPluginDialogViewModel)await DialogHost.Show(dialog, ProfileViewModel.ProfileDialogIdentifier);
            if (result?.SelectedPlugin == null) return;

            AddPlugin(result.SelectedPlugin.Plugin);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
