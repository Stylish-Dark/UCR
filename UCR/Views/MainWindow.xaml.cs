using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Media;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using HidWizards.UCR.Core;
using HidWizards.UCR.Core.Models;
using HidWizards.UCR.Utilities;
using HidWizards.UCR.ViewModels.Dashboard;
using HidWizards.UCR.Views.Dialogs;
using MaterialDesignThemes.Wpf;
using Forms = System.Windows.Forms;
using ProfileWindow = HidWizards.UCR.Views.ProfileViews.ProfileWindow;

namespace HidWizards.UCR.Views
{

    public partial class MainWindow : Window
    {
        private Context Context { get; set; }
        private readonly DashboardViewModel _dashboardViewModel;
        private CloseState WindowCloseState { get; set; }
        private Dictionary<Guid, ProfileWindow> ProfileWindows;
        private readonly HashSet<Guid> _profileWindowsHiddenToTray = new HashSet<Guid>();
        private Point _profileDragStartPoint;
        private ProfileItem _draggedProfileItem;
        private Forms.NotifyIcon _trayIcon;
        private Forms.ToolStripMenuItem _stopCurrentProfileMenuItem;
        private bool _exitRequested;

        enum CloseState
        {
            None,
            Closing,
            ForceClose
        }

        public MainWindow(Context context)
        {
            _dashboardViewModel = new DashboardViewModel(context);
            DataContext = _dashboardViewModel;
            Context = context;
            ProfileWindows = new Dictionary<Guid, ProfileWindow>();
            InitializeComponent();
            InitializeTrayIcon();
        }

        /// <summary>
        /// AddHook Handle WndProc messages in WPF
        /// This cannot be done in a Window's constructor as a handle window handle won't at that point, so there won't be a HwndSource.
        /// </summary>
        /// <param name="e"></param>
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            EnableMessageHandling();
            var hwndSource = PresentationSource.FromVisual(this) as HwndSource;
            hwndSource?.AddHook(WndProc);
        }

        private bool GetSelectedItem(out ProfileItem profileItem)
        {
            var pi = ProfileTree.SelectedItem as ProfileItem;
            if (pi == null)
            {
                MessageBox.Show("Please select a Profile", "No Profile selected!",MessageBoxButton.OK, MessageBoxImage.Exclamation);
                profileItem = null;
                return false;
            }
            profileItem = pi;
            return true;
        }

        // TODO Deprecated, replace with property notifications
        private void ReloadProfileTree()
        {
            var profileTree = ProfileItem.GetProfileTree(Context.Profiles);
            ProfileTree.ItemsSource = profileTree;
        }

