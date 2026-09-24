using System;
using System.Collections.Generic;
using System.Linq;
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
        private List<MappingViewModel> _mappingDragMappings = new List<MappingViewModel>();
        private ListViewItem _mappingDragContainer;
        private ListView _mappingDragListView;
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
            ScrollViewerWithMouseWheel(sender as ScrollViewer, e);
        }

        private void MappingSectionsScrollViewer_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            ScrollViewerWithMouseWheel(sender as ScrollViewer, e);
        }

        private static void ScrollViewerWithMouseWheel(ScrollViewer viewer, MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control) return;
            if (viewer == null || viewer.ScrollableHeight <= 0) return;

            var step = Math.Max(24.0, Math.Min(72.0, Math.Abs(e.Delta) / 2.2));
            viewer.ScrollToVerticalOffset(viewer.VerticalOffset + (e.Delta < 0 ? step : -step));
            e.Handled = true;
        }

        #region GUI

        private void StartGuiTimer()
        {
            if (!Profile.IsActive() || DispatcherTimer != null) return;
            // Preview bars are informational UI only. Keep them well below input/render work;
            // 20 Hz is responsive enough for meters without making the editor fight the remapper.
            DispatcherTimer = new DispatcherTimer(DispatcherPriority.Background);
            DispatcherTimer.Interval = TimeSpan.FromMilliseconds(50);
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
            if (Profile.IsActive()) StartGuiTimer();
            else StopGuiTimer();
        }

        private void DispatcherTimerOnTick(object sender, EventArgs e)
        {
            if (!IsVisible || DeviceBindingViewModels == null) return;
            foreach (var binding in DeviceBindingViewModels) binding.CurrentValueChanged();
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

        private async void AddMappingGroup_OnClick(object sender, RoutedEventArgs e)
        {
            if (Profile.IsActive()) return;
            var dialog = new StringDialog("Add mapping group", "Group name", "New group");
            var result = (bool?)await DialogHost.Show(dialog, ProfileViewModel.ProfileDialogIdentifier);
            if (result != true || string.IsNullOrWhiteSpace(dialog.Value)) return;

            var section = ProfileViewModel.AddMappingGroup(dialog.Value.Trim());
            if (section == null) return;
            Logger.Info("Mapping group added: " + section.Title + " in profile " + Profile.Title);
            Dispatcher.BeginInvoke((Action)(() => ScrollSectionIntoView(section)), DispatcherPriority.Background);
        }

        private void ToggleMappingGroupSection_OnClick(object sender, RoutedEventArgs e)
        {
            var section = (sender as FrameworkElement)?.DataContext as MappingGroupViewModel;
            if (section == null) return;
            section.IsExpanded = !section.IsExpanded;
        }

        private async void RenameMappingGroup_OnClick(object sender, RoutedEventArgs e)
        {
            var section = (sender as FrameworkElement)?.DataContext as MappingGroupViewModel;
            if (section == null || section.IsMain || Profile.IsActive()) return;

            var dialog = new StringDialog("Rename mapping group", "Group name", section.Title);
            var result = (bool?)await DialogHost.Show(dialog, ProfileViewModel.ProfileDialogIdentifier);
            if (result != true || string.IsNullOrWhiteSpace(dialog.Value)) return;

            var oldTitle = section.Title;
            if (!ProfileViewModel.RenameMappingGroup(section, dialog.Value.Trim())) return;
            Logger.Info("Mapping group renamed: '" + oldTitle + "' -> '" + section.Title + "'");
        }

        private void DuplicateMappingGroup_OnClick(object sender, RoutedEventArgs e)
        {
            var section = (sender as FrameworkElement)?.DataContext as MappingGroupViewModel;
            var copy = ProfileViewModel.DuplicateMappingGroup(section);
            if (copy == null) return;
            Logger.Info("Mapping group duplicated: " + section.Title + " -> " + copy.Title);
            Dispatcher.BeginInvoke((Action)(() => ScrollSectionIntoView(copy)), DispatcherPriority.Background);
        }

        private void CopyMappingGroup_OnClick(object sender, RoutedEventArgs e)
        {
            var section = (sender as FrameworkElement)?.DataContext as MappingGroupViewModel;
            if (section == null || section.IsMain) return;
            ProfileViewModel.CopyMappingGroup(section);
            Logger.Info("Mapping group copied: " + section.Title);
        }

        private void PasteMappingGroup_OnClick(object sender, RoutedEventArgs e)
        {
            var section = ProfileViewModel.PasteMappingGroup();
            if (section == null) return;
            Logger.Info("Mapping group pasted into profile " + Profile.Title + ": " + section.Title);
            Dispatcher.BeginInvoke((Action)(() => ScrollSectionIntoView(section)), DispatcherPriority.Background);
        }

        private void RemoveMappingGroup_OnClick(object sender, RoutedEventArgs e)
        {
            var section = (sender as FrameworkElement)?.DataContext as MappingGroupViewModel;
            if (section == null || section.IsMain || Profile.IsActive()) return;

            if (section.Mappings.Count > 0)
            {
                var result = HidWizards.UCR.Utilities.DarkMessageBox.Show(
                    "Remove mapping group '" + section.Title + "' and its " + section.Mappings.Count +
                    " mapping" + (section.Mappings.Count == 1 ? "" : "s") + "?",
                    "Remove mapping group", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result != MessageBoxResult.Yes) return;
            }

            var title = section.Title;
            if (!ProfileViewModel.RemoveMappingGroup(section)) return;
            Logger.Info("Mapping group removed: " + title + " from profile " + Profile.Title);
        }

        private void MappingListView_OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (Profile.IsActive()) return;

            var list = sender as ListView;
            if (list == null) return;
            var container = FindVisualAncestor<ListViewItem>(e.OriginalSource as DependencyObject);
            var mapping = container?.DataContext as MappingViewModel;
            if (mapping == null || mapping.IsExpanded || !list.Items.Contains(mapping)) return;

            _mappingDragListView = list;
            _mappingDragStart = e.GetPosition(list);
            _mappingDragSource = mapping;
            _mappingDragOriginalIndex = list.Items.IndexOf(mapping);
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
            var list = _mappingDragListView;
            if (list == null) return;

            if (_mappingDragActive)
            {
                if (e.RightButton != MouseButtonState.Pressed)
                {
                    EndMappingDrag(true);
                    return;
                }

                AutoScrollMappingList();
                var pointer = Mouse.GetPosition(list);
                UpdateMappingDrag(pointer);
                e.Handled = true;
                return;
            }

            if (!_mappingDragStart.HasValue || _mappingDragSource == null || e.RightButton != MouseButtonState.Pressed) return;

            var position = e.GetPosition(list);
            if (Math.Abs(position.X - _mappingDragStart.Value.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(position.Y - _mappingDragStart.Value.Y) < SystemParameters.MinimumVerticalDragDistance) return;

            if (BeginMappingDrag(position)) e.Handled = true;
        }

        private bool BeginMappingDrag(Point pointer)
        {
            var source = _mappingDragSource;
            var list = _mappingDragListView;
            if (source == null || list == null || source.IsExpanded || Profile.IsActive()) return false;

            list.UpdateLayout();
            var sourceContainer = list.ItemContainerGenerator.ContainerFromItem(source) as ListViewItem;
            if (sourceContainer == null || sourceContainer.ActualWidth <= 0 || sourceContainer.ActualHeight <= 0) return false;

            _mappingDragSlots.Clear();
            _mappingDragMappings = list.Items.OfType<MappingViewModel>().ToList();
            foreach (var mapping in _mappingDragMappings)
            {
                var container = list.ItemContainerGenerator.ContainerFromItem(mapping) as ListViewItem;
                if (container == null || container.ActualHeight <= 0)
                {
                    RestoreMappingDragVisuals();
                    Logger.Warn("Unable to prepare mapping reorder because a mapping card container is unavailable.");
                    return false;
                }

                var top = container.TranslatePoint(new Point(0, 0), list).Y;
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
                if (!list.CaptureMouse())
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
                if (Mouse.Captured == list) list.ReleaseMouseCapture();
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
            var list = _mappingDragListView;
            if (source == null || list == null || !_mappingDragActive) return;

            MappingDragSlot sourceSlot;
            if (!_mappingDragSlots.TryGetValue(source, out sourceSlot)) return;

            var sourceLayoutTop = sourceSlot.Top;
            var desiredTop = pointer.Y - _mappingDragGrabOffsetY;
            var maximumTop = Math.Max(0.0, list.ActualHeight - _mappingDragSourceHeight);
            desiredTop = Math.Max(0.0, Math.Min(maximumTop, desiredTop));

            sourceSlot.Translate.BeginAnimation(TranslateTransform.YProperty, null);
            sourceSlot.Translate.Y = desiredTop - sourceLayoutTop;

            var draggedCentre = desiredTop + (_mappingDragSourceHeight / 2.0);
            var desiredIndex = 0;
            foreach (var mapping in _mappingDragMappings)
            {
                if (ReferenceEquals(mapping, source)) continue;

                MappingDragSlot slot;
                if (!_mappingDragSlots.TryGetValue(mapping, out slot)) continue;

                var midpoint = slot.Top + (slot.Height / 2.0);
                if (draggedCentre >= midpoint)
                {
                    desiredIndex++;
                    continue;
                }
                break;
            }

            var count = _mappingDragMappings.Count;
            _mappingDragTargetIndex = count == 0 ? -1 : Math.Max(0, Math.Min(count - 1, desiredIndex));
            UpdateMappingNeighbourShifts();
        }

        private void UpdateMappingNeighbourShifts()
        {
            var source = _mappingDragSource;
            if (source == null) return;

            var mappings = _mappingDragMappings;
            var sourceIndex = _mappingDragOriginalIndex;
            var targetIndex = _mappingDragTargetIndex;
            if (sourceIndex < 0 || targetIndex < 0) return;

            for (var index = 0; index < mappings.Count; index++)
            {
                var mapping = mappings[index];
                if (ReferenceEquals(mapping, source)) continue;

                MappingDragSlot slot;
                if (!_mappingDragSlots.TryGetValue(mapping, out slot)) continue;

                var targetShift = 0.0;
                if (targetIndex > sourceIndex && index > sourceIndex && index <= targetIndex)
                    targetShift = -_mappingDragSourceHeight;
                else if (targetIndex < sourceIndex && index >= targetIndex && index < sourceIndex)
                    targetShift = _mappingDragSourceHeight;

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

        private void ProfileWindow_OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!_mappingDragActive || e.Key != Key.Escape) return;
            EndMappingDrag(false);
            e.Handled = true;
        }

        private void MappingListView_OnLostMouseCapture(object sender, MouseEventArgs e)
        {
            if (_mappingDragActive && !_mappingDragEnding && Mouse.Captured != _mappingDragListView)
                EndMappingDrag(false);
        }

        private void EndMappingDrag(bool commit)
        {
            if (_mappingDragEnding) return;
            _mappingDragEnding = true;

            var source = _mappingDragSource;
            var list = _mappingDragListView;
            var targetIndex = _mappingDragTargetIndex;
            try
            {
                if (source != null) source.IsDragging = false;
                RestoreMappingDragVisuals();

                if (commit && source != null && list != null && targetIndex >= 0 &&
                    targetIndex != _mappingDragOriginalIndex && list.Items.Contains(source))
                {
                    ProfileViewModel.MoveMappingTo(source, targetIndex);
                }

                _mappingDragActive = false;
                Mouse.OverrideCursor = null;
                if (list != null && Mouse.Captured == list) list.ReleaseMouseCapture();

                if (source != null && list != null)
                {
                    list.ScrollIntoView(source);
                    Logger.Info((commit ? "Finished" : "Cancelled") + " direct mapping reorder: " +
                                source.MappingTitle + " at position " + (list.Items.IndexOf(source) + 1));
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
                if (list != null && Mouse.Captured == list) list.ReleaseMouseCapture();
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
            _mappingDragMappings.Clear();
            _mappingDragContainer = null;
            _mappingDragTargetIndex = -1;
            _mappingDragSourceHeight = 0;
            _mappingDragGrabOffsetY = 0;
        }

        private void ResetPendingMappingDrag()
        {
            _mappingDragStart = null;
            _mappingDragSource = null;
            _mappingDragOriginalIndex = -1;
            _mappingDragListView = null;
        }

        private void AutoScrollMappingList()
        {
            var scrollViewer = MappingSectionsScrollViewer;
            if (scrollViewer == null || scrollViewer.ScrollableHeight <= 0) return;

            var pointer = Mouse.GetPosition(scrollViewer);
            const double edgeZone = 42.0;
            const double step = 30.0;
            if (pointer.Y < edgeZone)
                scrollViewer.ScrollToVerticalOffset(Math.Max(0, scrollViewer.VerticalOffset - step));
            else if (pointer.Y > scrollViewer.ActualHeight - edgeZone)
                scrollViewer.ScrollToVerticalOffset(Math.Min(scrollViewer.ScrollableHeight, scrollViewer.VerticalOffset + step));
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


        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject source) where T : DependencyObject
        {
            if (source == null) yield break;
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(source); index++)
            {
                var child = VisualTreeHelper.GetChild(source, index);
                var match = child as T;
                if (match != null) yield return match;
                foreach (var nested in FindVisualChildren<T>(child)) yield return nested;
            }
        }

        private void ScrollMappingIntoView(MappingViewModel mapping)
        {
            if (mapping == null) return;
            MappingSectionsItemsControl.UpdateLayout();
            var list = FindVisualChildren<ListView>(MappingSectionsItemsControl)
                .FirstOrDefault(candidate => candidate.Items.Contains(mapping));
            if (list == null) return;
            list.ScrollIntoView(mapping);
            list.UpdateLayout();
            (list.ItemContainerGenerator.ContainerFromItem(mapping) as FrameworkElement)?.BringIntoView();
        }

        private void ScrollSectionIntoView(MappingGroupViewModel section)
        {
            if (section == null) return;
            MappingSectionsItemsControl.UpdateLayout();
            (MappingSectionsItemsControl.ItemContainerGenerator.ContainerFromItem(section) as FrameworkElement)?.BringIntoView();
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
            if (definingMapping != null) ScrollMappingIntoView(definingMapping);
        }

        private void AddMapping_OnClick(object sender, RoutedEventArgs e)
        {
            var selectedRoute = ProfileViewModel.PluginToolbox.SelectedRoute;
            if (selectedRoute == null || selectedRoute.PluginItem == null) return;

            var mappingViewModel = ProfileViewModel.AddMappingToSelectedSection(ProfileViewModel.GetNextMappingTitle());
            if (mappingViewModel == null) return;
            mappingViewModel.AddPlugin(selectedRoute.PluginItem.Plugin);
            mappingViewModel.IsExpanded = true;
            Dispatcher.BeginInvoke((Action)(() => ScrollMappingIntoView(mappingViewModel)), DispatcherPriority.Background);
        }
    }
}
