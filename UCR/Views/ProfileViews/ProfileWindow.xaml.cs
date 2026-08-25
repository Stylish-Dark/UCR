using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
        private MappingDragAdorner _mappingDragAdorner;
        private AdornerLayer _mappingDragAdornerLayer;
        private Point _mappingDragGrabOffset;
        private bool _mappingDragActive;
        private bool _mappingDragEnding;

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
                UpdateMappingDragVisual(pointer);
                AutoScrollMappingList(pointer);
                ReorderMappingAtPointer(_mappingDragSource, pointer);
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

            var container = MappingListView.ItemContainerGenerator.ContainerFromItem(source) as ListViewItem;
            if (container == null || container.ActualWidth <= 0 || container.ActualHeight <= 0) return false;

            var layer = AdornerLayer.GetAdornerLayer(MappingListView);
            if (layer == null) return false;

            var snapshot = CaptureElement(container);
            if (snapshot == null) return false;

            var topLeft = container.TranslatePoint(new Point(0, 0), MappingListView);
            _mappingDragGrabOffset = new Point(pointer.X - topLeft.X, pointer.Y - topLeft.Y);

            MappingDragAdorner adorner = null;
            try
            {
                Logger.Debug("Preparing live mapping reorder: " + source.MappingTitle);
                adorner = new MappingDragAdorner(
                    MappingListView,
                    snapshot,
                    new Size(container.ActualWidth, container.ActualHeight));
                layer.Add(adorner);

                if (!MappingListView.CaptureMouse())
                {
                    layer.Remove(adorner);
                    Logger.Warn("Unable to capture mouse for live mapping reorder: " + source.MappingTitle);
                    return false;
                }

                _mappingDragAdornerLayer = layer;
                _mappingDragAdorner = adorner;
                source.IsDragging = true;
                _mappingDragActive = true;
                Mouse.OverrideCursor = Cursors.SizeAll;
                UpdateMappingDragVisual(pointer);
                Logger.Info("Started live mapping reorder: " + source.MappingTitle);
                return true;
            }
            catch (Exception exception)
            {
                try
                {
                    if (adorner != null) layer.Remove(adorner);
                }
                catch
                {
                    // The drag never became active; cleanup must not turn a drag failure into a crash.
                }

                if (Mouse.Captured == MappingListView)
                {
                    MappingListView.ReleaseMouseCapture();
                }

                source.IsDragging = false;
                _mappingDragAdorner = null;
                _mappingDragAdornerLayer = null;
                _mappingDragActive = false;
                Mouse.OverrideCursor = null;
                ResetPendingMappingDrag();
                Logger.Error("Unable to start live mapping reorder", exception);
                return false;
            }
        }

        private void UpdateMappingDragVisual(Point pointer)
        {
            _mappingDragAdorner?.MoveTo(
                pointer.X - _mappingDragGrabOffset.X,
                pointer.Y - _mappingDragGrabOffset.Y);
        }

        private void ReorderMappingAtPointer(MappingViewModel source, Point pointer)
        {
            if (source == null) return;
            var sourceIndex = ProfileViewModel.MappingsList.IndexOf(source);
            if (sourceIndex < 0) return;

            // Treat the dragged card as temporarily lifted out of the sequence. Its invisible
            // layout slot remains in the list, while the fully rendered drag visual follows the
            // pointer. Crossing another card's midpoint moves the slot immediately, producing the
            // same continuous shuffle behaviour used by browser tabs.
            var desiredIndex = 0;
            foreach (var mapping in ProfileViewModel.MappingsList)
            {
                if (ReferenceEquals(mapping, source)) continue;

                var container = MappingListView.ItemContainerGenerator.ContainerFromItem(mapping) as ListViewItem;
                if (container == null) continue;

                var midpoint = container.TranslatePoint(
                    new Point(0, container.ActualHeight / 2.0), MappingListView).Y;
                if (pointer.Y >= midpoint)
                {
                    desiredIndex++;
                    continue;
                }

                break;
            }

            desiredIndex = Math.Max(0, Math.Min(ProfileViewModel.MappingsList.Count - 1, desiredIndex));
            if (desiredIndex == sourceIndex) return;
            MoveMappingLive(source, desiredIndex);
        }

        private void MoveMappingLive(MappingViewModel source, int finalIndex)
        {
            var sourceIndex = ProfileViewModel.MappingsList.IndexOf(source);
            if (sourceIndex < 0) return;

            finalIndex = Math.Max(0, Math.Min(ProfileViewModel.MappingsList.Count - 1, finalIndex));
            if (sourceIndex == finalIndex) return;

            ProfileViewModel.MoveMappingTo(source, finalIndex);
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
            var originalIndex = _mappingDragOriginalIndex;
            try
            {
                if (!commit && source != null && originalIndex >= 0 && ProfileViewModel.MappingsList.Contains(source))
                {
                    MoveMappingLive(source, originalIndex);
                }

                if (source != null) source.IsDragging = false;

                if (_mappingDragAdorner != null && _mappingDragAdornerLayer != null)
                {
                    _mappingDragAdornerLayer.Remove(_mappingDragAdorner);
                }

                _mappingDragAdorner = null;
                _mappingDragAdornerLayer = null;
                _mappingDragActive = false;
                Mouse.OverrideCursor = null;

                if (Mouse.Captured == MappingListView)
                {
                    MappingListView.ReleaseMouseCapture();
                }

                if (source != null)
                {
                    MappingListView.ScrollIntoView(source);
                    Logger.Info((commit ? "Finished" : "Cancelled") + " live mapping reorder: " +
                                source.MappingTitle + " at position " +
                                (ProfileViewModel.MappingsList.IndexOf(source) + 1));
                }
            }
            finally
            {
                ResetPendingMappingDrag();
                _mappingDragEnding = false;
            }
        }

        private void ResetPendingMappingDrag()
        {
            _mappingDragStart = null;
            _mappingDragSource = null;
            _mappingDragOriginalIndex = -1;
        }

        private static ImageSource CaptureElement(FrameworkElement element)
        {
            try
            {
                element.UpdateLayout();
                var transform = Matrix.Identity;
                var presentationSource = PresentationSource.FromVisual(element);
                if (presentationSource?.CompositionTarget != null)
                {
                    transform = presentationSource.CompositionTarget.TransformToDevice;
                }

                var scaleX = transform.M11 <= 0 ? 1.0 : transform.M11;
                var scaleY = transform.M22 <= 0 ? 1.0 : transform.M22;
                var pixelWidth = Math.Max(1, (int)Math.Ceiling(element.ActualWidth * scaleX));
                var pixelHeight = Math.Max(1, (int)Math.Ceiling(element.ActualHeight * scaleY));
                var bitmap = new RenderTargetBitmap(
                    pixelWidth,
                    pixelHeight,
                    96.0 * scaleX,
                    96.0 * scaleY,
                    PixelFormats.Pbgra32);
                bitmap.Render(element);
                bitmap.Freeze();
                return bitmap;
            }
            catch (Exception exception)
            {
                Logger.Error("Unable to capture mapping card for live reorder", exception);
                return null;
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

        private sealed class MappingDragAdorner : Adorner
        {
            private readonly VisualCollection _visuals;
            private readonly Image _image;
            private readonly Size _size;
            private double _left;
            private double _top;

            public MappingDragAdorner(UIElement adornedElement, ImageSource source, Size size)
                : base(adornedElement)
            {
                // WPF can query VisualChildrenCount while dependency properties are being changed
                // in this constructor. Initialize the collection first so those callbacks never
                // observe a null visual tree.
                _visuals = new VisualCollection(this);
                IsHitTestVisible = false;
                _size = size;
                _image = new Image
                {
                    Source = source,
                    Width = size.Width,
                    Height = size.Height,
                    Stretch = Stretch.Fill,
                    SnapsToDevicePixels = true,
                    Opacity = 1.0
                };
                _visuals.Add(_image);
            }

            public void MoveTo(double left, double top)
            {
                _left = left;
                _top = top;
                InvalidateArrange();
            }

            protected override int VisualChildrenCount => _visuals?.Count ?? 0;

            protected override Visual GetVisualChild(int index)
            {
                if (_visuals == null) throw new ArgumentOutOfRangeException(nameof(index));
                return _visuals[index];
            }

            protected override Size MeasureOverride(Size constraint)
            {
                _image.Measure(_size);
                return constraint;
            }

            protected override Size ArrangeOverride(Size finalSize)
            {
                _image.Arrange(new Rect(new Point(_left, _top), _size));
                return finalSize;
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
