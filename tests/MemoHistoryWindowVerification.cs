using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Forms = System.Windows.Forms;

namespace LocalDesktopNote
{
    // Real WPF TextChanged/Click and WinForms closing handlers, without Show/Attach.
    internal static class MemoHistoryWindowVerification
    {
        private static readonly StringBuilder Report = new StringBuilder();
        private static int passed, failed;

        private static void Check(bool condition, string message)
        {
            Report.AppendLine((condition ? "PASS: " : "FAIL: ") + message);
            if (condition) passed++; else failed++;
        }
        private static void Run(string name, Action action)
        {
            Report.AppendLine("CASE: " + name);
            try { action(); } catch (Exception error) { failed++; Report.AppendLine("FAIL: " + error); }
        }
        private static T Field<T>(object value, string name)
        {
            return (T)value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(value);
        }
        private static void SetField(object value, string name, object field)
        {
            value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(value, field);
        }
        private static Forms.FormClosingEventArgs Closing(Forms.Form window)
        {
            var args = new Forms.FormClosingEventArgs(Forms.CloseReason.UserClosing, false);
            typeof(Forms.Form).GetMethod("OnFormClosing", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, new object[] { args });
            return args;
        }

        private sealed class Fixture : IDisposable
        {
            internal readonly WorkboardStore Store;
            internal readonly WorkboardWindow Card;
            internal readonly WorkbenchWindow Bench;
            internal readonly NoteContext Context;
            internal DateTime Clock = new DateTime(2026, 10, 22, 10, 0, 0, DateTimeKind.Utc);
            internal int Saved;
            private readonly string folder;

            internal Fixture(string name)
            {
                folder = Path.Combine(Storage.Folder, "fixture", name + "-" + Guid.NewGuid().ToString("N"));
                var seed = Open(); seed.State.Ideas = "已有随手记"; seed.Save();
                Store = Open(); Card = new WorkboardWindow(Store); Bench = new WorkbenchWindow(Store);
                // Exercise the monitor's exact helper without creating a tray,
                // starting its timer, migrating files, or attaching a desktop card.
                Context = (NoteContext)FormatterServices.GetUninitializedObject(typeof(NoteContext));
                SetField(Context, "workboardStore", Store); SetField(Context, "workboard", Card); SetField(Context, "workbench", Bench);
                Store.Saved += delegate { Saved++; };
            }
            private WorkboardStore Open()
            {
                var store = new WorkboardStore(folder);
                store.UtcNowProvider = delegate { return Clock; };
                store.TodayProvider = delegate { return new DateTime(2026, 10, 22); };
                return store;
            }
            internal int HistoryCount { get { return HistoryFiles.Length; } }
            internal string[] HistoryFiles { get { return Directory.Exists(Store.HistoryFolder) ? Directory.GetFiles(Store.HistoryFolder, "*.json", SearchOption.AllDirectories) : new string[0]; } }
            internal string PersistedMemo { get { return LibraryStore.Read<WorkboardState>(Store.PathName).Ideas; } }
            internal bool HasMemo(string memo) { return HistoryFiles.Any(path => Store.ReadHistorySnapshot(path).Ideas == memo); }
            internal bool HasExact(byte[] bytes) { return HistoryFiles.Any(path => File.ReadAllBytes(path).SequenceEqual(bytes)); }
            internal string CardStatus { get { return ((TextBlock)Card.View.FindName("CardSaveStatus")).Text; } }
            internal string BenchStatus { get { return Field<TextBlock>(Bench, "status").Text; } }
            internal TextBox Editor(bool card) { return card ? Card.IdeasEditor : Bench.IdeasEditor; }
            internal bool Flush(bool card) { return card ? Card.Flush() : Bench.Flush(); }
            internal bool Finish(bool card) { return card ? Card.FinishMemoHistory() : Bench.FinishMemoHistory(); }
            internal string Status(bool card) { return card ? CardStatus : BenchStatus; }
            internal object Window(bool card) { return card ? (object)Card : Bench; }
            internal void RefreshStatus(bool card) { if (card) Card.RefreshHistoryStatus(); else Bench.RefreshHistoryStatus(); }
            internal void Advance(double seconds) { Clock = Clock.AddSeconds(seconds); }
            internal void Navigate(string page)
            {
                Field<Dictionary<string, Button>>(Bench, "pageButtons")[page].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            public void Dispose() { Card.Dispose(); Bench.Dispose(); }
        }

