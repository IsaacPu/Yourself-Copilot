using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls.Primitives;
using Forms = System.Windows.Forms;

namespace LocalDesktopNote
{
    internal static class WorkboardVerification
    {
        private static void Check(bool value, string description, StringBuilder report)
        {
            if (!value) throw new InvalidOperationException("FAIL: " + description);
            report.AppendLine("PASS: " + description);
        }
        private static void Pump(int milliseconds)
        {
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < milliseconds) { Forms.Application.DoEvents(); Thread.Sleep(12); }
        }
        internal static int Run()
        {
            var report = new StringBuilder();
            string root = Path.Combine(Storage.Folder, ".qa", "workboard-" + Guid.NewGuid().ToString("N"));
            WorkboardWindow card = null;
            try
            {
                Directory.CreateDirectory(root);
                string wallpaper = Desktop.Wallpaper();
                string library = Path.Combine(root, "library.json"); File.WriteAllText(library, "existing image state");
                var store = new WorkboardStore(root);
                Check(store.State.Tasks.Count == 0 && store.State.Ideas == "", "first run starts with an empty writing surface", report);
                Check(store.AddTask(" \t ") == null && store.State.Tasks.Count == 0, "blank task is not added", report);
                store.State.Ideas = "想法：做一个真正好用的桌面助手。\r\n第二行 ✨";
                store.State.TaskDraft = "还没按回车的草稿";
                var task = store.AddTask("  验证中文输入  "); task.Done = true;
                store.State.TaskDraft = "保留输入中的任务";
                store.Save();
                var loaded = new WorkboardStore(root);
                Check(loaded.State.Ideas == store.State.Ideas && loaded.State.TaskDraft == "保留输入中的任务", "Unicode multiline ideas and unfinished task draft survive restart", report);
                Check(loaded.State.Tasks[0].Done && loaded.State.Tasks[0].Text == "验证中文输入", "task content and completion survive restart", report);
                store.Remove(task); store.Save();
                var removed = new WorkboardStore(root);
                Check(removed.State.Tasks.Count == 0 && removed.State.Removed.Count == 1, "removed tasks remain recoverable after restart", report);
                var restored = removed.Undo(); removed.Save();
                Check(restored.Id == task.Id && restored.Done, "undo restores task identity and completion", report);
                card = new WorkboardWindow(removed); card.Attach(); card.ShowCard(false); Pump(150);
                Check(card.Attached && Desktop.IsWindowVisible(card.Handle), "actual work card attaches to the Windows desktop", report);
                Check((Desktop.GetWindowLongPtr(card.Handle, Desktop.GWL_EXSTYLE).ToInt64() & Desktop.WS_EX_TOPMOST) == 0, "idle work card is not always on top", report);
                card.BeginEditing(card.IdeasEditor); Pump(150);
                // GetParent on a top-level tool window returns its hidden owner;
                // WS_CHILD and shell attachment identify actual desktop parenting.
                Check(card.IsEditing && !card.Attached && (Desktop.GetWindowLongPtr(card.Handle, Desktop.GWL_STYLE).ToInt64() & Desktop.WS_CHILD) == 0 && !card.IdeasEditor.IsReadOnly, "inline editor becomes a normal input-capable window", report);
                Check(card.IdeasEditor.IsKeyboardFocusWithin, "inline writing surface receives keyboard focus", report);
                card.IdeasEditor.Text = "窗口里写下的想法\n重启后继续。";
                card.NewTaskEditor.Text = "从窗口添加任务"; card.AddDraft(); Pump(80);
                Check(removed.State.Tasks.Count == 2 && card.NewTaskEditor.Text == "", "add action creates a task and clears the input", report);
                card.TaskChecks[0].IsChecked = true;
                card.TaskChecks[0].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Pump(850);
                loaded = new WorkboardStore(root);
                Check(loaded.State.Ideas == card.IdeasEditor.Text && loaded.State.Tasks[1].Done, "debounced autosave persists actual editor and checkbox events", report);
                card.NewTaskEditor.Text = "程序关闭前的最后几个字"; card.Flush();
                Check(new WorkboardStore(root).State.TaskDraft == "程序关闭前的最后几个字", "flush retains a task draft without requiring Enter", report);
                card.EndEditing(); Pump(100);
                Check(card.Attached && !card.IsEditing && card.IdeasEditor.IsReadOnly, "finishing editing returns the same card to the desktop", report);
                card.Place(new Rectangle(99999, 99999, 450, 340), true); card.Flush();
                Check(Forms.Screen.FromRectangle(card.ScreenRectangle).WorkingArea.Contains(card.ScreenRectangle), "offscreen placement is recovered into the working area", report);
                Rectangle expected = card.ScreenRectangle;
                card.HideCard(); Check(removed.State.Hidden && !card.Visible, "hide preserves card data and visibility state", report);
                card.ShowCard(false); card.Flush(); card.Dispose(); card = null;
                loaded = new WorkboardStore(root); card = new WorkboardWindow(loaded); card.Attach(); card.ShowCard(false);
                Check(card.ScreenRectangle == expected && !loaded.State.Hidden, "recreated window restores position size and visibility", report);
                card.IdeasEditor.Text += "\n保存失败也不清空。";
                Directory.CreateDirectory(loaded.PathName + ".tmp");
                Check(!card.Flush() && card.IdeasEditor.Text.Contains("保存失败也不清空"), "save failure is reported and in-memory content is retained", report);
                Directory.Delete(loaded.PathName + ".tmp");
                Check(card.Flush(), "saving can be retried after a storage failure", report);
                card.Dispose(); card = null;
                loaded.Save();
                string backup = File.ReadAllText(loaded.PathName + ".bak");
                var backupState = LibraryStore.Read<WorkboardState>(loaded.PathName + ".bak");
                File.WriteAllText(loaded.PathName, "broken primary");
                var recovered = new WorkboardStore(root);
                Check(recovered.Notice != null && recovered.State.Ideas == backupState.Ideas && recovered.State.Tasks.Select(t => t.Id).SequenceEqual(backupState.Tasks.Select(t => t.Id)), "corrupt primary recovers the exact last valid backup without inventing newer content", report);
                recovered.Save();
                Check(File.ReadAllText(loaded.PathName + ".bak") == backup, "first recovery save does not overwrite the valid backup with corrupt data", report);
                Check(Directory.GetFiles(root, "workboard-recovery-*.json").Any(p => File.ReadAllText(p) == "broken primary"), "unreadable original is preserved for manual recovery", report);
                Check(File.ReadAllText(library) == "existing image state", "work card storage never modifies the image library", report);
                Check(wallpaper == Desktop.Wallpaper(), "work card lifecycle leaves the wallpaper unchanged", report);
                report.AppendLine("Fixtures: " + root);
                File.WriteAllText(Path.Combine(Storage.Folder, "verification-workboard-v3.txt"), report.ToString(), Encoding.UTF8); return 0;
            }
            catch (Exception error)
            {
                report.AppendLine(error.ToString());
                File.WriteAllText(Path.Combine(Storage.Folder, "verification-workboard-v3.txt"), report.ToString(), Encoding.UTF8); return 1;
            }
            finally { if (card != null) card.Dispose(); }
        }
    }
}
