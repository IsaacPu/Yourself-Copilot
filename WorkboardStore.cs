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
        [DataMember] public string TaskDraft = "";
        [DataMember] public List<WorkTask> Tasks = new List<WorkTask>();
        [DataMember] public List<WorkTask> Removed = new List<WorkTask>();
        [DataMember] public int X, Y, Width = 450, Height = 340;
        [DataMember] public bool Positioned, Hidden, Locked;
        [DataMember] public bool CompletedExpanded;
        [DataMember] public bool ShowCardOnStartup = true;
        [DataMember] public string TaskScope = "today";
        [DataMember(EmitDefaultValue = false)] public string DraftPlanDate;

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
        internal DateTime Today { get { return TodayProvider().Date; } }
        internal event EventHandler Changed;
        internal event EventHandler Saved;
        private bool preserveBackup;
        private bool notifying;
        private byte[] previousSavedBytes;
        private byte[] previousContentBytes;

        internal WorkboardStore(string folder)
        {
            Directory.CreateDirectory(folder);
            PathName = Path.Combine(folder, "workboard.json");
            HistoryFolder = Path.Combine(folder, "history", "workboard");
            State = Load();
            previousContentBytes = ContentBytes(State);
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

        internal void Save()
        {
            byte[] bytes;
            using (var buffer = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(WorkboardState)).WriteObject(buffer, State);
                bytes = buffer.ToArray();
            }
            byte[] contentBytes = ContentBytes(State);
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
                if (previousSavedBytes != null && !contentBytes.SequenceEqual(previousContentBytes)) PreserveHistory(previousSavedBytes);
                if (File.Exists(PathName)) File.Replace(temporary, PathName, preserveBackup ? null : PathName + ".bak");
                else File.Move(temporary, PathName);
                previousSavedBytes = bytes;
                previousContentBytes = contentBytes;
                preserveBackup = false;
            }
            finally { if (File.Exists(temporary)) try { File.Delete(temporary); } catch { } }
            NotifySaved();
            NotifyChanged();
        }

        private void NotifySaved()
        {
            var handler = Saved;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private static byte[] ContentBytes(WorkboardState state)
        {
            // History follows written content, task order and task state; window settings are not revisions.
            var content = new WorkboardState
            {
                Ideas = state.Ideas ?? "",
                TaskDraft = state.TaskDraft ?? "",
                DraftPlanDate = state.DraftPlanDate,
                Tasks = state.Tasks ?? new List<WorkTask>(),
                Removed = state.Removed ?? new List<WorkTask>()
            };
            using (var buffer = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(WorkboardState)).WriteObject(buffer, content);
                return buffer.ToArray();
            }
        }

        private void PreserveHistory(byte[] bytes)
        {
            string temporary = null;
            try
            {
                string folder = Path.Combine(HistoryFolder, Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, DateTime.Now.ToString("HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N") + ".json");
                temporary = path + ".tmp";
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                File.Move(temporary, path);
                HistoryNotice = null;
            }
            catch (Exception)
            {
                HistoryNotice = "本次历史快照未能保存；当前内容仍会保存，请检查 history 文件夹的权限和可用空间。";
            }
            finally { if (temporary != null && File.Exists(temporary)) try { File.Delete(temporary); } catch { } }
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
            if (notifying) return;
            try
            {
                notifying = true;
                var handler = Changed;
                if (handler != null) handler(this, EventArgs.Empty);
            }
            finally { notifying = false; }
        }
    }
}
