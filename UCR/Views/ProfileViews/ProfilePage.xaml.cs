using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using HidWizards.UCR.Core;
using HidWizards.UCR.Core.Models;
using HidWizards.UCR.Core.Utilities;
using HidWizards.UCR.Utilities;
using HidWizards.UCR.ViewModels.Dashboard;
using HidWizards.UCR.ViewModels.Dialogs;
using HidWizards.UCR.ViewModels.ProfileViewModels;
using HidWizards.UCR.Views.Dialogs;
using MaterialDesignThemes.Wpf;

namespace HidWizards.UCR.Views.ProfileViews
{
    public partial class ProfilePage : UserControl, IDisposable
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
        private readonly Dictionary<MappingViewModel, MappingDragSlot> _mappingDragSlots =
            new Dictionary<MappingViewModel, MappingDragSlot>();
        private ListViewItem _mappingDragContainer;
        private ScrollViewer _mappingDragScrollViewer;
        private double _mappingDragInitialScrollOffset;
        private double _mappingDragGrabOffsetY;
        private double _mappingDragSourceHeight;
        private int _mappingDragTargetIndex = -1;
        private bool _mappingDragActive;
        private bool _mappingDragEnding;

        public ProfilePage(Context context, Profile profile)
        {
            Context = context;
            Profile = profile;
            ProfileViewModel = new ProfileViewModel(profile);
            InitializeComponent();
            PageTitle.Text = "Mappings — " + profile.Title;
            DataContext = ProfileViewModel;
            context.ActiveProfileChangedEvent += ContextOnActiveProfileChangedEvent;
            StartGuiTimer();
        }

