using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace HidWizards.UCR.ViewModels.ProfileViewModels
{
    // Presentation-only grouping of output bindings; persistence remains owned by Mapping.
    public partial class MappingViewModel
    {
        private List<OutputBindingSetViewModel> BuildOutputBindingSets()
        {
            var result = new List<OutputBindingSetViewModel>();
            if (Plugins == null || Plugins.Count == 0) return result;
            var fullStick = string.Equals(Mapping.OutputSideType,
                "Full Joystick", StringComparison.OrdinalIgnoreCase);
            var count = Mapping.DirectionalButtonOutputs ? 4 :
                Mapping.DirectionalPairButtonOutputs || fullStick ? 2 : 1;
            for (var start = 0; start < Plugins.Count; start += count)
            {
                var members = Plugins.Skip(start).Take(count).ToList();
                var slots = new List<OutputBindingSlotViewModel>();
                for (var i = 0; i < members.Count; i++)
                {
                    var label = Mapping.DirectionalButtonOutputs
                        ? new[] { "Left", "Right", "Up", "Down" }[i] :
                        fullStick ? (i == 0 ? "X" : "Y") :
                        Mapping.DirectionalPairButtonOutputs
                            ? (i == 0 ? "−" : "+") : "";
                    foreach (var binding in members[i].DeviceBindings)
                        slots.Add(new OutputBindingSlotViewModel
                        {
                            Plugin = members[i], Binding = binding, Direction = label
                        });
                }
                var multipleSets = Plugins.Count > count;
                var groupLabel = fullStick ? "Full joystick" :
                    Mapping.DirectionalButtonOutputs ? "4-way" :
                    Mapping.DirectionalPairButtonOutputs ? "Two directions" :
                    multipleSets ? "Output " + (start + 1) : "";
                result.Add(new OutputBindingSetViewModel
                {
                    Index = start / count,
                    Members = members,
                    Slots = slots,
                    Columns = fullStick ? 2 : Mapping.DirectionalButtonOutputs ? 4 :
                        Mapping.DirectionalPairButtonOutputs ? 2 : 1,
                    Caption = groupLabel,
                    IsFourWay = Mapping.DirectionalButtonOutputs && members.Count == 4,
                    IsFullJoystick = fullStick && members.Count == 2,
                    RemoveVisibility = multipleSets ? Visibility.Visible : Visibility.Collapsed,
                    PresetCaption = fullStick && members.Count == 2
                        ? (members[0].Plugin.Outputs.FirstOrDefault()?.IsBound == true
                            ? (members[0].Plugin.Outputs[0].KeyValue == 0
                                ? "Left joystick" : members[0].Plugin.Outputs[0].KeyValue == 2
                                    ? "Right joystick" : "Full joystick")
                            : "Choose joystick") : "Preset"
                });
            }
            return result;
        }

    }
}
