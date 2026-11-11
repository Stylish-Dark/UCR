using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using HidWizards.UCR.Core.Utilities;
using HidWizards.UCR.Core.Models;
using HidWizards.UCR.Core.Models.Binding;
using HidWizards.UCR.Utilities;
using HidWizards.UCR.ViewModels;
using HidWizards.UCR.ViewModels.Presentation;
using HidWizards.UCR.ViewModels.ProfileViewModels;

namespace HidWizards.UCR.Views.Controls
{
    public partial class MappingCardControl
    {
        private void FilterOutput_OnClick(object sender, RoutedEventArgs e)
        {
            var mapping = DataContext as MappingViewModel;
            if (mapping?.ButtonsEnabled != true) return;
            var analogue = mapping.Mapping.DeviceBindings.Any(input =>
                input.InputExpressionInputKind == 3);
            var choices = mapping.GetCompatiblePluginOptions().Where(option =>
                string.Equals(option.OutputType, "Filter", StringComparison.OrdinalIgnoreCase));
            var selected = PreferredOutputImplementation(choices, analogue);
            if (selected == null || !mapping.ChangeAllOutputTypes(selected.Plugin))
                DarkMessageBox.Show("A compatible Filter output is unavailable.",
                    "Cannot select Filter", MessageBoxButton.OK, MessageBoxImage.Information);
            mapping.RefreshRepresentationSelections();
        }

        private void InputRepresentation_OnClick(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as MappingViewModel;
            int value;
            if (vm == null || !int.TryParse((sender as FrameworkElement)?.Tag as string, out value))
                return;
            // Trigger is the analogue form of the *selected* Button family.
            // Clicking an already-selected Button must not discard that
            // trigger's physical assignment by forcing representation 3.
            if (value == 3 && vm.InputIsTrigger) return;
            if (!vm.SetInputRepresentation(value))
            {
                DarkMessageBox.Show("The input representation could not be prepared. Check the available remapper plugins.",
                    "Unable to change input type", MessageBoxButton.OK, MessageBoxImage.Information);
                vm.RefreshRepresentationSelections();
                (sender as RadioButton)?.GetBindingExpression(ToggleButton.IsCheckedProperty)?.UpdateTarget();
            }
        }

        private void OutputRepresentation_OnClick(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as MappingViewModel;
            int value;
            if (vm == null || !int.TryParse((sender as FrameworkElement)?.Tag as string, out value))
                return;
            if (value == 3 && vm.OutputIsTrigger) return;
            // Filter's selected type supersedes the persisted physical route.
            // Returning to a type reconstructs or restores the correct output bank,
            // including the case where its representation number did not change.
            if (!vm.SetOutputRepresentation(value))
            {
                DarkMessageBox.Show("The output representation could not be prepared. Check the available remapper plugins.",
                    "Unable to change output type", MessageBoxButton.OK, MessageBoxImage.Information);
                vm.RefreshRepresentationSelections();
                (sender as RadioButton)?.GetBindingExpression(ToggleButton.IsCheckedProperty)?.UpdateTarget();
            }
        }

        private void AddAndInput_OnClick(object sender, RoutedEventArgs e)
        {
            (DataContext as MappingViewModel)?.AddAndInput();
        }

        private void AddOrInput_OnClick(object sender, RoutedEventArgs e)
        {
            (DataContext as MappingViewModel)?.AddOrInput();
        }

        private void AddInputToCondition_OnClick(object sender, RoutedEventArgs e)
        {
            var condition = (sender as FrameworkElement)?.DataContext as InputConditionViewModel;
            (DataContext as MappingViewModel)?.AddInputToCondition(condition);
        }

        private void AddCondition_OnClick(object sender, RoutedEventArgs e)
        {
            (DataContext as MappingViewModel)?.AddCondition();
        }

        private void MappingEditorColumns_OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Keep input and output editors usable in the narrow middle workspace.
            // Two columns need room for device, Bind, NOT and Remove controls.
            if (sender is UniformGrid editor)
            {
                var singleInspector = InspectorMode && !InlineWorkspaceMode;
                var desired = singleInspector || e.NewSize.Width < 650 ? 1 : 2;
                if (editor.Columns != desired)
                    editor.Columns = desired;
            }
        }

