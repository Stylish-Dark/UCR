using HidWizards.UCR.ViewModels.Presentation;

namespace HidWizards.UCR.ViewModels
{
    public class ComboBoxItemViewModel
    {
        public string Title { get; set; }
        public dynamic Value { get; set; }
        public DeviceVisualDescriptor Visual { get; set; }

        public ComboBoxItemViewModel(string title, dynamic value)
            : this(title, value, null)
        {
        }

        public ComboBoxItemViewModel(string title, dynamic value, DeviceVisualDescriptor visual)
        {
            Title = title;
            Value = value;
            Visual = visual;
        }

        public override string ToString()
        {
            return Title;
        }
    }
}
