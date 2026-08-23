using System.Windows;
using System.Windows.Controls;
using HidWizards.UCR.ViewModels.ProfileViewModels;

namespace HidWizards.UCR.Views.Controls
{
    public partial class MappingCardControl : UserControl
    {
        public MappingCardControl()
        {
            InitializeComponent();
        }


        private void MoveUp_OnClick(object sender, RoutedEventArgs e)
        {
            (DataContext as MappingViewModel)?.MoveUp();
        }

        private void MoveDown_OnClick(object sender, RoutedEventArgs e)
        {
            (DataContext as MappingViewModel)?.MoveDown();
        }

        private void Remove_OnClick(object sender, RoutedEventArgs e)
        {
            var mappingViewModel = DataContext as MappingViewModel;
            mappingViewModel?.Remove();
        }

        private void Rename_OnClick(object sender, RoutedEventArgs e)
        {
            var mappingViewModel = DataContext as MappingViewModel;
            mappingViewModel?.Rename();
        }

        private void AddPlugin_OnClick(object sender, RoutedEventArgs e)
        {
            var mappingViewModel = DataContext as MappingViewModel;
            mappingViewModel?.AddPlugin();
        }
    }
}
