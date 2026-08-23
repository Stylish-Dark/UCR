using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace HidWizards.UCR.ViewModels.Dialogs
{
    public class FilterSelectDialogViewModel
    {
        public string Title => "Add filter";
        public ObservableCollection<string> FilterNames { get; }
        public string SelectedFilter { get; set; }
        public FilterSelectDialogViewModel ViewModel => this;

        public FilterSelectDialogViewModel()
        {
            FilterNames = new ObservableCollection<string>();
        }

        public FilterSelectDialogViewModel(IEnumerable<string> filterNames) : this()
        {
            if (filterNames == null) return;
            foreach (var filterName in filterNames)
            {
                if (!string.IsNullOrWhiteSpace(filterName)) FilterNames.Add(filterName);
            }
        }
    }
}
