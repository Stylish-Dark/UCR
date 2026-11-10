using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using System.Text.RegularExpressions;
using HidWizards.UCR.Core.Annotations;
using HidWizards.UCR.Core.Managers;
using HidWizards.UCR.Core.Models;
using HidWizards.UCR.Core.Models.Binding;
using HidWizards.UCR.Core.Utilities;
using HidWizards.UCR.Views.Dialogs;
using HidWizards.UCR.ViewModels.Presentation;
using MaterialDesignThemes.Wpf;

namespace HidWizards.UCR.ViewModels.ProfileViewModels
{
    public partial class MappingViewModel
    {
        public DeviceBindingViewModel ResolveCollapsedBinding(BindingVisualDescriptor descriptor, DeviceIoType ioType)
        {
            if (descriptor == null || descriptor.BindingGuid == Guid.Empty) return null;
            IEnumerable<DeviceBindingViewModel> candidates = ioType == DeviceIoType.Input
                ? DeviceBindings
                : Plugins.SelectMany(plugin => plugin.DeviceBindings);
            return candidates.FirstOrDefault(candidate => candidate?.DeviceBinding != null &&
                candidate.DeviceBinding.Guid == descriptor.BindingGuid &&
                candidate.DeviceBinding.DeviceIoType == ioType);
        }

        public bool QuickBindInput(BindingVisualDescriptor descriptor)
        {
            if (!ButtonsEnabled) return false;
            var bindingViewModel = ResolveCollapsedBinding(descriptor, DeviceIoType.Input);
            if (bindingViewModel?.DeviceBinding == null || !bindingViewModel.BindingEnabled) return false;

            // The collapsed row is interactive before its editor exists. Initialise
            // the same device list the expanded editor loads before capture.
            bindingViewModel.EnsureDeviceListLoaded();
            // Button and analogue captures share a logical input expression.
            // Without this, an axis can bind but the old button-only plugin
            // will never evaluate its continuous/threshold source correctly.
            if (Mapping.SupportsNativeInputExpression())
                Mapping.EnableInputExpression();
            StopQuickInputDetection();
            var binding = bindingViewModel.DeviceBinding;
            var bindingManager = binding.Profile?.Context?.BindingManager;
            _quickInputBinding = binding;
            binding.PropertyChanged += QuickInputBindingOnPropertyChanged;
            if (bindingManager != null) bindingManager.PropertyChanged += QuickInputBindingManagerOnPropertyChanged;

            IsQuickInputDetecting = true;
            QuickInputDetectionStatus = "WAITING FOR INPUT — 5.0s";
            QuickInputDetectionProgress = 100;

            try
            {
                binding.DeviceBindingCategory = bindingViewModel.DeviceBindingCategory;
                // Respect the selected input representation: a button capture must never
                // accept unrelated analogue axes (notably noisy DualSense HID sensors).
                binding.EnterBindMode();
                if (!binding.IsInBindMode) StopQuickInputDetection();
                return binding.IsInBindMode;
            }
            catch
            {
                StopQuickInputDetection();
                throw;
            }
        }

        private void QuickInputBindingManagerOnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (!string.Equals(e.PropertyName, nameof(BindingManager.BindModeProgress), StringComparison.Ordinal)) return;
            if (_quickInputBinding == null || !_quickInputBinding.IsInBindMode) return;

            var bindingManager = sender as BindingManager;
            if (bindingManager == null) return;
            QuickInputDetectionProgress = bindingManager.BindModeProgress;
            QuickInputDetectionStatus = "WAITING FOR INPUT — " +
                                        Math.Max(0, bindingManager.BindModeProgress / 20.0).ToString("0.0") + "s";
        }

