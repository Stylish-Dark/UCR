using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using HidWizards.UCR.Core.Managers;
using HidWizards.UCR.Core.Models;
using HidWizards.UCR.Core.Models.Binding;
using HidWizards.UCR.Core.Utilities;
using HidWizards.UCR.ViewModels.ProfileViewModels;

namespace HidWizards.UCR.Views.ProfileViews
{
    public partial class ProfilePage
    {
        private readonly List<MappingViewModel> _batchMappings = new List<MappingViewModel>();
        private CancellationTokenSource _batchCancellation;
        private bool _batchRunning;

        internal bool IsBatchCapturing => _batchRunning && _batchCancellation?.IsCancellationRequested == false;
        private bool CanEditBatchMappings => !_disposed && !Profile.IsActive() &&
                                             !Context.SubscriptionsManager.IsTransitionInProgress;

        private async void BatchCapture_OnClick(object sender, RoutedEventArgs e)
        {
            if (_batchRunning || !CanEditBatchMappings) return;
            _batchMappings.Clear();
            await RunBatchSessionAsync(false);
        }

        private async void BatchAssign_OnClick(object sender, RoutedEventArgs e)
        {
            if (_batchRunning || !CanEditBatchMappings) return;
            var currentMappings = new HashSet<MappingViewModel>(ProfileViewModel.MappingsList);
            _batchMappings.RemoveAll(mapping => !currentMappings.Contains(mapping) || !HasUnassignedOutput(mapping));

            // When this page was reopened, use unassigned Button-to-Button mappings
            // in displayed order. Otherwise preserve the most recent capture sequence.
            if (_batchMappings.Count == 0)
            {
                foreach (var mapping in ProfileViewModel.MappingsList)
                {
                    if (HasUnassignedOutput(mapping))
                        _batchMappings.Add(mapping);
                }
            }

            if (_batchMappings.Count == 0)
            {
                BatchTitle.Text = "Batch assign";
                BatchStatus.Text = "No unassigned Button-to-Button mappings. Capture some inputs first.";
                BatchProgress.Text = string.Empty;
                ShowBatchOverlay();
                return;
            }
            await RunBatchSessionAsync(true);
        }

