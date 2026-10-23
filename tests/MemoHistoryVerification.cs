using System;
using System.IO;
using System.Linq;
using System.Text;

namespace LocalDesktopNote
{
    internal static class MemoHistoryVerification
    {
        private static readonly StringBuilder Report = new StringBuilder();
        private static int assertions;
        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("FAIL: " + message);
            assertions++; Report.AppendLine("PASS: " + message);
        }
        private static string[] History(WorkboardStore store)
        {
            return Directory.Exists(store.HistoryFolder) ? Directory.GetFiles(store.HistoryFolder, "*.json", SearchOption.AllDirectories) : new string[0];
        }

        private sealed class Fixture
        {
            internal readonly string Folder;
            internal WorkboardStore Store;
            internal DateTime Clock = new DateTime(2026, 10, 22, 10, 0, 0, DateTimeKind.Utc);

            internal Fixture(string name, string memo)
            {
                Folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "memo-data", name + "-" + Guid.NewGuid().ToString("N"));
                Store = Open();
                Store.State.Ideas = memo; Store.Save();
                // Model an existing document. The initial seed must not remain an
                // unfinished editing session in the store used by each scenario.
                Store = Open();
            }

            internal WorkboardStore Open()
            {
                var store = new WorkboardStore(Folder);
                store.UtcNowProvider = delegate { return Clock; };
                store.TodayProvider = delegate { return new DateTime(2026, 10, 22); };
                return store;
            }

