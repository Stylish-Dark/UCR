using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
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
using HidWizards.UCR.ViewModels.Presentation;
using HidWizards.UCR.Views.Controls;
using HidWizards.UCR.Views.Dialogs;
using MaterialDesignThemes.Wpf;

namespace HidWizards.UCR.Views.ProfileViews
{
    public partial class ProfilePage : UserControl, IDisposable
    {
        public Guid ProfileGuid => Profile.Guid;
        public ContentControl AutoApplyPanelHost => AutoApplyDockHost;
        private Context Context { get; }
        internal Profile Profile { get; }
        private ProfileViewModel ProfileViewModel { get; }
        private DispatcherTimer DispatcherTimer { get; set; }
        private List<DeviceBindingViewModel> DeviceBindingViewModels { get; set; }
        private readonly object _dirtyBindingsLock = new object();
        private readonly HashSet<DeviceBindingViewModel> _dirtyBindings = new HashSet<DeviceBindingViewModel>();
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
        private Profile _profileSnapshot;

        public ProfilePage(Context context, Profile profile, bool embedded = false)
        {
            Context = context;
            Profile = profile;
            ProfileViewModel = new ProfileViewModel(profile);
            InitializeComponent();
            PageTitle.Text = "Mappings — " + profile.Title;
            DataContext = ProfileViewModel;
            _profileSnapshot = embedded ? null : Context.DeepXmlClone(Profile);
            context.ActiveProfileChangedEvent += ContextOnActiveProfileChangedEvent;
            StartGuiTimer();
        }

        public void ResumeEmbeddedVisuals()
        {
            if (!_disposed) StartGuiTimer();
        }

        public void SuspendEmbeddedVisuals()
        {
            StopGuiTimer();
        }

        public void UseEmbeddedLayout()
        {
            ProfilePageHeader.Visibility = Visibility.Collapsed;
            ProfilePageToolbar.Visibility = Visibility.Collapsed;
        }

        private Panel _deviceOriginalParent;
        private ContentControl _deviceDock;
        private Thickness _deviceOriginalMargin;
        private double _deviceOriginalScrollMaxHeight;

        // Reuse the actual device-management controls beneath the profile list.
        // This is not a duplicate readonly list: add, remove and detection remain wired.
        public void DockProfileDevices(ContentControl target)
        {
            if (_disposed || target == null) return;
            if (ReferenceEquals(target, _deviceDock)) return;
            UndockProfileDevices();

            var originalParent = ProfileDevicesPanel.Parent as Panel;
            if (originalParent == null) return;
            _deviceOriginalParent = originalParent;
            _deviceOriginalMargin = ProfileDevicesPanel.Margin;
            _deviceOriginalScrollMaxHeight = ProfileDevicesScrollViewer.MaxHeight;
            originalParent.Children.Remove(ProfileDevicesPanel);

            ProfileDevicesPanel.Margin = new Thickness(8, 7, 8, 8);
            ProfileDevicesPanel.Visibility = Visibility.Visible;
            ProfileDevicesPanel.DataContext = ProfileViewModel;
            ProfileDevicesScrollViewer.MaxHeight = 205;
            target.Content = ProfileDevicesPanel;
            _deviceDock = target;
            ShowInspectorTab(InspectorTabs.SelectedIndex);
        }

        public void UndockProfileDevices()
        {
            if (_deviceDock == null) return;
            _deviceDock.Content = null;
            // Ensure the old ContentPresenter releases its visual child before
            // the Border is reattached to the original Grid.
            _deviceDock.UpdateLayout();
            _deviceDock = null;
            ProfileDevicesPanel.Margin = _deviceOriginalMargin;
            ProfileDevicesScrollViewer.MaxHeight = _deviceOriginalScrollMaxHeight;
            ProfileDevicesPanel.ClearValue(DataContextProperty);
            if (_deviceOriginalParent != null)
            {
                Grid.SetRow(ProfileDevicesPanel, 1);
                _deviceOriginalParent.Children.Add(ProfileDevicesPanel);
                _deviceOriginalParent = null;
            }
            if (!_disposed) ShowInspectorTab(InspectorTabs.SelectedIndex);
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
            TryNavigateBack();
        }

        private void TryNavigateBack()
        {
            if (_profileSnapshot == null || Context.PersistentXmlEquivalent(Profile, _profileSnapshot))
            {
                BackRequested?.Invoke(this, EventArgs.Empty);
                return;
            }

            var result = HidWizards.UCR.Utilities.DarkMessageBox.Show(
                "Save changes to '" + Profile.Title + "' before returning to profiles?",
                "Save profile changes",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Cancel) return;

            if (result == MessageBoxResult.Yes)
            {
                Context.SaveContext();
                _profileSnapshot = Context.DeepXmlClone(Profile);
            }
            else
            {
                var restore = Context.DeepXmlClone(_profileSnapshot);
                Profile.RestorePersistentState(restore);

                // If these profile edits were the only dirty state, this comparison clears the
                // global dirty flag so closing UCR later does not ask to save discarded work.
                Context.HasUnsavedPersistentChanges();
            }

            BackRequested?.Invoke(this, EventArgs.Empty);
        }

