using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Input;
using Forms = System.Windows.Forms;

namespace LocalDesktopNote
{
    internal static class SharedPersistenceVerification
    {
        private static readonly StringBuilder Report = new StringBuilder();
        private static int passed, failed;
        private static void Check(bool condition, string message)
        {
            Report.AppendLine((condition ? "PASS: " : "FAIL: ") + message);
            if (condition) passed++; else failed++;
        }
        private static void Pump(int milliseconds)
        {
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < milliseconds) { Forms.Application.DoEvents(); Thread.Sleep(5); }
        }
        private sealed class Fixture : IDisposable
        {
            internal readonly WorkboardStore Store;
            internal readonly WorkboardWindow Card;
            internal readonly WorkbenchWindow Bench;
            internal Fixture(string name)
            {
                Store = new WorkboardStore(Path.Combine(Storage.Folder, "fixture", name));
                Store.AddTask("原任务标题"); Store.State.TaskDraft = "原草稿";
                Store.State.DraftPlanDate = WorkboardStore.FormatPlanDate(Store.Today); Store.Save();
                Card = new WorkboardWindow(Store); Bench = new WorkbenchWindow(Store);
            }
            public void Dispose() { Card.Dispose(); Bench.Dispose(); }
        }
        private static string Status(Fixture f, bool card)
        {
            return card ? ((TextBlock)f.Card.View.FindName("CardSaveStatus")).Text
                : ((TextBlock)typeof(WorkbenchWindow).GetField("status", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(f.Bench)).Text;
        }
        // Reproduce an old/deferred view without pretending it is a new user edit.
        private static void StaleView(object window, TextBox editor, string text)
        {
            FieldInfo loading = window.GetType().GetField("loading", BindingFlags.Instance | BindingFlags.NonPublic);
            bool previous = (bool)loading.GetValue(window); loading.SetValue(window, true);
            try { editor.Text = text; } finally { loading.SetValue(window, previous); }
        }
        private static int HistoryCount(Fixture f)
        {
            return Directory.Exists(f.Store.HistoryFolder) ? Directory.GetFiles(f.Store.HistoryFolder, "*.json", SearchOption.AllDirectories).Length : 0;
        }
        private static string[] HistoryTitles(Fixture f)
        {
            return Directory.Exists(f.Store.HistoryFolder) ? Directory.GetFiles(f.Store.HistoryFolder, "*.json", SearchOption.AllDirectories)
                .SelectMany(path => f.Store.ReadHistorySnapshot(path).Tasks.Select(task => task.Text)).ToArray() : new string[0];
        }
        private static void CommitTitle(Fixture f, bool card)
        {
            if (card) typeof(WorkboardWindow).GetMethod("CommitTaskTitle", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(f.Card, new object[] { f.Card.TaskEditors.Single(), f.Store.State.Tasks.Single() });
            else typeof(WorkbenchWindow).GetMethod("CommitTitle", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(f.Bench, new object[] { f.Bench.TaskEditors.Single() });
        }
        private static void Titles(bool card)
        {
            using (var f = new Fixture(card ? "card-title-commit" : "bench-title-commit"))
            {
                string name = card ? "card" : "workbench"; int history = HistoryCount(f);
                TextBox editor = card ? f.Card.TaskEditors.Single() : f.Bench.TaskEditors.Single();
                foreach (string input in new[] { "原任务标题g", "原任务标题guo", "原任务标题郭" })
                {
                    editor.Text = input; f.Card.Flush(); f.Bench.Flush();
                    Check(f.Store.State.Tasks.Single().Text == "原任务标题" && new WorkboardStore(Path.GetDirectoryName(f.Store.PathName)).State.Tasks.Single().Text == "原任务标题" && HistoryCount(f) == history,
                        name + " typing and pinyin remain an editor buffer during ordinary and peer flushes");
                }
                Pump(750);
                Check(f.Store.State.Tasks.Single().Text == "原任务标题" && HistoryCount(f) == history, name + " the real 600ms autosave deadline never submits a title buffer");
                if (card) { f.Bench.NewTaskEditor.Text = "同伴保存草稿"; f.Bench.Flush(); }
                else { f.Card.NewTaskEditor.Text = "同伴保存草稿"; f.Card.Flush(); }
                Check(editor.Text == "原任务标题郭" && (card ? f.Card.TaskEditors.Single() : f.Bench.TaskEditors.Single()) == editor,
                    name + " unfinished title survives an unrelated peer save and refresh");
                editor.Text = "  完整的任务新标题  ";
                Check(card ? f.Card.FinishMemoHistory() : f.Bench.FinishMemoHistory(), name + " explicit save accepts the final title");
                Check(f.Store.State.Tasks.Single().Text == "完整的任务新标题" && new WorkboardStore(Path.GetDirectoryName(f.Store.PathName)).State.Tasks.Single().Text == "完整的任务新标题",
                    name + " explicit save trims and persists the final title once");
                string[] titles = HistoryTitles(f);
                Check(titles.Count(text => text == "完整的任务新标题") == 1 && !titles.Any(text => text.Contains("guo") || text.EndsWith("郭", StringComparison.Ordinal)),
                    name + " history contains the final committed title without typing intermediates");
                object peer = card ? (object)f.Bench : f.Card;
                TextBox mirror = card ? f.Bench.TaskEditors.Single() : f.Card.TaskEditors.Single();
                StaleView(peer, mirror, "  旧镜像标题  "); CommitTitle(f, !card);
                f.Card.Flush(); f.Bench.Flush();
                Check(f.Store.State.Tasks.Single().Text == "完整的任务新标题", name + " stale peer projection cannot overwrite a committed title");
                editor = card ? f.Card.TaskEditors.Single() : f.Bench.TaskEditors.Single(); editor.Text = "   "; CommitTitle(f, card);
                Check(editor.Text == "完整的任务新标题" && f.Store.State.Tasks.Single().Text == "完整的任务新标题", name + " empty title submission restores the last committed value");
                editor.Text = "失败后仍保留的最终标题";
                using (var locked = new FileStream(f.Store.PathName, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    Check(!(card ? f.Card.FinishMemoHistory() : f.Bench.FinishMemoHistory()) && editor.Text == "失败后仍保留的最终标题" && !HistoryTitles(f).Contains("失败后仍保留的最终标题"),
                        name + " failed explicit save retains the submitted editor text without a false final history version");
                }
                Check((card ? f.Card.FinishMemoHistory() : f.Bench.FinishMemoHistory()) && new WorkboardStore(Path.GetDirectoryName(f.Store.PathName)).State.Tasks.Single().Text == "失败后仍保留的最终标题",
                    name + " failed title submission remains retryable");
            }
        }
        private static void TitleSubmissionBoundaries(bool card)
        {
            using (var f = new Fixture(card ? "card-title-boundaries" : "bench-title-boundaries"))
            {
                string name = card ? "card" : "workbench";
                TextBox editor = card ? f.Card.TaskEditors.Single() : f.Bench.TaskEditors.Single();
                editor.Text = "失焦后保存的完整任务";
                editor.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, editor, card ? f.Card.NewTaskEditor : f.Bench.NewTaskEditor) { RoutedEvent = Keyboard.LostKeyboardFocusEvent });
                Check(new WorkboardStore(Path.GetDirectoryName(f.Store.PathName)).State.Tasks.Single().Text == "失焦后保存的完整任务", name + " routed focus departure submits and persists the title");
                editor = card ? f.Card.TaskEditors.Single() : f.Bench.TaskEditors.Single(); editor.Text = "切页前保存的完整任务";
                if (card) f.Card.SelectScope("all");
                else typeof(WorkbenchWindow).GetMethod("Navigate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(f.Bench, new object[] { "history" });
                Check(new WorkboardStore(Path.GetDirectoryName(f.Store.PathName)).State.Tasks.Single().Text == "切页前保存的完整任务", name + " explicit page navigation submits and persists the title before changing the view");
            }
        }
        private static void Drafts(bool card)
        {
            using (var f = new Fixture(card ? "card-submits" : "bench-submits"))
            {
                string latest = "另一处刚写下的最新共享草稿";
                if (card) f.Bench.NewTaskEditor.Text = latest; else f.Card.NewTaskEditor.Text = latest;
                Check((card ? f.Card.NewTaskEditor.Text : f.Bench.NewTaskEditor.Text) == latest,
                    (card ? "workbench-to-card" : "card-to-workbench") + " draft edits synchronize immediately");
                StaleView(card ? (object)f.Card : f.Bench, card ? f.Card.NewTaskEditor : f.Bench.NewTaskEditor, "旧草稿镜像");
                if (card) f.Card.AddDraft(); else f.Bench.AddDraft();
                f.Card.Flush(); f.Bench.Flush();
                Check(f.Store.State.Tasks.Last().Text == latest && f.Store.State.TaskDraft == "" && new WorkboardStore(Path.GetDirectoryName(f.Store.PathName)).State.Tasks.Last().Text == latest,
                    (card ? "card" : "workbench") + " add consumes the latest model draft even when its editor projection is stale");
                int count = f.Store.State.Tasks.Count;
                if (card) f.Card.AddDraft(); else f.Bench.AddDraft();
                Check(f.Store.State.Tasks.Count == count, (card ? "card" : "workbench") + " retrying add after the shared draft was consumed creates no duplicate");
            }
        }
        private static void PeerSaveAfterError(bool card)
        {
            using (var f = new Fixture(card ? "card-error" : "bench-error"))
            {
                if (card) f.Card.IdeasEditor.Text = "真实保存失败之后由另一窗保存"; else f.Bench.IdeasEditor.Text = "真实保存失败之后由另一窗保存";
                using (var locked = new FileStream(f.Store.PathName, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    Check(!(card ? f.Card.Flush() : f.Bench.Flush()) && Status(f, card).Contains("保存失败"), "fixture produces a real " + (card ? "card" : "workbench") + " content-save failure");
                    string error = Status(f, card);
                    if (card) f.Bench.SelectScope("all"); else f.Card.SelectScope("all");
                    Check(Status(f, card) == error, "view-only broadcast does not clear the " + (card ? "card" : "workbench") + " outstanding content error");
                }
                if (card) { f.Bench.NewTaskEditor.Text = "工作台触发真实保存"; f.Bench.Flush(); }
                else { f.Card.NewTaskEditor.Text = "卡片触发真实保存"; f.Card.Flush(); }
                Check(new WorkboardStore(Path.GetDirectoryName(f.Store.PathName)).State.Ideas == "真实保存失败之后由另一窗保存"
                    && !Status(f, card).Contains("保存失败") && Status(f, card).Contains("已保存"),
                    "actual successful peer persistence clears the " + (card ? "card" : "workbench") + " stale save error");
            }
        }
        private static void BroadcastDeadline()
        {
            using (var f = new Fixture("broadcast-deadline"))
            {
                DateTime before = File.GetLastWriteTimeUtc(f.Store.PathName);
                f.Card.IdeasEditor.Text = "原600毫秒期限的备忘";
                Check(f.Bench.IdeasEditor.Text == "原600毫秒期限的备忘" && File.GetLastWriteTimeUtc(f.Store.PathName) == before,
                    "memo content synchronizes immediately without turning its broadcast into a disk write");
                for (int i = 0; i < 3; i++) { Pump(180); f.Bench.NewTaskEditor.Text = "另一窗连续内容广播 " + i; }
                Pump(180);
                var saved = new WorkboardStore(Path.GetDirectoryName(f.Store.PathName));
                Check(saved.State.Ideas == "原600毫秒期限的备忘" && saved.State.TaskDraft == "另一窗连续内容广播 2",
                    "incoming peer content broadcasts do not postpone the original content-save deadline");
            }
        }
        private static void PersistenceSignals()
        {
            var store = new WorkboardStore(Path.Combine(Storage.Folder, "fixture", "persistence-signals"));
            int saved = 0, changed = 0;
            store.Saved += delegate { saved++; }; store.Changed += delegate { changed++; };
            store.NotifyChanged();
            Check(saved == 0 && changed == 1, "an in-memory broadcast is never reported as successful persistence");
            store.Save(); store.Save();
            Check(saved == 2 && changed == 3, "both an atomic save and a verified unchanged save acknowledge successful persistence");
            store.State.Ideas = "不能误报保存成功";
            bool failedSave = false;
            using (var locked = new FileStream(store.PathName, FileMode.Open, FileAccess.Read, FileShare.Read))
                try { store.Save(); } catch (IOException) { failedSave = true; }
            Check(failedSave && saved == 2 && changed == 3, "a failed atomic write emits neither a persistence success nor a refresh success");
        }
        [STAThread]
        private static int Main()
        {
            try
            {
                Forms.Application.EnableVisualStyles(); Forms.Application.SetCompatibleTextRenderingDefault(false);
                Titles(true); Titles(false); TitleSubmissionBoundaries(true); TitleSubmissionBoundaries(false); Drafts(true); Drafts(false); PeerSaveAfterError(true); PeerSaveAfterError(false); BroadcastDeadline();
                PersistenceSignals();
                Report.AppendLine("Passed: " + passed + "; Failed: " + failed);
                Report.AppendLine("LIMIT: no Form.Show/Attach or native input/focus automation; stale projections are injected under the same loading guard used by rendering.");
                return failed == 0 ? 0 : 1;
            }
            catch (Exception error) { Report.AppendLine(error.ToString()); return 1; }
            finally { File.WriteAllText(Path.Combine(Storage.Folder, "verification-shared-persistence.txt"), Report.ToString(), Encoding.UTF8); }
        }
    }
}
