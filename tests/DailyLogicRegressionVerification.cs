using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Controls;
using Forms = System.Windows.Forms;

namespace LocalDesktopNote
{
    // Behavioral regressions only: construct windows, never Show/Attach or focus them.
    internal static class DailyLogicRegressionVerification
    {
        private static readonly StringBuilder Report = new StringBuilder();
        private static int passed, failed;
        private static readonly string Root = Path.Combine(Storage.Folder, "fixtures");

        private static void Check(bool value, string description)
        {
            Report.AppendLine((value ? "PASS: " : "FAIL: ") + description);
            if (value) passed++; else failed++;
        }
        private static string Status(WorkbenchWindow window)
        {
            return ((TextBlock)typeof(WorkbenchWindow).GetField("status", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window)).Text;
        }
        private static string LocalTimestamp(DateTime localTime)
        {
            return DateTime.SpecifyKind(localTime, DateTimeKind.Local).ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
        }
        private static void Run(string name, Action check)
        {
            Report.AppendLine("CASE: " + name);
            try { check(); }
            catch (Exception error) { failed++; Report.AppendLine("FAIL: unexpected fixture exception: " + error); }
        }

        private static void MemoRetry(bool delayed)
        {
            var store = new WorkboardStore(Path.Combine(Root, delayed ? "memo-delayed" : "memo-immediate"));
            store.State.Ideas = "将这段备忘建为今日任务";
            store.Save();
            using (var bench = new WorkbenchWindow(store))
            {
                Directory.CreateDirectory(store.PathName + ".tmp");
                bool first = bench.ConvertMemo(false);
                string firstId = store.State.Tasks.Single().Id;
                Check(!first && new WorkboardStore(Path.GetDirectoryName(store.PathName)).State.Tasks.Count == 0,
                    "memo " + (delayed ? "delayed" : "immediate") + " fixture induces a real failed atomic save and keeps one task in memory");
                if (!delayed)
                {
                    bench.ConvertMemo(false);
                    Check(store.State.Tasks.Count == 1 && store.State.Tasks[0].Id == firstId && Status(bench).Contains("保存失败"),
                        "retry while storage is still blocked keeps the same task and actionable failure feedback");
                }
                if (delayed) Thread.Sleep(2100);
                Directory.Delete(store.PathName + ".tmp");
                bench.ConvertMemo(false);
                var reopened = new WorkboardStore(Path.GetDirectoryName(store.PathName));
                Check(store.State.Tasks.Count == 1 && store.State.Tasks[0].Id == firstId
                    && reopened.State.Tasks.Count == 1 && reopened.State.Tasks[0].Id == firstId,
                    "memo " + (delayed ? "retry after 2 seconds" : "immediate retry") + " saves the original failed conversion exactly once");
                Check(bench.IdeasEditor.Text == "将这段备忘建为今日任务", "memo retry preserves the original memo text");
                Report.AppendLine("MEASURE memo " + (delayed ? "delayed" : "immediate") + ": memory=" + store.State.Tasks.Count + "; disk=" + reopened.State.Tasks.Count + "; status=" + Status(bench));
            }
        }

        private static void UndoDraftDate(int draftKind)
        {
            var store = new WorkboardStore(Path.Combine(Root, "undo-date-" + draftKind));
            DateTime today = store.Today;
            var task = store.AddTask("恢复到今天的原任务", today);
            store.Remove(task); store.State.TaskScope = "tomorrow"; store.Save();
            using (var bench = new WorkbenchWindow(store))
            {
                DateTime expected = today;
                if (draftKind == 1) { expected = today.AddDays(4); bench.DraftDatePicker.SelectedDate = expected; }
                if (draftKind == 2) { expected = today.AddDays(1); bench.NewTaskEditor.Text = "保留有日期的非空草稿"; }
                bench.UndoRemove();
                Check(bench.CurrentScope == "today" && bench.DraftDatePicker.SelectedDate == expected,
                    "undo reveals today and " + (draftKind == 0 ? "refreshes the implicit empty draft date" : draftKind == 1 ? "preserves the explicit empty-draft date" : "preserves the nonempty draft's date"));
                if (draftKind == 0)
                {
                    Check(store.State.DraftPlanDate == null, "undo keeps the empty default date implicit");
                    bench.NewTaskEditor.Text = "在恢复后的今天页添加"; bench.AddDraft();
                    Check(store.State.Tasks.Last().PlanDate == WorkboardStore.FormatPlanDate(today) && bench.CurrentScope == "today",
                        "adding after implicit-date undo schedules the new task for the currently displayed today");
                }
            }
        }

