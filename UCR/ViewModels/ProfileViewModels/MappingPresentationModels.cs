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
        public string VisualKeys { get; set; }
        public bool IsDPad { get; set; }
        // Input/output key descriptors are resolved before any binding is changed.
        public DeviceBindingInfo[] Controls { get; set; }
    }

    public sealed class QuickBindingOption
    {
        public string Title { get; set; }
        public Guid DeviceConfigurationGuid { get; set; }
        internal Device OutputDevice { get; set; }
        internal DeviceBindingInfo OutputControl { get; set; }
        public int KeyType { get; set; }
        public int KeyValue { get; set; }
        public int KeySubValue { get; set; }
        public BindingVisualDescriptor Visual { get; set; }
    }

    // One visual direction in the compass. Analogue sticks deliberately
    // share their X binding between Left/Right and Y between Up/Down.
    // Four-key D-pads instead reference four independent physical bindings.
    public sealed class DirectionalCompassSlotViewModel
    {
        public string Arrow { get; }
        public DeviceBindingViewModel PhysicalBinding { get; }

        public DirectionalCompassSlotViewModel(string arrow, DeviceBindingViewModel physicalBinding)
        {
            Arrow = arrow;
            PhysicalBinding = physicalBinding;
        }
    }

    public sealed class InputConditionViewModel : INotifyPropertyChanged
    {
        private readonly MappingViewModel _owner;
        public bool IsTypedDirectional => _owner?.IsTypedDirectionalMapping == true;
        public bool IsCompound => IsTypedDirectional && _owner.IsCompoundInput;
        public bool IsButtonPair => IsTypedDirectional && !IsCompound;
        public ObservableCollection<DeviceBindingViewModel> AndInputs { get; }
        public ObservableCollection<DeviceBindingViewModel> CoreInputs { get; }
        public Visibility CompoundVisibility => IsCompound ? Visibility.Visible : Visibility.Collapsed;
        // A full 4-Way input is one logical assignment. Keep its preset button,
        // but expose the actual physical channels with their directional labels.
        // A slash-joined key list hides which key belongs to which direction.
        // 4-Way is one logical compound binding in the editor. Its underlying
        // digital/analogue channels remain intact for routing and saved profiles.
        public Visibility CompoundChannelPreviewVisibility => Visibility.Collapsed;
        public Visibility StandaloneCompoundVisibility =>
            IsCompound ? Visibility.Visible : Visibility.Collapsed;
        public Visibility CompoundAdvancedMenuVisibility =>
            IsCompound && !_owner.InputIsFullStick && CoreInputs.Count == 1
                ? Visibility.Visible : Visibility.Collapsed;

        public string CompoundPrimaryToolTip => _owner?.InputIsFullStick == true
            ? "Choose a complete 4-Way input"
            : CoreInputs.Count == 1 ? CoreInputs[0].BindingActionToolTip : "Choose an input channel";

        public string CompoundPrimaryLabel => IsCompound && _owner.InputIsFullStick
            ? (CoreInputs.Any(input => input.DeviceBinding.IsBound)
                ? (_owner.ResolveCompoundPresetName(CoreInputs, false) ?? "Custom 4-Way") + " ▾"
                : "Select ▾")
            : CoreInputs.Count > 1 ? CompoundSummary + " ▾" : CompoundSummary;

        private DirectionalCompassSlotViewModel CompassSlot(int digitalSlot, int analogueIndex, string arrow)
        {
            // Expose the four semantic directions for routing/tests even though
            // the editor displays one compound binding instead of four buttons.
            if (!IsCompound || !_owner.InputIsFullStick || CoreInputs.Count <= 1) return null;
            DeviceBindingViewModel physical;
            if (CoreInputs.Count >= 4)
                physical = CoreInputs.FirstOrDefault(input =>
                    input.DeviceBinding.InputDirectionSlot == digitalSlot)
                    ?? CoreInputs.ElementAtOrDefault(digitalSlot - 1);
            else
                physical = CoreInputs.ElementAtOrDefault(analogueIndex);
            return physical == null ? null : new DirectionalCompassSlotViewModel(arrow, physical);
        }
        public DirectionalCompassSlotViewModel CompassInputLeft => CompassSlot(1, 0, "←");
        public DirectionalCompassSlotViewModel CompassInputRight => CompassSlot(2, 0, "→");
        public DirectionalCompassSlotViewModel CompassInputUp => CompassSlot(3, 1, "↑");
        public DirectionalCompassSlotViewModel CompassInputDown => CompassSlot(4, 1, "↓");
        public Visibility ButtonPairVisibility => IsButtonPair ? Visibility.Visible : Visibility.Collapsed;
        public string CompoundSummary
        {
            get
            {
                if (!IsCompound) return string.Empty;
                var labels = CoreInputs.Select(input => input.CompactBindButtonText).ToArray();
                return labels.Length == 0 ? "Bind" : string.Join(" / ", labels);
            }
        }
        public string CompoundDeviceLabel
        {
            get
            {
                var assigned = CoreInputs.Where(input => input.DeviceBinding?.IsBound == true).ToList();
                var source = assigned.Count > 0 ? assigned : CoreInputs.ToList();
                var selectedGuids = source.Where(input => input.DeviceBinding != null)
                    .Select(input => input.DeviceBinding.DeviceConfigurationGuid)
                    .Where(id => id != Guid.Empty).Distinct().ToList();
                if (selectedGuids.Count > 1) return "Mixed devices ▾";
                var guid = selectedGuids.FirstOrDefault();
                if (guid == Guid.Empty) return "Any device ▾";
                var profile = _owner?.ProfileViewModel?.Profile;
                var name = profile?.GetDeviceConfiguration(DeviceIoType.Input, guid)
                    ?.GetFullTitleForProfile(profile) ?? "Unavailable device";
                return (name.Length > 13 ? name.Substring(0, 12) + "…" : name) + " ▾";
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        public void RefreshCompoundSummary()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CompoundSummary)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CompoundPrimaryLabel)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CompoundPrimaryToolTip)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CompoundDeviceLabel)));
        }

        public int GroupIndex { get; }
        public ObservableCollection<DeviceBindingViewModel> Inputs { get; }
        public Visibility OrLabelVisibility => GroupIndex > 0 ? Visibility.Visible : Visibility.Collapsed;
        private readonly bool _positionalFourWay;
        private readonly ObservableCollection<DeviceBindingViewModel> _directionalInputs;
        private readonly ObservableCollection<DeviceBindingViewModel> _ordinaryInputs;

        // WPF evaluates these getters many times during measure/bind. Returning a
        // fresh ObservableCollection on every evaluation allocates and rebinds rows.
        // An input-condition group has a fixed membership until the mapping rebuilds it.
        public ObservableCollection<DeviceBindingViewModel> DirectionalInputs => _directionalInputs;
        public ObservableCollection<DeviceBindingViewModel> OrdinaryInputs => _ordinaryInputs;
        public DeviceBindingViewModel DirectionalPrimary => DirectionalInputs.FirstOrDefault();
        public bool DirectionalBlockEnabled => Inputs.Any(input => input.DeviceBinding.Block);
        public Visibility DirectionalVisibility => DirectionalPrimary == null ? Visibility.Collapsed : Visibility.Visible;
        public Visibility DirectionalPairVisibility =>
            DirectionalPrimary != null && !IsFourWay ? Visibility.Visible : Visibility.Collapsed;
        public Visibility FourWayVisibility => IsFourWay ? Visibility.Visible : Visibility.Collapsed;
        public bool HasDirectionalInputs => DirectionalPrimary != null;
        public bool IsFourWay => DirectionalInputs.Count == 4;
        public int DirectionalColumns => IsFourWay ? 4 : 2;
        public int ButtonPairColumns => CoreInputs.Count <= 1 ? 1 : 2;
        public bool IsSingleButton => IsButtonPair && CoreInputs.Count == 1;
        public Visibility SingleButtonVisibility => IsSingleButton ? Visibility.Visible : Visibility.Collapsed;
        public bool IsPlainAxisButtonPair => IsButtonPair && CoreInputs.Count == 2 &&
            _owner?.InputIsButton == true && _owner.OutputIsAxis;
        public Visibility PlainAxisButtonPairVisibility => IsPlainAxisButtonPair
            ? Visibility.Visible : Visibility.Collapsed;
        public Visibility DirectionalButtonsVisibility =>
            IsButtonPair && CoreInputs.Count > 1 && !IsPlainAxisButtonPair
                ? Visibility.Visible : Visibility.Collapsed;
        public Visibility GroupDevicePickerVisibility =>
            IsSingleButton || IsPlainAxisButtonPair
                ? Visibility.Collapsed : Visibility.Visible;
        private DeviceBindingViewModel Slot(int number) => CoreInputs.FirstOrDefault(input =>
            input.DeviceBinding.InputDirectionSlot == number);
        private bool IsVerticalTwoWay => CoreInputs.Count == 2 &&
            string.Equals(Slot(1)?.DeviceBindingName, "Up", StringComparison.OrdinalIgnoreCase);
        public DeviceBindingViewModel LeftDirection => CoreInputs.Count >= 2 && !IsVerticalTwoWay
            ? Slot(1) : null;
        public DeviceBindingViewModel RightDirection => CoreInputs.Count >= 2 && !IsVerticalTwoWay
            ? Slot(2) : null;
        public DeviceBindingViewModel UpDirection => CoreInputs.Count == 4 ? Slot(3)
            : IsVerticalTwoWay ? Slot(1) : null;
        public DeviceBindingViewModel DownDirection => CoreInputs.Count == 4 ? Slot(4)
            : IsVerticalTwoWay ? Slot(2) : null;
        public Visibility LeftDirectionVisibility => LeftDirection != null ? Visibility.Visible : Visibility.Collapsed;
        public Visibility RightDirectionVisibility => RightDirection != null ? Visibility.Visible : Visibility.Collapsed;
        public Visibility UpDirectionVisibility => UpDirection != null ? Visibility.Visible : Visibility.Collapsed;
        public Visibility DownDirectionVisibility => DownDirection != null ? Visibility.Visible : Visibility.Collapsed;
        public string DirectionalModeLabel => IsTypedDirectional
            ? _owner.InputDirectionalGroupTitle : IsFourWay ? "4-way joystick" : "Axis";
        public Visibility MatchingTypeCaptionVisibility =>
            _owner?.MatchingTypeCaptionVisibility ?? Visibility.Visible;
        // A single Button mapping uses the actual binding row; no dummy header row.
        public Visibility PrimaryDirectionalRowVisibility =>
            IsCompound || CanRemoveCondition || _owner?.UsesCongruentMappingLayout != true
                ? Visibility.Visible : Visibility.Collapsed;
        public Visibility LocalAddInputVisibility =>
            _owner?.SharedAddInputVisibility == Visibility.Visible
                ? Visibility.Collapsed : Visibility.Visible;

        public bool CanRemoveCondition { get; }

        public InputConditionViewModel(int groupIndex, IEnumerable<DeviceBindingViewModel> inputs,
            bool canRemoveCondition, bool positionalFourWay = false,
            MappingViewModel owner = null)
        {
            _owner = owner;
            GroupIndex = groupIndex;
            _positionalFourWay = positionalFourWay;
            Inputs = new ObservableCollection<DeviceBindingViewModel>(inputs ?? Enumerable.Empty<DeviceBindingViewModel>());
            CoreInputs = new ObservableCollection<DeviceBindingViewModel>(Inputs.Where(input =>
                !input.DeviceBinding.IsDirectionalAndCondition));
            AndInputs = new ObservableCollection<DeviceBindingViewModel>(Inputs.Where(input =>
                input.DeviceBinding.IsDirectionalAndCondition));
            var slots = _positionalFourWay
                ? Inputs.Take(4).ToList()
                : Inputs.Where(binding => binding.DeviceBinding.InputDirectionSlot > 0).ToList();
            if (!_positionalFourWay && slots.Count == 4)
            {
                // Preserve the existing compass order: Up, Down, Left, Right.
                slots = slots.OrderBy(binding =>
                {
                    switch (binding.DeviceBinding.InputDirectionSlot)
                    {
                        case 3: return 0;
                        case 4: return 1;
                        case 1: return 2;
                        case 2: return 3;
                        default: return 4;
                    }
                }).ToList();
            }
            else if (!_positionalFourWay)
            {
                slots = slots.OrderBy(binding => binding.DeviceBinding.InputDirectionSlot).ToList();
            }
            _directionalInputs = new ObservableCollection<DeviceBindingViewModel>(slots);
            _ordinaryInputs = new ObservableCollection<DeviceBindingViewModel>(_positionalFourWay
                ? Enumerable.Empty<DeviceBindingViewModel>()
                : Inputs.Where(binding => binding.DeviceBinding.InputDirectionSlot == 0));
            CanRemoveCondition = canRemoveCondition;
        }
    }

    public sealed class CollapsedConditionVisualRow
    {
        public List<BindingVisualDescriptor> Visuals { get; set; }
        public bool StartsAlternative { get; set; }
        public bool IsDirectionalCompass { get; set; }
        public bool ContinuesWithGuard { get; set; }
        public string DividerLabel => StartsAlternative ? "OR" : ContinuesWithGuard ? "AND" : string.Empty;
        public Visibility DividerVisibility =>
            StartsAlternative || ContinuesWithGuard ? Visibility.Visible : Visibility.Collapsed;
    }
}
