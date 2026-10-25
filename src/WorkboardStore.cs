using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace LocalDesktopNote
{
    [DataContract]
    public sealed class WorkTask
    {
        [DataMember] public string Id;
        [DataMember] public string Text;
        [DataMember] public bool Done;
        [DataMember(EmitDefaultValue = false)] public string PlanDate;
        [DataMember(EmitDefaultValue = false)] public string CompletedAt;
        [DataMember(EmitDefaultValue = false)] public int? RemovedIndex;
    }

    [DataContract]
    public sealed class WorkboardState
    {
        [DataMember] public int Version = 1;
        [DataMember] public string Ideas = "";
        [DataMember(EmitDefaultValue = false)] public string MemoTitle;
        [DataMember(EmitDefaultValue = false)] public Dictionary<string, string> MemoHistoryTitles;
        [DataMember] public string TaskDraft = "";
        [DataMember] public List<WorkTask> Tasks = new List<WorkTask>();
        [DataMember] public List<WorkTask> Removed = new List<WorkTask>();
        [DataMember] public int X, Y, Width = 450, Height = 340;
        [DataMember] public bool Positioned, Hidden, Locked;
        [DataMember] public bool CompletedExpanded;
        [DataMember] public bool ShowCardOnStartup = true;
        [DataMember] public string TaskScope = "today";
        [DataMember(EmitDefaultValue = false)] public string DraftPlanDate;
        [DataMember(EmitDefaultValue = false)] public double? WorkbenchMemoTaskRatio;
        [DataMember(EmitDefaultValue = false)] public double? CardMemoTaskRatio;
        [DataMember(EmitDefaultValue = false)] public List<MemoPaper> MemoPapers;

        [OnDeserializing]
        private void InitializeNewDefaults(StreamingContext context)
        {
            ShowCardOnStartup = true;
            TaskScope = "today";
        }
    }

    internal sealed class WorkboardStore
    {
        internal readonly string PathName;
        internal readonly WorkboardState State;
        internal readonly string HistoryFolder;
        internal string Notice;
        internal string HistoryNotice;
        internal Func<DateTime> TodayProvider = delegate { return DateTime.Today; };
        internal Func<DateTime> UtcNowProvider = delegate { return DateTime.UtcNow; };
        internal DateTime Today { get { return TodayProvider().Date; } }
        internal event EventHandler Changed;
        internal event EventHandler Saved;
        internal event EventHandler MemoReplaced;
        internal long MemoPaperGeneration { get; private set; }
        private readonly Stack<MemoPaperChange> memoUndo = new Stack<MemoPaperChange>();
        private readonly Stack<MemoPaperChange> memoRedo = new Stack<MemoPaperChange>();
        internal bool CanUndoMemoPaper { get { return memoUndo.Count != 0; } }
        internal bool CanRedoMemoPaper { get { return memoRedo.Count != 0; } }

        private sealed class MemoPaperChange
        {
            internal readonly string Before, After, BeforeTitle, AfterTitle;
            internal MemoPaperChange(string before, string after, string beforeTitle, string afterTitle)
            { Before = before; After = after; BeforeTitle = beforeTitle; AfterTitle = afterTitle; }
        }
        private bool preserveBackup;
        private bool notifying;
        private byte[] previousSavedBytes;
        private byte[] previousContentBytes;
        private byte[] previousOtherContentBytes, lastHistoryContentBytes;
        private byte[] pendingHistoryBytes, pendingHistoryContentBytes;
        private string previousIdeas, memoHistoryAnchor;
        private bool memoHistoryPending, taskHistoryPending;
        private DateTime? unsavedMemoSinceUtc;
        private DateTime lastMemoEditUtc, memoCheckpointUtc, historyRetryUtc;
        private static readonly TimeSpan MemoIdle = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan MemoInterval = TimeSpan.FromMinutes(5);

        // Continuous typing can keep postponing the editor's short debounce.
        // Ask the UI to flush first; history still accepts only persisted bytes.
        internal bool MemoAutosaveDue
        {
            get
            {
                if (!unsavedMemoSinceUtc.HasValue || previousIdeas == (State.Ideas ?? "")) return false;
                DateTime started = memoHistoryPending ? memoCheckpointUtc : unsavedMemoSinceUtc.Value;
                return UtcNowProvider() - started >= MemoInterval;
            }
        }

        internal WorkboardStore(string folder)
        {
            Directory.CreateDirectory(folder);
            PathName = Path.Combine(folder, "workboard.json");
            HistoryFolder = Path.Combine(folder, "history", "workboard");
            State = Load();
            previousContentBytes = ContentBytes(State);
            previousOtherContentBytes = ContentBytes(State, false);
            previousIdeas = State.Ideas ?? "";
            memoHistoryAnchor = previousIdeas;
            State.Ideas = State.Ideas ?? "";
            State.TaskDraft = State.TaskDraft ?? "";
            State.Tasks = State.Tasks ?? new List<WorkTask>();
            State.Removed = State.Removed ?? new List<WorkTask>();
            State.Tasks.RemoveAll(t => t == null);
            State.Removed.RemoveAll(t => t == null);
            foreach (var task in State.Tasks) Normalize(task);
            foreach (var task in State.Removed) Normalize(task);
            if (State.TaskScope != "today" && State.TaskScope != "tomorrow" && State.TaskScope != "later" && State.TaskScope != "all") State.TaskScope = "today";
            // Freeze the original default once for an old, nonempty draft.
            // Existing task dates are deliberately left untouched.
            if (State.DraftPlanDate == null && !String.IsNullOrWhiteSpace(State.TaskDraft))
                State.DraftPlanDate = State.TaskScope == "later" ? "" : FormatPlanDate(State.TaskScope == "tomorrow" ? Today.AddDays(1) : Today);
            DateTime date;
            if (State.Tasks.Concat(State.Removed).Any(t => !String.IsNullOrEmpty(t.PlanDate) && !TryPlanDate(t.PlanDate, out date)))
                Notice = (String.IsNullOrEmpty(Notice) ? "" : Notice + "\n") + "部分任务的计划日期无法识别，已保留在“以后／未安排”中，可重新安排日期。";
            // A final checkpoint from the previous run is already a recovery version.
            // Compare content, not geometry or serialization bytes, when deduplicating it.
            try
            {
                string latest = Directory.Exists(HistoryFolder) ? Directory.EnumerateFiles(HistoryFolder, "*.json", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() : null;
                if (latest != null) lastHistoryContentBytes = ContentBytes(Read(latest));
            }
            catch { /* An unreadable history entry must not prevent loading current data. */ }
        }

        private static void Normalize(WorkTask task)
        {
            if (String.IsNullOrEmpty(task.Id)) task.Id = Guid.NewGuid().ToString("N");
            task.Text = task.Text ?? "";
        }

        private WorkboardState Read(string path)
        {
            var state = LibraryStore.Read<WorkboardState>(path);
            if (state == null || state.Version != 1) throw new SerializationException("无法识别的卡片格式");
            return state;
        }

        internal static bool TryPlanDate(string value, out DateTime date)
        {
            return DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
        }

        internal static string FormatPlanDate(DateTime? date)
        {
            return date.HasValue ? date.Value.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
        }

        internal IEnumerable<WorkTask> SelectTasks(string scope)
        {
            DateTime today = Today;
            DateTime tomorrow = today == DateTime.MaxValue.Date ? today : today.AddDays(1);
            return State.Tasks.Where(delegate(WorkTask task)
            {
                if (scope == "all") return true;
                DateTime date;
                bool planned = TryPlanDate(task.PlanDate, out date);
                if (scope == "today")
                {
                    if (!planned || date > today) return false;
                    if (date == today || !task.Done) return true;
                    DateTimeOffset completed;
                    return DateTimeOffset.TryParse(task.CompletedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out completed) && completed.LocalDateTime.Date == today;
                }
                if (scope == "tomorrow") return planned && date == tomorrow && tomorrow != today;
                return !planned || date > tomorrow;
            });
        }

        private void Preserve(string path)
        {
            string recovery = Path.Combine(Path.GetDirectoryName(PathName), "workboard-recovery-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".json");
            File.Copy(path, recovery, false);
        }

        private WorkboardState Load()
        {
            if (File.Exists(PathName))
            {
                try { var loaded = Read(PathName); previousSavedBytes = File.ReadAllBytes(PathName); return loaded; }
                catch { Preserve(PathName); preserveBackup = true; }
            }
            if (File.Exists(PathName + ".bak"))
            {
                try { var recovered = Read(PathName + ".bak"); previousSavedBytes = File.ReadAllBytes(PathName + ".bak"); Notice = "卡片已从上一次保存恢复；异常文件已保留。"; return recovered; }
                catch { Preserve(PathName + ".bak"); preserveBackup = true; }
            }
            if (preserveBackup) Notice = "卡片数据或备份暂时无法读取，未能加载原有内容；异常文件已另存为 workboard-recovery 文件，可用于恢复。";
            return new WorkboardState();
        }

        internal WorkTask AddTask(string text)
        {
            return AddTask(text, Today);
        }

        internal WorkTask AddTask(string text, DateTime? date)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) return null;
            var task = new WorkTask { Id = Guid.NewGuid().ToString("N"), Text = text, PlanDate = FormatPlanDate(date) };
            State.Tasks.Add(task); State.TaskDraft = "";
            return task;
        }

        internal void SetCompleted(WorkTask task, bool completed)
        {
            if (!State.Tasks.Contains(task) || task.Done == completed) return;
            task.Done = completed;
            task.CompletedAt = completed ? DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) : null;
        }

        internal void SetPlanDate(WorkTask task, DateTime? date)
        {
            if (!State.Tasks.Contains(task)) return;
            task.PlanDate = FormatPlanDate(date);
        }

        internal void Remove(WorkTask task)
        {
            int index = State.Tasks.IndexOf(task);
            if (index < 0) return;
            task.RemovedIndex = index;
            if (!State.Tasks.Remove(task)) return;
            State.Removed.Add(task);
        }

        internal WorkTask Undo()
        {
            if (State.Removed.Count == 0) return null;
            var task = State.Removed[State.Removed.Count - 1];
            State.Removed.RemoveAt(State.Removed.Count - 1);
            int index = task.RemovedIndex.HasValue ? Math.Max(0, Math.Min(task.RemovedIndex.Value, State.Tasks.Count)) : State.Tasks.Count;
            State.Tasks.Insert(index, task); task.RemovedIndex = null; return task;
        }

        internal MemoPaper StoreCurrentMemo(string kind, string expectedText = null, long? expectedGeneration = null)
        {
            if (kind != "archive" && kind != "trash")
                throw new ArgumentException("请选择收起或回收随手记。", "kind");
            string current = State.Ideas ?? "";
            if ((expectedGeneration.HasValue && expectedGeneration.Value != MemoPaperGeneration) ||
                (expectedText != null && !String.Equals(current, expectedText, StringComparison.Ordinal)))
                throw new InvalidOperationException("随手记已发生变化，请按当前内容重新操作。");
            if (String.IsNullOrWhiteSpace(current)) return null;
            var paper = NewMemoPaper(current, kind);
            string title = State.MemoTitle;
            ReplaceMemo("", null, paper, delegate { RecordMemoChange(current, "", title, null); });
            return paper;
        }

        internal bool OpenMemoText(string text, string title = null)
        {
            text = text ?? "";
            title = NormalizeMemoTitle(title);
            string current = State.Ideas ?? "";
            string currentTitle = State.MemoTitle;
            if (String.Equals(current, text, StringComparison.Ordinal) && NormalizeMemoTitle(currentTitle) == title) return false;
            ReplaceMemo(text, title, String.IsNullOrWhiteSpace(current) ? null : NewMemoPaper(current, "archive"),
                delegate { RecordMemoChange(current, text, currentTitle, title); });
            return true;
        }

        private void RecordMemoChange(string before, string after, string beforeTitle, string afterTitle)
        {
            memoUndo.Push(new MemoPaperChange(before, after, beforeTitle, afterTitle));
            memoRedo.Clear();
        }

        internal bool UndoMemoPaper() { return ReplayMemoChange(false); }
        internal bool RedoMemoPaper() { return ReplayMemoChange(true); }

        private bool ReplayMemoChange(bool redo)
        {
            Stack<MemoPaperChange> from = redo ? memoRedo : memoUndo;
            Stack<MemoPaperChange> to = redo ? memoUndo : memoRedo;
            if (from.Count == 0) return false;
            MemoPaperChange change = from.Peek();
            string expected = redo ? change.Before : change.After;
            string target = redo ? change.After : change.Before;
            string expectedTitle = redo ? change.BeforeTitle : change.AfterTitle;
            string targetTitle = redo ? change.AfterTitle : change.BeforeTitle;
            string current = State.Ideas ?? "";
            string currentTitle = State.MemoTitle;
            // Typing since the paper action is a separate editor buffer. Keep it
            // as an archive before replaying the paper action, never overwrite it.
            MemoPaper protection = !String.IsNullOrWhiteSpace(current) &&
                (current != expected || NormalizeMemoTitle(currentTitle) != NormalizeMemoTitle(expectedTitle)) &&
                (current != target || NormalizeMemoTitle(currentTitle) != NormalizeMemoTitle(targetTitle))
                ? NewMemoPaper(current, "archive") : null;
            ReplaceMemo(target, targetTitle, protection, delegate
            {
                from.Pop();
                // A reverse command returns to the actual page we just left,
                // including any writing since the original paper action.
                to.Push(redo ? new MemoPaperChange(current, target, currentTitle, targetTitle) : new MemoPaperChange(target, current, targetTitle, currentTitle));
            });
            return true;
        }

        private MemoPaper NewMemoPaper(string text, string kind)
        {
            return new MemoPaper { Id = Guid.NewGuid().ToString("N"), Text = text,
                StoredAtUtc = UtcNowProvider().ToUniversalTime().ToString("o", CultureInfo.InvariantCulture), Kind = kind, Title = NormalizeMemoTitle(State.MemoTitle) };
        }

        private void ReplaceMemo(string text, string title, MemoPaper paper, Action historyCommitted)
        {
            string previousText = State.Ideas;
            string previousTitle = State.MemoTitle;
            List<MemoPaper> previousPapers = State.MemoPapers;
            if (paper != null)
            {
                State.MemoPapers = previousPapers == null ? new List<MemoPaper>() : new List<MemoPaper>(previousPapers);
                State.MemoPapers.Add(paper);
            }
            State.Ideas = text;
            State.MemoTitle = NormalizeMemoTitle(title);
            bool committed = false;
            try
            {
                Save(delegate
                {
                    committed = true;
                    // Even identical text taken back onto a new sheet invalidates
                    // an older drag. Advance only with durable primary replacement.
                    MemoPaperGeneration = unchecked(MemoPaperGeneration + 1);
                    // Publish availability with the same durable commit as the
                    // text replacement so every shared window sees one history.
                    historyCommitted();
                });
            }
            catch
            {
                if (!committed)
                {
                    State.Ideas = previousText;
                    State.MemoTitle = previousTitle;
                    State.MemoPapers = previousPapers;
                    throw;
                }
                // A refresh failure after the atomic replacement cannot undo the
                // persisted memo or make the UI retry an already completed action.
                Notice = "内容已保存，部分界面未及时刷新；重新打开窗口后可查看。";
            }
            NotifyHandlers(MemoReplaced);
        }

        internal const int MemoTitleLimit = 80;
        internal static string NormalizeMemoTitle(string title)
        {
            string value = (title ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (value.Length == 0) return null;
            int length = Math.Min(value.Length, MemoTitleLimit);
            if (length < value.Length && Char.IsHighSurrogate(value[length - 1])) length--;
            return value.Substring(0, length);
        }

        private string HistoryTitleKey(string path)
        {
            string root = Path.GetFullPath(HistoryFolder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string fullPath = Path.GetFullPath(path);
            if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("请选择本机历史记录。", "path");
            return fullPath.Substring(root.Length).Replace('\\', '/');
        }

        internal string HistoryMemoTitle(string path, string fallback)
        {
            string title;
            return State.MemoHistoryTitles != null && State.MemoHistoryTitles.TryGetValue(HistoryTitleKey(path), out title) ? NormalizeMemoTitle(title) : NormalizeMemoTitle(fallback);
        }

        // Rename metadata belongs to the primary state. Original snapshots and
        // their timestamps remain untouched, including for legacy records.
        internal bool RenameMemo(string paperId, string snapshotPath, string title)
        {
            title = NormalizeMemoTitle(title);
            MemoPaper paper = snapshotPath == null ? (State.MemoPapers ?? new List<MemoPaper>()).FirstOrDefault(value => value != null && value.Id == paperId) : null;
            if (snapshotPath == null && paper == null) return false;
            if (snapshotPath != null) ReadHistorySnapshot(snapshotPath);
            string previousTitle = paper == null ? null : paper.Title;
            var previousTitles = State.MemoHistoryTitles;
            if (paper != null) paper.Title = title;
            else
            {
                State.MemoHistoryTitles = previousTitles == null ? new Dictionary<string, string>() : new Dictionary<string, string>(previousTitles);
                State.MemoHistoryTitles[HistoryTitleKey(snapshotPath)] = title;
            }
            bool committed = false;
            try { Save(delegate { committed = true; }); }
            catch
            {
                if (!committed) { if (paper != null) paper.Title = previousTitle; State.MemoHistoryTitles = previousTitles; throw; }
            }
            return true;
        }

        internal void Save()
        {
            Save(null);
        }

        private void Save(Action committed)
        {
            byte[] bytes;
            using (var buffer = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(WorkboardState)).WriteObject(buffer, State);
                bytes = buffer.ToArray();
            }
            byte[] contentBytes = ContentBytes(State);
            byte[] otherContentBytes = ContentBytes(State, false);
            DateTime now = UtcNowProvider();
            string previousMemo = previousIdeas;
            bool memoChanged = previousIdeas != (State.Ideas ?? "");
            bool hadSavedVersion = previousSavedBytes != null;
            bool otherContentChanged = !otherContentBytes.SequenceEqual(previousOtherContentBytes);
            bool newMemoSession = !memoHistoryPending;
            bool primaryExists = File.Exists(PathName);
            bool primaryMatchesSaved = primaryExists && previousSavedBytes != null && File.ReadAllBytes(PathName).SequenceEqual(previousSavedBytes);
            if (primaryExists && previousSavedBytes != null && !primaryMatchesSaved && !preserveBackup)
            {
                // An unexpected on-disk change must not become the next known-good backup.
                Preserve(PathName);
                preserveBackup = true;
                Notice = "检测到卡片文件在运行期间发生变化，已保留该文件，并重新保存当前内容。";
            }
            if (!preserveBackup && primaryMatchesSaved && bytes.SequenceEqual(previousSavedBytes))
            {
                if (committed != null) committed();
                CheckpointMemoHistory();
                NotifySaved();
                NotifyChanged();
                return;
            }
            string temporary = PathName + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                if (previousSavedBytes != null && !contentBytes.SequenceEqual(previousContentBytes) &&
                    (otherContentChanged || !memoChanged || newMemoSession || RemovesSubstantialText(previousIdeas, State.Ideas ?? "")))
                    PreserveHistory(previousSavedBytes, previousContentBytes, false);
                if (File.Exists(PathName)) File.Replace(temporary, PathName, preserveBackup ? null : PathName + ".bak");
                else File.Move(temporary, PathName);
                previousSavedBytes = bytes;
                previousContentBytes = contentBytes;
                previousOtherContentBytes = otherContentBytes;
                previousIdeas = State.Ideas ?? "";
                unsavedMemoSinceUtc = null;
                if (memoChanged)
                {
                    if (newMemoSession) { memoCheckpointUtc = now; memoHistoryAnchor = previousMemo; }
                    lastMemoEditUtc = now;
                    memoHistoryPending = true;
                }
                preserveBackup = false;
                if (committed != null) committed();
            }
            finally { if (File.Exists(temporary)) try { File.Delete(temporary); } catch { } }
            // A committed task change is visible in history only after primary
            // persistence succeeds; autosaved task drafts never enter this path.
            if (hadSavedVersion && otherContentChanged) taskHistoryPending = true;
            CheckpointMemoHistory();
            NotifySaved();
            NotifyChanged();
        }

        private void NotifySaved()
        {
            NotifyHandlers(Saved);
        }

        private void NotifyHandlers(EventHandler handler)
        {
            if (handler == null) return;
            foreach (EventHandler subscriber in handler.GetInvocationList())
                try { subscriber(this, EventArgs.Empty); }
                catch
                {
                    const string message = "部分界面未及时刷新；重新打开窗口后可查看。";
                    if (String.IsNullOrEmpty(Notice)) Notice = message;
                    else if (!Notice.Contains(message)) Notice += "\n" + message;
                }
        }

        private static byte[] ContentBytes(WorkboardState state, bool includeIdeas = true)
        {
            // Drafts are autosaved for recovery, but are not committed task revisions.
            var content = new WorkboardState
            {
                Ideas = includeIdeas ? state.Ideas ?? "" : "",
                Tasks = state.Tasks ?? new List<WorkTask>(),
                Removed = state.Removed ?? new List<WorkTask>()
            };
            using (var buffer = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(WorkboardState)).WriteObject(buffer, content);
                return buffer.ToArray();
            }
        }

        private static bool RemovesSubstantialText(string before, string after)
        {
            if (before.Length == 0 || before == after) return false;
            if (String.IsNullOrWhiteSpace(after)) return true;
            int prefix = 0, suffix = 0;
            while (prefix < before.Length && prefix < after.Length && before[prefix] == after[prefix]) prefix++;
            while (suffix < before.Length - prefix && suffix < after.Length - prefix && before[before.Length - suffix - 1] == after[after.Length - suffix - 1]) suffix++;
            int removed = before.Length - prefix - suffix;
            return removed >= 20 && removed >= before.Length * 0.3;
        }

        internal static bool IsMeaningfulMemoRevision(string before, string after)
        {
            before = (before ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
            after = (after ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
            if (before.Trim() == after.Trim()) return false;
            if (RemovesSubstantialText(before, after)) return true;
            int prefix = 0, suffix = 0;
            while (prefix < before.Length && prefix < after.Length && before[prefix] == after[prefix]) prefix++;
            while (suffix < before.Length - prefix && suffix < after.Length - prefix && before[before.Length - suffix - 1] == after[after.Length - suffix - 1]) suffix++;
            string added = after.Substring(prefix, after.Length - prefix - suffix);
            int changed = added.Count(c => !Char.IsWhiteSpace(c));
            // A corrected character inside the same sentence is not another version.
            // Punctuation/paragraph boundaries are local heuristics, not semantic AI.
            if (changed >= 40) return true;
            // Starting a new line is not finishing its still-unwritten paragraph.
            if (after.EndsWith("\n", StringComparison.Ordinal) && added.IndexOf('\n') >= 0 && changed > 0) return true;
            string tail = after.TrimEnd(' ', '\t', '\n', '”', '’', '"', '\'');
            if (tail.Length == 0 || changed == 0) return false;
            char last = tail[tail.Length - 1];
            if ("。！？!?；;".IndexOf(last) >= 0 && added.IndexOf(last) >= 0) return true;
            if (last == '.' && added.IndexOf('.') >= 0 && (tail.Length < 2 || !Char.IsDigit(tail[tail.Length - 2]))) return true;
            return false;
        }

        // Current content is saved independently. Only successfully persisted memo
        // versions can become checkpoints; a timer never promotes unsaved input.
        internal bool CheckpointMemoHistory(bool force = false)
        {
            DateTime now = UtcNowProvider();
            if (pendingHistoryBytes != null && (force || now >= historyRetryUtc))
                if (!PreserveHistory(pendingHistoryBytes, pendingHistoryContentBytes, force)) return false;
            if (taskHistoryPending)
            {
                if (!PreserveHistory(previousSavedBytes, previousContentBytes, force)) return false;
                taskHistoryPending = false;
            }
            if (!memoHistoryPending || previousSavedBytes == null) return true;
            if (!force && now - lastMemoEditUtc < MemoIdle && now - memoCheckpointUtc < MemoInterval) return true;
            if (!force && now - memoCheckpointUtc < MemoInterval && !IsMeaningfulMemoRevision(memoHistoryAnchor, previousIdeas)) return true;
            if (!ContentBytes(State).SequenceEqual(previousContentBytes)) return true;
            if (!PreserveHistory(previousSavedBytes, previousContentBytes, force)) return false;
            memoHistoryPending = false;
            memoHistoryAnchor = previousIdeas;
            memoCheckpointUtc = now;
            return true;
        }

        private bool PreserveHistory(byte[] bytes, byte[] contentBytes, bool force)
        {
            // Keep the earliest failed recovery candidate in memory while current
            // autosaves continue. Retry it before writing a newer checkpoint.
            if (pendingHistoryBytes != null)
            {
                if (!force && UtcNowProvider() < historyRetryUtc) return false;
                if (!WriteHistory(pendingHistoryBytes, pendingHistoryContentBytes)) return false;
                pendingHistoryBytes = pendingHistoryContentBytes = null;
            }
            if (WriteHistory(bytes, contentBytes)) return true;
            pendingHistoryBytes = bytes;
            pendingHistoryContentBytes = contentBytes;
            return false;
        }

        private bool WriteHistory(byte[] bytes, byte[] contentBytes)
        {
            if (lastHistoryContentBytes != null && lastHistoryContentBytes.SequenceEqual(contentBytes)) { HistoryNotice = null; return true; }
            string temporary = null;
            try
            {
                bytes = HistoryPayload(bytes);
                string folder = Path.Combine(HistoryFolder, Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, UtcNowProvider().ToLocalTime().ToString("HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N") + ".json");
                temporary = path + ".tmp";
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                File.Move(temporary, path);
                lastHistoryContentBytes = contentBytes;
                HistoryNotice = null;
                return true;
            }
            catch (Exception)
            {
                HistoryNotice = "本次历史快照未能保存；当前内容仍会保存，请检查 history 文件夹的权限和可用空间。";
                historyRetryUtc = UtcNowProvider() + MemoIdle;
                return false;
            }
            finally { if (temporary != null && File.Exists(temporary)) try { File.Delete(temporary); } catch { } }
        }

        private static byte[] HistoryPayload(byte[] bytes)
        {
            WorkboardState snapshot;
            using (var buffer = new MemoryStream(bytes))
                snapshot = (WorkboardState)new DataContractJsonSerializer(typeof(WorkboardState)).ReadObject(buffer);
            // The primary file owns stored papers. Repeating the entire paper
            // collection in every recovery snapshot would grow quadratically.
            if (snapshot.MemoPapers == null && snapshot.MemoHistoryTitles == null && String.IsNullOrEmpty(snapshot.TaskDraft) && snapshot.DraftPlanDate == null) return bytes;
            snapshot.MemoPapers = null;
            snapshot.MemoHistoryTitles = null;
            snapshot.TaskDraft = "";
            snapshot.DraftPlanDate = null;
            using (var buffer = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(WorkboardState)).WriteObject(buffer, snapshot);
                return buffer.ToArray();
            }
        }

        internal WorkboardState ReadHistorySnapshot(string path)
        {
            string root = Path.GetFullPath(HistoryFolder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("请选择 history/workboard 中的历史快照。", "path");
            return Read(path);
        }

        internal void NotifyChanged()
        {
            if (previousIdeas == (State.Ideas ?? "")) unsavedMemoSinceUtc = null;
            else if (!unsavedMemoSinceUtc.HasValue) unsavedMemoSinceUtc = UtcNowProvider();
            if (notifying) return;
            try
            {
                notifying = true;
                NotifyHandlers(Changed);
            }
            finally { notifying = false; }
        }
    }
}
