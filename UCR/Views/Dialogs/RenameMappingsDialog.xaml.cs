using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using HidWizards.UCR.ViewModels.Dialogs;
using HidWizards.UCR.ViewModels.ProfileViewModels;

namespace HidWizards.UCR.Views.Dialogs
{
    public partial class RenameMappingsDialog : UserControl
    {
        public RenameMappingsDialog(ProfileViewModel profileViewModel)
        {
            DataContext = new RenameMappingsDialogViewModel(profileViewModel);
            InitializeComponent();
        }

        private void RenameMappingsDialog_OnLoaded(object sender, RoutedEventArgs e)
        {
            var first = FindVisualChild<TextBox>(this);
            if (first == null) return;
            first.Focus();
            first.SelectAll();
        }

        private void MappingName_OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            var textBox = sender as TextBox;
            if (textBox == null) return;

            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key != Key.Tab && key != Key.Enter && key != Key.Return) return;

            var direction = key == Key.Tab && (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift ? -1 : 1;
            if (MoveToAdjacentMappingName(textBox, direction))
            {
                e.Handled = true;
                return;
            }

            // Enter should never accidentally submit the dialog just because the current row is last.
            if (key == Key.Enter || key == Key.Return) e.Handled = true;
        }

        private bool MoveToAdjacentMappingName(TextBox current, int direction)
        {
            var textBoxes = GetMappingNameTextBoxes();
            var currentIndex = textBoxes.IndexOf(current);
            if (currentIndex < 0) return false;

            var targetIndex = currentIndex + direction;
            if (targetIndex < 0 || targetIndex >= textBoxes.Count) return false;

            var target = textBoxes[targetIndex];
            target.BringIntoView();
            target.Focus();
            Keyboard.Focus(target);
            target.SelectAll();
            return true;
        }

        private List<TextBox> GetMappingNameTextBoxes()
        {
            var result = new List<TextBox>();
            FindVisualChildren(MappingItemsControl, result);
            return result;
        }

        private static void FindVisualChildren<T>(DependencyObject parent, ICollection<T> result) where T : DependencyObject
        {
            if (parent == null) return;
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            {
                var child = VisualTreeHelper.GetChild(parent, index);
                var match = child as T;
                if (match != null) result.Add(match);
                FindVisualChildren(child, result);
            }
        }

        private static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            {
                var child = VisualTreeHelper.GetChild(parent, index);
                var match = child as T;
                if (match != null) return match;
                var nested = FindVisualChild<T>(child);
                if (nested != null) return nested;
            }
            return null;
        }
    }
}
