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

            var binding = editor.GetBindingExpression(TextBox.TextProperty);
            if (binding == null) return;

            // Commit the edit before querying the same routed command used by the Save button.
            e.Handled = true;
            binding.UpdateSource();
            if (!binding.HasError && ApplicationCommands.Save.CanExecute(null, editor))
                ApplicationCommands.Save.Execute(null, editor);
        }

        private void DecimalValidationTextBox(object sender, TextCompositionEventArgs e)
        {
            var regex = new Regex(@"([-+0-9]|\.)+");
            e.Handled = !regex.IsMatch(e.Text);
        }
    }
}
