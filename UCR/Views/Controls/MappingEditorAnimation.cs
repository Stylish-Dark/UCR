using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace HidWizards.UCR.Views.Controls
{
    // One animation policy for standalone cards and inline workspace editors.
    internal static class MappingEditorAnimation
    {
        internal static void AnimateHeight(FrameworkElement target, double from, double to,
            bool expanding, EventHandler completed)
        {
            if (target == null) return;
            var animation = new DoubleAnimation(from, to,
                TimeSpan.FromMilliseconds(expanding ? 190 : 150))
            {
                EasingFunction = new QuadraticEase
                {
                    EasingMode = expanding ? EasingMode.EaseOut : EasingMode.EaseIn
                }
            };
            if (completed != null) animation.Completed += completed;
            target.BeginAnimation(FrameworkElement.HeightProperty, animation);
        }
    }
}
