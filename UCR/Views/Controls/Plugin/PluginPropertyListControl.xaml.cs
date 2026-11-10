using System.Text.RegularExpressions;
using System.Windows.Controls;
using System.Windows.Input;

namespace HidWizards.UCR.Views.Controls.Plugin
{
    public partial class PluginPropertyListControl : UserControl
    {
        public PluginPropertyListControl()
        {
            InitializeComponent();
        }

        private void FilterName_OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.S || Keyboard.Modifiers != ModifierKeys.Control ||
                !(sender is TextBox editor) || editor.IsReadOnly) return;

            // Commit once before the routed Save command checks whether the profile is dirty.
            var binding = editor.GetBindingExpression(TextBox.TextProperty);
            if (binding?.IsDirty != true) return;
            binding.UpdateSource();
            if (binding.HasError) e.Handled = true;
        }

        private void DecimalValidationTextBox(object sender, TextCompositionEventArgs e)
        {
            var regex = new Regex(@"([-+0-9]|\.)+");
            e.Handled = !regex.IsMatch(e.Text);
        }
    }
}
