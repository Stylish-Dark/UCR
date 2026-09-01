using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HidWizards.UCR.ViewModels.Dashboard;
using HidWizards.UCR.Views.Dialogs;
using NUnit.Framework;

namespace HidWizards.UCR.Tests.UiTests
{
    [TestFixture]
    [NonParallelizable]
    internal class DeviceManagerPageSmokeTests
    {
        private sealed class FakeDeviceRow
        {
            public string ProviderDeviceName => "Test controller";
            public string ProviderName => "Test provider";
            public string IoTypes => "Input + Output";
            public string Alias { get; set; }
            public bool CanPersist => true;
            public Brush CurrentOutlineBrush => Brushes.Green;
            public bool Hidden { get; set; }
            public bool CanHide => true;
            public bool CanRemoveFromUcr => true;
            public bool CanRemoveFromWindows => false;
            public string RemoveFromUcrToolTip => "Remove";
            public DeviceOutlineColorChoice[] AvailableOutlineColors => new DeviceOutlineColorChoice[0];
        }

        private sealed class FakeDevicePageViewModel
        {
            public IList<FakeDeviceRow> Devices { get; } = new List<FakeDeviceRow> { new FakeDeviceRow() };
            public FakeDeviceRow SelectedDevice { get; set; }
            public string DetectionButtonText => "DETECT DEVICE";
            public string DetectionStatus => string.Empty;
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void DeviceManagerPageMaterializesARowWhenItemsSourceHasADevice()
        {
            EnsureApplicationResources();

            var page = new DeviceManagerPage();
            page.DataContext = new FakeDevicePageViewModel();
            page.Measure(new Size(1200, 900));
            page.Arrange(new Rect(0, 0, 1200, 900));
            page.UpdateLayout();

            var list = page.FindName("DeviceList") as ListView;
            Assert.That(list, Is.Not.Null);
            Assert.That(list.Items.Count, Is.EqualTo(1));
            Assert.That(list.HasItems, Is.True);

            list.UpdateLayout();
            var row = list.ItemContainerGenerator.ContainerFromIndex(0) as ListViewItem;
            Assert.That(row, Is.Not.Null,
                "The Devices page had an item in its source but WPF failed to materialize a visible row.");
            Assert.That(row.ActualHeight, Is.GreaterThan(0));
        }

        private static void EnsureApplicationResources()
        {
            if (Application.Current != null) return;
            var app = new App();
            app.InitializeComponent();
        }
    }
}
