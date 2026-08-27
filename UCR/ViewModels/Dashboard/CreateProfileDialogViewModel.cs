using System;

namespace HidWizards.UCR.ViewModels.Dashboard
{
    public class CreateProfileDialogViewModel
    {
        public string Title { get; set; }
        public string ProfileName { get; set; }
        public CreateProfileDialogViewModel ViewModel => this;

        public CreateProfileDialogViewModel()
        {
        }

        public CreateProfileDialogViewModel(string title)
        {
            Title = title;
        }
    }
}
