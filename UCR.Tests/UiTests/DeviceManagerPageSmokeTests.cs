using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HidWizards.UCR.Core.Models;
using HidWizards.UCR.ViewModels.Dashboard;
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
            Assert.That(item.AvailableOutlineColors.Length, Is.EqualTo(10));

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
                .FirstOrDefault(candidate => (candidate.ToolTip as string)?.StartsWith("Outline colour") == true);
            Assert.That(colourButton, Is.Not.Null,
                "The real device row failed before its compact outline-colour button was created.");
            Assert.That(colourButton.ActualHeight, Is.GreaterThan(0));
        }


        [Test]
        [Apartment(ApartmentState.STA)]
        public void DeviceManagerPageUsesSlimVerticalScrollbarWhenRowsOverflow()
        {
            EnsureApplicationResources();

            var items = Enumerable.Range(0, 24)
                .Select(index => new DeviceManagerItemViewModel(
                    new Device("Keyboard " + index, "Core_Interception", "Keyboard\\" + index, index),
                    DeviceIoType.Input, true, null, false, "keyboard", DeviceOutlineColor.Default))
                .ToList();

            var page = new DeviceManagerPage();
            page.DataContext = new FakeDevicePageViewModel(items);
            page.Measure(new Size(900, 360));
            page.Arrange(new Rect(0, 0, 900, 360));
            page.UpdateLayout();

            var list = page.FindName("DeviceList") as ListView;
            Assert.That(list, Is.Not.Null);
            list.UpdateLayout();

            var verticalScrollBar = FindVisualChildren<ScrollBar>(list)
                .FirstOrDefault(scrollBar => scrollBar.Orientation == Orientation.Vertical && scrollBar.Visibility == Visibility.Visible);
            Assert.That(verticalScrollBar, Is.Not.Null,
                "Overflowing device rows should expose a vertical scrollbar.");
            Assert.That(verticalScrollBar.ActualWidth, Is.GreaterThan(0));
            Assert.That(verticalScrollBar.ActualWidth, Is.LessThanOrEqualTo(9),
                "The Devices scrollbar should stay sleek and narrow.");
            var track = verticalScrollBar.Template.FindName("PART_Track", verticalScrollBar) as System.Windows.Controls.Primitives.Track;
            Assert.That(track, Is.Not.Null);
            Assert.That(track.Orientation, Is.EqualTo(Orientation.Vertical),
                "The slim scrollbar template must preserve vertical track orientation.");
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
