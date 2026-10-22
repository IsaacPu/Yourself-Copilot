using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls.Primitives;
using Forms = System.Windows.Forms;

namespace LocalDesktopNote
{
    internal static class DailyIntegrationVerification
    {
        private static readonly StringBuilder Report = new StringBuilder();
        private static void Check(bool ok, string description)
        {
            if (!ok) throw new InvalidOperationException("FAIL: " + description);
            Report.AppendLine("PASS: " + description);
        }
        private static void Click(ButtonBase button) { button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Forms.Application.DoEvents(); }
        private static T Field<T>(object target, string name) { return (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target); }
        [STAThread]
        private static int Main()
        {
            WorkboardWindow card = null; WorkbenchWindow bench = null; NoteContext context = null;
            try
            {
                try { Desktop.SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch (EntryPointNotFoundException) { }
                Forms.Application.EnableVisualStyles(); Forms.Application.SetCompatibleTextRenderingDefault(false);
                DateTime today = DateTime.Today;
                DateTime initialDay = today;
                string root = Path.Combine(Storage.Folder, "daily-integration-fixture");
                var store = new WorkboardStore(root); store.TodayProvider = delegate { return today; };
                var earlier = store.AddTask("之前未完成的计划", today.AddDays(-1));
                var current = store.AddTask("今天的计划", today);
                var next = store.AddTask("明天的计划", today.AddDays(1));
                var unplanned = store.AddTask("旧任务尚未安排", null);
                store.State.Ideas = "备忘不会随日期消失"; store.Save();
                card = new WorkboardWindow(store); card.Attach(); card.ShowCard(false);
                bench = new WorkbenchWindow(store); bench.Show();
                Forms.Application.DoEvents();
                Check(card.TaskEditors.Select(t => t.Text).SequenceEqual(new[] { earlier.Text, current.Text }), "desktop today includes earlier unfinished and current tasks only");
                Check(!card.IsEditing && card.Attached, "desktop display stays in non-activating desktop mode");
                Check(bench.TaskEditors.Select(t => t.Text).SequenceEqual(card.TaskEditors.Select(t => t.Text)), "desktop and workbench agree on visible task grouping");
                card.SetTaskCompleted(earlier, true); card.Flush();
                Check(card.TaskEditors.Count == 1 && bench.TaskChecks.Count == 1 && bench.CompletedChecks.Count == 1, "completion synchronizes both windows after safe save");
                Check(earlier.CompletedAt != null && earlier.PlanDate == WorkboardStore.FormatPlanDate(initialDay.AddDays(-1)), "completion records time without changing planned date");
                bench.CompletedChecks[0].IsChecked = false; Click(bench.CompletedChecks[0]);
                Check(!earlier.Done && card.TaskEditors[0].Text == earlier.Text && earlier.PlanDate == WorkboardStore.FormatPlanDate(initialDay.AddDays(-1)), "workbench restoration returns earlier task to the correct desktop group");
                card.SelectScope("tomorrow"); card.BeginEditing(card.NewTaskEditor); Forms.Application.DoEvents(); card.NewTaskEditor.Text = "卡片里的明日新增"; card.AddDraft(); card.Flush();
                Check(store.State.Tasks.Last().PlanDate == WorkboardStore.FormatPlanDate(initialDay.AddDays(1)) && bench.CurrentScope == "tomorrow" && bench.TaskChecks.Count == 2, "adding from tomorrow card uses tomorrow and updates workbench");
                card.NewTaskEditor.Text = "还没有提交的明日草稿"; card.Flush();
                Check(bench.NewTaskEditor.Text == card.NewTaskEditor.Text && bench.DraftDatePicker.SelectedDate == today.AddDays(1), "shared unfinished draft retains its explicit planned date");
                bench.IdeasEditor.Text += "\n工作台里追加的内容"; bench.Flush();
                Check(card.IdeasEditor.Text == bench.IdeasEditor.Text && card.NewTaskEditor.Text == "还没有提交的明日草稿", "memo editing synchronizes without dropping task draft");
                card.SelectScope("today");
                today = today.AddDays(1); card.RefreshDay(); bench.RefreshDay();
                Check(card.TaskEditors.Any(t => t.Tag == next) && next.PlanDate == WorkboardStore.FormatPlanDate(initialDay.AddDays(1)), "tomorrow naturally becomes today without rewriting the task date");
                Check(card.TaskEditors.Any(t => t.Tag == current) && current.PlanDate == WorkboardStore.FormatPlanDate(initialDay), "yesterday unfinished remains visible with its original plan date");
                Check(card.NewTaskEditor.Text == "还没有提交的明日草稿" && card.IdeasEditor.Text.Contains("追加"), "midnight refresh preserves memo and unfinished input");
                card.NewTaskEditor.Clear(); card.SelectScope("tomorrow"); card.NewTaskEditor.Text = "连续明日安排 A"; card.AddDraft();
                today = today.AddDays(1); card.RefreshDay(); bench.RefreshDay();
                card.NewTaskEditor.Text = "连续明日安排 B"; card.AddDraft(); card.Flush();
                Check(store.State.Tasks.Last().PlanDate == WorkboardStore.FormatPlanDate(today.AddDays(1)), "blank desktop draft follows the new tomorrow after midnight");
                card.SelectScope("today"); card.BeginEditing(card.TaskEditors[0]); Forms.Application.DoEvents();
                var editingTitle = card.TaskEditors[0]; editingTitle.Text += "编辑中";
                today = today.AddDays(1); card.RefreshDay();
                Check(card.TaskEditors.Contains(editingTitle) && editingTitle.IsKeyboardFocusWithin, "midnight does not recreate a desktop task editor with keyboard focus");
                card.EndEditing(); Forms.Application.DoEvents();
                card.SelectScope("later"); card.Flush();
                Check(card.TaskEditors.Any(t => t.Tag == unplanned), "legacy unscheduled tasks remain discoverable in later view");
                card.RemoveTask(next); card.UndoRemoveTask(); card.Flush();
                Check(card.TaskEditors.Any(t => t.Tag == next) && next.PlanDate == WorkboardStore.FormatPlanDate(initialDay.AddDays(1)), "undo reveals the restored task in its actual date group");
                card.HideCard();
                Check(!card.Visible && store.State.Tasks.Contains(next), "hiding affects display rather than content");
                card.ShowCard(false); card.Flush();
                Check(card.Visible && !card.IsEditing, "showing the desktop card does not enter focus-stealing edit mode");
                bench.Show(); bench.IdeasEditor.Text += "\n关闭前最后几个字"; bench.Close();
                Check(!bench.IsDisposed && !bench.Visible && new WorkboardStore(root).State.Ideas.Contains("最后几个字"), "closing workbench saves then hides without deleting or exiting");
                Check(Directory.GetFiles(store.HistoryFolder, "*.json", SearchOption.AllDirectories).Any(p => store.ReadHistorySnapshot(p).Ideas == "备忘不会随日期消失"), "earlier memo remains readable in historical snapshots");
                card.IdeasEditor.Text += "\n失败后仍保留"; Directory.CreateDirectory(store.PathName + ".tmp");
                Check(!card.Flush() && card.IdeasEditor.Text.Contains("失败后仍保留"), "failed desktop save preserves current input and reports failure");
                Directory.Delete(store.PathName + ".tmp");
                Check(card.Flush() && bench.IdeasEditor.Text.Contains("失败后仍保留"), "retry safely saves then synchronizes the workbench");
                card.Dispose(); card = null; bench.AllowClose = true; bench.Dispose(); bench = null;

                var bootStore = new WorkboardStore(Storage.Folder); bootStore.State.ShowCardOnStartup = false; bootStore.State.TaskScope = "tomorrow"; bootStore.Save();
                using (var signal = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.AutoReset))
                {
                    context = new NoteContext(signal, true); Forms.Application.DoEvents();
                    var bootCard = Field<WorkboardWindow>(context, "workboard");
                    Check(bootCard != null && !bootCard.Visible && !bootCard.IsEditing, "startup respects disabled card display without opening workbench");
                    Check(Field<WorkbenchWindow>(context, "workbench") == null && bootCard.Store.State.TaskScope == "today", "login startup resets date view to today and creates no foreground workbench");
                    context.ExitThread(); context.Dispose(); context = null;
                }
                bootStore = new WorkboardStore(Storage.Folder); bootStore.State.ShowCardOnStartup = true; bootStore.State.Hidden = true; bootStore.Save();
                using (var signal = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.AutoReset))
                {
                    context = new NoteContext(signal, true); Forms.Application.DoEvents();
                    var bootCard = Field<WorkboardWindow>(context, "workboard");
                    Check(bootCard.Visible && !bootCard.IsEditing && bootCard.Attached, "login startup shows card quietly even after a previous manual hide");
                    context.ShowWorkbench(); Forms.Application.DoEvents();
                    var workspace = Field<WorkbenchWindow>(context, "workbench");
                    Check(workspace != null && workspace.Visible && workspace.Store == bootCard.Store, "manual entry opens one real workbench backed by the same store");
                    workspace.Close(); Forms.Application.DoEvents();
                    Check(!workspace.Visible && bootCard.Visible, "closing the workbench leaves the desktop card running");
                    context.ExitThread(); context.Dispose(); context = null;
                }
                Report.AppendLine("Completed " + DateTime.Now.ToString("s"));
                return 0;
            }
            catch (Exception error) { Report.AppendLine(error.ToString()); return 1; }
            finally
            {
                if (card != null) card.Dispose();
                if (bench != null) { bench.AllowClose = true; bench.Dispose(); }
                if (context != null) { context.ExitThread(); context.Dispose(); }
                File.WriteAllText(Path.Combine(Storage.Folder, "verification-daily-integration.txt"), Report.ToString(), Encoding.UTF8);
            }
        }
    }
}