        private static void LegacyDraft(string originalScope, bool addFromCard)
        {
            string folder = Path.Combine(Root, "legacy-" + originalScope + "-" + (addFromCard ? "card" : "bench"));
            Directory.CreateDirectory(folder);
            const string draftText = "旧版保留下来的未提交草稿";
            File.WriteAllText(Path.Combine(folder, "workboard.json"),
                "{\"Version\":1,\"Ideas\":\"旧备忘\",\"TaskDraft\":\"" + draftText
                + "\",\"TaskScope\":\"" + originalScope + "\",\"Tasks\":[{\"Id\":\"legacy-unplanned\",\"Text\":\"旧版无日期任务\",\"Done\":false}],\"Removed\":[]}", new UTF8Encoding(false));
            var store = new WorkboardStore(folder);
            DateTime? expectedDate = originalScope == "later" ? (DateTime?)null : originalScope == "tomorrow" ? store.Today.AddDays(1) : store.Today;
            string expectedStateDate = WorkboardStore.FormatPlanDate(expectedDate) ?? "";
            Check(store.State.DraftPlanDate == expectedStateDate,
                "legacy " + originalScope + " draft date freezes once to the default displayed at load");
            using (var card = new WorkboardWindow(store))
            using (var bench = new WorkbenchWindow(store))
            {
                card.SelectScope(originalScope == "tomorrow" || originalScope == "later" ? "today" : "tomorrow");
                Check(store.State.TaskDraft == draftText && store.State.DraftPlanDate == expectedStateDate
                    && bench.DraftDatePicker.SelectedDate == expectedDate && (bench.UnplannedCheck.IsChecked == true) == !expectedDate.HasValue,
                    "legacy " + originalScope + " draft keeps one date interpretation in both windows after browsing");
                if (addFromCard) card.AddDraft(); else bench.AddDraft();
                var added = store.State.Tasks.Single(t => t.Text == draftText);
                Check(added.PlanDate == WorkboardStore.FormatPlanDate(expectedDate),
                    "legacy " + originalScope + " draft submitted from " + (addFromCard ? "card" : "workbench") + " uses the frozen load-time date");
                var old = store.State.Tasks.Single(t => t.Id == "legacy-unplanned");
                Check(old.PlanDate == null && old.Text == "旧版无日期任务", "legacy migration never assigns a plan date to existing tasks");
            }
        }

