using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
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
    public sealed class MappingHeaderToken
    {
        public string Text { get; }
        public string TypeLabel { get; }

        public MappingHeaderToken(string text, string typeLabel)
        {
            Text = text;
            TypeLabel = typeLabel;
        }
    }

    public sealed class FilterReferenceBadge
    {
        public string Name { get; set; }
        public string ToolTip { get; set; }
    }

    public class MappingViewModel : INotifyPropertyChanged, IDisposable
    {
        public string MappingTitle => Mapping.FullTitle;
        public ProfileViewModel ProfileViewModel { get; }
        public Mapping Mapping { get; set; }
        public ObservableCollection<PluginViewModel> Plugins { get; set; }
        public ObservableCollection<DeviceBindingViewModel> DeviceBindings { get; set; }
        public bool ButtonsEnabled => !ProfileViewModel.Profile.IsActive();
        public bool CanMoveUp => ButtonsEnabled && ProfileViewModel.MappingsList != null && ProfileViewModel.MappingsList.IndexOf(this) > 0;
        public bool CanMoveDown => ButtonsEnabled && ProfileViewModel.MappingsList != null &&
                                   ProfileViewModel.MappingsList.IndexOf(this) >= 0 &&
                                   ProfileViewModel.MappingsList.IndexOf(this) < ProfileViewModel.MappingsList.Count - 1;
        public string MappingRoute => Mapping != null && Mapping.Plugins.Count > 0 ? Mapping.Plugins[0].PluginName : "No plugin";
        public string MappingRouteDisplay => FormatMappingRoute(MappingRoute);
        public List<MappingHeaderToken> MappingRouteTokens => BuildMappingRouteTokens(MappingRouteDisplay);
        public string MappingOutputTypeLabel => GetMappingOutputTypeLabel();
        public string DefinedFilterName => GetDefinedFilterName();
        public List<FilterReferenceBadge> ReferencedFilters => BuildReferencedFilters();
        public bool HasFilterReferences => ReferencedFilters.Count > 0;
        public bool HasFilters => Mapping != null && Mapping.Plugins != null &&
                                  Mapping.Plugins.Any(plugin => plugin.Filters != null && plugin.Filters.Count > 0);
        public string CollapsedSummary => BuildCollapsedSummary();
        public List<BindingVisualDescriptor> CollapsedInputVisuals => BuildCollapsedInputVisuals();
        public List<BindingVisualDescriptor> CollapsedOutputVisuals => BuildCollapsedOutputVisuals();

        private bool _isFilterDefinitionHighlighted;
        public bool IsFilterDefinitionHighlighted
        {
            get => _isFilterDefinitionHighlighted;
            private set
            {
                if (_isFilterDefinitionHighlighted == value) return;
                _isFilterDefinitionHighlighted = value;
                OnPropertyChanged();
            }
        }

        private bool _isFilterReferenceHighlighted;
        public bool IsFilterReferenceHighlighted
        {
            get => _isFilterReferenceHighlighted;
            private set
            {
                if (_isFilterReferenceHighlighted == value) return;
                _isFilterReferenceHighlighted = value;
                OnPropertyChanged();
            }
        }

        private bool _isDragging;
        public bool IsDragging
        {
            get => _isDragging;
            set
            {
                if (_isDragging == value) return;
                _isDragging = value;
                OnPropertyChanged();
            }
        }

        private CancellationTokenSource _quickOutputDetectionCancellation;
        private DispatcherTimer _quickOutputDetectionTimer;
        private DateTime _quickOutputDetectionDeadlineUtc;
        private bool _isQuickOutputDetecting;
        private string _quickOutputDetectionStatus;
        private double _quickOutputDetectionProgress;

        public bool IsQuickOutputDetecting
        {
            get => _isQuickOutputDetecting;
            private set
            {
                if (_isQuickOutputDetecting == value) return;
                _isQuickOutputDetecting = value;
                OnPropertyChanged();
            }
        }

        public string QuickOutputDetectionStatus
        {
            get => _quickOutputDetectionStatus;
            private set
            {
                if (_quickOutputDetectionStatus == value) return;
                _quickOutputDetectionStatus = value;
                OnPropertyChanged();
            }
        }

        public double QuickOutputDetectionProgress
        {
            get => _quickOutputDetectionProgress;
            private set
            {
                if (Math.Abs(_quickOutputDetectionProgress - value) < 0.01) return;
                _quickOutputDetectionProgress = value;
                OnPropertyChanged();
            }
        }

        private bool _disposed;
        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded == value) return;
                _isExpanded = value;
                OnPropertyChanged();
            }
        }

        public MappingViewModel(ProfileViewModel profileViewModel, Mapping mapping)
        {
            ProfileViewModel = profileViewModel;
            Mapping = mapping;
            IsExpanded = false;
            profileViewModel.Profile.Context.ActiveProfileChangedEvent += ContextOnActiveProfileChangedEvent;
            DeviceBindings = new ObservableCollection<DeviceBindingViewModel>();
            PopulateDeviceBindingsViewModels();
            PopulatePlugins(mapping);
            SubscribeSummaryBindings();
        }

        private void ContextOnActiveProfileChangedEvent(Profile profile)
        {
            OnPropertyChanged(nameof(ButtonsEnabled));
            OnPropertyChanged(nameof(CanMoveUp));
            OnPropertyChanged(nameof(CanMoveDown));
        }

        public void RefreshPositionState()
        {
            OnPropertyChanged(nameof(CanMoveUp));
            OnPropertyChanged(nameof(CanMoveDown));
        }

        public void RefreshCollapsedSummary()
        {
            OnPropertyChanged(nameof(CollapsedSummary));
            OnPropertyChanged(nameof(CollapsedInputVisuals));
            OnPropertyChanged(nameof(CollapsedOutputVisuals));
            OnPropertyChanged(nameof(ReferencedFilters));
            OnPropertyChanged(nameof(HasFilterReferences));
        }

        public void RefreshTitle()
        {
            OnPropertyChanged(nameof(MappingTitle));
        }

        private void SubscribeSummaryBindings()
        {
            foreach (var binding in DeviceBindings) SubscribeSummaryBinding(binding);
            foreach (var plugin in Plugins)
            {
                foreach (var binding in plugin.DeviceBindings) SubscribeSummaryBinding(binding);
            }
        }

        private void SubscribeSummaryBinding(DeviceBindingViewModel binding)
        {
            if (binding == null) return;
            binding.PropertyChanged -= SummaryBindingOnPropertyChanged;
            binding.PropertyChanged += SummaryBindingOnPropertyChanged;
        }

        private void SummaryBindingOnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            RefreshCollapsedSummary();
        }

        public void MoveUp()
        {
            ProfileViewModel.MoveMapping(this, -1);
        }

        public void MoveDown()
        {
            ProfileViewModel.MoveMapping(this, 1);
        }

        public void Remove()
        {
            ProfileViewModel.RemoveMapping(this);
        }

        public void AddPlugin(Plugin plugin)
        {
            var newPlugin = ProfileViewModel.Profile.Context.PluginManager.GetNewPlugin(plugin);
            if (!Mapping.AddPlugin(newPlugin)) return;

            var pluginViewModel = new PluginViewModel(this, newPlugin);
            Plugins.Add(pluginViewModel);
            foreach (var binding in pluginViewModel.DeviceBindings) SubscribeSummaryBinding(binding);
            RefreshHeaderState();
            RefreshCollapsedSummary();
            ProfileViewModel.RefreshFilterReferenceLabels();
            Logger.Info("Output added to mapping '" + MappingTitle + "': " + newPlugin.PluginName);
            if (Plugins.Count != 1) return;
            
            PopulateDeviceBindingsViewModels();
            foreach (var binding in DeviceBindings) SubscribeSummaryBinding(binding);
            RefreshCollapsedSummary();
        }

        private void PopulateDeviceBindingsViewModels()
        {
            if (Mapping.Plugins.Count == 0) return;

            var plugin = Mapping.Plugins[0];
            for (var i = 0; i < plugin.InputCategories.Count; i++)
            {
                DeviceBindings.Add(new DeviceBindingViewModel(Mapping.DeviceBindings[i])
                {
                    DeviceBindingName = plugin.InputCategories[i].Name,
                    DeviceBindingCategory = plugin.InputCategories[i].Category
                });
            }
        }

        public void RemovePlugin(PluginViewModel pluginViewModel)
        {
            if (!Mapping.RemovePlugin(pluginViewModel.Plugin)) return;

            pluginViewModel.Dispose();
            Plugins.Remove(pluginViewModel);
            RefreshHeaderState();
            RefreshCollapsedSummary();
            ProfileViewModel.RefreshFilterNames();
            ProfileViewModel.RefreshFilterReferenceLabels();
            if (Plugins.Count == 0) DeviceBindings.Clear();
        }

        private void PopulatePlugins(Mapping mapping)
        {
            Plugins = new ObservableCollection<PluginViewModel>();
            foreach (var mappingPlugin in mapping.Plugins)
            {
                Plugins.Add(new PluginViewModel(this, mappingPlugin));
            }
        }

        public void RefreshFilterIndicator()
        {
            OnPropertyChanged(nameof(HasFilters));
            OnPropertyChanged(nameof(ReferencedFilters));
            OnPropertyChanged(nameof(HasFilterReferences));
            OnPropertyChanged(nameof(DefinedFilterName));
            OnPropertyChanged(nameof(MappingOutputTypeLabel));
        }

        private void RefreshHeaderState()
        {
            OnPropertyChanged(nameof(MappingRoute));
            OnPropertyChanged(nameof(MappingRouteDisplay));
            OnPropertyChanged(nameof(MappingRouteTokens));
            OnPropertyChanged(nameof(MappingOutputTypeLabel));
            OnPropertyChanged(nameof(DefinedFilterName));
            OnPropertyChanged(nameof(ReferencedFilters));
            OnPropertyChanged(nameof(HasFilterReferences));
            OnPropertyChanged(nameof(HasFilters));
        }

        private static string FormatMappingRoute(string route)
        {
            if (string.IsNullOrWhiteSpace(route)) return route;
            return Regex.Replace(route.Trim(), @"\s+to\s+", " → ", RegexOptions.IgnoreCase);
        }

        private static List<MappingHeaderToken> BuildMappingRouteTokens(string route)
        {
            var result = new List<MappingHeaderToken>();
            if (string.IsNullOrEmpty(route)) return result;

            var parts = Regex.Split(route, @"\b(Button|Buttons|Axis|Axes|Filter|Event|Events|Delta|Deltas|Value|Values|Multiple|None)\b", RegexOptions.IgnoreCase);
            foreach (var part in parts)
            {
                if (string.IsNullOrEmpty(part)) continue;
                result.Add(new MappingHeaderToken(part, IsMappingTypeWord(part) ? part : null));
            }
            return result;
        }

        private static bool IsMappingTypeWord(string value)
        {
            switch (value.ToLowerInvariant())
            {
                case "button":
                case "buttons":
                case "axis":
                case "axes":
                case "filter":
                case "event":
                case "events":
                case "delta":
                case "deltas":
                case "value":
                case "values":
                case "multiple":
                case "none":
                    return true;
                default:
                    return false;
            }
        }


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

            bindingViewModel.DeviceBinding.DeviceBindingCategory = bindingViewModel.DeviceBindingCategory;
            bindingViewModel.DeviceBinding.EnterBindMode();
            return true;
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

            var binding = bindingViewModel.DeviceBinding;
            var configurationGuid = binding.DeviceConfigurationGuid;
            if (configurationGuid == Guid.Empty && bindingViewModel.SelectedDevice != null)
                configurationGuid = bindingViewModel.SelectedDevice.Value;
            if (configurationGuid == Guid.Empty)
                configurationGuid = ProfileViewModel.Profile.GetPrimaryDeviceConfiguration(DeviceIoType.Output)?.Guid ?? Guid.Empty;

            var configuration = ProfileViewModel.Profile.GetDeviceConfiguration(DeviceIoType.Output, configurationGuid);
            if (configuration?.Device == null)
            {
                QuickOutputDetectionStatus = "Choose an output device first.";
                return false;
            }

            var timeout = TimeSpan.FromSeconds(5);
            var cancellation = new CancellationTokenSource();
            _quickOutputDetectionCancellation = cancellation;
            StartQuickOutputCountdown(timeout);

            try
            {
                var detected = await ProfileViewModel.Profile.Context.DevicesManager.DetectInputControlAsync(
                    bindingViewModel.DeviceBindingCategory, timeout, cancellation.Token);

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

                return ApplyDetectedOutputControl(bindingViewModel, configuration, detected);
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
            var outputNode = FlattenBindingNodes(ProfileViewModel.Profile.Context.DevicesManager.GetDeviceBindingMenu(
                    outputConfiguration.Device, DeviceIoType.Output))
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
            _quickOutputDetectionTimer = new DispatcherTimer(DispatcherPriority.Render)
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

        private List<BindingVisualDescriptor> BuildCollapsedInputVisuals()
        {
            var result = new List<BindingVisualDescriptor>();
            foreach (var binding in DeviceBindings.Take(3))
            {
                result.Add(DescribeCollapsedBinding(binding));
            }

            if (result.Count == 0)
            {
                result.Add(DeviceVisualCatalog.DescribeBinding(null, DeviceBindingCategory.Momentary, ProfileViewModel.Profile));
            }
            return result;
        }

        private List<BindingVisualDescriptor> BuildCollapsedOutputVisuals()
        {
            var result = new List<BindingVisualDescriptor>();
            // The card summary represents the mapping's primary route, which is the first
            // plugin (the same plugin used by MappingRoute). Additional plugins remain visible
            // when expanded and through filter/reference indicators, but must not replace the
            // primary output shown in the collapsed header.
            var primaryPlugin = Plugins.FirstOrDefault();
            if (primaryPlugin != null)
            {
                foreach (var binding in primaryPlugin.DeviceBindings.Take(3))
                {
                    result.Add(DescribeCollapsedBinding(binding));
                }

                if (result.Count == 0)
                {
                    var filterName = primaryPlugin.Plugin.GetDefinedFilterName();
                    if (!string.IsNullOrWhiteSpace(filterName)) result.Add(DeviceVisualCatalog.Filter(filterName));
                }
            }

            if (result.Count == 0)
            {
                result.Add(DeviceVisualCatalog.DescribeBinding(null, DeviceBindingCategory.Momentary, ProfileViewModel.Profile));
            }
            return result;
        }

        private BindingVisualDescriptor DescribeCollapsedBinding(DeviceBindingViewModel viewModel)
        {
            if (viewModel == null) return DeviceVisualCatalog.DescribeBinding(null, DeviceBindingCategory.Momentary, ProfileViewModel.Profile);
            var descriptor = DeviceVisualCatalog.DescribeBinding(
                viewModel.DeviceBinding, viewModel.DeviceBindingCategory, ProfileViewModel.Profile);
            var binding = viewModel.DeviceBinding;
            if (binding == null || !descriptor.IsBound || descriptor.Device == null) return descriptor;

            var primary = ProfileViewModel.Profile.GetPrimaryDeviceConfiguration(binding.DeviceIoType);
            descriptor.ShowDeviceBadge = primary == null || primary.Guid != binding.DeviceConfigurationGuid;
            return descriptor;
        }

        private string BuildCollapsedSummary()
        {
            var inputs = DeviceBindings.Select(DescribeBinding).Where(value => !string.IsNullOrWhiteSpace(value)).ToList();
            var outputs = new List<string>();

            var primaryPlugin = Plugins.FirstOrDefault();
            if (primaryPlugin != null)
            {
                outputs.AddRange(primaryPlugin.DeviceBindings.Select(DescribeBinding).Where(value => !string.IsNullOrWhiteSpace(value)));
                if (outputs.Count == 0)
                {
                    var filterName = primaryPlugin.Plugin.GetDefinedFilterName();
                    if (!string.IsNullOrWhiteSpace(filterName)) outputs.Add("Filter: " + filterName);
                }
            }

            var inputText = inputs.Count == 0 ? "No input bound" : JoinCompact(inputs);
            var outputText = outputs.Count == 0 ? "No output bound" : JoinCompact(outputs);
            return inputText + "  →  " + outputText;
        }

        private string DescribeBinding(DeviceBindingViewModel viewModel)
        {
            if (viewModel == null || viewModel.DeviceBinding == null) return null;
            var binding = viewModel.DeviceBinding;
            var configuration = ProfileViewModel.Profile.GetDeviceConfiguration(binding.DeviceIoType, binding.DeviceConfigurationGuid);
            var deviceName = configuration?.GetFullTitleForProfile(ProfileViewModel.Profile);
            if (string.IsNullOrWhiteSpace(deviceName)) deviceName = binding.DeviceIoType == DeviceIoType.Input ? "Input device" : "Output device";

            var controlName = binding.IsBound ? binding.BoundName() : "unbound";
            return Truncate(deviceName, 22) + ": " + Truncate(controlName, 24);
        }

        private static string JoinCompact(IList<string> values)
        {
            if (values.Count == 1) return values[0];
            if (values.Count == 2) return values[0] + " + " + values[1];
            return values[0] + " + " + (values.Count - 1) + " more";
        }

        private static string Truncate(string value, int maximumLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maximumLength) return value;
            return value.Substring(0, maximumLength - 1) + "…";
        }

        private string GetMappingOutputTypeLabel()
        {
            var plugin = Mapping?.Plugins?.FirstOrDefault();
            if (plugin == null) return "None";

            if (!string.IsNullOrWhiteSpace(plugin.GetDefinedFilterName())) return "Filter";
            if (plugin.OutputCategories == null || plugin.OutputCategories.Count == 0)
            {
                return string.Equals(plugin.Group, "Filter", StringComparison.OrdinalIgnoreCase) ? "Filter" : "None";
            }

            var category = plugin.OutputCategories[0].Category;
            for (var i = 1; i < plugin.OutputCategories.Count; i++)
            {
                if (plugin.OutputCategories[i].Category != category) return "Multiple";
            }

            switch (category)
            {
                case DeviceBindingCategory.Momentary: return "Button";
                case DeviceBindingCategory.Range: return "Axis";
                case DeviceBindingCategory.Event: return "Event";
                case DeviceBindingCategory.Delta: return "Delta";
                default: return "Value";
            }
        }

        private string GetDefinedFilterName()
        {
            if (Mapping?.Plugins == null) return null;
            foreach (var plugin in Mapping.Plugins)
            {
                var name = plugin.GetDefinedFilterName();
                if (!string.IsNullOrWhiteSpace(name)) return name.Trim();
            }
            return null;
        }

        private List<FilterReferenceBadge> BuildReferencedFilters()
        {
            var result = new List<FilterReferenceBadge>();
            if (Mapping?.Plugins == null) return result;
            var seen = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
            foreach (var plugin in Mapping.Plugins)
            {
                if (plugin.Filters == null) continue;
                foreach (var filter in plugin.Filters)
                {
                    if (filter == null || string.IsNullOrWhiteSpace(filter.Name)) continue;
                    var name = filter.Name.Trim();
                    if (!seen.Add(name)) continue;
                    result.Add(new FilterReferenceBadge
                    {
                        Name = name,
                        ToolTip = filter.Negative
                            ? "Uses filter '" + name + "' (inverted)"
                            : "Uses filter '" + name + "'"
                    });
                }
            }
            return result;
        }

        public bool DefinesFilter(string filterName)
        {
            if (string.IsNullOrWhiteSpace(filterName) || Mapping?.Plugins == null) return false;
            foreach (var plugin in Mapping.Plugins)
            {
                if (string.Equals(plugin.GetDefinedFilterName(), filterName, StringComparison.CurrentCultureIgnoreCase)) return true;
            }
            return false;
        }

        public bool ReferencesFilter(string filterName)
        {
            if (string.IsNullOrWhiteSpace(filterName) || Mapping?.Plugins == null) return false;
            foreach (var plugin in Mapping.Plugins)
            {
                if (plugin.Filters == null) continue;
                foreach (var filter in plugin.Filters)
                {
                    if (filter != null && string.Equals(filter.Name, filterName, StringComparison.CurrentCultureIgnoreCase)) return true;
                }
            }
            return false;
        }

        public void SetFilterHighlight(bool definition, bool reference)
        {
            IsFilterDefinitionHighlighted = definition;
            IsFilterReferenceHighlighted = reference;
        }

        public async void Rename()
        {
            var dialog = new StringDialog("Rename mapping", "Mapping name", Mapping.Title);
            var result = (bool?)await DialogHost.Show(dialog, ProfileViewModel.ProfileDialogIdentifier);
            if (result == null || !result.Value) return;

            var oldTitle = Mapping.Title;
            Mapping.Rename(dialog.Value);
            Logger.Info("Mapping renamed: '" + oldTitle + "' -> '" + Mapping.Title + "'");
            OnPropertyChanged(nameof(MappingTitle));
        }


        public List<SimplePluginViewModel> GetCompatiblePluginOptions()
        {
            return Mapping.GetPluginList()
                .Select(plugin => new SimplePluginViewModel(plugin))
                .OrderBy(plugin => plugin.OutputTypeOrder)
                .ThenBy(plugin => plugin.OutputType, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(plugin => plugin.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _quickOutputDetectionCancellation?.Cancel();
            StopQuickOutputCountdown();
            ProfileViewModel.Profile.Context.ActiveProfileChangedEvent -= ContextOnActiveProfileChangedEvent;

            foreach (var binding in DeviceBindings ?? new ObservableCollection<DeviceBindingViewModel>())
            {
                binding.PropertyChanged -= SummaryBindingOnPropertyChanged;
                binding.Dispose();
            }

            foreach (var plugin in Plugins ?? new ObservableCollection<PluginViewModel>())
            {
                foreach (var binding in plugin.DeviceBindings) binding.PropertyChanged -= SummaryBindingOnPropertyChanged;
                plugin.Dispose();
            }
        }

        public async void AddPlugin()
        {
            var dialog = new AddMappingPluginDialog(this);
            var result = (AddMappingPluginDialogViewModel)await DialogHost.Show(dialog, ProfileViewModel.ProfileDialogIdentifier);
            if (result?.SelectedPlugin == null) return;

            AddPlugin(result.SelectedPlugin.Plugin);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
