using System.Collections.Generic;
using System.Windows.Controls;
using HidWizards.UCR.ViewModels.Dialogs;

namespace HidWizards.UCR.Views.Dialogs
{
    public partial class FilterSelectDialog : UserControl
    {
        public FilterSelectDialog(IEnumerable<string> filterNames, bool allowNew = false)
        {
            DataContext = new FilterSelectDialogViewModel(filterNames, allowNew);
            InitializeComponent();
        }

        private void NewFilter_OnClick(object sender, System.Windows.RoutedEventArgs e)
        {
            FilterNamesCombo.SelectedIndex = -1;
            FilterNamesCombo.Text = string.Empty;
            FilterNamesCombo.Focus();
            System.Windows.Input.Keyboard.Focus(FilterNamesCombo);
        }
    }
}