        private void RemoveOrClearInput_OnClick(object sender, RoutedEventArgs e)
        {
            var binding = (sender as FrameworkElement)?.DataContext as DeviceBindingViewModel;
            if (binding?.DeviceBinding == null) return;
            var mapping = DataContext as MappingViewModel;
            if (mapping == null || !mapping.ButtonsEnabled) return;
            if (binding.CanRemoveExpressionInput)
                mapping.RemoveExpressionInput(binding);
            else
                binding.DeviceBinding.ClearBinding();
        }

        private void RemoveExpressionInput_OnClick(object sender, RoutedEventArgs e)
        {
            var binding = (sender as FrameworkElement)?.DataContext as DeviceBindingViewModel;
            (DataContext as MappingViewModel)?.RemoveExpressionInput(binding);
        }

        private void RemoveCondition_OnClick(object sender, RoutedEventArgs e)
        {
            var condition = (sender as FrameworkElement)?.DataContext as InputConditionViewModel;
            (DataContext as MappingViewModel)?.RemoveCondition(condition);
        }

        private void SpatialInputOptions_OnClick(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var input = (button?.DataContext as DirectionalCompassSlotViewModel)?.PhysicalBinding ??
                        button?.DataContext as DeviceBindingViewModel;
            if (button == null || input?.DeviceBinding == null || !input.BindingEnabled ||
                (DataContext as MappingViewModel)?.ButtonsEnabled != true) return;
            var menu = CompactInputBindingControl.CreateInputOptionsMenu(input, button);
            var mapping = DataContext as MappingViewModel;
            var profile = mapping?.ProfileViewModel.Profile;
            if (profile != null)
            {
                var deviceMenu = new MenuItem { Header = "Input device" };
                var currentGuid = input.DeviceBinding.DeviceConfigurationGuid;
                var any = new MenuItem
                {
                    Header = "Any input device",
                    IsChecked = currentGuid == Guid.Empty
                };
                any.Click += (s, args) => mapping.SetDirectionalSlotDevice(input, Guid.Empty);
                deviceMenu.Items.Add(any);
                foreach (var device in profile.GetDeviceConfigurationList(DeviceIoType.Input))
                {
                    if (device?.Device == null) continue;
                    var guid = device.Guid;
                    var choice = new MenuItem
                    {
                        Header = device.GetFullTitleForProfile(profile),
                        IsChecked = guid == currentGuid
                    };
                    choice.Click += (s, args) => mapping.SetDirectionalSlotDevice(input, guid);
                    deviceMenu.Items.Add(choice);
                }
                menu.Items.Insert(0, new Separator());
                menu.Items.Insert(0, deviceMenu);
            }
            menu.Items.Add(new Separator());
            var block = new MenuItem
            {
                Header = "Block original input",
                IsCheckable = true,
                IsChecked = input.Block,
                IsEnabled = input.CanBlock,
                ToolTip = input.BlockAvailability
            };
            block.Click += (s, args) => input.Block = block.IsChecked;
            menu.Items.Add(block);
            var not = new MenuItem
            {
                Header = "NOT (invert this button condition)",
                IsCheckable = true,
                IsChecked = input.InputExpressionNegated
            };
            not.Click += (s, args) => mapping.SetInputNegated(input, not.IsChecked);
            menu.Items.Add(not);
            menu.IsOpen = true;
        }

