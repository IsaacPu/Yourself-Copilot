using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace LocalDesktopNote
{
    // Both surfaces share the same policy. Layout is a view preference, not content.
    internal sealed class MemoSplitLayout : IDisposable
    {
        private readonly Grid panel;
        private readonly FrameworkElement taskContent;
        private readonly GridSplitter divider;
        private readonly double memoMinimum;
        private readonly Func<double?> readRatio;
        private readonly Action<double?> saveRatio;
        private bool updating, dragging, disposed, refreshQueued;

        internal MemoSplitLayout(Grid panel, FrameworkElement naturalTaskContent, GridSplitter divider,
            double memoMinimum, Func<double?> readRatio, Action<double?> saveRatio)
        {
            this.panel = panel; taskContent = naturalTaskContent; this.divider = divider;
            this.memoMinimum = memoMinimum; this.readRatio = readRatio; this.saveRatio = saveRatio;
            ConfigureDivider(divider);
            panel.SizeChanged += SizeChanged; taskContent.SizeChanged += SizeChanged; panel.Loaded += Loaded;
            divider.DragStarted += DragStarted; divider.DragCompleted += DragCompleted;
            divider.PreviewKeyDown += KeyDown; divider.PreviewMouseLeftButtonDown += MouseDown;
            Refresh();
        }

        internal static GridSplitter CreateDivider()
        {
            var result = new GridSplitter(); ConfigureDivider(result); return result;
        }

        private static void ConfigureDivider(GridSplitter value)
        {
            value.Width = 64; value.Height = 22; value.HorizontalAlignment = HorizontalAlignment.Center;
            value.VerticalAlignment = VerticalAlignment.Center; value.Focusable = true;
            // The template draws keyboard focus on the grip. The default WPF
            // focus adorner otherwise outlines the entire full-width hit area.
            value.FocusVisualStyle = null;
            value.ResizeDirection = GridResizeDirection.Rows; value.ResizeBehavior = GridResizeBehavior.PreviousAndNext;
            value.Cursor = Cursors.Hand; value.Background = Brushes.Transparent; value.ShowsPreview = false;
            value.ToolTip = "抓住手柄调整高度；上下方向键微调，双击恢复自动分配。";
            System.Windows.Automation.AutomationProperties.SetName(value, "调整任务与随手记高度");
            var root = new FrameworkElementFactory(typeof(Grid)); root.SetValue(Panel.BackgroundProperty, Brushes.Transparent);
            var grip = new FrameworkElementFactory(typeof(Border), "Grip");
            grip.SetValue(FrameworkElement.WidthProperty, 64.0); grip.SetValue(FrameworkElement.HeightProperty, 22.0);
            grip.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
            grip.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(239, 241, 233)));
            grip.SetValue(Border.BorderThicknessProperty, new Thickness(1)); grip.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
            var icon = new FrameworkElementFactory(typeof(Image));
            icon.SetValue(Image.SourceProperty, AppVisuals.IconSource("more", new SolidColorBrush(Color.FromRgb(104, 121, 99))));
            icon.SetValue(FrameworkElement.WidthProperty, 18.0); icon.SetValue(FrameworkElement.HeightProperty, 18.0);
            icon.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            icon.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            icon.SetValue(UIElement.IsHitTestVisibleProperty, false); grip.AppendChild(icon); root.AppendChild(grip);
            var template = new ControlTemplate(typeof(GridSplitter)) { VisualTree = root };
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(225, 232, 218)), "Grip")); template.Triggers.Add(hover);
            var focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
            focus.Setters.Add(new Setter(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(100, 128, 107)), "Grip"));
            template.Triggers.Add(focus); value.Template = template;
        }

        private void Loaded(object sender, RoutedEventArgs e) { Refresh(); }
        private void SizeChanged(object sender, SizeChangedEventArgs e) { Refresh(); }
        private double Available { get { return Math.Max(0, panel.ActualHeight - panel.RowDefinitions[1].ActualHeight); } }

        private double NaturalTaskHeight()
        {
            double natural = taskContent.DesiredSize.Height;
            var parent = taskContent.Parent as FrameworkElement;
            if (parent != null) natural += parent.Margin.Top + parent.Margin.Bottom;
            var scroll = parent as ScrollViewer;
            if (scroll != null) natural += scroll.Padding.Top + scroll.Padding.Bottom;
            return natural + 8;
        }
        private double TaskHeight(double desired)
        {
            double available = Available;
            // A saved ratio is a preference, not permission to cut an empty
            // state or the first task into a narrow strip. Short lists keep
            // their natural height; longer lists retain a readable viewport.
            double natural = NaturalTaskHeight();
            double minimum = natural + memoMinimum <= available ? natural : Math.Min(natural, memoMinimum);
            if (minimum + memoMinimum > available) minimum = Math.Min(minimum, available * 0.4);
            minimum = Math.Max(Math.Min(48, available * 0.22), minimum);
            double memo = Math.Min(memoMinimum, available - minimum);
            return Math.Max(minimum, Math.Min(available - memo, desired));
        }
        private static bool Valid(double? value)
        {
            return value.HasValue && !Double.IsNaN(value.Value) && !Double.IsInfinity(value.Value) && value > 0 && value < 1;
        }

        internal void Refresh()
        {
            if (disposed || refreshQueued) return;
            // RenderTasks/expander events precede WPF's next Measure pass. Read the
            // natural content size after layout, including when ActualHeight stays
            // stretched to the same ScrollViewer viewport and no SizeChanged fires.
            refreshQueued = true;
            panel.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(delegate
            {
                refreshQueued = false; ApplyLayout();
            }));
        }
        private void ApplyLayout()
        {
            if (disposed || updating || dragging || panel.RowDefinitions.Count != 3 || Available <= 0) return;
            updating = true;
            try
            {
                double available = Available; double? ratio = readRatio();
                double natural = NaturalTaskHeight();
                if (natural <= 0) natural = available * 0.35;
                // Show a short list in full when both sections can fit. Only
                // overflowing lists use the proportional automatic allocation.
                double automatic = natural <= TaskHeight(available) ? natural : available * 0.45;
                double height = TaskHeight(Valid(ratio) ? available * ratio.Value : automatic);
                // Persistent MinHeight values feed back into the parent Grid's
                // Measure and can keep a once-large card from shrinking. Clamp the
                // weights instead; use native row minima only during a drag.
                ClearDragMinimums();
                SetRows(height, available);
            }
            finally { updating = false; }
        }
        private void SetRows(double height, double available)
        {
            double ratio = height / available;
            if (!panel.RowDefinitions[0].Height.IsStar || Math.Abs(panel.RowDefinitions[0].Height.Value - ratio) > 0.0001)
                panel.RowDefinitions[0].Height = new GridLength(ratio, GridUnitType.Star);
            if (!panel.RowDefinitions[2].Height.IsStar || Math.Abs(panel.RowDefinitions[2].Height.Value - (1 - ratio)) > 0.0001)
                panel.RowDefinitions[2].Height = new GridLength(1 - ratio, GridUnitType.Star);
        }
        private void DragStarted(object sender, DragStartedEventArgs e)
        {
            dragging = true;
            panel.RowDefinitions[0].MinHeight = TaskHeight(0);
            panel.RowDefinitions[2].MinHeight = Math.Min(memoMinimum, Available - panel.RowDefinitions[0].MinHeight);
        }
        private void ClearDragMinimums()
        {
            panel.RowDefinitions[0].MinHeight = panel.RowDefinitions[2].MinHeight = 0;
        }
        private void DragCompleted(object sender, DragCompletedEventArgs e)
        {
            dragging = false;
            double height = panel.RowDefinitions[0].ActualHeight;
            ClearDragMinimums();
            if (!e.Canceled && Available > 0) StoreHeight(height); else Refresh();
        }
        private void StoreHeight(double height)
        {
            if (Available <= 0) return;
            double ratio = TaskHeight(height) / Available;
            SetRows(TaskHeight(height), Available);
            double? previous = readRatio();
            if (!Valid(previous) || Math.Abs(previous.Value - ratio) > 0.0001) saveRatio(ratio);
        }
        private void KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Up && e.Key != Key.Down) return;
            e.Handled = true;
            double step = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 40 : 10;
            StoreHeight(panel.RowDefinitions[0].ActualHeight + (e.Key == Key.Up ? -step : step));
        }
        private void MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount != 2) return;
            e.Handled = true; saveRatio(null); Refresh();
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            panel.SizeChanged -= SizeChanged; taskContent.SizeChanged -= SizeChanged; panel.Loaded -= Loaded;
            divider.DragStarted -= DragStarted; divider.DragCompleted -= DragCompleted;
            divider.PreviewKeyDown -= KeyDown; divider.PreviewMouseLeftButtonDown -= MouseDown;
        }
    }
}
