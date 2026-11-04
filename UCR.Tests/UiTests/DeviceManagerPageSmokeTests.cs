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
            var bindButton = control.FindName("BindButton") as Button;
            var ellipsis = control.FindName("MenuButton") as Button;
            Assert.That(bindButton, Is.Not.Null);
            Assert.That(ellipsis, Is.Not.Null);
            Assert.That(ellipsis.Width, Is.EqualTo(28),
                "The vertical ellipsis should not take unnecessary horizontal space.");
            Assert.That(bindButton.Padding.Left + bindButton.Padding.Right, Is.EqualTo(0),
                "Do not clip the Bind label with Material Design's default button padding.");

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

                toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, toggle));
                page.Measure(available);
                page.Arrange(new Rect(new Point(0, 0), available));
                page.UpdateLayout();
                Assert.That(mapping.IsExpanded, Is.False);
                Assert.That(toggle.Content, Is.InstanceOf<Grid>());
                Assert.That(row.ActualHeight, Is.LessThan(collapsedHeight + 2));
                // Verify the ACTUAL collapsed workspace row; MappingCardControl
                // is a separate expanded-only editor and is not this header.
                Button remove = null;
                foreach (var width in new[] { 1440d, 1100d })
                {
                    var viewport = new Size(width, 900);
                    page.Measure(viewport);
                    page.Arrange(new Rect(new Point(0, 0), viewport));
                    page.UpdateLayout();
                    remove = FindVisualChildren<Button>(row).SingleOrDefault(button =>
                        (button.ToolTip as string)?.StartsWith("Remove mapping.") == true &&
                        Grid.GetColumn(button) == 4);
                    var collapsedChevron = FindVisualChildren<Button>(row).SingleOrDefault(button =>
                        (button.ToolTip as string) == "Expand or collapse mapping Inputs and Outputs");
                    Assert.That(remove, Is.Not.Null, "The visible collapsed row must expose Delete.");
                    Assert.That(collapsedChevron, Is.Not.Null);
                    Assert.That(remove.ActualWidth, Is.GreaterThanOrEqualTo(28));
                    Assert.That(collapsedChevron.ActualWidth, Is.GreaterThanOrEqualTo(36));
                    var removeX = remove.TransformToAncestor(row).Transform(new Point()).X;
                    var chevronX = collapsedChevron.TransformToAncestor(row).Transform(new Point()).X;
                    Assert.That(removeX + remove.ActualWidth, Is.LessThanOrEqualTo(chevronX + 1),
                        "Delete must sit directly left of the mapping chevron.");
                    Assert.That(chevronX + collapsedChevron.ActualWidth, Is.LessThanOrEqualTo(row.ActualWidth + 1),
                        "Chevron must remain within the mapping row's right edge.");
                }
                remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, remove));
                Assert.That(profile.Mappings.Count, Is.EqualTo(0),
                    "The collapsed-row Delete button must actually remove the mapping.");

                // Exercise the real nested workspace with enough rows to exceed a
                // viewport. This runs on the same WPF dispatcher as the established
                // workspace smoke test instead of creating a second STA application.
                var model = (ProfileViewModel)page.DataContext;
                for (var index = 0; index < 40; index++)
                    model.AddMappingToSelectedSection("Virtual row " + index);
                var fullViewport = new Size(1440, 960);
                page.Measure(fullViewport);
                page.Arrange(new Rect(new Point(0, 0), fullViewport));
                page.UpdateLayout();

                var virtualPanel = FindVisualChildren<OuterViewportVirtualizingPanel>(page)
                    .FirstOrDefault();
                Assert.That(virtualPanel, Is.Not.Null);
                var longList = FindVisualChildren<ListView>(page)
                    .First(candidate => candidate.Name == "MappingRowsList");
                var visibleCount = Enumerable.Range(0, longList.Items.Count)
                    .Count(index => longList.ItemContainerGenerator.ContainerFromIndex(index) != null);
                Assert.That(visibleCount, Is.GreaterThan(0));
                Assert.That(visibleCount, Is.LessThan(longList.Items.Count),
                    "Off-screen mapping editors should not all be materialized.");

                var distant = model.MappingsList.Last();
                virtualPanel.BringMappingIntoView(distant);
                page.Measure(fullViewport);
                page.Arrange(new Rect(new Point(0, 0), fullViewport));
                page.UpdateLayout();
                Assert.That(longList.ItemContainerGenerator.ContainerFromItem(distant), Is.Not.Null,
                    "A distant mapping must still be reachable by ordinary scrolling.");

                // Expanding the entire profile must keep off-screen heavy editor
                // trees virtualized while allowing both ends of the list to open.
                var outerScroll = page.FindName("MappingSectionsScrollViewer") as ScrollViewer;
                Assert.That(outerScroll, Is.Not.Null);
                outerScroll.ScrollToTop();
                foreach (var mappingItem in model.MappingsList)
                    mappingItem.IsExpanded = true;
                virtualPanel.InvalidateMeasure();
                page.Measure(fullViewport);
                page.Arrange(new Rect(new Point(0, 0), fullViewport));
                page.UpdateLayout();
                var afterExpand = Enumerable.Range(0, longList.Items.Count)
                    .Count(index => longList.ItemContainerGenerator.ContainerFromIndex(index) != null);
                Assert.That(afterExpand, Is.GreaterThan(0));
                Assert.That(afterExpand, Is.LessThan(longList.Items.Count),
                    "Expanding all mappings must not instantiate all off-screen editors.");
                var firstVirtualizedRow = longList.ItemContainerGenerator.ContainerFromIndex(0)
                    as ListViewItem;
                Assert.That(firstVirtualizedRow, Is.Not.Null);
                var virtualizedRowHeight = firstVirtualizedRow.ActualHeight;

                // The existing drag/reorder logic requires all concrete row slots.
                // Its temporary full-layout mode must still realize every row,
                // and leaving that mode must restore virtualization.
                VirtualizingPanel.SetIsVirtualizing(longList, false);
                virtualPanel.InvalidateMeasure();
                page.Measure(fullViewport);
                page.Arrange(new Rect(new Point(0, 0), fullViewport));
                page.UpdateLayout();
                Assert.That(Enumerable.Range(0, longList.Items.Count)
                    .All(index => longList.ItemContainerGenerator.ContainerFromIndex(index) != null),
                    Is.True, "Drag mode must retain all reorder targets.");
                var firstFullyRealizedRow = longList.ItemContainerGenerator.ContainerFromIndex(0)
                    as ListViewItem;
                Assert.That(firstFullyRealizedRow, Is.Not.Null);
                Assert.That(firstFullyRealizedRow.ActualHeight,
                    Is.EqualTo(virtualizedRowHeight).Within(1.0),
                    "Virtualization must not change any visible mapping row's geometry.");

                VirtualizingPanel.SetIsVirtualizing(longList, true);
                virtualPanel.InvalidateMeasure();
                page.Measure(fullViewport);
                page.Arrange(new Rect(new Point(0, 0), fullViewport));
                page.UpdateLayout();
                Assert.That(Enumerable.Range(0, longList.Items.Count)
                    .Count(index => longList.ItemContainerGenerator.ContainerFromIndex(index) != null),
                    Is.LessThan(longList.Items.Count));

                virtualPanel.BringMappingIntoView(distant);
                page.Measure(fullViewport);
                page.Arrange(new Rect(new Point(0, 0), fullViewport));
                page.UpdateLayout();
                Assert.That(longList.ItemContainerGenerator.ContainerFromItem(distant), Is.Not.Null,
                    "A mapping at the end of an expanded profile must remain reachable.");

                foreach (var mappingItem in model.MappingsList)
                    mappingItem.IsExpanded = false;
                virtualPanel.InvalidateMeasure();
                page.Measure(fullViewport);
                page.Arrange(new Rect(new Point(0, 0), fullViewport));
                page.UpdateLayout();
                Assert.That(model.MappingsList.All(mappingItem => !mappingItem.IsExpanded), Is.True);
            }
            finally
            {
                page.Dispose();
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void MainMappingGroupCanBeDisabledBlockedCopiedAndDuplicated()
        {
            EnsureApplicationResources();
            var context = new Context();
            var profile = context.ProfilesManager.CreateProfile("Group parity", null, null);
            context.ProfilesManager.AddProfile(profile);
            profile.AddMapping("Root mapping");
            var viewModel = new ProfileViewModel(profile);
            try
            {
                var main = viewModel.MappingSections.Single(section => section.IsMain);
                main.Enabled = false;
                Assert.That(profile.MainMappingsEnabled, Is.False,
                    "Main must be deselectable just like an ordinary group.");
                main.BlockInputs = true;
                Assert.That(profile.MainBlockInputs, Is.True,
                    "The group Block option must persist for Main.");

                var duplicated = viewModel.DuplicateMappingGroup(main);
                Assert.That(duplicated, Is.Not.Null);
                Assert.That(duplicated.Mappings.Count, Is.EqualTo(1));
                Assert.That(duplicated.Enabled, Is.False);
                Assert.That(duplicated.BlockInputs, Is.True,
                    "Copied group must preserve the original group-level blocking preference.");
                viewModel.CopyMappingGroup(main);
                var pasted = viewModel.PasteMappingGroup();
                Assert.That(pasted, Is.Not.Null);
                Assert.That(pasted.Mappings.Count, Is.EqualTo(1));
                Assert.That(main.Mappings.Count, Is.EqualTo(1),
                    "Copying Main must leave its original mappings intact.");
                Assert.That(viewModel.RenameMappingGroup(main, "Renamed Main"), Is.True);
                Assert.That(main.Title, Is.EqualTo("Renamed Main"));
                Assert.That(profile.MainGroupTitle, Is.EqualTo("Renamed Main"));
                Assert.That(viewModel.RemoveMappingGroup(main), Is.True);
                Assert.That(profile.MainGroupVisible, Is.False);
                Assert.That(profile.Mappings, Is.Empty);
                Assert.That(viewModel.MappingSections.Any(section => section.IsMain), Is.False);
                var newMapping = viewModel.AddMapping("After deleting Main");
                Assert.That(newMapping, Is.Not.Null,
                    "Adding mappings after deleting Main must still target a real group.");
                Assert.That(viewModel.MappingSections.Any(section =>
                    section.Mappings.Contains(newMapping)), Is.True);
                Assert.That(typeof(Context).GetMethod("Undo"), Is.Null,
                    "Undo/redo restoration must remain removed.");
                Assert.That(typeof(Context).GetMethod("Redo"), Is.Null);
            }
            finally
            {
                viewModel.Dispose();
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void DirectionalTypeSelectorsRenderIndependentlyAndRestoreBindingBanks()
        {
            EnsureApplicationResources();
            var context = new Context();
            context.PluginManager.Plugins = new List<Plugin>
            {
                new HidWizards.UCR.Plugins.Remapper.ButtonToButton(),
                new HidWizards.UCR.Plugins.Remapper.AxisToAxis()
            };
            var profile = context.ProfilesManager.CreateProfile("Typed directional", null, null);
            context.ProfilesManager.AddProfile(profile);
            var model = profile.AddMapping("Full stick to buttons");
            model.AddPlugin(new HidWizards.UCR.Plugins.Remapper.ButtonToButton());
            var original = model.DeviceBindings.Single();
            original.IsBound = true;
            original.KeyType = 9;
            original.KeyValue = 42;

            var vm = new ProfileViewModel(profile);
            try
            {
                var mapping = vm.MappingsList.Single();
                // Grow an existing axis mapping into a full stick. The old
                // one-axis bank must remain recoverable, but may not be
                // incorrectly restored as the two-axis active bank.
                Assert.That(mapping.SetInputRepresentation(2), Is.True);
                Assert.That(model.DeviceBindings.Count, Is.EqualTo(1));
                Assert.That(mapping.SetInputRepresentation(3), Is.True);
                Assert.That(model.DeviceBindings.Count, Is.EqualTo(1),
                    "Returning to a single Button/Output Button mapping removes inactive direction slots.");
                Assert.That(mapping.SetInputRepresentation(1), Is.True);
                Assert.That(model.DirectionalCapacity, Is.EqualTo(4));
                Assert.That(model.InactiveInputRepresentations.Any(bank =>
                    bank.Representation == 2 && bank.Bindings.Count == 1), Is.True);
                Assert.That(mapping.SetInputRepresentation(2), Is.True);
                Assert.That(model.DeviceBindings.Count, Is.EqualTo(1),
                    "Axis restores one physical channel when the other side is Button.");
                Assert.That(model.DirectionalCapacity, Is.EqualTo(2),
                    "Reducing 4-Way to Axis must reduce logical capacity.");
                Assert.That(mapping.SetInputRepresentation(1), Is.True);
                Assert.That(model.DirectionalCapacity, Is.EqualTo(4));
                Assert.That(model.DeviceBindings.Count, Is.EqualTo(2),
                    "One full stick has two physical axis channels.");
                Assert.That(model.Plugins.Count, Is.EqualTo(4),
                    "A full stick represented by buttons needs four destinations.");
                Assert.That(mapping.OutputIsButton, Is.True,
                    "Changing the input type must not change the output's visible type.");
                Assert.That(mapping.SetOutputRepresentation(2), Is.True);
                Assert.That(model.DeviceBindings.Count, Is.EqualTo(2),
                    "Changing the output type must leave the input untouched.");
                Assert.That(model.Plugins.Count, Is.EqualTo(2));
                Assert.That(mapping.InputIsFullStick, Is.True);

                var savedAxis = model.DeviceBindings[0];
                savedAxis.IsBound = true;
                savedAxis.KeyValue = 271;

                Assert.That(mapping.SetInputRepresentation(3), Is.True);
                Assert.That(model.DeviceBindings.Count, Is.EqualTo(2));
                Assert.That(model.DirectionalCapacity, Is.EqualTo(2));
                Assert.That(model.InactiveInputRepresentations.Any(
                    bank => bank.Representation == 1 &&
                        bank.Bindings.Any(binding => binding.KeyValue == 271)), Is.True);
                Assert.That(mapping.SetInputRepresentation(1), Is.True);
                Assert.That(model.DeviceBindings[0].KeyValue, Is.EqualTo(271),
                    "Restoring Full Stick must recover its previous axis assignment.");
                Assert.That(mapping.OutputIsAxis, Is.True);
                Assert.That(mapping.SetOutputRepresentation(3), Is.True);
                Assert.That(model.Plugins.Count, Is.EqualTo(4));
                Assert.That(mapping.SetOutputRepresentation(2), Is.True);
                Assert.That(model.Plugins.Count, Is.EqualTo(2));

                Assert.That(model.InactiveInputRepresentations.Any(
                    bank => bank.Representation == 0 &&
                        bank.Bindings.Any(binding => binding.KeyValue == 42)), Is.True,
                    "The original legacy input must remain preserved.");

                Assert.That(mapping.SetOutputRepresentation(3), Is.True);
                var card = new MappingCardControl
                {
                    InspectorMode = true,
                    InlineWorkspaceMode = true,
                    DataContext = mapping
                };
                card.Measure(new Size(1050, 1100));
                card.Arrange(new Rect(0, 0, 1050, 1100));
                card.UpdateLayout();

                var selectors = FindVisualChildren<RadioButton>(card)
                    .Where(button => new[] { "1", "2", "3", "4" }
                        .Contains(button.Tag as string) &&
                        ((button.ToolTip as string)?.Contains(" input") == true ||
                         (button.ToolTip as string)?.Contains(" output") == true))
                    .ToList();
                Assert.That(selectors.Count, Is.EqualTo(8),
                    "Each side exposes Button, Trigger, Axis and 4-Way.");
                Assert.That(selectors.All(button => button.IsEnabled), Is.True,
                    "Unassigned choices must not be locked by current capability.");
                Assert.That(selectors.Count(button => button.IsChecked == true), Is.EqualTo(2),
                    "Exactly one representation must be selected on each side.");

                var axisOutput = selectors.Single(button =>
                    (button.ToolTip as string) == "Axis output");
                axisOutput.IsChecked = true;
                axisOutput.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, axisOutput));
                Assert.That(model.OutputRepresentation, Is.EqualTo(2));
                Assert.That(mapping.InputIsFullStick, Is.True,
                    "Clicking Output Axis must not change Input Full Stick.");

                mapping.SetOutputRepresentation(3);
                card.Measure(new Size(1050, 1100));
                card.Arrange(new Rect(0, 0, 1050, 1100));
                card.UpdateLayout();
                var slotLabels = FindVisualChildren<TextBlock>(card)
                    .Select(text => text.Text).ToList();
                Assert.That(slotLabels, Does.Contain("4-Way"),
                    "The compound input must identify the selected 4-Way representation.");
                var compoundButtons = FindVisualChildren<Button>(card)
                    .Where(button => (button.ToolTip as string) ==
                        "Bind the axis or choose a complete preset")
                    .ToList();
                Assert.That(compoundButtons.Count, Is.EqualTo(1),
                    "A 4-Way input must retain one shared preset assignment control.");
                Assert.That(compoundButtons.Single().Content as string,
                    Is.EqualTo("Choose 4-Way preset ▾"),
                    "Do not compress four direction assignments into a slash-separated button label.");
                var physicalChannels = FindVisualChildren<Button>(card)
                    .Where(button => (button.ToolTip as string) ==
                        "Bind this physical 4-Way channel")
                    .ToList();
                Assert.That(physicalChannels.Count, Is.EqualTo(model.DeviceBindings.Count),
                    "Show every underlying physical binding in the 4-Way editor.");
                Assert.That(physicalChannels.All(button =>
                    button.DataContext is DeviceBindingViewModel), Is.True,
                    "Each visible channel must bind the real physical input, not a dummy label.");
                var analogNegationToggles = FindVisualChildren<ToggleButton>(card)
                    .Where(toggle => (toggle.ToolTip as string) ==
                        "Negate this digital channel")
                    .ToList();
                Assert.That(analogNegationToggles.Count, Is.EqualTo(physicalChannels.Count));
                Assert.That(analogNegationToggles.All(toggle =>
                {
                    var channel = (DeviceBindingViewModel)toggle.DataContext;
                    return toggle.Visibility == (channel.InputExpressionInputKind == 0
                        ? Visibility.Visible : Visibility.Collapsed);
                }), Is.True,
                    "Show NOT only on digital channels; analog channels must not offer an inert toggle.");

                var screenshot = System.IO.Path.Combine(
                    Environment.GetEnvironmentVariable("GITHUB_WORKSPACE") ??
                    System.IO.Path.GetTempPath(), "UCR-directional-preview.png");
                var bitmap = new RenderTargetBitmap(1050, 1100, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(card);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var file = System.IO.File.Create(screenshot)) encoder.Save(file);
                TestContext.WriteLine("Rendered WPF typed editor: " + screenshot);
            }
            finally
            {
                vm.Dispose();
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void MappingCardIsEditorOnlyWithoutASecondHeader()
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
                // This is how the actual workspace instantiates the editor.
                var card = new MappingCardControl
                {
                    InspectorMode = true,
                    InlineWorkspaceMode = true,
                    DataContext = mapping
                };
                card.Measure(new Size(900, 600));
                card.Arrange(new Rect(0, 0, 900, 600));
                card.UpdateLayout();

                Assert.That(card.FindName("MappingHeaderSurface"), Is.Null,
                    "The collapsed mapping header must exist only in ProfilePage.xaml.");
                Assert.That(card.FindName("ExpandMappingButton"), Is.Null,
                    "The editor must never carry a second mapping chevron.");
                Assert.That(FindVisualChildren<Button>(card).Any(button =>
                    (button.ToolTip as string)?.StartsWith("Remove mapping.") == true), Is.False,
                    "Only the workspace row may expose the mapping-delete action.");

                var body = card.FindName("ExpandedBodyHost") as ContentControl;
                Assert.That(body, Is.Not.Null);
                Assert.That(body.Visibility, Is.EqualTo(Visibility.Visible),
                    "The embedded editor must still display its inputs and outputs.");
                Assert.That(body.ContentTemplate, Is.Not.Null);
                Assert.That(body.ActualHeight, Is.GreaterThan(0));
                var headings = FindVisualChildren<TextBlock>(body).Select(t => t.Text).ToList();
                Assert.That(headings, Does.Contain("INPUTS"));
                Assert.That(headings, Does.Contain("OUTPUTS"));
            }
            finally
            {
                profileViewModel.Dispose();
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void CollapsedMappingCanStartButtonAndAxisCaptureWithoutOpeningEditor()
        {
            EnsureApplicationResources();
            var context = new Context();
            var profile = context.ProfilesManager.CreateProfile("Quick input", null, null);
            context.ProfilesManager.AddProfile(profile);
            profile.AddMapping("Capture").AddPlugin(
                new HidWizards.UCR.Plugins.Remapper.ButtonToButton());
            var vm = new ProfileViewModel(profile);
            try
            {
                var mapping = vm.MappingsList.Single();
                Assert.That(mapping.IsExpanded, Is.False);
                var descriptor = mapping.CollapsedInputRows.SelectMany(row => row.Visuals).First();
                var input = mapping.ResolveCollapsedBinding(descriptor, DeviceIoType.Input);
                Assert.That(input, Is.Not.Null);
                Assert.That(mapping.QuickBindInput(descriptor), Is.True);
                Assert.That(input.DeviceBinding.IsInBindMode, Is.True);
                var autoCategory = typeof(HidWizards.UCR.Core.Managers.BindingManager).GetField(
                    "_autoDetectInputCategory",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                Assert.That(autoCategory, Is.Not.Null);
                Assert.That((bool)autoCategory.GetValue(context.BindingManager), Is.True,
                    "A collapsed input must accept button and axis controls in the same capture.");
                input.DeviceBinding.CancelBindMode();

                input.InputExpressionInputKind = 3;
                Assert.That(input.InputExpressionInputKind, Is.EqualTo(3));
                Assert.That(input.DeviceBindingCategory, Is.EqualTo(
                    HidWizards.UCR.Core.Models.Binding.DeviceBindingCategory.Range));
                Assert.That(mapping.QuickBindInput(descriptor), Is.True);
                Assert.That(input.DeviceBinding.IsInBindMode, Is.True);
                input.DeviceBinding.CancelBindMode();
                Assert.That(mapping.IsExpanded, Is.False);
            }
            finally
            {
                vm.Dispose();
            }
        }

        [TestCase("A", "A", true)]
        [TestCase("Buttons,A", "A", true)]
        [TestCase("Face buttons: Cross", "Cross", true)]
        [TestCase("Face buttons: Circle", "Cross", false)]
        [TestCase("Y", "A", false)]
        public void FaceButtonPresetMatchesCompleteProviderControlLabels(
            string providerTitle, string faceButton, bool expected)
        {
            Assert.That(MappingViewModel.MatchesControlLeafName(providerTitle, faceButton),
                Is.EqualTo(expected));
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void WasdPresetBindsOnlyItsOrGroupAndSurvivesAxisRoundTrip()
        {
            EnsureApplicationResources();
            var context = new Context();
            context.PluginManager.Plugins = new List<Plugin>
            {
                new HidWizards.UCR.Plugins.Remapper.ButtonToButton(),
                new HidWizards.UCR.Plugins.Remapper.AxisToAxis()
            };
            var profile = context.ProfilesManager.CreateProfile("Directional keyboard alternatives",
                null, null);
            context.ProfilesManager.AddProfile(profile);
            var keyboard = new Device(new DeviceCache
            {
                Title = "Keyboard",
                ProviderName = "Core_Interception",
                DeviceHandle = "keyboard",
                DeviceBindingMenu = new List<HidWizards.UCR.Core.Models.Binding.DeviceBindingNode>
                {
                    new HidWizards.UCR.Core.Models.Binding.DeviceBindingNode
                    {
                        Title = "Keys",
                        ChildrenNodes = new[] { "A", "D", "W", "S" }
                            .Select((key, index) => new HidWizards.UCR.Core.Models.Binding.DeviceBindingNode
                            {
                                Title = "Keys," + key,
                                DeviceBindingInfo = new HidWizards.UCR.Core.Models.Binding.DeviceBindingInfo
                                {
                                    DeviceBindingCategory = HidWizards.UCR.Core.Models.Binding.DeviceBindingCategory.Momentary,
                                    KeyType = 1, KeyValue = index + 10
                                }
                            }).ToList()
                    }
                }
            });
            profile.AddDeviceConfigurations(new List<DeviceConfiguration>
            {
                new DeviceConfiguration(keyboard)
            }, DeviceIoType.Input);
            profile.AddMapping("Keyboard alternatives")
                .AddPlugin(new HidWizards.UCR.Plugins.Remapper.ButtonToButton());
            var vm = new ProfileViewModel(profile);
            try
            {
                var mapping = vm.MappingsList.Single();
                Assert.That(mapping.SetOutputRepresentation(2), Is.True);
                mapping.AddOrInput();
                Assert.That(mapping.Mapping.DeviceBindings.Count, Is.EqualTo(4));
                string error;
                Assert.That(mapping.ApplyDirectionalKeyboardPreset("WASD", out error, 1),
                    Is.True, error);
                Assert.That(mapping.Mapping.EffectiveInputRepresentation, Is.EqualTo(1));
                Assert.That(mapping.Mapping.CompoundDigitalInput, Is.True);
                Assert.That(mapping.Mapping.DeviceBindings.Count, Is.EqualTo(8));
                var firstSet = mapping.Mapping.DeviceBindings
                    .Where(binding => binding.InputExpressionGroup == 0).ToList();
                var secondSet = mapping.Mapping.DeviceBindings
                    .Where(binding => binding.InputExpressionGroup == 1)
                    .OrderBy(binding => binding.InputDirectionSlot).ToList();
                Assert.That(firstSet.All(binding => !binding.IsBound), Is.True,
                    "Applying a preset to one OR set must not bind another set.");
                Assert.That(secondSet.Select(binding => binding.KeyValue),
                    Is.EqualTo(new[] { 10, 11, 12, 13 }));
                Assert.That(mapping.SetInputRepresentation(2), Is.True);
                Assert.That(mapping.Mapping.CompoundDigitalInput, Is.False);
                Assert.That(mapping.Mapping.DeviceBindings.Count, Is.EqualTo(2),
                    "After reducing from 4-Way to Axis, each OR group needs one axis.");
                Assert.That(mapping.SetInputRepresentation(1), Is.True);
                Assert.That(mapping.Mapping.CompoundDigitalInput, Is.True,
                    "Returning to 4-Way should restore the previous compound preset.");
                Assert.That(mapping.Mapping.DeviceBindings.Count, Is.EqualTo(8));
                Assert.That(mapping.Mapping.DeviceBindings
                    .Where(binding => binding.InputExpressionGroup == 1)
                    .OrderBy(binding => binding.InputDirectionSlot)
                    .Select(binding => binding.KeyValue),
                    Is.EqualTo(new[] { 10, 11, 12, 13 }));
            }
            finally
            {
                vm.Dispose();
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void DirectionalButtonSlotsShowSpatialPresetAndAccessibleOuterOptions()
        {
            EnsureApplicationResources();
            var context = new Context();
            context.PluginManager.Plugins = new List<Plugin>
            {
                new HidWizards.UCR.Plugins.Remapper.ButtonToButton(),
                new HidWizards.UCR.Plugins.Remapper.AxisToAxis()
            };
            var profile = context.ProfilesManager.CreateProfile("Spatial directions", null, null);
            context.ProfilesManager.AddProfile(profile);
            profile.AddMapping("Spatial").AddPlugin(
                new HidWizards.UCR.Plugins.Remapper.ButtonToButton());
            var vm = new ProfileViewModel(profile);
            try
            {
                var mapping = vm.MappingsList.Single();
                Assert.That(mapping.SetOutputRepresentation(2), Is.True);
                Assert.That(mapping.Mapping.DeviceBindings.Count, Is.EqualTo(2));
                var card = new MappingCardControl
                {
                    InspectorMode = true,
                    InlineWorkspaceMode = true,
                    DataContext = mapping
                };
                card.Measure(new Size(1050, 1200));
                card.Arrange(new Rect(0, 0, 1050, 1200));
                card.UpdateLayout();
                var buttons = FindVisualChildren<Button>(card).ToList();
                Assert.That(buttons.Any(button => string.Equals(button.Content as string,
                    "Preset ▾", StringComparison.Ordinal)), Is.True,
                    "The two independent directions surround a central preset.");
                Assert.That(buttons.Count(button => string.Equals(button.ToolTip as string,
                    "Binding categories, clearing and blocking", StringComparison.Ordinal) &&
                    button.Visibility == Visibility.Visible), Is.EqualTo(2),
                    "Every active direction keeps its own full binding-options menu.");
                Assert.That(FindVisualChildren<CompactInputBindingControl>(card)
                    .Any(control => control.DeviceOnly && control.Visibility == Visibility.Visible),
                    Is.True, "Compound device selection must reuse the real device icon selector.");
                Assert.That(buttons.Any(button => string.Equals(button.Content as string,
                    "+AND", StringComparison.Ordinal)), Is.True,
                    "The directional editor must retain its AND action.");
                var twoWayCompass = FindVisualChildren<Grid>(card).FirstOrDefault(grid =>
                    grid.RowDefinitions.Count == 3 && grid.ColumnDefinitions.Count == 3 &&
                    grid.Children.OfType<Button>().Any(button =>
                        string.Equals(button.Content as string, "Preset ▾", StringComparison.Ordinal)));
                Assert.That(twoWayCompass, Is.Not.Null);
                Assert.That(twoWayCompass.RowDefinitions[0].ActualHeight, Is.LessThan(2),
                    "2-Way controls must not reserve an empty top compass row.");
                Assert.That(twoWayCompass.RowDefinitions[2].ActualHeight, Is.LessThan(2),
                    "2-Way controls must not reserve an empty bottom compass row.");

                Assert.That(mapping.SetOutputRepresentation(1), Is.True);
                card.Measure(new Size(1050, 1200));
                card.Arrange(new Rect(0, 0, 1050, 1200));
                card.UpdateLayout();
                Assert.That(FindVisualChildren<Button>(card).Count(button =>
                    string.Equals(button.ToolTip as string,
                        "Binding categories, clearing and blocking", StringComparison.Ordinal) &&
                    button.Visibility == Visibility.Visible), Is.EqualTo(4),
                    "Four-way Button input must expose four individually assignable directions.");
                var labels = FindVisualChildren<TextBlock>(card)
                    .Select(text => text.Text).ToList();
                foreach (var glyph in new[] { "↑", "↓", "←", "→" })
                    Assert.That(labels, Does.Contain(glyph),
                        "Every spatial binding button must actually display its direction.");
                Assert.That(mapping.CompoundOutputSummary, Is.EqualTo("Choose D-pad / stick ▾"),
                    "An unassigned 4-Way output must offer a meaningful destination choice.");
            }
            finally
            {
                vm.Dispose();
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void TypedDirectionalAndNotConditionsRemainUsableAcrossTypeChanges()
        {
            EnsureApplicationResources();
            var context = new Context();
            context.PluginManager.Plugins = new List<Plugin>
            {
                new HidWizards.UCR.Plugins.Remapper.ButtonToButton(),
                new HidWizards.UCR.Plugins.Remapper.AxisToAxis()
            };
            var profile = context.ProfilesManager.CreateProfile("AND NOT regression", null, null);
            context.ProfilesManager.AddProfile(profile);
            profile.AddMapping("Typed AND").AddPlugin(
                new HidWizards.UCR.Plugins.Remapper.ButtonToButton());
            var vm = new ProfileViewModel(profile);
            try
            {
                var mapping = vm.MappingsList.Single();
                Assert.That(mapping.SetOutputRepresentation(2), Is.True);
                var first = mapping.InputConditions.Single();
                mapping.AddInputToCondition(first);
                first = mapping.InputConditions.Single();
                Assert.That(first.CoreInputs.Count, Is.EqualTo(2));
                Assert.That(first.AndInputs.Count, Is.EqualTo(1));
                var guard = first.AndInputs.Single();
                Assert.That(guard.DeviceBinding.IsDirectionalAndCondition, Is.True);
                mapping.SetInputNegated(guard, true);
                Assert.That(guard.DeviceBinding.InputExpressionNegated, Is.True);

                var card = new MappingCardControl
                {
                    InspectorMode = true, InlineWorkspaceMode = true,
                    DataContext = mapping
                };
                card.Measure(new Size(1050, 1200));
                card.Arrange(new Rect(0, 0, 1050, 1200));
                card.UpdateLayout();
                Assert.That(FindVisualChildren<Button>(card).Any(button =>
                    string.Equals(button.Content as string, "+AND", StringComparison.Ordinal)),
                    Is.True, "An AND control must exist in the actual typed mapping editor.");
                Assert.That(FindVisualChildren<ToggleButton>(card).Any(button =>
                    string.Equals(button.Content as string, "NOT", StringComparison.Ordinal)),
                    Is.True, "The AND gate must expose an actual NOT toggle.");

                // Capture the real WPF control at a representative two-column
                // editor width, so review can catch visual inconsistencies that
                // property assertions alone do not detect.
                card.Measure(new Size(850, 760));
                card.Arrange(new Rect(0, 0, 850, 760));
                card.UpdateLayout();
                var preview = System.IO.Path.Combine(
                    Environment.GetEnvironmentVariable("GITHUB_WORKSPACE") ??
                    System.IO.Path.GetTempPath(), "UCR-typed-conditions-preview.png");
                var bitmap = new RenderTargetBitmap(850, 760, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(card);
                var png = new PngBitmapEncoder();
                png.Frames.Add(BitmapFrame.Create(bitmap));
                using (var output = System.IO.File.Create(preview)) png.Save(output);
                TestContext.WriteLine("Rendered WPF AND/NOT editor: " + preview);

                Assert.That(mapping.SetInputRepresentation(2), Is.True);
                Assert.That(mapping.InputConditions.Single().CoreInputs.Count, Is.EqualTo(1));
                Assert.That(mapping.InputConditions.Single().AndInputs.Count, Is.EqualTo(1));
                Assert.That(mapping.InputConditions.Single().AndInputs.Single()
                    .DeviceBinding.InputExpressionNegated, Is.True);
                Assert.That(mapping.SetInputRepresentation(3), Is.True);
                Assert.That(mapping.InputConditions.Single().CoreInputs.Count, Is.EqualTo(2));
                Assert.That(mapping.InputConditions.Single().AndInputs.Count, Is.EqualTo(1));
                Assert.That(mapping.Mapping.RequiredInputBindingSlots,
                    Is.EqualTo(mapping.Mapping.DeviceBindings.Count));
                var saved = Context.DeepXmlClone(mapping.Mapping);
                Assert.That(saved.DeviceBindings.Count(binding => binding.IsDirectionalAndCondition),
                    Is.EqualTo(1), "AND gates must persist across profile save and reload.");
                Assert.That(saved.DeviceBindings.Single(binding => binding.IsDirectionalAndCondition)
                    .InputExpressionNegated, Is.True);
            }
            finally
            {
                vm.Dispose();
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void MissingPhysicalDeviceStillShowsActionableBindCaption()
        {
            var context = new Context();
            var profile = context.ProfilesManager.CreateProfile("Disconnected input", null, null);
            context.ProfilesManager.AddProfile(profile);
            var binding = new HidWizards.UCR.Core.Models.Binding.DeviceBinding(
                null, profile, DeviceIoType.Input)
            {
                IsBound = true,
                DeviceConfigurationGuid = Guid.NewGuid()
            };
            var viewModel = new DeviceBindingViewModel(binding);
            try
            {
                Assert.That(viewModel.BindButtonText, Is.EqualTo("Bind"));
                Assert.That(viewModel.CompactBindButtonText, Is.EqualTo("Bind"));
                Assert.That(binding.IsBound, Is.True,
                    "A missing provider must not destroy a saved physical assignment.");
            }
            finally
            {
                viewModel.Dispose();
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void TriggerAndAxisUseDifferentCapacitiesWithoutLockingTypeSelectors()
        {
            EnsureApplicationResources();
            var context = new Context();
            context.PluginManager.Plugins = new List<Plugin>
            {
                new HidWizards.UCR.Plugins.Remapper.ButtonToButton(),
                new HidWizards.UCR.Plugins.Remapper.AxisToAxis()
            };
            var profile = context.ProfilesManager.CreateProfile("Trigger and axis", null, null);
            context.ProfilesManager.AddProfile(profile);
            profile.AddMapping("Directional").AddPlugin(
                new HidWizards.UCR.Plugins.Remapper.ButtonToButton());
            var vm = new ProfileViewModel(profile);
            try
            {
                var mapping = vm.MappingsList.Single();
                Assert.That(mapping.CanChooseInputFourWay && mapping.CanChooseInputAxis &&
                    mapping.CanChooseInputTrigger && mapping.CanChooseInputButton, Is.True);
                Assert.That(mapping.CanChooseOutputFourWay && mapping.CanChooseOutputAxis &&
                    mapping.CanChooseOutputTrigger && mapping.CanChooseOutputButton, Is.True);

                // One Button drives one unidirectional Trigger; it does not
                // automatically grow a spurious opposing Button slot.
                Assert.That(mapping.SetOutputRepresentation(4), Is.True);
                Assert.That(mapping.Mapping.DirectionalCapacity, Is.EqualTo(1));
                Assert.That(mapping.Mapping.DeviceBindings.Count, Is.EqualTo(1));
                Assert.That(mapping.Mapping.Plugins.Count, Is.EqualTo(1));
                Assert.That(mapping.Plugins.Single().OutputType, Is.EqualTo("Axis"));
                Assert.That(mapping.OutputIsTrigger, Is.True);

                // Axis is bidirectional: one pair of Button directions drives it.
                Assert.That(mapping.SetOutputRepresentation(2), Is.True);
                Assert.That(mapping.Mapping.DirectionalCapacity, Is.EqualTo(2));
                Assert.That(mapping.Mapping.DeviceBindings.Count, Is.EqualTo(2));
                Assert.That(mapping.Mapping.Plugins.Count, Is.EqualTo(1));

                Assert.That(mapping.SetInputRepresentation(2), Is.True);
                Assert.That(mapping.Mapping.DeviceBindings.Count, Is.EqualTo(1));
                Assert.That(mapping.SetOutputRepresentation(4), Is.True);
                Assert.That(mapping.Mapping.DirectionalCapacity, Is.EqualTo(2),
                    "A signed axis can drive a positive-only Trigger without losing its source range.");
                Assert.That(mapping.SetInputRepresentation(4), Is.True);
                Assert.That(mapping.Mapping.DirectionalCapacity, Is.EqualTo(1));
                Assert.That(mapping.Mapping.DeviceBindings.Count, Is.EqualTo(1));
                Assert.That(mapping.Mapping.Plugins.Count, Is.EqualTo(1));
                Assert.That(mapping.InputIsTrigger && mapping.OutputIsTrigger, Is.True);

                Assert.That(mapping.SetOutputRepresentation(3), Is.True);
                Assert.That(mapping.SetInputRepresentation(1), Is.True);
                Assert.That(mapping.Mapping.DirectionalCapacity, Is.EqualTo(4));
                Assert.That(mapping.Mapping.Plugins.Count, Is.EqualTo(4));
                Assert.That(mapping.SetOutputRepresentation(4), Is.True,
                    "No type is locked: a full 4-Way source can drive four one-sided Trigger channels.");
                Assert.That(mapping.Mapping.DirectionalCapacity, Is.EqualTo(4));
                Assert.That(mapping.OutputIsTrigger, Is.True);
                Assert.That(mapping.Mapping.Plugins.Count, Is.EqualTo(4));
                Assert.That(Mapping.RequiredBindingSlots(4, 4), Is.EqualTo(4));
                Assert.That(mapping.SetInputRepresentation(4), Is.True);
                Assert.That(mapping.Mapping.DirectionalCapacity, Is.EqualTo(1),
                    "When both sides become Trigger, the four-way capacity should disappear.");
                Assert.That(mapping.Mapping.DeviceBindings.Count, Is.EqualTo(1));
                Assert.That(mapping.SetOutputRepresentation(1), Is.True);
                Assert.That(mapping.Mapping.DirectionalCapacity, Is.EqualTo(4));
                Assert.That(mapping.Mapping.DeviceBindings.Count, Is.EqualTo(4),
                    "One-sided Trigger inputs expand into four slots against a 4-Way destination.");
                Assert.That(mapping.SetOutputRepresentation(4), Is.True);
                Assert.That(mapping.Mapping.DirectionalCapacity, Is.EqualTo(1));
                Assert.That(mapping.SetInputRepresentation(3), Is.True);
                Assert.That(mapping.Mapping.DirectionalCapacity, Is.EqualTo(1));
                Assert.That(mapping.Mapping.Plugins.Count, Is.EqualTo(1));
            }
            finally
            {
                vm.Dispose();
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void OutputSelectionCanReplaceButtonWithAxisWithoutLosingDestination()
        {
            EnsureApplicationResources();
            var context = new Context();
            context.PluginManager.Plugins = new List<Plugin>
            {
                new HidWizards.UCR.Plugins.Remapper.ButtonToButton(),
                new HidWizards.UCR.Plugins.Remapper.ButtonToAxis(),
                new HidWizards.UCR.Plugins.Remapper.AxisToAxis()
            };
            var profile = context.ProfilesManager.CreateProfile("Output category conversion", null, null);
            context.ProfilesManager.AddProfile(profile);
            var outputDevice = new DeviceConfiguration(
                new Device("X360 Controller", "Core_ViGEm", "xb360", 0));
            profile.AddDeviceConfigurations(new List<DeviceConfiguration> { outputDevice },
                DeviceIoType.Output);
            profile.AddMapping("Output").AddPlugin(
                new HidWizards.UCR.Plugins.Remapper.ButtonToButton());
            var vm = new ProfileViewModel(profile);
            try
            {
                var mapping = vm.MappingsList.Single();
                var original = mapping.Plugins.Single().DeviceBindings.First(binding =>
                    binding.DeviceBinding.DeviceIoType == DeviceIoType.Output);
                var control = new MappingCardControl { DataContext = mapping };
                var axis = new HidWizards.UCR.Core.Models.Binding.DeviceBindingInfo
                {
                    DeviceBindingCategory = HidWizards.UCR.Core.Models.Binding.DeviceBindingCategory.Range,
                    KeyType = 3, KeyValue = 0, KeySubValue = 0
                };
                var method = typeof(MappingCardControl).GetMethod("ApplyOutputControlChoice",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                Assert.That(method, Is.Not.Null);
                var applied = (bool)method.Invoke(control,
                    new object[] { original, outputDevice.Guid, axis, 0 });
                Assert.That(applied, Is.True, "Choosing a controller axis must actually apply.");
                Assert.That(mapping.Plugins.Single().OutputType, Is.EqualTo("Axis"));
                var destination = mapping.Plugins.Single().DeviceBindings.First(binding =>
                    binding.DeviceBinding.DeviceIoType == DeviceIoType.Output).DeviceBinding;
                Assert.That(destination.DeviceConfigurationGuid, Is.EqualTo(outputDevice.Guid),
                    "Switching output category must retain the selected controller.");
                Assert.That(destination.IsBound, Is.True);
                Assert.That(destination.DeviceBindingCategory, Is.EqualTo(axis.DeviceBindingCategory));
                Assert.That(mapping.Mapping.DirectionalCapacity, Is.EqualTo(2));
                Assert.That(mapping.Mapping.EffectiveOutputRepresentation, Is.EqualTo(2));
                Assert.That(mapping.Mapping.EffectiveInputRepresentation, Is.EqualTo(3));
                Assert.That(mapping.DeviceBindings.Count, Is.EqualTo(2),
                    "One Axis output requires an opposing Button input pair.");
                Assert.That(mapping.CanChooseInputButton, Is.True);
                Assert.That(mapping.CanChooseInputAxis, Is.True);
                Assert.That(mapping.CanChooseInputFourWay, Is.True,
                    "Type selectors declare intent, including before a four-way control is bound.");
                Assert.That(mapping.SetInputRepresentation(2), Is.True);
                Assert.That(mapping.DeviceBindings.Count, Is.EqualTo(1),
                    "Selecting Axis merges the opposing pair into one compound slot.");
                mapping.AddOrInput();
                Assert.That(mapping.DeviceBindings.Count, Is.EqualTo(2),
                    "A second OR alternative in Axis mode must be a whole compound slot.");
                Assert.That(mapping.DeviceBindings.Select(input =>
                    input.DeviceBinding.InputExpressionGroup).Distinct().Count(), Is.EqualTo(2));
                Assert.That(mapping.SetInputRepresentation(3), Is.True);
                Assert.That(mapping.DeviceBindings.Count, Is.EqualTo(4),
                    "Switching back to Button restores one opposing pair per OR set.");

                // A captured Button overrides a manually selected Axis expectation.
                Assert.That(mapping.SetInputRepresentation(2), Is.True);
                var axisExpectation = mapping.DeviceBindings.First().DeviceBinding;
                axisExpectation.SetInputExpressionInputKind(0);
                axisExpectation.SetKeyTypeValue(1, 5, 0);
                Assert.That(mapping.Mapping.EffectiveInputRepresentation, Is.EqualTo(3));
                Assert.That(mapping.DeviceBindings.Count, Is.EqualTo(4),
                    "A captured Button splits every Axis OR set into a matching pair.");
                Assert.That(mapping.Mapping.DeviceBindings.Any(input =>
                    input.IsBound && input.InputExpressionInputKind == 0 && input.KeyValue == 5),
                    Is.True, "The newly captured Button must survive automatic type switching.");

                Assert.That(mapping.SetInputRepresentation(1), Is.True);
                Assert.That(mapping.SetOutputRepresentation(1), Is.True);
                Assert.That(mapping.IsCompoundOutput, Is.True);
                Assert.That(mapping.Plugins.Count, Is.EqualTo(2),
                    "A 4-Way stick output has two internal axis channels and one logical control.");

                var fourWayEditor = new MappingCardControl
                {
                    InspectorMode = true,
                    InlineWorkspaceMode = true,
                    DataContext = mapping
                };
                fourWayEditor.Measure(new Size(1050, 900));
                fourWayEditor.Arrange(new Rect(0, 0, 1050, 900));
                fourWayEditor.UpdateLayout();
                Assert.That(FindVisualChildren<Button>(fourWayEditor).Count(button =>
                    string.Equals(button.ToolTip as string,
                        "Choose a complete 4-Way output destination",
                        StringComparison.Ordinal)), Is.EqualTo(1),
                    "Four-way outputs must display one logical binding regardless of internal channel count.");

                var stick = new FourWayOutputChoice
                {
                    DeviceConfigurationGuid = outputDevice.Guid,
                    Controls = new[]
                    {
                        new HidWizards.UCR.Core.Models.Binding.DeviceBindingInfo
                        {
                            DeviceBindingCategory = HidWizards.UCR.Core.Models.Binding.DeviceBindingCategory.Range,
                            KeyType = 3, KeyValue = 0
                        },
                        new HidWizards.UCR.Core.Models.Binding.DeviceBindingInfo
                        {
                            DeviceBindingCategory = HidWizards.UCR.Core.Models.Binding.DeviceBindingCategory.Range,
                            KeyType = 3, KeyValue = 1
                        }
                    }
                };
                Assert.That(mapping.ApplyTypedFourWayOutput(stick), Is.True);
                Assert.That(mapping.Plugins.All(plugin =>
                    plugin.Plugin.Outputs.Single().IsBound), Is.True);

                var dpad = new FourWayOutputChoice
                {
                    DeviceConfigurationGuid = outputDevice.Guid,
                    IsDPad = true,
                    Controls = Enumerable.Range(0, 4)
                        .Select(index => new HidWizards.UCR.Core.Models.Binding.DeviceBindingInfo
                        {
                            DeviceBindingCategory = HidWizards.UCR.Core.Models.Binding.DeviceBindingCategory.Momentary,
                            KeyType = 2, KeyValue = index
                        }).ToArray()
                };
                Assert.That(mapping.ApplyTypedFourWayOutput(dpad), Is.True);
                Assert.That(mapping.IsCompoundOutput, Is.True);
                Assert.That(mapping.Mapping.CompoundDigitalOutput, Is.True);
                Assert.That(mapping.Mapping.RequiredOutputBindingSlots, Is.EqualTo(4));
                Assert.That(mapping.Plugins.Count, Is.EqualTo(4));
                Assert.That(mapping.Plugins.All(plugin =>
                    plugin.Plugin.Outputs.Single().DeviceBindingCategory ==
                        HidWizards.UCR.Core.Models.Binding.DeviceBindingCategory.Momentary), Is.True);

                var savedDpad = Context.DeepXmlClone(mapping.Mapping);
                Assert.That(savedDpad.CompoundDigitalOutput, Is.True,
                    "The four-way digital-output backing must survive a saved profile.");
                Assert.That(savedDpad.RequiredOutputBindingSlots, Is.EqualTo(4));
                Assert.That(savedDpad.Plugins.Count, Is.EqualTo(4));
                // XmlSerializer appends persisted outputs after constructor defaults.
                // Normal profile loading runs PostLoad to reconcile those entries.
                foreach (var restoredPlugin in savedDpad.Plugins)
                    restoredPlugin.PostLoad(context, profile);
                Assert.That(savedDpad.Plugins.All(plugin =>
                    plugin.Outputs.Count == 1 && plugin.Outputs[0].IsBound), Is.True,
                    "Reopened four-way D-pad outputs must each retain one assigned direction.");

                Assert.That(mapping.SetOutputRepresentation(2), Is.True);
                Assert.That(mapping.Plugins.Count, Is.EqualTo(2),
                    "Returning to Axis restores two axis outputs without changing 4-Way input.");

                // Digital compound presets and analogue axes are two different
                // physical structures even when both are labelled 4-Way.
                // Switching to Axis must not leave four hidden digital slots.
                mapping.Mapping.CompoundDigitalInput = true;
                Assert.That(mapping.SetInputRepresentation(1), Is.True);
                Assert.That(mapping.Mapping.DeviceBindings.Count, Is.EqualTo(8),
                    "Each of two OR alternatives requires its own four digital directions.");
                Assert.That(mapping.Mapping.RequiredInputBindingSlots, Is.EqualTo(8));
                Assert.That(mapping.SetInputRepresentation(2), Is.True);
                Assert.That(mapping.Mapping.CompoundDigitalInput, Is.False,
                    "Axis input must not retain digital-compound routing.");
                Assert.That(mapping.Mapping.DeviceBindings.Count, Is.EqualTo(2),
                    "Each OR alternative becomes one Axis after capacity shrinks from 4-Way.");
                Assert.That(mapping.SetInputRepresentation(1), Is.True);
                Assert.That(mapping.Mapping.CompoundDigitalInput, Is.True,
                    "Returning to 4-Way restores the previous digital compound preset.");
                Assert.That(mapping.Mapping.DeviceBindings.Count, Is.EqualTo(8));
                string analogueError;
                Assert.That(mapping.UseAnalogueCompoundInput(out analogueError), Is.True,
                    analogueError);
                Assert.That(mapping.Mapping.DeviceBindings.Count, Is.EqualTo(4),
                    "Explicitly choosing analogue 4-Way must restore two axis bindings per OR group.");
                Assert.That(mapping.Mapping.DeviceBindings.GroupBy(binding =>
                    binding.InputExpressionGroup).All(group => group.Count() == 2), Is.True);
                var savedAnalogue = Context.DeepXmlClone(mapping.Mapping);
                Assert.That(savedAnalogue.DeviceBindings.Count, Is.EqualTo(4));
                Assert.That(savedAnalogue.DeviceBindings.Select(binding =>
                    binding.InputExpressionGroup).Distinct().Count(), Is.EqualTo(2));
                Assert.That(savedAnalogue.InactiveInputRepresentations.Any(bank =>
                    bank.Bindings.Count == 8), Is.True,
                    "Converting back to Axis must retain the prior 4-Way keyboard layout.");

                // All preset menus must be transactional: a missing configured
                // input device must leave the input type, OR count and assignments
                // exactly as they were before the user opened the preset menu.
                var beforePreset = Context.DeepXmlClone(mapping.Mapping);
                string presetError;
                Assert.That(mapping.ApplyDirectionalKeyboardPreset("WASD", out presetError),
                    Is.False);
                Assert.That(mapping.ApplyDirectionalDPadPreset(out presetError), Is.False);
                Assert.That(mapping.ApplyTypedControllerFaceButtons(out presetError), Is.False);
                Assert.That(mapping.ApplyTypedControllerStickPreset(0, out presetError), Is.False);
                Assert.That(mapping.Mapping.InputRepresentation,
                    Is.EqualTo(beforePreset.InputRepresentation));
                Assert.That(mapping.Mapping.CompoundDigitalInput,
                    Is.EqualTo(beforePreset.CompoundDigitalInput));
                Assert.That(mapping.Mapping.DeviceBindings.Select(binding =>
                    binding.InputExpressionGroup).ToArray(),
                    Is.EqualTo(beforePreset.DeviceBindings.Select(binding =>
                        binding.InputExpressionGroup).ToArray()));
                Assert.That(mapping.Mapping.DeviceBindings.Count,
                    Is.EqualTo(beforePreset.DeviceBindings.Count));
            }
            finally
            {
                vm.Dispose();
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
        public void ProfileDeviceCardsHideProviderAndWrapWithinSidebar()
        {
            EnsureApplicationResources();

            var context = new Context();
            var profile = context.ProfilesManager.CreateProfile("Device card layout", null, null);
            context.ProfilesManager.AddProfile(profile);
            var configuration = new DeviceConfiguration(new Device(
                "M: Logitech USB Receiver With An Extended Name", "Core_Interception", "kbd", 0));
            profile.AddDeviceConfigurations(new List<DeviceConfiguration> { configuration }, DeviceIoType.Input);
            var viewModel = new ProfileDeviceListControlViewModel(profile,
                new List<DeviceConfiguration> { configuration }, DeviceIoType.Input);
            try
            {
                var control = new ProfileDeviceListControl
                {
                    Title = "Input devices",
                    DataContext = viewModel
                };
                control.Measure(new Size(300, 400));
                control.Arrange(new Rect(0, 0, 300, 400));
                control.UpdateLayout();

                var list = control.FindName("DeviceList") as ListView;
                Assert.That(list, Is.Not.Null);
                Assert.That(ScrollViewer.GetHorizontalScrollBarVisibility(list),
                    Is.EqualTo(ScrollBarVisibility.Disabled),
                    "The actual profile device list must not produce a horizontal scrollbar.");
                var row = list.ItemContainerGenerator.ContainerFromIndex(0) as ListViewItem;
                Assert.That(row, Is.Not.Null, "The profile device row must materialize.");
                var labels = FindVisualChildren<TextBlock>(row).ToList();
                Assert.That(labels.Any(label => label.Text != null &&
                    label.Text.Contains("Core_Interception")), Is.False,
                    "Provider identifiers must not appear below the device names.");
                var deviceLabel = labels.FirstOrDefault(label =>
                    label.Text != null && label.Text.Contains("Logitech"));
                Assert.That(deviceLabel, Is.Not.Null);
                Assert.That(deviceLabel.TextWrapping, Is.EqualTo(TextWrapping.Wrap));
                Assert.That(deviceLabel.MaxHeight, Is.LessThanOrEqualTo(38));
                Assert.That(FindVisualChildren<Button>(row).Any(button =>
                    (button.ToolTip as string)?.IndexOf("primary", StringComparison.OrdinalIgnoreCase) >= 0), Is.True,
                    "The existing primary-device selector must remain available.");
            }
            finally
            {
                viewModel.Dispose();
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
