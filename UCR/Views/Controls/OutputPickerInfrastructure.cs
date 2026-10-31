using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using HidWizards.UCR.Core.Models.Binding;

namespace HidWizards.UCR.Views.Controls
{
    // Shared provider traversal and popup lifetime for all output pickers.
    internal static class OutputPickerInfrastructure
    {
        internal static IEnumerable<DeviceBindingNode> BindingControls(IEnumerable<DeviceBindingNode> nodes)
        {
            if (nodes == null) yield break;
            foreach (var node in nodes)
            {
                if (node == null) continue;
                if (node.IsBinding && node.DeviceBindingInfo != null) yield return node;
                foreach (var child in BindingControls(node.ChildrenNodes))
                    yield return child;
            }
        }

        internal static void OpenMenu(FrameworkElement target, ContextMenu menu)
        {
            if (target == null || menu == null) return;
            if (target.ContextMenu != null && target.ContextMenu.IsOpen)
                target.ContextMenu.IsOpen = false;
            menu.Closed += (sender, args) =>
            {
                if (ReferenceEquals(target.ContextMenu, menu))
                    target.ContextMenu = null;
            };
            target.ContextMenu = menu;
            menu.IsOpen = true;
        }
    }
}
