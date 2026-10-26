using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HidWizards.UCR.Core;
using HidWizards.UCR.Core.Models;
using HidWizards.UCR.ViewModels.Controls;
using HidWizards.UCR.ViewModels.Dashboard;
using HidWizards.UCR.ViewModels.ProfileViewModels;
using HidWizards.UCR.ViewModels.Presentation;
using HidWizards.UCR.Views.Controls;
using HidWizards.UCR.Views.Dialogs;
using HidWizards.UCR.Views.ProfileViews;
using NUnit.Framework;
using MaterialDesignThemes.Wpf;

namespace HidWizards.UCR.Tests.UiTests
{
    [TestFixture]
    [NonParallelizable]
    internal class DeviceManagerPageSmokeTests
    {
        private sealed class FakeDevicePageViewModel
        {
            public IList<DeviceManagerItemViewModel> Devices { get; }
            public DeviceManagerItemViewModel SelectedDevice { get; set; }
            public string DetectionButtonText => "DETECT DEVICE";
            public string DetectionStatus => string.Empty;

            public FakeDevicePageViewModel(DeviceManagerItemViewModel device)
                : this(new[] { device })
            {
            }

            public FakeDevicePageViewModel(IEnumerable<DeviceManagerItemViewModel> devices)
            {
                Devices = devices.ToList();
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void FilterSelectorOpensWithoutMissingXamlResources()
        {
            EnsureApplicationResources();

            foreach (var allowNew in new[] { false, true })
            {
                Assert.DoesNotThrow(() =>
                {
                    var dialog = new FilterSelectDialog(new[] { "Existing filter" }, allowNew);
                    dialog.Measure(new Size(480, 300));
                    dialog.Arrange(new Rect(0, 0, 480, 300));
                    dialog.UpdateLayout();
                    Assert.That(FindVisualChildren<Button>(dialog).Any(), Is.True);
                }, "The Filter selector must load its resources before the user can add a filter.");
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void ProfileListUpdatesInputGlyphAndGroupingAfterDeviceChanges()
        {
            EnsureApplicationResources();

            var context = new Context();
            var profile = context.ProfilesManager.CreateProfile("Glyph refresh", null, null);
            context.ProfilesManager.AddProfile(profile);
            var dashboard = new DashboardViewModel(context);
            var item = dashboard.ProfileList.Single(x => x.Id == profile.Guid);

            Assert.That(item.InputVisuals.Count, Is.EqualTo(0));
            var keyboard = new DeviceConfiguration(
                new Device("Laptop Keyboard", "Core_Interception", "kbd", 0));
            profile.AddDeviceConfigurations(new List<DeviceConfiguration> { keyboard }, DeviceIoType.Input);
            Assert.That(item.InputVisuals.Count, Is.EqualTo(1));
            Assert.That(item.InputVisuals.Single().Kind, Is.EqualTo(DeviceVisualKind.Keyboard));

            dashboard.GroupProfilesByInput = true;
            var controller = new DeviceConfiguration(
                new Device("Xbox 360 Controller", "SharpDX_XInput", "xinput", 1));
            profile.AddDeviceConfigurations(new List<DeviceConfiguration> { controller }, DeviceIoType.Input);
            profile.SetPrimaryDeviceConfiguration(DeviceIoType.Input, controller.Guid);
            Assert.That(item.InputVisuals.Single().Kind, Is.EqualTo(DeviceVisualKind.Xbox360));
            Assert.That(item.InputGroupName, Is.EqualTo("Multiple input devices"));
            Assert.That(item.HasAdditionalInputs, Is.True);

            profile.RemoveDeviceConfiguration(controller);
            Assert.That(item.InputVisuals.Single().Kind, Is.EqualTo(DeviceVisualKind.Keyboard));
            Assert.That(item.HasAdditionalInputs, Is.False);
            profile.RemoveDeviceConfiguration(keyboard);
            Assert.That(item.InputVisuals.Count, Is.EqualTo(0));
            Assert.That(item.InputGroupName, Is.EqualTo("No input device"));
        }

        private sealed class FakeCompactDeviceItem
        {
            public string Title { get; set; }
            public DeviceVisualDescriptor Visual { get; set; }
        }

        private sealed class FakeCompactBinding
        {
            public IList<FakeCompactDeviceItem> Devices { get; set; }
            public FakeCompactDeviceItem SelectedDevice { get; set; }
            public bool BindingEnabled => true;
            public bool ShowButtonPreview => false;
            public Visibility ShowBlock => Visibility.Collapsed;
            public Visibility ShowInvertInput => Visibility.Collapsed;
            public string CompactBindButtonText => "Bind";
            public string BindingActionToolTip => "Bind";
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void CompactDeviceSelectorKeepsFullSizeGlyphInsideItsActualBorder()
        {
            EnsureApplicationResources();

            var option = new FakeCompactDeviceItem
            {
                Title = "Laptop Keyboard With A Very Long Friendly Name",
                Visual = DeviceVisualCatalog.Describe(
                    new Device("Laptop Keyboard", "Core_Interception", "kbd", 0), DeviceIoType.Input)
            };
            var control = new CompactInputBindingControl
            {
                DataContext = new FakeCompactBinding
                {
                    SelectedDevice = option,
                    Devices = new[] { option }
                }
            };
            control.Measure(new Size(395, 55));
            control.Arrange(new Rect(0, 0, 395, 55));
            control.UpdateLayout();

            var selector = control.FindName("DeviceSelectionBox") as ComboBox;
            Assert.That(selector, Is.Not.Null);
            Assert.That(selector.ActualWidth, Is.EqualTo(58).Within(1));
            selector.ApplyTemplate();

            var border = selector.Template.FindName("DeviceSurface", selector) as Border;
            var glyph = selector.Template.FindName("ClosedDeviceGlyph", selector) as DeviceSymbolControl;
            Assert.That(border, Is.Not.Null, "Selected device must have its own dedicated bordered button.");
            Assert.That(glyph, Is.Not.Null, "Selected device glyph must not be squeezed into a full-name row.");
            Assert.That(border.BorderThickness.Left, Is.GreaterThanOrEqualTo(2));
            Assert.That(glyph.SymbolText, Is.EqualTo(option.Visual.SymbolText));
            Assert.That(glyph.ActualWidth, Is.GreaterThanOrEqualTo(30));
            Assert.That(glyph.ActualHeight, Is.GreaterThanOrEqualTo(30));
            Assert.That(glyph.ActualWidth, Is.LessThan(selector.ActualWidth),
                "The selected glyph must fit beside its dedicated dropdown arrow.");

            var popup = selector.Template.FindName("PART_Popup", selector)
                as System.Windows.Controls.Primitives.Popup;
            Assert.That(popup, Is.Not.Null);
            Assert.That(popup.Child, Is.InstanceOf<Border>());
            Assert.That(((Border)popup.Child).MinWidth, Is.GreaterThanOrEqualTo(240),
                "The opened device list needs enough width for full friendly names.");
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void DeviceSymbolControlRendersEveryEmbeddedFontGlyphWithoutFallbackBoxes()
        {
            EnsureApplicationResources();

            using (var stream = typeof(DeviceSymbolControl).Assembly
                .GetManifestResourceStream("HidWizards.UCR.Assets.Fonts.DeviceSymbols-v7.ttf"))
            {
                Assert.That(stream, Is.Not.Null, "DeviceSymbols-v7.ttf is not embedded in UCR.exe.");
                Assert.That(stream.Length, Is.GreaterThan(1024));
            }

            foreach (var codePoint in Enumerable.Range(0xE001, 5))
            {
                const int width = 64;
                const int height = 42;
                var control = new DeviceSymbolControl
                {
                    SymbolText = char.ConvertFromUtf32(codePoint),
                    Foreground = Brushes.White,
                    GlyphSize = 32,
                    Width = width,
                    Height = height
                };

                control.Measure(new Size(width, height));
                control.Arrange(new Rect(0, 0, width, height));
                control.UpdateLayout();

                var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(control);

                var stride = width * 4;
                var pixels = new byte[stride * height];
                bitmap.CopyPixels(pixels, stride, 0);

                var paintedPixels = 0;
                for (var index = 3; index < pixels.Length; index += 4)
                {
                    if (pixels[index] > 0) paintedPixels++;
                }

                Assert.That(paintedPixels, Is.GreaterThan(20),
                    "Embedded device symbol U+" + codePoint.ToString("X4") + " did not render.");
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void LegacyDeviceSymbolTextResourceRemainsResolvable()
        {
            EnsureApplicationResources();

            var resource = Application.Current.TryFindResource("DeviceSymbolText");
            Assert.That(resource, Is.InstanceOf<Style>(),
                "DeviceSymbolText compatibility resource must remain available so stale compiled templates cannot crash startup.");
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void ApplicationUsesDefaultRenderingSoWpfCanUseHardwareAcceleration()
        {
            EnsureApplicationResources();
            Assert.That(RenderOptions.ProcessRenderMode, Is.EqualTo(System.Windows.Interop.RenderMode.Default));
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void ActualProfileWorkspaceMappingChevronExpandsInputsAndOutputsBelowItsOwnRow()
        {
            EnsureApplicationResources();
            var context = new Context();
            var profile = context.ProfilesManager.CreateProfile("Workspace expansion", null, null);
            context.ProfilesManager.AddProfile(profile);
            profile.AddMapping("A");

            var page = new ProfilePage(context, profile, embedded: true);
            try
            {
                page.SuspendEmbeddedVisuals();
                var available = new Size(1440, 960);
                page.Measure(available);
                page.Arrange(new Rect(new Point(0, 0), available));
                page.UpdateLayout();

                var rows = FindVisualChildren<ListView>(page)
                    .SingleOrDefault(list => list.Name == "MappingRowsList");
                Assert.That(rows, Is.Not.Null);
                var row = rows.ItemContainerGenerator.ContainerFromIndex(0) as ListViewItem;
                Assert.That(row, Is.Not.Null);
                var mapping = row.DataContext as MappingViewModel;
                Assert.That(mapping, Is.Not.Null);

                // Collapsed rows preserve the original 18pt mapping title,
                // visual columns, and glyph presentation. Only the chevron
                // is converted from inert TextBlock to a real Button.
                var title = FindVisualChildren<TextBlock>(row)
                    .FirstOrDefault(t => t.Text == mapping.MappingTitle && t.FontSize == 18);
                Assert.That(title, Is.Not.Null, "Original mapping title typography changed.");
                var toggle = FindVisualChildren<Button>(row)
                    .SingleOrDefault(button => (button.ToolTip as string) ==
                        "Expand or collapse mapping Inputs and Outputs");
                Assert.That(toggle, Is.Not.Null);
                Assert.That(toggle.Content, Is.EqualTo("▼"));
                var collapsedHeight = row.ActualHeight;

                toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, toggle));
                page.Measure(available);
                page.Arrange(new Rect(new Point(0, 0), available));
                page.UpdateLayout();

                Assert.That(mapping.IsExpanded, Is.True);
                Assert.That(toggle.Content, Is.EqualTo("▲"));
                Assert.That(row.ActualHeight, Is.GreaterThan(collapsedHeight),
                    "The original mapping row must expand downwards.");
                var inlinePanel = FindVisualChildren<MappingCardControl>(row)
                    .SingleOrDefault(card => card.InspectorMode);
                Assert.That(inlinePanel, Is.Not.Null);
                var body = inlinePanel.FindName("ExpandedBodyHost") as ContentControl;
                Assert.That(body, Is.Not.Null);
                Assert.That(body.Visibility, Is.EqualTo(Visibility.Visible));
                Assert.That(body.ActualHeight, Is.GreaterThan(0));
                var headings = FindVisualChildren<TextBlock>(body).Select(t => t.Text).ToList();
                Assert.That(headings, Does.Contain("INPUTS"));
                Assert.That(headings, Does.Contain("OUTPUTS"));
                var inlineColumns = FindVisualChildren<UniformGrid>(body).FirstOrDefault();
                Assert.That(inlineColumns, Is.Not.Null);
                Assert.That(inlineColumns.Columns, Is.EqualTo(2),
                    "Inline mapping Inputs and Outputs must appear side by side.");

                // Original collapsed glyphs must be actual clickable buttons.
                var rebindingButtons = FindVisualChildren<Button>(row)
                    .Where(button => button.ToolTip is string &&
                        ((string)button.ToolTip).Contains("bind") &&
                        button.Cursor == System.Windows.Input.Cursors.Hand).ToList();
                Assert.That(rebindingButtons.Count, Is.GreaterThan(0),
                    "The workspace must expose clickable rebinding glyphs.");

                toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, toggle));
                page.Measure(available);
                page.Arrange(new Rect(new Point(0, 0), available));
                page.UpdateLayout();
                Assert.That(mapping.IsExpanded, Is.False);
                Assert.That(toggle.Content, Is.EqualTo("▼"));
                Assert.That(row.ActualHeight, Is.LessThan(collapsedHeight + 2));
            }
            finally
            {
                page.Dispose();
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void MappingChevronReallyOpensAndClosesInlineEditorWithoutExpanderHeader()
        {
            EnsureApplicationResources();

            var context = new Context();
            var profile = context.ProfilesManager.CreateProfile("Inline mapping", null, null);
            context.ProfilesManager.AddProfile(profile);
            profile.AddMapping("Mapping");

            var profileViewModel = new ProfileViewModel(profile);
            try
            {
                var mapping = profileViewModel.MappingsList.Single();
                var card = new MappingCardControl { DataContext = mapping };
                card.Measure(new Size(900, 600));
                card.Arrange(new Rect(0, 0, 900, 600));
                card.UpdateLayout();

                var chevron = card.FindName("ExpandMappingButton") as Button;
                var header = card.FindName("MappingHeaderSurface") as Border;
                var body = card.FindName("ExpandedBodyHost") as ContentControl;
                Assert.That(chevron, Is.Not.Null, "The mapping must expose a dedicated clickable chevron.");
                Assert.That(header, Is.Not.Null);
                Assert.That(body, Is.Not.Null);
                Assert.That(card.FindName("MappingExpander"), Is.Null,
                    "The header must not remain wrapped in a clickable WPF Expander.");
                Assert.That(chevron.Content, Is.InstanceOf<Grid>());
                var chevronIcon = ((Grid)chevron.Content).Children.OfType<System.Windows.Shapes.Path>().Single();
                Assert.That(chevronIcon.Data, Is.Not.Null);
                Assert.That(chevron.Width, Is.GreaterThanOrEqualTo(38));
                Assert.That(chevron.BorderThickness.Left, Is.GreaterThan(0));
                Assert.That(mapping.IsExpanded, Is.False);
                Assert.That(body.Visibility, Is.EqualTo(Visibility.Collapsed));

                // Actual WPF button automation, not direct invocation of the private handler.
                ((IInvokeProvider)new ButtonAutomationPeer(chevron)).Invoke();
                card.UpdateLayout();
                PumpDispatcherFor(TimeSpan.FromMilliseconds(100));

                Assert.That(mapping.IsExpanded, Is.True);
                Assert.That(body.Visibility, Is.EqualTo(Visibility.Visible));
                Assert.That(body.ContentTemplate, Is.Not.Null);
                Assert.That(((RotateTransform)chevronIcon.RenderTransform).Angle, Is.EqualTo(180));
                Assert.That(body.ActualHeight, Is.GreaterThan(0));

                ((IInvokeProvider)new ButtonAutomationPeer(chevron)).Invoke();
                PumpDispatcherFor(TimeSpan.FromMilliseconds(40));
                Assert.That(mapping.IsExpanded, Is.False);
                Assert.That(body.Visibility, Is.EqualTo(Visibility.Visible),
                    "Closing should preserve the existing short fade instead of snapping away.");
                PumpDispatcherFor(TimeSpan.FromMilliseconds(250));
                Assert.That(body.Visibility, Is.EqualTo(Visibility.Collapsed));
                Assert.That(((RotateTransform)chevronIcon.RenderTransform).Angle, Is.EqualTo(0));
            }
            finally
            {
                profileViewModel.Dispose();
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void MappingBindingVisualTemplateUsesARealClickableButton()
        {
            EnsureApplicationResources();

            var context = new Context();
            var profile = context.ProfilesManager.CreateProfile("Glyph click", null, null);
            context.ProfilesManager.AddProfile(profile);
            profile.AddMapping("Mapping");
            var profileViewModel = new ProfileViewModel(profile);
            try
            {
                var card = new MappingCardControl { DataContext = profileViewModel.MappingsList.Single() };
                var template = card.Resources["MappingBindingVisualTemplate"] as DataTemplate;
                Assert.That(template, Is.Not.Null);
                var glyph = template.LoadContent() as Button;
                Assert.That(glyph, Is.Not.Null, "Each glyph must be a real clickable Button, not a passive Grid.");
                Assert.That(glyph.Cursor, Is.EqualTo(System.Windows.Input.Cursors.Hand));
                Assert.That(glyph.IsEnabled, Is.True);
            }
            finally
            {
                profileViewModel.Dispose();
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void DeviceManagerPageMaterializesRealDeviceRowAndColourButton()
        {
            EnsureApplicationResources();

            var device = new Device("ViGEm Xbox 360 Controller 1", "Core_ViGEm", "xb360", 0);
            var item = new DeviceManagerItemViewModel(device, DeviceIoType.Output, true, null, false,
                "xbox", DeviceOutlineColor.Default);
            var page = new DeviceManagerPage();
            page.DataContext = new FakeDevicePageViewModel(item);
            page.Measure(new Size(1200, 900));
            page.Arrange(new Rect(0, 0, 1200, 900));
            page.UpdateLayout();

            var list = page.FindName("DeviceList") as ListView;
            Assert.That(list, Is.Not.Null);
            Assert.That(list.Items.Count, Is.EqualTo(1));
            Assert.That(list.HasItems, Is.True);
            Assert.That(list.Items[0], Is.SameAs(item), "The real DeviceManagerItemViewModel did not reach the ListView.");
            Assert.That(item.AvailableTextColors.Length, Is.EqualTo(10));

            list.UpdateLayout();
            var row = list.ItemContainerGenerator.ContainerFromIndex(0) as ListViewItem;
            Assert.That(row, Is.Not.Null,
                "The Devices page had a real device item but WPF failed to materialize its row.");
            Assert.That(row.ActualHeight, Is.GreaterThan(0));

            var aliasBox = FindVisualChildren<TextBox>(row).FirstOrDefault();
            Assert.That(aliasBox, Is.Not.Null, "The real device row failed before its friendly-name editor was created.");
            Assert.That(aliasBox.FontSize, Is.GreaterThanOrEqualTo(16),
                "Friendly names should visually fill the editor rather than render like small form text.");
            Assert.That(aliasBox.FontWeight, Is.EqualTo(FontWeights.SemiBold),
                "Friendly names should be slightly emphasized.");
            Assert.That(aliasBox.Padding.Left, Is.GreaterThanOrEqualTo(8));
            Assert.That(ScrollViewer.GetVerticalScrollBarVisibility(list), Is.EqualTo(ScrollBarVisibility.Auto));
var colourButton = FindVisualChildren<Button>(row)
                .FirstOrDefault(candidate => (candidate.ToolTip as string)?.StartsWith("Device accent colour") == true);
            Assert.That(colourButton, Is.Not.Null,
                "The real device row failed before its accent-colour button was created.");
            Assert.That(colourButton.ActualHeight, Is.GreaterThan(0));
        }

        private static void PumpDispatcherFor(TimeSpan duration)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = duration
            };
            timer.Tick += (sender, args) =>
            {
                timer.Stop();
                frame.Continue = false;
            };
            timer.Start();
            Dispatcher.PushFrame(frame);
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
        {
            if (root == null) yield break;
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            {
                var child = VisualTreeHelper.GetChild(root, index);
                var typed = child as T;
                if (typed != null) yield return typed;
                foreach (var nested in FindVisualChildren<T>(child)) yield return nested;
            }
        }

        private static void EnsureApplicationResources()
        {
            if (Application.Current != null) return;
            var app = new App();
            app.InitializeComponent();
        }
    }
}
