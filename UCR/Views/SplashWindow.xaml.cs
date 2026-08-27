using System.Windows;
using System.Windows.Media;
using HidWizards.UCR.Utilities;

namespace HidWizards.UCR.Views
{
    public partial class SplashWindow : Window
    {
        public SplashWindow()
            : this(AppearanceManager.CurrentAccentColor, "Starting UCR", "Device providers can take a few seconds to initialize.")
        {
        }

        public SplashWindow(Color accent, string phase, string detail)
        {
            InitializeComponent();
            SetAccent(accent);
            PhaseText.Text = phase ?? string.Empty;
            DetailText.Text = detail ?? string.Empty;
            DetailText.Visibility = string.IsNullOrWhiteSpace(detail) ? Visibility.Collapsed : Visibility.Visible;
        }

        public void SetStatus(string status)
        {
            StatusText.Text = status;
        }

        public void SetProgress(double value)
        {
            ProgressIndicator.IsIndeterminate = false;
            ProgressIndicator.Minimum = 0;
            ProgressIndicator.Maximum = 100;
            ProgressIndicator.Value = value < 0 ? 0 : value > 100 ? 100 : value;
        }

        private void SetAccent(Color accent)
        {
            var brush = new SolidColorBrush(accent);
            brush.Freeze();
            RootBorder.BorderBrush = brush;
            ProgressIndicator.Foreground = brush;
        }
    }
}
