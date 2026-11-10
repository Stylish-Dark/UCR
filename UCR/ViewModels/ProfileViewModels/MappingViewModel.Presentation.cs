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
        private BindingVisualDescriptor DescribeCompoundVisual(
            IList<DeviceBindingViewModel> channels, bool output)
        {
            var selected = channels?.FirstOrDefault(candidate => candidate.DeviceBinding.IsBound)
                ?? channels?.FirstOrDefault();
            var visual = DescribeCollapsedBinding(selected);
            if (selected == null) return visual;

            var profile = ProfileViewModel.Profile;
            var binding = selected.DeviceBinding;
            var device = profile.GetDeviceConfiguration(
                output ? DeviceIoType.Output : DeviceIoType.Input,
                binding.DeviceConfigurationGuid);
            var deviceKind = device?.Device == null ? DeviceVisualKind.Unknown :
                DeviceVisualCatalog.Describe(device, profile,
                    output ? DeviceIoType.Output : DeviceIoType.Input).Kind;
            var names = channels.Select(channel => channel.DeviceBinding.IsBound
                ? channel.CompactBindButtonText : string.Empty).ToList();
            while (names.Count < 4) names.Add(string.Empty);
            if (channels.Any(channel =>
                channel.DeviceBindingCategory == DeviceBindingCategory.Range))
            {
                visual.ControlKind = ControlVisualKind.CompoundStick;
                var rightStick = names.Any(name =>
                    name.IndexOf("Right", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.StartsWith("RX", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("RY", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("RS", StringComparison.OrdinalIgnoreCase)) ||
                    channels.Where(channel => channel.DeviceBinding.IsBound &&
                            channel.DeviceBindingCategory == DeviceBindingCategory.Range)
                        .Any(channel => channel.DeviceBinding.KeyValue == 2 ||
                            channel.DeviceBinding.KeyValue == 3);
                visual.ControlLabel = rightStick ? "R" : "L";
            }
            else if (deviceKind == DeviceVisualKind.Keyboard)
            {
                visual.ControlKind = ControlVisualKind.CompoundKeys;
                // Providers may report "Keys,A" or "Keyboard,Keys,A".
                // The physical keycaps must show A, D, W, S, not category paths.
                var keys = names.Select(name =>
                {
                    var label = name ?? string.Empty;
                    var separator = Math.Max(label.LastIndexOf(','), label.LastIndexOf(':'));
                    return (separator >= 0 ? label.Substring(separator + 1) : label).Trim();
                });
                visual.ControlLabel = string.Join("|", keys.Take(4));
            }
            else
            {
                var dpad = names.Any(name =>
                    name.IndexOf("D-pad", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("Dpad", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("POV", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("Hat", StringComparison.OrdinalIgnoreCase) >= 0);
                visual.ControlKind = dpad || !channels.Any(channel =>
                    channel.DeviceBinding.IsBound)
                    ? ControlVisualKind.DPad : ControlVisualKind.CompoundFaceButtons;
                visual.ControlLabel = dpad ? string.Empty : string.Join("|", names.Take(4));
            }
            visual.IsBound = channels.Any(channel => channel.DeviceBinding.IsBound);
            visual.IsCompoundVisual = true;
            visual.CompoundPresetName = visual.IsBound
                ? ResolveCompoundPresetName(channels, output) ?? "Custom 4-Way" : "4-Way";
            visual.ToolTip = visual.CompoundPresetName +
                " — choose a complete D-pad, stick, face-button or keyboard preset";
            visual.JoinSymbol = "+";
            return visual;
        }

        private List<CollapsedConditionVisualRow> BuildCollapsedInputRows()
        {
            var result = new List<CollapsedConditionVisualRow>();
            // The collapsed preview and the inspector must render the SAME logical
            // AND/OR groups. Never flatten all inputs into one row merely because
            // an output plugin reports a different legacy input signature.
            var groups = UsesInputExpression
                ? InputConditions.OrderBy(condition => condition.GroupIndex)
                    .Select(condition => condition.Inputs.ToList()).ToList()
                : new List<List<DeviceBindingViewModel>> { DeviceBindings.ToList() };

            for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                var bindings = groups[groupIndex];
                if (IsTypedDirectionalMapping && InputIsFullStick)
                {
                    var mainChannels = bindings.Where(binding =>
                        !binding.DeviceBinding.IsDirectionalAndCondition).ToList();
                    var guarded = bindings.Where(binding =>
                        binding.DeviceBinding.IsDirectionalAndCondition).ToList();
                    var groupedVisuals = new List<BindingVisualDescriptor>
                    {
                        DescribeCompoundVisual(mainChannels, false)
                    };
                    foreach (var guard in guarded)
                        groupedVisuals.Add(DescribeCollapsedBinding(guard));
                    result.Add(new CollapsedConditionVisualRow
                    {
                        Visuals = groupedVisuals,
                        StartsAlternative = groupIndex > 0
                    });
                    continue;
                }
                var compass = BuildCollapsedCompass(bindings, groupIndex > 0);
                if (compass != null)
                {
                    result.Add(compass);
                    bindings = bindings.Where(binding => !compass.Visuals.Any(
                        direction => direction.BindingGuid == binding.DeviceBinding.Guid)).ToList();
                }

                var visuals = new List<BindingVisualDescriptor>();
                for (var i = 0; i < bindings.Count; i++)
                {
                    var visual = DescribeCollapsedBinding(bindings[i]);
                    if (i > 0 && bindings[i].DeviceBinding.InputDirectionSlot > 0 &&
                        bindings[i - 1].DeviceBinding.InputDirectionSlot > 0)
                        visual.JoinSymbol = "∨";
                    visuals.Add(visual);
                }
                var chunkSize = bindings.Any(binding =>
                    binding.DeviceBinding.InputDirectionSlot > 0) ? 4 : 3;
                for (var start = 0; start < visuals.Count; start += chunkSize)
                    result.Add(new CollapsedConditionVisualRow
                    {
                        Visuals = visuals.Skip(start).Take(chunkSize).ToList(),
                        StartsAlternative = compass == null && groupIndex > 0 && start == 0,
                        ContinuesWithGuard = compass != null && start == 0
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

        private CollapsedConditionVisualRow BuildCollapsedCompass(
            List<DeviceBindingViewModel> bindings, bool startsAlternative)
        {
            var directions = bindings.Where(binding => !binding.DeviceBinding.IsDirectionalAndCondition &&
                    binding.DeviceBinding.InputDirectionSlot > 0)
                .OrderBy(binding => binding.DeviceBinding.InputDirectionSlot).ToList();
            if (!directions.Select(binding => binding.DeviceBinding.InputDirectionSlot)
                    .SequenceEqual(new[] { 1, 2, 3, 4 }))
            {
                if (directions.Select(binding => binding.DeviceBinding.InputDirectionSlot)
                    .SequenceEqual(new[] { 1, 2 }))
                {
                    var vertical = string.Equals(directions[0].DeviceBindingName, "Up",
                        StringComparison.OrdinalIgnoreCase);
                    return new CollapsedConditionVisualRow
                    {
                        StartsAlternative = startsAlternative,
                        IsDirectionalCompass = true,
                        Visuals = vertical
                            ? new List<BindingVisualDescriptor>
                            {
                                DescribeCompassSlot(directions[0], "Up", 0, 1),
                                DescribeCompassSlot(directions[1], "Down", 2, 1)
                            }
                            : new List<BindingVisualDescriptor>
                            {
                                DescribeCompassSlot(directions[0], "Left", 1, 0),
                                DescribeCompassSlot(directions[1], "Right", 1, 2)
                            }
                    };
                }
                // Legacy four-button routes store Up, Down, Left, Right in plugin order.
                if (directions.Count != 0 || bindings.Count != 4 ||
                    Plugins.Count != 1 || !Plugins[0].IsFourButtonAxis) return null;
                directions = new List<DeviceBindingViewModel> { bindings[2], bindings[3], bindings[0], bindings[1] };
            }

            return new CollapsedConditionVisualRow
            {
                StartsAlternative = startsAlternative,
                IsDirectionalCompass = true,
                Visuals = new List<BindingVisualDescriptor>
                {
                    DescribeCompassSlot(directions[2], "Up", 0, 1),
                    DescribeCompassSlot(directions[0], "Left", 1, 0),
                    DescribeCompassSlot(directions[1], "Right", 1, 2),
                    DescribeCompassSlot(directions[3], "Down", 2, 1)
                }
            };
        }

        private BindingVisualDescriptor DescribeCompassSlot(DeviceBindingViewModel binding,
            string direction, int row, int column)
        {
            var visual = DescribeCollapsedBinding(binding);
            visual.DirectionalName = direction;
            visual.DirectionalRow = row;
            visual.DirectionalColumn = column;
            visual.ToolTip = direction + " — " + visual.ToolTip;
            return visual;
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
            // A 4-Way output is one logical destination even though its runtime
            // routes to two analogue or four digital physical channels.
            if (IsCompoundOutput)
            {
                var channels = Plugins.SelectMany(plugin => plugin.DeviceBindings)
                    .Where(binding => binding.DeviceBinding.DeviceIoType == DeviceIoType.Output)
                    .ToList();
                result.Add(DescribeCompoundVisual(channels, true));
                return result;
            }
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
            descriptor.IsCapturing = viewModel.DeviceBinding?.IsInBindMode == true;
            if (descriptor.IsCapturing)
                descriptor.ToolTip = "LISTENING — right-click to cancel";
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
    }
}
