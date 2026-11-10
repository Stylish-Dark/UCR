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
        public bool ChangeAllOutputTypes(Plugin replacementTemplate)
        {
            return ChangeOutputTypes(Plugins.ToList(), replacementTemplate);
        }

        // Replace only the requested output rows. Shared input bindings belong to the mapping.
        public bool ChangeOutputType(PluginViewModel original, Plugin replacementTemplate)
        {
            return ChangeOutputTypes(new[] { original }, replacementTemplate);
        }

        private bool ChangeOutputTypes(IList<PluginViewModel> originals, Plugin replacementTemplate)
        {
            if (!ButtonsEnabled || replacementTemplate == null || originals.Count == 0 ||
                !Mapping.GetPluginList().Any(option => option.GetType() == replacementTemplate.GetType()))
                return false;

            var indices = originals.Select(original => original == null ? -1 : Plugins.IndexOf(original)).ToList();
            if (indices.Any(index => index < 0 || index >= Mapping.Plugins.Count)) return false;
            var changes = indices.Where(index => Mapping.Plugins[index].GetType() != replacementTemplate.GetType())
                .ToList();
            if (changes.Count == 0) return true;

            var enableInputExpression = !Mapping.UseInputExpression && changes.Any(index =>
                !Mapping.Plugins[index].HasSameInputCategories(replacementTemplate));
            if (enableInputExpression && (!Mapping.SupportsNativeInputExpression() || Mapping.DeviceBindings.Count != 1))
                return false;

            var banks = Mapping.InactiveOutputRepresentations?.ToList() ?? new List<DirectionalOutputBank>();
            var selectingFilter = IsFilterOutput(replacementTemplate);
            var category = replacementTemplate.OutputCategories.FirstOrDefault().Category;
            var prepared = new List<KeyValuePair<int, PluginViewModel>>();
            try
            {
                // Create and validate every replacement before touching the live collection.
                foreach (var index in changes)
                {
                    var previous = Mapping.Plugins[index];
                    var replacement = selectingFilter ? null : FindSavedPhysicalOutput(previous, category, banks);
                    if (replacement == null)
                    {
                        replacement = ProfileViewModel.Profile.Context.PluginManager.GetNewPlugin(replacementTemplate);
                        replacement.SetProfile(ProfileViewModel.Profile);
                        replacement.Filters = previous.Filters.ToList();
                        replacement.FilterRestoreKey = previous.FilterRestoreKey;
                        for (var position = 0; position < Math.Min(previous.Outputs.Count, replacement.Outputs.Count); position++)
                        {
                            if (previous.Outputs[position].IsBound &&
                                previous.OutputCategories[position].Category == replacement.OutputCategories[position].Category)
                                CopyPhysicalOutput(previous.Outputs[position], replacement.Outputs[position]);
                        }
                        SelectCompatibleOutputDevices(replacement,
                            previous.Outputs.Select(binding => binding.DeviceConfigurationGuid).ToList());
                    }

                    if (selectingFilter && !IsFilterOutput(previous) && previous.Outputs.Count > 0)
                    {
                        var key = previous.FilterRestoreKey == Guid.Empty ? Guid.NewGuid() : previous.FilterRestoreKey;
                        replacement.FilterRestoreKey = key;
                        banks.RemoveAll(bank => bank.FilterRestoreKey == key);
                        banks.Add(new DirectionalOutputBank { FilterRestoreKey = key, Plugins = new List<Plugin> { previous } });
                    }
                    prepared.Add(new KeyValuePair<int, PluginViewModel>(index, new PluginViewModel(this, replacement)));
                }
            }
            catch (Exception exception)
            {
                foreach (var item in prepared) item.Value.Dispose();
                Logger.Error("Unable to prepare the output type change.", exception);
                return false;
            }

            if (enableInputExpression && !Mapping.EnableInputExpression())
            {
                foreach (var item in prepared) item.Value.Dispose();
                return false;
            }

            Mapping.InactiveOutputRepresentations = banks;
            foreach (var item in prepared)
                Mapping.Plugins[item.Key] = item.Value.Plugin;
            foreach (var item in prepared)
            {
                var old = Plugins[item.Key];
                foreach (var binding in old.DeviceBindings)
                    binding.PropertyChanged -= SummaryBindingOnPropertyChanged;
                old.Dispose();
                Plugins[item.Key] = item.Value;
                foreach (var binding in item.Value.DeviceBindings)
                    SubscribeSummaryBinding(binding);
            }
            ProfileViewModel.Profile.PruneUndefinedFilterReferencesRecursive();
            ProfileViewModel.Profile.Context.ContextChanged();
            RefreshHeaderState();
            RefreshCollapsedSummary();
            ProfileViewModel.RefreshFilterReferenceLabels();
            RefreshInputExpressionPresentation();
            // Remove unused paired-input presentation after the entire batch is installed.
            if (!HasAxisOutput && Mapping.DeviceBindings.Any(input => input.InputDirectionSlot > 0) &&
                !selectingFilter)
                RestoreBooleanInputsAfterAxis();
            Logger.Info("Changed output type in mapping '" + MappingTitle + "' to " + replacementTemplate.PluginName);
            return true;
        }

        private static bool IsFilterOutput(Plugin plugin) =>
            string.Equals(plugin.Group, "Filter", StringComparison.OrdinalIgnoreCase);

        private static bool HasPhysicalOutputCategory(Plugin plugin, DeviceBindingCategory category) =>
            plugin != null && plugin.OutputCategories.Count > 0 &&
            plugin.OutputCategories.All(output => output.Category == category);

        private Plugin FindSavedPhysicalOutput(Plugin current, DeviceBindingCategory category,
            List<DirectionalOutputBank> banks, int? representation = null)
        {
            if (current.FilterRestoreKey != Guid.Empty)
            {
                var bank = banks.FirstOrDefault(item => item.FilterRestoreKey == current.FilterRestoreKey);
                var saved = bank?.Plugins?.SingleOrDefault();
                if (saved == null || !HasPhysicalOutputCategory(saved, category)) return null;
                banks.Remove(bank);
                return saved;
            }

            if (!IsFilterOutput(current)) return null;
            var index = Mapping.Plugins.IndexOf(current);
            // Older builds saved one whole bank. Leave it available for its other rows.
            var legacy = banks.LastOrDefault(item => item.FilterRestoreKey == Guid.Empty &&
                (!representation.HasValue || item.Representation == representation.Value) &&
                item.Plugins?.Count == Mapping.Plugins.Count && item.Plugins[index] != null &&
                HasPhysicalOutputCategory(item.Plugins[index], category));
            return legacy?.Plugins[index].Duplicate();
        }

        private List<Plugin> RestoreLegacyPhysicalOutputs(IList<Plugin> current,
            DeviceBindingCategory category, int representation)
        {
            if (!current.All(IsFilterOutput)) return null;
            var banks = Mapping.InactiveOutputRepresentations.ToList();
            var next = current.Select(plugin => FindSavedPhysicalOutput(plugin, category, banks, representation)).ToList();
            if (next.Any(plugin => plugin == null)) return null;
            Mapping.InactiveOutputRepresentations = banks;
            return next;
        }

        private static void CopyPhysicalOutput(DeviceBinding source, DeviceBinding destination)
        {
            destination.DeviceConfigurationGuid = source.DeviceConfigurationGuid;
            destination.KeyType = source.KeyType;
            destination.KeyValue = source.KeyValue;
            destination.KeySubValue = source.KeySubValue;
            destination.OutputAxisDirection = source.OutputAxisDirection;
            destination.IsBound = source.IsBound;
        }

        private void RestoreBooleanInputsAfterAxis()
        {
            foreach (var group in Mapping.DeviceBindings.Where(input => input.InputDirectionSlot > 0)
                         .GroupBy(input => input.InputExpressionGroup).ToList())
            {
                var primary = group.OrderBy(input => input.InputDirectionSlot).First();
                foreach (var extra in group.Where(input => input != primary).ToList())
                    Mapping.DeviceBindings.Remove(extra);
                if (primary.DeviceBindingCategory != DeviceBindingCategory.Momentary)
                {
                    primary.ClearBinding();
                }
                primary.InputDirectionSlot = 0;
                primary.InputExpressionInputKind = 0;
                primary.InputExpressionKindConfigured = true;
                primary.DeviceBindingCategory = DeviceBindingCategory.Momentary;
            }
            RebuildDirectionalEditors();
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
                if (selected != null && !plugin.Outputs[index].IsBound)
                    plugin.Outputs[index].DeviceConfigurationGuid = selected.Guid;
            }
        }

        public void RemoveOptionalCompoundOutput()
        {
            // X removes only an independently added output, never the canonical
            // channels required for the mapping's selected representation.
            if (!ButtonsEnabled || !IsTypedDirectionalMapping ||
                Plugins.Count <= Mapping.RequiredOutputBindingSlots) return;
            RemovePlugin(Plugins.Last());
            OnPropertyChanged(nameof(CompoundOutputRemoveVisibility));
        }

        public void RemovePlugin(PluginViewModel pluginViewModel)
        {
            if (!ButtonsEnabled || pluginViewModel?.CanRemove != true) return;
            if (!Mapping.RemovePlugin(pluginViewModel.Plugin)) return;

            pluginViewModel.Dispose();
            Plugins.Remove(pluginViewModel);
            RefreshHeaderState();
            RefreshCollapsedSummary();
            ProfileViewModel.RefreshFilterReferenceLabels();
            // The inputs remain available if the final output is removed.
            RefreshInputExpressionPresentation();
            RefreshRepresentationSelections();
        }

        private void PopulatePlugins(Mapping mapping)
        {
            Plugins = new ObservableCollection<PluginViewModel>();
            foreach (var mappingPlugin in mapping.Plugins)
            {
                Plugins.Add(new PluginViewModel(this, mappingPlugin));
            }
        }
    }
}
