using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using HidWizards.UCR.Core;
using HidWizards.UCR.Core.Models;
using HidWizards.UCR.Core.Utilities;
using HidWizards.UCR.ViewModels.Dialogs;
using HidWizards.UCR.ViewModels.ProfileViewModels;
using HidWizards.UCR.Views.Dialogs;
using MaterialDesignThemes.Wpf;

namespace HidWizards.UCR.Views.ProfileViews
{
    public partial class ProfileWindow : Window
    {
        public Guid ProfileGuid => Profile.Guid;
        private Context Context { get; }
        private Profile Profile { get; }
        private ProfileViewModel ProfileViewModel { get; }
        private DispatcherTimer DispatcherTimer { get; set; }
        private List<DeviceBindingViewModel> DeviceBindingViewModels { get; set; }
        private Point? _mappingDragStart;
        private MappingViewModel _mappingDragSource;
        private int _mappingDragOriginalIndex = -1;

        public ProfileWindow(Context context, Profile profile)
        {
            Context = context;
            Profile = profile;
            ProfileViewModel = new ProfileViewModel(profile);
            InitializeComponent();
            Title = "Edit " + profile.Title;
            DataContext = ProfileViewModel;
            context.ActiveProfileChangedEvent += ContextOnActiveProfileChangedEvent;
            StartGuiTimer();
        }

        private void Save_OnExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            Context.SaveContext();
        }