        public event EventHandler BackRequested;
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_mappingDragActive) EndMappingDrag(false);
            StopGuiTimer();
            Context.ActiveProfileChangedEvent -= ContextOnActiveProfileChangedEvent;
            ProfileViewModel.Dispose();
            Logger.Debug("Profile page released: " + Profile.Title + " (" + Profile.Guid + ")");
        }

        private void Back_OnClick(object sender, RoutedEventArgs e)
        {
            BackRequested?.Invoke(this, EventArgs.Empty);
        }

        private void Save_OnExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            Context.SaveContext();
        }

        private void Save_OnCanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = Context.IsNotSaved;
        }

        private void ProfileWindow_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control) return;
            var scale = AppearanceManager.AdjustUiScale(e.Delta);
            Logger.Info("UI scale changed to " + Math.Round(scale * 100) + "%");
            e.Handled = true;
        }

        private void ProfileDevicesScrollViewer_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control) return;
            var viewer = sender as ScrollViewer;
            if (viewer == null || viewer.ScrollableHeight <= 0) return;

            var step = Math.Max(24.0, Math.Min(60.0, Math.Abs(e.Delta) / 2.5));
            viewer.ScrollToVerticalOffset(viewer.VerticalOffset + (e.Delta < 0 ? step : -step));
            e.Handled = true;
        }

        #region GUI

        private void StartGuiTimer()
        {
            if (!Profile.IsActive() || DispatcherTimer != null) return;
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
            if (DispatcherTimer != null)
            {
                DispatcherTimer.Stop();
                DispatcherTimer.Tick -= DispatcherTimerOnTick;
                DispatcherTimer = null;
            }
            DeviceBindingViewModels?.Clear();
            DeviceBindingViewModels = null;
        }

        private void ContextOnActiveProfileChangedEvent(Profile profile)
        {
            if (profile == null || profile.Guid != ProfileGuid)
            {
                StopGuiTimer();
                return;
            }

            StartGuiTimer();
        }

        private void DispatcherTimerOnTick(object sender, EventArgs e)
        {
            if (!IsVisible) return;
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

        private void AddProfileInputDevice_OnClick(object sender, RoutedEventArgs e)
        {
            ProfileViewModel.InputDeviceControlViewModel?.AddDevices();
        }

        private void RemoveProfileInputDevice_OnClick(object sender, RoutedEventArgs e)
        {
            var viewModel = ProfileViewModel.InputDeviceControlViewModel;
            viewModel?.RemoveDevice(viewModel.SelectedDeviceConfiguration);
        }

        private void ManageProfileInputDevice_OnClick(object sender, RoutedEventArgs e)
        {
            ProfileViewModel.InputDeviceControlViewModel?.ManageDeviceConfiguration();
        }

        private async void DetectProfileInputDevice_OnClick(object sender, RoutedEventArgs e)
        {
            Keyboard.ClearFocus();
            var item = await ProfileViewModel.InputDeviceControlViewModel.DetectAndAddInputDeviceAsync();
            if (item == null) return;
            InputProfileDeviceList.UpdateLayout();
            InputProfileDeviceList.ScrollIntoView(item);
        }

        private void AddProfileOutputDevice_OnClick(object sender, RoutedEventArgs e)
        {
            ProfileViewModel.OutputDeviceControlViewModel?.AddDevices();
        }

        private void RemoveProfileOutputDevice_OnClick(object sender, RoutedEventArgs e)
        {
            var viewModel = ProfileViewModel.OutputDeviceControlViewModel;
            viewModel?.RemoveDevice(viewModel.SelectedDeviceConfiguration);
        }

        private void ManageProfileOutputDevice_OnClick(object sender, RoutedEventArgs e)
        {
            ProfileViewModel.OutputDeviceControlViewModel?.ManageDeviceConfiguration();
        }

        private void PrimaryInputDevice_OnClick(object sender, RoutedEventArgs e)
        {
            var item = (sender as FrameworkElement)?.DataContext as DeviceItem;
            ProfileViewModel.InputDeviceControlViewModel?.SetPrimaryDevice(item);
            e.Handled = true;
        }

        private void PrimaryOutputDevice_OnClick(object sender, RoutedEventArgs e)
        {
            var item = (sender as FrameworkElement)?.DataContext as DeviceItem;
            ProfileViewModel.OutputDeviceControlViewModel?.SetPrimaryDevice(item);
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
            if (_mappingDragActive)
            {
                EndMappingDrag(true);
                e.Handled = true;
                return;
            }

            ResetPendingMappingDrag();
        }

        private void MappingListView_OnPreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_mappingDragActive)
            {
                if (e.RightButton != MouseButtonState.Pressed)
                {
                    EndMappingDrag(true);
                    return;
                }

                var pointer = e.GetPosition(MappingListView);
                AutoScrollMappingList(pointer);
                UpdateMappingDrag(pointer);
                e.Handled = true;
                return;
            }

            if (!_mappingDragStart.HasValue || _mappingDragSource == null || e.RightButton != MouseButtonState.Pressed) return;

            var position = e.GetPosition(MappingListView);
            if (Math.Abs(position.X - _mappingDragStart.Value.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(position.Y - _mappingDragStart.Value.Y) < SystemParameters.MinimumVerticalDragDistance) return;

            if (BeginMappingDrag(position))
            {
                e.Handled = true;
            }
        }

        private bool BeginMappingDrag(Point pointer)
        {
            var source = _mappingDragSource;
            if (source == null || source.IsExpanded || Profile.IsActive()) return false;

            MappingListView.UpdateLayout();
            var sourceContainer = MappingListView.ItemContainerGenerator.ContainerFromItem(source) as ListViewItem;
            if (sourceContainer == null || sourceContainer.ActualWidth <= 0 || sourceContainer.ActualHeight <= 0) return false;

            _mappingDragSlots.Clear();
            _mappingDragScrollViewer = FindVisualChild<ScrollViewer>(MappingListView);
            _mappingDragInitialScrollOffset = _mappingDragScrollViewer?.VerticalOffset ?? 0.0;

            foreach (var mapping in ProfileViewModel.MappingsList)
            {
                var container = MappingListView.ItemContainerGenerator.ContainerFromItem(mapping) as ListViewItem;
                if (container == null || container.ActualHeight <= 0)
                {
                    RestoreMappingDragVisuals();
                    Logger.Warn("Unable to prepare mapping reorder because a mapping card container is unavailable.");
                    return false;
                }

                var top = container.TranslatePoint(new Point(0, 0), MappingListView).Y;
                var slot = new MappingDragSlot
                {
                    Mapping = mapping,
                    Container = container,
                    Top = top,
                    Height = container.ActualHeight,
                    OriginalRenderTransform = container.RenderTransform,
                    OriginalZIndex = Panel.GetZIndex(container),
                    Translate = new TranslateTransform()
                };

                container.RenderTransform = slot.Translate;
                _mappingDragSlots[mapping] = slot;
            }

            MappingDragSlot sourceSlot;
            if (!_mappingDragSlots.TryGetValue(source, out sourceSlot))
            {
                RestoreMappingDragVisuals();
                return false;
            }

            _mappingDragContainer = sourceSlot.Container;
            _mappingDragGrabOffsetY = pointer.Y - sourceSlot.Top;
            _mappingDragSourceHeight = sourceSlot.Height;
            _mappingDragTargetIndex = _mappingDragOriginalIndex;

            try
            {
                if (!MappingListView.CaptureMouse())
                {
                    RestoreMappingDragVisuals();
                    Logger.Warn("Unable to capture mouse for live mapping reorder: " + source.MappingTitle);
                    return false;
                }

                Panel.SetZIndex(_mappingDragContainer, 1000);
                source.IsDragging = true;
                _mappingDragActive = true;
                Mouse.OverrideCursor = Cursors.SizeAll;
                UpdateMappingDrag(pointer);
                Logger.Info("Started direct mapping reorder: " + source.MappingTitle);
                return true;
            }
            catch (Exception exception)
            {
                if (Mouse.Captured == MappingListView) MappingListView.ReleaseMouseCapture();
                source.IsDragging = false;
                _mappingDragActive = false;
                Mouse.OverrideCursor = null;
                RestoreMappingDragVisuals();
                ResetPendingMappingDrag();
                Logger.Error("Unable to start direct mapping reorder", exception);
                return false;
            }
        }

        private void UpdateMappingDrag(Point pointer)
        {
            var source = _mappingDragSource;
            if (source == null || !_mappingDragActive) return;

            MappingDragSlot sourceSlot;
            if (!_mappingDragSlots.TryGetValue(source, out sourceSlot)) return;

            var scrollDelta = CurrentMappingScrollOffset() - _mappingDragInitialScrollOffset;
            var sourceLayoutTop = sourceSlot.Top - scrollDelta;
            var desiredTop = pointer.Y - _mappingDragGrabOffsetY;

            // Keep the actual card inside the visible mapping viewport while edge scrolling moves
            // the list beneath it. Horizontally it remains locked to the card column.
            var maximumTop = Math.Max(0.0, MappingListView.ActualHeight - _mappingDragSourceHeight);
            desiredTop = Math.Max(0.0, Math.Min(maximumTop, desiredTop));

            sourceSlot.Translate.BeginAnimation(TranslateTransform.YProperty, null);
            sourceSlot.Translate.Y = desiredTop - sourceLayoutTop;

            var draggedCentre = desiredTop + (_mappingDragSourceHeight / 2.0);
            var desiredIndex = 0;
            foreach (var mapping in ProfileViewModel.MappingsList)
            {
                if (ReferenceEquals(mapping, source)) continue;

                MappingDragSlot slot;
                if (!_mappingDragSlots.TryGetValue(mapping, out slot)) continue;

                var midpoint = slot.Top - scrollDelta + (slot.Height / 2.0);
                if (draggedCentre >= midpoint)
                {
                    desiredIndex++;
                    continue;
                }

                break;
            }

            _mappingDragTargetIndex = Math.Max(0,
                Math.Min(ProfileViewModel.MappingsList.Count - 1, desiredIndex));
            UpdateMappingNeighbourShifts();
        }

        private void UpdateMappingNeighbourShifts()
        {
            var source = _mappingDragSource;
            if (source == null) return;

            var sourceIndex = _mappingDragOriginalIndex;
            var targetIndex = _mappingDragTargetIndex;
            if (sourceIndex < 0 || targetIndex < 0) return;

            for (var index = 0; index < ProfileViewModel.MappingsList.Count; index++)
            {
                var mapping = ProfileViewModel.MappingsList[index];
                if (ReferenceEquals(mapping, source)) continue;

                MappingDragSlot slot;
                if (!_mappingDragSlots.TryGetValue(mapping, out slot)) continue;

                var targetShift = 0.0;
                if (targetIndex > sourceIndex && index > sourceIndex && index <= targetIndex)
                {
                    targetShift = -_mappingDragSourceHeight;
                }
                else if (targetIndex < sourceIndex && index >= targetIndex && index < sourceIndex)
                {
                    targetShift = _mappingDragSourceHeight;
                }

                AnimateMappingShift(slot.Translate, targetShift);
            }
        }

        private static void AnimateMappingShift(TranslateTransform transform, double target)
        {
            if (transform == null) return;

            var current = transform.Y;
            if (Math.Abs(current - target) < 0.5)
            {
                transform.BeginAnimation(TranslateTransform.YProperty, null);
                transform.Y = target;
                return;
            }

            transform.BeginAnimation(TranslateTransform.YProperty, null);
            transform.Y = target;
            var animation = new DoubleAnimation(current, target, TimeSpan.FromMilliseconds(95))
            {
                FillBehavior = FillBehavior.Stop,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            transform.BeginAnimation(TranslateTransform.YProperty, animation, HandoffBehavior.SnapshotAndReplace);
        }

        private double CurrentMappingScrollOffset()
        {
            return _mappingDragScrollViewer?.VerticalOffset ?? 0.0;
        }

        private void ProfileWindow_OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!_mappingDragActive || e.Key != Key.Escape) return;
            EndMappingDrag(false);
            e.Handled = true;
        }

        private void MappingListView_OnLostMouseCapture(object sender, MouseEventArgs e)
        {
            if (_mappingDragActive && !_mappingDragEnding && Mouse.Captured != MappingListView)
            {
                EndMappingDrag(false);
            }
        }

        private void EndMappingDrag(bool commit)
        {
            if (_mappingDragEnding) return;
            _mappingDragEnding = true;

            var source = _mappingDragSource;
            var targetIndex = _mappingDragTargetIndex;
            try
            {
                if (source != null) source.IsDragging = false;

                // Restore every real card before changing the collection. Because all of this is
                // synchronous on the UI thread, WPF never renders an intermediate snap-back frame.
                RestoreMappingDragVisuals();

                if (commit && source != null && targetIndex >= 0 && targetIndex != _mappingDragOriginalIndex &&
                    ProfileViewModel.MappingsList.Contains(source))
                {
                    ProfileViewModel.MoveMappingTo(source, targetIndex);
                }

                _mappingDragActive = false;
                Mouse.OverrideCursor = null;

                if (Mouse.Captured == MappingListView)
                {
                    MappingListView.ReleaseMouseCapture();
                }

                if (source != null)
                {
                    MappingListView.ScrollIntoView(source);
                    Logger.Info((commit ? "Finished" : "Cancelled") + " direct mapping reorder: " +
                                source.MappingTitle + " at position " +
                                (ProfileViewModel.MappingsList.IndexOf(source) + 1));
                }
            }
            catch (Exception exception)
            {
                Logger.Error("Unable to finish direct mapping reorder", exception);
            }
            finally
            {
                RestoreMappingDragVisuals();
                _mappingDragActive = false;
                Mouse.OverrideCursor = null;
                if (Mouse.Captured == MappingListView) MappingListView.ReleaseMouseCapture();
                ResetPendingMappingDrag();
                _mappingDragEnding = false;
            }
        }

        private void RestoreMappingDragVisuals()
        {
            foreach (var pair in _mappingDragSlots)
            {
                var slot = pair.Value;
                if (slot?.Container == null) continue;

                if (slot.Translate != null)
                {
                    slot.Translate.BeginAnimation(TranslateTransform.YProperty, null);
                    slot.Translate.Y = 0;
                }

                slot.Container.RenderTransform = slot.OriginalRenderTransform;
                Panel.SetZIndex(slot.Container, slot.OriginalZIndex);
            }

            _mappingDragSlots.Clear();
            _mappingDragContainer = null;
            _mappingDragScrollViewer = null;
            _mappingDragTargetIndex = -1;
            _mappingDragSourceHeight = 0;
            _mappingDragGrabOffsetY = 0;
            _mappingDragInitialScrollOffset = 0;
        }

        private void ResetPendingMappingDrag()
        {
            _mappingDragStart = null;
            _mappingDragSource = null;
            _mappingDragOriginalIndex = -1;
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

        private sealed class MappingDragSlot
        {
            public MappingViewModel Mapping { get; set; }
            public ListViewItem Container { get; set; }
            public double Top { get; set; }
            public double Height { get; set; }
            public TranslateTransform Translate { get; set; }
            public Transform OriginalRenderTransform { get; set; }
            public int OriginalZIndex { get; set; }
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

        private void FilterDefinitionList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var filter = FilterDefinitionList.SelectedItem as FilterDefinitionItemViewModel;
            var definingMapping = ProfileViewModel.HighlightFilter(filter);
            if (definingMapping != null) MappingListView.ScrollIntoView(definingMapping);
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