        private void QuickInputBindingOnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (!string.Equals(e.PropertyName, nameof(DeviceBinding.IsInBindMode), StringComparison.Ordinal)) return;
            var binding = sender as DeviceBinding;
            if (binding == null || !ReferenceEquals(binding, _quickInputBinding) || binding.IsInBindMode) return;
            StopQuickInputDetection();
        }

        private void StopQuickInputDetection()
        {
            var binding = _quickInputBinding;
            if (binding != null)
            {
                binding.PropertyChanged -= QuickInputBindingOnPropertyChanged;
                var bindingManager = binding.Profile?.Context?.BindingManager;
                if (bindingManager != null) bindingManager.PropertyChanged -= QuickInputBindingManagerOnPropertyChanged;
            }

            _quickInputBinding = null;
            IsQuickInputDetecting = false;
            QuickInputDetectionProgress = 0;
            QuickInputDetectionStatus = null;
        }

        public bool UsesPressCaptureForQuickOutput(BindingVisualDescriptor descriptor)
        {
            if (!ButtonsEnabled) return false;
            var bindingViewModel = ResolveCollapsedBinding(descriptor, DeviceIoType.Output);
            var configuration = ResolveQuickOutputConfiguration(bindingViewModel);
            if (configuration?.Device == null || bindingViewModel.DeviceBinding.DeviceConfigurationGuid == Guid.Empty)
                return false;

            return DeviceVisualCatalog.Describe(configuration, ProfileViewModel.Profile, DeviceIoType.Output).Kind ==
                   DeviceVisualKind.Keyboard;
        }

        public List<QuickBindingOption> GetQuickOutputBindingOptions(BindingVisualDescriptor descriptor)
        {
            var result = new List<QuickBindingOption>();
            if (!ButtonsEnabled) return result;

            var bindingViewModel = ResolveCollapsedBinding(descriptor, DeviceIoType.Output);
            if (bindingViewModel?.DeviceBinding == null || !bindingViewModel.BindingEnabled) return result;

            var configuration = ResolveQuickOutputConfiguration(bindingViewModel);
            if (configuration?.Device == null) return result;

            var menu = configuration.Device.GetDeviceBindingMenu(ProfileViewModel.Profile.Context, DeviceIoType.Output);
            var category = bindingViewModel.DeviceBindingCategory;
            var plugin = Plugins.FirstOrDefault(candidate => candidate.DeviceBindings.Contains(bindingViewModel));
            var restrictCategory = bindingViewModel.DeviceBinding.DeviceConfigurationGuid != Guid.Empty ||
                category == DeviceBindingCategory.Event || plugin?.DeviceBindings.Count > 1 || IsCompoundOutput;
            foreach (var node in FlattenBindingNodes(menu))
            {
                var info = node?.DeviceBindingInfo;
                if (info == null ||
                    (info.DeviceBindingCategory != category &&
                     (restrictCategory || info.DeviceBindingCategory == DeviceBindingCategory.Event))) continue;
                var temporary = new DeviceBinding
                {
                    Profile = ProfileViewModel.Profile,
                    DeviceIoType = DeviceIoType.Output,
                    DeviceBindingCategory = info.DeviceBindingCategory,
                    DeviceConfigurationGuid = configuration.Guid,
                    IsBound = true,
                    KeyType = info.KeyType,
                    KeyValue = info.KeyValue,
                    KeySubValue = info.KeySubValue
                };

                result.Add(new QuickBindingOption
                {
                    Title = node.Title,
                    DeviceConfigurationGuid = configuration.Guid,
                    OutputDevice = configuration.Device,
                    OutputControl = info,
                    KeyType = info.KeyType,
                    KeyValue = info.KeyValue,
                    KeySubValue = info.KeySubValue,
                    Visual = DeviceVisualCatalog.DescribeBinding(
                        temporary, info.DeviceBindingCategory, ProfileViewModel.Profile)
                });
            }

            return result;
        }

        public bool ApplyQuickOutputBinding(BindingVisualDescriptor descriptor, QuickBindingOption option)
        {
            if (!ButtonsEnabled || option == null) return false;
            var bindingViewModel = ResolveCollapsedBinding(descriptor, DeviceIoType.Output);
            if (bindingViewModel?.DeviceBinding == null || !bindingViewModel.BindingEnabled) return false;

            var configuration = ResolveQuickOutputConfiguration(bindingViewModel);
            if (configuration?.Device == null || configuration.Guid != option.DeviceConfigurationGuid ||
                !ReferenceEquals(configuration.Device, option.OutputDevice)) return false;

            var binding = bindingViewModel.DeviceBinding;
            if (binding.DeviceConfigurationGuid == Guid.Empty && option.OutputControl != null)
                return ApplyOutputControlChoice(bindingViewModel, configuration.Guid,
                    configuration.Device, option.OutputControl);

            binding.DeviceBindingCategory = bindingViewModel.DeviceBindingCategory;
            binding.SetDeviceConfigurationGuid(option.DeviceConfigurationGuid);
            binding.SetKeyTypeValue(option.KeyType, option.KeyValue, option.KeySubValue);
            bindingViewModel.RefreshDeviceList();
            RefreshCollapsedSummary();
            return true;
        }

        internal bool ApplyOutputControlChoice(DeviceBindingViewModel binding,
            Guid deviceGuid, Device outputDevice, DeviceBindingInfo selectedControl, int direction = 0)
        {
            if (!ButtonsEnabled || binding?.DeviceBinding == null || selectedControl == null)
                return false;

            var original = Plugins.FirstOrDefault(plugin =>
                plugin.DeviceBindings.Any(item => ReferenceEquals(item.DeviceBinding, binding.DeviceBinding)));
            if (original == null) return false;
            var index = Plugins.IndexOf(original);
            var outputIndex = original.Plugin.Outputs.IndexOf(binding.DeviceBinding);
            if (outputIndex < 0 || outputIndex >= original.Plugin.OutputCategories.Count) return false;
            var requested = selectedControl.DeviceBindingCategory;
            var currentCategory = original.Plugin.OutputCategories[outputIndex].Category;
            var isPositionalMultiOutput = original.Plugin.Outputs.Count > 1;
            if (isPositionalMultiOutput && currentCategory != requested) return false;
            var configuration = binding.ResolveOutputConfiguration();
            if (configuration?.Device == null || configuration.Guid != deviceGuid ||
                !ReferenceEquals(configuration.Device, outputDevice)) return false;
            var isTrigger = requested == DeviceBindingCategory.Range &&
                DeviceVisualCatalog.IsKnownTriggerAxis(configuration,
                    ProfileViewModel.Profile, DeviceIoType.Output, selectedControl.KeyValue);
            var selectedRepresentation = requested == DeviceBindingCategory.Momentary ? 3 :
                requested == DeviceBindingCategory.Range ? (isTrigger ? 4 : 2) : 0;
            if (selectedRepresentation == 0 && currentCategory != requested) return false;
            if (IsTypedDirectionalMapping && !isPositionalMultiOutput && selectedRepresentation != 0)
            {
                if (OutputIsFullStick)
                {
                    // A 4-Way destination is a complete bank. Selecting a single
                    // control cannot replace the whole bank with a one-sided type.
                    if (currentCategory != requested) return false;
                }
                else if (Mapping.EffectiveOutputRepresentation != selectedRepresentation)
                {
                    // Trigger and Button share the selector but remain distinct
                    // physical signal types, so never turn a trigger into an Axis.
                    var targetCapacity = Math.Max(
                        Mapping.EffectiveInputRepresentation == 1 ? 4 :
                        Mapping.EffectiveInputRepresentation == 2 ? 2 : 1,
                        selectedRepresentation == 2 ? 2 : 1);
                    if (index >= Mapping.RequiredBindingSlots(targetCapacity, selectedRepresentation) ||
                        !SetOutputRepresentation(selectedRepresentation)) return false;
                    original = Plugins[index];
                }
            }
            if (original.Plugin.OutputCategories[outputIndex].Category != requested)
            {
                var analogue = Mapping.DeviceBindings.Any(input => input.InputExpressionInputKind == 3);
                var preferredName = requested == DeviceBindingCategory.Range
                    ? (analogue ? "Axis to Axis" : "Button to Axis")
                    : requested == DeviceBindingCategory.Momentary
                        ? (analogue ? "Axis to Button" : "Button to Button") : null;
                if (preferredName == null) return false;
                var replacement = GetCompatiblePluginOptions().FirstOrDefault(option =>
                    string.Equals(option.Name, preferredName, StringComparison.OrdinalIgnoreCase));
                if (replacement == null || !ChangeOutputType(original, replacement.Plugin)) return false;
            }

            if (index < 0 || index >= Plugins.Count) return false;
            var destinationPlugin = Plugins[index];
            if (outputIndex >= destinationPlugin.DeviceBindings.Count) return false;
            var destination = destinationPlugin.DeviceBindings[outputIndex];
            var output = destination.DeviceBinding;
            output.DeviceBindingCategory = requested;
            output.SetDeviceConfigurationGuid(deviceGuid);
            output.SetKeyTypeValue(selectedControl.KeyType, selectedControl.KeyValue, selectedControl.KeySubValue);
            output.OutputAxisDirection = requested == DeviceBindingCategory.Range ? Math.Sign(direction) : 0;

            // A button maps to a signed analogue extreme for the chosen stick direction.
            // Neutral is restored when released; a full axis choice remains unrestricted.
            var pressed = destinationPlugin.Plugin.GetType().GetProperty("RangePressed");
            var released = destinationPlugin.Plugin.GetType().GetProperty("Range");
            if (pressed != null && pressed.PropertyType == typeof(double))
            {
                pressed.SetValue(destinationPlugin.Plugin, direction < 0 ? -100.0 : 100.0, null);
                if (released != null && released.PropertyType == typeof(double))
                    released.SetValue(destinationPlugin.Plugin, 0.0, null);
            }
            destination.RefreshDeviceList();
            RefreshCollapsedSummary();
            // A positional multi-output remapper keeps its existing plugin and sibling outputs.
            if (!isPositionalMultiOutput && destinationPlugin.Plugin.Outputs.Count == 1 &&
                (IsTypedDirectionalMapping || currentCategory != requested))
                InferOutputTypeFromAssignment(requested,
                    isTrigger || DeviceVisualCatalog.IsTriggerBinding(output, ProfileViewModel.Profile));
            if (requested == DeviceBindingCategory.Range && !isTrigger && direction == 0)
                EnsureButtonAxisEditor();
            ProfileViewModel.Profile.Context.ContextChanged();
            return true;
        }

        private DeviceConfiguration ResolveQuickOutputConfiguration(DeviceBindingViewModel bindingViewModel)
        {
            return bindingViewModel?.ResolveOutputConfiguration();
        }

        public async Task<bool> QuickBindOutputAsync(BindingVisualDescriptor descriptor)
        {
            if (!ButtonsEnabled) return false;

            if (IsQuickOutputDetecting)
            {
                _quickOutputDetectionCancellation?.Cancel();
                return false;
            }

            var bindingViewModel = ResolveCollapsedBinding(descriptor, DeviceIoType.Output);
            if (bindingViewModel?.DeviceBinding == null || !bindingViewModel.BindingEnabled) return false;

            var configuration = ResolveQuickOutputConfiguration(bindingViewModel);
            if (configuration?.Device == null)
            {
                QuickOutputDetectionStatus = "Choose an output device first.";
                return false;
            }

            var outputDevice = configuration.Device;
            var category = bindingViewModel.DeviceBindingCategory;
            var timeout = TimeSpan.FromSeconds(5);
            var cancellation = new CancellationTokenSource();
            _quickOutputDetectionCancellation = cancellation;
            StartQuickOutputCountdown(timeout);

            try
            {
                var detected = await ProfileViewModel.Profile.Context.DevicesManager.DetectInputControlAsync(
                    category, timeout, cancellation.Token);

                if (cancellation.IsCancellationRequested)
                {
                    QuickOutputDetectionStatus = "Input capture cancelled.";
                    return false;
                }
                if (detected == null)
                {
                    QuickOutputDetectionStatus = "No input detected.";
                    return false;
                }

                var currentBinding = ResolveCollapsedBinding(descriptor, DeviceIoType.Output);
                var currentConfiguration = ResolveQuickOutputConfiguration(currentBinding);
                if (!ButtonsEnabled || !ReferenceEquals(currentBinding, bindingViewModel) ||
                    !bindingViewModel.BindingEnabled || bindingViewModel.DeviceBindingCategory != category ||
                    currentConfiguration?.Guid != configuration.Guid ||
                    !ReferenceEquals(currentConfiguration.Device, outputDevice))
                {
                    QuickOutputDetectionStatus = "Output selection changed during capture.";
                    return false;
                }

                return ApplyDetectedOutputControl(bindingViewModel, currentConfiguration, detected);
            }
            catch (InvalidOperationException exception)
            {
                QuickOutputDetectionStatus = exception.Message;
                return false;
            }
            catch (Exception exception)
            {
                Logger.Error("Quick output binding failed", exception);
                QuickOutputDetectionStatus = "Input capture failed.";
                return false;
            }
            finally
            {
                StopQuickOutputCountdown();
                cancellation.Dispose();
                if (ReferenceEquals(_quickOutputDetectionCancellation, cancellation))
                    _quickOutputDetectionCancellation = null;
            }
        }

        private bool ApplyDetectedOutputControl(DeviceBindingViewModel bindingViewModel,
            DeviceConfiguration outputConfiguration, DetectedInputControl detected)
        {
            if (bindingViewModel?.DeviceBinding == null || outputConfiguration?.Device == null || detected == null)
                return false;

            // A press on an input device is only a selector. Resolve the same named control on the
            // already-selected output device, then bind that output control. The input device itself
            // is deliberately ignored here and can never replace the output-device configuration.
            var outputNode = FlattenBindingNodes(outputConfiguration.Device.GetDeviceBindingMenu(ProfileViewModel.Profile.Context, DeviceIoType.Output))
                .FirstOrDefault(node => node?.DeviceBindingInfo != null &&
                                        node.DeviceBindingInfo.DeviceBindingCategory == bindingViewModel.DeviceBindingCategory &&
                                        string.Equals(node.Title, detected.ControlTitle, StringComparison.CurrentCultureIgnoreCase));

            if (outputNode?.DeviceBindingInfo == null)
            {
                QuickOutputDetectionStatus = string.IsNullOrWhiteSpace(detected.ControlTitle)
                    ? "That input has no matching output control."
                    : "No matching output control named " + detected.ControlTitle + ".";
                return false;
            }

            var info = outputNode.DeviceBindingInfo;
            var binding = bindingViewModel.DeviceBinding;
            binding.DeviceBindingCategory = bindingViewModel.DeviceBindingCategory;
            binding.SetDeviceConfigurationGuid(outputConfiguration.Guid);
            binding.SetKeyTypeValue(info.KeyType, info.KeyValue, info.KeySubValue);
            bindingViewModel.RefreshDeviceList();
            RefreshCollapsedSummary();
            QuickOutputDetectionStatus = "Bound output: " + outputNode.Title;
            return true;
        }

        private void StartQuickOutputCountdown(TimeSpan timeout)
        {
            _quickOutputDetectionDeadlineUtc = DateTime.UtcNow.Add(timeout);
            IsQuickOutputDetecting = true;
            UpdateQuickOutputCountdown();

            _quickOutputDetectionTimer?.Stop();
            _quickOutputDetectionTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _quickOutputDetectionTimer.Tick += QuickOutputDetectionTimerOnTick;
            _quickOutputDetectionTimer.Start();
        }

        private void QuickOutputDetectionTimerOnTick(object sender, EventArgs e)
        {
            UpdateQuickOutputCountdown();
        }

        private void UpdateQuickOutputCountdown()
        {
            var remaining = Math.Max(0, (_quickOutputDetectionDeadlineUtc - DateTime.UtcNow).TotalSeconds);
            QuickOutputDetectionStatus = "Press an input — " + remaining.ToString("0.0") + "s";
            QuickOutputDetectionProgress = Math.Max(0, Math.Min(100, remaining / 5.0 * 100.0));
        }

        private void StopQuickOutputCountdown()
        {
            if (_quickOutputDetectionTimer != null)
            {
                _quickOutputDetectionTimer.Stop();
                _quickOutputDetectionTimer.Tick -= QuickOutputDetectionTimerOnTick;
                _quickOutputDetectionTimer = null;
            }
            IsQuickOutputDetecting = false;
            QuickOutputDetectionProgress = 0;
        }

        private static IEnumerable<DeviceBindingNode> FlattenBindingNodes(IEnumerable<DeviceBindingNode> nodes)
        {
            if (nodes == null) yield break;
            foreach (var node in nodes)
            {
                if (node == null) continue;
                if (node.IsBinding) yield return node;
                foreach (var child in FlattenBindingNodes(node.ChildrenNodes)) yield return child;
            }
        }

        // Build preview rows from the actual bindings. InputConditions is a presentation
        // collection and may be empty for plugin types without native expressions.
        // Never truncate the preview at the first three bindings.
    }
}
