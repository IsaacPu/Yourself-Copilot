using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;

namespace LocalDesktopNote
{
    internal static class HistoryPanelVerification
    {
        private static readonly StringBuilder Report = new StringBuilder();
        private static int passed, failed;
        private static readonly DateTime Start = new DateTime(2026, 10, 23, 1, 0, 0, DateTimeKind.Utc);

        private static void Check(bool condition, string message)
        {
            Report.AppendLine((condition ? "PASS: " : "FAIL: ") + message); if (condition) passed++; else failed++;
        }
        private static void Run(string name, Action action)
        {
            Report.AppendLine("CASE: " + name); try { action(); } catch (Exception error) { failed++; Report.AppendLine("FAIL: " + error); }
        }
        private sealed class Fixture
        {
            internal readonly WorkboardStore Store;
            internal readonly string Folder;
            internal Fixture(string name)
            {
                Folder = Path.Combine(Storage.Folder, "fixtures", name + "-" + Guid.NewGuid().ToString("N"));
                Store = new WorkboardStore(Folder);
            }
            internal string Snapshot(int index, string memo, string task = "Review outline", bool done = false, string draft = "", string removed = null, string draftPlanDate = "")
            {
                string folder = Path.Combine(Store.HistoryFolder, "2026-10-23"); Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, index.ToString("D5", CultureInfo.InvariantCulture) + ".json");
                var state = new WorkboardState { Ideas = memo, TaskDraft = draft, DraftPlanDate = draftPlanDate };
                if (task != null) state.Tasks.Add(new WorkTask { Id = "stable-task", Text = task, Done = done, PlanDate = "2026-10-23" });
                if (removed != null) state.Removed.Add(new WorkTask { Id = "removed-task", Text = removed, PlanDate = "" });
                LibraryStore.Write(path, state); File.SetLastWriteTimeUtc(path, Start.AddSeconds(index)); return path;
            }
            internal void Paper(string id, string text, string kind, int minute)
            {
                if (Store.State.MemoPapers == null) Store.State.MemoPapers = new List<MemoPaper>();
                Store.State.MemoPapers.Add(new MemoPaper { Id = id, Text = text, Kind = kind, StoredAtUtc = Start.AddMinutes(minute).ToString("o", CultureInfo.InvariantCulture) });
            }
        }
        private static void Click(Button button) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
        private static void Render(HistoryPanel panel, string name, double width, double height)
        {
            panel.View.Measure(new Size(width, height)); panel.View.Arrange(new Rect(0, 0, width, height)); panel.View.UpdateLayout();
            var image = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32); image.Render(panel.View);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using (var stream = File.Create(Path.Combine(Storage.Folder, name + ".png"))) encoder.Save(stream);
            Check(panel.List.ActualHeight > 100 && panel.Preview.ActualWidth > 230 && panel.Preview.ActualHeight > 100, name + " keeps both history list and preview usable");
        }
        private static void CategorizedPapers()
        {
            var f = new Fixture("categorized");
            f.Paper("archive-old", "First page\nOlder plain text", "archive", 1);
            f.Paper("archive-new", "Morning notes\nA small observation.\nA next step.", "archive", 3);
            f.Paper("trash-new", "Discarded idea\nStill recoverable", "trash", 4);
            f.Snapshot(1, "AUTO MEMO ONLY", "TASK ONLY");
            var panel = new HistoryPanel(f.Store, delegate(string text) { return true; }); panel.Load();
            Check(panel.CurrentCategory == "memo" && panel.CurrentFilter == "archive", "default entry is memo / stored papers");
            Check(panel.Entries.Count == 2 && panel.List.Items.Count == 2, "stored paper list excludes trash and automatic snapshots");
            Check(panel.Entries[0].Text.StartsWith("Morning notes") && panel.Entries[1].Text.StartsWith("First page"), "papers are sorted by stored timestamp newest first");
            Check(panel.Entries[0].Title == "Morning notes" && panel.Entries[0].Summary == "A small observation. A next step.", "paper rows use first line and remaining text summary");
            Check(panel.Preview.Text == f.Store.State.MemoPapers[1].Text && !panel.Preview.Text.Contains("TASK ONLY"), "memo preview is exact original text without task sections");
            Check(panel.RestoreButton.Visibility == Visibility.Visible && panel.RestoreButton.IsEnabled, "stored memo offers take-back action");
            Render(panel, "history-stored-papers", 770, 570); Render(panel, "history-stored-papers-narrow", 600, 390);
            panel.List.SelectedIndex = 1; panel.Load();
            Check(panel.List.SelectedIndex == 1 && panel.Preview.Text.StartsWith("First page"), "reload preserves selected paper when it still exists");
            panel.SelectCategory("memo", "trash");
            Check(panel.Entries.Count == 1 && panel.Preview.Text == "Discarded idea\nStill recoverable", "paper bin shows only binned paper and original text");
            Check(panel.Notice.Text.Contains("仍保留") && panel.RestoreButton.IsEnabled, "paper bin explains recoverability and can take back");
            panel.SelectCategory("memo", "versions");
            Check(panel.Entries.Count == 1 && panel.Preview.Text == "AUTO MEMO ONLY", "automatic versions exclude stored papers and tasks");
            panel.SelectCategory("task", "archive");
            Check(panel.CurrentFilter == "versions" && panel.Entries.Count == 1, "task category has its own snapshot projection");
            Check(panel.Preview.Text.Contains("TASK ONLY") && !panel.Preview.Text.Contains("AUTO MEMO ONLY") && !panel.Preview.Text.Contains("Morning notes"), "task preview excludes every memo source");
            Check(panel.RestoreButton.Visibility == Visibility.Collapsed && !panel.RestoreButton.IsEnabled, "task history is read-only with no overwrite action");
            panel.SelectCategory("invalid", "invalid");
            Check(panel.CurrentCategory == "memo" && panel.CurrentFilter == "archive", "invalid navigation safely returns to default paper list");
        }
        private static void ProjectedDeduplication()
        {
            var f = new Fixture("projection-dedup");
            f.Snapshot(180, "Memo A", "TASK ALPHA", false, "DRAFT ALPHA", "REMOVED ALPHA");
            f.Snapshot(360, "Memo B", "TASK ALPHA", false, "DRAFT ALPHA", "REMOVED ALPHA");
            f.Snapshot(540, "Memo B", "TASK ALPHA", true, "DRAFT ALPHA", "REMOVED ALPHA");
            f.Snapshot(720, "Memo B", "TASK ALPHA", true, "DRAFT ALPHA", "REMOVED ALPHA");
            f.Snapshot(900, "Memo A", "TASK ALPHA", true, "DRAFT ALPHA", "REMOVED ALPHA");
            f.Snapshot(1080, "Memo A", "TASK ALPHA", true, "DRAFT BETA", "REMOVED ALPHA");
            f.Snapshot(1260, "Memo A", "TASK ALPHA", true, "DRAFT BETA", "REMOVED BETA");
            var panel = new HistoryPanel(f.Store, null); panel.SelectCategory("memo", "versions");
            Check(panel.Entries.Count == 3 && panel.Entries.Select(e => e.Text).SequenceEqual(new[] { "Memo A", "Memo B", "Memo A" }), "adjacent memo projections merge while a later return to earlier text remains a distinct session");
            Check(panel.Entries.All(e => e.IsMemo && !e.Text.Contains("TASK")), "automatic memo projection never contains task data");
            Check(!panel.RestoreButton.IsEnabled, "memo preview with no controller callback remains safe and read-only");
            panel.SelectCategory("task", "versions");
            Check(panel.Entries.Count == 3, "memo-only edits, unsubmitted drafts and duplicate task snapshots coalesce while status and removed changes remain");
            Check(panel.Entries[0].Text.Contains("REMOVED BETA") && !panel.Entries[0].Text.Contains("DRAFT BETA") && panel.Entries[0].Text.Contains("[已完成] TASK ALPHA"), "task projection previews committed active, completed and removed content without drafts");
            Check(panel.Entries.All(e => !e.Text.Contains("Memo A") && !e.Text.Contains("Memo B")), "task history omits memo text in every row");
            Check(panel.Entries.Last().Text.Contains("[待办] TASK ALPHA"), "old unfinished task status remains readable");
            Render(panel, "history-task-projection", 770, 570);
        }
        private static void TaskDraftNoise()
        {
            var f = new Fixture("task-draft-noise");
            f.Snapshot(1, "第一句记录。", "已保存的任务", false, "看一下郭", null, "2026-10-23");
            f.Snapshot(2, "第一句记录。", "已保存的任务", false, "看一下郭昱hua", null, "2026-10-23");
            f.Snapshot(3, "第一句记录。新增一句。", "已保存的任务", false, "看一下郭昱华的", null, "2026-10-24");
            f.Snapshot(4, "第一句记录。新增一句。", "已保存的任务", false, "看一下郭昱华的资料", null, "2026-10-25");
            var panel = new HistoryPanel(f.Store, null); panel.SelectCategory("task", "versions");
            Check(panel.Entries.Count == 1, "pinyin, single-letter edits, draft plan dates and memo edits never create committed task revisions");
            Check(panel.Entries[0].Text.Contains("已保存的任务") && !panel.Entries[0].Text.Contains("郭") && !panel.Entries[0].Text.Contains("草稿"), "task preview contains no unsubmitted draft text or draft headings");
            var draftsOnly = new Fixture("task-only-drafts");
            draftsOnly.Snapshot(1, "", null, false, "尚未保存的任务", null, "2026-10-24");
            draftsOnly.Snapshot(2, "", null, false, "尚未保存的任务ping", null, "2026-10-25");
            var emptyPanel = new HistoryPanel(draftsOnly.Store, null); emptyPanel.SelectCategory("task", "versions");
            Check(emptyPanel.Entries.Count == 1 && emptyPanel.Entries[0].Summary == "当时没有任务" && !emptyPanel.Preview.Text.Contains("尚未保存"), "draft-only snapshots have a neutral committed-task summary and no draft leakage");
        }
        private static void SentenceAndParagraphVersions()
        {
            var f = new Fixture("sentence-versions");
            string first = "先确认用户要解决的问题。";
            string second = first + "\n然后梳理实际工作流程。";
            f.Snapshot(1, first);
            f.Snapshot(3, first + "\n然后梳理shi");
            f.Snapshot(5, first + "\n然后梳理实际工作流");
            f.Snapshot(7, second);
            f.Snapshot(9, first + "\n然后梳理真实工作流程。");
            var panel = new HistoryPanel(f.Store, null); panel.SelectCategory("memo", "versions");
            Check(panel.Entries.Count == 2 && panel.Entries[0].Text == first + "\n然后梳理真实工作流程。" && panel.Entries[1].Text == first, "one complete sentence remains a version while the next sentence's pinyin and small corrections merge into its latest text");
            Check(panel.Entries.All(e => !e.Text.EndsWith("shi", StringComparison.Ordinal) && !e.Text.EndsWith("工作流", StringComparison.Ordinal)), "automatic memo list hides incomplete typing states superseded by the same completed sentence");
            Check(Directory.GetFiles(f.Store.HistoryFolder, "*.json", SearchOption.AllDirectories).Length == 5, "memo display merging preserves every original snapshot file");
            Render(panel, "history-sentence-versions", 770, 570);

            var boundaries = new Fixture("memo-session-boundaries");
            string old = "确认今天的问题。", revised = "确认明天的问题。";
            boundaries.Snapshot(1, old); boundaries.Snapshot(122, revised);
            var boundaryPanel = new HistoryPanel(boundaries.Store, null); boundaryPanel.SelectCategory("memo", "versions");
            Check(boundaryPanel.Entries.Count == 2, "small edits more than two minutes apart remain separately recoverable");
            var duplicateGap = new Fixture("memo-duplicate-gap");
            duplicateGap.Snapshot(1, old); duplicateGap.Snapshot(2, revised); duplicateGap.Snapshot(600, revised);
            var duplicateGapPanel = new HistoryPanel(duplicateGap.Store, null); duplicateGapPanel.SelectCategory("memo", "versions");
            Check(duplicateGapPanel.Entries.Count == 2 && duplicateGapPanel.Entries[1].Text == old, "duplicate raw snapshots cannot bridge a long interval and merge an older different memo");
            var dates = new Fixture("memo-date-boundaries");
            string priorDay = dates.Snapshot(1, old), nextDay = dates.Snapshot(2, revised);
            File.SetLastWriteTimeUtc(priorDay, new DateTime(2026, 10, 23, 15, 59, 50, DateTimeKind.Utc));
            File.SetLastWriteTimeUtc(nextDay, new DateTime(2026, 10, 23, 16, 0, 10, DateTimeKind.Utc));
            var datePanel = new HistoryPanel(dates.Store, null); datePanel.SelectCategory("memo", "versions");
            Check(datePanel.Entries.Count == 2, "nearby small edits on different local dates remain separate versions");
            var separated = new Fixture("memo-blank-boundaries");
            separated.Snapshot(1, old); separated.Snapshot(2, ""); separated.Snapshot(3, revised);
            var separatedPanel = new HistoryPanel(separated.Store, null); separatedPanel.SelectCategory("memo", "versions");
            Check(separatedPanel.Entries.Count == 2, "blank memo state prevents fuzzy merging across pages");

            var paragraphs = new Fixture("memo-paragraph-boundaries");
            string paragraph = "先整理实际工作流程\n";
            paragraphs.Snapshot(1, paragraph); paragraphs.Snapshot(2, paragraph + "再核对每个人负责的步骤\n");
            var paragraphPanel = new HistoryPanel(paragraphs.Store, null); paragraphPanel.SelectCategory("memo", "versions");
            Check(paragraphPanel.Entries.Count == 2 && paragraphPanel.Entries[1].Text == paragraph, "a completed paragraph remains a version when a new paragraph is added without sentence punctuation");
            var deletion = new Fixture("memo-substantial-deletion");
            string substantial = "工作流程" + new string('甲', 76);
            deletion.Snapshot(1, substantial); deletion.Snapshot(2, substantial.Substring(0, 48) + "。");
            var deletionPanel = new HistoryPanel(deletion.Store, null); deletionPanel.SelectCategory("memo", "versions");
            Check(deletionPanel.Entries.Count == 2 && deletionPanel.Entries[1].Text == substantial, "completing punctuation never merges away a substantial deletion recovery version");
        }
        private static void DedupBeforeLimit()
        {
            var f = new Fixture("limit-after-dedup");
            for (int index = 0; index < 305; index++) f.Snapshot(index * 180, "Memo " + index, "Task " + index);
            for (int index = 305; index < 620; index++) f.Snapshot(index * 180, "Memo 304", "Task 304");
            var panel = new HistoryPanel(f.Store, null); panel.SelectCategory("task", "versions");
            Check(panel.Entries.Count == 300 && panel.Entries.First().Title == "1 项待办 · 0 项已完成", "task projected history retains 300 distinct changes after a long duplicate burst");
            Check(panel.Entries.First().Text.Contains("Task 304") && panel.Entries.Last().Text.Contains("Task 5"), "task deduplication scans beyond the newest 300 raw files");
            panel.SelectCategory("memo", "versions");
            Check(panel.Entries.Count == 300 && panel.Entries.First().Text == "Memo 304" && panel.Entries.Last().Text == "Memo 5", "memo deduplication also occurs before the 300 entry display limit");
            Check(Directory.GetFiles(f.Store.HistoryFolder, "*.json", SearchOption.AllDirectories).Length == 620, "display limits never delete or rewrite old snapshots");

            // Keep the original 620-file expectations above intact. Corrupt
            // one file outside that visible range only after verifying them;
            // parsing beyond the limit would expose an extra skipped record.
            string oldest = Path.Combine(f.Store.HistoryFolder, "2026-10-23", "00000.json");
            DateTime oldestTime = File.GetLastWriteTimeUtc(oldest);
            File.WriteAllText(oldest, "{ old invalid json outside visible range"); File.SetLastWriteTimeUtc(oldest, oldestTime);
            string newest = f.Snapshot(620 * 180, "placeholder", "placeholder");
            DateTime newestTime = File.GetLastWriteTimeUtc(newest);
            File.WriteAllText(newest, "{ new invalid json before visible range"); File.SetLastWriteTimeUtc(newest, newestTime);
            byte[] oldestBytes = File.ReadAllBytes(oldest), newestBytes = File.ReadAllBytes(newest);
            panel.SelectCategory("task", "versions");
            Check(panel.Entries.Count == 300 && panel.Entries.First().Text.Contains("Task 304") && panel.Entries.Last().Text.Contains("Task 5") && panel.Notice.Text.Contains("已跳过 1 条"), "task scan skips new corruption but stops parsing at 300 valid projected revisions before old corruption");
            panel.SelectCategory("memo", "versions");
            Check(panel.Entries.Count == 300 && panel.Entries.First().Text == "Memo 304" && panel.Entries.Last().Text == "Memo 5" && panel.Notice.Text.Contains("已跳过 1 条"), "memo scan also stops parsing at 300 coalesced versions while skipping invalid files encountered before the limit");
            Check(File.ReadAllBytes(oldest).SequenceEqual(oldestBytes) && File.ReadAllBytes(newest).SequenceEqual(newestBytes) && Directory.GetFiles(f.Store.HistoryFolder, "*.json", SearchOption.AllDirectories).Length == 621, "bounded history browsing leaves all old and newly added fixture files unchanged");
        }
        private static void BrokenEntriesAndEmptySources()
        {
            var f = new Fixture("broken"); f.Snapshot(1, "Good memo", "Good task"); string bad = f.Snapshot(2, "Placeholder");
            File.WriteAllText(bad, "{ invalid json"); byte[] original = File.ReadAllBytes(bad);
            string unsupported = f.Snapshot(3, "Unknown"); var unknown = new WorkboardState { Version = 99 }; LibraryStore.Write(unsupported, unknown);
            var panel = new HistoryPanel(f.Store, delegate(string text) { return true; }); panel.SelectCategory("memo", "versions");
            Check(panel.Entries.Count == 1 && panel.Preview.Text == "Good memo", "corrupt and unsupported snapshots do not hide a good memo version");
            Check(panel.Notice.Text.Contains("已跳过 2 条") && panel.Notice.Text.Contains("原文件仍保留"), "skipped snapshot count is visible without dumping exception details");
            Check(File.ReadAllBytes(bad).SequenceEqual(original) && File.Exists(unsupported), "reading corrupted history preserves its exact files");
            panel.SelectCategory("task", "versions");
            Check(panel.Entries.Count == 1 && panel.Preview.Text.Contains("Good task") && panel.Notice.Text.Contains("已跳过 2 条"), "broken snapshots are isolated in task category too");
            f.Paper("good", "Stored memo", "archive", 1); f.Paper("bad-time", "Bad time", "archive", 2); f.Store.State.MemoPapers[1].StoredAtUtc = "not a date"; f.Store.State.MemoPapers.Add(null);
            panel.SelectCategory("memo", "archive");
            Check(panel.Entries.Count == 1 && panel.Preview.Text == "Stored memo" && panel.Notice.Text.Contains("已跳过 2 条"), "malformed stored paper metadata is skipped without losing valid papers");
            var emptyFixture = new Fixture("empty"); var emptyPanel = new HistoryPanel(emptyFixture.Store, null); emptyPanel.Load();
            Check(emptyPanel.List.Items.Count == 0 && emptyPanel.Preview.Text == "" && !emptyPanel.RestoreButton.IsEnabled, "old state with null MemoPapers shows a safe empty default");
            emptyPanel.SelectCategory("memo", "trash"); Check(emptyPanel.Notice.Text.Contains("可随时取回"), "empty paper bin still explains non-destructive behavior");
            emptyPanel.SelectCategory("task", "versions"); Check(emptyPanel.List.Items.Count == 0 && emptyPanel.Preview.Text == "", "missing history directory is a safe empty task source");
            var blankFixture = new Fixture("blank-separator"); blankFixture.Snapshot(1, "Same page"); blankFixture.Snapshot(2, ""); blankFixture.Snapshot(3, "Same page");
            var blankPanel = new HistoryPanel(blankFixture.Store, null); blankPanel.SelectCategory("memo", "versions");
            Check(blankPanel.Entries.Count == 2 && blankPanel.Entries.All(e => e.Text == "Same page"), "empty memo snapshots separate sessions but are not offered as recoverable paper");
        }
        private static void RestoreCallbacks()
        {
            var f = new Fixture("restore-callback"); f.Paper("recover", "Recovered page\nExact content", "archive", 1);
            string received = null; bool accept = false; int calls = 0;
            var panel = new HistoryPanel(f.Store, delegate(string text) { received = text; calls++; return accept; }); panel.Load();
            Click(panel.RestoreButton);
            Check(calls == 1 && received == "Recovered page\nExact content" && panel.List.Items.Count == 1, "take-back calls the controller with exact original text and retains the list on rejection");
            Check(panel.Notice.Text.Contains("暂时未能取回"), "failed callback leaves a clear recoverable state");
            f.Store.State.Ideas = received; Click(panel.RestoreButton);
            Check(calls == 2 && panel.Notice.Text.Contains("已经是当前"), "same-current callback rejection shows no-op feedback");
            accept = true; f.Paper("new", "Newly stored page", "archive", 2); Click(panel.RestoreButton);
            Check(calls == 3 && panel.List.Items.Count == 2 && panel.Notice.Text.Contains("已取回"), "successful take-back refreshes history and reports completion");
            panel.List.SelectedIndex = 0;
            Check(!panel.Notice.Text.Contains("已取回") && panel.Notice.Text.Contains("可随时取回"), "selecting another entry replaces transient feedback with the browsing description");
            var throwing = new HistoryPanel(f.Store, delegate(string text) { throw new IOException("isolated simulated save failure"); }); throwing.Load(); Click(throwing.RestoreButton);
            Check(throwing.List.Items.Count == 2 && throwing.Notice.Text.Contains("均已保留"), "controller exception preserves browsing and explains that nothing was removed");
            panel.SelectCategory("task", "versions"); Click(panel.RestoreButton);
            Check(calls == 3, "task category never invokes memo restore callback, even if click is raised programmatically");
        }
        private static void RealStoreTakeBack()
        {
            var f = new Fixture("real-take-back"); f.Store.State.Ideas = "Current unsorted observation"; f.Store.State.TaskDraft = "DRAFT KEEP"; f.Store.State.Tasks.Add(new WorkTask { Id = "keep-task", Text = "TASK KEEP" }); f.Store.Save();
            f.Paper("recover", "Archived observation\nSecond line", "archive", 1);
            var panel = new HistoryPanel(f.Store, f.Store.OpenMemoText); panel.Load(); Click(panel.RestoreButton);
            Check(f.Store.State.Ideas == "Archived observation\nSecond line" && LibraryStore.Read<WorkboardState>(f.Store.PathName).Ideas == f.Store.State.Ideas, "real take-back controller persists the selected paper as current memo");
            Check(f.Store.State.MemoPapers.Count == 2 && f.Store.State.MemoPapers.Any(p => p.Text == "Current unsorted observation" && p.Kind == "archive"), "real take-back protects the prior current paper in archive");
            Check(f.Store.State.MemoPapers.Any(p => p.Id == "recover") && panel.List.Items.Count == 2, "take-back is a copy and leaves original stored paper available");
            Check(f.Store.State.TaskDraft == "DRAFT KEEP" && f.Store.State.Tasks.Single().Text == "TASK KEEP", "memo take-back leaves task and draft content unchanged");
            int count = f.Store.State.MemoPapers.Count; Click(panel.RestoreButton);
            Check(f.Store.State.MemoPapers.Count == count && panel.Notice.Text.Contains("已经是当前"), "second take-back of the same selected page is a nonduplicating no-op");
        }
        [STAThread]
        private static int Main()
        {
            try
            {
                Forms.Application.EnableVisualStyles(); Forms.Application.SetCompatibleTextRenderingDefault(false);
                Run("separate paper and task categories", CategorizedPapers);
                Run("independent projected adjacent deduplication", ProjectedDeduplication);
                Run("draft typing never becomes committed task history", TaskDraftNoise);
                Run("sentence versions and editing-session boundaries", SentenceAndParagraphVersions);
                Run("deduplication before display limit", DedupBeforeLimit);
                Run("broken and absent sources", BrokenEntriesAndEmptySources);
                Run("controller success rejection and failure", RestoreCallbacks);
                Run("real store protected take-back", RealStoreTakeBack);
                Report.AppendLine("Passed: " + passed + "; Failed: " + failed);
                Report.AppendLine("LIMIT: WPF views and routed Click events are measured/rendered while hidden, using isolated neutral fixture files. No production user files, app processes, native mouse input or registry changes were used.");
                return failed == 0 ? 0 : 1;
            }
            finally { File.WriteAllText(Path.Combine(Storage.Folder, "verification-history-panel.txt"), Report.ToString(), new UTF8Encoding(false)); }
        }
    }
}
