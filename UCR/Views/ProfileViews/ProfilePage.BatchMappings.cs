using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using HidWizards.UCR.Core.Managers;
using HidWizards.UCR.Core.Models;
using HidWizards.UCR.Core.Models.Binding;
using HidWizards.UCR.Core.Utilities;
using HidWizards.UCR.ViewModels.Presentation;
using HidWizards.UCR.ViewModels.ProfileViewModels;
using HidWizards.UCR.Views.Controls;

namespace HidWizards.UCR.Views.ProfileViews
{
    public partial class ProfilePage
    {
        private readonly List<MappingViewModel> _batchMappings = new List<MappingViewModel>();
        private CancellationTokenSource _batchCancellation;
        private bool _batchRunning;
        private ContextMenu _batchOutputMenu;

        internal bool IsBatchCapturing => _batchRunning && _batchCancellation?.IsCancellationRequested == false;
        private bool CanEditBatchMappings => !_disposed && !Profile.IsActive() &&
                                             !Context.SubscriptionsManager.IsTransitionInProgress;

        private async void BatchCapture_OnClick(object sender, RoutedEventArgs e)
        {
            if (_batchRunning || !CanEditBatchMappings) return;
            RemoveFinishedBatchMappings();
            await RunBatchCaptureAsync();
        }

        private async void BatchAssign_OnClick(object sender, RoutedEventArgs e)
        {
            if (_batchRunning || !CanEditBatchMappings) return;
            RemoveFinishedBatchMappings();
            // Captures stay in capture order. Include other pending mappings in displayed order.
            var pendingMappings = new List<MappingViewModel>(_batchMappings);
            var queuedMappings = new HashSet<MappingViewModel>(pendingMappings);
            foreach (var mapping in ProfileViewModel.MappingsList)
            {
                if (HasUnassignedOutput(mapping) && queuedMappings.Add(mapping))
                    pendingMappings.Add(mapping);
            }

            if (pendingMappings.Count == 0)
            {
                BatchTitle.Text = "Batch assign";
                BatchStatus.Text = "No unassigned Button-to-Button mappings. Capture some inputs first.";
                BatchProgress.Text = string.Empty;
                ShowBatchOverlay();
                return;
            }
            await RunBatchOutputPickerAsync(pendingMappings);
        }

        private void RemoveFinishedBatchMappings()
        {
            var currentMappings = new HashSet<MappingViewModel>(ProfileViewModel.MappingsList);
            _batchMappings.RemoveAll(mapping => !currentMappings.Contains(mapping) || !HasUnassignedOutput(mapping));
        }

        private static bool HasUnassignedOutput(MappingViewModel mapping)
        {
            return GetUnassignedButtonOutputs(mapping).Any();
        }

        private static IEnumerable<DeviceBinding> GetUnassignedButtonOutputs(MappingViewModel mapping)
        {
            return mapping.Plugins.Where(plugin =>
                    string.Equals(plugin.Plugin.PluginName, "Button to Button", StringComparison.OrdinalIgnoreCase))
                .SelectMany(plugin => plugin.Plugin.Outputs)
                .Where(binding => !binding.IsBound);
        }

        private void BatchFinish_OnClick(object sender, RoutedEventArgs e)
        {
            _batchCancellation?.Cancel();
            CloseBatchOverlay();
        }

