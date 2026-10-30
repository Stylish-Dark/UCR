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
using HidWizards.UCR.Plugins.Remapper;
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

                // Collapsed rows use the larger mapping title,
                // visual columns, and glyph presentation. Only the chevron
                // is converted from inert TextBlock to a real Button.
                var title = FindVisualChildren<TextBlock>(row)
                    .FirstOrDefault(t => t.Text == mapping.MappingTitle && t.FontSize == 20);
                Assert.That(title, Is.Not.Null, "Original mapping title typography changed.");
                var toggle = FindVisualChildren<Button>(row)
                    .SingleOrDefault(button => (button.ToolTip as string) ==
                        "Expand or collapse mapping Inputs and Outputs");
                Assert.That(toggle, Is.Not.Null);
                Assert.That(toggle.Content, Is.InstanceOf<Grid>());
                var collapsedHeight = row.ActualHeight;

                toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, toggle));
                page.Measure(available);
                page.Arrange(new Rect(new Point(0, 0), available));
                page.UpdateLayout();

                Assert.That(mapping.IsExpanded, Is.True);
                Assert.That(toggle.Content, Is.InstanceOf<Grid>());
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

                var deletion = FindVisualChildren<Button>(row)
                    .FirstOrDefault(button => (button.ToolTip as string) == "Delete this mapping" &&
                        ReferenceEquals(button.Parent, toggle.Parent));
                Assert.That(deletion, Is.Not.Null,
                    "The delete button must be in the same action group as the mapping chevron.");
                Assert.That(((Panel)deletion.Parent).Children.IndexOf(deletion),
                    Is.LessThan(((Panel)toggle.Parent).Children.IndexOf(toggle)),
                    "Deletion must appear to the LEFT of the chevron.");

                var editorHost = FindVisualChildren<ContentControl>(row)
                    .FirstOrDefault(control => control.Name == "InlineMappingEditor");
                Assert.That(editorHost, Is.Not.Null);
                toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, toggle));
                // The state stays expanded while the height sweeps closed.
                // Do not pump the real application's startup dispatcher in this
                // headless layout test: other tests cover completed transitions.
                Assert.That(mapping.IsExpanded, Is.True);
                Assert.That(double.IsNaN(editorHost.Height), Is.False,
                    "The inline editor should be animated through an explicit height.");
                toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, toggle));
                Assert.That(mapping.IsExpanded, Is.True,
                    "A second click during collapse should reverse it rather than queue another collapse.");
                Assert.That(editorHost.Tag, Is.Null,
                    "A reversed collapse must clear the pending closing marker.");
                Assert.That(toggle.Content, Is.InstanceOf<Grid>());
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

        [Test]
        [Apartment(ApartmentState.STA)]
        public void DirectionalMappingOutputPluginsAreOneLogicalBindingSet()
        {
            EnsureApplicationResources();
            var context = new Context();
            var profile = context.ProfilesManager.CreateProfile("Directional set", null, null);
            context.ProfilesManager.AddProfile(profile);
            var mapping = profile.AddMapping("WASD");
            for (var i = 0; i < 4; i++)
                profile.AddPlugin(mapping, new ButtonToButton());
            mapping.DirectionalButtonOutputs = true;
            mapping.OutputSideType = "4-way";

            var profileViewModel = new ProfileViewModel(profile);
            try
            {
                var editor = profileViewModel.MappingsList.Single();
                Assert.That(editor.OutputBindingSets.Count, Is.EqualTo(1),
                    "Four directions are one logical output Binding Set, not four output rows.");
                Assert.That(editor.OutputBindingSets.Single().Slots.Count, Is.EqualTo(4));
                Assert.That(editor.OutputBindingSets.Single().IsFourWay, Is.True);
                editor.SetDirectionalMode(4);
                Assert.That(editor.Plugins.Count, Is.EqualTo(4),
                    "Changing input direction slots must never add output plugins.");
                Assert.That(editor.OutputBindingSets.Count, Is.EqualTo(1));

                var card = new MappingCardControl { DataContext = editor };
                Assert.That(card.Resources["UcrSideDevicePicker"], Is.InstanceOf<Style>(),
                    "Side selectors must use the original icon-only device-picker presentation.");
                Assert.That(card.Resources["UcrNotButton"], Is.InstanceOf<Style>(),
                    "NOT must look like a button, not a switch.");

                card.Measure(new Size(1250, 950));
                card.Arrange(new Rect(0, 0, 1250, 950));
                card.UpdateLayout();
                var chevron = card.FindName("ExpandMappingButton") as Button;
                Assert.That(chevron, Is.Not.Null);
                ((IInvokeProvider)new ButtonAutomationPeer(chevron)).Invoke();
                PumpDispatcherFor(TimeSpan.FromMilliseconds(120));
                card.UpdateLayout();
                var body = card.FindName("ExpandedBodyHost") as ContentControl;
                Assert.That(body, Is.Not.Null);
                Assert.That(body.Visibility, Is.EqualTo(Visibility.Visible));
                Assert.That(FindVisualChildren<Button>(body).Count(button =>
                    string.Equals(button.Content as string, "Preset",
                        StringComparison.OrdinalIgnoreCase)), Is.GreaterThanOrEqualTo(2),
                    "Both four-direction sides need a centre Preset button.");
                Assert.That(FindVisualChildren<Button>(body).Any(button =>
                    (button.Content as string)?.Contains("4-way ▼") == true), Is.False,
                    "The separate layout selector must not return.");
            }
            finally
            {
                profileViewModel.Dispose();
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void OneAxisAndOneFullJoystickHaveDifferentLogicalOutputShapes()
        {
            var context = new Context();
            var profile = context.ProfilesManager.CreateProfile("Logical controls", null, null);
            context.ProfilesManager.AddProfile(profile);
            var oneAxis = profile.AddMapping("X axis");
            profile.AddPlugin(oneAxis, new AxisToAxis());
            var stick = profile.AddMapping("Left stick");
            profile.AddPlugin(stick, new AxisToAxis());
            profile.AddPlugin(stick, new AxisToAxis());
            stick.OutputSideType = "Full Joystick";

            var editor = new ProfileViewModel(profile);
            try
            {
                var one = editor.MappingsList.Single(mapping =>
                    mapping.Mapping.Title == "X axis");
                var full = editor.MappingsList.Single(mapping =>
                    mapping.Mapping.Title == "Left stick");
                Assert.That(one.OutputBindingSets.Count, Is.EqualTo(1));
                Assert.That(one.OutputBindingSets.Single().Slots.Count, Is.EqualTo(1),
                    "One output axis must have only one output binding.");
                Assert.That(full.OutputBindingSets.Count, Is.EqualTo(1),
                    "X/Y stick channels must be grouped into ONE logical joystick.");
                Assert.That(full.OutputBindingSets.Single().IsFullJoystick, Is.True);
                Assert.That(full.OutputBindingSets.Single().Slots.Count, Is.EqualTo(2),
                    "The full stick retains its two internal X/Y channel bindings.");
            }
            finally
            {
                editor.Dispose();
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void DeleteConfirmationDependsOnActualInputOrOutputAssignments()
        {
            var context = new Context();
            var profile = context.ProfilesManager.CreateProfile("Mapping deletion", null, null);
            context.ProfilesManager.AddProfile(profile);
            var mapping = profile.AddMapping("Incomplete");
            profile.AddPlugin(mapping, new ButtonToButton());
            var viewModel = new ProfileViewModel(profile);
            try
            {
                var editor = viewModel.MappingsList.Single();
                Assert.That(MappingCardControl.RequiresDeleteConfirmation(editor), Is.False,
                    "Empty mappings must be deleted without a confirmation dialog.");
                var output = mapping.Plugins.Single().Outputs.Single();
                output.SetKeyTypeValue(1, 42, 0);
                Assert.That(MappingCardControl.RequiresDeleteConfirmation(editor), Is.True,
                    "An assigned output is user work, even when inputs are still empty.");
                output.SetDeviceConfigurationGuid(Guid.Empty, false);
                Assert.That(MappingCardControl.RequiresDeleteConfirmation(editor), Is.False,
                    "Clearing the last bound control must remove the confirmation requirement.");
            }
            finally
            {
                viewModel.Dispose();
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void QuickOutputSelectionBindsARealProfileDestinationAndRejectsStaleDevices()
        {
            var context = new Context();
            var profile = context.ProfilesManager.CreateProfile("Output binding", null, null);
            context.ProfilesManager.AddProfile(profile);
            var outputDevice = new DeviceConfiguration(
                new Device("Test output keyboard", "Core_Interception", "keyboard", 0));
            profile.AddDeviceConfigurations(new List<DeviceConfiguration> { outputDevice }, DeviceIoType.Output);
            var mapping = profile.AddMapping("Output picker");
            profile.AddPlugin(mapping, new ButtonToButton());
            var viewModel = new ProfileViewModel(profile);
            try
            {
                var editor = viewModel.MappingsList.Single();
                var output = editor.Plugins.Single().DeviceBindings.Single().DeviceBinding;
                var descriptor = new BindingVisualDescriptor { BindingGuid = output.Guid };
                var stale = new QuickBindingOption
                {
                    DeviceConfigurationGuid = Guid.NewGuid(), KeyType = 1, KeyValue = 42
                };
                Assert.That(editor.ApplyQuickOutputBinding(descriptor, stale), Is.False);
                Assert.That(output.IsBound, Is.False,
                    "A removed or unknown output device must not result in a ghost binding.");

                var choice = new QuickBindingOption
                {
                    DeviceConfigurationGuid = outputDevice.Guid, KeyType = 1, KeyValue = 42
                };
                Assert.That(editor.ApplyQuickOutputBinding(descriptor, choice), Is.True,
                    "Choosing a real output control must assign the destination.");
                Assert.That(output.IsBound, Is.True);
                Assert.That(output.DeviceConfigurationGuid, Is.EqualTo(outputDevice.Guid));
                Assert.That(output.KeyValue, Is.EqualTo(42));
                Assert.That(editor.OutputSelectionFeedback, Is.Null);
            }
            finally
            {
                viewModel.Dispose();
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void SelectingNoOutputDeviceIsValidAndKeepsTheMapping()
        {
            EnsureApplicationResources();
            var context = new Context();
            var profile = context.ProfilesManager.CreateProfile("No destination", null, null);
            context.ProfilesManager.AddProfile(profile);
            var mapping = profile.AddMapping("Unassigned output");
            profile.AddPlugin(mapping, new ButtonToButton());

            var viewModel = new ProfileViewModel(profile);
            try
            {
                var editor = viewModel.MappingsList.Single();
                Assert.That(editor.ChangeOutputSideDevice(Guid.Empty), Is.True,
                    "Choosing no output device must be a valid selection.");
                Assert.That(editor.OutputSideDeviceGuid, Is.EqualTo(Guid.Empty));
                Assert.That(editor.Plugins.Count, Is.EqualTo(1),
                    "Unselecting the device must not delete the output set.");
                Assert.That(editor.OutputBindingSets.Single().Slots.Count, Is.EqualTo(1));
                Assert.That(editor.OutputSelectionFeedback, Is.Null,
                    "No device is not an error and should produce no intrusive warning.");
            }
            finally
            {
                viewModel.Dispose();
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void OutputBindButtonWithNoSelectedDeviceUsesInlineGuidanceNotADialog()
        {
            EnsureApplicationResources();
            var context = new Context();
            var profile = context.ProfilesManager.CreateProfile("Output click", null, null);
            context.ProfilesManager.AddProfile(profile);
            var mapping = profile.AddMapping("Bind an output");
            profile.AddPlugin(mapping, new ButtonToButton());
            var viewModel = new ProfileViewModel(profile);
            try
            {
                var editor = viewModel.MappingsList.Single();
                var card = new MappingCardControl { DataContext = editor };
                card.Measure(new Size(1150, 950));
                card.Arrange(new Rect(0, 0, 1150, 950));
                card.UpdateLayout();
                var chevron = card.FindName("ExpandMappingButton") as Button;
                Assert.That(chevron, Is.Not.Null);
                ((IInvokeProvider)new ButtonAutomationPeer(chevron)).Invoke();
                PumpDispatcherFor(TimeSpan.FromMilliseconds(120));
                card.UpdateLayout();
                // Headless WPF tests do not reliably realize nested output
                // DataTemplates without a Window host. Verify the real
                // output picker callback and its no-device code path.
                Assert.That(editor.OutputBindingSets.Single().Slots.Count, Is.EqualTo(1));
                var picker = typeof(MappingCardControl).GetMethod("OpenUnifiedOutputPicker",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                Assert.That(picker, Is.Not.Null);
                picker.Invoke(card, new object[] { editor.Plugins[0].DeviceBindings[0], new Button() });
                Assert.That(editor.OutputSelectionFeedback,
                    Does.Contain("output device"),
                    "The picker must guide the user inline when no output device is selected.");
            }
            finally
            {
                viewModel.Dispose();
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void OutputButtonIsWiredToUnifiedPickerOnTheVisibleExpandedMapping()
        {
            EnsureApplicationResources();
            var context = new Context();
            var profile = context.ProfilesManager.CreateProfile("Output click regression", null, null);
            context.ProfilesManager.AddProfile(profile);
            var mapping = profile.AddMapping("Output controls");
            profile.AddPlugin(mapping, new ButtonToButton());
            var viewModel = new ProfileViewModel(profile);
            try
            {
                var editor = viewModel.MappingsList.Single();
                var card = new MappingCardControl { DataContext = editor };
                card.Measure(new Size(1200, 1000));
                card.Arrange(new Rect(0, 0, 1200, 1000));
                card.UpdateLayout();
                var chevron = card.FindName("ExpandMappingButton") as Button;
                Assert.That(chevron, Is.Not.Null);
                chevron.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, chevron));
                card.UpdateLayout();

                var body = card.FindName("ExpandedBodyHost") as ContentControl;
                Assert.That(body, Is.Not.Null);
                // Hidden four-way templates can be realized even for a
                // one-output mapping; only choose the active row's real binding.
                var output = FindVisualChildren<CompactInputBindingControl>(body)
                    .FirstOrDefault(control => (control.Tag as string) == "Output" &&
                        control.DataContext is DeviceBindingViewModel);
                Assert.That(output, Is.Not.Null, "The expanded Outputs section must create a real clickable binding control.");
                var click = output.FindName("BindButton") as Button;
                Assert.That(click, Is.Not.Null);
                Assert.That(click.IsEnabled, Is.True);
                Assert.That(output.DataContext, Is.InstanceOf<DeviceBindingViewModel>(),
                    "The output row must have its binding view-model.");
                var bound = ((DeviceBindingViewModel)output.DataContext).DeviceBinding;
                Assert.That(bound, Is.Not.Null);
                Assert.That(bound.DeviceIoType, Is.EqualTo(DeviceIoType.Output),
                    "The Output control must point at an output, not an input.");
                Assert.That(output.EnableUnifiedOutputPicker, Is.True);
                DependencyObject ancestor = output;
                while (ancestor != null && !(ancestor is MappingCardControl))
                    ancestor = VisualTreeHelper.GetParent(ancestor);
                Assert.That(ancestor, Is.SameAs(card), "The Output click must resolve its mapping-card parent.");
                click.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, click));
                Assert.That(editor.OutputSelectionFeedback,
                    Does.Contain("output device"),
                    "Clicking the visible OUTPUT button must reach the picker, not silently do nothing.");
            }
            finally
            {
                viewModel.Dispose();
            }
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
