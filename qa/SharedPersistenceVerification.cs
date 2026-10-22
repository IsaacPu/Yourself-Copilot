using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Controls;
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
        private static void Titles()
        {
            using (var f = new Fixture("titles"))
            {
                f.Card.TaskEditors.Single().Text = "卡片刚改的新标题";
                Check(f.Bench.TaskEditors.Single().Text == "卡片刚改的新标题", "task-title edits synchronize the other view before any timer tick");
                StaleView(f.Bench, f.Bench.TaskEditors.Single(), "旧工作台镜像");
                f.Bench.Flush(); f.Card.Flush();
                Check(new WorkboardStore(Path.GetDirectoryName(f.Store.PathName)).State.Tasks.Single().Text == "卡片刚改的新标题", "a clean workbench flush cannot overwrite the shared title from an old editor projection");
                f.Bench.TaskEditors.Single().Text = "  工作台真实修改并修剪  "; f.Bench.Flush();
                Check(f.Store.State.Tasks.Single().Text == "工作台真实修改并修剪" && f.Bench.TaskEditors.Single().Text == "工作台真实修改并修剪", "real title edits still trim and persist normally");
                StaleView(f.Card, f.Card.TaskEditors.Single(), "  旧卡片镜像  ");
                typeof(WorkboardWindow).GetMethod("CommitTaskTitle", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(f.Card, new object[] { f.Card.TaskEditors.Single(), f.Store.State.Tasks.Single() });
                Check(f.Store.State.Tasks.Single().Text == "工作台真实修改并修剪", "a stale whitespace-bearing card projection cannot overwrite the latest peer title during commit");
                f.Bench.TaskEditors.Single().Text = "   "; f.Bench.Flush();
                Check(f.Bench.TaskEditors.Single().Text == "工作台真实修改并修剪" && f.Store.State.Tasks.Single().Text == "工作台真实修改并修剪", "clearing a task title restores the last valid shared value");
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
                Titles(); Drafts(true); Drafts(false); PeerSaveAfterError(true); PeerSaveAfterError(false); BroadcastDeadline();
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
