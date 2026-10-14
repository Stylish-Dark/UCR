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

    public sealed class FourWayOutputDeviceOption
    {
        public Guid Guid { get; set; }
        public string Title { get; set; }
    }

    public sealed class FourWayOutputChoice
    {
        public Guid DeviceConfigurationGuid { get; set; }
        public string DeviceTitle { get; set; }
        public string Destination { get; set; }
        public bool IsDPad { get; set; }
        // Input/output key descriptors are resolved before any binding is changed.
        public DeviceBindingInfo[] Controls { get; set; }
    }

    public sealed class QuickBindingOption
    {
        public string Title { get; set; }
        public Guid DeviceConfigurationGuid { get; set; }
        public int KeyType { get; set; }
        public int KeyValue { get; set; }
        public int KeySubValue { get; set; }
        public BindingVisualDescriptor Visual { get; set; }
    }

    public sealed class InputConditionViewModel
    {
        public int GroupIndex { get; }
        public ObservableCollection<DeviceBindingViewModel> Inputs { get; }
        public Visibility OrLabelVisibility => GroupIndex > 0 ? Visibility.Visible : Visibility.Collapsed;
        public bool CanRemoveCondition { get; }

        public InputConditionViewModel(int groupIndex, IEnumerable<DeviceBindingViewModel> inputs, bool canRemoveCondition)
        {
            GroupIndex = groupIndex;
            Inputs = new ObservableCollection<DeviceBindingViewModel>(inputs ?? Enumerable.Empty<DeviceBindingViewModel>());
            CanRemoveCondition = canRemoveCondition;
        }
    }

    public sealed class CollapsedConditionVisualRow
    {
        public List<BindingVisualDescriptor> Visuals { get; set; }
        public bool StartsAlternative { get; set; }
        public Visibility AlternativeDividerVisibility =>
            StartsAlternative ? Visibility.Visible : Visibility.Collapsed;
    }

    public class MappingViewModel : INotifyPropertyChanged, IDisposable
    {
        public string MappingTitle => Mapping.FullTitle;
        public ProfileViewModel ProfileViewModel { get; }
        public Mapping Mapping { get; set; }
        public ObservableCollection<PluginViewModel> Plugins { get; set; }
        public ObservableCollection<DeviceBindingViewModel> DeviceBindings { get; set; }
        public ObservableCollection<InputConditionViewModel> InputConditions { get; private set; }
        public bool ButtonsEnabled => !ProfileViewModel.Profile.IsActive();
        public bool SupportsNativeInputExpression => Mapping?.SupportsNativeInputExpression() == true;
        public bool UsesInputExpression => Mapping?.UseInputExpression == true;
        public string InputLogicSummary => BuildInputLogicSummary();
        public bool CanMoveUp => ButtonsEnabled && ProfileViewModel.CanMoveMapping(this, -1);
        public bool CanMoveDown => ButtonsEnabled && ProfileViewModel.CanMoveMapping(this, 1);
        public string MappingRoute => Mapping != null && Mapping.Plugins.Count > 0 ? Mapping.Plugins[0].PluginName : "No plugin";
        public string MappingRouteDisplay => FormatMappingRoute(MappingRoute, Mapping?.DeviceBindings?.Count ?? 0, Plugins?.Count ?? 0);
        public List<MappingHeaderToken> MappingRouteTokens => _mappingRouteTokens;
        public string MappingOutputTypeLabel => _mappingOutputTypeLabel;
        public string MappingOutputCategoryDisplay
        {
            get
            {
                var category = Plugins?.FirstOrDefault()?.OutputType ?? "None";
                if (string.Equals(category, "Button", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(category, "Buttons", StringComparison.OrdinalIgnoreCase))
                    return (Plugins?.Count ?? 0) == 1 ? "Button" : "Buttons";
                return category;
            }
        }
        public string DefinedFilterName => _definedFilterName;
        public List<FilterReferenceBadge> ReferencedFilters => _referencedFilters;
        public bool HasFilterReferences => _referencedFilters.Count > 0;
        public string FilterIndicatorToolTip => _filterIndicatorToolTip;
        public bool HasFilters => Mapping != null && Mapping.Plugins != null &&
                                  Mapping.Plugins.Any(plugin => plugin.Filters != null && plugin.Filters.Count > 0);
        public string CollapsedSummary => _collapsedSummary;
        public List<BindingVisualDescriptor> CollapsedInputVisuals => _collapsedInputVisuals;
        public List<CollapsedConditionVisualRow> CollapsedInputRows => _collapsedInputRows;
        public List<CollapsedConditionVisualRow> CollapsedOutputRows => _collapsedOutputRows;
        public List<BindingVisualDescriptor> CollapsedOutputVisuals => _collapsedOutputVisuals;

        private List<MappingHeaderToken> _mappingRouteTokens = new List<MappingHeaderToken>();
        private string _mappingOutputTypeLabel = "None";
        private string _definedFilterName;
        private List<FilterReferenceBadge> _referencedFilters = new List<FilterReferenceBadge>();
        private string _filterIndicatorToolTip;
        private string _collapsedSummary;
        private List<BindingVisualDescriptor> _collapsedInputVisuals = new List<BindingVisualDescriptor>();
        private List<CollapsedConditionVisualRow> _collapsedInputRows = new List<CollapsedConditionVisualRow>();
        private List<CollapsedConditionVisualRow> _collapsedOutputRows = new List<CollapsedConditionVisualRow>();
        private List<BindingVisualDescriptor> _collapsedOutputVisuals = new List<BindingVisualDescriptor>();

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

        private DeviceBinding _quickInputBinding;
        private bool _isQuickInputDetecting;
        private string _quickInputDetectionStatus;
        private double _quickInputDetectionProgress;

        public bool IsQuickInputDetecting
        {
            get => _isQuickInputDetecting;
            private set
            {
                if (_isQuickInputDetecting == value) return;
                _isQuickInputDetecting = value;
                OnPropertyChanged();
            }
        }

        public string QuickInputDetectionStatus
        {
            get => _quickInputDetectionStatus;
            private set
            {
                if (_quickInputDetectionStatus == value) return;
                _quickInputDetectionStatus = value;
                OnPropertyChanged();
            }
        }

        public double QuickInputDetectionProgress
        {
            get => _quickInputDetectionProgress;
            private set
            {
                if (Math.Abs(_quickInputDetectionProgress - value) < 0.01) return;
                _quickInputDetectionProgress = value;
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
            DeviceBindings = new ObservableCollection<DeviceBindingViewModel>();
            InputConditions = new ObservableCollection<InputConditionViewModel>();
            PopulateDeviceBindingsViewModels();
            PopulatePlugins(mapping);
            SubscribeSummaryBindings();
            RefreshHeaderState();
            RefreshCollapsedSummary();
        }

        internal void RefreshActiveState()
        {
            if (_disposed) return;

            OnPropertyChanged(nameof(ButtonsEnabled));
            OnPropertyChanged(nameof(CanMoveUp));
            OnPropertyChanged(nameof(CanMoveDown));
            foreach (var binding in DeviceBindings) binding.RefreshActiveState();
            foreach (var plugin in Plugins) plugin.RefreshActiveState();
        }

        public void RefreshPositionState()
        {
            OnPropertyChanged(nameof(CanMoveUp));
            OnPropertyChanged(nameof(CanMoveDown));
        }

        public void RefreshFilterDefinitionPresentation()
        {
            RefreshHeaderState();
            RefreshCollapsedSummary();
        }

        public void RefreshCollapsedSummary()
        {
            _collapsedSummary = BuildCollapsedSummary();
            _collapsedInputVisuals = BuildCollapsedInputVisuals();
            _collapsedInputRows = BuildCollapsedInputRows();
            _collapsedOutputVisuals = BuildCollapsedOutputVisuals();
            _collapsedOutputRows = _collapsedOutputVisuals.Select(visual =>
                new CollapsedConditionVisualRow
                {
                    Visuals = new List<BindingVisualDescriptor> { visual }
                }).ToList();

            OnPropertyChanged(nameof(CollapsedSummary));
            OnPropertyChanged(nameof(CollapsedInputVisuals));
            OnPropertyChanged(nameof(CollapsedInputRows));
            OnPropertyChanged(nameof(CollapsedOutputRows));
            OnPropertyChanged(nameof(CollapsedOutputVisuals));
            OnPropertyChanged(nameof(InputLogicSummary));
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
            // Countdown/preview animation is deliberately high-frequency and does not change the
            // collapsed route summary. Rebuilding all glyph descriptors on every progress tick was
            // needless WPF churn while the user was trying to press the input we were listening for.
            if (string.Equals(e.PropertyName, nameof(DeviceBindingViewModel.BindModeProgress), StringComparison.Ordinal) ||
                string.Equals(e.PropertyName, nameof(DeviceBindingViewModel.CurrentValue), StringComparison.Ordinal) ||
                string.Equals(e.PropertyName, nameof(DeviceBindingViewModel.PreviewValue), StringComparison.Ordinal) ||
                string.Equals(e.PropertyName, nameof(DeviceBindingViewModel.ShowButtonPreview), StringComparison.Ordinal))
            {
                return;
            }

            RefreshInputExpressionPresentation();
            RefreshPresetAvailability();
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
            var preferredDevices = Plugins.LastOrDefault()?.Plugin?.Outputs
                .Select(output => output.DeviceConfigurationGuid).ToList();
            var newPlugin = ProfileViewModel.Profile.Context.PluginManager.GetNewPlugin(plugin);
            if (!Mapping.AddPlugin(newPlugin)) return;
            SelectCompatibleOutputDevices(newPlugin, preferredDevices);

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
            if (Mapping.UseInputExpression)
            {
                for (var i = 0; i < Mapping.DeviceBindings.Count; i++)
                {
                    DeviceBindings.Add(new DeviceBindingViewModel(Mapping.DeviceBindings[i])
                    {
                        DeviceBindingName = "Input " + (i + 1),
                        DeviceBindingCategory = DeviceBindingCategory.Momentary
                    });
                }
            }
            else
            {
                for (var i = 0; i < plugin.InputCategories.Count && i < Mapping.DeviceBindings.Count; i++)
                {
                    DeviceBindings.Add(new DeviceBindingViewModel(Mapping.DeviceBindings[i])
                    {
                        DeviceBindingName = plugin.InputCategories[i].Name,
                        DeviceBindingCategory = plugin.InputCategories[i].Category
                    });
                }
            }

            RefreshInputExpressionPresentation();
        }

        public void AddAndInput()
        {
            var condition = InputConditions?.LastOrDefault();
            AddInputToCondition(condition);
        }

        public void AddOrInput()
        {
            AddCondition();
        }

        public void AddInputToCondition(InputConditionViewModel condition)
        {
            if (!ButtonsEnabled || !SupportsNativeInputExpression) return;
            var group = condition?.GroupIndex ?? 0;
            var binding = Mapping.AddExpressionInputToGroup(group);
            AddExpressionBindingViewModel(binding);
        }

        public void AddCondition()
        {
            if (!ButtonsEnabled || !SupportsNativeInputExpression) return;
            var binding = Mapping.AddExpressionCondition();
            AddExpressionBindingViewModel(binding);
        }

        private void AddExpressionBindingViewModel(DeviceBinding binding)
        {
            if (binding == null) return;

            var viewModel = new DeviceBindingViewModel(binding)
            {
                DeviceBindingName = "Input",
                DeviceBindingCategory = DeviceBindingCategory.Momentary
            };
            var modelIndex = Mapping.DeviceBindings.IndexOf(binding);
            if (modelIndex >= 0 && modelIndex <= DeviceBindings.Count)
                DeviceBindings.Insert(modelIndex, viewModel);
            else
                DeviceBindings.Add(viewModel);
            SubscribeSummaryBinding(viewModel);
            RefreshInputExpressionPresentation();
            RefreshCollapsedSummary();
        }

        public void RemoveExpressionInput(DeviceBindingViewModel bindingViewModel)
        {
            if (!ButtonsEnabled || bindingViewModel?.DeviceBinding == null) return;
            if (!Mapping.RemoveExpressionInput(bindingViewModel.DeviceBinding)) return;

            bindingViewModel.Dispose();
            DeviceBindings.Remove(bindingViewModel);
            RefreshInputExpressionPresentation();
            RefreshCollapsedSummary();
        }

        public void RemoveCondition(InputConditionViewModel condition)
        {
            if (!ButtonsEnabled || condition == null || !condition.CanRemoveCondition) return;
            var removed = condition.Inputs.ToList();
            if (!Mapping.RemoveExpressionGroup(condition.GroupIndex)) return;

            foreach (var binding in removed)
            {
                binding.Dispose();
                DeviceBindings.Remove(binding);
            }
            RefreshInputExpressionPresentation();
            RefreshCollapsedSummary();
        }

        public void SetInputNegated(DeviceBindingViewModel bindingViewModel, bool negated)
        {
            if (!ButtonsEnabled || bindingViewModel?.DeviceBinding == null) return;
            if (!Mapping.SetExpressionNegated(bindingViewModel.DeviceBinding, negated)) return;
            RefreshInputExpressionPresentation();
            RefreshCollapsedSummary();
        }

        private void RefreshInputExpressionPresentation()
        {
            var canRemove = UsesInputExpression && DeviceBindings.Count > 1;
            for (var i = 0; i < DeviceBindings.Count; i++)
            {
                DeviceBindings[i].DeviceBindingName = SupportsNativeInputExpression
                    ? "Input"
                    : (Mapping.Plugins.Count > 0 && i < Mapping.Plugins[0].InputCategories.Count
                        ? Mapping.Plugins[0].InputCategories[i].Name
                        : "Input " + (i + 1));
                DeviceBindings[i].InputExpressionControlsVisibility = Visibility.Collapsed;
                DeviceBindings[i].CanRemoveExpressionInput = canRemove;
            }

            RebuildInputConditions();
            OnPropertyChanged(nameof(SupportsNativeInputExpression));
            OnPropertyChanged(nameof(UsesInputExpression));
            OnPropertyChanged(nameof(InputLogicSummary));
            OnPropertyChanged(nameof(InputConditions));
            RefreshHeaderState();
        }

        private void RebuildInputConditions()
        {
            if (InputConditions == null) InputConditions = new ObservableCollection<InputConditionViewModel>();
            InputConditions.Clear();
            if (!SupportsNativeInputExpression || DeviceBindings == null || DeviceBindings.Count == 0) return;

            if (!UsesInputExpression)
            {
                InputConditions.Add(new InputConditionViewModel(0, DeviceBindings.Take(1), false));
                return;
            }

            var groups = DeviceBindings
                .Where(binding => binding?.DeviceBinding != null)
                .GroupBy(binding => Math.Max(0, binding.DeviceBinding.InputExpressionGroup))
                .OrderBy(group => group.Key)
                .ToList();
            var canRemoveCondition = groups.Count > 1;
            foreach (var group in groups)
            {
                InputConditions.Add(new InputConditionViewModel(group.Key, group, canRemoveCondition));
            }
        }

        private string BuildInputLogicSummary()
        {
            if (!UsesInputExpression) return "Single input";
            if (Mapping?.DeviceBindings == null || Mapping.DeviceBindings.Count == 0) return "No inputs";

            return string.Join("  OR  ", Mapping.DeviceBindings.Select((binding, index) => new
                {
                    Binding = binding,
                    Index = index
                })
                .GroupBy(item => Math.Max(0, item.Binding.InputExpressionGroup))
                .OrderBy(group => group.Key)
                .Select(group => string.Join(" + ", group.Select(item =>
                    (item.Binding.InputExpressionNegated ? "NOT " : string.Empty) + "Input " + (item.Index + 1)))));
        }

        // Adding an output uses the existing compatible output type by default.
        // The new row's type selector can change it afterwards.
        public bool AddDefaultOutput()
        {
            if (!ButtonsEnabled) return false;
            var options = GetCompatiblePluginOptions();
            var currentType = Plugins.LastOrDefault()?.Plugin?.GetType();
            var selected = options.FirstOrDefault(option => option.Plugin.GetType() == currentType)
                ?? options.FirstOrDefault(option => option.OutputType == "Button")
                ?? options.FirstOrDefault();
            if (selected == null) return false;

            var count = Plugins.Count;
            AddPlugin(selected.Plugin);
            return Plugins.Count > count;
        }

        // Change the number of button inputs without replacing the mapping,
        // its title, first axis output or filter references.
        public bool ChangeAxisButtonMode(PluginViewModel original, bool useTwoButtons)
        {
            return ChangeAxisInputMode(original, useTwoButtons ? 2 : 1);
        }

        public bool ChangeAxisInputMode(PluginViewModel original, int buttonCount)
        {
            if (!ButtonsEnabled || original == null || Plugins.Count != 1 ||
                Mapping.Plugins.Count != 1 ||
                !original.IsAxisButtonMapping || (buttonCount != 1 && buttonCount != 2 && buttonCount != 4))
                return false;

            var currentCount = original.IsFourButtonAxis ? 4 : original.IsTwoButtonAxis ? 2 : 1;
            if (currentCount == buttonCount) return true;

            var className = buttonCount == 1 ? "ButtonToAxis" :
                buttonCount == 2 ? "ButtonsToAxis" : "ButtonsToAxes";
            var targetType = original.Plugin.GetType().Assembly.GetType(
                original.Plugin.GetType().Namespace + "." + className);
            if (targetType == null || !typeof(Plugin).IsAssignableFrom(targetType)) return false;
            var replacement = (Plugin)Activator.CreateInstance(targetType);
            replacement.SetProfile(ProfileViewModel.Profile);
            if (replacement.InputCategories.Count != buttonCount ||
                replacement.Outputs.Count < 1 || original.Plugin.Outputs.Count < 1) return false;

            // Single-axis mode keeps X when leaving a two-axis stick. Entering four-way
            // mode requires choosing a whole destination, not reusing one arbitrary axis.
            if (buttonCount != 4 && !original.IsFourButtonAxis ||
                buttonCount != 4 && original.IsFourButtonAxis &&
                original.Plugin.OutputCategories[0].Category == DeviceBindingCategory.Range)
                replacement.Outputs[0] = original.Plugin.Outputs[0];
            replacement.Filters = original.Plugin.Filters.ToList();
            var preferredOutput = original.Plugin.Outputs.Select(binding => binding.DeviceConfigurationGuid).ToList();
            SelectCompatibleOutputDevices(replacement, preferredOutput);

            // Axis mode 2/4 consumes positional inputs, not a boolean AND/OR expression.
            // The caller must explicitly confirm discarding expression terms before this point.
            if (Mapping.UseInputExpression)
            {
                Mapping.UseInputExpression = false;
                foreach (var term in Mapping.DeviceBindings)
                {
                    term.SetInputExpressionGroup(0);
                    term.SetInputExpressionNegated(false);
                }
            }

            foreach (var binding in DeviceBindings)
            {
                binding.PropertyChanged -= SummaryBindingOnPropertyChanged;
                binding.Dispose();
            }
            DeviceBindings.Clear();
            foreach (var binding in original.DeviceBindings)
                binding.PropertyChanged -= SummaryBindingOnPropertyChanged;
            original.Dispose();

            while (Mapping.DeviceBindings.Count < buttonCount)
                Mapping.DeviceBindings.Add(new DeviceBinding(Mapping.Update,
                    ProfileViewModel.Profile, DeviceIoType.Input)
                {
                    DeviceConfigurationGuid = ProfileViewModel.Profile.GetPrimaryDeviceConfiguration(DeviceIoType.Input)?.Guid ?? Guid.Empty
                });
            while (Mapping.DeviceBindings.Count > buttonCount)
                Mapping.DeviceBindings.RemoveAt(Mapping.DeviceBindings.Count - 1);

            Mapping.Plugins[0] = replacement;
            var viewModel = new PluginViewModel(this, replacement);
            Plugins[0] = viewModel;
            PopulateDeviceBindingsViewModels();
            foreach (var binding in DeviceBindings) SubscribeSummaryBinding(binding);
            foreach (var binding in viewModel.DeviceBindings) SubscribeSummaryBinding(binding);
            ProfileViewModel.Profile.Context.ContextChanged();
            RefreshHeaderState();
            RefreshInputExpressionPresentation();
            RefreshPresetAvailability();
            RefreshCollapsedSummary();
            ProfileViewModel.RefreshFilterReferenceLabels();
            Logger.Info("Axis input mode for '" + MappingTitle + "': " + buttonCount + " buttons");
            return true;
        }

        public Visibility ControllerStickPresetVisibility =>
            Mapping?.Plugins?.Count == 1 &&
            string.Equals(Mapping.Plugins[0].PluginName, "Axes to Axes", StringComparison.OrdinalIgnoreCase) &&
            Mapping.DeviceBindings.Count == 2 ? Visibility.Visible : Visibility.Collapsed;

        public bool ApplyControllerStickInputPreset(int axisIndex, out string error)
        {
            error = null;
            if (!ButtonsEnabled || ControllerStickPresetVisibility != Visibility.Visible ||
                (axisIndex != 0 && axisIndex != 2)) return false;
            var profile = ProfileViewModel.Profile;
            var preferred = Mapping.DeviceBindings[0].DeviceConfigurationGuid;
            var primary = profile.GetPrimaryDeviceConfiguration(DeviceIoType.Input)?.Guid;
            var options = new List<Tuple<DeviceConfiguration, DeviceBindingInfo[]>>();
            foreach (var device in profile.GetDeviceConfigurationList(DeviceIoType.Input))
            {
                if (device?.Device == null) continue;
                var kind = DeviceVisualCatalog.Describe(device, profile, DeviceIoType.Input).Kind;
                if (kind == DeviceVisualKind.Keyboard || kind == DeviceVisualKind.Mouse ||
                    kind == DeviceVisualKind.Unavailable || kind == DeviceVisualKind.Unknown) continue;
                var axes = FlattenBindingNodes(profile.Context.DevicesManager.GetDeviceBindingMenu(
                    device.Device, DeviceIoType.Input)).Where(node => node?.DeviceBindingInfo != null &&
                    node.DeviceBindingInfo.DeviceBindingCategory == DeviceBindingCategory.Range).ToList();
                var pair = axes.GroupBy(node => node.DeviceBindingInfo.KeyType).FirstOrDefault(group =>
                    group.Any(node => node.DeviceBindingInfo.KeyValue == axisIndex) &&
                    group.Any(node => node.DeviceBindingInfo.KeyValue == axisIndex + 1));
                if (pair != null) options.Add(Tuple.Create(device, new[]
                {
                    pair.First(node => node.DeviceBindingInfo.KeyValue == axisIndex).DeviceBindingInfo,
                    pair.First(node => node.DeviceBindingInfo.KeyValue == axisIndex + 1).DeviceBindingInfo
                }));
            }
            var selected = options.FirstOrDefault(option => option.Item1.Guid == preferred)
                ?? options.FirstOrDefault(option => option.Item1.Guid == primary)
                ?? options.FirstOrDefault();
            if (selected == null)
            {
                error = "Add a controller with the requested analogue stick to the profile's input devices first.";
                return false;
            }
            for (var i = 0; i < 2; i++)
            {
                var info = selected.Item2[i];
                var binding = Mapping.DeviceBindings[i];
                binding.DeviceBindingCategory = DeviceBindingCategory.Range;
                binding.SetDeviceConfigurationGuid(selected.Item1.Guid, false);
                binding.SetKeyTypeValue(info.KeyType, info.KeyValue, info.KeySubValue);
            }
            foreach (var binding in DeviceBindings) binding.RefreshDeviceList();
            RefreshCollapsedSummary();
            return true;
        }

        // One four-way destination may consist of two analogue axes or four D-pad buttons.
        // Only offer a choice after verifying *all* its constituent controls exist.
        public DeviceConfiguration GetSelectedPresetInputDevice()
        {
            var profile = ProfileViewModel.Profile;
            var guid = Mapping.DeviceBindings.FirstOrDefault()?.DeviceConfigurationGuid ?? Guid.Empty;
            return profile.GetDeviceConfiguration(DeviceIoType.Input, guid)
                ?? profile.GetPrimaryDeviceConfiguration(DeviceIoType.Input);
        }

        public bool SelectedPresetInputIsKeyboard =>
            DeviceVisualCatalog.Describe(GetSelectedPresetInputDevice(), ProfileViewModel.Profile,
                DeviceIoType.Input).Kind == DeviceVisualKind.Keyboard;

        public bool SelectedPresetInputHasDPad =>
            FindDPadControls(GetSelectedPresetInputDevice(), DeviceIoType.Input) != null;

        public void RefreshPresetAvailability()
        {
            OnPropertyChanged(nameof(SelectedPresetInputIsKeyboard));
            OnPropertyChanged(nameof(SelectedPresetInputHasDPad));
            foreach (var plugin in Plugins ?? Enumerable.Empty<PluginViewModel>())
                plugin.RefreshInputPresetDisplay();
        }

        public List<FourWayOutputDeviceOption> GetFourWayOutputDevices()
        {
            return GetFourWayOutputChoices().GroupBy(option => option.DeviceConfigurationGuid)
                .Select(group => new FourWayOutputDeviceOption
                {
                    Guid = group.Key, Title = group.First().DeviceTitle
                }).ToList();
        }

        public void SetFourWayOutputDevice(PluginViewModel axis, Guid guid)
        {
            if (axis?.IsFourButtonAxis != true || !ButtonsEnabled) return;
            var options = GetFourWayOutputChoices()
                .Where(option => option.DeviceConfigurationGuid == guid).ToList();
            if (options.Count == 0) return;
            var preferred = axis.FourWayOutputLabel;
            var chosen = options.FirstOrDefault(option => option.Destination == preferred)
                ?? options.FirstOrDefault(option => option.Destination == "Left stick")
                ?? options.First();
            ApplyFourWayOutput(axis, chosen);
        }

        public List<FourWayOutputChoice> GetFourWayOutputChoices()
        {
            var result = new List<FourWayOutputChoice>();
            var profile = ProfileViewModel.Profile;
            foreach (var device in profile.GetDeviceConfigurationList(DeviceIoType.Output))
            {
                if (device?.Device == null || !string.Equals(device.Device.ProviderName,
                    "Core_ViGEm", StringComparison.OrdinalIgnoreCase)) continue;
                var nodes = FlattenBindingNodes(profile.Context.DevicesManager.GetDeviceBindingMenu(
                    device.Device, DeviceIoType.Output))
                    .Where(node => node?.DeviceBindingInfo != null).ToList();
                var axes = nodes.Where(node => node.DeviceBindingInfo.DeviceBindingCategory == DeviceBindingCategory.Range &&
                    node.DeviceBindingInfo.KeyValue >= 0 && node.DeviceBindingInfo.KeyValue <= 3)
                    .GroupBy(node => node.DeviceBindingInfo.KeyType)
                    .FirstOrDefault(group => Enumerable.Range(0, 4).All(index =>
                        group.Any(node => node.DeviceBindingInfo.KeyValue == index)))?.ToList();
                if (axes != null)
                {
                    foreach (var side in new[] { 0, 2 })
                    {
                        result.Add(new FourWayOutputChoice
                        {
                            DeviceConfigurationGuid = device.Guid,
                            DeviceTitle = device.GetFullTitleForProfile(profile),
                            Destination = side == 0 ? "Left stick" : "Right stick",
                            Controls = new[] { axes.First(node => node.DeviceBindingInfo.KeyValue == side).DeviceBindingInfo,
                                axes.First(node => node.DeviceBindingInfo.KeyValue == side + 1).DeviceBindingInfo }
                        });
                    }
                }
                var dpad = FindDPadControls(device, DeviceIoType.Output);
                if (dpad != null)
                    result.Add(new FourWayOutputChoice
                    {
                        DeviceConfigurationGuid = device.Guid,
                        DeviceTitle = device.GetFullTitleForProfile(profile),
                        Destination = "D-pad", IsDPad = true,
                        Controls = dpad
                    });
            }
            return result;
        }

        // Stored order matches the four input directions: Up, Down, Left, Right.
        private DeviceBindingInfo[] FindDPadControls(DeviceConfiguration device, DeviceIoType ioType)
        {
            var profile = ProfileViewModel.Profile;
            if (device?.Device == null) return null;
            var nodes = FlattenBindingNodes(profile.Context.DevicesManager.GetDeviceBindingMenu(
                device.Device, ioType)).Where(node => node?.DeviceBindingInfo != null &&
                    node.DeviceBindingInfo.DeviceBindingCategory == DeviceBindingCategory.Momentary).ToList();
            var dpad = nodes.Where(node =>
            {
                var info = node.DeviceBindingInfo;
                var candidate = new DeviceBinding
                {
                    Profile = profile, DeviceIoType = ioType,
                    DeviceBindingCategory = DeviceBindingCategory.Momentary,
                    DeviceConfigurationGuid = device.Guid, IsBound = true,
                    KeyType = info.KeyType, KeyValue = info.KeyValue, KeySubValue = info.KeySubValue
                };
                return DeviceVisualCatalog.DescribeBinding(candidate,
                    DeviceBindingCategory.Momentary, profile).ControlKind == ControlVisualKind.DPad;
            }).ToList();
            if (!Enumerable.Range(0, 4).All(index => dpad.Any(node => node.DeviceBindingInfo.KeyValue == index)))
                return null;
            return new[] { 0, 2, 3, 1 }
                .Select(index => dpad.First(node => node.DeviceBindingInfo.KeyValue == index).DeviceBindingInfo)
                .ToArray();
        }

        public bool ApplyFourWayOutput(PluginViewModel axis, FourWayOutputChoice choice)
        {
            if (!ButtonsEnabled || axis?.IsFourButtonAxis != true || choice?.Controls == null ||
                Plugins.Count != 1 || Mapping.Plugins.Count != 1) return false;
            var destination = ProfileViewModel.Profile.GetDeviceConfiguration(
                DeviceIoType.Output, choice.DeviceConfigurationGuid);
            if (destination?.Device == null ||
                choice.Controls.Length != (choice.IsDPad ? 4 : 2)) return false;
            var typeName = choice.IsDPad ? "ButtonsToDPad" : "ButtonsToAxes";
            var type = axis.Plugin.GetType().Assembly.GetType(axis.Plugin.GetType().Namespace + "." + typeName);
            if (type == null || !typeof(Plugin).IsAssignableFrom(type)) return false;
            var replace = axis.Plugin.GetType() != type;
            var plugin = replace ? (Plugin)Activator.CreateInstance(type) : axis.Plugin;
            if (replace)
            {
                plugin.SetProfile(ProfileViewModel.Profile);
                plugin.Filters = axis.Plugin.Filters.ToList();
                foreach (var propertyName in new[] { "InvertX", "InvertY" })
                {
                    var source = axis.Plugin.GetType().GetProperty(propertyName);
                    var target = type.GetProperty(propertyName);
                    if (source != null && target != null)
                        target.SetValue(plugin, source.GetValue(axis.Plugin, null), null);
                }
            }
            if (plugin.Outputs.Count != choice.Controls.Length) return false;
            for (var i = 0; i < plugin.Outputs.Count; i++)
            {
                var info = choice.Controls[i];
                var binding = plugin.Outputs[i];
                binding.DeviceBindingCategory = plugin.OutputCategories[i].Category;
                binding.SetDeviceConfigurationGuid(destination.Guid, false);
                binding.SetKeyTypeValue(info.KeyType, info.KeyValue, info.KeySubValue);
            }
            if (replace)
            {
                foreach (var oldBinding in axis.DeviceBindings)
                    oldBinding.PropertyChanged -= SummaryBindingOnPropertyChanged;
                axis.Dispose();
                Mapping.Plugins[0] = plugin;
                var newAxis = new PluginViewModel(this, plugin);
                Plugins[0] = newAxis;
                foreach (var output in newAxis.DeviceBindings) SubscribeSummaryBinding(output);
            }
            else
            {
                foreach (var output in axis.DeviceBindings) output.RefreshDeviceList();
                axis.RefreshFourWayDestination();
            }
            ProfileViewModel.Profile.Context.ContextChanged();
            RefreshHeaderState();
            RefreshCollapsedSummary();
            return true;
        }

        public bool ApplyControllerDPadPreset(out string error)
        {
            error = null;
            if (!ButtonsEnabled || Mapping.Plugins.Count != 1 ||
                !(Plugins[0].IsFourButtonAxis) || Mapping.DeviceBindings.Count != 4)
            {
                error = "Select a four-way mapping first.";
                return false;
            }
            var profile = ProfileViewModel.Profile;
            var selectedDevice = GetSelectedPresetInputDevice();
            var selectedControls = FindDPadControls(selectedDevice, DeviceIoType.Input);
            if (selectedControls == null)
            {
                error = "Add a controller with a D-pad to this profile's input devices first.";
                return false;
            }
            for (var i = 0; i < 4; i++)
            {
                var info = selectedControls[i];
                var binding = Mapping.DeviceBindings[i];
                binding.DeviceBindingCategory = DeviceBindingCategory.Momentary;
                binding.SetDeviceConfigurationGuid(selectedDevice.Guid, false);
                binding.SetKeyTypeValue(info.KeyType, info.KeyValue, info.KeySubValue);
            }
            foreach (var binding in DeviceBindings) binding.RefreshDeviceList();
            RefreshCollapsedSummary();
            return true;
        }

        // Apply all four directions as a single transaction. Resolve every key
        // before changing a binding, so missing keys cannot partially apply.
        public bool ApplyKeyboardAxisPreset(string preset, out string error)
        {
            error = null;
            if (!ButtonsEnabled || Mapping.Plugins.Count != 1 ||
                !Plugins[0].IsFourButtonAxis || Mapping.DeviceBindings.Count != 4)
            {
                error = "Select a four-way joystick mapping first.";
                return false;
            }

            var presets = new Dictionary<string, string[]>
            {
                { "WASD", new[] { "W", "S", "A", "D" } },
                { "Arrows", new[] { "Up", "Down", "Left", "Right" } },
                { "Numpad", new[] { "Numpad8", "Numpad2", "Numpad4", "Numpad6" } },
                { "UJHK", new[] { "U", "J", "H", "K" } }
            };
            string[] names;
            if (!presets.TryGetValue(preset ?? string.Empty, out names)) return false;

            var profile = ProfileViewModel.Profile;
            // Respect the keyboard explicitly chosen on the first input, otherwise
            // the profile primary keyboard, before considering a fallback keyboard.
            var keyboard = SelectedPresetInputIsKeyboard ? GetSelectedPresetInputDevice() : null;
            if (keyboard == null)
            {
                error = "Add a keyboard to this profile's input devices first.";
                return false;
            }

            var keys = FlattenBindingNodes(profile.Context.DevicesManager.GetDeviceBindingMenu(
                    keyboard.Device, DeviceIoType.Input))
                .Where(node => node.DeviceBindingInfo != null &&
                    node.DeviceBindingInfo.DeviceBindingCategory == DeviceBindingCategory.Momentary)
                .ToList();
            var selected = new List<DeviceBindingNode>();
            foreach (var name in names)
            {
                var aliases = name.StartsWith("Numpad", StringComparison.OrdinalIgnoreCase)
                    ? new[] { name, name.Replace("Numpad", "Num"), name.Replace("Numpad", "Keypad") }
                    : new[] { name };
                var key = keys.FirstOrDefault(node => aliases.Any(alias =>
                    NormalizeAxisPresetKey(node.Title) == NormalizeAxisPresetKey(alias)));
                if (key == null)
                {
                    error = "The input keyboard does not expose " + name + ".";
                    return false;
                }
                selected.Add(key);
            }

            for (var i = 0; i < selected.Count; i++)
            {
                var binding = Mapping.DeviceBindings[i];
                var info = selected[i].DeviceBindingInfo;
                binding.DeviceBindingCategory = DeviceBindingCategory.Momentary;
                binding.SetDeviceConfigurationGuid(keyboard.Guid);
                binding.SetKeyTypeValue(info.KeyType, info.KeyValue, info.KeySubValue);
            }
            foreach (var binding in DeviceBindings) binding.RefreshDeviceList();
            RefreshCollapsedSummary();
            return true;
        }

        private static string NormalizeAxisPresetKey(string value)
        {
            return Regex.Replace(value ?? string.Empty, "[^A-Za-z0-9]", "").ToUpperInvariant();
        }

        // Swap only the chosen output plugin, never the mapping's shared input bindings.
        public bool ChangeAllOutputTypes(Plugin replacementTemplate)
        {
            if (!ButtonsEnabled || replacementTemplate == null || Plugins.Count == 0) return false;
            var candidates = GetCompatiblePluginOptions();
            if (!candidates.Any(candidate => candidate.Plugin.GetType() == replacementTemplate.GetType()))
                return false;
            // Apply the common category to each destination. Individual output type changes
            // remain available in the row's optional settings.
            var success = true;
            foreach (var output in Plugins.ToList())
                success &= ChangeOutputType(output, replacementTemplate);
            return success;
        }

        public bool ChangeOutputType(PluginViewModel original, Plugin replacementTemplate)
        {
            if (!ButtonsEnabled || original == null || replacementTemplate == null) return false;
            var index = Plugins.IndexOf(original);
            if (index < 0 || index >= Mapping.Plugins.Count) return false;
            if (!Mapping.GetPluginList().Any(option => option.GetType() == replacementTemplate.GetType()))
                return false;
            if (original.Plugin.GetType() == replacementTemplate.GetType()) return true;

            var replacement = ProfileViewModel.Profile.Context.PluginManager.GetNewPlugin(replacementTemplate);
            replacement.SetProfile(ProfileViewModel.Profile);
            replacement.Filters = original.Plugin.Filters.ToList();
            SelectCompatibleOutputDevices(replacement,
                original.Plugin.Outputs.Select(binding => binding.DeviceConfigurationGuid).ToList());
            var replacementViewModel = new PluginViewModel(this, replacement);
            foreach (var binding in original.DeviceBindings)
                binding.PropertyChanged -= SummaryBindingOnPropertyChanged;
            original.Dispose();
            Mapping.Plugins[index] = replacement;
            Plugins[index] = replacementViewModel;
            foreach (var binding in replacementViewModel.DeviceBindings)
                SubscribeSummaryBinding(binding);
            ProfileViewModel.Profile.PruneUndefinedFilterReferencesRecursive();
            ProfileViewModel.Profile.Context.ContextChanged();
            RefreshHeaderState();
            RefreshCollapsedSummary();
            ProfileViewModel.RefreshFilterReferenceLabels();
            Logger.Info("Changed output type in mapping '" + MappingTitle + "' to " + replacement.PluginName);
            return true;
        }

        // Preserve the previous device when it supports the new output type. Otherwise
        // choose a genuinely compatible output device instead of the first device in the list.
        private void SelectCompatibleOutputDevices(Plugin plugin, IList<Guid> preferredDevices)
        {
            if (plugin?.Outputs == null) return;
            var profile = ProfileViewModel.Profile;
            var candidates = profile.GetDeviceConfigurationList(DeviceIoType.Output);
            for (var index = 0; index < plugin.Outputs.Count && index < plugin.OutputCategories.Count; index++)
            {
                var category = plugin.OutputCategories[index].Category;
                var compatible = candidates.Where(device => DeviceBindingViewModel.SupportsOutputCategory(
                    device, category, profile.Context)).ToList();
                var preference = preferredDevices != null && index < preferredDevices.Count
                    ? preferredDevices[index] : Guid.Empty;
                var primary = profile.GetPrimaryDeviceConfiguration(DeviceIoType.Output);
                var selected = compatible.FirstOrDefault(device => device.Guid == preference)
                    ?? compatible.FirstOrDefault(device => device.Guid == primary?.Guid)
                    ?? compatible.FirstOrDefault();
                if (selected != null && (!plugin.Outputs[index].IsBound ||
                    plugin.Outputs[index].DeviceConfigurationGuid != selected.Guid))
                    plugin.Outputs[index].SetDeviceConfigurationGuid(selected.Guid, false);
            }
        }

        public void RemovePlugin(PluginViewModel pluginViewModel)
        {
            if (!Mapping.RemovePlugin(pluginViewModel.Plugin)) return;

            pluginViewModel.Dispose();
            Plugins.Remove(pluginViewModel);
            RefreshHeaderState();
            RefreshCollapsedSummary();
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
            RefreshFilterPresentationCache();
            _mappingOutputTypeLabel = GetMappingOutputTypeLabel();

            OnPropertyChanged(nameof(HasFilters));
            OnPropertyChanged(nameof(ReferencedFilters));
            OnPropertyChanged(nameof(HasFilterReferences));
            OnPropertyChanged(nameof(FilterIndicatorToolTip));
            OnPropertyChanged(nameof(DefinedFilterName));
            OnPropertyChanged(nameof(MappingOutputTypeLabel));
        }

        private void RefreshHeaderState()
        {
            _mappingRouteTokens = BuildMappingRouteTokens(MappingRouteDisplay);
            _mappingOutputTypeLabel = GetMappingOutputTypeLabel();
            RefreshFilterPresentationCache();

            OnPropertyChanged(nameof(MappingRoute));
            OnPropertyChanged(nameof(MappingRouteDisplay));
            OnPropertyChanged(nameof(MappingRouteTokens));
            OnPropertyChanged(nameof(MappingOutputTypeLabel));
            OnPropertyChanged(nameof(MappingInputTypeLabel));
            OnPropertyChanged(nameof(MappingOutputCategoryDisplay));
            OnPropertyChanged(nameof(DefinedFilterName));
            OnPropertyChanged(nameof(ReferencedFilters));
            OnPropertyChanged(nameof(HasFilterReferences));
            OnPropertyChanged(nameof(FilterIndicatorToolTip));
            OnPropertyChanged(nameof(HasFilters));
            OnPropertyChanged(nameof(ControllerStickPresetVisibility));
        }

        private void RefreshFilterPresentationCache()
        {
            _definedFilterName = GetDefinedFilterName();
            _referencedFilters = BuildReferencedFilters();
            _filterIndicatorToolTip = BuildFilterIndicatorToolTip(_referencedFilters);
        }

        private static string FormatMappingRoute(string route, int inputCount, int outputCount)
        {
            if (string.IsNullOrWhiteSpace(route)) return route;
            var sides = Regex.Split(route.Trim(), @"\s+to\s+", RegexOptions.IgnoreCase);
            if (sides.Length != 2)
                return Regex.Replace(route.Trim(), @"\s+to\s+", " → ", RegexOptions.IgnoreCase);

            var input = Regex.Replace(sides[0], @"\bButtons?\b",
                inputCount == 1 ? "Button" : "Buttons", RegexOptions.IgnoreCase);
            var output = Regex.Replace(sides[1], @"\bButtons?\b",
                outputCount == 1 ? "Button" : "Buttons", RegexOptions.IgnoreCase);
            return input + " → " + output;
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
            if (configuration?.Device == null) return false;

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

            var menu = ProfileViewModel.Profile.Context.DevicesManager.GetDeviceBindingMenu(
                configuration.Device, DeviceIoType.Output);
            foreach (var node in FlattenBindingNodes(menu))
            {
                if (node?.DeviceBindingInfo == null ||
                    node.DeviceBindingInfo.DeviceBindingCategory != bindingViewModel.DeviceBindingCategory) continue;

                var info = node.DeviceBindingInfo;
                var temporary = new DeviceBinding
                {
                    Profile = ProfileViewModel.Profile,
                    DeviceIoType = DeviceIoType.Output,
                    DeviceBindingCategory = bindingViewModel.DeviceBindingCategory,
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
                    KeyType = info.KeyType,
                    KeyValue = info.KeyValue,
                    KeySubValue = info.KeySubValue,
                    Visual = DeviceVisualCatalog.DescribeBinding(
                        temporary, bindingViewModel.DeviceBindingCategory, ProfileViewModel.Profile)
                });
            }

            return result;
        }

        public bool ApplyQuickOutputBinding(BindingVisualDescriptor descriptor, QuickBindingOption option)
        {
            if (!ButtonsEnabled || option == null) return false;
            var bindingViewModel = ResolveCollapsedBinding(descriptor, DeviceIoType.Output);
            if (bindingViewModel?.DeviceBinding == null || !bindingViewModel.BindingEnabled) return false;

            var binding = bindingViewModel.DeviceBinding;
            binding.DeviceBindingCategory = bindingViewModel.DeviceBindingCategory;
            binding.SetDeviceConfigurationGuid(option.DeviceConfigurationGuid);
            binding.SetKeyTypeValue(option.KeyType, option.KeyValue, option.KeySubValue);
            bindingViewModel.RefreshDeviceList();
            RefreshCollapsedSummary();
            return true;
        }

        private DeviceConfiguration ResolveQuickOutputConfiguration(DeviceBindingViewModel bindingViewModel)
        {
            if (bindingViewModel?.DeviceBinding == null) return null;
            bindingViewModel.EnsureDeviceListLoaded();
            var binding = bindingViewModel.DeviceBinding;
            var configurationGuid = binding.DeviceConfigurationGuid;
            if (configurationGuid == Guid.Empty && bindingViewModel.SelectedDevice != null)
                configurationGuid = bindingViewModel.SelectedDevice.Value;
            if (configurationGuid == Guid.Empty)
                configurationGuid = ProfileViewModel.Profile.GetPrimaryDeviceConfiguration(DeviceIoType.Output)?.Guid ?? Guid.Empty;

            return ProfileViewModel.Profile.GetDeviceConfiguration(DeviceIoType.Output, configurationGuid);
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
        private List<CollapsedConditionVisualRow> BuildCollapsedInputRows()
        {
            var result = new List<CollapsedConditionVisualRow>();
            var groups = SupportsNativeInputExpression && UsesInputExpression
                ? DeviceBindings.Where(binding => binding?.DeviceBinding != null)
                    .GroupBy(binding => Math.Max(0, binding.DeviceBinding.InputExpressionGroup))
                    .OrderBy(group => group.Key)
                    .Select(group => group.ToList()).ToList()
                : new List<List<DeviceBindingViewModel>> { DeviceBindings.ToList() };

            for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                var visuals = groups[groupIndex].Select(DescribeCollapsedBinding).ToList();
                for (var start = 0; start < visuals.Count; start += 3)
                    result.Add(new CollapsedConditionVisualRow
                    {
                        Visuals = visuals.Skip(start).Take(3).ToList(),
                        StartsAlternative = groupIndex > 0 && start == 0
                    });
            }
            if (result.Count == 0)
                result.Add(new CollapsedConditionVisualRow
                {
                    Visuals = new List<BindingVisualDescriptor>
                    {
                        DeviceVisualCatalog.DescribeBinding(null,
                            DeviceBindingCategory.Momentary, ProfileViewModel.Profile)
                    }
                });
            return result;
        }

        private static List<CollapsedConditionVisualRow> ChunkVisuals(List<BindingVisualDescriptor> visuals)
        {
            var rows = new List<CollapsedConditionVisualRow>();
            for (var start = 0; start < visuals.Count; start += 3)
                rows.Add(new CollapsedConditionVisualRow
                {
                    Visuals = visuals.Skip(start).Take(3).ToList()
                });
            return rows;
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
            // Present every output route, in the same order as the expanded output rows.
            // Avoid the old primary-plugin-only summary that concealed extra outputs.
            foreach (var plugin in Plugins)
            {
                foreach (var binding in plugin.DeviceBindings)
                    result.Add(DescribeCollapsedBinding(binding));

                if (plugin.DeviceBindings.Count == 0 &&
                    string.Equals(plugin.Plugin.Group, "Filter", StringComparison.OrdinalIgnoreCase))
                {
                    // Filter-definition plugins have no physical output binding. Do not
                    // misleadingly draw an unbound '?' as though they require a device.
                    var filterName = plugin.Plugin.GetDefinedFilterName();
                    result.Add(DeviceVisualCatalog.Filter(
                        string.IsNullOrWhiteSpace(filterName) ? "Set filter name" : filterName));
                }
            }

            if (result.Count == 0)
                result.Add(DeviceVisualCatalog.DescribeBinding(null,
                    DeviceBindingCategory.Momentary, ProfileViewModel.Profile));
            return result;
        }

        private BindingVisualDescriptor DescribeCollapsedBinding(DeviceBindingViewModel viewModel)
        {
            if (viewModel == null) return DeviceVisualCatalog.DescribeBinding(null, DeviceBindingCategory.Momentary, ProfileViewModel.Profile);
            var descriptor = DeviceVisualCatalog.DescribeBinding(
                viewModel.DeviceBinding, viewModel.DeviceBindingCategory, ProfileViewModel.Profile);
            descriptor.IsNegatedInput = viewModel.DeviceBinding?.DeviceIoType == DeviceIoType.Input &&
                viewModel.InputExpressionNegated;
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

        private static string BuildFilterIndicatorToolTip(IList<FilterReferenceBadge> filters)
        {
            if (filters.Count == 0) return null;
            if (filters.Count == 1) return "Filter: " + filters[0].Name;
            return "Filters: " + string.Join(", ", filters.Select(filter => filter.Name));
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


        // Input type changes select a real remapper with the same output signature.
        // They never masquerade as output changes, and cannot silently mutate multi-output mappings.
        public string MappingInputTypeLabel
        {
            get
            {
                var inputs = Mapping?.Plugins?.FirstOrDefault()?.InputCategories;
                if (inputs == null || inputs.Count == 0) return "None";
                var kinds = inputs.Select(item => item.Category).Distinct().ToList();
                if (kinds.Count != 1) return "Multiple";
                switch (kinds[0])
                {
                    case DeviceBindingCategory.Momentary:
                        return (Mapping?.DeviceBindings?.Count ?? 0) == 1 ? "Button" : "Buttons";
                    case DeviceBindingCategory.Range: return "Axis";
                    case DeviceBindingCategory.Event: return "Event";
                    case DeviceBindingCategory.Delta: return "Delta";
                    default: return "Value";
                }
            }
        }

        // A category change must preserve every existing output, including mappings with
        // several independently configured destinations.
        private bool HasReplacementForEachOutput(Plugin template, IList<Plugin> catalog)
        {
            if (template == null || Mapping?.Plugins == null) return false;
            var inputSignature = template.InputCategories.Select(c => c.Category).ToList();
            return Mapping.Plugins.All(original => catalog.Any(candidate =>
                candidate.InputCategories.Select(c => c.Category).SequenceEqual(inputSignature) &&
                candidate.OutputCategories.Select(c => c.Category)
                    .SequenceEqual(original.OutputCategories.Select(c => c.Category))));
        }

        public List<SimplePluginViewModel> GetCompatibleInputPluginOptions()
        {
            if (Mapping?.Plugins?.Count == 0) return new List<SimplePluginViewModel>();
            var current = Mapping.Plugins[0];
            var catalog = ProfileViewModel.Profile.Context.GetPlugins().ToList();
            return catalog
                .Where(candidate => candidate.InputCategories.Count > 0 &&
                    candidate.OutputCategories.Select(c => c.Category)
                        .SequenceEqual(current.OutputCategories.Select(c => c.Category)) &&
                    HasReplacementForEachOutput(candidate, catalog))
                .Select(candidate => new SimplePluginViewModel(candidate))
                .OrderBy(candidate => candidate.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        public bool ChangeInputType(PluginViewModel original, Plugin replacementTemplate)
        {
            if (!ButtonsEnabled || original == null || replacementTemplate == null ||
                Plugins.Count == 0 || Plugins[0] != original ||
                Mapping.Plugins.Count != Plugins.Count) return false;
            if (!GetCompatibleInputPluginOptions().Any(option =>
                option.Plugin.GetType() == replacementTemplate.GetType())) return false;
            if (original.Plugin.GetType() == replacementTemplate.GetType() &&
                Mapping.Plugins.All(p => p.InputCategories.Select(c => c.Category)
                    .SequenceEqual(replacementTemplate.InputCategories.Select(c => c.Category))))
                return true;

            var profile = ProfileViewModel.Profile;
            var catalog = profile.Context.GetPlugins().ToList();
            var inputSignature = replacementTemplate.InputCategories.Select(c => c.Category).ToList();
            var replacements = new List<Plugin>();
            for (var i = 0; i < Mapping.Plugins.Count; i++)
            {
                var existing = Mapping.Plugins[i];
                var choice = i == 0 ? replacementTemplate : catalog.FirstOrDefault(candidate =>
                    candidate.InputCategories.Select(c => c.Category).SequenceEqual(inputSignature) &&
                    candidate.OutputCategories.Select(c => c.Category)
                        .SequenceEqual(existing.OutputCategories.Select(c => c.Category)));
                if (choice == null) return false;
                var next = profile.Context.PluginManager.GetNewPlugin(choice);
                next.SetProfile(profile);
                if (next.Outputs.Count != existing.Outputs.Count) return false;
                for (var j = 0; j < next.Outputs.Count; j++)
                    next.Outputs[j] = existing.Outputs[j];
                next.Filters = existing.Filters.ToList();
                replacements.Add(next);
            }

            var oldInputCategories = original.Plugin.InputCategories.Select(c => c.Category).ToList();
            foreach (var binding in DeviceBindings)
            {
                binding.PropertyChanged -= SummaryBindingOnPropertyChanged;
                binding.Dispose();
            }
            DeviceBindings.Clear();
            foreach (var output in Plugins)
            {
                foreach (var binding in output.DeviceBindings)
                    binding.PropertyChanged -= SummaryBindingOnPropertyChanged;
                output.Dispose();
            }

            Mapping.UseInputExpression = false;
            var former = Mapping.DeviceBindings.ToList();
            Mapping.DeviceBindings.Clear();
            for (var i = 0; i < inputSignature.Count; i++)
            {
                var previous = i < former.Count && i < oldInputCategories.Count &&
                    oldInputCategories[i] == inputSignature[i] ? former[i] : null;
                if (previous != null)
                {
                    previous.SetInputExpressionGroup(0);
                    previous.SetInputExpressionNegated(false);
                    Mapping.DeviceBindings.Add(previous);
                }
                else
                {
                    Mapping.DeviceBindings.Add(new DeviceBinding(Mapping.Update, profile, DeviceIoType.Input)
                    {
                        DeviceConfigurationGuid = profile.GetPrimaryDeviceConfiguration(DeviceIoType.Input)?.Guid ?? Guid.Empty
                    });
                }
            }

            Mapping.Plugins.Clear();
            Mapping.Plugins.AddRange(replacements);
            Plugins.Clear();
            foreach (var next in replacements)
            {
                var viewModel = new PluginViewModel(this, next);
                Plugins.Add(viewModel);
                foreach (var binding in viewModel.DeviceBindings)
                    SubscribeSummaryBinding(binding);
            }
            PopulateDeviceBindingsViewModels();
            foreach (var binding in DeviceBindings) SubscribeSummaryBinding(binding);
            profile.Context.ContextChanged();
            RefreshHeaderState();
            RefreshInputExpressionPresentation();
            RefreshPresetAvailability();
            RefreshCollapsedSummary();
            profile.PruneUndefinedFilterReferencesRecursive();
            ProfileViewModel.RefreshFilterReferenceLabels();
            Logger.Info("Changed input category in mapping '" + MappingTitle + "' to " + replacementTemplate.PluginName);
            return true;
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

            StopQuickInputDetection();
            _quickOutputDetectionCancellation?.Cancel();
            StopQuickOutputCountdown();

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
