using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace HidWizards.UCR.Utilities
{
    /// <summary>
    /// Synchronous UCR-styled replacement for the native WPF MessageBox.
    /// Native MessageBox windows do not reliably follow UCR's dark theme on .NET Framework.
    /// </summary>
    internal static class DarkMessageBox
    {
        public static MessageBoxResult Show(string messageBoxText)
        {
            return Show(null, messageBoxText, "Universal Control Remapper", MessageBoxButton.OK, MessageBoxImage.None);
        }

        public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon)
        {
            return Show(null, messageBoxText, caption, button, icon);
        }

        public static MessageBoxResult Show(Window owner, string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon)
        {
            if (owner == null && Application.Current?.MainWindow != null && Application.Current.MainWindow.IsVisible)
            {
                owner = Application.Current.MainWindow;
            }

            var dialog = new DarkMessageBoxWindow(messageBoxText, caption, button, icon, owner);
            dialog.ShowDialog();
            return dialog.Result;
        }

        private sealed class DarkMessageBoxWindow : Window
        {
            private static readonly Brush BackgroundBrush = BrushFromRgb(0x21, 0x21, 0x21);
            private static readonly Brush SurfaceBrush = BrushFromRgb(0x2B, 0x2B, 0x2B);
            private static Brush AccentBrush => ResolveResourceBrush("PrimaryHueDarkBrush", AppearanceManager.BrushFor(AppearanceManager.CurrentAccentName));
            private static Brush AccentForegroundBrush => ResolveResourceBrush("PrimaryHueDarkForegroundBrush", Brushes.White);
            private static readonly Brush TextBrush = Brushes.White;
            private static readonly Brush SecondaryTextBrush = BrushFromRgb(0xD0, 0xD0, 0xD0);

            private readonly MessageBoxButton _buttons;
            private MessageBoxResult _result = MessageBoxResult.None;

            public MessageBoxResult Result => _result;

            public DarkMessageBoxWindow(string message, string caption, MessageBoxButton buttons, MessageBoxImage icon, Window owner)
            {
                _buttons = buttons;
                Title = string.IsNullOrWhiteSpace(caption) ? "Universal Control Remapper" : caption;
                Width = 470;
                MinWidth = 360;
                MaxWidth = 680;
                SizeToContent = SizeToContent.Height;
                MaxHeight = 700;
                ResizeMode = ResizeMode.NoResize;
                WindowStyle = WindowStyle.None;
                ShowInTaskbar = false;
                Background = BackgroundBrush;
                BorderBrush = AccentBrush;
                BorderThickness = new Thickness(1);
                WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
                if (owner != null) Owner = owner;

                Content = BuildContent(message ?? string.Empty, icon);
                PreviewKeyDown += OnPreviewKeyDown;
                Closing += (sender, args) =>
                {
                    if (_result == MessageBoxResult.None) _result = GetCloseResult(_buttons);
                };
            }

            private UIElement BuildContent(string message, MessageBoxImage icon)
            {
                var root = new Grid { Background = BackgroundBrush };
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var titleBar = new Grid
                {
                    Background = AccentBrush,
                    Height = 38
                };
                titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                titleBar.MouseLeftButtonDown += (sender, args) =>
                {
                    if (args.LeftButton == MouseButtonState.Pressed) DragMove();
                };

                var titleText = new TextBlock
                {
                    Text = Title,
                    Foreground = AccentForegroundBrush,
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(12, 0, 8, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                titleBar.Children.Add(titleText);

                var closeButton = new Button
                {
                    Content = "×",
                    Width = 42,
                    Height = 38,
                    Padding = new Thickness(0),
                    BorderThickness = new Thickness(0),
                    Background = Brushes.Transparent,
                    Foreground = AccentForegroundBrush,
                    FontSize = 20,
                    FontWeight = FontWeights.Light,
                    Focusable = false
                };
                closeButton.Click += (sender, args) => Complete(GetCloseResult(_buttons));
                Grid.SetColumn(closeButton, 1);
                titleBar.Children.Add(closeButton);
                Grid.SetRow(titleBar, 0);
                root.Children.Add(titleBar);

                var contentGrid = new Grid
                {
                    Margin = new Thickness(20, 20, 20, 16),
                    Background = BackgroundBrush
                };
                contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var iconText = GetIconText(icon);
                if (!string.IsNullOrEmpty(iconText))
                {
                    var iconBlock = new TextBlock
                    {
                        Text = iconText,
                        FontFamily = new FontFamily("Segoe UI Symbol"),
                        FontSize = 26,
                        Foreground = GetIconBrush(icon),
                        VerticalAlignment = VerticalAlignment.Top,
                        Margin = new Thickness(0, 0, 14, 0)
                    };
                    contentGrid.Children.Add(iconBlock);
                }

                var messageBlock = new TextBlock
                {
                    Text = message,
                    Foreground = TextBrush,
                    FontSize = 13,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Center,
                    MaxWidth = 570
                };
                Grid.SetColumn(messageBlock, 1);
                contentGrid.Children.Add(messageBlock);
                Grid.SetRow(contentGrid, 1);
                root.Children.Add(contentGrid);

                var buttonPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right
                };

                var results = GetButtonResults(_buttons);
                for (var i = 0; i < results.Count; i++)
                {
                    var result = results[i];
                    var button = CreateButton(result, i == 0);
                    buttonPanel.Children.Add(button);
                }

                var buttonSurface = new Border
                {
                    Background = SurfaceBrush,
                    Padding = new Thickness(14, 10, 14, 10),
                    Child = buttonPanel
                };
                Grid.SetRow(buttonSurface, 2);
                root.Children.Add(buttonSurface);
                return root;
            }

            private Button CreateButton(MessageBoxResult result, bool primary)
            {
                var button = new Button
                {
                    Content = ResultText(result),
                    MinWidth = 78,
                    Height = 32,
                    Margin = new Thickness(8, 0, 0, 0),
                    Padding = new Thickness(12, 0, 12, 0),
                    Foreground = primary ? AccentForegroundBrush : TextBrush,
                    Background = primary ? AccentBrush : Brushes.Transparent,
                    BorderBrush = primary ? AccentBrush : SecondaryTextBrush,
                    BorderThickness = new Thickness(1),
                    IsDefault = primary
                };
                button.Click += (sender, args) => Complete(result);
                return button;
            }

            private void Complete(MessageBoxResult result)
            {
                _result = result;
                Close();
            }

            private void OnPreviewKeyDown(object sender, KeyEventArgs e)
            {
                if (e.Key != Key.Escape) return;
                Complete(GetCloseResult(_buttons));
                e.Handled = true;
            }

            private static List<MessageBoxResult> GetButtonResults(MessageBoxButton buttons)
            {
                switch (buttons)
                {
                    case MessageBoxButton.OKCancel:
                        return new List<MessageBoxResult> { MessageBoxResult.OK, MessageBoxResult.Cancel };
                    case MessageBoxButton.YesNo:
                        return new List<MessageBoxResult> { MessageBoxResult.Yes, MessageBoxResult.No };
                    case MessageBoxButton.YesNoCancel:
                        return new List<MessageBoxResult> { MessageBoxResult.Yes, MessageBoxResult.No, MessageBoxResult.Cancel };
                    default:
                        return new List<MessageBoxResult> { MessageBoxResult.OK };
                }
            }

            private static MessageBoxResult GetCloseResult(MessageBoxButton buttons)
            {
                switch (buttons)
                {
                    case MessageBoxButton.OKCancel:
                    case MessageBoxButton.YesNoCancel:
                        return MessageBoxResult.Cancel;
                    case MessageBoxButton.YesNo:
                        return MessageBoxResult.No;
                    default:
                        return MessageBoxResult.OK;
                }
            }

            private static string ResultText(MessageBoxResult result)
            {
                switch (result)
                {
                    case MessageBoxResult.Yes: return "YES";
                    case MessageBoxResult.No: return "NO";
                    case MessageBoxResult.Cancel: return "CANCEL";
                    default: return "OK";
                }
            }

            private static string GetIconText(MessageBoxImage icon)
            {
                switch (icon)
                {
                    case MessageBoxImage.Error: return "×";
                    case MessageBoxImage.Warning: return "⚠";
                    case MessageBoxImage.Question: return "?";
                    case MessageBoxImage.Information: return "ℹ";
                    default: return string.Empty;
                }
            }

            private static Brush GetIconBrush(MessageBoxImage icon)
            {
                switch (icon)
                {
                    case MessageBoxImage.Error: return BrushFromRgb(0xEF, 0x53, 0x50);
                    case MessageBoxImage.Warning: return BrushFromRgb(0xFF, 0xD5, 0x4F);
                    case MessageBoxImage.Question: return BrushFromRgb(0x42, 0xA5, 0xF5);
                    case MessageBoxImage.Information: return BrushFromRgb(0x42, 0xA5, 0xF5);
                    default: return SecondaryTextBrush;
                }
            }


            private static Brush ResolveResourceBrush(string key, Brush fallback)
            {
                return Application.Current?.TryFindResource(key) as Brush ?? fallback;
            }

            private static Brush BrushFromRgb(byte red, byte green, byte blue)
            {
                var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
                brush.Freeze();
                return brush;
            }
        }
    }
}