        private static void TodayCompletion()
        {
            var store = new WorkboardStore(Path.Combine(Root, "today-completion"));
            DateTime clock = DateTime.Today;
            store.TodayProvider = delegate { return clock; };
            var todayPending = store.AddTask("计划今天未完成", clock);
            var todayDone = store.AddTask("计划今天已完成", clock); store.SetCompleted(todayDone, true);
            var overduePending = store.AddTask("之前未完成", clock.AddDays(-1));
            var overdueDoneToday = store.AddTask("今天完成的逾期任务", clock.AddDays(-1)); store.SetCompleted(overdueDoneToday, true);
            overdueDoneToday.CompletedAt = LocalTimestamp(clock.AddMinutes(1));
            var historicalDone = store.AddTask("昨天已完成", clock.AddDays(-1)); store.SetCompleted(historicalDone, true);
            historicalDone.CompletedAt = LocalTimestamp(clock.AddSeconds(-1));
            var legacyDone = store.AddTask("旧版无完成时间", clock.AddDays(-2)); legacyDone.Done = true; legacyDone.CompletedAt = null;
            var invalidDone = store.AddTask("无效完成时间", clock.AddDays(-2)); invalidDone.Done = true; invalidDone.CompletedAt = "not-a-timestamp";
            var tomorrow = store.AddTask("计划明天", clock.AddDays(1));
            var unplanned = store.AddTask("未安排但今天完成", null); store.SetCompleted(unplanned, true);
            store.Save();
            var expectedToday = new[] { todayPending, todayDone, overduePending, overdueDoneToday };
            Check(store.SelectTasks("today").SequenceEqual(expectedToday),
                "today contains planned-today tasks, earlier unfinished tasks and overdue tasks completed on this local date only");
            Check(store.SelectTasks("today").Contains(overdueDoneToday) && !store.SelectTasks("today").Contains(historicalDone),
                "completion timestamps use the machine's local calendar date across the UTC midnight boundary");
            Check(store.SelectTasks("all").Count() == 9 && store.SelectTasks("all").Contains(legacyDone) && store.SelectTasks("all").Contains(invalidDone),
                "historical or unparseable completions remain available in all without data loss");
            using (var card = new WorkboardWindow(store))
            using (var bench = new WorkbenchWindow(store))
            {
                card.SetTaskCompleted(overduePending, true); card.Flush();
                Check(card.CompletedChecks.Any(c => Object.ReferenceEquals(c.Tag, overduePending)) && bench.CompletedChecks.Count == 3,
                    "completing an overdue task immediately keeps it visible in today's completed group in both windows");
                clock = clock.AddDays(1); card.RefreshDay(); bench.RefreshDay();
                Check(store.SelectTasks("today").SequenceEqual(new[] { todayPending, tomorrow })
                    && card.CompletedChecks.Count == 0 && bench.CompletedChecks.Count == 0,
                    "after local midnight yesterday's completions leave today while unfinished tasks retain their original dates");
                Check(store.State.Tasks.Count == 9 && store.SelectTasks("all").Contains(overdueDoneToday),
                    "midnight changes visibility only and preserves all completed records");
                card.RemoveTask(historicalDone); card.UndoRemoveTask();
                Check(store.State.TaskScope == "all" && card.CompletedChecks.Any(c => Object.ReferenceEquals(c.Tag, historicalDone)),
                    "desktop undo reveals a historical completed task in all rather than an empty today group");
                bench.SelectScope("today"); store.Remove(historicalDone); bench.UndoRemove();
                Check(store.State.TaskScope == "all" && store.State.Tasks.Contains(historicalDone) && historicalDone.Done && bench.CompletedChecks.Count == store.State.Tasks.Count(t => t.Done) && bench.CompletedGroup.IsExpanded,
                    "workbench undo reveals a historical completed task in all while preserving its completion");
            }
            Report.AppendLine("MEASURE local timezone=" + TimeZoneInfo.Local.Id + "; today-completion-UTC=" + overdueDoneToday.CompletedAt + "; prior-day-completion-UTC=" + historicalDone.CompletedAt);

            var empty = new WorkboardStore(Path.Combine(Root, "today-empty-with-history"));
            var old = empty.AddTask("昨天已经做完", empty.Today.AddDays(-1)); old.Done = true; old.CompletedAt = LocalTimestamp(empty.Today.AddSeconds(-1)); empty.Save();
            using (var card = new WorkboardWindow(empty))
            using (var bench = new WorkbenchWindow(empty))
            {
                var counter = (TextBlock)card.View.FindName("TaskCount");
                Check(card.CompletedChecks.Count == 0 && bench.CompletedChecks.Count == 0 && counter.Text != "全部完成",
                    "a new empty day with historical completed tasks does not misleadingly claim all of today's work is complete");
            }
        }

        [STAThread]
        private static int Main()
        {
            try
            {
                Forms.Application.EnableVisualStyles(); Forms.Application.SetCompatibleTextRenderingDefault(false);
                Run("memo immediate retry", delegate { MemoRetry(false); });
                Run("memo delayed retry", delegate { MemoRetry(true); });
                for (int kind = 0; kind < 3; kind++) { int selectedKind = kind; Run("undo draft date " + kind, delegate { UndoDraftDate(selectedKind); }); }
                foreach (string scope in new[] { "today", "tomorrow", "later", "all" })
                {
                    string selectedScope = scope;
                    Run("legacy " + scope + " submit card", delegate { LegacyDraft(selectedScope, true); });
                    Run("legacy " + scope + " submit bench", delegate { LegacyDraft(selectedScope, false); });
                }
                Run("today completed scope", TodayCompletion);
                Report.AppendLine("Passed: " + passed + "; Failed: " + failed);
                Report.AppendLine("LIMIT: windows constructed only; no Show, Attach, focus, popup or keyboard automation. Production data never used.");
                return failed == 0 ? 0 : 1;
            }
            finally { File.WriteAllText(Path.Combine(Storage.Folder, "verification-daily-logic-regressions.txt"), Report.ToString(), new UTF8Encoding(false)); }
        }
    }
}
