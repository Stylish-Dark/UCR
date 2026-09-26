using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using HidWizards.UCR.Core;
using HidWizards.UCR.Core.Models;
using HidWizards.UCR.ViewModels.Controls;
using HidWizards.UCR.ViewModels.Dashboard;
using HidWizards.UCR.ViewModels.ProfileViewModels;
using HidWizards.UCR.Views.Controls;
using HidWizards.UCR.Views.Dialogs;
using NUnit.Framework;

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
        public void ApplicationUsesDefaultRenderingSoWpfCanUseHardwareAcceleration()
        {
            EnsureApplicationResources();
            Assert.That(RenderOptions.ProcessRenderMode, Is.EqualTo(System.Windows.Interop.RenderMode.Default));
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void MappingCollapseKeepsBodyVisibleWhileTheCloseAnimationRuns()
        {
            EnsureApplicationResources();

            var context = new Context();
            var profile = context.ProfilesManager.CreateProfile("Animation", null, null);
            context.ProfilesManager.AddProfile(profile);
            profile.AddMapping("Mapping");

            var profileViewModel = new ProfileViewModel(profile);
            var card = new MappingCardControl { DataContext = profileViewModel.MappingsList.Single() };
            card.Measure(new Size(900, 600));
            card.Arrange(new Rect(0, 0, 900, 600));
            card.UpdateLayout();

            var expander = card.FindName("MappingExpander") as Expander;
            var body = card.FindName("ExpandedBodyHost") as ContentControl;
            Assert.That(expander, Is.Not.Null);
            Assert.That(body, Is.Not.Null);

            expander.IsExpanded = true;
            card.UpdateLayout();
            Assert.That(body.Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(body.ContentTemplate, Is.Not.Null);
            PumpDispatcherFor(TimeSpan.FromMilliseconds(220));
            Assert.That(body.ActualHeight, Is.GreaterThan(0));

            expander.IsExpanded = false;

            Assert.That(body.Visibility, Is.EqualTo(Visibility.Visible),
                "Collapse should animate the body to zero height before hiding it, not snap shut immediately.");

            profileViewModel.Dispose();
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

            var deviceGlyph = FindVisualChildren<DeviceGlyphControl>(row).FirstOrDefault();
            Assert.That(deviceGlyph, Is.Not.Null,
                "Every device-manager row should expose its vector device glyph.");
            Assert.That(deviceGlyph.Kind, Is.EqualTo(DeviceVisualKind.Xbox360));

            var colourButton = FindVisualChildren<Button>(row)
                .FirstOrDefault(candidate => (candidate.ToolTip as string)?.StartsWith("Device glyph colour") == true);
            Assert.That(colourButton, Is.Not.Null,
                "The real device row failed before its compact glyph-colour button was created.");
            Assert.That(colourButton.ActualHeight, Is.GreaterThan(0));
        }



        [Test]
        [Apartment(ApartmentState.STA)]
        public void AddDevicePickerMaterializesVectorControllerGlyphs()
        {
            EnsureApplicationResources();

            var devices = new List<Device>
            {
                new Device("PS4 DualShock", "Core_ViGEm", "ds4", 0),
                new Device("X360 Controller", "Core_ViGEm", "xb360", 0)
            };
            var picker = new DeviceSelectControl
            {
                DataContext = new DeviceSelectControlViewModel("Add output devices", devices, DeviceIoType.Output)
            };
            picker.Measure(new Size(360, 500));
            picker.Arrange(new Rect(0, 0, 360, 500));
            picker.UpdateLayout();

            var glyphKinds = FindVisualChildren<DeviceGlyphControl>(picker)
                .Select(candidate => candidate.Kind)
                .ToList();
            Assert.That(glyphKinds, Is.EquivalentTo(new[]
            {
                DeviceVisualKind.PlayStation4,
                DeviceVisualKind.Xbox360
            }), "Every add-device row should materialize the matching vector controller glyph.");
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