        private void Save_OnCanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = Context.IsNotSaved;
        }

        #region GUI

        private void StartGuiTimer()
        {
            if (!Profile.IsActive()) return;
            DispatcherTimer = new DispatcherTimer(DispatcherPriority.Render);
            DispatcherTimer.Interval = TimeSpan.FromMilliseconds(15);
            DispatcherTimer.Tick += DispatcherTimerOnTick;

            DeviceBindingViewModels = new List<DeviceBindingViewModel>();
            foreach (var mappingViewModel in ProfileViewModel.MappingsList)
            {
                DeviceBindingViewModels.AddRange(mappingViewModel.DeviceBindings);
                foreach (var pluginViewModel in mappingViewModel.Plugins)
                {
                    DeviceBindingViewModels.AddRange(pluginViewModel.DeviceBindings);
                }
            }

            DispatcherTimer.Start();
        }

        private void StopGuiTimer()
        {
            DispatcherTimer?.Stop();
            DispatcherTimer = null;
        }

        private void ContextOnActiveProfileChangedEvent(Profile profile)
        {
            if (profile == null)
            {
                StopGuiTimer();
                return;
            }

            if (profile.Guid != ProfileGuid) return;
            StartGuiTimer();
        }

        private void DispatcherTimerOnTick(object sender, EventArgs e)
        {
            if (!IsActive) return;
            DeviceBindingViewModels.ForEach(d => d.CurrentValueChanged());
        }

        #endregion

        #region Profile

        private void ActivateProfile(object sender, RoutedEventArgs e)
        {
            if (!Profile.ActivateProfile())
            {
                HidWizards.UCR.Utilities.DarkMessageBox.Show("The Profile could not be activated, see the log for more details", "Profile failed to activate!", MessageBoxButton.OK, MessageBoxImage.Exclamation);
            }
        }

        private void DeactivateProfile(object sender, RoutedEventArgs e)
        {
            Profile.Deactivate();
        }

        #endregion

        private async void BatchDevices_OnClick(object sender, RoutedEventArgs e)
        {
            var dialog = new BatchDeviceChangeDialog(ProfileViewModel);
            var result = (BatchDeviceChangeDialogViewModel)await DialogHost.Show(dialog, ProfileViewModel.ProfileDialogIdentifier);
            if (result?.SelectedSource == null || result.SelectedTarget == null) return;

            var change = ProfileViewModel.BatchChangeDevice(result.SelectedSource, result.SelectedTarget);
            Logger.Info("Profile device replacement: " + result.SelectedSource.DisplayTitle + " -> " + result.SelectedTarget.DisplayTitle +
                        "; changed=" + change.Changed + "; incompatible-cleared=" + change.ClearedAsIncompatible +
                        "; unknown-preserved=" + change.PreservedUnknown);

            var message = change.Changed + " binding" + (change.Changed == 1 ? "" : "s") + " changed.";
            if (change.ClearedAsIncompatible > 0)
            {
                message += " " + change.ClearedAsIncompatible + " incompatible binding" +
                           (change.ClearedAsIncompatible == 1 ? " was" : "s were") + " cleared safely.";
            }
            HidWizards.UCR.Utilities.DarkMessageBox.Show(message, "Replace device", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void RenameMappings_OnClick(object sender, RoutedEventArgs e)
        {
            var dialog = new RenameMappingsDialog(ProfileViewModel);
            var result = (RenameMappingsDialogViewModel)await DialogHost.Show(dialog, ProfileViewModel.ProfileDialogIdentifier);
            if (result == null) return;

            ProfileViewModel.ApplyMappingNames(result.Items);
            Logger.Info("Bulk mapping rename completed for profile: " + Profile.Title);
        }

        private void ContextMenuButton_OnClick(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button?.ContextMenu == null) return;

            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            button.ContextMenu.IsOpen = true;
            e.Handled = true;
        }

        private void CollapseAllMappings_OnClick(object sender, RoutedEventArgs e)
        {
            foreach (var mapping in ProfileViewModel.MappingsList) mapping.IsExpanded = false;
        }

        private void ExpandAllMappings_OnClick(object sender, RoutedEventArgs e)
        {
            foreach (var mapping in ProfileViewModel.MappingsList) mapping.IsExpanded = true;
        }

        private void MappingListView_OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (Profile.IsActive()) return;

            var container = FindVisualAncestor<ListViewItem>(e.OriginalSource as DependencyObject);
            var mapping = container?.DataContext as MappingViewModel;
            if (mapping == null || mapping.IsExpanded) return;

            _mappingDragStart = e.GetPosition(MappingListView);
            _mappingDragSource = mapping;
            _mappingDragOriginalIndex = ProfileViewModel.MappingsList.IndexOf(mapping);
        }

        private void MappingListView_OnPreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            _mappingDragStart = null;
            _mappingDragSource = null;
            _mappingDragOriginalIndex = -1;
        }

        private void MappingListView_OnPreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_mappingDragStart.HasValue || _mappingDragSource == null || e.RightButton != MouseButtonState.Pressed) return;

            var position = e.GetPosition(MappingListView);
            if (Math.Abs(position.X - _mappingDragStart.Value.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(position.Y - _mappingDragStart.Value.Y) < SystemParameters.MinimumVerticalDragDistance) return;

            var container = MappingListView.ItemContainerGenerator.ContainerFromItem(_mappingDragSource) as ListViewItem;
            if (container == null) return;

            var draggedMapping = _mappingDragSource;
            var originalIndex = _mappingDragOriginalIndex;
            try
            {
                Logger.Info("Reordering mapping by right-drag: " + draggedMapping.MappingTitle);
                var result = DragDrop.DoDragDrop(MappingListView, draggedMapping, DragDropEffects.Move);
                if (result != DragDropEffects.Move && originalIndex >= 0 &&
                    ProfileViewModel.MappingsList.Contains(draggedMapping))
                {
                    // Esc or dropping outside the mapping list cancels the operation cleanly.
                    ProfileViewModel.MoveMappingTo(draggedMapping, originalIndex);
                    MappingListView.ScrollIntoView(draggedMapping);
                    Logger.Info("Mapping reorder cancelled; original position restored: " + draggedMapping.MappingTitle);
                }
            }
            finally
            {
                _mappingDragStart = null;
                _mappingDragSource = null;
                _mappingDragOriginalIndex = -1;
            }

            e.Handled = true;
        }

        private void MappingListView_OnDragOver(object sender, DragEventArgs e)
        {
            var source = e.Data.GetData(typeof(MappingViewModel)) as MappingViewModel;
            if (source == null || source.IsExpanded || Profile.IsActive())
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            e.Effects = DragDropEffects.Move;
            AutoScrollMappingList(e.GetPosition(MappingListView));
            ReorderMappingAtPointer(source, e);
            e.Handled = true;
        }

        private void MappingListView_OnDrop(object sender, DragEventArgs e)
        {
            var source = e.Data.GetData(typeof(MappingViewModel)) as MappingViewModel;
            if (source == null || Profile.IsActive()) return;

            // DragOver performs the move continuously so the real card occupies its prospective
            // slot while dragging. Drop only makes one last position check for very fast releases.
            ReorderMappingAtPointer(source, e);
            MappingListView.ScrollIntoView(source);
            Logger.Info("Mapping reorder finished: " + source.MappingTitle + " at position " +
                        (ProfileViewModel.MappingsList.IndexOf(source) + 1));
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }

        private void ReorderMappingAtPointer(MappingViewModel source, DragEventArgs e)
        {
            var sourceIndex = ProfileViewModel.MappingsList.IndexOf(source);
            if (sourceIndex < 0) return;

            var targetContainer = FindVisualAncestor<ListViewItem>(e.OriginalSource as DependencyObject);
            if (targetContainer == null)
            {
                var pointer = e.GetPosition(MappingListView);
                if (pointer.Y >= MappingListView.ActualHeight - 8 && sourceIndex < ProfileViewModel.MappingsList.Count - 1)
                {
                    MoveMappingLive(source, ProfileViewModel.MappingsList.Count - 1);
                }
                return;
            }

            var target = targetContainer.DataContext as MappingViewModel;
            if (target == null || ReferenceEquals(target, source)) return;

            var targetIndex = ProfileViewModel.MappingsList.IndexOf(target);
            if (targetIndex < 0) return;

            var point = e.GetPosition(targetContainer);
            var crossedMidpoint = point.Y >= targetContainer.ActualHeight / 2.0;

            // Crossing the midpoint of the neighbouring card is the commitment point. Moving the
            // ObservableCollection here causes WPF to move the real card immediately and the other
            // cards naturally shuffle around it; no detached/transparent drag ghost is involved.
            int finalIndex;
            if (sourceIndex < targetIndex)
            {
                if (!crossedMidpoint) return;
                finalIndex = targetIndex;
            }
            else
            {
                if (crossedMidpoint) return;
                finalIndex = targetIndex;
            }

            MoveMappingLive(source, finalIndex);
        }

        private void MoveMappingLive(MappingViewModel source, int finalIndex)
        {
            var sourceIndex = ProfileViewModel.MappingsList.IndexOf(source);
            if (sourceIndex < 0) return;

            finalIndex = Math.Max(0, Math.Min(ProfileViewModel.MappingsList.Count - 1, finalIndex));
            if (sourceIndex == finalIndex) return;

            if (ProfileViewModel.MoveMappingTo(source, finalIndex))
            {
                MappingListView.ScrollIntoView(source);
            }
        }

        private void AutoScrollMappingList(Point pointer)
        {
            var scrollViewer = FindVisualChild<ScrollViewer>(MappingListView);
            if (scrollViewer == null) return;

            const double edgeZone = 36.0;
            const double step = 28.0;
            if (pointer.Y < edgeZone)
            {
                scrollViewer.ScrollToVerticalOffset(Math.Max(0, scrollViewer.VerticalOffset - step));
            }
            else if (pointer.Y > MappingListView.ActualHeight - edgeZone)
            {
                scrollViewer.ScrollToVerticalOffset(Math.Min(scrollViewer.ScrollableHeight, scrollViewer.VerticalOffset + step));
            }
        }

        private static T FindVisualChild<T>(DependencyObject source) where T : DependencyObject
        {
            if (source == null) return null;
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(source); index++)
            {
                var child = VisualTreeHelper.GetChild(source, index);
                var match = child as T;
                if (match != null) return match;
                var nested = FindVisualChild<T>(child);
                if (nested != null) return nested;
            }
            return null;
        }

        private static T FindVisualAncestor<T>(DependencyObject source) where T : DependencyObject
        {
            while (source != null)
            {
                var match = source as T;
                if (match != null) return match;
                source = VisualTreeHelper.GetParent(source);
            }
            return null;
        }

        private void AddMapping_OnClick(object sender, RoutedEventArgs e)
        {
            var selectedRoute = ProfileViewModel.PluginToolbox.SelectedRoute;
            if (selectedRoute == null || selectedRoute.PluginItem == null) return;

            var mappingViewModel = ProfileViewModel.AddMapping(ProfileViewModel.GetNextMappingTitle());
            mappingViewModel.AddPlugin(selectedRoute.PluginItem.Plugin);
            mappingViewModel.IsExpanded = true;
            MappingListView.ScrollIntoView(mappingViewModel);
        }
    }
}
