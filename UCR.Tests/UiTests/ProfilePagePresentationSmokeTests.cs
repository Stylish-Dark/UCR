using System.Threading;
using System.Windows;
using System.Windows.Controls;
using HidWizards.UCR.Core;
using HidWizards.UCR.Core.Models;
using HidWizards.UCR.Views.Controls;
using HidWizards.UCR.Views.ProfileViews;
using NUnit.Framework;

namespace HidWizards.UCR.Tests.UiTests
{
    [TestFixture]
    [NonParallelizable]
    internal class ProfilePagePresentationSmokeTests
    {
        [Test]
        [Apartment(ApartmentState.STA)]
        public void DevicesPanelCollapsesToHeaderAndOwnsAConstrainedScrollbar()
        {
            EnsureApplicationResources();
            var context = new Context();
            var profile = context.ProfilesManager.CreateProfile("UI smoke", null, null);
            context.ProfilesManager.AddProfile(profile);

            using (var page = new ProfilePage(context, profile))
            {
                page.Measure(new Size(1280, 760));
                page.Arrange(new Rect(0, 0, 1280, 760));
                page.UpdateLayout();

                var panel = page.FindName("ProfileDevicesPanel") as Border;
                var expander = page.FindName("ProfileDevicesExpander") as Expander;
                var scroll = page.FindName("ProfileDevicesScrollViewer") as ScrollViewer;

                Assert.That(panel, Is.Not.Null);
                Assert.That(panel.VerticalAlignment, Is.EqualTo(VerticalAlignment.Top));
                Assert.That(expander, Is.Not.Null);
                Assert.That(scroll, Is.Not.Null);
                Assert.That(scroll.MaxHeight, Is.GreaterThan(200).And.LessThanOrEqualTo(380));
                Assert.That(scroll.VerticalScrollBarVisibility, Is.EqualTo(ScrollBarVisibility.Auto));

                expander.IsExpanded = false;
                page.UpdateLayout();
                Assert.That(panel.ActualHeight, Is.LessThan(70),
                    "Collapsed Devices should shrink to its header instead of leaving a full-height empty panel.");
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void BackControlIsACompactArrowOnlyButton()
        {
            EnsureApplicationResources();
            var context = new Context();
            var profile = context.ProfilesManager.CreateProfile("UI smoke", null, null);
            context.ProfilesManager.AddProfile(profile);

            using (var page = new ProfilePage(context, profile))
            {
                var back = page.FindName("BackButton") as Button;
                Assert.That(back, Is.Not.Null);
                Assert.That(back.Width, Is.InRange(36, 42));
                Assert.That(back.Height, Is.InRange(36, 42));
                Assert.That(back.Content, Is.Not.TypeOf<StackPanel>(),
                    "Back should be a browser-style arrow button, not an arrow-plus-label control.");
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void MappingHeaderKeepsAConstantFilterIndicatorLane()
        {
            EnsureApplicationResources();
            var card = new MappingCardControl();
            card.Measure(new Size(1200, 120));
            card.Arrange(new Rect(0, 0, 1200, 120));
            card.UpdateLayout();

            var header = card.FindName("HeaderGrid") as Grid;
            var indicator = card.FindName("FilterIndicator") as Border;
            Assert.That(header, Is.Not.Null);
            Assert.That(header.ColumnDefinitions.Count, Is.GreaterThanOrEqualTo(5));
            Assert.That(header.ColumnDefinitions[3].Width.IsAbsolute, Is.True);
            Assert.That(header.ColumnDefinitions[3].Width.Value, Is.EqualTo(34).Within(0.1));
            Assert.That(indicator, Is.Not.Null);
        }

        private static void EnsureApplicationResources()
        {
            if (Application.Current != null) return;
            var app = new App();
            app.InitializeComponent();
        }
    }
}
