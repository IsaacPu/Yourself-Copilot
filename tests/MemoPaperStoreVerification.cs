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

        private static void SharedPaperUndoRedo()
        {
            var f = new Fixture("paper-undo-redo", "最初的纸页 A");
            byte[] initial = File.ReadAllBytes(f.Store.PathName);
            Check(!f.Store.CanUndoMemoPaper && !f.Store.CanRedoMemoPaper && !f.Store.UndoMemoPaper() && !f.Store.RedoMemoPaper(),
                "a fresh shared store has no paper actions to undo or redo");
            Check(initial.SequenceEqual(File.ReadAllBytes(f.Store.PathName)) && f.Store.MemoPaperGeneration == 0,
                "unavailable paper undo or redo neither saves nor changes sheet identity");
            f.Store.State.Ideas = "普通自动保存"; f.Store.Save();
            Check(!f.Store.CanUndoMemoPaper && !f.Store.CanRedoMemoPaper, "ordinary typing autosave never creates a paper action");
            f.Store.State.Ideas = "最初的纸页 A"; f.Store.Save();
            bool seenUndoAtSave = false, seenRedoAtReplace = false;
            f.Store.Saved += delegate { seenUndoAtSave = f.Store.CanUndoMemoPaper; };
            f.Store.MemoReplaced += delegate { seenRedoAtReplace = f.Store.CanRedoMemoPaper; };
            f.Store.StoreCurrentMemo("archive");
            Check(seenUndoAtSave && f.Store.CanUndoMemoPaper && !f.Store.CanRedoMemoPaper,
                "shared observers see undo availability only after the archived page commits");
            f.Store.OpenMemoText("从历史取回的纸页 B");
            f.Store.StoreCurrentMemo("trash");
            int originalPapers = f.PaperCount;
            string[] ids = f.Store.State.MemoPapers.Select(p => p.Id).ToArray();
            Check(f.Store.UndoMemoPaper() && f.Persisted.Ideas == "从历史取回的纸页 B" && seenRedoAtReplace,
                "undo restores the most recent trashed page through the shared store");
            Check(f.Store.UndoMemoPaper() && f.Persisted.Ideas == "", "undo of history take-back returns to its previous blank page");
            Check(f.Store.UndoMemoPaper() && f.Persisted.Ideas == "最初的纸页 A" && !f.Store.CanUndoMemoPaper,
                "archive, take-back and trash form one ordered paper action history");
            Check(f.Store.RedoMemoPaper() && f.Persisted.Ideas == "", "redo of archive returns to the blank page");
            Check(f.Store.RedoMemoPaper() && f.Persisted.Ideas == "从历史取回的纸页 B", "redo of history take-back restores its target");
            Check(f.Store.RedoMemoPaper() && f.Persisted.Ideas == "" && !f.Store.CanRedoMemoPaper,
                "redo of trash completes the same ordered history without a remaining redo");
            Check(f.PaperCount == originalPapers && ids.SequenceEqual(f.Store.State.MemoPapers.Select(p => p.Id)),
                "paper undo and redo preserve every stored paper identity without duplicating normal replay");
            Check(f.Store.MemoPaperGeneration == 9, "each of three actions and six successful replays invalidates earlier drags once");
            f.Store.UndoMemoPaper();
            Check(f.Store.CanRedoMemoPaper && !f.Store.OpenMemoText(f.Store.State.Ideas) && f.Store.CanRedoMemoPaper,
                "same-current take-back is a no-op that preserves redo availability");
            f.Store.State.Ideas += " 继续输入"; f.Store.Save();
            Check(f.Store.CanRedoMemoPaper, "ordinary autosave does not silently replace the paper redo history");
            f.Store.OpenMemoText("新的主动取回动作");
            Check(!f.Store.CanRedoMemoPaper && f.Store.CanUndoMemoPaper, "a new committed paper action clears the former redo branch");
            f.Store = f.Open();
            Check(!f.Store.CanUndoMemoPaper && !f.Store.CanRedoMemoPaper && f.Store.State.Ideas == "新的主动取回动作",
                "paper command history is runtime-only while its resulting text survives restart");
        }

        private static void ReplayProtectsInterveningText()
        {
            var f = new Fixture("paper-replay-protection", "被投放的原始纸页");
            var original = f.Store.StoreCurrentMemo("trash");
            f.Store.State.Ideas = "换页后还没自动保存的新内容";
            Check(f.Store.UndoMemoPaper() && f.Persisted.Ideas == original.Text,
                "undo can restore a paper while the current page contains new unsaved text");
            Check(f.Persisted.MemoPapers.Last().Text == "换页后还没自动保存的新内容" && f.Persisted.MemoPapers.Last().Kind == "archive",
                "undo protects intervening unsaved text in the same atomic replacement");
            f.Store.State.Ideas = "撤销以后、重做以前又写的内容";
            Check(f.Store.RedoMemoPaper() && f.Persisted.Ideas == "换页后还没自动保存的新内容",
                "redo returns to the actual page that undo left, including its intervening writing");
            Check(f.Persisted.MemoPapers.Last().Text == "撤销以后、重做以前又写的内容" && f.Persisted.MemoPapers.Last().Kind == "archive",
                "redo archives its intervening text instead of discarding it");
            Check(f.Persisted.MemoPapers[0].Id == original.Id && f.Persisted.MemoPapers[0].Kind == "trash",
                "both directions retain the original trash paper and its recoverable kind");
            Check(f.Store.UndoMemoPaper() && f.Persisted.Ideas == "撤销以后、重做以前又写的内容",
                "undo after redo returns to the latest actual page left before that redo");
            Check(f.Store.RedoMemoPaper() && f.Persisted.Ideas == "换页后还没自动保存的新内容" && f.PaperCount == 3,
                "repeated snapshot exchange restores either page without duplicating its protected archives");
            f.Store = f.Open();
            Check(f.Store.State.Ideas == "换页后还没自动保存的新内容" && f.PaperCount == 3 && f.Store.State.MemoPapers.Any(p => p.Text == "换页后还没自动保存的新内容")
                && f.Store.State.MemoPapers.Any(p => p.Text == "撤销以后、重做以前又写的内容"),
                "both protected editor buffers remain recoverable after restart");
        }

        private static void ReplayFailureIsRetryable()
        {
            var f = new Fixture("paper-replay-failure", "需要撤销恢复的纸页");
            f.Store.StoreCurrentMemo("archive");
            f.Store.State.Ideas = "撤销失败时仍要保留的未保存文字";
            byte[] before = File.ReadAllBytes(f.Store.PathName);
            var collection = f.Store.State.MemoPapers;
            long generation = f.Store.MemoPaperGeneration;
            bool failed;
            using (var held = new FileStream(f.Store.PathName, FileMode.Open, FileAccess.Read, FileShare.Read))
                failed = FailsIo(delegate { f.Store.UndoMemoPaper(); });
            Check(failed && f.Store.State.Ideas == "撤销失败时仍要保留的未保存文字" && Object.ReferenceEquals(collection, f.Store.State.MemoPapers),
                "failed undo rolls back both text and its protective archive addition");
            Check(f.Store.CanUndoMemoPaper && !f.Store.CanRedoMemoPaper && f.Store.MemoPaperGeneration == generation,
                "failed undo leaves the shared action stacks and sheet identity retryable");
            Check(before.SequenceEqual(File.ReadAllBytes(f.Store.PathName)), "failed undo preserves exact primary bytes");
            Check(f.Store.UndoMemoPaper() && f.Persisted.Ideas == "需要撤销恢复的纸页" && f.PaperCount == 2,
                "undo retry stores the intervening buffer only once");
            f.Store.State.Ideas = "重做失败时保留的新文字";
            before = File.ReadAllBytes(f.Store.PathName); collection = f.Store.State.MemoPapers; generation = f.Store.MemoPaperGeneration;
            using (var held = new FileStream(f.Store.PathName, FileMode.Open, FileAccess.Read, FileShare.Read))
                failed = FailsIo(delegate { f.Store.RedoMemoPaper(); });
            Check(failed && f.Store.State.Ideas == "重做失败时保留的新文字" && Object.ReferenceEquals(collection, f.Store.State.MemoPapers),
                "failed redo restores the live editor buffer and existing paper collection");
            Check(!f.Store.CanUndoMemoPaper && f.Store.CanRedoMemoPaper && f.Store.MemoPaperGeneration == generation,
                "failed redo retains its action without advancing the sheet generation");
            Check(before.SequenceEqual(File.ReadAllBytes(f.Store.PathName)), "failed redo preserves exact primary bytes");
            Check(f.Store.RedoMemoPaper() && f.Persisted.Ideas == "撤销失败时仍要保留的未保存文字" && f.PaperCount == 3,
                "redo retry protects the latest buffer once and returns to the exact page left by the successful undo");

            Check(f.Store.UndoMemoPaper() && f.Persisted.Ideas == "重做失败时保留的新文字" && f.Store.CanRedoMemoPaper,
                "after failed redo and retry, undo still returns to the exact live text from before the retry");
            generation = f.Store.MemoPaperGeneration;
            using (var held = new FileStream(f.Store.PathName, FileMode.Open, FileAccess.Read, FileShare.Read))
                failed = FailsIo(delegate { f.Store.OpenMemoText("未成功的新取回动作"); });
            Check(failed && f.Store.CanRedoMemoPaper && f.Store.MemoPaperGeneration == generation,
                "a failed new paper action cannot clear a previously available redo");
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

        private static void TitlesAndRenaming()
        {
            var f = new Fixture("titles", "长篇正文保持原样。\n第二段仍然是原文。");
            string original = f.Store.State.Ideas;
            int historyCount = f.History.Length;
            foreach (string title in new[] { "长", "长篇", "长篇阅读随记" }) { f.Store.State.MemoTitle = title; f.Store.Save(); }
            Check(f.Persisted.MemoTitle == "长篇阅读随记" && f.History.Length == historyCount && f.Persisted.Ideas == original, "title autosaves persist independently without keystroke history or body changes");
            var paper = f.Store.StoreCurrentMemo("trash");
            Check(paper.Title == "长篇阅读随记" && f.Persisted.MemoPapers.Single().Title == paper.Title && String.IsNullOrEmpty(f.Persisted.MemoTitle), "putting a titled paper in the bin stores its title and starts an untitled blank sheet");
            f.Store.UndoMemoPaper();
            Check(f.Persisted.Ideas == original && f.Persisted.MemoTitle == paper.Title, "paper undo restores the original title together with its body");
            f.Store.RedoMemoPaper();
            Check(f.Persisted.Ideas == "" && String.IsNullOrEmpty(f.Persisted.MemoTitle), "paper redo returns to the blank sheet without leaking the old title");
            f.Store = f.Open();
            Check(f.Store.OpenMemoText(paper.Text, paper.Title) && f.Persisted.MemoTitle == paper.Title, "restart and take-back preserve a stored paper title");
            f.Store.State.MemoTitle = "当前页自己的名字"; f.Store.Save();
            f.Store.OpenMemoText("另一张纸的正文。", "另一张纸");
            Check(f.Persisted.MemoPapers.Any(value => value.Text == original && value.Title == "当前页自己的名字"), "take-back protects the current page with its actual title");
            f.Store.UndoMemoPaper();
            Check(f.Persisted.MemoTitle == "当前页自己的名字" && f.Persisted.Ideas == original, "undo after take-back restores the protected title and body pair");
            f.Store.RedoMemoPaper();
            Check(f.Persisted.MemoTitle == "另一张纸" && f.Persisted.Ideas == "另一张纸的正文。", "redo restores the selected historical title and body pair");

            f.Store.RenameMemo(paper.Id, null, "右键改好的标题");
            Check(f.Persisted.MemoPapers.First(value => value.Id == paper.Id).Title == "右键改好的标题" && f.Persisted.MemoPapers.First(value => value.Id == paper.Id).Text == original && f.Persisted.MemoTitle == "另一张纸", "renaming an archived paper changes only its title metadata");
            int count = f.History.Length;
            f.Store.RenameMemo(paper.Id, null, "再次修改标题");
            Check(f.History.Length == count, "renaming an old paper creates no new content history versions");

            string folder = Path.Combine(f.Store.HistoryFolder, "2026-10-25"); Directory.CreateDirectory(folder);
            string snapshot = Path.Combine(folder, "named-original.json");
            using (var stream = File.Create(snapshot)) new DataContractJsonSerializer(typeof(WorkboardState)).WriteObject(stream, new WorkboardState { Ideas = original, MemoTitle = "原始快照的标题" });
            byte[] source = File.ReadAllBytes(snapshot); DateTime sourceTime = File.GetLastWriteTimeUtc(snapshot);
            f.Store.RenameMemo(null, snapshot, "只改索引名称");
            Check(source.SequenceEqual(File.ReadAllBytes(snapshot)) && sourceTime == File.GetLastWriteTimeUtc(snapshot), "renaming automatic history never changes the original snapshot bytes or timestamp");
            f.Store = f.Open();
            Check(f.Store.HistoryMemoTitle(snapshot, "原始快照的标题") == "只改索引名称", "history title overrides survive restart");
            f.Store.RenameMemo(null, snapshot, " ");
            Check(f.Store.HistoryMemoTitle(snapshot, "原始快照的标题") == null, "clearing an override explicitly restores the body-derived display name");

            string oldTitle = f.Store.State.MemoPapers.First(value => value.Id == paper.Id).Title;
            var oldTitles = f.Store.State.MemoHistoryTitles; byte[] primary = File.ReadAllBytes(f.Store.PathName);
            Directory.CreateDirectory(f.Store.PathName + ".tmp");
            try
            {
                Check(FailsIo(delegate { f.Store.RenameMemo(paper.Id, null, "不会保存的名字"); }) && f.Store.State.MemoPapers.First(value => value.Id == paper.Id).Title == oldTitle, "failed paper rename rolls back its in-memory title");
                Check(FailsIo(delegate { f.Store.RenameMemo(null, snapshot, "不会保存的索引"); }) && Object.ReferenceEquals(oldTitles, f.Store.State.MemoHistoryTitles), "failed snapshot rename rolls back the title override map");
                Check(FailsIo(delegate { f.Store.StoreCurrentMemo("trash"); }) && f.Store.State.MemoTitle == "另一张纸" && f.Store.State.Ideas == "另一张纸的正文。", "failed page replacement preserves the title and body together");
                Check(primary.SequenceEqual(File.ReadAllBytes(f.Store.PathName)), "failed title and paper writes preserve exact primary file bytes");
            }
            finally { Directory.Delete(f.Store.PathName + ".tmp"); }
            f.Store.RenameMemo(null, snapshot, "可移动的标题");
            string movedFolder = Path.Combine(f.Folder, "moved-copy"); Directory.CreateDirectory(Path.Combine(movedFolder, "history", "workboard", "2026-10-25"));
            File.Copy(f.Store.PathName, Path.Combine(movedFolder, "workboard.json"));
            string movedSnapshot = Path.Combine(movedFolder, "history", "workboard", "2026-10-25", "named-original.json"); File.Copy(snapshot, movedSnapshot);
            Check(new WorkboardStore(movedFolder).HistoryMemoTitle(movedSnapshot, null) == "可移动的标题", "relative snapshot identities keep renamed titles when a portable directory moves");
            Check(WorkboardStore.NormalizeMemoTitle(new string('甲', 79) + "🌿") == new string('甲', 79), "title length limit never splits a Unicode surrogate pair");
            Check(!f.Store.RenameMemo("absent-paper", null, "missing") && f.Store.State.MemoPapers.All(value => value.Id != "absent-paper"), "renaming an unavailable paper cannot create a phantom record");
            byte[] currentPrimary = File.ReadAllBytes(f.Store.PathName);
            bool rejected = false; try { f.Store.RenameMemo(null, f.Store.PathName, "outside history"); } catch (ArgumentException) { rejected = true; }
            Check(rejected && currentPrimary.SequenceEqual(File.ReadAllBytes(f.Store.PathName)), "snapshot title edits reject a source outside history without changing primary data");
            f.Store.State.Ideas += "\n新增完整句子。"; f.Store.Save(); f.Store.CheckpointMemoHistory(true);
            var latest = f.History.Select(path => f.Store.ReadHistorySnapshot(path)).First(value => value.Ideas == f.Store.State.Ideas);
            Check(latest.MemoTitle == f.Store.State.MemoTitle && latest.MemoHistoryTitles == null && latest.MemoPapers == null, "recovery snapshots carry their own title without repeating global title maps or paper collections");
        }

        private static int Main()
        {
            int exitCode = 0;
            try
            {
                EmptyAndExpectedText(); StoreAndRestart("archive"); StoreAndRestart("trash"); PrimaryWriteFailure(); SameTextSheetIdentity();
                SharedPaperUndoRedo(); ReplayProtectsInterveningText(); ReplayFailureIsRetryable();
                RestoreWithProtection(); NotificationFailures(); TaskAndHistoryIsolation(); LegacyAndHistoryFailure(); TitlesAndRenaming();
                Report.AppendLine("RESULT: " + assertions + " assertions passed, 0 failed");
            }
            catch (Exception error) { Report.AppendLine(error.ToString()); exitCode = 1; }
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "verification-memo-paper-store.txt"), Report.ToString(), new UTF8Encoding(false));
            return exitCode;
        }
    }
}
