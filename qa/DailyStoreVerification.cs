using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;

namespace LocalDesktopNote
{
    // Store-only runner deliberately excludes the desktop application and user data.
    internal static class LibraryStore
    {
        internal static T Read<T>(string path)
        {
            using (var stream = File.OpenRead(path)) return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
        }
    }

    internal static class DailyStoreVerification
    {
        private static readonly StringBuilder Report = new StringBuilder();
        private static int assertions;
        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("FAIL: " + message);
            assertions++;
            Report.AppendLine("PASS: " + message);
        }
        private static string[] History(WorkboardStore store)
        {
            return Directory.Exists(store.HistoryFolder) ? Directory.GetFiles(store.HistoryFolder, "*.json", SearchOption.AllDirectories) : new string[0];
        }
        private static void Write(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text, new UTF8Encoding(false));
        }
        private static int Main()
        {
            string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
            try
            {
                var store = new WorkboardStore(Path.Combine(root, "daily"));
                DateTime clock = new DateTime(2026, 10, 20, 23, 59, 0);
                store.TodayProvider = delegate { return clock; };
                Check(store.Today == new DateTime(2026, 10, 20), "Today excludes the clock time");
                Check(store.State.ShowCardOnStartup && store.State.TaskScope == "today", "new state defaults to quiet visible startup and today scope");
                var today = store.AddTask("  今日任务  ");
                var tomorrow = store.AddTask("明日任务", clock.AddDays(1));
                var yesterday = store.AddTask("昨天未完成", clock.AddDays(-1));
                var later = store.AddTask("未来任务", clock.AddDays(2));
                var unplanned = store.AddTask("未安排", null);
                var invalid = store.AddTask("旧无效日期", null); invalid.PlanDate = "2026-02-30";
                Check(today.PlanDate == "2026-10-20" && today.Text == "今日任务", "new task defaults to the specific current plan date");
                Check(unplanned.PlanDate == null && store.AddTask("   ", null) == null, "explicit null remains unplanned and empty tasks are rejected");
                Check(store.SelectTasks("today").SequenceEqual(new[] { today, yesterday }), "today includes unfinished earlier tasks without reordering");
                Check(store.SelectTasks("tomorrow").SequenceEqual(new[] { tomorrow }), "tomorrow selects the exact next calendar day");
                Check(store.SelectTasks("later").SequenceEqual(new[] { later, unplanned, invalid }), "later includes future, unplanned and invalid-date tasks without data loss");
                Check(store.SelectTasks("all").SequenceEqual(store.State.Tasks), "all preserves every task and original sequence");
                string todayId = today.Id;
                store.SetCompleted(today, true); string completedAt = today.CompletedAt;
                Check(today.Done && !String.IsNullOrEmpty(completedAt) && today.PlanDate == "2026-10-20", "completing writes a timestamp without changing plan date");
                store.SetCompleted(today, true);
                Check(today.CompletedAt == completedAt, "repeated completion does not rewrite completion timestamp");
                today.CompletedAt = DateTime.SpecifyKind(clock, DateTimeKind.Local).ToUniversalTime().ToString("o");
                clock = clock.AddDays(1);
                Check(store.SelectTasks("today").SequenceEqual(new[] { tomorrow, yesterday }) && store.SelectTasks("all").Contains(today), "midnight advances tomorrow and retains unfinished earlier tasks while keeping prior completions in all");
                store.SetCompleted(today, false);
                Check(!today.Done && today.CompletedAt == null && today.PlanDate == "2026-10-20", "restoring a completed task retains its original plan date");
                store.SetPlanDate(today, null);
                Check(today.Id == todayId && store.State.Tasks[0] == today && today.PlanDate == null, "rescheduling preserves task identity and source position");
                store.SetPlanDate(today, new DateTime(2026, 12, 31, 18, 0, 0));
                clock = new DateTime(2026, 12, 30);
                Check(store.SelectTasks("tomorrow").Single() == today, "December 31 is correctly tomorrow on December 30");
                store.SetPlanDate(today, new DateTime(2027, 1, 1)); clock = new DateTime(2026, 12, 31);
                Check(store.SelectTasks("tomorrow").Single() == today, "tomorrow crosses a year boundary");
                store.SetPlanDate(today, new DateTime(2028, 3, 1)); clock = new DateTime(2028, 2, 29);
                Check(store.SelectTasks("tomorrow").Single() == today, "tomorrow crosses leap-day month boundary");
                store.Remove(today); var restored = store.Undo();
                Check(restored == today && store.State.Tasks[0] == today && today.PlanDate == "2028-03-01", "undo preserves plan date and original position");
                store.State.Ideas = "保留备忘"; store.State.TaskDraft = "保留草稿"; store.State.DraftPlanDate = "2028-03-02";
                store.State.X = 33; store.State.Y = 44; store.State.Width = 460; store.State.Height = 350;
                int changes = 0; store.Changed += delegate { changes++; store.NotifyChanged(); };
                store.Save();
                Check(changes == 1 && History(store).Length == 0, "first save broadcasts once and does not invent a previous history version");
                byte[] first = File.ReadAllBytes(store.PathName);
                store.Save();
                Check(changes == 2 && History(store).Length == 0 && !File.Exists(store.PathName + ".bak"), "unchanged save broadcasts but does not rotate backups or create history");
                store.State.Ideas = "修改后的备忘"; store.Save();
                Check(History(store).Length == 1 && File.ReadAllBytes(store.PathName + ".bak").SequenceEqual(first), "changed save snapshots the previous contents and rotates the last backup");
                Check(File.ReadAllBytes(History(store)[0]).SequenceEqual(first), "history retains exact prior persisted bytes");
                var historic = store.ReadHistorySnapshot(History(store)[0]); historic.Ideas = "只读副本";
                Check(store.State.Ideas == "修改后的备忘" && LibraryStore.Read<WorkboardState>(History(store)[0]).Ideas == "保留备忘", "reading a history snapshot is detached from live and archived data");
                bool outsideRejected = false;
                try { store.ReadHistorySnapshot(store.PathName); } catch (ArgumentException) { outsideRejected = true; }
                Check(outsideRejected, "history reader rejects paths outside the history directory");
                store.State.Ideas = "连续修改一"; store.Save(); store.State.Ideas = "连续修改二"; store.Save();
                Check(History(store).Length == 3 && History(store).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 3, "rapid saves retain distinct history files without timestamp collisions");
                var reopened = new WorkboardStore(Path.GetDirectoryName(store.PathName));
                Check(reopened.State.Tasks.Select(t => t.Id).SequenceEqual(store.State.Tasks.Select(t => t.Id)) && reopened.State.Tasks[0].PlanDate == "2028-03-01", "restart preserves task identities, order and dates");
                Check(reopened.State.TaskDraft == "保留草稿" && reopened.State.DraftPlanDate == "2028-03-02" && reopened.State.X == 33 && reopened.State.Height == 350, "restart preserves draft date, draft text and geometry");
                Check(!String.IsNullOrEmpty(reopened.Notice) && reopened.State.Tasks.Last().PlanDate == "2026-02-30", "invalid plan date is retained and reported after loading");

                string legacyPath = Path.Combine(root, "legacy", "workboard.json");
                Write(legacyPath, "{\"Version\":1,\"Ideas\":\"legacy memo\",\"TaskDraft\":\"draft\",\"Tasks\":[{\"Id\":\"legacy-a\",\"Text\":\"old\",\"Done\":true}],\"Removed\":[],\"X\":4,\"Y\":5,\"Width\":450,\"Height\":340,\"Hidden\":true}");
                var legacy = new WorkboardStore(Path.GetDirectoryName(legacyPath));
                Check(legacy.State.ShowCardOnStartup && legacy.State.TaskScope == "today", "legacy JSON receives additive startup and scope defaults");
                Check(legacy.State.Tasks.Single().PlanDate == null && legacy.State.Tasks.Single().Id == "legacy-a" && legacy.State.Tasks.Single().Done, "legacy task remains unplanned with its completion and identity intact");
                Check(legacy.SelectTasks("later").Count() == 1 && legacy.SelectTasks("today").Count() == 0, "legacy unplanned task stays visible in its correct section");
                legacy.Save();
                Check(History(legacy).Length == 1 && legacy.State.DraftPlanDate == WorkboardStore.FormatPlanDate(legacy.Today) && File.ReadAllText(legacy.PathName + ".bak").Contains("legacy memo"), "legacy draft-date migration retains the pre-upgrade backup and exact prior content revision");
                legacy.State.ShowCardOnStartup = false; legacy.State.TaskScope = "all"; legacy.Save();
                var falseSetting = new WorkboardStore(Path.GetDirectoryName(legacyPath));
                Check(!falseSetting.State.ShowCardOnStartup && falseSetting.State.TaskScope == "all", "explicit startup-off and scope preferences survive restart");

                var failure = new WorkboardStore(Path.Combine(root, "history-failure"));
                failure.State.Ideas = "原始内容"; failure.Save(); byte[] original = File.ReadAllBytes(failure.PathName);
                Write(Path.Combine(Path.GetDirectoryName(failure.PathName), "history"), "blocking file");
                failure.State.Ideas = "新内容"; failure.Save();
                Check(!String.IsNullOrEmpty(failure.HistoryNotice) && LibraryStore.Read<WorkboardState>(failure.PathName).Ideas == "新内容", "history-directory failure reports the limitation while saving current data");
                Check(File.ReadAllBytes(failure.PathName + ".bak").SequenceEqual(original), "history failure does not destroy the previous backup");
                byte[] current = File.ReadAllBytes(failure.PathName); byte[] backup = File.ReadAllBytes(failure.PathName + ".bak");
                int failedEvents = 0; failure.Changed += delegate { failedEvents++; };
                failure.State.Ideas = "写入失败的修改"; bool saveFailed = false;
                using (var locked = new FileStream(failure.PathName, FileMode.Open, FileAccess.Read, FileShare.Read))
                    try { failure.Save(); } catch (IOException) { saveFailed = true; }
                Check(saveFailed && failedEvents == 0 && File.ReadAllBytes(failure.PathName).SequenceEqual(current), "failed atomic replacement leaves current data unchanged and does not broadcast success");
                Check(File.ReadAllBytes(failure.PathName + ".bak").SequenceEqual(backup) && !File.Exists(failure.PathName + ".tmp"), "failed save retains the backup and cleans its temporary file");

                var recovery = new WorkboardStore(Path.Combine(root, "recovery"));
                recovery.State.Ideas = "可恢复版本"; recovery.Save(); recovery.State.Ideas = "后来版本"; recovery.Save();
                Write(recovery.PathName, "broken");
                var recovered = new WorkboardStore(Path.GetDirectoryName(recovery.PathName));
                Check(recovered.State.Ideas == "可恢复版本" && !String.IsNullOrEmpty(recovered.Notice), "corrupt primary data recovers the last valid backup with a notice");
                Check(Directory.GetFiles(Path.GetDirectoryName(recovery.PathName), "workboard-recovery-*.json").Length == 1, "corrupt source is retained as a recovery file");
                recovered.State.Ideas = "恢复后保存"; recovered.Save();
                Check(LibraryStore.Read<WorkboardState>(recovered.PathName).Ideas == "恢复后保存" && LibraryStore.Read<WorkboardState>(recovered.PathName + ".bak").Ideas == "可恢复版本", "saving recovery keeps the known-good backup intact");

                var runtimeCorrupt = new WorkboardStore(Path.Combine(root, "runtime-corrupt-no-backup"));
                runtimeCorrupt.State.Ideas = "运行中备忘"; runtimeCorrupt.AddTask("运行中任务"); runtimeCorrupt.Save();
                Write(runtimeCorrupt.PathName, "runtime broken"); runtimeCorrupt.Save();
                var runtimeReopened = new WorkboardStore(Path.GetDirectoryName(runtimeCorrupt.PathName));
                Check(runtimeReopened.State.Ideas == "运行中备忘" && runtimeReopened.State.Tasks.Single().Text == "运行中任务", "unchanged save repairs runtime-corrupt primary without requiring a backup");
                Check(Directory.GetFiles(Path.GetDirectoryName(runtimeCorrupt.PathName), "workboard-recovery-*.json").Any(p => File.ReadAllText(p) == "runtime broken"), "runtime-corrupt source is preserved before repair");

                var runtimeBackup = new WorkboardStore(Path.Combine(root, "runtime-corrupt-with-backup"));
                runtimeBackup.State.Ideas = "有效备份"; runtimeBackup.Save(); runtimeBackup.State.Ideas = "有效当前"; runtimeBackup.Save();
                byte[] knownBackup = File.ReadAllBytes(runtimeBackup.PathName + ".bak");
                Write(runtimeBackup.PathName, "second broken"); runtimeBackup.Save();
                Check(LibraryStore.Read<WorkboardState>(runtimeBackup.PathName).Ideas == "有效当前" && File.ReadAllBytes(runtimeBackup.PathName + ".bak").SequenceEqual(knownBackup), "runtime repair protects the known-good backup from corrupt primary contents");

                string onlyBrokenBackupFolder = Path.Combine(root, "only-broken-backup");
                Write(Path.Combine(onlyBrokenBackupFolder, "workboard.json.bak"), "broken backup");
                var onlyBrokenBackup = new WorkboardStore(onlyBrokenBackupFolder);
                Check(!String.IsNullOrEmpty(onlyBrokenBackup.Notice) && Directory.GetFiles(onlyBrokenBackupFolder, "workboard-recovery-*.json").Length == 1, "missing primary with corrupt backup reports unloaded data and preserves the broken backup");

                var contentOnly = new WorkboardStore(Path.Combine(root, "content-only-history"));
                var contentTask = contentOnly.AddTask("稳定内容"); contentOnly.State.Ideas = "稳定备忘"; contentOnly.Save();
                contentOnly.State.X = 100; contentOnly.State.Y = 200; contentOnly.State.Width = 500; contentOnly.State.Height = 400;
                contentOnly.State.Hidden = true; contentOnly.State.Locked = true; contentOnly.State.Positioned = true;
                contentOnly.State.CompletedExpanded = true; contentOnly.State.TaskScope = "all"; contentOnly.State.ShowCardOnStartup = false; contentOnly.Save();
                Check(History(contentOnly).Length == 0 && LibraryStore.Read<WorkboardState>(contentOnly.PathName).X == 100, "geometry and display preferences save normally without creating content history");
                contentOnly.State.TaskDraft = "新增草稿"; contentOnly.Save();
                Check(History(contentOnly).Length == 1 && contentOnly.ReadHistorySnapshot(History(contentOnly)[0]).TaskDraft == "", "draft text changes create an exact previous-content revision");
                contentOnly.State.DraftPlanDate = "2026-10-21"; contentOnly.Save();
                contentOnly.SetPlanDate(contentTask, contentOnly.Today.AddDays(1)); contentOnly.Save();
                contentOnly.SetCompleted(contentTask, true); contentOnly.Save();
                contentOnly.Remove(contentTask); contentOnly.Save(); contentOnly.Undo(); contentOnly.Save();
                Check(History(contentOnly).Length == 6, "draft date, planned date, completion, removal and undo each retain prior content");
                Report.AppendLine("Assertions: " + assertions);
                return 0;
            }
            catch (Exception error) { Report.AppendLine(error.ToString()); return 1; }
            finally { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "verification-daily-store.txt"), Report.ToString(), new UTF8Encoding(false)); }
        }
    }
}
