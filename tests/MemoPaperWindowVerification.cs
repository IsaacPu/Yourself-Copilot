using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace LocalDesktopNote
{
    // Production WinForms/WPF views and routed events, instantiated only in this
    // isolated executable directory. No Show, desktop attachment or registry.
    internal static class MemoPaperWindowVerification
    {
        private static readonly StringBuilder Report = new StringBuilder();
        private static int passed, failed;

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
        private static T Field<T>(object target, string name)
        {
            return (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
        }
        private static void Click(Button button) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }

        private sealed class Fixture : IDisposable
        {
            internal readonly WorkboardStore Store;
            internal readonly WorkboardWindow Card;
            internal readonly WorkbenchWindow Bench;
            internal readonly string Original;
            internal readonly WorkTask Task, Removed;
            internal int Saved;

            internal Fixture(string name)
            {
                Original = "留给下一个念头的文字\r\n把问题记下来，再慢慢整理。";
                string folder = Path.Combine(Storage.Folder, "fixtures", name + "-" + Guid.NewGuid().ToString("N"));
                var seed = new WorkboardStore(folder);
                seed.State.Ideas = Original;
                seed.AddTask("检查纸页交互", new DateTime(2026, 10, 23));
                var removed = seed.AddTask("保留的已移除任务", null); seed.Remove(removed);
                seed.State.TaskDraft = "尚未提交的任务草稿"; seed.State.DraftPlanDate = "2026-10-25";
                seed.Save();
                Store = new WorkboardStore(folder);
                Store.TodayProvider = delegate { return new DateTime(2026, 10, 23); };
                Store.UtcNowProvider = delegate { return new DateTime(2026, 10, 23, 8, 0, 0, DateTimeKind.Utc); };
                Task = Store.State.Tasks.Single(); Removed = Store.State.Removed.Single();
                Card = new WorkboardWindow(Store); Bench = new WorkbenchWindow(Store);
                Store.Saved += delegate { Saved++; };
            }
            internal MemoPaperControls Controls(string surface)
            {
                return surface == "card" ? Field<MemoPaperControls>(Card, "paperControls")
                    : Field<MemoPaperControls>(Bench, surface == "compact" ? "compactPaperControls" : "fullPaperControls");
            }
            internal TextBox Editor(string surface) { return surface == "card" ? Card.IdeasEditor : Bench.IdeasEditor; }
            internal WorkboardState Persisted { get { return LibraryStore.Read<WorkboardState>(Store.PathName); } }
            internal int PaperCount { get { return Store.State.MemoPapers == null ? 0 : Store.State.MemoPapers.Count; } }
            internal void Navigate(string page) { Click(Field<Dictionary<string, Button>>(Bench, "pageButtons")[page]); }
            internal void Use(string surface) { if (surface != "card") Navigate(surface == "full" ? "memo" : "tasks"); }
            internal string Status(string surface)
            {
                return surface == "card" ? ((TextBlock)Card.View.FindName("CardSaveStatus")).Text : Field<TextBlock>(Bench, "status").Text;
            }
            internal void CheckTasks(string label)
            {
                var data = Persisted;
                Check(data.Tasks.Count == 1 && data.Tasks[0].Id == Task.Id && data.Tasks[0].Text == "检查纸页交互" && data.Tasks[0].PlanDate == "2026-10-23" && !data.Tasks[0].Done,
                    label + " preserves task identity, text, date and completion");
                Check(data.Removed.Count == 1 && data.Removed[0].Id == Removed.Id && data.TaskDraft == "尚未提交的任务草稿" && data.DraftPlanDate == "2026-10-25",
                    label + " preserves removed tasks and the pending task draft");
            }
            public void Dispose() { Card.Dispose(); Bench.Dispose(); }
        }

        private static void DisabledWhenBlank(Fixture f, string label)
        {
            foreach (string surface in new[] { "card", "compact", "full" })
            {
                var controls = f.Controls(surface);
                Check(!controls.Grabber.IsEnabled && !controls.ArchiveButton.IsEnabled && !controls.TrashButton.IsEnabled,
                    label + " disables empty-sheet actions on " + surface);
            }
        }

        private static void ClickStore(string surface, string kind)
        {
            using (var f = new Fixture(surface + "-" + kind))
            {
                f.Use(surface); var controls = f.Controls(surface);
                string text = f.Original + "\r\n刚输入、还没触发自动保存的尾句";
                f.Editor(surface).Text = text;
                Check(f.Card.IdeasEditor.Text == text && f.Bench.IdeasEditor.Text == text, surface + " edit synchronizes both actual editors before storage");
                Check(Field<bool>(surface == "card" ? (object)f.Card : f.Bench, "dirty"), surface + " starts with real queued TextChanged content");
                Click(kind == "archive" ? controls.ArchiveButton : controls.TrashButton);
                Check(f.Store.State.Ideas == "" && f.Card.IdeasEditor.Text == "" && f.Bench.IdeasEditor.Text == "", surface + " " + kind + " click replaces both editors with a blank sheet");
                Check(f.Persisted.Ideas == "" && f.Persisted.MemoPapers.Single().Text == text && f.Persisted.MemoPapers.Single().Kind == kind,
                    surface + " " + kind + " commits the latest actual editor text and blank state together");
                Check(!Field<bool>(f.Card, "dirty") && !Field<bool>(f.Bench, "dirty") && !Field<Forms.Timer>(f.Card, "saveTimer").Enabled && !Field<Forms.Timer>(f.Bench, "saveTimer").Enabled,
                    surface + " replacement clears stale dirty flags and stops both save timers");
                Check(controls.UndoButton.Visibility == Visibility.Visible, surface + " exposes undo after a successful paper action");
                DisabledWhenBlank(f, surface);
                int count = f.PaperCount;
                Check(!controls.StorePaper(kind, null) && f.PaperCount == count, surface + " repeated activation on a blank sheet cannot duplicate a stored paper");
                var reopened = new WorkboardStore(Path.GetDirectoryName(f.Store.PathName));
                Check(reopened.State.Ideas == "" && reopened.State.MemoPapers.Single().Text == text, surface + " stored paper survives a fresh store load");
                f.CheckTasks(surface + " " + kind);
            }
        }

        private static void UndoProtectsNewText(string surface)
        {
            using (var f = new Fixture("undo-" + surface))
            {
                f.Use(surface); var controls = f.Controls(surface);
                Click(controls.TrashButton);
                string interim = "上一页放入纸篓后，继续写下的新想法";
                f.Editor(surface).Text = interim;
                Click(controls.UndoButton);
                Check(f.Card.IdeasEditor.Text == f.Original && f.Bench.IdeasEditor.Text == f.Original && f.Persisted.Ideas == f.Original,
                    surface + " undo returns the discarded paper to both actual editors");
                Check(f.PaperCount == 2 && f.Persisted.MemoPapers.Any(p => p.Kind == "archive" && p.Text == interim), surface + " undo first stores intervening unsaved content safely");
                Check(f.Persisted.MemoPapers.Any(p => p.Kind == "trash" && p.Text == f.Original), surface + " undo retains the original recovery paper");
                Check(controls.UndoButton.Visibility == Visibility.Collapsed && !controls.Undo(), surface + " completed undo consumes its one-shot action");
                Check(f.Controls("card").ArchiveButton.IsEnabled && f.Controls("full").TrashButton.IsEnabled, surface + " nonempty restored content re-enables sheet actions");
                f.CheckTasks(surface + " undo");
            }
        }

        private static void FailedStorage(string surface, bool unsaved)
        {
            using (var f = new Fixture("io-failure-" + surface + "-" + unsaved))
            {
                f.Use(surface); var controls = f.Controls(surface);
                if (unsaved) f.Editor(surface).Text += "尚未自动保存的后续内容";
                string current = f.Store.State.Ideas;
                byte[] before = File.ReadAllBytes(f.Store.PathName); int count = f.PaperCount;
                Directory.CreateDirectory(f.Store.PathName + ".tmp");
                try
                {
                    Click(controls.ArchiveButton);
                    Check(f.Store.State.Ideas == current && f.Card.IdeasEditor.Text == current && f.Bench.IdeasEditor.Text == current,
                        surface + " failed " + (unsaved ? "flush" : "atomic storage") + " keeps the exact original text in both editors");
                    Check(count == f.PaperCount && before.SequenceEqual(File.ReadAllBytes(f.Store.PathName)), surface + " failed storage leaves the paper collection and primary bytes unchanged");
                    Check(controls.UndoButton.Visibility == Visibility.Collapsed && controls.ArchiveButton.IsEnabled, surface + " failed storage leaves no false undo and remains retryable");
                    Check(f.Status(surface).Contains("失败") || f.Status(surface).Contains("没有换页"), surface + " failed storage exposes a visible retry message");
                }
                finally { Directory.Delete(f.Store.PathName + ".tmp"); }
                Click(controls.ArchiveButton);
                Check(f.Persisted.Ideas == "" && f.Persisted.MemoPapers.Single().Text == current, surface + " successful retry preserves the same content exactly once");
            }
        }

        private static DragEventArgs DragArgs(UIElement target, IDataObject data, RoutedEvent routedEvent)
        {
            var constructor = typeof(DragEventArgs).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
                .Single(c => c.GetParameters().Length == 5 && c.GetParameters()[0].ParameterType == typeof(IDataObject));
            var args = (DragEventArgs)constructor.Invoke(new object[] { data, DragDropKeyStates.LeftMouseButton, DragDropEffects.Move, target, new Point(4, 4) });
            args.RoutedEvent = routedEvent; args.Source = target; return args;
        }
        private static DragEventArgs RouteDrag(MemoPaperControls controls, IDataObject data, RoutedEvent routedEvent)
        {
            DragEventArgs args = DragArgs(controls.Bin, data, routedEvent); controls.Bin.RaiseEvent(args); return args;
        }

        private static int BoundHandlers(UIElement element, RoutedEvent routedEvent, object target, string methodName)
        {
            object events = typeof(UIElement).GetProperty("EventHandlersStore", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(element, null);
            if (events == null) return 0;
            var handlers = (Array)events.GetType().GetMethod("GetRoutedEventHandlers", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Invoke(events, new object[] { routedEvent });
            if (handlers == null) return 0;
            int count = 0;
            foreach (object item in handlers)
            {
                var handler = (Delegate)item.GetType().GetProperty("Handler", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(item, null);
                if (Object.ReferenceEquals(handler.Target, target) && handler.Method.Name == methodName) count++;
            }
            return count;
        }

        private static void DragBoundaries(string surface)
        {
            using (var f = new Fixture("drag-" + surface))
            {
                f.Use(surface); var controls = f.Controls(surface);
                Check(controls.Bin.AllowDrop && controls.Grabber.Cursor == Cursors.Hand, surface + " exposes a hand grip and a dedicated bin drop target");
                Check(BoundHandlers(controls.Grabber, UIElement.PreviewMouseLeftButtonDownEvent, controls, "BeginDrag") == 1
                    && BoundHandlers(controls.Grabber, UIElement.PreviewMouseMoveEvent, controls, "MoveDrag") == 1,
                    surface + " binds whole-paper drag handlers to the dedicated grip");
                Check(BoundHandlers(f.Editor(surface), UIElement.PreviewMouseLeftButtonDownEvent, controls, "BeginDrag") == 0
                    && BoundHandlers(f.Editor(surface), UIElement.PreviewMouseMoveEvent, controls, "MoveDrag") == 0,
                    surface + " leaves editor text selection free of whole-paper drag handlers");
                int initialSaved = f.Saved; byte[] before = File.ReadAllBytes(f.Store.PathName);
                var external = new DataObject(DataFormats.UnicodeText, "外部拖入的文字");
                DragEventArgs externalOver = RouteDrag(controls, external, DragDrop.DragOverEvent);
                DragEventArgs externalDrop = RouteDrag(controls, external, DragDrop.DropEvent);
                Check(externalOver.Effects == DragDropEffects.None && externalDrop.Effects == DragDropEffects.None, surface + " rejects ordinary external data on both hover and drop");
                var foreignStore = new WorkboardStore(Path.Combine(Storage.Folder, "foreign", surface));
                var foreign = new DataObject(typeof(MemoPaperControls.PaperDrag), new MemoPaperControls.PaperDrag(foreignStore, f.Original));
                Check(RouteDrag(controls, foreign, DragDrop.DropEvent).Effects == DragDropEffects.None, surface + " rejects a paper belonging to a different store");
                var valid = new DataObject(typeof(MemoPaperControls.PaperDrag), new MemoPaperControls.PaperDrag(f.Store, f.Original));
                DragEventArgs hover = RouteDrag(controls, valid, DragDrop.DragOverEvent);
                Check(hover.Effects == DragDropEffects.Move && f.PaperCount == 0 && f.Store.State.Ideas == f.Original, surface + " valid drag-over only previews a possible action");
                RouteDrag(controls, valid, DragDrop.DragLeaveEvent);
                Check(controls.Bin.ToolTip == null && f.Saved == initialSaved && before.SequenceEqual(File.ReadAllBytes(f.Store.PathName)), surface + " cancelling or leaving a drag never saves, clears or stores a paper");
                f.Editor(surface).Text += "拖动期间又写了内容"; string updated = f.Store.State.Ideas;
                DragEventArgs stale = RouteDrag(controls, valid, DragDrop.DropEvent);
                Check(stale.Effects == DragDropEffects.None && f.PaperCount == 0 && f.Store.State.Ideas == updated, surface + " rejects a stale paper drag after a real editor change");
                var latest = new DataObject(typeof(MemoPaperControls.PaperDrag), new MemoPaperControls.PaperDrag(f.Store, updated));
                DragEventArgs dropped = RouteDrag(controls, latest, DragDrop.DropEvent);
                Check(dropped.Handled && dropped.Effects == DragDropEffects.Move && f.Persisted.Ideas == "", surface + " routed valid drop commits a blank sheet");
                Check(f.Persisted.MemoPapers.Single().Kind == "trash" && f.Persisted.MemoPapers.Single().Text == updated, surface + " routed drop stores exactly the accepted whole paper in the bin");
                Check(f.Card.IdeasEditor.Text == "" && f.Bench.IdeasEditor.Text == "", surface + " routed drop replaces both real editor controls");
                f.CheckTasks(surface + " drop");
            }
        }

        private static void SameTextReplacementInvalidatesDrag(string surface)
        {
            using (var f = new Fixture("same-text-drag-" + surface))
            {
                f.Use(surface); var controls = f.Controls(surface);
                var stale = new DataObject(typeof(MemoPaperControls.PaperDrag), new MemoPaperControls.PaperDrag(f.Store, f.Original));
                Click(controls.ArchiveButton); Click(controls.UndoButton);
                Check(f.Persisted.Ideas == f.Original && f.Card.IdeasEditor.Text == f.Original && f.Bench.IdeasEditor.Text == f.Original,
                    surface + " archive and real undo return identical text on a new active sheet");
                byte[] before = File.ReadAllBytes(f.Store.PathName); int papers = f.PaperCount;
                DragEventArgs hover = RouteDrag(controls, stale, DragDrop.DragOverEvent);
                DragEventArgs drop = RouteDrag(controls, stale, DragDrop.DropEvent);
                Check(hover.Effects == DragDropEffects.None && drop.Effects == DragDropEffects.None && before.SequenceEqual(File.ReadAllBytes(f.Store.PathName)) && f.PaperCount == papers,
                    surface + " rejects a drag from before the same-text replacement without saving or discarding the newer sheet");
                var current = new DataObject(typeof(MemoPaperControls.PaperDrag), new MemoPaperControls.PaperDrag(f.Store, f.Original));
                Check(RouteDrag(controls, current, DragDrop.DropEvent).Effects == DragDropEffects.Move && f.Persisted.Ideas == "" && f.PaperCount == papers + 1,
                    surface + " a newly grabbed replacement sheet remains valid for normal drop");
                f.CheckTasks(surface + " same-text drag rejection");
            }
        }

        private static void Layout(Grid view, double width, double height)
        {
            view.Width = width; view.Height = height;
            for (int index = 0; index < 3; index++)
            {
                view.Measure(new Size(width, height)); view.Arrange(new Rect(0, 0, width, height)); view.UpdateLayout();
                view.Dispatcher.Invoke(DispatcherPriority.Background, new Action(delegate { }));
            }
        }
        private static void Snapshot(Grid view, string name)
        {
            var bitmap = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(view);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var file = File.Create(Path.Combine(Storage.Folder, name))) encoder.Save(file);
        }
        private static void HistoryRestoreAndSnapshots()
        {
            using (var f = new Fixture("history-restore"))
            {
                f.Navigate("memo"); Click(f.Controls("full").ArchiveButton);
                Layout(f.Bench.View, 1026, 800); Snapshot(f.Bench.View, "memo-paper-blank.png");
                f.Bench.IdeasEditor.Text = "准备取回旧纸前，当前新写的一段文字";
                f.Navigate("history"); var panel = Field<HistoryPanel>(f.Bench, "historyPanel");
                panel.SelectCategory("memo", "archive");
                Check(panel.Entries.Count == 1 && panel.Preview.Text == f.Original && panel.RestoreButton.IsEnabled, "real history navigation previews the stored memo and enables recovery");
                Layout(f.Bench.View, 1026, 800); Snapshot(f.Bench.View, "memo-paper-history.png");
                Click(panel.RestoreButton);
                Check(f.Card.IdeasEditor.Text == f.Original && f.Bench.IdeasEditor.Text == f.Original, "history restore button returns the selected paper to both editor controls");
                Check(f.Persisted.MemoPapers.Any(p => p.Kind == "archive" && p.Text == "准备取回旧纸前，当前新写的一段文字"), "history restore protects the current page before replacing it");
                Check(Field<string>(f.Bench, "page") == "memo", "successful history recovery takes the user directly to the full memo editor");
                f.Navigate("history"); panel.SelectCategory("task", "versions");
                Check(panel.RestoreButton.Visibility == Visibility.Collapsed && panel.Entries.All(e => !e.IsMemo) && !panel.Preview.Text.Contains(f.Original), "task history exposes task-only records without a memo recovery action");
                f.CheckTasks("history recovery");
                Check(File.Exists(Path.Combine(Storage.Folder, "memo-paper-blank.png")) && File.Exists(Path.Combine(Storage.Folder, "memo-paper-history.png")), "neutral production WPF blank-page and history snapshots were rendered");
            }
        }

        private static void ReferenceVisual()
        {
            using (var f = new Fixture("selected-reference-visual"))
            {
                f.Bench.IdeasEditor.Text = "做一个更安静的专注计时器，让开始变得更简单。\r\n为阅读场景设计一个轻量的摘录工具。\r\n整理一下最近的旅行照片，写一篇短游记。\r\n尝试用传感器数据记录睡眠，看看规律。";
                f.Navigate("memo");
                foreach (Size size in new[] { new Size(1026, 800), new Size(800, 600) })
                {
                    Layout(f.Bench.View, size.Width, size.Height);
                    var controls = f.Controls("full");
                    foreach (FrameworkElement target in new FrameworkElement[] { controls.Grabber, controls.ArchiveButton, controls.Bin, f.Bench.IdeasEditor })
                    {
                        Rect bounds = target.TransformToAncestor(f.Bench.View).TransformBounds(new Rect(target.RenderSize));
                        Check(bounds.Left >= -1 && bounds.Top >= -1 && bounds.Right <= size.Width + 1 && bounds.Bottom <= size.Height + 1 && bounds.Width > 0 && bounds.Height > 0,
                            "paper visual " + size + " keeps " + target.GetType().Name + " visible and inside the view: " + bounds);
                    }
                    Check(f.Bench.IdeasEditor.ActualHeight >= 65, "paper visual " + size + " retains a readable editing viewport");
                    if (size.Width == 1026) Snapshot(f.Bench.View, "memo-paper-reference.png");
                    else Snapshot(f.Bench.View, "memo-paper-minimum.png");
                }
            }
        }

        [STAThread]
        private static int Main()
        {
            try
            {
                Forms.Application.EnableVisualStyles(); Forms.Application.SetCompatibleTextRenderingDefault(false);
                foreach (string surface in new[] { "card", "compact", "full" })
                {
                    string current = surface;
                    Run(current + " archive click", delegate { ClickStore(current, "archive"); });
                    Run(current + " trash click", delegate { ClickStore(current, "trash"); });
                    Run(current + " undo protection", delegate { UndoProtectsNewText(current); });
                    Run(current + " persisted storage failure", delegate { FailedStorage(current, false); });
                    Run(current + " unsaved flush failure", delegate { FailedStorage(current, true); });
                    Run(current + " routed drag boundaries", delegate { DragBoundaries(current); });
                    Run(current + " same-text sheet drag boundaries", delegate { SameTextReplacementInvalidatesDrag(current); });
                }
                Run("history recovery and native snapshots", HistoryRestoreAndSnapshots);
                Run("selected concept and responsive paper", ReferenceVisual);
                Report.AppendLine("Passed: " + passed + "; Failed: " + failed);
                Report.AppendLine("LIMIT: isolated production WinForms/WPF constructors, TextChanged, routed Button.Click and DragOver/DragLeave/Drop events, primary IO failures, fresh store loading, and hidden native WPF rendering. DragEventArgs are constructed through the .NET Framework internal constructor. Handler inspection verifies only the grip owns whole-paper drag source handlers. No Show/Attach, physical pointer drag, native keyboard focus, production data, registry or production process changes.");
                return failed == 0 ? 0 : 1;
            }
            finally { File.WriteAllText(Path.Combine(Storage.Folder, "verification-memo-paper-window.txt"), Report.ToString(), new UTF8Encoding(false)); }
        }
    }
}
