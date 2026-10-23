using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace LocalDesktopNote
{
    // Real production views in an isolated folder; no Show, desktop parenting,
    // registry calls, production JSON, process changes, or synthetic redraws.
    internal static class MemoLayoutVerification
    {
        private static readonly StringBuilder Report = new StringBuilder();
        private static int passed, failed;
        private const string ReferenceMemo = "面试前想清楚的事\r\n1. 产品解决了谁的什么问题？\r\n2. 当前主要产品线和团队分工是什么？\r\n3. 入职后最先负责哪一段工作？\r\n\r\n我的经验\r\n传感器数据采集与分析\r\n把业务问题拆成可以验证的小步骤";

        private static void Check(bool condition, string message)
        {
            Report.AppendLine((condition ? "PASS: " : "FAIL: ") + message);
            if (condition) passed++; else failed++;
        }
        private static void Run(string name, Action action)
        {
            Report.AppendLine("CASE: " + name);
            try { action(); }
            catch (Exception error) { failed++; Report.AppendLine("FAIL: " + error); }
        }
        private static T Field<T>(object value, string name)
        {
            return (T)value.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(value);
        }
        private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                if (child is T) yield return (T)child;
                foreach (T nested in Descendants<T>(child)) yield return nested;
            }
        }
        private static Grid SplitGrid(TextBox editor)
        {
            DependencyObject current = editor;
            while (current != null)
            {
                var grid = current as Grid;
                if (grid != null && grid.RowDefinitions.Count == 3 && grid.Children.OfType<GridSplitter>().Any()) return grid;
                current = VisualTreeHelper.GetParent(current);
            }
            throw new InvalidOperationException("Memo editor is not hosted in a three-row adjustable split grid.");
        }
        private static void Layout(Grid view, double width, double height)
        {
            view.Width = width; view.Height = height;
            for (int i = 0; i < 3; i++)
            {
                view.Measure(new Size(width, height));
                view.Arrange(new Rect(0, 0, width, height));
                view.UpdateLayout();
                view.Dispatcher.Invoke(DispatcherPriority.Background, new Action(delegate { }));
            }
        }
        private static Rect Bounds(FrameworkElement item, Visual root)
        {
            return item.TransformToAncestor(root).TransformBounds(new Rect(item.RenderSize));
        }
        private static void Inside(FrameworkElement item, Grid view, string name)
        {
            Rect bounds = Bounds(item, view);
            Check(bounds.Left >= -1 && bounds.Top >= -1 && bounds.Right <= view.ActualWidth + 1 && bounds.Bottom <= view.ActualHeight + 1 && bounds.Width > 0 && bounds.Height > 0,
                name + " stays inside the measured view: " + bounds);
        }
        private static void MemoAboveInput(TextBox editor, TextBox taskInput, Grid view, string name)
        {
            Rect memo = Bounds(editor, view), input = Bounds(taskInput, view);
            Check(memo.Bottom <= input.Top + 1, name + " memo does not overlap add-task input: memoBottom=" + memo.Bottom + "; inputTop=" + input.Top);
        }
        private sealed class HiddenInputSource : PresentationSource
        {
            private Visual root;
            internal HiddenInputSource(Visual view) { root = view; }
            public override Visual RootVisual { get { return root; } set { root = value; } }
            public override bool IsDisposed { get { return false; } }
            protected override CompositionTarget GetCompositionTargetCore() { return null; }
        }
        private static void Snapshot(Grid view, string name)
        {
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth), (int)Math.Ceiling(view.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(view);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var file = File.Create(Path.Combine(Storage.Folder, name))) encoder.Save(file);
            Check(File.Exists(Path.Combine(Storage.Folder, name)), "native WPF snapshot written: " + name);
        }

        private sealed class Fixture : IDisposable
        {
            internal readonly WorkboardStore Store;
            internal readonly WorkboardWindow Card;
            internal readonly WorkbenchWindow Bench;
            internal Fixture(string name, int activeTasks) : this(name, activeTasks, true) { }
            internal Fixture(string name, int activeTasks, bool includeCompleted)
            {
                Store = new WorkboardStore(Path.Combine(Storage.Folder, "fixtures", name));
                Store.TodayProvider = delegate { return new DateTime(2026, 10, 22); };
                Store.State.Ideas = ReferenceMemo;
                if (includeCompleted) { var completed = Store.AddTask("已处理的示例任务", Store.Today); Store.SetCompleted(completed, true); }
                for (int i = 0; i < activeTasks; i++) Store.AddTask("今天第 " + (i + 1) + " 件需要处理的事：核对产品问题与具体工作", Store.Today);
                Store.Save(); Store.CheckpointMemoHistory(true);
                Card = new WorkboardWindow(Store); Bench = new WorkbenchWindow(Store);
            }
            internal int HistoryCount { get { return Directory.Exists(Store.HistoryFolder) ? Directory.GetFiles(Store.HistoryFolder, "*.json", SearchOption.AllDirectories).Length : 0; } }
            public void Dispose() { Card.Dispose(); Bench.Dispose(); }
        }

        private static ScrollViewer TaskScroll(TextBox editor)
        {
            Grid split = SplitGrid(editor);
            FrameworkElement root = split.Children.OfType<FrameworkElement>().First(c => Grid.GetRow(c) == 0);
            return root as ScrollViewer ?? Descendants<ScrollViewer>(root).First();
        }

        private static void FullyVisibleInTaskViewport(FrameworkElement item, TextBox editor, string name)
        {
            ScrollViewer scroll = TaskScroll(editor);
            ScrollContentPresenter viewport = Descendants<ScrollContentPresenter>(scroll).First();
            Rect itemBounds = Bounds(item, viewport);
            FrameworkElement content = scroll.Content as FrameworkElement;
            Check(item.Visibility == Visibility.Visible && item.ActualHeight > 0 && itemBounds.Top >= -1 && itemBounds.Bottom <= viewport.ActualHeight + 1,
                name + " is fully visible inside task viewport: top=" + itemBounds.Top + "; bottom=" + itemBounds.Bottom + "; viewport=" + viewport.ActualHeight
                + "; contentDesired=" + (content == null ? 0 : content.DesiredSize.Height) + "; extent=" + scroll.ExtentHeight + "; scrollViewport=" + scroll.ViewportHeight);
        }

        private static void ShortTaskViewportRegression()
        {
            using (var f = new Fixture("regression-empty-low-ratio", 0, false))
            {
                foreach (double? ratio in new double?[] { null, 0.001, 0.2 })
                {
                    f.Store.State.WorkbenchMemoTaskRatio = ratio;
                    Field<MemoSplitLayout>(f.Bench, "memoSplit").Refresh();
                    Layout(f.Bench.View, 1026, 800);
                    FullyVisibleInTaskViewport(Field<TextBlock>(f.Bench, "taskEmpty"), f.Bench.IdeasEditor,
                        "workbench 1026x800 ratio=" + (ratio.HasValue ? ratio.ToString() : "automatic") + " keeps the complete empty-state text visible");
                }
                Snapshot(f.Bench.View, "regression-empty-low-ratio.png");
            }
            using (var f = new Fixture("regression-one-completed", 0))
            {
                f.Bench.CompletedGroup.IsExpanded = true;
                foreach (double? ratio in new double?[] { null, 0.001, 0.2 })
                {
                    f.Store.State.WorkbenchMemoTaskRatio = ratio;
                    Field<MemoSplitLayout>(f.Bench, "memoSplit").Refresh();
                    Layout(f.Bench.View, 1026, 800);
                    string name = "workbench one completed task ratio=" + (ratio.HasValue ? ratio.ToString() : "automatic");
                    FullyVisibleInTaskViewport(Field<TextBlock>(f.Bench, "taskEmpty"), f.Bench.IdeasEditor, name + " empty-state text");
                    FullyVisibleInTaskViewport(f.Bench.CompletedGroup, f.Bench.IdeasEditor, name + " expanded completed group including its first complete row");
                    Check(f.Bench.IdeasEditor.ActualHeight >= 100, name + " preserves a multiline memo viewport");
                }
                Snapshot(f.Bench.View, "regression-one-completed-low-ratio.png");
                Layout(f.Bench.View, 1026, 740);
                FullyVisibleInTaskViewport(f.Bench.CompletedGroup, f.Bench.IdeasEditor, "workbench 1026x740 expanded completed row");
            }
            using (var f = new Fixture("regression-active-and-completed", 1))
            {
                f.Bench.CompletedGroup.IsExpanded = true; f.Card.CompletedGroup.IsExpanded = true;
                Layout(f.Bench.View, 1026, 800); Layout(f.Card.View, 529, 650);
                FullyVisibleInTaskViewport((FrameworkElement)f.Bench.TaskChecks.Single().Parent, f.Bench.IdeasEditor, "automatic workbench complete active task row");
                FullyVisibleInTaskViewport(f.Bench.CompletedGroup, f.Bench.IdeasEditor, "automatic workbench expanded completed group beside the active row");
                FullyVisibleInTaskViewport((FrameworkElement)f.Card.TaskChecks.Single().Parent, f.Card.IdeasEditor, "automatic large card complete active task row");
                FullyVisibleInTaskViewport(f.Card.CompletedGroup, f.Card.IdeasEditor, "automatic large card expanded completed group beside the active row");
                Snapshot(f.Bench.View, "regression-active-and-completed-automatic.png");
                foreach (double ratio in new[] { 0.001, 0.2 })
                {
                    f.Store.State.WorkbenchMemoTaskRatio = ratio;
                    Field<MemoSplitLayout>(f.Bench, "memoSplit").Refresh(); Layout(f.Bench.View, 1026, 800);
                    ScrollViewer scroll = TaskScroll(f.Bench.IdeasEditor); scroll.ScrollToTop(); f.Bench.View.UpdateLayout();
                    FullyVisibleInTaskViewport((FrameworkElement)f.Bench.TaskChecks.Single().Parent, f.Bench.IdeasEditor,
                        "workbench ratio=" + ratio + " retains the complete first active row");
                    FullyVisibleInTaskViewport(f.Bench.CompletedGroup, f.Bench.IdeasEditor,
                        "workbench short mixed list ratio=" + ratio + " shows the complete expanded completed group including its date without scrolling");
                    scroll.ScrollToBottom(); f.Bench.View.UpdateLayout();
                    FullyVisibleInTaskViewport((FrameworkElement)f.Bench.CompletedChecks.Single().Parent, f.Bench.IdeasEditor,
                        "workbench ratio=" + ratio + " can reach the complete completed row by task scrolling");
                }
                Snapshot(f.Bench.View, "regression-active-and-completed-low-ratio.png");
            }
        }

        private static void DividerFocusRegression()
        {
            using (var f = new Fixture("regression-divider-focus", 0))
            {
                Layout(f.Bench.View, 1026, 800); Layout(f.Card.View, 529, 500);
                foreach (bool card in new[] { false, true })
                {
                    Grid split = SplitGrid(card ? f.Card.IdeasEditor : f.Bench.IdeasEditor);
                    GridSplitter divider = split.Children.OfType<GridSplitter>().Single();
                    string name = card ? "card" : "workbench";
                    Check(divider.Focusable, name + " divider remains keyboard accessible");
                    Check(divider.FocusVisualStyle == null, name + " divider suppresses the default full-width dotted focus adorner");
                    var focusTargets = divider.Template.Triggers.OfType<Trigger>()
                        .Where(t => t.Property == UIElement.IsKeyboardFocusedProperty || t.Property == UIElement.IsKeyboardFocusWithinProperty)
                        .SelectMany(t => t.Setters.OfType<Setter>())
                        .Select(s => String.IsNullOrEmpty(s.TargetName) ? null : divider.Template.FindName(s.TargetName, divider) as FrameworkElement)
                        .ToArray();
                    Check(focusTargets.Length > 0 && focusTargets.All(t => t != null && t.ActualWidth > 0 && t.ActualWidth <= 64 && t.ActualWidth < split.ActualWidth / 2),
                        name + " keyboard focus feedback is confined to the small drag grip");
                    Check(divider.Cursor == Cursors.Hand, name + " grip uses the hand cursor");
                    Check(divider.ActualWidth <= 64 && divider.ActualHeight <= 22, name + " only the small grip is an interactive resize target");
                    Rect gripBounds = Bounds(divider, split);
                    var centerResult = VisualTreeHelper.HitTest(split, new Point(gripBounds.Left + gripBounds.Width / 2, gripBounds.Top + gripBounds.Height / 2));
                    var lineResult = VisualTreeHelper.HitTest(split, new Point(5, gripBounds.Top + gripBounds.Height / 2));
                    DependencyObject centerHit = centerResult == null ? null : centerResult.VisualHit;
                    DependencyObject lineHit = lineResult == null ? null : lineResult.VisualHit;
                    Check(centerHit != null && (centerHit == divider || divider.IsAncestorOf(centerHit)), name + " grip center receives drag input");
                    Check(lineHit == null || (lineHit != divider && !divider.IsAncestorOf(lineHit)), name + " separator outside the grip does not receive drag input");
                }
            }
        }

        private static void LowRatioIndependentScrollRegression()
        {
            using (var f = new Fixture("regression-low-ratio-independent-scroll", 24))
            {
                f.Bench.CompletedGroup.IsExpanded = true;
                f.Bench.IdeasEditor.Text = String.Join("\r\n", Enumerable.Range(1, 100).Select(i => "示例随手记第 " + i + " 行：用于验证独立滚动。"));
                f.Bench.Flush();
                f.Store.State.WorkbenchMemoTaskRatio = 0.001;
                Field<MemoSplitLayout>(f.Bench, "memoSplit").Refresh(); Layout(f.Bench.View, 1026, 800);
                ScrollViewer scroll = TaskScroll(f.Bench.IdeasEditor);
                FullyVisibleInTaskViewport((FrameworkElement)f.Bench.TaskChecks.First().Parent, f.Bench.IdeasEditor,
                    "long task list under low ratio keeps the first active row usable");
                Check(scroll.ScrollableHeight > 0 && f.Bench.IdeasEditor.ExtentHeight > f.Bench.IdeasEditor.ViewportHeight,
                    "low persisted ratio retains separate overflow viewports for task list and memo");
                double memoOffset = f.Bench.IdeasEditor.VerticalOffset;
                scroll.ScrollToBottom(); f.Bench.View.UpdateLayout();
                FullyVisibleInTaskViewport((FrameworkElement)f.Bench.CompletedChecks.Single().Parent, f.Bench.IdeasEditor,
                    "long task list under low ratio can scroll to a complete completed row");
                Check(scroll.VerticalOffset > 0 && f.Bench.IdeasEditor.VerticalOffset == memoOffset,
                    "low-ratio task scrolling leaves memo position unchanged");
                double taskOffset = scroll.VerticalOffset;
                f.Bench.IdeasEditor.ScrollToEnd(); f.Bench.View.UpdateLayout();
                Check(taskOffset == scroll.VerticalOffset && f.Bench.IdeasEditor.VerticalOffset > 0,
                    "low-ratio memo scrolling leaves task position unchanged");
                Snapshot(f.Bench.View, "regression-low-ratio-independent-scroll.png");
            }
        }

        private static void EmptyLayouts()
        {
            using (var f = new Fixture("empty-reference", 0))
            {
                foreach (Size size in new[] { new Size(1026, 800), new Size(1026, 740), new Size(800, 600) })
                {
                    Layout(f.Bench.View, size.Width, size.Height);
                    Grid split = SplitGrid(f.Bench.IdeasEditor);
                    Check(split.RowDefinitions[0].ActualHeight < split.RowDefinitions[2].ActualHeight, "workbench " + size + " gives the empty task area's unused space to memo");
                    Check(f.Bench.IdeasEditor.ActualHeight >= 100, "workbench " + size + " memo displays several lines without reducing font size");
                    Check(f.Bench.IdeasEditor.FontSize == 16, "workbench compact memo font stays 16 at " + size);
                    Inside(f.Bench.IdeasEditor, f.Bench.View, "workbench memo " + size);
                    Inside(f.Bench.NewTaskEditor, f.Bench.View, "workbench add-task " + size);
                    Report.AppendLine("MEASURE workbench " + size + ": task=" + split.RowDefinitions[0].ActualHeight + "; memo=" + split.RowDefinitions[2].ActualHeight + "; editor=" + f.Bench.IdeasEditor.ActualHeight);
                }
                Layout(f.Bench.View, 1026, 800); Snapshot(f.Bench.View, "workbench-empty-reference.png");
                foreach (Size size in new[] { new Size(529, 400), new Size(529, 500), new Size(350, 290) })
                {
                    Layout(f.Card.View, size.Width, size.Height);
                    Grid split = SplitGrid(f.Card.IdeasEditor);
                    Check(split.RowDefinitions[0].ActualHeight >= 20 && split.RowDefinitions[2].ActualHeight >= 45, "card " + size + " keeps both task and memo areas usable");
                    if (size.Height >= 400) Check(f.Card.IdeasEditor.ActualHeight >= 55, "card " + size + " shows at least three text lines rather than a one-line strip");
                    Check(f.Card.IdeasEditor.FontSize == 14, "card memo font stays 14 at " + size);
                    Inside(f.Card.IdeasEditor, f.Card.View, "card memo " + size);
                    Inside(f.Card.NewTaskEditor, f.Card.View, "card add-task " + size);
                    Inside((FrameworkElement)f.Card.View.FindName("CardSaveStatus"), f.Card.View, "card save status " + size);
                    MemoAboveInput(f.Card.IdeasEditor, f.Card.NewTaskEditor, f.Card.View, "card " + size);
                    if (size.Width == 350) Snapshot(f.Card.View, "card-minimum-after-resize.png");
                    Report.AppendLine("MEASURE card " + size + ": task=" + split.RowDefinitions[0].ActualHeight + "; memo=" + split.RowDefinitions[2].ActualHeight + "; editor=" + f.Card.IdeasEditor.ActualHeight);
                }
                Layout(f.Card.View, 529, 400); Snapshot(f.Card.View, "card-empty-reference.png");
                Check(!f.Card.Visible && !f.Card.Attached && !f.Bench.Visible, "both real native windows remain hidden and desktop-unattached");
            }
        }

        private static void OverflowAndScroll()
        {
            using (var f = new Fixture("long-memo-many-tasks", 28))
            {
                string longMemo = String.Join("\r\n", Enumerable.Range(1, 100).Select(i => "第 " + i + " 条随手记：把业务问题拆成可以验证的小步骤。"));
                f.Bench.IdeasEditor.Text = longMemo; f.Bench.Flush();
                Layout(f.Bench.View, 1026, 740); Layout(f.Card.View, 529, 400);
                foreach (bool card in new[] { false, true })
                {
                    Grid view = card ? f.Card.View : f.Bench.View; TextBox editor = card ? f.Card.IdeasEditor : f.Bench.IdeasEditor;
                    Grid split = SplitGrid(editor); string name = card ? "card" : "workbench";
                    FrameworkElement taskRoot = split.Children.OfType<FrameworkElement>().First(c => Grid.GetRow(c) == 0);
                    var taskScroll = taskRoot as ScrollViewer ?? Descendants<ScrollViewer>(taskRoot).First();
                    Check(taskScroll.ScrollableHeight > 0 && taskScroll.ViewportHeight > 0, name + " many tasks have their own vertical scrolling viewport");
                    Check(editor.ExtentHeight > editor.ViewportHeight && editor.ViewportHeight > 0, name + " long memo has its own vertical scrolling viewport");
                    double memoOffset = editor.VerticalOffset; taskScroll.ScrollToBottom(); view.UpdateLayout();
                    Check(editor.VerticalOffset == memoOffset && taskScroll.VerticalOffset > 0, name + " scrolling tasks leaves memo scroll position unchanged");
                    double taskOffset = taskScroll.VerticalOffset; editor.ScrollToEnd(); view.UpdateLayout();
                    Check(taskScroll.VerticalOffset == taskOffset && editor.VerticalOffset > 0, name + " scrolling memo leaves task scroll position unchanged");
                    Inside(editor, view, name + " long editor");
                    Check(editor.FontSize == (card ? 14 : 16), name + " does not shrink text when content exceeds available space");
                }
                f.Bench.CompletedGroup.IsExpanded = true; Layout(f.Bench.View, 1026, 740);
                Check(f.Bench.CompletedChecks.Count == 1 && f.Bench.IdeasEditor.ActualHeight > 0, "expanding completed tasks preserves memo viewport");
                Snapshot(f.Bench.View, "workbench-overflow.png"); Snapshot(f.Card.View, "card-overflow.png");
            }
        }

        private static void PreferencePersistence()
        {
            using (var f = new Fixture("view-preferences", 2))
            {
                int history = f.HistoryCount;
                var contentMethod = typeof(WorkboardStore).GetMethod("ContentBytes", BindingFlags.Static | BindingFlags.NonPublic);
                byte[] contentBefore = (byte[])contentMethod.Invoke(null, new object[] { f.Store.State, true });
                f.Store.State.WorkbenchMemoTaskRatio = 0.5; f.Store.State.CardMemoTaskRatio = 0.4;
                Field<MemoSplitLayout>(f.Bench, "memoSplit").Refresh(); Field<MemoSplitLayout>(f.Card, "memoSplit").Refresh();
                Layout(f.Bench.View, 1026, 800); Layout(f.Card.View, 529, 400); f.Store.Save();
                var reopened = new WorkboardStore(Path.GetDirectoryName(f.Store.PathName));
                Check(reopened.State.WorkbenchMemoTaskRatio == 0.5 && reopened.State.CardMemoTaskRatio == 0.4, "independent workbench/card split ratios survive Store.Save and reopening");
                Check(f.HistoryCount == history, "changing only split preferences creates no history entries");
                byte[] contentAfter = (byte[])contentMethod.Invoke(null, new object[] { f.Store.State, true });
                Check(contentBefore.SequenceEqual(contentAfter), "split ratios are excluded from history ContentBytes equality");
                Check(!Field<bool>(f.Bench, "dirty") && !Field<bool>(f.Card, "dirty"), "ratio refresh alone does not mark either memo's content dirty");
                Grid bs = SplitGrid(f.Bench.IdeasEditor), cs = SplitGrid(f.Card.IdeasEditor);
                double br = bs.RowDefinitions[0].ActualHeight / (bs.RowDefinitions[0].ActualHeight + bs.RowDefinitions[2].ActualHeight);
                Check(Math.Abs(br - 0.5) < 0.03, "workbench persisted ratio inside usable bounds controls actual measured split");
                Check(cs.RowDefinitions[0].ActualHeight > 0 && cs.RowDefinitions[2].ActualHeight > 0, "card persisted ratio is constrained to keep both areas visible");
            }
        }

        private static void NaturalContentShrinksAgain()
        {
            using (var f = new Fixture("natural-content-shrink", 1))
            {
                foreach (bool card in new[] { false, true })
                {
                    Grid view = card ? f.Card.View : f.Bench.View; TextBox editor = card ? f.Card.IdeasEditor : f.Bench.IdeasEditor;
                    Expander completed = card ? f.Card.CompletedGroup : f.Bench.CompletedGroup;
                    double width = card ? 529 : 1026, height = card ? 650 : 1000;
                    completed.IsExpanded = false; Layout(view, width, height);
                    Grid split = SplitGrid(editor); double collapsed = split.RowDefinitions[0].ActualHeight;
                    completed.IsExpanded = true; Layout(view, width, height);
                    double expanded = split.RowDefinitions[0].ActualHeight;
                    Report.AppendLine("MEASURE " + (card ? "card" : "workbench") + " natural completed rows: collapsed=" + collapsed + "; expanded=" + expanded);
                    Check(expanded > collapsed + 8, (card ? "card" : "workbench") + " expanding completed tasks grows the natural task viewport");
                    completed.IsExpanded = false; Layout(view, width, height);
                    Check(split.RowDefinitions[0].ActualHeight < expanded - 8, (card ? "card" : "workbench") + " collapsing completed tasks gives the released height back to memo");
                }
                Layout(f.Bench.View, 1026, 1000); Layout(f.Card.View, 529, 650);
                double benchBefore = SplitGrid(f.Bench.IdeasEditor).RowDefinitions[0].ActualHeight;
                double cardBefore = SplitGrid(f.Card.IdeasEditor).RowDefinitions[0].ActualHeight;
                foreach (WorkTask task in f.Store.State.Tasks.Where(t => !t.Done).ToArray()) f.Store.Remove(task);
                f.Store.Save(); Layout(f.Bench.View, 1026, 1000); Layout(f.Card.View, 529, 650);
                Check(SplitGrid(f.Bench.IdeasEditor).RowDefinitions[0].ActualHeight < benchBefore - 20, "workbench removing a short active list tightens the task area after real store refresh");
                Check(SplitGrid(f.Card.IdeasEditor).RowDefinitions[0].ActualHeight < cardBefore - 20, "card removing a short active list tightens the task area after real store refresh");
            }
        }

        private static void ExtremeRatios()
        {
            using (var f = new Fixture("extreme-ratios", 0))
            {
                foreach (double ratio in new[] { 0.0, -1.0, 1.0, 2.0, Double.NaN, Double.PositiveInfinity, Double.NegativeInfinity })
                {
                    foreach (bool card in new[] { false, true })
                    {
                        if (card) f.Store.State.CardMemoTaskRatio = ratio; else f.Store.State.WorkbenchMemoTaskRatio = ratio;
                        object target = card ? (object)f.Card : f.Bench;
                        Field<MemoSplitLayout>(target, "memoSplit").Refresh();
                        Grid view = card ? f.Card.View : f.Bench.View; TextBox editor = card ? f.Card.IdeasEditor : f.Bench.IdeasEditor;
                        Layout(view, card ? 350 : 800, card ? 290 : 600);
                        Grid split = SplitGrid(editor);
                        Check(!Double.IsNaN(split.RowDefinitions[0].ActualHeight) && split.RowDefinitions[0].ActualHeight >= 20 && split.RowDefinitions[2].ActualHeight >= 45,
                            (card ? "card" : "workbench") + " invalid ratio " + ratio + " falls back to finite usable automatic layout");
                        Inside(editor, view, (card ? "card" : "workbench") + " invalid ratio " + ratio);
                    }
                }
                foreach (double ratio in new[] { 0.001, 0.999 })
                {
                    f.Store.State.WorkbenchMemoTaskRatio = ratio; f.Store.State.CardMemoTaskRatio = ratio;
                    Field<MemoSplitLayout>(f.Bench, "memoSplit").Refresh(); Field<MemoSplitLayout>(f.Card, "memoSplit").Refresh();
                    Layout(f.Bench.View, 800, 600); Layout(f.Card.View, 350, 290);
                    Check(f.Bench.IdeasEditor.ActualHeight >= 70 && f.Card.IdeasEditor.ActualHeight > 0, "valid extreme ratio " + ratio + " cannot collapse memo input");
                    Inside(f.Bench.NewTaskEditor, f.Bench.View, "workbench input under extreme ratio");
                    Inside(f.Card.NewTaskEditor, f.Card.View, "card input under extreme ratio");
                }
            }
        }

        private static void NavigationKeepsOneEditor()
        {
            using (var f = new Fixture("page-editor-ownership", 0))
            {
                TextBox original = f.Bench.IdeasEditor;
                string text = ReferenceMemo + "\r\n未切页前新增的一句话。";
                original.Text = text;
                var navigate = typeof(WorkbenchWindow).GetMethod("Navigate", BindingFlags.Instance | BindingFlags.NonPublic);
                foreach (string page in new[] { "memo", "history", "tasks", "settings", "memo", "tasks" })
                {
                    navigate.Invoke(f.Bench, new object[] { page }); Layout(f.Bench.View, 1026, 800);
                    Check(Object.ReferenceEquals(original, f.Bench.IdeasEditor) && original.Text == text && f.Store.State.Ideas == text, "navigate " + page + " retains the same editor and newest text");
                    Check(original.Parent == Field<Border>(f.Bench, page == "memo" ? "fullMemoHost" : "compactMemoHost"), "navigate " + page + " reparents only the shared memo editor to the expected host");
                }
                Check(f.Bench.IdeasEditor.FontSize == 16 && f.Card.IdeasEditor.Text == text, "returning to tasks restores compact font and peer card shows newest memo");
                var reopened = new WorkboardStore(Path.GetDirectoryName(f.Store.PathName));
                Check(reopened.State.Ideas == text, "page switches preserve edited content on disk");
            }
        }

        private static void SplitterRoutedGestures()
        {
            using (var f = new Fixture("splitter-routed-gestures", 10))
            {
                foreach (bool card in new[] { false, true })
                {
                    // Card's preview mouse route normally promotes it to an
                    // editable native window. Keep this fixture in edit state
                    // before routed input so that no Show/activation is invoked.
                    if (card) typeof(WorkboardWindow).GetField("editing", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(f.Card, true);
                    Grid view = card ? f.Card.View : f.Bench.View; TextBox editor = card ? f.Card.IdeasEditor : f.Bench.IdeasEditor;
                    // Start inside the usable range. Automatic allocation can
                    // already sit at the task-content safety minimum.
                    if (card) f.Store.State.CardMemoTaskRatio = 0.55; else f.Store.State.WorkbenchMemoTaskRatio = 0.55;
                    Field<MemoSplitLayout>(card ? (object)f.Card : f.Bench, "memoSplit").Refresh();
                    Layout(view, card ? 529 : 1026, card ? 500 : 800);
                    Grid split = SplitGrid(editor); GridSplitter divider = split.Children.OfType<GridSplitter>().Single();
                    int history = f.HistoryCount; double before = split.RowDefinitions[0].ActualHeight;
                    divider.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
                    // Hidden GridSplitter has no native pointer capture/source.
                    // Supply the row sizes a native drag would have produced,
                    // then exercise the production routed completion/save path.
                    double available = before + split.RowDefinitions[2].ActualHeight;
                    split.RowDefinitions[0].Height = new GridLength(before - 45);
                    split.RowDefinitions[2].Height = new GridLength(available - before + 45);
                    Layout(view, card ? 529 : 1026, card ? 500 : 800);
                    divider.RaiseEvent(new DragCompletedEventArgs(0, -45, false) { RoutedEvent = Thumb.DragCompletedEvent });
                    Layout(view, card ? 529 : 1026, card ? 500 : 800);
                    double? ratio = card ? f.Store.State.CardMemoTaskRatio : f.Store.State.WorkbenchMemoTaskRatio;
                    Check(ratio.HasValue && split.RowDefinitions[0].ActualHeight < before - 10, (card ? "card" : "workbench") + " routed Thumb completion persists a controlled row-height resize");
                    Check(f.HistoryCount == history, (card ? "card" : "workbench") + " routed split adjustment leaves history unchanged");
                    var doubleClick = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent };
                    // WPF normally fills ClickCount when promoting device input.
                    // Set that device-supplied field explicitly for this hidden routed test.
                    typeof(MouseButtonEventArgs).GetProperty("ClickCount").SetValue(doubleClick, 2, null);
                    divider.RaiseEvent(doubleClick);
                    Layout(view, card ? 529 : 1026, card ? 500 : 800);
                    ratio = card ? f.Store.State.CardMemoTaskRatio : f.Store.State.WorkbenchMemoTaskRatio;
                    Check(!ratio.HasValue, (card ? "card" : "workbench") + " routed double-click resets split preference to automatic");
                    before = split.RowDefinitions[0].ActualHeight;
                    PresentationSource inputSource = PresentationSource.FromVisual(view) ?? new HiddenInputSource(view);
                    divider.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, inputSource, Environment.TickCount, Key.Up) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                    Layout(view, card ? 529 : 1026, card ? 500 : 800);
                    ratio = card ? f.Store.State.CardMemoTaskRatio : f.Store.State.WorkbenchMemoTaskRatio;
                    Check(ratio.HasValue && split.RowDefinitions[0].ActualHeight < before, (card ? "card" : "workbench") + " routed Up key adjusts split and stores view preference");
                    Check(f.HistoryCount == history, (card ? "card" : "workbench") + " routed keyboard adjustment creates no history snapshot");
                    for (int i = 0; i < 30; i++)
                        divider.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, inputSource, Environment.TickCount, Key.Up) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                    Layout(view, card ? 529 : 1026, card ? 500 : 800);
                    Check((card ? f.Store.State.CardMemoTaskRatio : f.Store.State.WorkbenchMemoTaskRatio).HasValue,
                        (card ? "card" : "workbench") + " repeated keyboard shrink persists a bounded preference");
                    FullyVisibleInTaskViewport((FrameworkElement)(card ? f.Card.TaskChecks.First().Parent : f.Bench.TaskChecks.First().Parent), editor,
                        (card ? "card" : "workbench") + " repeated keyboard shrink cannot clip the first task row");
                    Check(f.HistoryCount == history, (card ? "card" : "workbench") + " repeated keyboard clamping creates no history snapshot");
                    Inside(editor, view, (card ? "card" : "workbench") + " memo after routed drag/reset");
                    Check(!f.Card.Visible && !f.Card.Attached && !f.Bench.Visible, "routed " + (card ? "card" : "workbench") + " gestures keep both native windows hidden and desktop-unattached");
                }
            }
        }

        [STAThread]
        private static int Main()
        {
            try
            {
                Forms.Application.EnableVisualStyles(); Forms.Application.SetCompatibleTextRenderingDefault(false);
                Run("empty content and reference WPF screenshots", EmptyLayouts);
                Run("independent overflow scrolling", OverflowAndScroll);
                Run("view preference persistence and history isolation", PreferencePersistence);
                Run("natural task content grows and shrinks", NaturalContentShrinksAgain);
                Run("invalid and extreme ratios", ExtremeRatios);
                Run("single editor page ownership", NavigationKeepsOneEditor);
                Run("routed splitter gestures", SplitterRoutedGestures);
                Run("short task viewport regression", ShortTaskViewportRegression);
                Run("divider focus regression", DividerFocusRegression);
                Run("low-ratio independent scrolling regression", LowRatioIndependentScrollRegression);
                Report.AppendLine("Passed: " + passed + "; Failed: " + failed);
                Report.AppendLine("LIMIT: WPF views explicitly measured/arranged and rendered while hidden. Thumb completion uses controlled row-size changes; Mouse/Key events exercise production routes using a test PresentationSource, without native drag capture. No physical pointer/keyboard input, native foreground focus, real desktop attachment, production files, registry, or application processes were used.");
                return failed == 0 ? 0 : 1;
            }
            finally { File.WriteAllText(Path.Combine(Storage.Folder, "verification-memo-layout.txt"), Report.ToString(), new UTF8Encoding(false)); }
        }
    }
}
