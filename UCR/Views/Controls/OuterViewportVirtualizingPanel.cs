using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using HidWizards.UCR.ViewModels.ProfileViewModels;

namespace HidWizards.UCR.Views.Controls
{
    /// <summary>
    /// Virtualizes mapping rows against the parent workspace's single outer ScrollViewer.
    /// The existing group border, header, ListView row template, dimensions and selection
    /// remain unchanged. A nested ListView cannot use the stock VirtualizingStackPanel:
    /// the outer ScrollViewer measures it with unbounded height.
    /// </summary>
    public sealed class OuterViewportVirtualizingPanel : VirtualizingPanel
    {
        private sealed class RowHeight
        {
            public double Height;
            public double Width;
            public bool Expanded;
        }

        private readonly Dictionary<object, RowHeight> _heights = new Dictionary<object, RowHeight>();
        private readonly Dictionary<int, UIElement> _realized = new Dictionary<int, UIElement>();
        private ScrollViewer _outerViewer;
        private double _lastWidth;
        private bool _itemsDirty = true;
        private int _lastItemCount = -1;

        public OuterViewportVirtualizingPanel()
        {
            Loaded += (sender, args) =>
            {
                AttachOuterViewer();
                InvalidateMeasure();
            };
            Unloaded += (sender, args) =>
            {
                if (_outerViewer != null) _outerViewer.ScrollChanged -= OuterViewerOnScrollChanged;
                _outerViewer = null;
            };
        }

        private void AttachOuterViewer()
        {
            if (_outerViewer != null) return;
            for (DependencyObject current = this; current != null; current = VisualTreeHelper.GetParent(current))
            {
                var viewer = current as ScrollViewer;
                if (viewer == null || viewer.Name != "MappingSectionsScrollViewer") continue;
                _outerViewer = viewer;
                viewer.ScrollChanged += OuterViewerOnScrollChanged;
                break;
            }
        }

