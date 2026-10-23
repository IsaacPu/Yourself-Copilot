using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;

namespace LocalDesktopNote
{
    internal static class MemoPaperStoreVerification
    {
        private static readonly StringBuilder Report = new StringBuilder();
        private static int assertions;

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("FAIL: " + message);
            assertions++;
            Report.AppendLine("PASS: " + message);
        }

        private sealed class Fixture
        {
            internal readonly string Folder;
            internal WorkboardStore Store;
            internal readonly DateTime Clock = new DateTime(2026, 10, 23, 8, 0, 0, DateTimeKind.Utc);

            internal Fixture(string name, string text)
            {
                Folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "memo-paper-data", name + "-" + Guid.NewGuid().ToString("N"));
                Store = Open(); Store.State.Ideas = text; Store.Save(); Store = Open();
            }

            internal WorkboardStore Open()
            {
                var store = new WorkboardStore(Folder);
                store.UtcNowProvider = delegate { return Clock; };
                store.TodayProvider = delegate { return Clock.Date; };
                return store;
            }

            internal WorkboardState Persisted { get { return LibraryStore.Read<WorkboardState>(Store.PathName); } }
            internal int PaperCount { get { return Store.State.MemoPapers == null ? 0 : Store.State.MemoPapers.Count; } }
            internal string[] History { get { return Directory.Exists(Store.HistoryFolder) ? Directory.GetFiles(Store.HistoryFolder, "*.json", SearchOption.AllDirectories) : new string[0]; } }
        }

        private static bool FailsIo(Action action)
        {
            try { action(); }
            catch (IOException) { return true; }
            catch (UnauthorizedAccessException) { return true; }
            return false;
        }

        private static void EmptyAndExpectedText()
        {
            var f = new Fixture("empty", " \r\n\t");
            byte[] before = File.ReadAllBytes(f.Store.PathName);
            long generation = f.Store.MemoPaperGeneration;
            int replaced = 0; f.Store.MemoReplaced += delegate { replaced++; };
            Check(f.Store.StoreCurrentMemo("archive") == null && f.PaperCount == 0, "whitespace does not create a stored paper");
            Check(before.SequenceEqual(File.ReadAllBytes(f.Store.PathName)) && replaced == 0, "empty storage is a disk and notification no-op");
            bool badKind = false;
            try { f.Store.StoreCurrentMemo("delete"); } catch (ArgumentException) { badKind = true; }
            Check(badKind && f.PaperCount == 0, "unsupported storage kinds are rejected without mutation");
            f.Store.State.Ideas = "拖动开始后，又输入的新内容";
            bool mismatch = false;
            try { f.Store.StoreCurrentMemo("trash", "拖动开始时的旧内容"); } catch (InvalidOperationException) { mismatch = true; }
            Check(mismatch && f.Store.State.Ideas == "拖动开始后，又输入的新内容" && f.PaperCount == 0, "a stale drag payload cannot discard newly edited text");
            Check(before.SequenceEqual(File.ReadAllBytes(f.Store.PathName)) && replaced == 0, "rejected stale text cannot touch persisted data or emit replacement success");
            Check(f.Store.MemoPaperGeneration == generation, "empty or rejected paper actions do not change the active sheet identity");
        }

        private static void StoreAndRestart(string kind)
        {
            string original = "  原样收起\r\n第二行与🙂符号  ";
            var f = new Fixture(kind, original);
            long generation = f.Store.MemoPaperGeneration;
            int replaced = 0;
            bool committedWhenNotified = false;
            f.Store.MemoReplaced += delegate
            {
                replaced++;
                committedWhenNotified = f.Persisted.Ideas == "" && f.Persisted.MemoPapers.Count == 1 && f.Store.MemoPaperGeneration == generation + 1;
            };
            var paper = f.Store.StoreCurrentMemo(kind, original);
            Check(committedWhenNotified, kind + " replacement notification observes committed state");
            Check(paper != null && paper.Text == original && paper.Kind == kind, kind + " retains exact text and its storage kind");
            Check(!String.IsNullOrEmpty(paper.Id) && paper.StoredAtUtc == f.Clock.ToString("o"), kind + " records stable identity and UTC time");
            Check(f.Store.State.Ideas == "" && f.PaperCount == 1 && replaced == 1, kind + " clears current text only after storing one paper");
            byte[] saved = File.ReadAllBytes(f.Store.PathName);
            Check(f.Store.StoreCurrentMemo(kind) == null && replaced == 1 && f.PaperCount == 1, kind + " double activation after completion cannot create a duplicate");
            Check(saved.SequenceEqual(File.ReadAllBytes(f.Store.PathName)), kind + " repeated activation does not rewrite current data");
            Check(f.Store.MemoPaperGeneration == generation + 1, kind + " successful replacement advances identity once while repeated activation leaves it unchanged");
            f.Store = f.Open();
            Check(f.Store.State.Ideas == "" && f.PaperCount == 1 && f.Store.State.MemoPapers[0].Id == paper.Id, kind + " survives restart with the same paper identity");
            f.Store.State.Ideas = "下一张随手记"; f.Store.Save();
            Check(f.Persisted.MemoPapers.Count == 1 && f.Persisted.MemoPapers[0].Text == original, kind + " ordinary autosave preserves the stored collection");
        }

        private static void PrimaryWriteFailure()
        {
            var f = new Fixture("primary-failure", "已经落盘的原文");
            long generation = f.Store.MemoPaperGeneration;
            f.Store.State.Ideas = "还没来得及自动保存的新原文";
            byte[] current = File.ReadAllBytes(f.Store.PathName);
            int replaced = 0, saved = 0; f.Store.MemoReplaced += delegate { replaced++; }; f.Store.Saved += delegate { saved++; };
            bool failed;
            using (var held = new FileStream(f.Store.PathName, FileMode.Open, FileAccess.Read, FileShare.Read))
                failed = FailsIo(delegate { f.Store.StoreCurrentMemo("trash"); });
            Check(failed && f.Store.State.Ideas == "还没来得及自动保存的新原文" && f.PaperCount == 0, "failed replacement restores unsaved editor text and optional collection");
            Check(current.SequenceEqual(File.ReadAllBytes(f.Store.PathName)) && !File.Exists(f.Store.PathName + ".tmp"), "failed replacement retains exact primary bytes and removes its temporary file");
            Check(replaced == 0 && saved == 0, "failed replacement emits no successful persistence or memo replacement");
            Check(f.Store.MemoPaperGeneration == generation, "failed primary replacement retains the current sheet identity");
            var result = f.Store.StoreCurrentMemo("trash");
            Check(result.Text == "还没来得及自动保存的新原文" && f.PaperCount == 1 && f.Persisted.Ideas == "", "retry after IO recovery stores the latest editor content exactly once");
            Check(replaced == 1 && saved == 1, "only a successful retry publishes completion");
            Check(f.Store.MemoPaperGeneration == generation + 1, "successful retry advances the sheet identity exactly once");

            f.Store.State.Ideas = "已有归档后的当前原文"; f.Store.Save();
            var papers = f.Store.State.MemoPapers;
            current = File.ReadAllBytes(f.Store.PathName);
            using (var held = new FileStream(f.Store.PathName, FileMode.Open, FileAccess.Read, FileShare.Read))
                failed = FailsIo(delegate { f.Store.StoreCurrentMemo("archive"); });
            Check(failed && Object.ReferenceEquals(papers, f.Store.State.MemoPapers) && papers.Count == 1, "failed storage retains the previous collection and paper identities");
            Check(f.Store.State.Ideas == "已有归档后的当前原文" && current.SequenceEqual(File.ReadAllBytes(f.Store.PathName)), "failed storage with existing papers keeps both current text and primary bytes");
            Check(f.Store.MemoPaperGeneration == generation + 1, "ordinary autosave and later failed storage cannot advance the sheet identity");
        }

        private static void SameTextSheetIdentity()
        {
            var f = new Fixture("same-text-sheet-identity", "取回后文字相同，纸页已经换过");
            string text = f.Store.State.Ideas;
            long originalGeneration = f.Store.MemoPaperGeneration;
            f.Store.StoreCurrentMemo("archive");
            Check(f.Store.OpenMemoText(text) && f.Store.MemoPaperGeneration == originalGeneration + 2,
                "storing and taking back identical text creates a new active sheet identity");
            long currentGeneration = f.Store.MemoPaperGeneration;
            byte[] before = File.ReadAllBytes(f.Store.PathName);
            Check(!f.Store.OpenMemoText(text) && f.Store.MemoPaperGeneration == currentGeneration,
                "opening identical current text is an identity-preserving no-op");
            bool rejected = false;
            try { f.Store.StoreCurrentMemo("trash", text, originalGeneration); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected && f.Store.State.Ideas == text && f.PaperCount == 1 && before.SequenceEqual(File.ReadAllBytes(f.Store.PathName)),
                "an old generation cannot discard a different active sheet even when its exact text matches");
            Check(f.Store.MemoPaperGeneration == currentGeneration, "rejecting a stale generation leaves active identity unchanged");
            Check(f.Store.StoreCurrentMemo("trash", text, currentGeneration) != null && f.Persisted.Ideas == "" && f.PaperCount == 2,
                "the current generation can still store the same text successfully");
        }

        private static void RestoreWithProtection()
        {
            var f = new Fixture("restore", "旧纸张");
            var old = f.Store.StoreCurrentMemo("trash");
            Check(f.Store.OpenMemoText(old.Text) && f.Persisted.Ideas == old.Text && f.PaperCount == 1, "opening a stored paper into an empty editor does not duplicate it");
            Check(f.Store.State.MemoPapers[0].Id == old.Id && f.Store.State.MemoPapers[0].Kind == "trash", "opening keeps the original recovery paper intact");
            byte[] unchanged = File.ReadAllBytes(f.Store.PathName);
            Check(!f.Store.OpenMemoText(old.Text) && unchanged.SequenceEqual(File.ReadAllBytes(f.Store.PathName)), "opening identical current content is a no-op");
            f.Store.State.Ideas = "当前还在编辑的文字";
            Check(f.Store.OpenMemoText("取回的目标文字"), "opening another version accepts a genuinely different target");
            Check(f.Persisted.Ideas == "取回的目标文字" && f.Persisted.MemoPapers.Count == 2, "opening atomically persists the target and protects nonempty current content");
            Check(f.Persisted.MemoPapers.Last().Text == "当前还在编辑的文字" && f.Persisted.MemoPapers.Last().Kind == "archive", "protected current text becomes an archive rather than overwritten data");
            f.Store = f.Open();
            Check(f.Store.State.Ideas == "取回的目标文字" && f.Store.State.MemoPapers.Last().Text == "当前还在编辑的文字", "restored target and protected current text both survive restart");

            var papers = f.Store.State.MemoPapers; byte[] before = File.ReadAllBytes(f.Store.PathName); bool failed;
            long generation = f.Store.MemoPaperGeneration;
            using (var held = new FileStream(f.Store.PathName, FileMode.Open, FileAccess.Read, FileShare.Read))
                failed = FailsIo(delegate { f.Store.OpenMemoText("写盘失败的取回目标"); });
            Check(failed && f.Store.State.Ideas == "取回的目标文字" && Object.ReferenceEquals(papers, f.Store.State.MemoPapers), "failed restore rolls back both current text and its protected-paper addition");
            Check(before.SequenceEqual(File.ReadAllBytes(f.Store.PathName)), "failed restore retains exact primary data");
            Check(f.Store.MemoPaperGeneration == generation, "failed restore retains the identity of the sheet still being edited");
            Check(f.Store.OpenMemoText(null) && f.Persisted.Ideas == "" && f.Persisted.MemoPapers.Last().Text == "取回的目标文字", "explicitly opening a blank note still protects the outgoing text");
        }

        private static void NotificationFailures()
        {
            var f = new Fixture("notification-failure", "通知失败仍应保留的内容");
            int saved = 0, changed = 0, replaced = 0;
            bool committedWhenNotified = false;
            f.Store.Saved += delegate { throw new InvalidOperationException("saved subscriber failed"); };
            f.Store.Saved += delegate { saved++; };
            f.Store.Changed += delegate { throw new InvalidOperationException("changed subscriber failed"); };
            f.Store.Changed += delegate { changed++; };
            f.Store.MemoReplaced += delegate { throw new InvalidOperationException("replacement subscriber failed"); };
            f.Store.MemoReplaced += delegate { replaced++; committedWhenNotified = f.Persisted.Ideas == f.Store.State.Ideas; };
            var paper = f.Store.StoreCurrentMemo("archive");
            Check(committedWhenNotified, "replacement notification runs after durable primary replacement");
            Check(paper != null && f.Store.State.Ideas == "" && f.Persisted.MemoPapers.Single().Text == paper.Text, "subscriber failures cannot roll back a successfully committed paper");
            Check(saved == 1 && changed == 1 && replaced == 1, "one failing subscriber does not block later subscribers or other windows");
            Check(f.Store.MemoPaperGeneration == 1, "post-commit subscriber failures do not roll back the active sheet generation");
            Check(!String.IsNullOrEmpty(f.Store.Notice), "subscriber failure leaves a readable refresh notice");
            Check(f.Store.OpenMemoText(paper.Text) && f.Persisted.Ideas == paper.Text && replaced == 2, "restore also remains committed when subscribers fail");
            Check(committedWhenNotified, "restore replacement notification also observes the committed primary data");
            f.Store = f.Open();
            Check(f.Store.State.Ideas == paper.Text && f.PaperCount == 1, "notification failures never create memory-versus-restart divergence");
        }

        private static byte[] TaskAndViewBytes(WorkboardState state)
        {
            var projection = new WorkboardState { TaskDraft = state.TaskDraft, DraftPlanDate = state.DraftPlanDate,
                Tasks = state.Tasks, Removed = state.Removed, X = state.X, Y = state.Y, Width = state.Width, Height = state.Height,
                Positioned = state.Positioned, Hidden = state.Hidden, Locked = state.Locked, CompletedExpanded = state.CompletedExpanded,
                ShowCardOnStartup = state.ShowCardOnStartup, TaskScope = state.TaskScope,
                WorkbenchMemoTaskRatio = state.WorkbenchMemoTaskRatio, CardMemoTaskRatio = state.CardMemoTaskRatio };
            using (var buffer = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(WorkboardState)).WriteObject(buffer, projection); return buffer.ToArray();
            }
        }

        private static void TaskAndHistoryIsolation()
        {
            var f = new Fixture("task-history", "随手记正文");
            var task = f.Store.AddTask("保留的任务", new DateTime(2026, 10, 26)); f.Store.SetCompleted(task, true);
            var removed = f.Store.AddTask("保留的移除任务", null); f.Store.Remove(removed);
            f.Store.State.TaskDraft = "保留的任务草稿"; f.Store.State.DraftPlanDate = "2026-10-24";
            f.Store.State.TaskScope = "all"; f.Store.State.CompletedExpanded = true; f.Store.State.X = 31;
            f.Store.State.WorkbenchMemoTaskRatio = 0.41; f.Store.State.CardMemoTaskRatio = 0.55; f.Store.Save();
            byte[] tasks = TaskAndViewBytes(f.Store.State);
            f.Store.StoreCurrentMemo("trash"); f.Store.OpenMemoText("恢复到编辑器的内容");
            Check(tasks.SequenceEqual(TaskAndViewBytes(f.Persisted)), "storing and restoring do not alter tasks, draft dates, removal state or view preferences");
            Check(Object.ReferenceEquals(task, f.Store.State.Tasks[0]) && Object.ReferenceEquals(removed, f.Store.State.Removed[0]), "memo transactions retain live task object identities");
            for (int index = 0; index < 5; index++)
            {
                f.Store.State.Ideas = "第" + index + "张长便笺" + new string('记', 12000); f.Store.Save(); f.Store.StoreCurrentMemo("archive");
            }
            f.Store.State.Ideas = "只有很短的当前文字"; f.Store.Save(); f.Store.CheckpointMemoHistory(true);
            Check(new FileInfo(f.Store.PathName).Length > 120000, "large-paper fixture exercises a substantial primary collection");
            Check(f.History.Length > 0 && f.History.All(path => !File.ReadAllText(path).Contains("\"MemoPapers\"")), "every new recovery snapshot omits the entire stored-paper collection");
            Check(f.History.All(path => f.Store.ReadHistorySnapshot(path).MemoPapers == null), "sanitized history remains readable through the legacy snapshot API");
            Check(f.History.Max(path => new FileInfo(path).Length) < 50000, "history payload size follows active content rather than accumulated paper size");
            int historyCount = f.History.Length;
            f.Store.State.MemoPapers[0].Kind = "archive"; f.Store.Save(); f.Store.CheckpointMemoHistory(true);
            Check(f.History.Length == historyCount, "paper metadata is excluded from automatic revision signatures");
            Check(f.Persisted.MemoPapers[0].Kind == "archive" && f.Persisted.MemoPapers.Count == 6, "ordinary save still persists paper metadata and all stored papers");
        }

        private static void LegacyAndHistoryFailure()
        {
            var f = new Fixture("legacy", "placeholder");
            File.WriteAllText(f.Store.PathName, "{\"Version\":1,\"Ideas\":\"旧版随手记\",\"TaskDraft\":\"旧任务草稿\",\"Tasks\":[{\"Id\":\"old-id\",\"Text\":\"旧任务\",\"Done\":false}],\"Removed\":[]}", new UTF8Encoding(false));
            f.Store = f.Open();
            Check(f.Store.State.Version == 1 && f.Store.State.MemoPapers == null && f.Store.State.Ideas == "旧版随手记", "legacy version-one data loads without a paper field or migration rewrite");
            byte[] old = File.ReadAllBytes(f.Store.PathName);
            f.Store.StoreCurrentMemo("archive");
            Check(f.Persisted.Version == 1 && f.Persisted.MemoPapers.Single().Text == "旧版随手记" && f.Persisted.Tasks[0].Id == "old-id", "legacy content can be stored additively without changing schema version or tasks");
            Check(f.History.Any(path => f.Store.ReadHistorySnapshot(path).Ideas == "旧版随手记" && f.Store.ReadHistorySnapshot(path).TaskDraft == "") && old.SequenceEqual(File.ReadAllBytes(f.Store.PathName + ".bak")), "legacy history retains committed content without drafts while the primary backup preserves exact original bytes");

            var blocked = new Fixture("history-blocked", "历史文件夹失败也不能丢的原文");
            File.WriteAllText(Path.Combine(blocked.Folder, "history"), "blocks history directory", new UTF8Encoding(false));
            var paper = blocked.Store.StoreCurrentMemo("trash");
            Check(paper != null && blocked.Persisted.Ideas == "" && blocked.Persisted.MemoPapers.Single().Text == paper.Text, "an unavailable recovery directory cannot lose a paper stored in the same primary transaction");
            Check(!String.IsNullOrEmpty(blocked.Store.HistoryNotice), "independent recovery-history failures remain visible");
            blocked.Store = blocked.Open();
            Check(blocked.Store.OpenMemoText(paper.Text) && blocked.Persisted.Ideas == paper.Text, "stored papers can be recovered after restart even when recovery history remains unavailable");
        }

        private static int Main()
        {
            int exitCode = 0;
            try
            {
                EmptyAndExpectedText(); StoreAndRestart("archive"); StoreAndRestart("trash"); PrimaryWriteFailure(); SameTextSheetIdentity();
                RestoreWithProtection(); NotificationFailures(); TaskAndHistoryIsolation(); LegacyAndHistoryFailure();
                Report.AppendLine("RESULT: " + assertions + " assertions passed, 0 failed");
            }
            catch (Exception error) { Report.AppendLine(error.ToString()); exitCode = 1; }
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "verification-memo-paper-store.txt"), Report.ToString(), new UTF8Encoding(false));
            return exitCode;
        }
    }
}