        private sealed class BlockHistory : IDisposable
        {
            private readonly string path, held;
            internal BlockHistory(Fixture fixture)
            {
                path = fixture.Store.HistoryFolder;
                held = Path.Combine(Path.GetDirectoryName(fixture.Store.PathName), "held-history-" + Guid.NewGuid().ToString("N"));
                if (Directory.Exists(path)) Directory.Move(path, held);
                Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, "blocks history directory creation");
            }
            public void Dispose()
            {
                File.Delete(path); if (Directory.Exists(held)) Directory.Move(held, path);
            }
        }

        private static void SharedTypingAndIdle()
        {
            using (var f = new Fixture("shared-typing-idle"))
            {
                byte[] before = File.ReadAllBytes(f.Store.PathName);
                for (int index = 0; index < 20; index++)
                {
                    bool card = index % 2 == 0;
                    f.Advance(1); f.Editor(card).Text += "字";
                    Check(f.Store.State.Ideas == f.Editor(card).Text && f.Editor(!card).Text == f.Editor(card).Text,
                        "real TextChanged synchronizes the shared memo from " + (card ? "card" : "workbench") + " edit " + (index + 1));
                    Check(f.Flush(card) && f.PersistedMemo == f.Store.State.Ideas, "real Flush persists shared memo edit " + (index + 1));
                }
                Check(f.HistoryCount == 1 && f.HasExact(before), "alternating actual window edits produce one exact pre-edit recovery baseline");
                Check(!Field<bool>(f.Card, "dirty") && !Field<bool>(f.Bench, "dirty") &&
                    !Field<Forms.Timer>(f.Card, "saveTimer").Enabled && !Field<Forms.Timer>(f.Bench, "saveTimer").Enabled,
                    "shared Saved acknowledgements clear both window dirty flags and debounce timers");
                f.Advance(29.999); f.Context.CheckpointMemoHistory();
                Check(f.HistoryCount == 1, "shared cleared dirty flags do not create a final checkpoint before 30 seconds");
                f.Advance(0.001); f.Context.CheckpointMemoHistory();
                Check(f.HistoryCount == 2 && f.HasExact(File.ReadAllBytes(f.Store.PathName)), "shared store still checkpoints the final persisted memo after 30 seconds with both windows clean");
            }
        }

        private static void WorkbenchNavigation()
        {
            using (var f = new Fixture("workbench-navigation"))
            {
                f.Navigate("memo"); f.Bench.IdeasEditor.Text += "导航前的最终内容";
                Check(f.Bench.Flush() && f.HistoryCount == 1, "navigation fixture starts with one persisted memo editing session");
                f.Navigate("history");
                Check(f.HistoryCount == 2 && f.HasMemo(f.PersistedMemo), "actual history navigation button finishes the memo session immediately");
                Check(f.Bench.HistoryList.Items.Count == 2 && f.Bench.HistoryPreview.IsReadOnly && f.Bench.HistoryPreview.Text.Contains("随手记"), "history navigation renders both recovery versions through the actual human-readable read-only preview");
                f.Navigate("history"); f.Navigate("memo"); f.Navigate("history");
                Check(f.HistoryCount == 2, "repeated navigation and leaving an unchanged memo do not duplicate snapshots");
            }
        }

        private static void FinishPaths(bool card)
        {
            using (var f = new Fixture(card ? "card-finish" : "bench-finish"))
            {
                f.Editor(card).Text += "尚在输入区的最终内容";
                Check(f.Finish(card) && f.PersistedMemo == f.Store.State.Ideas && f.HistoryCount == 2 && f.HasMemo(f.PersistedMemo),
                    (card ? "card" : "workbench") + " FinishMemoHistory used by Ctrl+S flushes first and checkpoints its persisted final value");
                Check(f.Finish(card) && f.HistoryCount == 2, (card ? "card" : "workbench") + " repeated Ctrl+S persistence helper is idempotent");
                Check(!f.Card.Visible && !f.Bench.Visible, "finishing the memo uses the persistence phase without showing or attaching either form");
                if (card)
                {
                    f.Card.EndEditing();
                    Check(f.HistoryCount == 2, "the card persistence helper settles history before an inactive EndEditing leaves it untouched");
                }
            }
            using (var f = new Fixture(card ? "card-finish-failure" : "bench-finish-failure"))
            {
                string before = f.PersistedMemo; f.Editor(card).Text += "写盘失败的未保存内容";
                Directory.CreateDirectory(f.Store.PathName + ".tmp");
                try
                {
                    Check(!f.Finish(card) && f.PersistedMemo == before && f.HistoryCount == 0 && !f.HasMemo(f.Editor(card).Text),
                        (card ? "card" : "workbench") + " FinishMemoHistory stops on real primary-save failure and never archives unpersisted input");
                    Check(Field<bool>(f.Window(card), "dirty") && Field<bool>(f.Window(card), "saveFailed") && f.Status(card).Contains("保存失败"),
                        (card ? "card" : "workbench") + " failed finish retains retryable content and failure feedback");
                }
                finally { Directory.Delete(f.Store.PathName + ".tmp"); }
                Check(f.Finish(card) && f.HistoryCount == 2 && f.HasMemo(f.PersistedMemo), (card ? "card" : "workbench") + " finish retries and preserves the final memo once storage recovers");
            }
        }