        private static bool HasUnassignedOutput(MappingViewModel mapping)
        {
            var binding = mapping.Plugins.FirstOrDefault()?.DeviceBindings.FirstOrDefault()?.DeviceBinding;
            return binding != null && !binding.IsBound &&
                   string.Equals(mapping.MappingRoute, "Button to Button", StringComparison.OrdinalIgnoreCase);
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
            _batchCancellation?.Cancel();
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

        private DeviceConfiguration FindBatchOutput(DeviceBinding binding, DetectedInputControl detected,
            out DeviceBindingNode bindingNode)
        {
            bindingNode = null;
            if (detected?.Device == null) return null;

            // An explicitly selected destination remains the destination even
            // when the physical button used to select the control comes from another device.
            if (binding.DeviceConfigurationGuid != Guid.Empty)
            {
                var selected = Profile.GetDeviceConfiguration(DeviceIoType.Output, binding.DeviceConfigurationGuid);
                bindingNode = FindBatchControl(selected?.Device, DeviceIoType.Output, detected);
                return bindingNode == null ? null : selected;
            }

            // Prefer a configured output that represents the exact device being pressed.
            foreach (var candidate in Profile.GetDeviceConfigurationList(DeviceIoType.Output))
            {
                if (candidate?.Device == null ||
                    !DevicesManager.PersistedIdentityEquals(candidate.Device, detected.Device)) continue;
                var node = FindBatchControl(candidate.Device, DeviceIoType.Output, detected);
                if (node == null) continue;
                bindingNode = node;
                return candidate;
            }

            // Virtual outputs are not physical input endpoints. Resolve the equivalent
            // control on the primary output when no destination was selected.
            var primary = Profile.GetPrimaryDeviceConfiguration(DeviceIoType.Output);
            bindingNode = FindBatchControl(primary?.Device, DeviceIoType.Output, detected);
            return bindingNode == null ? null : primary;
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

        private async Task RunBatchSessionAsync(bool assign)
        {
            Context.BindingManager.CancelProfileBindMode(Profile);
            _batchRunning = true;
            var cancellation = new CancellationTokenSource();
            _batchCancellation = cancellation;
            var token = cancellation.Token;
            var completed = 0;
            ShowBatchOverlay();
            BatchTitle.Text = assign ? "Batch assign outputs" : "Batch capture inputs";

            try
            {
                var buttonPlugin = ProfileViewModel.PluginToolbox.PluginGroupList.Values
                    .SelectMany(group => group.Plugins)
                    .FirstOrDefault(plugin => string.Equals(plugin.Name, "Button to Button",
                        StringComparison.OrdinalIgnoreCase));

                if (!assign && buttonPlugin == null)
                    throw new InvalidOperationException("The Button to Button plugin is unavailable.");

                while (!token.IsCancellationRequested && CanEditBatchMappings &&
                       (!assign || completed < _batchMappings.Count))
                {
                    BatchStatus.Text = assign
                        ? "Press the output for " + _batchMappings[completed].MappingTitle + "."
                        : "Press a button on the input device. Each press creates a mapping.";
                    BatchProgress.Text = assign
                        ? (completed + 1) + " / " + _batchMappings.Count + " outputs"
                        : _batchMappings.Count + " inputs captured";

                    // Expiration simply re-arms the listener; Finish explicitly ends the session.
                    var detected = await Context.DevicesManager.DetectInputControlAsync(
                        DeviceBindingCategory.Momentary, TimeSpan.FromSeconds(20), token);
                    if (token.IsCancellationRequested || !CanEditBatchMappings) break;
                    if (detected == null) continue;

                    if (!assign)
                    {
                        var input = FindBatchInput(detected);
                        var node = FindBatchControl(input?.Device, DeviceIoType.Input, detected);
                        if (input == null || node == null)
                        {
                            BatchStatus.Text = "Add the detected device under profile INPUT devices first.";
                            await Task.Delay(600, token);
                            continue;
                        }

                        var mapping = ProfileViewModel.AddMappingToSelectedSection(
                            UniqueBatchMappingTitle(detected.ControlTitle));
                        if (mapping == null) break;
                        mapping.AddPlugin(buttonPlugin.Plugin);
                        var binding = mapping.DeviceBindings.FirstOrDefault()?.DeviceBinding;
                        if (binding == null)
                        {
                            ProfileViewModel.RemoveMapping(mapping);
                            throw new InvalidOperationException("Could not initialize the mapping input.");
                        }
                        binding.DeviceBindingCategory = DeviceBindingCategory.Momentary;
                        binding.SetDeviceConfigurationGuid(input.Guid);
                        binding.SetKeyTypeValue(node.DeviceBindingInfo.KeyType,
                            node.DeviceBindingInfo.KeyValue, node.DeviceBindingInfo.KeySubValue, node.Title);
                        mapping.RefreshCollapsedSummary();
                        _batchMappings.Add(mapping);
                        BatchStatus.Text = "Captured: " + detected.ControlTitle;
                    }
                    else
                    {
                        var mapping = _batchMappings[completed];
                        var output = mapping.Plugins.FirstOrDefault()?.DeviceBindings.FirstOrDefault();
                        if (output?.DeviceBinding == null)
                        {
                            BatchStatus.Text = "Mapping has no output binding: " + mapping.MappingTitle;
                            break;
                        }
                        DeviceBindingNode node;
                        var configuration = FindBatchOutput(output.DeviceBinding, detected, out node);
                        if (configuration == null || node == null)
                        {
                            BatchStatus.Text = "The destination has no equivalent for " + detected.ControlTitle +
                                               ". Press another control or finish to change the output device.";
                            await Task.Delay(700, token);
                            continue;
                        }

                        var binding = output.DeviceBinding;
                        binding.DeviceBindingCategory = DeviceBindingCategory.Momentary;
                        binding.SetDeviceConfigurationGuid(configuration.Guid);
                        binding.SetKeyTypeValue(node.DeviceBindingInfo.KeyType,
                            node.DeviceBindingInfo.KeyValue, node.DeviceBindingInfo.KeySubValue, node.Title);
                        output.RefreshDeviceList();
                        mapping.RefreshCollapsedSummary();
                        completed++;
                        BatchStatus.Text = "Assigned: " + mapping.MappingTitle + " -> " + node.Title;
                    }
                    await Task.Delay(110, token);
                }

                if (assign && completed == _batchMappings.Count && !token.IsCancellationRequested)
                {
                    BatchStatus.Text = "All " + completed + " outputs assigned.";
                    BatchProgress.Text = "Complete";
                    await Task.Delay(700, token);
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