            internal void Advance(double seconds) { Clock = Clock.AddSeconds(seconds); }
            internal int Count { get { return History(Store).Length; } }
            internal byte[] CurrentBytes { get { return File.ReadAllBytes(Store.PathName); } }
            internal string PersistedMemo { get { return LibraryStore.Read<WorkboardState>(Store.PathName).Ideas; } }
            internal bool HasExact(byte[] bytes) { return History(Store).Any(path => File.ReadAllBytes(path).SequenceEqual(bytes)); }
            internal bool HasMemo(string text) { return History(Store).Any(path => Store.ReadHistorySnapshot(path).Ideas == text); }
        }

        private sealed class BlockHistory : IDisposable
        {
            private readonly string path, held;
            internal BlockHistory(Fixture fixture)
            {
                path = fixture.Store.HistoryFolder;
                held = Path.Combine(fixture.Folder, "held-history-" + Guid.NewGuid().ToString("N"));
                if (Directory.Exists(path)) Directory.Move(path, held);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, "blocks history directory creation");
            }
            public void Dispose()
            {
                File.Delete(path);
                if (Directory.Exists(held)) Directory.Move(held, path);
            }
        }

        private static void ContinuousAppendAndIdle()
        {
            var f = new Fixture("append-and-idle", "原有备忘");
            byte[] before = f.CurrentBytes;
            for (int index = 0; index < 20; index++)
            {
                f.Advance(1); f.Store.State.Ideas += "字"; f.Store.Save();
                Check(f.PersistedMemo == f.Store.State.Ideas, "autosave persists single-character append " + (index + 1));
            }
            Check(f.Count == 1 && f.HasExact(before), "twenty consecutive appends retain one exact pre-edit version");
            byte[] current = f.CurrentBytes, backup = File.ReadAllBytes(f.Store.PathName + ".bak");
            f.Advance(29.999); Check(f.Store.CheckpointMemoHistory(), "checking before the idle boundary succeeds without forcing history");
            Check(f.Count == 1, "29.999 seconds of idle does not produce a final checkpoint");
            f.Advance(0.001); Check(f.Store.CheckpointMemoHistory(), "the 30-second idle boundary can checkpoint the persisted final version");
            Check(f.Count == 2 && f.HasExact(current), "30 seconds of idle adds exactly the final persisted memo version");
            Check(f.CurrentBytes.SequenceEqual(current) && File.ReadAllBytes(f.Store.PathName + ".bak").SequenceEqual(backup), "history checkpointing leaves primary data and the last autosave backup unchanged");
            f.Advance(60); f.Store.CheckpointMemoHistory(); f.Store.CheckpointMemoHistory(true);
            Check(f.Count == 2, "repeated idle and forced checks do not duplicate an already checkpointed version");
        }

        private static void ContinuousBackspace()
        {
            var f = new Fixture("single-backspace", new string('记', 100));
            byte[] before = f.CurrentBytes;
            for (int index = 0; index < 20; index++)
            {
                f.Advance(1); f.Store.State.Ideas = f.Store.State.Ideas.Substring(0, f.Store.State.Ideas.Length - 1); f.Store.Save();
                Check(f.PersistedMemo == f.Store.State.Ideas, "autosave persists single-character deletion " + (index + 1));
            }
            Check(f.Count == 1 && f.HasExact(before), "twenty individual backspaces share one recovery baseline rather than twenty snapshots");
            f.Advance(30); f.Store.CheckpointMemoHistory();
            Check(f.Count == 2 && f.HasMemo(new string('记', 80)), "the final result of ordinary backspacing becomes one idle checkpoint");
        }

        private static void ContinuousFiveMinuteCheckpoint()
        {
            var f = new Fixture("continuous-five-minute", "持续编辑");
            f.Store.State.Ideas += "始"; f.Store.Save();
            for (int index = 0; index < 29; index++)
            {
                f.Advance(10); f.Store.State.Ideas += "续"; f.Store.Save();
            }
            f.Advance(9); f.Store.State.Ideas += "前"; f.Store.Save();
            Check(f.Count == 1, "continuous edits at 299 seconds retain one baseline without an early periodic checkpoint");
            f.Advance(1); f.Store.State.Ideas += "界"; f.Store.Save();
            Check(f.Count == 2 && f.HasExact(f.CurrentBytes), "an autosave at five minutes retains the latest persisted version even without idle time");
            f.Advance(10); f.Store.State.Ideas += "后"; f.Store.Save();
            Check(f.Count == 2, "editing after a periodic checkpoint does not duplicate its already archived baseline");
            f.Advance(30); f.Store.CheckpointMemoHistory();
            Check(f.Count == 3 && f.HasMemo(f.Store.State.Ideas), "the later final version still receives its own idle checkpoint");
        }

        private static void ForceAndUnsavedInput()
        {
            var f = new Fixture("force-and-unsaved", "已保存内容");
            f.Store.CheckpointMemoHistory(true);
            Check(f.Count == 0, "forcing without a pending persisted memo does not invent a history entry");
            f.Store.State.Ideas = "还未落盘的文字";
            f.Advance(60); f.Store.CheckpointMemoHistory(true);
            Check(f.Count == 0 && f.PersistedMemo == "已保存内容", "a forced checkpoint never promotes unpersisted input into history or current data");
            f.Store.Save(); byte[] current = f.CurrentBytes;
            Check(f.Count == 1, "saving the first real edit retains its existing-document baseline");
            f.Store.CheckpointMemoHistory(true); f.Store.CheckpointMemoHistory(true);
            Check(f.Count == 2 && f.HasExact(current), "manual or leaving-editor checkpoint stores the persisted final version exactly once");
            f.Store.State.Ideas += "仍未保存"; f.Store.CheckpointMemoHistory(true);
            Check(f.Count == 2 && !f.HasMemo(f.Store.State.Ideas), "unsaved input after a completed session cannot create an additional forced snapshot");
            f.Store.Save(); f.Store.CheckpointMemoHistory(true);
            Check(f.Count == 3 && f.HasExact(f.CurrentBytes), "the same input becomes eligible after successful persistence");
        }

        private static void ContinuousUnsavedMemoDeadline()
        {
            var f = new Fixture("continuous-unsaved-deadline", "持续输入基线");
            byte[] original = f.CurrentBytes;
            f.Store.State.Ideas += "始"; f.Store.NotifyChanged();
            for (int second = 1; second <= 299; second++)
            {
                f.Advance(1); f.Store.State.Ideas += "续"; f.Store.NotifyChanged();
            }
            Check(!f.Store.MemoAutosaveDue && f.CurrentBytes.SequenceEqual(original) && f.Count == 0, "continuous input broadcasts do not save or become due before 300 seconds");
            f.Advance(1); f.Store.State.Ideas += "界"; f.Store.NotifyChanged();
            Check(f.Store.MemoAutosaveDue, "the first continuous unsaved memo becomes due at exactly five minutes");
            f.Store.CheckpointMemoHistory(true);
            Check(f.Count == 0 && f.CurrentBytes.SequenceEqual(original), "the maximum-wait signal never directly promotes unsaved input into history");
            f.Store.Save(); byte[] persisted = f.CurrentBytes; f.Store.CheckpointMemoHistory(true);
            Check(!f.Store.MemoAutosaveDue && f.Count == 2 && f.HasExact(persisted), "flushing and forcing a due memo preserves its exact persisted version and resets the deadline");

            var pending = new Fixture("unsaved-existing-session", "已开始会话");
            pending.Store.State.Ideas += "存"; pending.Store.Save();
            pending.Advance(10); pending.Store.State.Ideas += "未存"; pending.Store.NotifyChanged();
            pending.Advance(289); pending.Store.NotifyChanged();
            Check(!pending.Store.MemoAutosaveDue, "a session with a persisted edit is not due at 299 seconds from its checkpoint anchor");
            pending.Advance(1); pending.Store.NotifyChanged();
            Check(pending.Store.MemoAutosaveDue, "unsaved input in an existing session follows the checkpoint anchor rather than restarting five minutes");
            pending.Store.Save(); pending.Store.CheckpointMemoHistory(true);
            Check(!pending.Store.MemoAutosaveDue && pending.HasExact(pending.CurrentBytes), "the existing session's maximum-wait flush also checkpoints only its saved version");

            var reverted = new Fixture("unsaved-reverted-and-view", "可以撤回");
            reverted.Store.State.X += 1; reverted.Store.NotifyChanged();
            reverted.Advance(600); reverted.Store.NotifyChanged();
            Check(!reverted.Store.MemoAutosaveDue, "view-only broadcasts never start a memo maximum-wait deadline");
            string before = reverted.Store.State.Ideas;
            reverted.Store.State.Ideas += "临时"; reverted.Store.NotifyChanged();
            reverted.Advance(299); reverted.Store.State.Ideas = before; reverted.Store.NotifyChanged();
            reverted.Advance(1); reverted.Store.NotifyChanged();
            Check(!reverted.Store.MemoAutosaveDue, "reverting to the persisted memo cancels its unsaved deadline");
            reverted.Store.State.Ideas += "新一轮"; reverted.Store.NotifyChanged();
            reverted.Advance(299);
            Check(!reverted.Store.MemoAutosaveDue, "input after a full revert gets a new five-minute deadline");
            reverted.Advance(1);
            Check(reverted.Store.MemoAutosaveDue, "a new unreverted input session becomes due at its own five-minute boundary");
        }

        private static void DestructiveChanges()
        {
            var f = new Fixture("large-middle-delete", new string('甲', 50) + new string('乙', 50));
            f.Store.State.Ideas += "尾"; f.Store.Save(); byte[] beforeDelete = f.CurrentBytes;
            f.Advance(1); f.Store.State.Ideas = f.Store.State.Ideas.Remove(30, 40); f.Store.Save();
            Check(f.Count == 2 && f.HasExact(beforeDelete), "one deletion of at least 20 characters and 30 percent immediately protects the exact pre-delete version");
            f.Advance(1); f.Store.State.Ideas = f.Store.State.Ideas.Substring(0, f.Store.State.Ideas.Length - 1); f.Store.Save();
            Check(f.Count == 2, "a small backspace after a large deletion does not add another snapshot");
            f.Store.CheckpointMemoHistory(true);
            Check(f.Count == 3 && f.HasExact(f.CurrentBytes), "the final destructive-edit result can still be checkpointed separately");

            var replacement = new Fixture("equal-length-replacement", new string('甲', 100));
            replacement.Store.State.Ideas += "尾"; replacement.Store.Save(); byte[] beforeReplace = replacement.CurrentBytes;
            replacement.Advance(1); replacement.Store.State.Ideas = replacement.Store.State.Ideas.Substring(0, 30) + new string('乙', 40) + replacement.Store.State.Ideas.Substring(70); replacement.Store.Save();
            Check(replacement.Count == 2 && replacement.HasExact(beforeReplace), "equal-length replacement protects removed text using the common prefix and suffix rather than net length");

            var clear = new Fixture("clear-memo", "短备忘也需要清空保护");
            clear.Store.State.Ideas += "尾"; clear.Store.Save(); byte[] beforeClear = clear.CurrentBytes;
            clear.Advance(1); clear.Store.State.Ideas = ""; clear.Store.Save();
            Check(clear.Count == 2 && clear.HasExact(beforeClear) && clear.PersistedMemo == "", "clearing even a short memo protects the latest saved text while preserving the intentional empty current value");

            var threshold = new Fixture("deletion-thresholds", new string('甲', 100));
            threshold.Store.State.Ideas += "尾"; threshold.Store.Save();
            threshold.Advance(1); threshold.Store.State.Ideas = threshold.Store.State.Ideas.Remove(10, 19); threshold.Store.Save();
            Check(threshold.Count == 1, "removing fewer than 20 characters stays in the current editing session");
            threshold.Advance(1); threshold.Store.State.Ideas = threshold.Store.State.Ideas.Remove(10, 20); threshold.Store.Save();
            Check(threshold.Count == 1, "removing 20 characters below 30 percent does not trigger destructive-edit history");
            byte[] beforeThreshold = threshold.CurrentBytes;
            threshold.Advance(1); threshold.Store.State.Ideas = threshold.Store.State.Ideas.Remove(10, 20); threshold.Store.Save();
            Check(threshold.Count == 2 && threshold.HasExact(beforeThreshold), "both deletion thresholds must be met to retain an additional pre-edit version");
        }

        private static void RestartDeduplication()
        {
            var f = new Fixture("restart-deduplication", "重启前基线");
            f.Store.State.Ideas += "新"; f.Store.Save(); f.Store.CheckpointMemoHistory(true);
            Check(f.Count == 2, "restart fixture contains one baseline and one final checkpoint");
            // Make the intended latest history explicit without wall-clock sleeps.
            foreach (string path in History(f.Store))
                File.SetLastWriteTimeUtc(path, f.Store.ReadHistorySnapshot(path).Ideas == f.Store.State.Ideas ? f.Clock : f.Clock.AddSeconds(-1));
            f.Store = f.Open(); f.Store.State.Ideas += "再"; f.Store.Save();
            Check(f.Count == 2, "reopening and editing deduplicates the previous run's final checkpoint as the new baseline");
            f.Store.CheckpointMemoHistory(true);
            Check(f.Count == 3 && f.HasExact(f.CurrentBytes), "a genuinely new final version is preserved after reopening");
            f.Store = f.Open(); f.Store.CheckpointMemoHistory(true);
            Check(f.Count == 3, "restarting alone does not manufacture a pending memo revision");
        }

        private static void OtherContentKeepsHistory()
        {
            var f = new Fixture("other-content", "固定随手记");
            byte[] previous = f.CurrentBytes;
            var task = f.Store.AddTask("任务原文"); f.Store.Save();
            Check(f.Count == 1 && f.HasExact(previous), "task addition still retains its exact previous content version");
            previous = f.CurrentBytes; task.Text = "任务新标题"; f.Store.Save();
            Check(f.Count == 2 && f.HasExact(previous), "task-title changes remain individual content revisions");
            previous = f.CurrentBytes; f.Store.State.TaskDraft = "任务草稿"; f.Store.State.DraftPlanDate = "2026-10-23"; f.Store.Save();
            Check(f.Count == 3 && f.HasExact(previous), "task draft content is not grouped by the memo-only policy");
            previous = f.CurrentBytes; f.Store.State.DraftPlanDate = "2026-10-24"; f.Store.Save();
            Check(f.Count == 4 && f.HasExact(previous), "explicit draft-date changes keep their previous revision");
            previous = f.CurrentBytes; f.Store.SetPlanDate(task, new DateTime(2026, 10, 25)); f.Store.Save();
            Check(f.Count == 5 && f.HasExact(previous), "task rescheduling keeps its previous revision");
            previous = f.CurrentBytes; f.Store.SetCompleted(task, true); f.Store.Save();
            Check(f.Count == 6 && f.HasExact(previous), "completion remains an individual previous-content snapshot");
            previous = f.CurrentBytes; f.Store.Remove(task); f.Store.Save();
            Check(f.Count == 7 && f.HasExact(previous), "task removal remains an individual previous-content snapshot");
            previous = f.CurrentBytes; f.Store.Undo(); f.Store.Save();
            Check(f.Count == 8 && f.HasExact(previous), "undo retains the preceding removed-task version");
            f.Advance(60); f.Store.CheckpointMemoHistory(true);
            Check(f.Count == 8, "task-only revisions do not create a pending memo final checkpoint");
        }

        private static void SharedSaveAndViewChanges()
        {
            var f = new Fixture("shared-save", "共享基线");
            int cardSaved = 0, benchSaved = 0;
            f.Store.Saved += delegate { cardSaved++; };
            f.Store.Saved += delegate { benchSaved++; };
            f.Store.State.Ideas += "编辑"; f.Store.NotifyChanged(); f.Store.Save();
            f.Store.Save();
            Check(cardSaved == 2 && benchSaved == 2 && f.Count == 1, "shared save acknowledgements can clear both window timers without duplicating memo history");
            f.Advance(29); f.Store.State.X += 10; f.Store.State.TaskScope = "all"; f.Store.Save();
            Check(f.Count == 1, "a view-only save during a memo session does not create a content revision");
            f.Advance(1); f.Store.CheckpointMemoHistory();
            Check(f.Count == 2 && f.HasMemo(f.Store.State.Ideas), "the shared store can checkpoint after both windows cleared dirty flags, and view saves do not postpone memo idle time");
        }

        private static void HistoryFailureRetries()
        {
            var baseline = new Fixture("failed-baseline", "必须保留的最早基线");
            byte[] earliest = baseline.CurrentBytes;
            using (var blocked = new BlockHistory(baseline))
            {
                baseline.Store.State.Ideas += "一"; baseline.Store.Save();
                Check(baseline.PersistedMemo == baseline.Store.State.Ideas && !String.IsNullOrEmpty(baseline.Store.HistoryNotice), "history-directory failure reports a warning while current memo autosave succeeds");
                baseline.Advance(1); baseline.Store.State.Ideas += "二"; baseline.Store.Save();
                Check(baseline.PersistedMemo == baseline.Store.State.Ideas, "later autosaves continue while the earliest failed history candidate remains pending");
            }
            baseline.Store.CheckpointMemoHistory(true);
            Check(baseline.Count == 2 && baseline.HasExact(earliest) && baseline.HasExact(baseline.CurrentBytes), "retry restores the earliest failed baseline before preserving the latest persisted final version");
            Check(String.IsNullOrEmpty(baseline.Store.HistoryNotice), "a successful history retry clears the historical warning");

            var checkpoint = new Fixture("failed-final-checkpoint", "最终检查点基线");
            checkpoint.Store.State.Ideas += "新内容"; checkpoint.Store.Save(); byte[] final = checkpoint.CurrentBytes;
            checkpoint.Advance(30);
            using (var blocked = new BlockHistory(checkpoint))
            {
                Check(!checkpoint.Store.CheckpointMemoHistory(), "a real final-checkpoint directory failure returns failure");
                Check(checkpoint.CurrentBytes.SequenceEqual(final) && !String.IsNullOrEmpty(checkpoint.Store.HistoryNotice), "checkpoint failure leaves successfully saved current data untouched and exposes the warning");
            }
            Check(checkpoint.Store.CheckpointMemoHistory(true), "a forced retry succeeds when history storage recovers");
            Check(checkpoint.Count == 2 && checkpoint.HasExact(final), "checkpoint failure retains the pending final version for retry without duplicate snapshots");
            checkpoint.Store.CheckpointMemoHistory(true);
            Check(checkpoint.Count == 2, "retrying an already recovered checkpoint is idempotent");
        }

        private static void PrimaryFailureRetainsPendingAndClock()
        {
            var f = new Fixture("failed-primary-clock", "主保存基线");
            f.Store.State.Ideas += "成功编辑"; f.Store.Save();
            string successfulMemo = f.Store.State.Ideas;
            byte[] current = f.CurrentBytes, backup = File.ReadAllBytes(f.Store.PathName + ".bak");
            int saved = 0, changed = 0;
            f.Store.Saved += delegate { saved++; }; f.Store.Changed += delegate { changed++; };
            f.Advance(20); f.Store.State.Ideas += "失败编辑"; string retryMemo = f.Store.State.Ideas;
            bool failed = false;
            using (var locked = new FileStream(f.Store.PathName, FileMode.Open, FileAccess.Read, FileShare.Read))
                try { f.Store.Save(); } catch (IOException) { failed = true; }
            Check(failed && saved == 0 && changed == 0, "a failed primary replacement emits no persistence or refresh success");
            Check(f.CurrentBytes.SequenceEqual(current) && File.ReadAllBytes(f.Store.PathName + ".bak").SequenceEqual(backup) && !File.Exists(f.Store.PathName + ".tmp"), "primary failure preserves exact current and backup bytes and removes its temporary file");
            f.Advance(10); f.Store.CheckpointMemoHistory(true);
            Check(f.Count == 1 && !f.HasMemo(retryMemo), "unpersisted text after primary failure cannot be forced into a final checkpoint");
            f.Store.State.Ideas = successfulMemo; f.Store.CheckpointMemoHistory();
            Check(f.Count == 2 && f.HasExact(current), "a failed save neither loses the prior pending version nor advances its successful-edit idle clock");
            f.Store.State.Ideas = retryMemo; f.Store.Save(); f.Store.CheckpointMemoHistory(true);
            Check(f.PersistedMemo == retryMemo && f.Count == 3 && f.HasExact(f.CurrentBytes), "retrying primary persistence then checkpointing retains the recovered new version once");

            var first = new Fixture("failed-first-edit", "没有已成功编辑会话");
            byte[] original = first.CurrentBytes;
            first.Store.State.Ideas += "未落盘"; failed = false;
            using (var locked = new FileStream(first.Store.PathName, FileMode.Open, FileAccess.Read, FileShare.Read))
                try { first.Store.Save(); } catch (IOException) { failed = true; }
            int historyAfterFailure = first.Count;
            first.Advance(600); first.Store.CheckpointMemoHistory(true);
            Check(failed && first.CurrentBytes.SequenceEqual(original) && first.Count == historyAfterFailure && !first.HasMemo(first.Store.State.Ideas), "a failed first edit creates no pending final revision for an unpersisted memo");
            first.Store.Save(); first.Store.CheckpointMemoHistory(true);
            Check(first.Count == 2 && first.HasExact(original) && first.HasExact(first.CurrentBytes), "a failed first edit remains recoverable on retry with exact baseline and final versions");
        }

        private static int Main()
        {
            try
            {
                ContinuousAppendAndIdle(); ContinuousBackspace(); ContinuousFiveMinuteCheckpoint(); ForceAndUnsavedInput(); ContinuousUnsavedMemoDeadline();
                DestructiveChanges(); RestartDeduplication(); OtherContentKeepsHistory(); SharedSaveAndViewChanges();
                HistoryFailureRetries(); PrimaryFailureRetainsPendingAndClock();
                Report.AppendLine("Assertions: " + assertions); return 0;
            }
            catch (Exception error) { Report.AppendLine(error.ToString()); return 1; }
            finally { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "verification-memo-history.txt"), Report.ToString(), new UTF8Encoding(false)); }
        }
    }
}
