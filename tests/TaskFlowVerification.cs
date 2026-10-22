using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Controls;
using System.Windows.Input;
using Forms = System.Windows.Forms;

namespace LocalDesktopNote
{
    internal static class TaskFlowVerification
    {
        private static void Check(bool result, string name, StringBuilder report)
        {
            if (!result) throw new InvalidOperationException("FAIL: " + name);
            report.AppendLine("PASS: " + name);
        }
        [STAThread]
        private static int Main()
        {
            var report = new StringBuilder(); WorkboardWindow card = null;
            try
            {
                try { Desktop.SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch (EntryPointNotFoundException) { }
                Forms.Application.EnableVisualStyles(); Forms.Application.SetCompatibleTextRenderingDefault(false);
                var store = new WorkboardStore(Storage.Folder);
                var first = store.AddTask("1. 吃饭"); var second = store.AddTask("整理今天的想法");
                card = new WorkboardWindow(store); card.Attach(); card.ShowCard(false); Forms.Application.DoEvents();
                Check(card.TaskEditors.Count == 2, "two pending tasks are visible before completion", report);
                card.TaskChecks[0].IsChecked = true;
                card.TaskChecks[0].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Forms.Application.DoEvents();
                Check(store.State.Tasks[0].Done, "checkbox changes the actual task completion state", report);
                Check(card.TaskEditors.Count == 1 && card.TaskEditors[0].Text == "整理今天的想法", "completed task leaves the pending list immediately", report);
                Check(store.State.Tasks.Count == 2, "completion preserves the task for later restoration", report);
                Check(card.CompletedGroup.Visibility == Visibility.Visible && !card.CompletedGroup.IsExpanded, "completed group is discoverable and collapsed by default", report);
                Check(card.CompletedLabels.Count == 1 && !card.CompletedLabels[0].IsVisible, "collapsed completion does not occupy a task row", report);
                Check(card.CompletedGroup.Header.ToString() == "已完成（1）" && ((TextBlock)card.View.FindName("TaskCount")).Text == "1 项待办", "completed and pending counts describe their own sections", report);
                card.CompletedGroup.IsExpanded = true; Forms.Application.DoEvents();
                Check(card.CompletedLabels[0].IsVisible && card.CompletedLabels[0].Text == "1. 吃饭", "expanding completed tasks shows the original title without rewriting it", report);
                Check(card.CompletedLabels[0].TextDecorations.Any(d => d.Location == TextDecorationLocation.Strikethrough), "completed task has a real strikethrough decoration", report);
                Check(System.Windows.Automation.AutomationProperties.GetName(card.CompletedChecks[0]).StartsWith("恢复为待办"), "completed checkbox announces its restore action", report);
                card.CompletedChecks[0].IsChecked = false;
                card.CompletedChecks[0].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Forms.Application.DoEvents();
                Check(card.TaskEditors.Select(t => t.Text).SequenceEqual(new[] { "1. 吃饭", "整理今天的想法" }), "uncompleting restores the task to its original pending order", report);
                Check(card.CompletedGroup.Visibility == Visibility.Collapsed, "empty completed group is removed from the interface", report);
                card.TaskChecks[0].IsChecked = true; card.TaskChecks[0].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Forms.Application.DoEvents();
                Check(card.CompletedGroup.IsExpanded && card.CompletedLabels[0].IsVisible, "explicit expanded preference is respected after later completions", report);
                card.RemoveTask(first); card.UndoRemoveTask(); Forms.Application.DoEvents();
                Check(store.State.Tasks[0] == first && first.Done && card.CompletedLabels[0].Text == "1. 吃饭", "undoing removal restores completed status and original order", report);
                card.RemoveTask(second); card.UndoRemoveTask(); Forms.Application.DoEvents();
                Check(store.State.Tasks[1] == second && card.TaskEditors.Single().Text == second.Text, "undoing pending removal restores its place without changing other tasks", report);
                card.BeginEditing(card.TaskEditors[0]); Forms.Application.DoEvents();
                card.TaskEditors[0].Text = "  "; card.NewTaskEditor.Focus(); Forms.Application.DoEvents();
                Check(card.TaskEditors[0].Text == "整理今天的想法" && second.Text == "整理今天的想法", "blank title edit restores the previous valid title", report);
                card.TaskEditors[0].Text = "  整理项目复盘  "; card.NewTaskEditor.Focus(); Forms.Application.DoEvents();
                Check(second.Text.Trim() == "整理项目复盘", "renaming changes the same task rather than creating a duplicate", report);
                card.NewTaskEditor.Text = "新增待办"; card.AddDraft(); Forms.Application.DoEvents();
                var third = store.State.Tasks.Last();
                Check(card.TaskEditors.Count == 2 && card.TaskEditors[0].Tag == second && card.TaskEditors[1].Tag == third, "new pending task stays above the completed group", report);
                card.NewTaskEditor.Text = "尚未提交的草稿";
                card.TaskChecks[0].IsChecked = true; card.TaskChecks[0].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Forms.Application.DoEvents();
                Check(card.NewTaskEditor.Text == "尚未提交的草稿" && store.State.TaskDraft == "尚未提交的草稿", "regrouping preserves unfinished task input", report);
                Check(card.TaskChecks[0].IsKeyboardFocusWithin, "completing an item keeps keyboard focus on the next pending task", report);
                card.TaskChecks[0].IsChecked = true; card.TaskChecks[0].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Forms.Application.DoEvents();
                Check(card.TaskEditors.Count == 0 && card.CompletedChecks.Count == 3, "all completed tasks leave the active section", report);
                Check(((TextBlock)card.View.FindName("TasksPlaceholder")).Text.Contains("完成") && ((TextBlock)card.View.FindName("TaskCount")).Text == "全部完成", "all-done state differs from a first-run empty list", report);
                Check(card.NewTaskEditor.IsKeyboardFocusWithin, "finishing the last task leaves the new-task input ready", report);
                card.CompletedGroup.IsExpanded = false; card.Flush(); card.Dispose(); card = null;
                var reloaded = new WorkboardStore(Storage.Folder); card = new WorkboardWindow(reloaded); card.Attach(); card.ShowCard(false); Forms.Application.DoEvents();
                Check(reloaded.State.Tasks.Count == 3 && reloaded.State.Tasks.All(t => t.Done) && card.TaskEditors.Count == 0, "restart preserves completion and never mixes completed tasks into pending", report);
                Check(!card.CompletedGroup.IsExpanded && reloaded.State.TaskDraft == "尚未提交的草稿", "restart preserves collapsed preference and unfinished draft", report);
                var middle = reloaded.State.Tasks[1]; card.RemoveTask(middle); card.Flush(); card.Dispose(); card = null;
                reloaded = new WorkboardStore(Storage.Folder); var undo = reloaded.Undo();
                Check(undo.Id == middle.Id && reloaded.State.Tasks[1].Id == middle.Id && undo.Done, "undo after restart restores the original position and completion", report);
                string legacyFolder = Path.Combine(Storage.Folder, "legacy-schema"); Directory.CreateDirectory(legacyFolder);
                File.WriteAllText(Path.Combine(legacyFolder, "workboard.json"), "{\"Version\":1,\"Ideas\":\"old note\",\"TaskDraft\":\"old draft\",\"Tasks\":[{\"Id\":\"legacy-task\",\"Text\":\"old completed task\",\"Done\":true}],\"Removed\":[]}");
                var legacy = new WorkboardStore(legacyFolder);
                Check(legacy.State.Tasks.Single().Done && legacy.State.Ideas == "old note" && !legacy.State.CompletedExpanded, "v3 data loads without requiring a destructive migration", report);
                card = new WorkboardWindow(reloaded); card.Attach(); card.ShowCard(false);
                var random = new Random(42); bool consistent = true;
                for (int step = 0; step < 64; step++)
                {
                    int action = random.Next(5);
                    if (action == 0 || reloaded.State.Tasks.Count == 0) { card.NewTaskEditor.Text = "连续操作 " + step; card.AddDraft(); }
                    else if (action == 1)
                    {
                        var item = reloaded.State.Tasks[random.Next(reloaded.State.Tasks.Count)];
                        if (item.Done) card.CompletedGroup.IsExpanded = true;
                        var box = card.TaskChecks.Concat(card.CompletedChecks).First(c => c.Tag == item);
                        box.IsChecked = !item.Done; box.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    }
                    else if (action == 2) card.RemoveTask(reloaded.State.Tasks[random.Next(reloaded.State.Tasks.Count)]);
                    else if (action == 3) card.UndoRemoveTask();
                    else card.CompletedGroup.IsExpanded = !card.CompletedGroup.IsExpanded;
                    consistent &= card.TaskEditors.Select(t => ((WorkTask)t.Tag).Id).SequenceEqual(reloaded.State.Tasks.Where(t => !t.Done).Select(t => t.Id));
                    consistent &= card.CompletedChecks.Select(t => ((WorkTask)t.Tag).Id).SequenceEqual(reloaded.State.Tasks.Where(t => t.Done).Select(t => t.Id));
                    var all = reloaded.State.Tasks.Concat(reloaded.State.Removed).ToList();
                    consistent &= all.Select(t => t.Id).Distinct().Count() == all.Count && all.All(t => !String.IsNullOrWhiteSpace(t.Text));
                }
                Check(consistent, "64 mixed task actions preserve grouping order valid titles and unique task identities", report);
                report.AppendLine("Completed " + DateTime.Now.ToString("s"));
                File.WriteAllText(Path.Combine(Storage.Folder, "verification-task-flow-v4.txt"), report.ToString(), Encoding.UTF8); return 0;
            }
            catch (Exception error) { report.AppendLine(error.ToString()); File.WriteAllText(Path.Combine(Storage.Folder, "verification-task-flow-v4.txt"), report.ToString(), Encoding.UTF8); return 1; }
            finally { if (card != null) card.Dispose(); }
        }
    }
}
