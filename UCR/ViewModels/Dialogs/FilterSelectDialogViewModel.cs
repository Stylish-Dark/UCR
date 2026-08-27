using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace HidWizards.UCR.ViewModels.Dialogs
{
    public class FilterSelectDialogViewModel : System.ComponentModel.INotifyPropertyChanged
    {
        public string Title => "Add filter";
        public ObservableCollection<string> FilterNames { get; }

        private string _selectedFilter;
        public string SelectedFilter
        {
            get => _selectedFilter;
            set
            {
                if (_selectedFilter == value) return;
                _selectedFilter = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(SelectedFilter)));
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(HasSelection)));
            }
        }

        public bool HasSelection => !string.IsNullOrWhiteSpace(SelectedFilter);
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

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
    }
}