        private void ProfileTree_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _profileDragStartPoint = e.GetPosition(ProfileTree);
            var container = GetTreeViewItem(e.OriginalSource as DependencyObject);
            _draggedProfileItem = container?.DataContext as ProfileItem;
        }

        private void ProfileTree_OnPreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _draggedProfileItem == null) return;

            var currentPosition = e.GetPosition(ProfileTree);
            if (Math.Abs(currentPosition.X - _profileDragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(currentPosition.Y - _profileDragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance) return;

            var draggedItem = _draggedProfileItem;
            _draggedProfileItem = null;
            DragDrop.DoDragDrop(ProfileTree, draggedItem, DragDropEffects.Move);
        }

        private void ProfileTree_OnDragOver(object sender, DragEventArgs e)
        {
            var sourceItem = e.Data.GetData(typeof(ProfileItem)) as ProfileItem;
            var targetContainer = GetTreeViewItem(e.OriginalSource as DependencyObject);
            var targetItem = targetContainer?.DataContext as ProfileItem;

            e.Effects = CanReorderProfile(sourceItem, targetItem) ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        }

        private void ProfileTree_OnDrop(object sender, DragEventArgs e)
        {
            var sourceItem = e.Data.GetData(typeof(ProfileItem)) as ProfileItem;
            var targetContainer = GetTreeViewItem(e.OriginalSource as DependencyObject);
            var targetItem = targetContainer?.DataContext as ProfileItem;
            if (!CanReorderProfile(sourceItem, targetItem) || targetContainer == null) return;

            var siblings = sourceItem.Profile.ParentProfile == null
                ? Context.Profiles
                : sourceItem.Profile.ParentProfile.ChildProfiles;

            var sourceIndex = siblings.IndexOf(sourceItem.Profile);
            var targetIndex = siblings.IndexOf(targetItem.Profile);
            if (sourceIndex < 0 || targetIndex < 0) return;

            var targetHeader = GetTreeViewItemHeaderElement(targetContainer);
            var dropPosition = e.GetPosition(targetHeader);
            var insertAfterTarget = dropPosition.Y > targetHeader.ActualHeight / 2;
            var insertIndex = targetIndex + (insertAfterTarget ? 1 : 0);

            siblings.RemoveAt(sourceIndex);
            if (sourceIndex < insertIndex) insertIndex--;
            if (insertIndex < 0) insertIndex = 0;
            if (insertIndex > siblings.Count) insertIndex = siblings.Count;
            siblings.Insert(insertIndex, sourceItem.Profile);

            Context.ContextChanged();
            ReloadProfileTree();
            e.Handled = true;
        }

        private static bool CanReorderProfile(ProfileItem sourceItem, ProfileItem targetItem)
        {
            if (sourceItem?.Profile == null || targetItem?.Profile == null) return false;
            if (ReferenceEquals(sourceItem.Profile, targetItem.Profile)) return false;
            return ReferenceEquals(sourceItem.Profile.ParentProfile, targetItem.Profile.ParentProfile);
        }

        private static FrameworkElement GetTreeViewItemHeaderElement(TreeViewItem container)
        {
            container.ApplyTemplate();

            var header = container.Template?.FindName("ContentGrid", container) as FrameworkElement
                         ?? container.Template?.FindName("PART_Header", container) as FrameworkElement;

            return header != null && header.ActualHeight > 0 ? header : container;
        }

        private TreeViewItem GetTreeViewItem(DependencyObject source)
        {
            while (source != null)
            {
                if (source is TreeViewItem treeViewItem) return treeViewItem;

                if (source is Visual || source is System.Windows.Media.Media3D.Visual3D)
                {
                    source = VisualTreeHelper.GetParent(source);
                }
                else if (source is FrameworkContentElement contentElement)
                {
                    source = contentElement.Parent;
                }
                else
                {
                    source = LogicalTreeHelper.GetParent(source);
                }
            }

            return null;
        }

        #region Profile Actions

        private void ActivateProfile(object sender, RoutedEventArgs e)
        {
            if (!GetSelectedItem(out var profileItem)) return;
            if (!Context.SubscriptionsManager.ActivateProfile(profileItem.Profile))
            {
                // TODO Move to dialog
                MessageBox.Show("The Profile could not be activated, see the log for more details", "Profile failed to activate!", MessageBoxButton.OK, MessageBoxImage.Exclamation);
            }
        }

        private void DeactivateProfile(object sender, RoutedEventArgs e)
        {
            DeactivateCurrentProfile();
        }

        private void DeactivateCurrentProfile()
        {
            if (Context.ActiveProfile == null) return;

            if (!Context.SubscriptionsManager.DeactivateCurrentProfile())
            {
                // TODO Move to dialog
                MessageBox.Show("The active Profile could not be deactivated, see the log for more details", "Profile failed to deactivate!", MessageBoxButton.OK, MessageBoxImage.Exclamation);
            }
        }

        private async void AddProfile(object sender, RoutedEventArgs e)
        {
            var dialog = new CreateProfileDialog("Create profile", Context.DevicesManager);
            var result = (CreateProfileDialogViewModel) await DialogHost.Show(dialog, "RootDialog");
            if (result == null || string.IsNullOrEmpty(result.ProfileName)) return;

            var inputs = result.GetInputDevices().ConvertAll(d => new DeviceConfiguration(d));
            var outputs = result.GetOutputDevices().ConvertAll(d => new DeviceConfiguration(d));

            var profile = Context.ProfilesManager.CreateProfile(result.ProfileName, inputs, outputs);
            Context.ProfilesManager.AddProfile(profile);

            ReloadProfileTree();
            OpenProfileWindow(profile);
        }

        private async void AddChildProfile(object sender, RoutedEventArgs e)
        {
            if (!GetSelectedItem(out var profileItem)) return;
            var dialog = new CreateProfileDialog("Create child profile", Context.DevicesManager);
            var result = (CreateProfileDialogViewModel)await DialogHost.Show(dialog, "RootDialog");
            if (result == null || string.IsNullOrEmpty(result.ProfileName)) return;

            var inputs = result.GetInputDevices().ConvertAll(d => new DeviceConfiguration(d));
            var outputs = result.GetOutputDevices().ConvertAll(d => new DeviceConfiguration(d));

            var profile = Context.ProfilesManager.CreateProfile(result.ProfileName, inputs, outputs);
            Context.ProfilesManager.AddProfile(profile, profileItem.Profile);

            ReloadProfileTree();
            OpenProfileWindow(profile);
        }

        private void EditProfile(object sender, RoutedEventArgs e)
        {
            if (!GetSelectedItem(out var profileItem)) return;

            if (sender is TreeViewItem)
            {
                var senderItem = ((TreeViewItem)sender).DataContext as ProfileItem;
                if (!profileItem.Id.Equals(senderItem?.Id)) return;
            }

            if (ProfileWindows.TryGetValue(profileItem.Profile.Guid, out var profileWindow))
            {
                void FocusAction() => profileWindow.Focus();
                Dispatcher.BeginInvoke((Action) FocusAction);
                return;
            }
            
            OpenProfileWindow(profileItem.Profile);
        }

        private void OpenProfileWindow(Profile profile)
        {
            void ShowAction()
            {
                var win = new ProfileWindow(Context, profile);
                ProfileWindows.Add(win.ProfileGuid, win);
                win.Closed += OnProfileWindowClosed;
                win.Focus();
                win.Show();
                var restoreFocusDialogClose = RootDialog.GetType().GetField("_restoreFocusDialogClose", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                restoreFocusDialogClose?.SetValue(RootDialog, null);
            }

            Dispatcher.BeginInvoke((Action)ShowAction);
        }

        private void OnProfileWindowClosed(object sender, EventArgs e)
        {
            if (sender is ProfileWindow window)
            {
                _profileWindowsHiddenToTray.Remove(window.ProfileGuid);
                ProfileWindows.Remove(window.ProfileGuid);
            }
        }

        private async void RenameProfile(object sender, RoutedEventArgs e)
        {
            if (!GetSelectedItem(out var profileItem)) return;
            var dialog = new StringDialog("Rename profile", "Profile name", profileItem.Profile.Title);
            var result = (bool?)await DialogHost.Show(dialog, "RootDialog");
            if (result == null || !result.Value) return;

            profileItem.Profile.Rename(dialog.Value);
            ReloadProfileTree();
        }

        private async void CopyProfile(object sender, RoutedEventArgs e)
        {
            if (!GetSelectedItem(out var profileItem)) return;
            var dialog = new StringDialog("Copy profile", "Profile name", profileItem.Profile.Title + " Copy");
            var result = (bool?)await DialogHost.Show(dialog, "RootDialog");
            if (result == null || !result.Value) return;

            Context.ProfilesManager.CopyProfile(profileItem.Profile, dialog.Value);
            ReloadProfileTree();
        }

        private async void RemoveProfile(object sender, RoutedEventArgs e)
        {
            if (!GetSelectedItem(out var profileItem)) return;
            var dialog = new BoolDialog("Remove profile","Are you sure you want to remove: " + profileItem.Profile.Title + "?");
            var result = (bool?)await DialogHost.Show(dialog, "RootDialog");
            if (result == null || !result.Value) return;

            profileItem.Profile.Remove();
            ReloadProfileTree();
        }

        #endregion Profile Actions

        private void InitializeTrayIcon()
        {
            var contextMenu = new Forms.ContextMenuStrip();
            _stopCurrentProfileMenuItem = new Forms.ToolStripMenuItem("Stop current profile");
            var exitMenuItem = new Forms.ToolStripMenuItem("Exit UCR");

            _stopCurrentProfileMenuItem.Click += (sender, args) => Dispatcher.BeginInvoke((Action)DeactivateCurrentProfile);
            exitMenuItem.Click += (sender, args) => Dispatcher.BeginInvoke((Action)ExitFromTray);
            contextMenu.Opening += (sender, args) =>
            {
                _stopCurrentProfileMenuItem.Enabled = Context.ActiveProfile != null;
            };
            contextMenu.Items.Add(_stopCurrentProfileMenuItem);
            contextMenu.Items.Add(exitMenuItem);

            _trayIcon = new Forms.NotifyIcon
            {
                Text = "Universal Control Remapper",
                Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location),
                ContextMenuStrip = contextMenu,
                Visible = false
            };
            _trayIcon.MouseDoubleClick += (sender, args) =>
            {
                if (args.Button == Forms.MouseButtons.Left) Dispatcher.BeginInvoke((Action)RestoreFromTray);
            };
        }

        private void HideToTray()
        {
            _profileWindowsHiddenToTray.Clear();
            foreach (var profileWindow in ProfileWindows.Values)
            {
                if (!profileWindow.IsVisible) continue;

                _profileWindowsHiddenToTray.Add(profileWindow.ProfileGuid);
                profileWindow.Hide();
            }

            _trayIcon.Visible = true;
            Hide();
        }

        private void RestoreFromTray()
        {
            Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;

            foreach (var profileGuid in _profileWindowsHiddenToTray)
            {
                if (ProfileWindows.TryGetValue(profileGuid, out var profileWindow))
                {
                    profileWindow.Show();
                }
            }
            _profileWindowsHiddenToTray.Clear();

            Activate();
            _trayIcon.Visible = false;
        }

        private void ExitFromTray()
        {
            RestoreFromTray();
            _exitRequested = true;
            WindowCloseState = CloseState.None;
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }

            base.OnClosed(e);
        }

        private async void MainWindow_OnClosing(object sender, CancelEventArgs e)
        {
            if (!_exitRequested)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }

            if (CloseState.ForceClose.Equals(WindowCloseState)) return;
            if (CloseState.Closing.Equals(WindowCloseState))
            {
                if (WindowState.Equals(WindowState.Minimized)) WindowState = WindowState.Normal;
                
                e.Cancel = true;
                SystemSounds.Exclamation.Play();
                return;
            }

            WindowCloseState = CloseState.Closing;

            if (Context.IsNotSaved)
            {
                e.Cancel = true;

                if (WindowState.Equals(WindowState.Minimized))
                {
                    WindowState = WindowState.Normal;
                    SystemSounds.Exclamation.Play();
                    RootDialog.Focus();
                }

                if (RootDialog.IsOpen)
                {
                    DialogHost.CloseDialogCommand.Execute(null, RootDialog);
                }

                var dialog = new DecisionDialog("Configuration has changed", "Do you want to save before closing?");
                var result = (MessageBoxResult?)await DialogHost.Show(dialog, "RootDialog");
                if (result == null)
                {
                    WindowCloseState = CloseState.None;
                    _exitRequested = false;
                    return;
                }

                switch (result)
                {
                    case MessageBoxResult.None:
                    case MessageBoxResult.Cancel:
                        WindowCloseState = CloseState.None;
                        _exitRequested = false;
                        return;
                    case MessageBoxResult.OK:
                    case MessageBoxResult.Yes:
                        Context.SaveContext();
                        WindowCloseState = CloseState.ForceClose;
                        Close();
                        break;
                    case MessageBoxResult.No:
                        WindowCloseState = CloseState.ForceClose;
                        Close();
                        break;
                }
            }
        }
        
        private void Save_OnExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            Context.SaveContext();
        }

        private void Save_OnCanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = Context.IsNotSaved;
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg != NativeMethods.WM_COPYDATA) return IntPtr.Zero;
            
            var data = (NativeMethods.COPYDATASTRUCT)Marshal.PtrToStructure(lParam, typeof(NativeMethods.COPYDATASTRUCT));
            var argsString = Marshal.PtrToStringAnsi(data.lpData);
            if (!string.IsNullOrEmpty(argsString)) Context.ParseCommandLineArguments(argsString.Split(';'));
            return IntPtr.Zero;
        }

        private void EnableMessageHandling()
        {
            var changeFilter = new NativeMethods.CHANGEFILTERSTRUCT();
            changeFilter.size = (uint)Marshal.SizeOf(changeFilter);
            changeFilter.info = 0;
            if
            (
                NativeMethods.ChangeWindowMessageFilterEx(
                    new WindowInteropHelper(this).EnsureHandle(),
                    NativeMethods.WM_COPYDATA,
                    NativeMethods.ChangeWindowMessageFilterExAction.Allow,
                    ref changeFilter)
            ) return;

            var error = Marshal.GetLastWin32Error();
            MessageBox.Show($"Enabling message handling failed with the error: {error}");
        }

        private async void About_OnClick(object sender, RoutedEventArgs e)
        {
            var dialog = new AboutDialog();
            await DialogHost.Show(dialog, "RootDialog");
        }

        private async void Help_OnClick(object sender, RoutedEventArgs e)
        {
            var dialog = new HelpDialog();
            await DialogHost.Show(dialog, "RootDialog");
        }

        private void ProfileTree_OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            var treeView = sender as TreeView;
            _dashboardViewModel.SelectedProfileItem = treeView?.SelectedItem as ProfileItem;
        }
    }
}