        private static void WorkbenchClosing()
        {
            using (var f = new Fixture("workbench-closing"))
            {
                f.Bench.IdeasEditor.Text += "关闭前最后输入";
                var args = Closing(f.Bench);
                Check(args.Cancel && !f.Bench.Visible && !f.Bench.IsDisposed, "actual UserClosing handler hides the workbench and keeps its reusable form alive");
                Check(f.HistoryCount == 2 && f.HasMemo(f.PersistedMemo) && f.PersistedMemo == f.Bench.IdeasEditor.Text,
                    "workbench UserClosing flushes the latest input and checkpoints it before hiding");
                Closing(f.Bench);
                Check(f.HistoryCount == 2, "repeating the close lifecycle does not duplicate a completed memo version");
            }
            using (var f = new Fixture("workbench-closing-failure"))
            {
                string before = f.PersistedMemo; f.Bench.IdeasEditor.Text += "失败仍保留";
                Directory.CreateDirectory(f.Store.PathName + ".tmp");
                try
                {
                    var args = Closing(f.Bench);
                    Check(args.Cancel && f.PersistedMemo == before && f.HistoryCount == 0 && f.BenchStatus.Contains("保存失败"),
                        "failed actual workbench closing cancels before archiving any unpersisted input");
                }
                finally { Directory.Delete(f.Store.PathName + ".tmp"); }
            }
        }

        private static void ContinuousTypingDue(bool card)
        {
            using (var f = new Fixture(card ? "card-continuous-due" : "bench-continuous-due"))
            {
                string before = f.PersistedMemo;
                // No message pump: every real TextChanged restarts the 600 ms UI
                // debounce, while the injected clock advances in 250 ms steps.
                f.Editor(card).Text += "始";
                for (int index = 1; index < 1200; index++)
                {
                    f.Advance(0.25); f.Editor(card).Text += "续";
                }
                Check(!f.Store.MemoAutosaveDue && f.PersistedMemo == before && f.HistoryCount == 0,
                    (card ? "card" : "workbench") + " sub-debounce TextChanged activity at 299.75 seconds remains pending without a premature forced save");
                f.Advance(0.25); f.Editor(card).Text += "界";
                Check(f.Store.MemoAutosaveDue && Field<bool>(f.Window(card), "dirty"),
                    (card ? "card" : "workbench") + " five minutes of continuous real input exposes MemoAutosaveDue even without a debounce tick");
                f.Context.CheckpointMemoHistory();
                Check(f.PersistedMemo == f.Store.State.Ideas && !Field<bool>(f.Card, "dirty") && !Field<bool>(f.Bench, "dirty"),
                    (card ? "card" : "workbench") + " actual monitor helper flushes the latest editor value before checkpointing");
                Check(f.HistoryCount == 2 && f.HasMemo(before) && f.HasMemo(f.PersistedMemo) && !f.Store.MemoAutosaveDue,
                    "continuous-input recovery stores the original and final persisted versions and resets the due condition");
            }
        }

        private static void HistoryFeedbackRecovery()
        {
            using (var f = new Fixture("history-feedback-recovery"))
            {
                using (var blocked = new BlockHistory(f))
                {
                    f.Card.IdeasEditor.Text += "历史目录暂时失败"; f.Card.Flush();
                    f.Card.RefreshHistoryStatus(); f.Bench.RefreshHistoryStatus();
                    Check(!String.IsNullOrEmpty(f.Store.HistoryNotice) && f.CardStatus.Contains("历史") && f.BenchStatus.Contains("历史"),
                        "real history-directory failure is visible in both clean window status areas");
                }
                f.Advance(30); f.Context.CheckpointMemoHistory();
                Check(f.HistoryCount == 2 && String.IsNullOrEmpty(f.Store.HistoryNotice), "actual monitor helper retries recovered history storage and clears its warning");
                Check(!f.CardStatus.Contains("历史") && !f.BenchStatus.Contains("历史") && f.CardStatus.Contains("已保存") && f.BenchStatus.Contains("已保存"),
                    "both RefreshHistoryStatus methods clear a stale historical warning after successful recovery");
            }
        }