        private void BindDirectionalSlot_OnClick(object sender, RoutedEventArgs e)
        {
            var data = (sender as FrameworkElement)?.DataContext;
            var input = (data as DirectionalCompassSlotViewModel)?.PhysicalBinding ??
                        data as DeviceBindingViewModel;
            if (input?.DeviceBinding == null) return;
            if (input.DeviceBinding.IsInBindMode)
            {
                input.DeviceBinding.CancelBindMode();
                return;
            }
            try
            {
                input.ExpressionMapping?.EnableInputExpression();
                input.DeviceBinding.DeviceBindingCategory = input.DeviceBindingCategory;
                input.DeviceBinding.EnterBindMode(autoDetectInputCategory: true);
            }
            catch (Exception exception)
            {
                Logger.Error("Unable to capture directional input", exception);
                DarkMessageBox.Show("Unable to detect this direction: " + exception.GetBaseException().Message,
                    "Input detection unavailable", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DirectionalCapture_OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var data = (sender as FrameworkElement)?.DataContext;
            var input = (data as DirectionalCompassSlotViewModel)?.PhysicalBinding ??
                        data as DeviceBindingViewModel;
            if (input?.DeviceBinding?.IsInBindMode != true) return;
            input.DeviceBinding.CancelBindMode();
            e.Handled = true;
        }

        private void DirectionalDevice_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var combo = sender as ComboBox;
            var group = combo?.DataContext as InputConditionViewModel;
            var selected = combo?.SelectedItem as ComboBoxItemViewModel;
            if (group?.DirectionalPrimary == null || selected == null) return;

            // WPF fires SelectionChanged when the template first loads. That must
            // not clear existing inputs unless the user chose a different device.
            var primary = group.DirectionalPrimary;
            if (selected.Value == primary.DeviceBinding.DeviceConfigurationGuid) return;
            foreach (var input in group.DirectionalInputs)
                input.ChangeDeviceConfiguration(selected.Value);
        }

        private void DirectionalType_OnClick(object sender, RoutedEventArgs e)
        {
            var item = sender as MenuItem;
            var button = (item?.Parent as ContextMenu)?.PlacementTarget as Button;
            var input = button?.DataContext as DeviceBindingViewModel ?? item?.DataContext as DeviceBindingViewModel;
            int kind;
            if (input == null || !int.TryParse(item?.Tag as string, out kind)) return;
            input.InputExpressionInputKind = kind;
        }

        private void DirectionalNot_OnClick(object sender, RoutedEventArgs e)
        {
            var group = (sender as FrameworkElement)?.DataContext as InputConditionViewModel;
            if (group?.DirectionalPrimary == null) return;
            (DataContext as MappingViewModel)?.SetInputNegated(group.DirectionalPrimary,
                (sender as ToggleButton)?.IsChecked == true);
        }

        private void DirectionalBlock_OnClick(object sender, RoutedEventArgs e)
        {
            var group = (sender as FrameworkElement)?.DataContext as InputConditionViewModel;
            var mapping = DataContext as MappingViewModel;
            if (group == null || mapping?.ButtonsEnabled != true) return;
            var enabled = (sender as ToggleButton)?.IsChecked == true;
            if (mapping.IsTypedDirectionalMapping)
            {
                foreach (var input in group.Inputs)
                {
                    var binding = input.DeviceBinding;
                    binding.SetBlock(enabled && binding.IsBound && binding.IsBlockable());
                }
                group.RefreshCompoundSummary();
                return;
            }
            if (group.DirectionalPrimary?.DeviceBinding?.InputDirectionSlot > 0)
            {
                mapping.SetDirectionalBlocked(group.DirectionalPrimary, enabled);
            }
            else
            {
                // The four-input ButtonsToAxes plugin is positional: it has
                // no InputDirectionSlot markers. Its group Block toggle still
                // applies to all four independently bindable physical inputs.
                foreach (var input in group.DirectionalInputs)
                {
                    var binding = input.DeviceBinding;
                    binding.SetBlock(enabled && binding.IsBound && binding.IsBlockable());
                }
            }
        }

        private void RemoveDirectionalUnit_OnClick(object sender, RoutedEventArgs e)
        {
            var group = (sender as FrameworkElement)?.DataContext as InputConditionViewModel;
            if (group == null) return;
            (DataContext as MappingViewModel)?.SetDirectionalMode(0);
        }

        private void InputNegated_OnClick(object sender, RoutedEventArgs e)
        {
            var toggle = sender as ToggleButton;
            var binding = (toggle?.DataContext as DirectionalCompassSlotViewModel)?.PhysicalBinding ??
                          toggle?.DataContext as DeviceBindingViewModel;
            if (toggle == null || binding == null) return;
            (DataContext as MappingViewModel)?.SetInputNegated(binding, toggle.IsChecked == true);
        }

        private void AddPlugin_OnClick(object sender, RoutedEventArgs e)
        {
            var mapping = DataContext as MappingViewModel;
            if (mapping?.ButtonsEnabled != true) return;
            mapping.AddDefaultOutput();
        }
    }
}
