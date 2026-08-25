using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Media;
using HidWizards.UCR.Utilities;

namespace HidWizards.UCR.ViewModels.Dialogs
{
    public sealed class AccentOptionViewModel
    {
        public string Name { get; set; }
        public Brush Brush { get; set; }
    }

    public class AppearanceDialogViewModel : INotifyPropertyChanged
    {
        public ObservableCollection<AccentOptionViewModel> Options { get; }

        private AccentOptionViewModel _selectedOption;
        public AccentOptionViewModel SelectedOption
        {
            get => _selectedOption;
            set
            {
                if (_selectedOption == value) return;
                _selectedOption = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedOption)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedAccentName)));
            }
        }

        public string SelectedAccentName => SelectedOption?.Name;

        public AppearanceDialogViewModel()
        {
            Options = new ObservableCollection<AccentOptionViewModel>();
            foreach (var palette in AppearanceManager.AvailablePalettes)
            {
                var option = new AccentOptionViewModel
                {
                    Name = palette.Name,
                    Brush = AppearanceManager.BrushFor(palette.Name)
                };
                Options.Add(option);
                if (palette.Name == AppearanceManager.CurrentAccentName) SelectedOption = option;
            }

            if (SelectedOption == null && Options.Count > 0) SelectedOption = Options[0];
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
