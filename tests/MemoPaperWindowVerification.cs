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
                Check(f.Store.CanUndoMemoPaper && !f.Store.CanRedoMemoPaper, surface + " exposes a shared keyboard undo after a successful paper action");
                Check(typeof(MemoPaperControls).GetField("UndoButton", BindingFlags.Instance | BindingFlags.NonPublic) == null, surface + " has no explicit paper undo button");
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
                Check(controls.Undo(), surface + " shared paper undo succeeds");
                Check(f.Card.IdeasEditor.Text == f.Original && f.Bench.IdeasEditor.Text == f.Original && f.Persisted.Ideas == f.Original,
                    surface + " undo returns the discarded paper to both actual editors");
                Check(f.PaperCount == 2 && f.Persisted.MemoPapers.Any(p => p.Kind == "archive" && p.Text == interim), surface + " undo first stores intervening unsaved content safely");
                Check(f.Persisted.MemoPapers.Any(p => p.Kind == "trash" && p.Text == f.Original), surface + " undo retains the original recovery paper");
                Check(!f.Store.CanUndoMemoPaper && f.Store.CanRedoMemoPaper && !controls.Undo(), surface + " completed undo advances the shared stack and enables redo");
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
                    Check(!f.Store.CanUndoMemoPaper && !f.Store.CanRedoMemoPaper && controls.ArchiveButton.IsEnabled, surface + " failed storage leaves no false undo or redo and remains retryable");
                    Check(f.Status(surface).Contains("失败") || f.Status(surface).Contains("没有换页"), surface + " failed storage exposes a visible retry message");
                }
                finally { Directory.Delete(f.Store.PathName + ".tmp"); }
                Click(controls.ArchiveButton);
                Check(f.Persisted.Ideas == "" && f.Persisted.MemoPapers.Single().Text == current, surface + " successful retry preserves the same content exactly once");
            }
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
                Grid root = surface == "card" ? f.Card.View : f.Bench.View;
                Layout(root, surface == "card" ? 410 : 1026, surface == "card" ? 600 : 800);
                Point grip = Center(controls.Grabber, root), bin = Center(controls.Bin, root);
                double restingOpacity = controls.PaperSurface.Opacity;
                Check(controls.Grabber.Cursor == Cursors.Hand && !controls.IsPaperLifted, surface + " exposes a hand grip without entering a native forbidden-drop mode");
                Check(BoundHandlers(controls.Grabber, UIElement.PreviewMouseLeftButtonDownEvent, controls, "BeginDrag") == 1
                    && BoundHandlers(controls.Grabber, UIElement.PreviewMouseMoveEvent, controls, "MoveDrag") == 1,
                    surface + " binds whole-paper drag handlers to the dedicated grip");
                Check(BoundHandlers(f.Editor(surface), UIElement.PreviewMouseLeftButtonDownEvent, controls, "BeginDrag") == 0
                    && BoundHandlers(f.Editor(surface), UIElement.PreviewMouseMoveEvent, controls, "MoveDrag") == 0,
                    surface + " leaves editor text selection free of whole-paper drag handlers");
                Check(!controls.FinishPaperLift(bin) && f.PaperCount == 0, surface + " a release without an internal paper lift cannot accept external content");
                Check(controls.StartPaperLift(grip), surface + " the actual lift controller starts from the laid-out native paper");
                root.UpdateLayout();
                Check(controls.IsPaperLifted && controls.DragPreview != null && !controls.DragPreview.IsHitTestVisible,
                    surface + " shows a non-hit-testable paper visual while the paper is held");
                Check(controls.PaperSurface.Opacity < restingOpacity && controls.DragPreview.ActualWidth > 30 && controls.DragPreview.ActualHeight > 30,
                    surface + " visibly lifts a substantial real paper instead of a cursor-only drag");
                Point first = controls.DragPreview.TranslatePoint(new Point(), root);
                controls.MovePaperLift(new Point(grip.X + 52, grip.Y + 43)); root.UpdateLayout();
                Point second = controls.DragPreview.TranslatePoint(new Point(), root);
                Check(Math.Abs((second.X - first.X) - 52) < 2 && Math.Abs((second.Y - first.Y) - 43) < 2,
                    surface + " held paper follows the pointer displacement in both axes");
                Snapshot(root, "memo-paper-lift-" + surface + ".png");
                byte[] before = File.ReadAllBytes(f.Store.PathName);
                controls.MovePaperLift(bin);
                Check(f.PaperCount == 0 && f.Store.State.Ideas == f.Original && before.SequenceEqual(File.ReadAllBytes(f.Store.PathName)), surface + " hovering above the bin never commits or clears a held paper");
                controls.CancelPaperLift(); root.UpdateLayout();
                Check(!controls.IsPaperLifted && controls.DragPreview == null && controls.PaperSurface.Opacity == restingOpacity && controls.Bin.ToolTip == null,
                    surface + " cancel restores the stationary paper and removes the held visual and bin highlight");
                Check(before.SequenceEqual(File.ReadAllBytes(f.Store.PathName)) && f.PaperCount == 0 && f.Store.State.Ideas == f.Original,
                    surface + " cancelled lift preserves exact disk bytes and original content");
                Check(controls.StartPaperLift(grip) && !controls.FinishPaperLift(new Point(-20, -20)) && !controls.IsPaperLifted,
                    surface + " releasing outside the application restores the original sheet without storing it");
                Check(before.SequenceEqual(File.ReadAllBytes(f.Store.PathName)) && f.PaperCount == 0, surface + " outside release creates no hidden history paper");
                Check(controls.StartPaperLift(grip), surface + " a fresh lift starts before a concurrent editor change");
                f.Editor(surface).Text += "拖动期间又写了内容"; string updated = f.Store.State.Ideas;
                Check(!controls.FinishPaperLift(bin) && f.PaperCount == 0 && f.Store.State.Ideas == updated && !controls.IsPaperLifted,
                    surface + " rejects a held paper made stale by a real editor change");
                bool restarted = controls.StartPaperLift(grip);
                Check(restarted && controls.FinishPaperLift(bin) && !controls.IsPaperLifted && f.Persisted.Ideas == "", surface + " releasing the current held paper inside the bin commits a blank sheet");
                Check(f.Persisted.MemoPapers.Single().Kind == "trash" && f.Persisted.MemoPapers.Single().Text == updated, surface + " valid release stores exactly the accepted whole paper in the bin");
                Check(f.Card.IdeasEditor.Text == "" && f.Bench.IdeasEditor.Text == "", surface + " valid release replaces both real editor controls");
                Check(!controls.FinishPaperLift(bin) && f.PaperCount == 1 && !controls.StartPaperLift(grip), surface + " a duplicate release or lift of the blank replacement cannot store another paper");
                f.CheckTasks(surface + " drop");
            }
        }

        private static void SameTextReplacementInvalidatesDrag(string surface)
        {
            using (var f = new Fixture("same-text-drag-" + surface))
            {
                f.Use(surface); var controls = f.Controls(surface);
                Grid root = surface == "card" ? f.Card.View : f.Bench.View;
                Layout(root, surface == "card" ? 410 : 1026, surface == "card" ? 600 : 800);
                Point grip = Center(controls.Grabber, root), bin = Center(controls.Bin, root);
                Check(controls.StartPaperLift(grip), surface + " starts a real held-sheet session before same-text replacement");
                Click(controls.ArchiveButton); controls.Undo();
                Check(f.Persisted.Ideas == f.Original && f.Card.IdeasEditor.Text == f.Original && f.Bench.IdeasEditor.Text == f.Original,
                    surface + " archive and real undo return identical text on a new active sheet");
                byte[] before = File.ReadAllBytes(f.Store.PathName); int papers = f.PaperCount;
                Check(!controls.FinishPaperLift(bin) && !controls.IsPaperLifted && before.SequenceEqual(File.ReadAllBytes(f.Store.PathName)) && f.PaperCount == papers,
                    surface + " rejects a held session from before same-text replacement without discarding the newer sheet");
                Check(controls.StartPaperLift(grip) && controls.FinishPaperLift(bin) && f.Persisted.Ideas == "" && f.PaperCount == papers + 1,
                    surface + " a newly grabbed replacement sheet remains valid for normal drop");
                f.CheckTasks(surface + " same-text drag rejection");
            }
        }

        private static Point Center(FrameworkElement element, Grid root)
        {
            return element.TranslatePoint(new Point(element.ActualWidth / 2, element.ActualHeight / 2), root);
        }

        private static void KeyboardHistory(string surface)
        {
            using (var f = new Fixture("keyboard-" + surface))
            {
                f.Use(surface); var controls = f.Controls(surface); TextBox editor = f.Editor(surface);
                Layout(surface == "card" ? f.Card.View : f.Bench.View, surface == "card" ? 410 : 1026, surface == "card" ? 600 : 800);
                var peer = f.Controls(surface == "card" ? "full" : "card");
                TextBox peerEditor = surface == "card" ? f.Bench.IdeasEditor : f.Card.IdeasEditor;
                Click(controls.TrashButton);
                Check(!editor.CanUndo && !editor.CanRedo && !peerEditor.CanUndo && !peerEditor.CanRedo,
                    surface + " real replacement clears both editors local undo so old papers cannot bypass the shared stack");
                Check(peer.HandleHistoryKey(Key.Z, ModifierKeys.Control, peerEditor, true) && f.Persisted.Ideas == f.Original && f.Store.CanRedoMemoPaper,
                    surface + " Ctrl+Z in the peer window undoes the same shared paper operation");
                Check(f.Card.IdeasEditor.Text == f.Original && f.Bench.IdeasEditor.Text == f.Original && !editor.CanUndo && !peerEditor.CanUndo,
                    surface + " paper undo synchronizes both editors without creating a local replacement undo unit");
                string tail = "正文局部编辑优先于纸页切换";
                editor.Select(editor.Text.Length, 0); editor.BeginChange(); editor.SelectedText = tail; editor.EndChange();
                Check(editor.CanUndo && f.Store.State.Ideas == f.Original + tail, surface + " a genuine TextBox edit creates local undo on the current paper");
                Check(peerEditor.Text == editor.Text && !peerEditor.CanUndo && !peerEditor.CanRedo,
                    surface + " the peer editor mirrors current text without retaining undo units for obsolete projections");
                Check(controls.HandleHistoryKey(Key.Z, ModifierKeys.Control, editor, true) && editor.Text == f.Original && f.Store.CanRedoMemoPaper,
                    surface + " Ctrl+Z consumes local text undo before traveling the paper stack");
                Check(editor.CanRedo && controls.HandleHistoryKey(Key.Y, ModifierKeys.Control, editor, true) && editor.Text == f.Original + tail && f.Store.CanRedoMemoPaper,
                    surface + " Ctrl+Y consumes local text redo before traveling the paper stack");
                Check(controls.HandleHistoryKey(Key.Y, ModifierKeys.Control, editor, true) && f.Persisted.Ideas == "" && !f.Store.CanRedoMemoPaper,
                    surface + " Ctrl+Y redoes the paper action when no local text redo remains");
                Check(f.Persisted.MemoPapers.Any(p => p.Kind == "archive" && p.Text == f.Original + tail),
                    surface + " paper redo protects text written since the matching paper undo");
                Check(controls.HandleHistoryKey(Key.Z, ModifierKeys.Control, editor, true) && f.Persisted.Ideas == f.Original + tail,
                    surface + " the next paper undo restores the actual text present before redo");
                byte[] before = File.ReadAllBytes(f.Store.PathName); string current = editor.Text;
                Check(!controls.HandleHistoryKey(Key.Z, ModifierKeys.Control, editor, false) && editor.Text == current && before.SequenceEqual(File.ReadAllBytes(f.Store.PathName)),
                    surface + " task and unrelated editor scopes retain their own Ctrl+Z behavior");
                Check(!controls.HandleHistoryKey(Key.ImeProcessed, ModifierKeys.Control, editor, true)
                    && !controls.HandleHistoryKey(Key.Z, ModifierKeys.Control | ModifierKeys.Alt, editor, true),
                    surface + " IME processed keys and unrelated modifier chords never trigger a paper switch");
                f.CheckTasks(surface + " keyboard history");
            }
        }

        private static void LiftCancellationRoutes(string surface)
        {
            using (var f = new Fixture("lift-cancel-" + surface))
            {
                f.Use(surface); var controls = f.Controls(surface);
                Grid root = surface == "card" ? f.Card.View : f.Bench.View;
                double width = surface == "card" ? 410 : 1026, height = surface == "card" ? 600 : 800;
                Layout(root, width, height);
                Check(controls.StartPaperLift(Center(controls.Grabber, root)), surface + " begins a held visual before simulated capture loss");
                byte[] before = File.ReadAllBytes(f.Store.PathName);
                controls.Grabber.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.LostMouseCaptureEvent });
                Check(!controls.IsPaperLifted && controls.DragPreview == null && before.SequenceEqual(File.ReadAllBytes(f.Store.PathName)) && f.PaperCount == 0,
                    surface + " the actual LostMouseCapture route cancels the held visual without persisting a discarded paper");
                Check(controls.StartPaperLift(Center(controls.Grabber, root)), surface + " begins a held visual before resizing its host");
                Layout(root, width + 35, height + 20);
                Check(!controls.IsPaperLifted && controls.DragPreview == null && before.SequenceEqual(File.ReadAllBytes(f.Store.PathName)),
                    surface + " a real host size change cancels the lift so stale bin coordinates cannot commit");
                Point release = Center(controls.Bin, root);
                Check(controls.StartPaperLift(Center(controls.Grabber, root)), surface + " begins a held visual before hiding its grip");
                controls.Grabber.Visibility = Visibility.Collapsed; root.UpdateLayout();
                Check(!controls.FinishPaperLift(release) && !controls.IsPaperLifted && controls.DragPreview == null && before.SequenceEqual(File.ReadAllBytes(f.Store.PathName)),
                    surface + " releasing after the source grip is collapsed rejects the drop and restores its original text");
                controls.Grabber.Visibility = Visibility.Visible; root.UpdateLayout();
                Check(controls.StartPaperLift(Center(controls.Grabber, root)), surface + " begins a held visual before leaving its paper page");
                if (surface == "card") root.Visibility = Visibility.Collapsed; else f.Navigate("history");
                Check(!controls.FinishPaperLift(release) && !controls.IsPaperLifted && f.Store.State.Ideas == f.Original && f.PaperCount == 0,
                    surface + " a sheet from a hidden or navigated-away page cannot be committed with its old bin coordinates");
                Check(!controls.StartPaperLift(release), surface + " the hidden page cannot start a fresh paper lift");
                if (surface == "card") root.Visibility = Visibility.Visible; else f.Use(surface);
                controls.CancelPaperLift(); controls.CancelPaperLift();
                Check(!controls.IsPaperLifted && f.Store.State.Ideas == f.Original && f.PaperCount == 0,
                    surface + " repeated cleanup is idempotent and leaves the original current paper intact");
                f.CheckTasks(surface + " cancellation routes");
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
                    Run(current + " local and shared keyboard history", delegate { KeyboardHistory(current); });
                    Run(current + " lost capture resize and hidden grip", delegate { LiftCancellationRoutes(current); });
                }
                Run("history recovery and native snapshots", HistoryRestoreAndSnapshots);
                Run("selected concept and responsive paper", ReferenceVisual);
                Report.AppendLine("Passed: " + passed + "; Failed: " + failed);
                Report.AppendLine("LIMIT: isolated production WinForms/WPF constructors, TextChanged, routed Button.Click and LostMouseCapture, shared pointer-controller methods used by actual handlers, local TextBox Undo/Redo and shared keyboard controller, real host resizing, primary IO failures, fresh store loading, and hidden native WPF rendering. Handler inspection verifies only the grip owns whole-paper drag source handlers. No Show/Attach, physical pointer capture or native keyboard-focus automation, production data, registry or production process changes.");
                return failed == 0 ? 0 : 1;
            }
            finally { File.WriteAllText(Path.Combine(Storage.Folder, "verification-memo-paper-window.txt"), Report.ToString(), new UTF8Encoding(false)); }
        }
    }
}
