using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Forms = System.Windows.Forms;

namespace LocalDesktopNote
{
    internal static class SavePolicyVerification
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
            internal int Changes, CardSavingEntries, BenchSavingEntries;
            private readonly TextBlock cardStatus, benchStatus;
            private readonly DependencyPropertyDescriptor descriptor;
            private readonly EventHandler cardStatusChanged, benchStatusChanged;
            internal string CardStatusText { get { return cardStatus.Text; } }
            internal string BenchStatusText { get { return benchStatus.Text; } }

            internal Fixture(string name, bool withCompletedTask = false)
            {
                Store = new WorkboardStore(Path.Combine(Storage.Folder, "fixture", name));
                Store.TodayProvider = delegate { return new DateTime(2026, 10, 21); };
                Store.AddTask("今天任务", Store.Today); Store.AddTask("明天任务", Store.Today.AddDays(1)); Store.AddTask("未安排任务", null);
                if (withCompletedTask) { var completed = Store.AddTask("已完成任务", Store.Today); Store.SetCompleted(completed, true); }
                Store.Save();
                Card = new WorkboardWindow(Store); Bench = new WorkbenchWindow(Store);
                cardStatus = (TextBlock)Card.View.FindName("CardSaveStatus");
                benchStatus = (TextBlock)typeof(WorkbenchWindow).GetField("status", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(Bench);
                descriptor = DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock));
                cardStatusChanged = delegate { if (cardStatus.Text.Contains("正在保存")) CardSavingEntries++; };
                benchStatusChanged = delegate { if (benchStatus.Text.Contains("正在保存")) BenchSavingEntries++; };
                descriptor.AddValueChanged(cardStatus, cardStatusChanged); descriptor.AddValueChanged(benchStatus, benchStatusChanged);
                Store.Changed += delegate { Changes++; };
            }

            internal void MarkFile()
            {
                File.SetLastWriteTimeUtc(Store.PathName, new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc));
            }

            public void Dispose()
            {
                descriptor.RemoveValueChanged(cardStatus, cardStatusChanged); descriptor.RemoveValueChanged(benchStatus, benchStatusChanged);
                Card.Dispose(); Bench.Dispose();
            }
        }

        private static void SameScope()
        {
            using (var f = new Fixture("same-scope"))
            {
                f.MarkFile(); DateTime before = File.GetLastWriteTimeUtc(f.Store.PathName);
                for (int i = 0; i < 3; i++) f.Card.SelectScope("today");
                Check(f.CardSavingEntries == 0, "reselecting the current card filter does not show content-saving status");
                for (int i = 0; i < 3; i++) f.Bench.SelectScope("today");
                Check(f.BenchSavingEntries == 0, "reselecting the current workbench filter does not enter even a transient content-saving status");
                Check(File.GetLastWriteTimeUtc(f.Store.PathName) == before, "same-filter clicks leave the persisted file untouched");
                Report.AppendLine("MEASURE same-scope: cardSaving=" + f.CardSavingEntries + "; benchSaving=" + f.BenchSavingEntries + "; Changed=" + f.Changes);
            }
        }

        private static void Browsing(bool card)
        {
            using (var f = new Fixture(card ? "card-browse" : "bench-browse"))
            {
                int writes = 0;
                foreach (string scope in new[] { "tomorrow", "later", "all" })
                {
                    DateTime before = File.GetLastWriteTimeUtc(f.Store.PathName);
                    if (card) f.Card.SelectScope(scope); else f.Bench.SelectScope(scope);
                    if (File.GetLastWriteTimeUtc(f.Store.PathName) != before) writes++;
                    if (scope == "tomorrow")
                        Check(card ? f.Bench.TaskEditors.SingleOrDefault() != null && f.Bench.TaskEditors[0].Text == "明天任务"
                            : f.Card.TaskEditors.SingleOrDefault() != null && f.Card.TaskEditors[0].Text == "明天任务",
                            (card ? "card" : "workbench") + " filter change updates the other window immediately before any timer tick");
                }
                Check(writes <= 1, (card ? "card" : "workbench") + " rapid filter browsing does not synchronously rewrite the file on every click");
                Check((card ? f.CardSavingEntries : f.BenchSavingEntries) == 0,
                    (card ? "card" : "workbench") + " browsing does not claim content is being saved");
                Report.AppendLine("MEASURE " + (card ? "card" : "workbench") + "-browse: immediateWrites=" + writes + "; Changed=" + f.Changes + "; cardSaving=" + f.CardSavingEntries + "; benchSaving=" + f.BenchSavingEntries);
                // An explicit lifecycle flush must keep the final selection across restart.
                Check(f.Card.Flush() && f.Bench.Flush() && new WorkboardStore(Path.GetDirectoryName(f.Store.PathName)).State.TaskScope == "all",
                    (card ? "card" : "workbench") + " final selection survives lifecycle flush and reopening");
            }
        }

        private static void PendingContent()
        {
            using (var f = new Fixture("pending-content"))
            {
                var watch = Stopwatch.StartNew();
                f.Card.IdeasEditor.Text = "原保存期限内必须落盘的备忘";
                f.Card.NewTaskEditor.Text = "不能因浏览丢失的任务草稿";
                string date = f.Store.State.DraftPlanDate;
                foreach (string scope in new[] { "tomorrow", "later", "all", "today" })
                {
                    Pump(180); f.Card.SelectScope(scope);
                }
                Pump(100);
                var saved = new WorkboardStore(Path.GetDirectoryName(f.Store.PathName));
                Check(saved.State.Ideas == "原保存期限内必须落盘的备忘" && saved.State.TaskDraft == "不能因浏览丢失的任务草稿",
                    "continuous browsing does not postpone an already-pending content save past its original debounce deadline");
                Check(f.Store.State.TaskDraft == "不能因浏览丢失的任务草稿" && f.Store.State.DraftPlanDate == date,
                    "browsing preserves pending draft text and its concrete plan date");
                Report.AppendLine("MEASURE pending-content: elapsedMs=" + watch.ElapsedMilliseconds + "; persistedMemo=" + saved.State.Ideas + "; persistedDraft=" + saved.State.TaskDraft + "; Changed=" + f.Changes);
                Check(f.Card.Flush() && f.Bench.Flush(), "pending content and final preferences can be flushed before closing");
            }
        }

        private static void Folding(bool card)
        {
            using (var f = new Fixture(card ? "card-folding" : "bench-folding", true))
            {
                f.MarkFile(); DateTime before = File.GetLastWriteTimeUtc(f.Store.PathName);
                if (card) f.Card.CompletedGroup.IsExpanded = true; else f.Bench.CompletedGroup.IsExpanded = true;
                Check(card ? f.Bench.CompletedGroup.IsExpanded : f.Card.CompletedGroup.IsExpanded,
                    (card ? "card" : "workbench") + " completed-group disclosure synchronizes immediately to the other window");
                Check(f.CardSavingEntries == 0 && f.BenchSavingEntries == 0 && File.GetLastWriteTimeUtc(f.Store.PathName) == before,
                    (card ? "card" : "workbench") + " disclosure stays quiet and does not synchronously save content");
                if (card) f.Card.SelectScope("all"); else f.Bench.SelectScope("all");
                f.Card.Close(); f.Bench.Close();
                var reopened = new WorkboardStore(Path.GetDirectoryName(f.Store.PathName));
                Check(reopened.State.CompletedExpanded && reopened.State.TaskScope == "all",
                    (card ? "card" : "workbench") + " final scope and disclosure survive actual unshown-form Close/Dispose");
            }
        }

        private static void ReturnToOriginal(bool card)
        {
            using (var f = new Fixture(card ? "card-return-original" : "bench-return-original"))
            {
                f.MarkFile(); DateTime before = File.GetLastWriteTimeUtc(f.Store.PathName);
                byte[] original = File.ReadAllBytes(f.Store.PathName);
                if (card) { f.Card.SelectScope("tomorrow"); f.Card.SelectScope("today"); }
                else { f.Bench.SelectScope("tomorrow"); f.Bench.SelectScope("today"); }
                Check(f.Card.Flush() && f.Bench.Flush() && File.GetLastWriteTimeUtc(f.Store.PathName) == before && File.ReadAllBytes(f.Store.PathName).SequenceEqual(original),
                    (card ? "card" : "workbench") + " browsing away and back to the original view does not rewrite identical persisted bytes on final flush");
            }
        }

        private static void FailedContent()
        {
            using (var f = new Fixture("card-save-error"))
            {
                f.Card.IdeasEditor.Text = "写盘失败仍须保留的内容";
                using (var locked = new FileStream(f.Store.PathName, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    Check(!f.Card.Flush() && f.CardStatusText.Contains("保存失败"), "fixture induces a real card atomic-save failure");
                    string error = f.CardStatusText;
                    f.Card.SelectScope("tomorrow");
                    Check(f.CardStatusText == error && f.Card.IdeasEditor.Text == "写盘失败仍须保留的内容",
                        "card browsing after a content-save failure preserves the error feedback and in-memory content");
                }
                Check(f.Card.Flush() && new WorkboardStore(Path.GetDirectoryName(f.Store.PathName)).State.Ideas == "写盘失败仍须保留的内容",
                    "card content and browsed view can save successfully after the failure is removed");
            }
            using (var f = new Fixture("bench-save-error"))
            {
                f.Bench.IdeasEditor.Text = "工作台失败内容";
                using (var locked = new FileStream(f.Store.PathName, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    Check(!f.Bench.Flush() && f.BenchStatusText.Contains("保存失败"), "fixture induces a real workbench atomic-save failure");
                    f.Bench.SelectScope("tomorrow");
                    Check(f.Store.State.TaskScope == "today" && f.BenchStatusText.Contains("保存失败") && f.Bench.IdeasEditor.Text == "工作台失败内容",
                        "workbench content-save failure blocks navigation and retains actionable error feedback");
                }
            }
        }

        private static void EmptyDraftDates()
        {
            using (var f = new Fixture("explicit-empty-draft-date"))
            {
                DateTime chosen = new DateTime(2030, 5, 16);
                f.Bench.DraftDatePicker.SelectedDate = chosen;
                f.Card.SelectScope("tomorrow"); f.Bench.SelectScope("later");
                Check(f.Store.State.TaskDraft == "" && f.Store.State.DraftPlanDate == "2030-05-16" && f.Bench.DraftDatePicker.SelectedDate == chosen,
                    "browsing preserves an explicitly chosen date even while the shared draft is empty");
                f.Bench.UnplannedCheck.IsChecked = true;
                f.Card.SelectScope("today"); f.Bench.SelectScope("tomorrow");
                Check(f.Store.State.DraftPlanDate == "" && f.Bench.UnplannedCheck.IsChecked == true,
                    "browsing also preserves an explicit unplanned selection for an empty draft");
            }
            foreach (bool card in new[] { true, false })
            {
                using (var f = new Fixture(card ? "card-clears-draft" : "bench-clears-draft"))
                {
                    f.Bench.DraftDatePicker.SelectedDate = new DateTime(2030, 5, 16);
                    if (card) { f.Card.NewTaskEditor.Text = "原来的草稿"; f.Card.SelectScope("tomorrow"); f.Card.NewTaskEditor.Clear(); }
                    else { f.Bench.NewTaskEditor.Text = "原来的草稿"; f.Bench.SelectScope("tomorrow"); f.Bench.NewTaskEditor.Clear(); }
                    Check(f.Store.State.TaskDraft == "" && f.Store.State.DraftPlanDate == null,
                        (card ? "card" : "workbench") + " actually clearing a previously written draft releases its frozen date");
                    if (card) f.Card.NewTaskEditor.Text = "重新记录的新草稿"; else f.Bench.NewTaskEditor.Text = "重新记录的新草稿";
                    Check(f.Store.State.DraftPlanDate == WorkboardStore.FormatPlanDate(f.Store.Today.AddDays(1)),
                        (card ? "card" : "workbench") + " a fresh draft after clearing uses the currently selected tomorrow default");
                }
            }
        }

        private static void FailedViewWrite(bool card)
        {
            using (var f = new Fixture(card ? "card-view-write-failure" : "bench-view-write-failure"))
            {
                string originalStatus = card ? f.CardStatusText : f.BenchStatusText;
                if (card) f.Card.SelectScope("tomorrow"); else f.Bench.SelectScope("tomorrow");
                using (var locked = new FileStream(f.Store.PathName, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    bool flushed = card ? f.Card.Flush() : f.Bench.Flush();
                    Check(flushed && (card ? f.CardStatusText : f.BenchStatusText) == originalStatus,
                        (card ? "card" : "workbench") + " view-only atomic-write failure remains quiet and is not reported as a content-save failure");
                    // A promoted content-dirty flag would make workbench navigation fail here.
                    if (card) f.Card.SelectScope("later"); else f.Bench.SelectScope("later");
                    f.Bench.AllowClose = true;
                    var args = new Forms.FormClosingEventArgs(Forms.CloseReason.ApplicationExitCall, false);
                    typeof(Forms.Form).GetMethod("OnFormClosing", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(card ? (object)f.Card : f.Bench, new object[] { args });
                    Check(f.Store.State.TaskScope == "later" && !args.Cancel && (card ? f.Card.Flush() : f.Bench.Flush()),
                        (card ? "card" : "workbench") + " failed view persistence does not promote content dirty, block later browsing or cancel application close");
                }
            }
        }

        private static void HistoryWarningAfterFailure()
        {
            using (var f = new Fixture("history-warning-after-content-error"))
            {
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(f.Store.PathName), "history"), "blocks real history snapshot creation");
                f.Bench.IdeasEditor.Text = "快照和内容写入同时失败时保留错误";
                using (var locked = new FileStream(f.Store.PathName, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    Check(!f.Bench.Flush() && !String.IsNullOrEmpty(f.Store.HistoryNotice) && f.BenchStatusText.Contains("保存失败"),
                        "fixture produces a real history warning together with a primary content-save error");
                    string error = f.BenchStatusText;
                    f.Card.SelectScope("tomorrow"); f.Bench.RefreshContent();
                    Check(f.BenchStatusText == error && f.Bench.IdeasEditor.Text == "快照和内容写入同时失败时保留错误",
                        "shared view refresh with HistoryNotice does not overwrite an outstanding content-save error");
                }
            }
        }

        [STAThread]
        private static int Main()
        {
            try
            {
                Forms.Application.EnableVisualStyles(); Forms.Application.SetCompatibleTextRenderingDefault(false);
                SameScope(); Browsing(true); Browsing(false); PendingContent();
                Folding(true); Folding(false); ReturnToOriginal(true); ReturnToOriginal(false); FailedContent();
                EmptyDraftDates(); FailedViewWrite(true); FailedViewWrite(false); HistoryWarningAfterFailure();
                Report.AppendLine("Passed: " + passed + "; Failed: " + failed);
                Report.AppendLine("LIMIT: windows are constructed but never shown or attached; no native focus or input automation is used.");
                return failed == 0 ? 0 : 1;
            }
            catch (Exception error) { Report.AppendLine(error.ToString()); return 1; }
            finally { File.WriteAllText(Path.Combine(Storage.Folder, "verification-save-policy.txt"), Report.ToString(), Encoding.UTF8); }
        }
    }
}