        private void ProfilePage_OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!(bool)e.NewValue) CancelProfileCapture();
        }

        private void CancelProfileCapture()
        {
            Context.BindingManager.CancelProfileBindMode(Profile);
            if (ProfileViewModel?.MappingsList != null)
                foreach (var mapping in ProfileViewModel.MappingsList)
                    mapping.CancelQuickOutputCapture();
            _batchCancellation?.Cancel();
            if (_batchOutputMenu != null) _batchOutputMenu.IsOpen = false;
            CloseBatchOverlay();
        }

        private void ShowBatchOverlay()
        {
            BatchOverlay.Visibility = Visibility.Visible;
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                if (BatchOverlay.IsVisible) BatchOverlay.Focus();
            }));
        }

        private void CloseBatchOverlay()
        {
            if (BatchOverlay == null) return;
            var restoreFocus = IsVisible && BatchOverlay.IsKeyboardFocusWithin;
            BatchOverlay.Visibility = Visibility.Collapsed;
            if (restoreFocus) UiWorkspaceMore.Focus();
        }

        private static IEnumerable<DeviceBindingNode> FlattenBatchMenu(IEnumerable<DeviceBindingNode> nodes)
        {
            if (nodes == null) yield break;
            foreach (var node in nodes)
            {
                if (node == null) continue;
                if (node.DeviceBindingInfo != null) yield return node;
                foreach (var child in FlattenBatchMenu(node.ChildrenNodes)) yield return child;
            }
        }

        private DeviceBindingNode FindBatchControl(Device device, DeviceIoType ioType, DetectedInputControl detected)
        {
            if (device == null) return null;
            var controls = FlattenBatchMenu(Context.DevicesManager.GetDeviceBindingMenu(device, ioType));
            return detected.FindEquivalentControl(device, controls);
        }

        private DeviceConfiguration FindBatchInput(DetectedInputControl detected)
        {
            if (detected?.Device == null) return null;
            return Profile.GetDeviceConfigurationList(DeviceIoType.Input)
                .FirstOrDefault(item => item?.Device != null &&
                    DevicesManager.PersistedIdentityEquals(item.Device, detected.Device));
        }

        private async Task RunBatchOutputPickerAsync(IEnumerable<MappingViewModel> pendingMappings)
        {
            _batchRunning = true;
            var cancellation = new CancellationTokenSource();
            _batchCancellation = cancellation;
            var token = cancellation.Token;
            var unavailable = new List<string>();
            try
            {
                // Let the workspace menu close before opening the first control picker.
                await Dispatcher.Yield(DispatcherPriority.Background);
                var targets = pendingMappings.SelectMany(mapping => GetUnassignedButtonOutputs(mapping)
                    .Select(binding => new { Mapping = mapping, Binding = binding })).ToArray();
                foreach (var target in targets)
                {
                    if (token.IsCancellationRequested || !CanEditBatchMappings || !IsVisible) break;
                    var mapping = target.Mapping;
                    if (!ProfileViewModel.MappingsList.Contains(mapping) ||
                        !GetUnassignedButtonOutputs(mapping).Contains(target.Binding)) continue;

                    var binding = target.Binding;
                    ProfileViewModel.SelectedMapping = mapping;
                    ScrollMappingIntoView(mapping);
                    var descriptor = new BindingVisualDescriptor
                    {
                        BindingGuid = binding.Guid,
                        DeviceConfigurationGuid = binding.DeviceConfigurationGuid
                    };
                    var menu = MappingCardControl.CreateQuickOutputMenu(mapping, descriptor, UiWorkspaceMore,
                        DeviceBindingCategory.Momentary);
                    if (!menu.Items.OfType<MenuItem>().Any(item => item.IsEnabled))
                    {
                        if (!unavailable.Contains(mapping.MappingTitle)) unavailable.Add(mapping.MappingTitle);
                        continue;
                    }
                    var outputs = mapping.Plugins.SelectMany(plugin => plugin.Plugin.Outputs).ToList();
                    var slotLabel = outputs.Count > 1 ? "Output " + (outputs.IndexOf(binding) + 1) : "Output";
                    menu.Items.Insert(0, new MenuItem
                    {
                        Header = slotLabel + " for " + mapping.MappingTitle,
                        IsEnabled = false
                    });
                    var closed = new TaskCompletionSource<bool>();
                    menu.Closed += (sender, args) => closed.TrySetResult(true);
                    _batchOutputMenu = menu;
                    menu.IsOpen = true;
                    if (menu.IsOpen) await closed.Task;
                    await Dispatcher.Yield(DispatcherPriority.Background);
                    // Esc or clicking outside ends the batch. Earlier selections are kept.
                    if (!target.Binding.IsBound) break;
                }

                if (!token.IsCancellationRequested && IsVisible && CanEditBatchMappings && unavailable.Count != 0)
                {
                    var names = string.Join(", ", unavailable.Take(3));
                    var suffix = unavailable.Count > 3 ? " and " + (unavailable.Count - 3) + " more" : "";
                    MappingCardControl.ShowOutputPickerStatus(UiWorkspaceMore,
                        "No compatible output controls for " + names + suffix + ".");
                }
            }
            catch (Exception exception)
            {
                Logger.Error("Batch output selection failed", exception);
                if (!token.IsCancellationRequested && IsVisible)
                    MappingCardControl.ShowOutputPickerStatus(UiWorkspaceMore, "Batch output selection stopped.");
            }
            finally
            {
                _batchOutputMenu = null;
                _batchCancellation = null;
                _batchRunning = false;
                cancellation.Dispose();
            }
        }

        private string UniqueBatchMappingTitle(string title)
        {
            var baseTitle = string.IsNullOrWhiteSpace(title) ? "Button" : title.Trim();
            var candidate = baseTitle;
            var suffix = 2;
            while (Profile.GetAllMappings().Any(mapping =>
                string.Equals(mapping.Title, candidate, StringComparison.OrdinalIgnoreCase)))
            {
                candidate = baseTitle + " " + suffix++;
            }
            return candidate;
        }

        private async Task RunBatchCaptureAsync()
        {
            Context.BindingManager.CancelProfileBindMode(Profile);
            _batchRunning = true;
            var cancellation = new CancellationTokenSource();
            _batchCancellation = cancellation;
            var token = cancellation.Token;
            var capturedControls = new HashSet<Tuple<Guid, int, int, int>>(_batchMappings
                .SelectMany(mapping => mapping.DeviceBindings)
                .Select(input => input.DeviceBinding)
                .Where(binding => binding.IsBound && binding.DeviceBindingCategory == DeviceBindingCategory.Momentary)
                .Select(binding => Tuple.Create(binding.DeviceConfigurationGuid, binding.KeyType,
                    binding.KeyValue, binding.KeySubValue)));
            var capturedCount = 0;
            ShowBatchOverlay();
            BatchTitle.Text = "Batch capture inputs";

            try
            {
                var buttonPlugin = ProfileViewModel.PluginToolbox.PluginGroupList.Values
                    .SelectMany(group => group.Plugins)
                    .FirstOrDefault(plugin => string.Equals(plugin.Name, "Button to Button",
                        StringComparison.OrdinalIgnoreCase));

                if (buttonPlugin == null)
                    throw new InvalidOperationException("The Button to Button plugin is unavailable.");

                while (!token.IsCancellationRequested && CanEditBatchMappings)
                {
                    BatchStatus.Text = "Press a button on the input device. Each distinct button creates a mapping.";
                    BatchProgress.Text = capturedCount + " inputs captured";

                    var detected = await Context.DevicesManager.DetectInputControlAsync(
                        DeviceBindingCategory.Momentary, TimeSpan.FromSeconds(20), token);
                    if (token.IsCancellationRequested || !CanEditBatchMappings) break;
                    if (detected == null) continue;

                    var input = FindBatchInput(detected);
                    var node = FindBatchControl(input?.Device, DeviceIoType.Input, detected);
                    if (input == null || node == null)
                    {
                        BatchStatus.Text = "Add the detected device under profile INPUT devices first.";
                        await Task.Delay(600, token);
                        continue;
                    }

                    var control = node.DeviceBindingInfo;
                    if (!capturedControls.Add(Tuple.Create(input.Guid, control.KeyType, control.KeyValue, control.KeySubValue)))
                    {
                        BatchStatus.Text = "Already captured: " + node.Title;
                        await Task.Delay(350, token);
                        continue;
                    }

                    var mapping = ProfileViewModel.AddMappingToSelectedSection(UniqueBatchMappingTitle(node.Title));
                    if (mapping == null) break;
                    mapping.AddPlugin(buttonPlugin.Plugin);
                    var binding = mapping.DeviceBindings.FirstOrDefault()?.DeviceBinding;
                    if (binding == null)
                    {
                        ProfileViewModel.RemoveMapping(mapping);
                        throw new InvalidOperationException("Could not initialize the mapping input.");
                    }

                    binding.AssignPhysicalControl(input.Guid, DeviceBindingCategory.Momentary,
                        control.KeyType, control.KeyValue, control.KeySubValue, node.Title);
                    mapping.RefreshCollapsedSummary();
                    _batchMappings.Add(mapping);
                    capturedCount++;
                    BatchStatus.Text = "Captured: " + node.Title;
                    await Task.Delay(110, token);
                }
            }
            catch (OperationCanceledException)
            {
                // Finish intentionally cancels the in-progress detector.
            }
            catch (Exception exception)
            {
                Logger.Error("Batch mapping session failed", exception);
                BatchStatus.Text = "Batch operation stopped: " + exception.Message;
                BatchProgress.Text = "Error";
                await Task.Delay(1400);
            }
            finally
            {
                if (ReferenceEquals(_batchCancellation, cancellation))
                {
                    _batchCancellation = null;
                    _batchRunning = false;
                    CloseBatchOverlay();
                }
                cancellation.Dispose();
            }
        }
    }
}
