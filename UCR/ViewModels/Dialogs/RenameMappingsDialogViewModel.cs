using System.Collections.ObjectModel;
using HidWizards.UCR.ViewModels.ProfileViewModels;

namespace HidWizards.UCR.ViewModels.Dialogs
{
    public sealed class RenameMappingItemViewModel
    {
        public MappingViewModel Mapping { get; set; }
        public int Number { get; set; }
        public string Name { get; set; }
    }

    public class RenameMappingsDialogViewModel
    {
        public ObservableCollection<RenameMappingItemViewModel> Items { get; }
        public RenameMappingsDialogViewModel ViewModel => this;

        public RenameMappingsDialogViewModel(ProfileViewModel profileViewModel)
        {
            Items = new ObservableCollection<RenameMappingItemViewModel>();
            var number = 1;
            foreach (var mapping in profileViewModel.MappingsList)
            {
                Items.Add(new RenameMappingItemViewModel
                {
                    Mapping = mapping,
                    Number = number++,
                    Name = mapping.Mapping.Title
                });
            }
        }
    }
}
