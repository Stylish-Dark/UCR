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
            if (e.Key != Key.Enter && e.Key != Key.Return) return;
            var textBox = sender as TextBox;
            if (textBox == null) return;

            textBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            e.Handled = true;
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
