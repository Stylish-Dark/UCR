using System.Collections.ObjectModel;
using System.Windows.Media;
using HidWizards.UCR.Utilities;

namespace HidWizards.UCR.ViewModels.Dialogs
{
    public sealed class AccentOptionViewModel
    {
        public string Name { get; set; }
        public Brush Brush { get; set; }
        public bool IsCurrent { get; set; }
    }

    public class AppearanceDialogViewModel
    {
        public ObservableCollection<AccentOptionViewModel> Options { get; }

        public AppearanceDialogViewModel()
        {
            Options = new ObservableCollection<AccentOptionViewModel>();
            foreach (var palette in AppearanceManager.AvailablePalettes)
            {
                Options.Add(new AccentOptionViewModel
                {
                    Name = palette.Name,
                    Brush = AppearanceManager.BrushFor(palette.Name),
                    IsCurrent = palette.Name == AppearanceManager.CurrentAccentName
                });
            }
        }
    }
}
