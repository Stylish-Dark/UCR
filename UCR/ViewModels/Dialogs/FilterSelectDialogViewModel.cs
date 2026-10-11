using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace HidWizards.UCR.ViewModels.Dialogs
{
    public class FilterSelectDialogViewModel : System.ComponentModel.INotifyPropertyChanged
    {
        public bool AllowNew { get; }
        public string Title => AllowNew ? "Choose or create filter" : "Add filter";
        public string Prompt => AllowNew ? "Choose an existing filter or create a new one." :
            "Choose an existing filter.";
        public ObservableCollection<string> FilterNames { get; }

        private string _selectedExistingFilter;
        public string SelectedExistingFilter
        {
            get => _selectedExistingFilter;
            set
            {
                if (_selectedExistingFilter == value) return;
                _selectedExistingFilter = value;
                if (!string.IsNullOrWhiteSpace(value)) SelectedFilter = value;
            }
        }

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

        public bool HasSelection => !string.IsNullOrWhiteSpace(SelectedFilter) &&
            (AllowNew || FilterNames.Any(value => string.Equals(value, SelectedFilter.Trim(),
                System.StringComparison.OrdinalIgnoreCase)));
        public FilterSelectDialogViewModel ViewModel => this;

        public FilterSelectDialogViewModel()
        {
            FilterNames = new ObservableCollection<string>();
        }

        public FilterSelectDialogViewModel(IEnumerable<string> filterNames, bool allowNew = false) : this()
        {
            AllowNew = allowNew;
            if (filterNames == null) return;
            foreach (var filterName in filterNames)
            {
                if (!string.IsNullOrWhiteSpace(filterName)) FilterNames.Add(filterName);
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
    }
}