        private void Save_OnExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            Context.SaveContext();
            _profileSnapshot = Context.DeepXmlClone(Profile);
        }

        private void Save_OnCanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = Context.IsNotSaved;
        }

        private void ProfilePage_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // Zoom temporarily disabled; preserve normal scrolling.
            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
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

            lock (_dirtyBindingsLock) _dirtyBindings.Clear();
            foreach (var binding in DeviceBindingViewModels)
            {
                binding.UiValueInvalidated -= DeviceBindingOnUiValueInvalidated;
                binding.UiValueInvalidated += DeviceBindingOnUiValueInvalidated;
                binding.RequestUiValueRefresh();
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
            if (DeviceBindingViewModels != null)
            {
                foreach (var binding in DeviceBindingViewModels)
                    binding.UiValueInvalidated -= DeviceBindingOnUiValueInvalidated;
                DeviceBindingViewModels.Clear();
            }
            DeviceBindingViewModels = null;
            lock (_dirtyBindingsLock) _dirtyBindings.Clear();
        }

        private void ContextOnActiveProfileChangedEvent(Profile profile)
        {
            if (Profile.IsActive()) StartGuiTimer();
            else StopGuiTimer();
        }

        private void DeviceBindingOnUiValueInvalidated(DeviceBindingViewModel binding)
        {
            if (binding == null) return;
            lock (_dirtyBindingsLock) _dirtyBindings.Add(binding);
        }

        private void DispatcherTimerOnTick(object sender, EventArgs e)
        {
            if (!IsVisible || DeviceBindingViewModels == null) return;

            DeviceBindingViewModel[] dirty;
            lock (_dirtyBindingsLock)
            {
                if (_dirtyBindings.Count == 0) return;
                dirty = _dirtyBindings.ToArray();
                _dirtyBindings.Clear();
            }

            foreach (var binding in dirty) binding.CurrentValueChanged();
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

        private void ActivateProfileAlongside(object sender, RoutedEventArgs e)
        {
            if (!Profile.ActivateProfileAlongside())
            {
                HidWizards.UCR.Utilities.DarkMessageBox.Show("The additional Profile could not be activated. The profiles that were already running have been restored.", "Profile failed to activate!", MessageBoxButton.OK, MessageBoxImage.Exclamation);
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

            var selectedGroups = result.MappingGroups.Where(group => group.IsSelected)
                .Select(group => group.Section).ToList();
            if (selectedGroups.Count == 0) return;
            var change = ProfileViewModel.BatchChangeDevice(result.SelectedSource, result.SelectedTarget, selectedGroups);
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

        private void CancelProfileInputDetection_OnClick(object sender, RoutedEventArgs e)
        {
            ProfileViewModel.InputDeviceControlViewModel?.CancelInputDeviceDetection();
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
            SetAllMappingsExpanded(false);
        }

        private void ExpandAllMappings_OnClick(object sender, RoutedEventArgs e)
        {
            SetAllMappingsExpanded(true);
        }

        private void SetAllMappingsExpanded(bool expanded)
        {
            if (ProfileViewModel?.MappingsList == null) return;
            // Logical state is updated once per mapping. The viewport-aware panels
            // then calculate new heights in a single layout pass rather than building
            // all off-screen editors and repeatedly relaying the scroll extent.
            foreach (var mapping in ProfileViewModel.MappingsList)
            {
                if (mapping.IsExpanded != expanded)
                    mapping.IsExpanded = expanded;
            }
            foreach (var panel in FindVisualChildren<OuterViewportVirtualizingPanel>(
                MappingSectionsItemsControl))
                panel.InvalidateMeasure();
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
            _ = Dispatcher.BeginInvoke((Action)(() => ScrollSectionIntoView(section)), DispatcherPriority.Background);
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
            if (section == null || Profile.IsActive()) return;

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
            if (section == null) return;
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

        private void AddCompoundPreset_OnClick(object sender, RoutedEventArgs e)
        {
            EditCompoundPresetDialog(null);
        }

        private void EditCompoundPreset_OnClick(object sender, RoutedEventArgs e)
        {
            EditCompoundPresetDialog((sender as FrameworkElement)?.DataContext as CompoundBindingPreset);
        }

        private void RemoveCompoundPreset_OnClick(object sender, RoutedEventArgs e)
        {
            var preset = (sender as FrameworkElement)?.DataContext as CompoundBindingPreset;
            if (Profile.IsActive() || preset == null) return;
            if (HidWizards.UCR.Utilities.DarkMessageBox.Show(
                    "Delete 4-Way preset '" + preset.Name + "'?",
                    "Delete preset", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;
            Profile.CompoundBindingPresets?.Remove(preset);
            Profile.Context.ContextChanged();
            ProfileViewModel.RefreshCompoundPresetItems();
        }

        private void EditCompoundPresetDialog(CompoundBindingPreset existing)
        {
            if (Profile.IsActive()) return;
            CreateCompoundPresetDialog(existing).ShowDialog();
        }

        // Separate construction from showing: actual WPF layout and scrolling
        // can be regression-tested without opening a modal desktop window.
        private Window CreateCompoundPresetDialog(CompoundBindingPreset existing)
        {
            var dialog = new Window
            {
                Title = existing == null ? "New 4-Way preset" : "Edit 4-Way preset",
                Width = 430,
                Height = Math.Min(490, Math.Max(360, SystemParameters.WorkArea.Height - 80)),
                MinWidth = 390,
                MinHeight = 340,
                ResizeMode = ResizeMode.CanResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = new SolidColorBrush(Color.FromRgb(38, 38, 38)),
                Foreground = Brushes.White,
                Owner = Window.GetWindow(this)
            };
            // A scrollable form with a fixed footer keeps Save/Cancel reachable
            // even at small display heights, DPI scaling or enlarged text.
            var layout = new Grid { Margin = new Thickness(14, 12, 14, 12) };
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var root = new StackPanel { Margin = new Thickness(0, 0, 6, 8) };
            var scroll = new ScrollViewer
            {
                Content = root,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            Grid.SetRow(scroll, 0);
            layout.Children.Add(scroll);
            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0)
            };
            Grid.SetRow(actions, 1);
            layout.Children.Add(actions);
            var fields = new List<TextBox>();
            Action<string, string> addField = (label, value) =>
            {
                root.Children.Add(new TextBlock
                {
                    Text = label,
                    Margin = new Thickness(0, fields.Count == 0 ? 0 : 8, 0, 3)
                });
                var edit = new TextBox
                {
                    Height = 28,
                    MaxLength = 60,
                    Text = value ?? string.Empty,
                    Padding = new Thickness(5, 2, 5, 2),
                    Background = new SolidColorBrush(Color.FromRgb(42, 42, 42)),
                    Foreground = Brushes.White,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(100, 100, 100))
                };
                fields.Add(edit);
                root.Children.Add(edit);
            };
            addField("Preset name", existing?.Name);
            var keyNames = new[] { "Left", "Right", "Up", "Down" };
            for (var i = 0; i < keyNames.Length; i++)
                addField(keyNames[i] + " key", existing?.Keys?.ElementAtOrDefault(i));
            var save = new Button
            {
                Content = "Save preset",
                Height = 34, Width = 116,
                IsDefault = true
            };
            save.Click += (clicked, args) =>
            {
                var name = fields[0].Text.Trim();
                var keys = fields.Skip(1).Select(field => field.Text.Trim()).ToList();
                if (name.Length == 0 || keys.Any(string.IsNullOrWhiteSpace))
                {
                    HidWizards.UCR.Utilities.DarkMessageBox.Show(
                        "Give the preset a name and all four directional keys.",
                        "Incomplete preset", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                if (new[] { "WASD", "Arrows", "Numpad", "UHJK", "UJHK" }.Any(preset =>
                    string.Equals(preset, name, StringComparison.OrdinalIgnoreCase)))
                {
                    HidWizards.UCR.Utilities.DarkMessageBox.Show(
                        "This name belongs to a built-in preset. Choose another name.",
                        "Reserved preset name", MessageBoxButton.OK, MessageBoxImage.Information);
                    fields[0].Focus();
                    return;
                }
                if (keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 4)
                {
                    HidWizards.UCR.Utilities.DarkMessageBox.Show(
                        "Each direction must use a different physical key.",
                        "Duplicate directional keys", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                if (Profile.CompoundBindingPresets == null)
                    Profile.CompoundBindingPresets = new List<CompoundBindingPreset>();
                if (Profile.CompoundBindingPresets.Any(preset =>
                    !ReferenceEquals(preset, existing) &&
                    string.Equals(preset.Name, name, StringComparison.OrdinalIgnoreCase)))
                {
                    HidWizards.UCR.Utilities.DarkMessageBox.Show(
                        "A preset with that name already exists.",
                        "Duplicate preset", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                var target = existing;
                if (target == null)
                {
                    target = new CompoundBindingPreset { Capacity = 4 };
                    Profile.CompoundBindingPresets.Add(target);
                }
                target.Name = name;
                target.Capacity = 4;
                target.Keys = keys;
                Profile.Context.ContextChanged();
                ProfileViewModel.RefreshCompoundPresetItems();
                dialog.DialogResult = true;
            };
            var cancel = new Button
            {
                Content = "Cancel", Width = 85, Height = 34,
                IsCancel = true, Margin = new Thickness(0, 0, 10, 0)
            };
            cancel.Click += (sender, args) => dialog.DialogResult = false;
            actions.Children.Add(cancel);
            actions.Children.Add(save);
            dialog.Content = layout;
            dialog.Loaded += (sender, args) => fields[0].Focus();
            return dialog;
        }

        private void RemoveMappingGroup_OnClick(object sender, RoutedEventArgs e)
        {
            var section = (sender as FrameworkElement)?.DataContext as MappingGroupViewModel;
            if (section == null || Profile.IsActive()) return;

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

        private void MappingListView_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (Profile.IsActive()) return;
            var sourceVisual = e.OriginalSource as DependencyObject;
            var onGrip = false;
            while (sourceVisual != null)
            {
                if ((sourceVisual as FrameworkElement)?.Name == "MappingDragHandle") { onGrip = true; break; }
                sourceVisual = VisualTreeHelper.GetParent(sourceVisual);
            }
            if (!onGrip) return;

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

        private void MappingListView_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
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
                if (e.LeftButton != MouseButtonState.Pressed)
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

            if (!_mappingDragStart.HasValue || _mappingDragSource == null || e.LeftButton != MouseButtonState.Pressed) return;

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

            // Dragging needs every row's exact slot position for its existing
            // live-reorder animation. Temporarily realize all rows only during drag.
            VirtualizingPanel.SetIsVirtualizing(list, false);
            FindVisualChild<OuterViewportVirtualizingPanel>(list)?.InvalidateMeasure();
            list.UpdateLayout();
            var sourceContainer = list.ItemContainerGenerator.ContainerFromItem(source) as ListViewItem;
            if (sourceContainer == null || sourceContainer.ActualWidth <= 0 || sourceContainer.ActualHeight <= 0)
            {
                RestoreMappingVirtualization(list);
                return false;
            }

            _mappingDragSlots.Clear();
            _mappingDragMappings = list.Items.OfType<MappingViewModel>().ToList();
            foreach (var mapping in _mappingDragMappings)
            {
                var container = list.ItemContainerGenerator.ContainerFromItem(mapping) as ListViewItem;
                if (container == null || container.ActualHeight <= 0)
                {
                    RestoreMappingDragVisuals();
                    RestoreMappingVirtualization(list);
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
                RestoreMappingVirtualization(list);
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
                    RestoreMappingVirtualization(list);
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

            sourceSlot.Translate.Y = desiredTop - sourceLayoutTop;

            var draggedCentre = desiredTop + (_mappingDragSourceHeight / 2.0);
            var desiredIndex = 0;

            // A dragged card can be taller than the first card. At the absolute top their centres
            // can therefore still compare as "below" the first midpoint, making slot zero impossible.
            // Treat matching/crossing the first card's top edge as an explicit first-position target.
            var firstOther = _mappingDragMappings.FirstOrDefault(mapping => !ReferenceEquals(mapping, source));
            MappingDragSlot firstOtherSlot;
            var isAtFirstSlot = firstOther != null &&
                                _mappingDragSlots.TryGetValue(firstOther, out firstOtherSlot) &&
                                desiredTop <= firstOtherSlot.Top + 1.0;

            if (!isAtFirstSlot)
            {
                foreach (var mapping in _mappingDragMappings)
                {
                    if (ReferenceEquals(mapping, source)) continue;

                    MappingDragSlot slot;
                    if (!_mappingDragSlots.TryGetValue(mapping, out slot)) continue;

                    var midpoint = slot.Top + (slot.Height / 2.0);
                    if (draggedCentre > midpoint)
                    {
                        desiredIndex++;
                        continue;
                    }
                    break;
                }
            }

            var count = _mappingDragMappings.Count;
            var targetIndex = count == 0 ? -1 : Math.Max(0, Math.Min(count - 1, desiredIndex));
            if (targetIndex == _mappingDragTargetIndex) return;

            _mappingDragTargetIndex = targetIndex;
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

        private void ProfilePage_OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if ((e.Key == Key.Escape || e.SystemKey == Key.Escape) &&
                Context.BindingManager.IsBindModeActive)
            {
                Context.BindingManager.CancelBindMode();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Escape &&
                ProfileViewModel.InputDeviceControlViewModel?.IsDetecting == true)
            {
                ProfileViewModel.InputDeviceControlViewModel.CancelInputDeviceDetection();
                e.Handled = true;
                return;
            }

            if (_mappingDragActive && e.Key == Key.Escape)
            {
                EndMappingDrag(false);
                e.Handled = true;
                return;
            }

            if ((Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt && e.Key == Key.Left)
            {
                TryNavigateBack();
                e.Handled = true;
                return;
            }

            if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) ==
                (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.Enter)
            {
                ActivateProfileAlongside(this, new RoutedEventArgs());
                e.Handled = true;
                return;
            }

            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && e.Key == Key.Enter)
            {
                ActivateProfile(this, new RoutedEventArgs());
                e.Handled = true;
                return;
            }

            if (e.Key != Key.F6) return;

            if (BackButton.IsKeyboardFocusWithin)
                FocusFirst(ProfileDevicesPanel);
            else if (ProfileDevicesPanel.IsKeyboardFocusWithin)
                FocusFirst(MappingSectionsScrollViewer);
            else
                BackButton.Focus();

            e.Handled = true;
        }

        private static void FocusFirst(FrameworkElement element)
        {
            if (element == null) return;
            element.Focus();
            element.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
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

        private static void RestoreMappingVirtualization(ListView list)
        {
            if (list == null) return;
            VirtualizingPanel.SetIsVirtualizing(list, true);
            FindVisualChild<OuterViewportVirtualizingPanel>(list)?.InvalidateMeasure();
        }

        private void ResetPendingMappingDrag()
        {
            RestoreMappingVirtualization(_mappingDragListView);
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
            var panel = FindVisualChild<OuterViewportVirtualizingPanel>(list);
            if (panel != null)
                panel.BringMappingIntoView(mapping);
            else
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

        private void AddInspectorOrInput_OnClick(object sender, RoutedEventArgs e)
        {
            if (ProfileViewModel?.SelectedMapping?.ButtonsEnabled != true) return;
            ProfileViewModel.SelectedMapping.AddCondition();
        }

        private void AddInspectorOutput_OnClick(object sender, RoutedEventArgs e)
        {
            if (ProfileViewModel?.SelectedMapping?.ButtonsEnabled != true) return;
            ProfileViewModel.SelectedMapping.AddDefaultOutput();
        }

        private void AddFilter_OnClick(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button == null || !ProfileViewModel.CanEditProfile) return;

            var menu = new ContextMenu
            {
                PlacementTarget = button,
                Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
                Background = new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x24)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x45, 0x45, 0x45)),
                BorderThickness = new Thickness(1)
            };
            var buttonFilter = new MenuItem { Header = "Button filter", Padding = new Thickness(10, 6, 14, 6) };
            buttonFilter.Click += async (s, args) => await CreateNamedFilterAsync("Button to Filter");
            menu.Items.Add(buttonFilter);
            menu.IsOpen = true;
        }

        private async void CreateButtonFilter_OnClick(object sender, RoutedEventArgs e)
        {
            await CreateNamedFilterAsync("Button to Filter");
        }

        private async System.Threading.Tasks.Task CreateNamedFilterAsync(string pluginName)
        {
            if (Profile.IsActive()) return;
            var filterTemplate = Context.GetPlugins().FirstOrDefault(plugin =>
                string.Equals(plugin.PluginName, pluginName, StringComparison.OrdinalIgnoreCase));
            if (filterTemplate == null)
            {
                DarkMessageBox.Show("The filter plugin is unavailable.", "Filter unavailable",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dialog = new StringDialog("Create filter", "Filter name", "New filter");
            var accepted = (bool?)await DialogHost.Show(dialog, ProfileViewModel.ProfileDialogIdentifier);
            if (accepted != true) return;
            var name = dialog.Value?.Trim();
            if (string.IsNullOrWhiteSpace(name)) return;
            if (Profile.GetFilters().Any(existing =>
                string.Equals(existing, name, StringComparison.OrdinalIgnoreCase)))
            {
                DarkMessageBox.Show("A filter with that name already exists. Choose a unique name.",
                    "Filter already exists", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // A filter is a mapping that produces a named condition, not an output device.
            // AddPlugin creates the input binding and allows the normal binding UI to configure it.
            var mapping = ProfileViewModel.AddMappingToSelectedSection(name);
            if (mapping == null) return;
            mapping.AddPlugin(filterTemplate);
            if (mapping.Plugins.Count == 0)
            {
                mapping.Remove();
                return;
            }

            var filterPlugin = mapping.Plugins[0].Plugin;
            var filterName = filterPlugin.GetGuiMatrix()
                .SelectMany(group => group.PluginProperties)
                .FirstOrDefault(property => property.PropertyInfo.Name == "FilterName");
            if (filterName == null)
            {
                mapping.Remove();
                return;
            }
            filterName.Property = name;
            mapping.RefreshFilterDefinitionPresentation();
            ProfileViewModel.RefreshFilterReferenceLabels();
            ProfileViewModel.SelectedMapping = mapping;
            ShowWorkspaceInspector();
            Dispatcher.BeginInvoke((Action)(() => ScrollMappingIntoView(mapping)), DispatcherPriority.Background);
        }

        private void FilterDefinitionList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var filter = FilterDefinitionList.SelectedItem as FilterDefinitionItemViewModel;
            var definingMapping = ProfileViewModel.HighlightFilter(filter);
            if (definingMapping != null) ScrollMappingIntoView(definingMapping);
        }

        // Profile settings use the actual existing settings controls, embedded inside
        // the right inspector instead of navigating away from the mapping workspace.
        public void DockRightWorkspacePanels()
        {
            // The sidebar owns Filters; do not move them to a second host.
        }

        public void UndockRightWorkspacePanels()
        {
            UndockProfileFilters();
        }

        public void DockProfileFilters(ContentControl target)
        {
            // Intentionally keep Filters in the unified workspace sidebar.
        }

        public void UndockProfileFilters()
        {
            // No separate dock exists.
        }

        public void AttachProfileOptions(UIElement options)
        {
            ProfileOptionsHost.Content = options;
        }

        public void DetachProfileOptions()
        {
            ProfileOptionsHost.Content = null;
        }

        private bool _switchingInspectorTab;

        private void InspectorTabs_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source != InspectorTabs || _switchingInspectorTab || _disposed) return;
            ShowInspectorTab(InspectorTabs.SelectedIndex);
        }

        private void SelectInspectorTab(int index)
        {
            if (InspectorTabs == null || InspectorTabs.SelectedIndex == index) return;
            _switchingInspectorTab = true;
            try { InspectorTabs.SelectedIndex = index; }
            finally { _switchingInspectorTab = false; }
        }

        private void ShowInspectorTab(int index)
        {
            if (_disposed || SidebarGrid == null || SelectedMappingInspector == null
                || ProfileOptionsHost == null) return;
            var showOptions = index == 3;
            SelectedMappingInspector.Visibility = Visibility.Collapsed;
            // The sidebar and profile settings are mutually exclusive surfaces.
            // Hiding only their contents leaves an overlay that intercepts clicks.
            SidebarScrollViewer.Visibility = showOptions ? Visibility.Collapsed : Visibility.Visible;
            SidebarGrid.Visibility = showOptions ? Visibility.Collapsed : Visibility.Visible;
            ProfileOptionsHost.Visibility = showOptions ? Visibility.Visible : Visibility.Collapsed;
            ProfileOptionsHost.IsHitTestVisible = showOptions;
            DockedDevicesNotice.Visibility = Visibility.Collapsed;
            MappingFiltersPanel.Visibility = Visibility.Visible;
            CompoundPresetsPanel.Visibility = Visibility.Visible;
            ProfileDevicesPanel.Visibility = Visibility.Visible;
        }

        public void ShowWorkspaceInspector()
        {
            if (_disposed) return;
            SelectInspectorTab(1);
            ShowInspectorTab(1);
        }

        public void ShowWorkspaceToolbox(string section)
        {
            // Mapping creation lives only in the '+' toolbar. The three sidebar
            // sections share one scroll surface; profile options are a separate tab.
            if (section == "Profile options")
            {
                SelectInspectorTab(3);
                ShowInspectorTab(3);
                return;
            }
            ShowWorkspaceInspector();
            if (section == "Devices") ProfileDevicesExpander.IsExpanded = true;
        }

        private void DeleteSelectedMapping_OnClick(object sender, RoutedEventArgs e)
        {
            var mapping = ProfileViewModel.SelectedMapping;
            if (mapping?.ButtonsEnabled != true) return;
            if (MappingCardControl.RequiresDeleteConfirmation(mapping) && MessageBox.Show("Delete mapping '" + mapping.MappingTitle + "'?", "Delete mapping", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            mapping.Remove();
        }

        private void WorkspaceAddMapping_OnClick(object sender, RoutedEventArgs e)
        {
            if (!ProfileViewModel.CanEditProfile) return;
            var mapping = ProfileViewModel.AddMappingToSelectedSection(ProfileViewModel.GetNextMappingTitle());
            if (mapping == null) return;
            // A new mapping starts as one neutral button route, not an axis,
            // joystick or multiple preconfigured outputs.
            if (mapping.Plugins.Count == 0)
            {
                var buttonTemplate = mapping.GetCompatiblePluginOptions().FirstOrDefault(option =>
                    string.Equals(option.Name, "Button to Button", StringComparison.OrdinalIgnoreCase));
                if (buttonTemplate == null)
                {
                    mapping.Remove();
                    Logger.Warn("Cannot create mapping: Button to Button implementation unavailable.");
                    return;
                }
                mapping.AddPlugin(buttonTemplate.Plugin);
                // A fresh mapping must not inherit a controller merely because it is primary.
                foreach (var output in mapping.Plugins.SelectMany(plugin => plugin.Plugin.Outputs))
                    output.SetDeviceConfigurationGuid(Guid.Empty, false);
            }
            ProfileViewModel.SelectedMapping = mapping;
            mapping.IsExpanded = true;
            ShowWorkspaceInspector();
            Dispatcher.BeginInvoke((Action)(() => ScrollMappingIntoView(mapping)), DispatcherPriority.Background);
        }

        private void WorkspaceMore_OnClick(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button?.ContextMenu == null) return;
            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.IsOpen = true;
        }

        private void WorkspaceSearch_OnTextChanged(object sender, TextChangedEventArgs e)
        {
            if (ProfileViewModel?.MappingSections == null) return;
            var query = WorkspaceMappingSearch?.Text?.Trim();
            foreach (var section in ProfileViewModel.MappingSections)
            {
                var view = CollectionViewSource.GetDefaultView(section.Mappings);
                if (view == null) continue;
                if (string.IsNullOrEmpty(query))
                    view.Filter = null;
                else
                    view.Filter = item =>
                    {
                        var mapping = item as MappingViewModel;
                        return mapping?.MappingTitle?.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
                    };
            }
        }

        private void BlockSelectedInputs_OnClick(object sender, RoutedEventArgs e)
        {
            SetSelectedMappingInputBlocking(true);
        }

        private void UnblockSelectedInputs_OnClick(object sender, RoutedEventArgs e)
        {
            SetSelectedMappingInputBlocking(false);
        }

        private void SetSelectedMappingInputBlocking(bool block)
        {
            if (Profile.IsActive()) return;
            var selected = FindVisualChildren<ListView>(MappingSectionsItemsControl)
                .Where(view => view.Name == "MappingRowsList")
                .SelectMany(view => view.SelectedItems.OfType<MappingViewModel>())
                .Distinct().ToList();
            if (selected.Count == 0)
            {
                DarkMessageBox.Show("Select one or more mappings with Ctrl or Shift first.",
                    "No mappings selected", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var changed = 0;
            foreach (var mapping in selected)
            {
                foreach (var input in mapping.DeviceBindings)
                {
                    var binding = input.DeviceBinding;
                    if (binding == null || binding.DeviceIoType != DeviceIoType.Input || !binding.IsBound)
                        continue;
                    if (block && !binding.IsBlockable()) continue;
                    if (binding.Block == block) continue;
                    binding.SetBlock(block);
                    changed++;
                }
                mapping.RefreshCollapsedSummary();
            }
            Logger.Info((block ? "Blocked " : "Unblocked ") + changed +
                " inputs across " + selected.Count + " selected mappings.");
        }

        private static MappingViewModel MappingForWorkspaceGlyph(DependencyObject source)
        {
            for (var current = source; current != null; current = VisualTreeHelper.GetParent(current))
            {
                var row = current as ListViewItem;
                if (row?.DataContext is MappingViewModel mapping) return mapping;
            }
            return null;
        }

        private void QuickBindWorkspaceInput_OnClick(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            var button = sender as Button;
            var descriptor = button?.DataContext as BindingVisualDescriptor;
            var mapping = MappingForWorkspaceGlyph(button);
            if (mapping == null || descriptor == null || !mapping.ButtonsEnabled) return;
            if (mapping.InputIsFullStick && mapping.DirectionalCapacity == 4)
            {
                var selectedBinding = mapping.ResolveCollapsedBinding(descriptor, DeviceIoType.Input);
                MappingCardControl.ShowQuickCompoundInputMenu(mapping, button,
                    selectedBinding?.DeviceBinding?.InputExpressionGroup ?? 0);
                return;
            }
            try
            {
                var selected = mapping.ResolveCollapsedBinding(descriptor, DeviceIoType.Input)?.DeviceBinding;
                if (selected?.IsInBindMode == true)
                    selected.CancelBindMode();
                else
                    mapping.QuickBindInput(descriptor);
            }
            catch (Exception exception)
            {
                Logger.Error("Failed to start workspace input binding", exception);
                DarkMessageBox.Show("Unable to detect input: " + exception.GetBaseException().Message,
                    "Input binding failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void QuickBindWorkspaceInput_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            CancelWorkspaceCapture(sender, e);
        }

        private void QuickBindWorkspaceInput_OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var button = sender as Button;
            var descriptor = button?.DataContext as BindingVisualDescriptor;
            var mapping = MappingForWorkspaceGlyph(button);
            var binding = mapping?.ResolveCollapsedBinding(descriptor, DeviceIoType.Input);
            if (binding?.DeviceBinding == null || !mapping.ButtonsEnabled) return;
            if (binding.DeviceBinding.IsInBindMode)
            {
                binding.DeviceBinding.CancelBindMode();
                e.Handled = true;
                return;
            }

            var menu = CompactInputBindingControl.CreateInputOptionsMenu(binding, button);
            menu.IsOpen = true;
            e.Handled = true;
        }

        private void CancelWorkspaceCapture(object sender, MouseButtonEventArgs e)
        {
            var button = sender as Button;
            var descriptor = button?.DataContext as BindingVisualDescriptor;
            var mapping = MappingForWorkspaceGlyph(button);
            var binding = mapping?.ResolveCollapsedBinding(descriptor, DeviceIoType.Input)?.DeviceBinding;
            if (binding?.IsInBindMode != true) return;
            binding.CancelBindMode();
            e.Handled = true;
        }

        private void QuickBindWorkspaceOutput_OnPreviewMouseRightButtonDown(object sender,
            MouseButtonEventArgs e)
        {
            var button = sender as Button;
            var descriptor = button?.DataContext as BindingVisualDescriptor;
            var mapping = MappingForWorkspaceGlyph(button);
            if (mapping?.ButtonsEnabled != true || descriptor == null) return;
            var binding = mapping.ResolveCollapsedBinding(descriptor, DeviceIoType.Output);
            var output = mapping.Plugins.FirstOrDefault(plugin => plugin.DeviceBindings
                .Any(item => ReferenceEquals(item.DeviceBinding, binding?.DeviceBinding)));
            MappingCardControl.ShowOutputCategoryMenu(mapping, button, output);
            e.Handled = true;
        }

        private async void QuickBindWorkspaceOutput_OnClick(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            var button = sender as Button;
            var descriptor = button?.DataContext as BindingVisualDescriptor;
            var mapping = MappingForWorkspaceGlyph(button);
            if (mapping == null || descriptor == null || !mapping.ButtonsEnabled) return;
            if (mapping.IsCompoundOutput)
                MappingCardControl.ShowQuickCompoundOutputMenu(mapping, button);
            else
                await MappingCardControl.OpenQuickOutputPickerAsync(mapping, descriptor, button);
        }

        private void RenameMappingRow_OnClick(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            var mapping = (sender as FrameworkElement)?.DataContext as MappingViewModel;
            if (mapping?.ButtonsEnabled == true) mapping.Rename();
        }

        private void RemoveMappingRow_OnClick(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            var mapping = (sender as FrameworkElement)?.DataContext as MappingViewModel;
            if (mapping == null || !mapping.ButtonsEnabled) return;
            if (MappingCardControl.RequiresDeleteConfirmation(mapping) &&
                MessageBox.Show("Delete mapping '" + mapping.MappingTitle + "'?", "Delete mapping",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;
            mapping.Remove();
        }

        private void ToggleInlineMapping_OnClick(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            var mapping = (sender as FrameworkElement)?.DataContext as MappingViewModel;
            if (mapping == null) return;
            mapping.IsExpanded = !mapping.IsExpanded;
        }

        private void MappingListView_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selected = (sender as ListView)?.SelectedItem as MappingViewModel;
            if (selected == null) return;
            ProfileViewModel.SelectedMapping = selected;
            // Selecting a row does not hijack another inspector tab.
            if (InspectorTabs.SelectedIndex == 0)
                ShowWorkspaceInspector();
        }

        private MappingViewModel AddNamedFilterMapping(Plugin template, string name)
        {
            var mapping = ProfileViewModel.AddMappingToSelectedSection(ProfileViewModel.GetNextMappingTitle());
            if (mapping == null) return null;
            mapping.AddPlugin(template);
            var property = mapping.Mapping.Plugins.FirstOrDefault()?.GetGuiMatrix()
                .SelectMany(group => group.PluginProperties)
                .FirstOrDefault(item => item.PropertyInfo.Name == "FilterName");
            if (property == null)
            {
                mapping.Remove();
                return null;
            }
            property.Property = name;
            mapping.RefreshFilterDefinitionPresentation();
            ProfileViewModel.RefreshFilterReferenceLabels();
            ProfileViewModel.SelectedMapping = mapping;
            ShowWorkspaceInspector();
            Dispatcher.BeginInvoke((Action)(() => ScrollMappingIntoView(mapping)), DispatcherPriority.Background);
            return mapping;
        }


    }
}
