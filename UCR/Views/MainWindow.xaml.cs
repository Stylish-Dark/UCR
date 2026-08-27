using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using HidWizards.UCR.Core;
using HidWizards.UCR.Core.Managers;
using HidWizards.UCR.Core.Models;
using HidWizards.UCR.Core.Utilities;
using HidWizards.UCR.Utilities;
using HidWizards.UCR.ViewModels.Dashboard;
using HidWizards.UCR.Views.Dialogs;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;
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
        private readonly AutoProfileMonitor _autoProfileMonitor;
        private DeviceManagerDialog _deviceManagerWindow;
        private bool _deviceManagerHiddenToTray;
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
            _autoProfileMonitor = new AutoProfileMonitor(context);
        }

        private void MainWindow_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control) return;
            var scale = AppearanceManager.AdjustUiScale(e.Delta);
            Logger.Info("UI scale changed to " + Math.Round(scale * 100) + "%");
            e.Handled = true;
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
                HidWizards.UCR.Utilities.DarkMessageBox.Show("Please select a Profile", "No Profile selected!",MessageBoxButton.OK, MessageBoxImage.Exclamation);
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
            _dashboardViewModel.ReplaceProfileList(profileTree);
        }

        private void ProfileTree_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!string.Equals(_dashboardViewModel.ProfileGroupingMode, "Tree", StringComparison.Ordinal))
            {
                _draggedProfileItem = null;
                return;
            }

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
            if (!string.Equals(_dashboardViewModel.ProfileGroupingMode, "Tree", StringComparison.Ordinal))
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            var sourceItem = e.Data.GetData(typeof(ProfileItem)) as ProfileItem;
            var targetContainer = GetTreeViewItem(e.OriginalSource as DependencyObject);
            var targetItem = targetContainer?.DataContext as ProfileItem;

            e.Effects = CanReorderProfile(sourceItem, targetItem) ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        }

        private void ProfileTree_OnDrop(object sender, DragEventArgs e)
        {
            if (!string.Equals(_dashboardViewModel.ProfileGroupingMode, "Tree", StringComparison.Ordinal))
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

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
                HidWizards.UCR.Utilities.DarkMessageBox.Show("The Profile could not be activated, see the log for more details", "Profile failed to activate!", MessageBoxButton.OK, MessageBoxImage.Exclamation);
            }
        }

        private void DeactivateProfile(object sender, RoutedEventArgs e)
        {
            DeactivateCurrentProfile();
        }

        private void BrowseAutoActivateExecutable(object sender, RoutedEventArgs e)
        {
            if (!GetSelectedItem(out var profileItem)) return;

            var dialog = new OpenFileDialog
            {
                Title = "Choose executable for automatic profile activation",
                Filter = "Applications (*.exe)|*.exe",
                DefaultExt = ".exe",
                CheckFileExists = true,
                Multiselect = false
            };

            if (dialog.ShowDialog(this) != true) return;
            profileItem.Profile.AutoActivateExecutable = Path.GetFileName(dialog.FileName);
        }

        private void DeactivateCurrentProfile()
        {
            if (Context.ActiveProfile == null) return;

            if (!Context.SubscriptionsManager.DeactivateCurrentProfile())
            {
                // TODO Move to dialog
                HidWizards.UCR.Utilities.DarkMessageBox.Show("The active Profile could not be deactivated, see the log for more details", "Profile failed to deactivate!", MessageBoxButton.OK, MessageBoxImage.Exclamation);
            }
        }

        private async void AddProfile(object sender, RoutedEventArgs e)
        {
            var dialog = new CreateProfileDialog("Create profile");
            var result = (CreateProfileDialogViewModel) await DialogHost.Show(dialog, "RootDialog");
            if (result == null || string.IsNullOrEmpty(result.ProfileName)) return;

            var profile = Context.ProfilesManager.CreateProfile(result.ProfileName,
                new List<DeviceConfiguration>(), new List<DeviceConfiguration>());
            Context.ProfilesManager.AddProfile(profile);

            ReloadProfileTree();
            OpenProfileWindow(profile);
        }

        private async void AddChildProfile(object sender, RoutedEventArgs e)
        {
            if (!GetSelectedItem(out var profileItem)) return;
            var dialog = new CreateProfileDialog("Create child profile");
            var result = (CreateProfileDialogViewModel)await DialogHost.Show(dialog, "RootDialog");
            if (result == null || string.IsNullOrEmpty(result.ProfileName)) return;

            var profile = Context.ProfilesManager.CreateProfile(result.ProfileName,
                new List<DeviceConfiguration>(), new List<DeviceConfiguration>());
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
                Dispatcher.BeginInvoke((Action)(() => SurfaceProfileWindow(profileWindow)));
                return;
            }

            OpenProfileWindow(profileItem.Profile);
        }

        private void OpenProfileWindow(Profile profile)
        {
            void ShowAction()
            {
                CloseDeviceManagerWindow();

                if (ProfileWindows.TryGetValue(profile.Guid, out var existing))
                {
                    SurfaceProfileWindow(existing);
                    return;
                }

                // Keep one profile editor form open at a time. Closing the previous editor also
                // releases its binding/plugin view-model graph immediately.
                var previousWindows = new List<ProfileWindow>(ProfileWindows.Values);
                foreach (var previous in previousWindows) previous.Close();

                var win = new ProfileWindow(Context, profile) { Owner = this };
                ProfileWindows[win.ProfileGuid] = win;
                win.Closed += OnProfileWindowClosed;
                win.Show();
                SurfaceProfileWindow(win);
                var restoreFocusDialogClose = RootDialog.GetType().GetField("_restoreFocusDialogClose", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                restoreFocusDialogClose?.SetValue(RootDialog, null);
            }

            Dispatcher.BeginInvoke((Action)ShowAction);
        }

        private static void SurfaceProfileWindow(ProfileWindow window)
        {
            SurfaceAuxiliaryWindow(window);
        }

        private static void SurfaceAuxiliaryWindow(Window window)
        {
            if (window == null) return;
            if (!window.IsVisible) window.Show();
            if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
            window.Topmost = true;
            try
            {
                window.Activate();
                window.Focus();
            }
            finally
            {
                window.Topmost = false;
            }
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

        private async void ImportExport_OnClick(object sender, RoutedEventArgs e)
        {
            var result = (string)await DialogHost.Show(new ImportExportDialog(), "RootDialog");
            if (string.Equals(result, "Import", StringComparison.OrdinalIgnoreCase))
            {
                ImportFromCombinedDialog();
            }
            else if (string.Equals(result, "Export", StringComparison.OrdinalIgnoreCase))
            {
                ExportFromCombinedDialog();
            }
        }

        private void ImportFromCombinedDialog()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Import UCR profile or profile list",
                Filter = "UCR files (*.ucrprofile;*.ucrprofiles)|*.ucrprofile;*.ucrprofiles|UCR profile (*.ucrprofile)|*.ucrprofile|UCR profile list (*.ucrprofiles)|*.ucrprofiles",
                CheckFileExists = true,
                Multiselect = false
            };
            if (dialog.ShowDialog(this) != true) return;

            var extension = Path.GetExtension(dialog.FileName);
            if (string.Equals(extension, ".ucrprofiles", StringComparison.OrdinalIgnoreCase))
            {
                ImportProfileListFromPath(dialog.FileName);
            }
            else
            {
                ImportProfilePackageFromPath(dialog.FileName, null);
            }
        }

        private void ExportFromCombinedDialog()
        {
            var selectedProfile = (ProfileTree.SelectedItem as ProfileItem)?.Profile;
            var dialog = new SaveFileDialog
            {
                Title = "Export UCR",
                AddExtension = true
            };

            if (selectedProfile != null)
            {
                dialog.Filter = "Selected profile (*.ucrprofile)|*.ucrprofile|All profiles (*.ucrprofiles)|*.ucrprofiles";
                dialog.FilterIndex = 1;
                dialog.DefaultExt = ".ucrprofile";
                dialog.FileName = SanitizeFileName(selectedProfile.Title) + ".ucrprofile";
            }
            else
            {
                dialog.Filter = "All profiles (*.ucrprofiles)|*.ucrprofiles";
                dialog.FilterIndex = 1;
                dialog.DefaultExt = ".ucrprofiles";
                dialog.FileName = "UCR Profiles.ucrprofiles";
            }

            if (dialog.ShowDialog(this) != true) return;

            try
            {
                var exportAll = selectedProfile == null || dialog.FilterIndex == 2 ||
                                string.Equals(Path.GetExtension(dialog.FileName), ".ucrprofiles", StringComparison.OrdinalIgnoreCase);
                if (exportAll)
                {
                    var exportPath = EnsureExtension(dialog.FileName, ".ucrprofiles");
                    Context.ProfilesManager.ExportProfileList(exportPath);
                    HidWizards.UCR.Utilities.DarkMessageBox.Show(this, "All profiles exported successfully.", "Export profiles", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    var exportPath = EnsureExtension(dialog.FileName, ".ucrprofile");
                    Context.ProfilesManager.ExportProfile(selectedProfile, exportPath);
                    HidWizards.UCR.Utilities.DarkMessageBox.Show(this, "Profile exported successfully.", "Export profile", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception exception)
            {
                ShowTransferError("Export failed", exception);
            }
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

        private void OpenLogs_OnClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var path = Logger.GetLogDirectory();
                System.IO.Directory.CreateDirectory(path);
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = "\"" + path + "\"",
                    UseShellExecute = true
                });
                Logger.Info("Opened diagnostic logs folder: " + path);
            }
            catch (Exception exception)
            {
                Logger.Error("Unable to open the diagnostic logs folder", exception);
                HidWizards.UCR.Utilities.DarkMessageBox.Show(
                    "UCR could not open the logs folder. The logs remain under the UCR 'logs' directory.",
                    "Unable to open logs", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ManageDevices_OnClick(object sender, RoutedEventArgs e)
        {
            if (_deviceManagerWindow != null)
            {
                SurfaceAuxiliaryWindow(_deviceManagerWindow);
                return;
            }

            CloseAllProfileWindows();
            var dialog = new DeviceManagerDialog(Context.DevicesManager) { Owner = this };
            _deviceManagerWindow = dialog;
            dialog.Closed += DeviceManagerWindow_OnClosed;
            dialog.Show();
            SurfaceAuxiliaryWindow(dialog);
        }

        private void DeviceManagerWindow_OnClosed(object sender, EventArgs e)
        {
            if (sender is DeviceManagerDialog dialog) dialog.Closed -= DeviceManagerWindow_OnClosed;
            if (ReferenceEquals(_deviceManagerWindow, sender)) _deviceManagerWindow = null;
            _deviceManagerHiddenToTray = false;
        }

        private void CloseDeviceManagerWindow()
        {
            var dialog = _deviceManagerWindow;
            if (dialog == null) return;
            _deviceManagerWindow = null;
            dialog.Closed -= DeviceManagerWindow_OnClosed;
            dialog.Close();
            _deviceManagerHiddenToTray = false;
        }

        private async void Appearance_OnClick(object sender, RoutedEventArgs e)
        {
            await DialogHost.Show(new AppearanceDialog(), "RootDialog");
        }

        private void ExportProfile(object sender, RoutedEventArgs e)
        {
            if (!GetSelectedItem(out var profileItem)) return;

            var dialog = new SaveFileDialog
            {
                Title = "Export UCR profile",
                Filter = "UCR profile (*.ucrprofile)|*.ucrprofile",
                DefaultExt = ".ucrprofile",
                AddExtension = true,
                FileName = SanitizeFileName(profileItem.Profile.Title) + ".ucrprofile"
            };
            if (dialog.ShowDialog(this) != true) return;

            try
            {
                Context.ProfilesManager.ExportProfile(profileItem.Profile, dialog.FileName);
                HidWizards.UCR.Utilities.DarkMessageBox.Show(this, "Profile exported successfully.", "Export profile", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception exception)
            {
                ShowTransferError("Profile export failed", exception);
            }
        }

        private void ImportProfile(object sender, RoutedEventArgs e)
        {
            ImportProfilePackage(null);
        }

        private void ImportChildProfile(object sender, RoutedEventArgs e)
        {
            if (!GetSelectedItem(out var profileItem)) return;
            ImportProfilePackage(profileItem.Profile);
        }

        private void ImportProfilePackage(Profile parentProfile)
        {
            var dialog = new OpenFileDialog
            {
                Title = parentProfile == null ? "Import UCR profile" : "Import UCR profile as child",
                Filter = "UCR profile (*.ucrprofile)|*.ucrprofile",
                DefaultExt = ".ucrprofile",
                CheckFileExists = true,
                Multiselect = false
            };
            if (dialog.ShowDialog(this) != true) return;
            ImportProfilePackageFromPath(dialog.FileName, parentProfile);
        }

        private void ImportProfilePackageFromPath(string fileName, Profile parentProfile)
        {
            try
            {
                Context.ProfilesManager.ImportProfile(fileName, parentProfile);
                ReloadProfileTree();
                HidWizards.UCR.Utilities.DarkMessageBox.Show(this,
                    parentProfile == null ? "Profile imported successfully." : "Child profile imported successfully.",
                    "Import profile", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception exception)
            {
                ShowTransferError("Profile import failed", exception);
            }
        }

        private void ExportAllProfiles(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Title = "Export all UCR profiles",
                Filter = "UCR profile list (*.ucrprofiles)|*.ucrprofiles",
                DefaultExt = ".ucrprofiles",
                AddExtension = true,
                FileName = "UCR Profiles.ucrprofiles"
            };
            if (dialog.ShowDialog(this) != true) return;

            try
            {
                Context.ProfilesManager.ExportProfileList(dialog.FileName);
                HidWizards.UCR.Utilities.DarkMessageBox.Show(this, "All profiles exported successfully.", "Export profiles", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception exception)
            {
                ShowTransferError("Profile-list export failed", exception);
            }
        }

        private void ImportAllProfiles(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Import UCR profile list",
                Filter = "UCR profile list (*.ucrprofiles)|*.ucrprofiles",
                DefaultExt = ".ucrprofiles",
                CheckFileExists = true,
                Multiselect = false
            };
            if (dialog.ShowDialog(this) != true) return;
            ImportProfileListFromPath(dialog.FileName);
        }

        private void ImportProfileListFromPath(string fileName)
        {
            var choice = HidWizards.UCR.Utilities.DarkMessageBox.Show(this,
                "How should the imported profile list be applied?\n\n" +
                "Yes = REPLACE the current profile list with the imported backup.\n" +
                "No = MERGE the imported profiles into the current list.\n" +
                "Cancel = Do nothing.",
                "Import profile list", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (choice == MessageBoxResult.Cancel) return;

            var mode = choice == MessageBoxResult.Yes
                ? ProfileListImportMode.Replace
                : ProfileListImportMode.Merge;

            try
            {
                var importedCount = Context.ProfilesManager.ImportProfileList(fileName, mode);
                if (mode == ProfileListImportMode.Replace)
                {
                    CloseAllProfileWindows();
                    _dashboardViewModel.SelectedProfileItem = null;
                }
                ReloadProfileTree();
                HidWizards.UCR.Utilities.DarkMessageBox.Show(this,
                    $"Imported {importedCount} top-level profile{(importedCount == 1 ? string.Empty : "s")} successfully.",
                    "Import profiles", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception exception)
            {
                ShowTransferError("Profile-list import failed", exception);
            }
        }

        private void CloseAllProfileWindows()
        {
            var windows = new List<ProfileWindow>(ProfileWindows.Values);
            foreach (var profileWindow in windows)
            {
                profileWindow.Close();
            }
        }

        internal void PrepareForShutdown()
        {
            _autoProfileMonitor?.Dispose();
            CloseAllProfileWindows();
            CloseDeviceManagerWindow();
            if (_trayIcon != null) _trayIcon.Visible = false;
        }

        private static string EnsureExtension(string filePath, string extension)
        {
            if (string.Equals(Path.GetExtension(filePath), extension, StringComparison.OrdinalIgnoreCase)) return filePath;
            return Path.ChangeExtension(filePath, extension.TrimStart('.'));
        }

        private static string SanitizeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "UCR Profile";
            foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(invalidCharacter, '_');
            }
            return value.Trim();
        }

        private void ShowTransferError(string title, Exception exception)
        {
            HidWizards.UCR.Utilities.DarkMessageBox.Show(this, exception.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
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
                Visible = true
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

            _deviceManagerHiddenToTray = _deviceManagerWindow != null && _deviceManagerWindow.IsVisible;
            if (_deviceManagerHiddenToTray) _deviceManagerWindow.Hide();

            _trayIcon.Visible = true;
            Hide();
        }

        private void RestoreFromTray()
        {
            Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;

            ProfileWindow restoredProfile = null;
            foreach (var profileGuid in _profileWindowsHiddenToTray)
            {
                if (ProfileWindows.TryGetValue(profileGuid, out var profileWindow))
                {
                    profileWindow.Show();
                    restoredProfile = profileWindow;
                }
            }
            _profileWindowsHiddenToTray.Clear();

            if (_deviceManagerHiddenToTray && _deviceManagerWindow != null) _deviceManagerWindow.Show();
            _deviceManagerHiddenToTray = false;

            if (_deviceManagerWindow != null && _deviceManagerWindow.IsVisible)
            {
                SurfaceAuxiliaryWindow(_deviceManagerWindow);
            }
            else if (restoredProfile != null)
            {
                SurfaceProfileWindow(restoredProfile);
            }
            else
            {
                var visibleProfile = ProfileWindows.Values.FirstOrDefault(window => window.IsVisible);
                if (visibleProfile != null) SurfaceProfileWindow(visibleProfile);
                else BringToForeground();
            }

            _trayIcon.Visible = true;
        }

        /// <summary>
        /// Gives UCR normal foreground activation behaviour after startup, tray restoration,
        /// or launching UCR again while an instance is already running. The Topmost pulse is
        /// deliberately temporary; UCR must not remain above other applications afterwards.
        /// </summary>
        public void BringToForeground()
        {
            if (!IsVisible) Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;

            Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
            {
                var handle = new WindowInteropHelper(this).Handle;
                if (handle != IntPtr.Zero)
                {
                    NativeMethods.BringWindowToTop(handle);
                    NativeMethods.SetForegroundWindow(handle);
                }

                Topmost = true;
                try
                {
                    Activate();
                    Focus();
                }
                finally
                {
                    Topmost = false;
                }
            }));
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
            _autoProfileMonitor?.Dispose();

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

            e.Cancel = true;
            WindowCloseState = CloseState.Closing;
            var saveBeforeShutdown = false;

            if (Context.IsNotSaved)
            {
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
                        saveBeforeShutdown = true;
                        break;
                    case MessageBoxResult.No:
                        break;
                }
            }

            BeginFinalShutdown(saveBeforeShutdown);
        }

        private void BeginFinalShutdown(bool saveContext)
        {
            WindowCloseState = CloseState.ForceClose;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                var app = Application.Current as App;
                if (app != null)
                {
                    app.ShutdownWithProgress(this, saveContext);
                    return;
                }

                Close();
            }));
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
            RestoreFromTray();
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
            HidWizards.UCR.Utilities.DarkMessageBox.Show($"Enabling message handling failed with the error: {error}");
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