        private void OuterViewerOnScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.VerticalChange != 0 || e.ViewportHeightChange != 0 ||
                e.ViewportWidthChange != 0)
                InvalidateMeasure();
        }

        protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
        {
            base.OnItemsChanged(sender, args);
            _itemsDirty = true;
            InvalidateMeasure();
        }

        private double EstimatedHeight(object item, double width)
        {
            var mapping = item as MappingViewModel;
            RowHeight cached;
            if (_heights.TryGetValue(item, out cached) &&
                Math.Abs(cached.Width - width) < 0.5 &&
                cached.Expanded == (mapping?.IsExpanded == true))
                return cached.Height;

            // These are only off-screen estimates. Visible rows are always measured
            // using their original, unmodified WPF templates, then cached precisely.
            var alternativeRows = Math.Max(0, (mapping?.CollapsedInputRows?.Count ?? 1) - 1);
            var header = 74.0 + alternativeRows * 46.0;
            if (mapping?.IsExpanded != true) return header;
            var inputSlots = mapping.DeviceBindings?.Count ?? 1;
            var outputs = mapping.Plugins?.Count ?? 1;
            return header + 240.0 + Math.Max(0, inputSlots - 1) * 37.0 +
                   Math.Max(0, outputs - 1) * 79.0;
        }

        private double[] BuildOffsets(ItemCollection items, double width)
        {
            var offsets = new double[items.Count + 1];
            for (var i = 0; i < items.Count; i++)
                offsets[i + 1] = offsets[i] + EstimatedHeight(items[i], width);
            return offsets;
        }

        private void ReconcileExisting(ItemCollection items)
        {
            _realized.Clear();
            // An ItemsSource move or filter may change indices while keeping WPF
            // containers. Find each survivor by its current DataContext.
            for (var childIndex = 0; childIndex < InternalChildren.Count; childIndex++)
            {
                var child = InternalChildren[childIndex];
                var item = (child as FrameworkElement)?.DataContext;
                var index = item == null ? -1 : items.IndexOf(item);
                if (index >= 0) _realized[index] = child;
            }

            // Device events and scrolling only move the viewport; don't allocate
            // an index of every mapping just to prune unchanged height-cache keys.
            if (_itemsDirty || _lastItemCount != items.Count)
            {
                var valid = new HashSet<object>(items.Cast<object>());
                foreach (var obsolete in _heights.Keys.Where(item => !valid.Contains(item)).ToList())
                    _heights.Remove(obsolete);
                _lastItemCount = items.Count;
                _itemsDirty = false;
            }
        }

        private void RemoveOutside(int first, int last)
        {
            var generator = ItemContainerGenerator;
            foreach (var entry in _realized.OrderByDescending(pair =>
                InternalChildren.IndexOf(pair.Value)).ToArray())
            {
                if (entry.Key >= first && entry.Key <= last) continue;
                var childIndex = InternalChildren.IndexOf(entry.Value);
                if (childIndex < 0) { _realized.Remove(entry.Key); continue; }
                var position = generator.GeneratorPositionFromIndex(entry.Key);
                if (position.Index >= 0 && position.Offset == 0)
                    generator.Remove(position, 1);
                RemoveInternalChildRange(childIndex, 1);
                _realized.Remove(entry.Key);
            }
        }

        private void Realize(int first, int last)
        {
            var generator = ItemContainerGenerator;
            for (var index = first; index <= last; index++)
            {
                if (_realized.ContainsKey(index)) continue;
                var position = generator.GeneratorPositionFromIndex(index);
                using (generator.StartAt(position, GeneratorDirection.Forward, true))
                {
                    bool newContainer;
                    var child = generator.GenerateNext(out newContainer) as UIElement;
                    if (child == null) continue;
                    if (newContainer)
                    {
                        var insertAt = position.Offset == 0 ? position.Index : position.Index + 1;
                        insertAt = Math.Max(0, Math.Min(insertAt, InternalChildren.Count));
                        if (insertAt == InternalChildren.Count) AddInternalChild(child);
                        else InsertInternalChild(insertAt, child);
                        generator.PrepareItemContainer(child);
                    }
                    _realized[index] = child;
                }
            }
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            AttachOuterViewer();
            var owner = ItemsControl.GetItemsOwner(this);
            if (owner == null) return new Size(0, 0);
            var items = owner.Items;
            var width = availableSize.Width;
            if (double.IsInfinity(width) || double.IsNaN(width))
                width = Math.Max(1, owner.ActualWidth);
            width = Math.Max(1, width);
            _lastWidth = width;
            ReconcileExisting(items);
            var offsets = BuildOffsets(items, width);

            var first = 0;
            var last = items.Count - 1;
            if (VirtualizingPanel.GetIsVirtualizing(owner) && items.Count >= 18)
            {
                var viewHeight = Math.Max(1, _outerViewer?.ViewportHeight ??
                    (_outerViewer?.ActualHeight ?? 800));
                var top = 0.0;
                if (_outerViewer != null)
                {
                    try
                    {
                        top = TransformToAncestor(_outerViewer).Transform(new Point()).Y;
                    }
                    catch (InvalidOperationException)
                    {
                        top = 0;
                    }
                }

                // A full viewport of overscan keeps mouse-wheel scrolling smooth.
                var visibleMin = -top - viewHeight;
                var visibleMax = -top + viewHeight * 2.0;
                first = 0;
                while (first < items.Count && offsets[first + 1] < visibleMin) first++;
                last = first - 1;
                while (last + 1 < items.Count && offsets[last + 1] <= visibleMax) last++;
                if (first >= items.Count || (last < first && offsets[first] > visibleMax))
                {
                    first = 0;
                    last = -1;
                }
            }

            RemoveOutside(first, last);
            if (last >= first) Realize(first, last);
            var changed = false;
            foreach (var item in _realized)
            {
                var index = item.Key;
                if (index >= items.Count) continue;
                item.Value.Measure(new Size(width, double.PositiveInfinity));
                var measured = item.Value.DesiredSize.Height;
                if (measured <= 0 || double.IsInfinity(measured) || double.IsNaN(measured)) continue;
                if (Math.Abs(measured - EstimatedHeight(items[index], width)) > 1.0)
                    changed = true;
                _heights[items[index]] = new RowHeight
                {
                    Height = measured, Width = width,
                    Expanded = (items[index] as MappingViewModel)?.IsExpanded == true
                };
            }
            offsets = BuildOffsets(items, width);
            // Re-plan once with true row heights. No animation or geometry is
            // substituted for a visible item; it always uses its actual template.
            if (changed && VirtualizingPanel.GetIsVirtualizing(owner) && items.Count >= 18)
                Dispatcher.BeginInvoke((Action)InvalidateMeasure,
                    System.Windows.Threading.DispatcherPriority.Loaded);
            return new Size(width, offsets[items.Count]);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var owner = ItemsControl.GetItemsOwner(this);
            if (owner == null) return finalSize;
            var offsets = BuildOffsets(owner.Items, _lastWidth > 0 ? _lastWidth : finalSize.Width);
            foreach (var entry in _realized)
            {
                if (entry.Key >= owner.Items.Count) continue;
                entry.Value.Arrange(new Rect(0, offsets[entry.Key], finalSize.Width,
                    offsets[entry.Key + 1] - offsets[entry.Key]));
            }
            return finalSize;
        }

        // ListView keyboard navigation, accessibility and ScrollIntoView use
        // VirtualizingPanel.BringIndexIntoView. Its base implementation is empty;
        // without an override, off-screen rows could become unreachable by keyboard.
        protected internal override void BringIndexIntoView(int index)
        {
            var owner = ItemsControl.GetItemsOwner(this);
            if (owner == null || index < 0 || index >= owner.Items.Count) return;
            BringMappingIntoView(owner.Items[index]);
        }

        public void BringMappingIntoView(object item)
        {
            AttachOuterViewer();
            var owner = ItemsControl.GetItemsOwner(this);
            if (_outerViewer == null || owner == null) return;
            var index = owner.Items.IndexOf(item);
            if (index < 0) return;
            var offsets = BuildOffsets(owner.Items, Math.Max(1, _lastWidth));
            var panelTop = TransformToAncestor(_outerViewer).Transform(new Point()).Y;
            var start = _outerViewer.VerticalOffset + panelTop + offsets[index];
            _outerViewer.ScrollToVerticalOffset(Math.Max(0, start));
            InvalidateMeasure();
        }
    }
}
