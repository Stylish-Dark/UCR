using System.Collections.Generic;
using System.Windows.Controls;
using HidWizards.UCR.ViewModels.Dialogs;

namespace HidWizards.UCR.Views.Dialogs
{
    public partial class FilterSelectDialog : UserControl
    {
        public FilterSelectDialog(IEnumerable<string> filterNames)
        {
            DataContext = new FilterSelectDialogViewModel(filterNames);
            InitializeComponent();
        }
    }
}