        private static void MonitorDueFailure(bool card)
        {
            using (var f = new Fixture(card ? "card-monitor-due-failure" : "bench-monitor-due-failure"))
            {
                string before = f.PersistedMemo; f.Editor(card).Text += "五分钟待保存内容"; f.Advance(300);
                Directory.CreateDirectory(f.Store.PathName + ".tmp");
                try
                {
                    f.Context.CheckpointMemoHistory();
                    Check(f.PersistedMemo == before && f.HistoryCount == 0 && f.Store.MemoAutosaveDue &&
                        Field<bool>(f.Window(card), "dirty") && Field<bool>(f.Window(card), "saveFailed"),
                        (card ? "card" : "workbench") + " monitor due processing preserves pending input and creates no final history when the real flush fails");
                }
                finally { Directory.Delete(f.Store.PathName + ".tmp"); }
                f.Context.CheckpointMemoHistory();
                Check(f.PersistedMemo == f.Store.State.Ideas && f.HistoryCount == 2 && f.HasMemo(f.PersistedMemo) && !f.Store.MemoAutosaveDue,
                    (card ? "card" : "workbench") + " next monitor helper call retries the due flush and final checkpoint once storage recovers");
            }
        }

        private static void MonitorWithoutStore()
        {
            var context = (NoteContext)FormatterServices.GetUninitializedObject(typeof(NoteContext));
            context.CheckpointMemoHistory();
            Check(true, "the exact monitor checkpoint helper safely handles a missing shared store");
        }

        private static void HistoryFeedbackGuards(bool card)
        {
            using (var f = new Fixture(card ? "card-history-feedback-guards" : "bench-history-feedback-guards"))
            {
                f.Editor(card).Text += "尚未落盘"; string dirtyStatus = f.Status(card);
                f.RefreshStatus(card);
                Check(Field<bool>(f.Window(card), "dirty") && !Field<bool>(f.Window(card), "saveFailed") && f.Status(card) == dirtyStatus,
                    (card ? "card" : "workbench") + " historical status refresh preserves current dirty-input feedback");
                Directory.CreateDirectory(f.Store.PathName + ".tmp");
                try
                {
                    Check(!f.Flush(card), "feedback guard fixture induces a real " + (card ? "card" : "workbench") + " primary-save failure");
                    string error = f.Status(card); f.RefreshStatus(card);
                    Check(Field<bool>(f.Window(card), "saveFailed") && error.Contains("保存失败") && f.Status(card) == error,
                        (card ? "card" : "workbench") + " historical status refresh never overwrites an unresolved primary-save error");
                }
                finally { Directory.Delete(f.Store.PathName + ".tmp"); }
                Check(f.Finish(card) && !Field<bool>(f.Window(card), "saveFailed"), "feedback fixture can retry successfully after the real storage obstacle is removed");
            }
        }

        [STAThread]
        private static int Main()
        {
            try
            {
                Forms.Application.EnableVisualStyles(); Forms.Application.SetCompatibleTextRenderingDefault(false);
                Run("shared real memo input and idle", SharedTypingAndIdle);
                Run("actual workbench history navigation", WorkbenchNavigation);
                Run("card finish and failure retry", delegate { FinishPaths(true); });
                Run("workbench finish and failure retry", delegate { FinishPaths(false); });
                Run("actual workbench closing", WorkbenchClosing);
                Run("card continuous sub-debounce input", delegate { ContinuousTypingDue(true); });
                Run("workbench continuous sub-debounce input", delegate { ContinuousTypingDue(false); });
                Run("history warning recovery", HistoryFeedbackRecovery);
                Run("monitor helper without store", MonitorWithoutStore);
                Run("card monitor due failure retry", delegate { MonitorDueFailure(true); });
                Run("workbench monitor due failure retry", delegate { MonitorDueFailure(false); });
                Run("card historical feedback guards", delegate { HistoryFeedbackGuards(true); });
                Run("workbench historical feedback guards", delegate { HistoryFeedbackGuards(false); });
                Report.AppendLine("Passed: " + passed + "; Failed: " + failed);
                Report.AppendLine("LIMIT: real hidden WinForms/WPF controls, TextChanged, routed Click, closing handlers, shared Ctrl+S/EndEditing persistence helper and the exact monitor checkpoint helper on an uninitialized NoteContext; injected clock. No Show/Attach, native focus, physical Ctrl+S, Explorer desktop attachment, live monitor timer, production data or registry changes.");
                return failed == 0 ? 0 : 1;
            }
            finally { File.WriteAllText(Path.Combine(Storage.Folder, "verification-memo-history-window.txt"), Report.ToString(), new UTF8Encoding(false)); }
        }
    }
}
