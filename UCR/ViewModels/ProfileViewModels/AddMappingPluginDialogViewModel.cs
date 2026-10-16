using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using HidWizards.UCR.Core.Annotations;

namespace HidWizards.UCR.ViewModels.ProfileViewModels
{
    public class AddMappingPluginDialogViewModel : INotifyPropertyChanged
    {
        public string Title => $"Add plugin to mapping: {MappingViewModel?.MappingTitle}";
        public AddMappingPluginDialogViewModel ViewModel { get; set; }
        private MappingViewModel MappingViewModel { get; }

        public List<SimplePluginViewModel> Plugins { get; set; }
        public SimplePluginViewModel SelectedPlugin { get; set; }
        public bool InputValid => SelectedPlugin != null;

        public AddMappingPluginDialogViewModel()
        {
        }

        public AddMappingPluginDialogViewModel(MappingViewModel mappingViewModel)
        {
            ViewModel = this;
            Plugins = new List<SimplePluginViewModel>();
            MappingViewModel = mappingViewModel;

            mappingViewModel.Mapping.GetPluginList()
                .Where(plugin => !string.Equals(plugin.Group, "Axis", StringComparison.OrdinalIgnoreCase) &&
                    !plugin.InputCategories.Any(category => category.Category == HidWizards.UCR.Core.Models.Binding.DeviceBindingCategory.Range) &&
                    !plugin.OutputCategories.Any(category => category.Category == HidWizards.UCR.Core.Models.Binding.DeviceBindingCategory.Range))
                .ToList().ForEach(plugin => Plugins.Add(new SimplePluginViewModel(plugin)));
        }

        public event PropertyChangedEventHandler PropertyChanged;

        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void SelectionChanged()
        {
            OnPropertyChanged(nameof(InputValid));
        }
    }
}
