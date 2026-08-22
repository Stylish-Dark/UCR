using System.Windows;

namespace HidWizards.UCR.Views
{
    public partial class SplashWindow : Window
    {
        public SplashWindow()
        {
            InitializeComponent();
        }

        public void SetStatus(string status)
        {
            StatusText.Text = status;
        }
    }
}
