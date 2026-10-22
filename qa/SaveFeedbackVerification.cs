using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Forms = System.Windows.Forms;

namespace LocalDesktopNote
{
    // Drives actual WPF handlers and shared-store paths without opening windows.
    internal static class SaveFeedbackVerification
    {
        private static readonly StringBuilder Report = new StringBuilder();
        private static int passed, failed;
        private static void Check(bool condition, string text)
        {
            Report.AppendLine((condition ? "PASS: " : "FAIL: ") + text);
            if (condition) passed++; else failed++;
        }
        private static void Run(string name, Action action)
        {
            Report.AppendLine("CASE: " + name);
            try { action(); } catch (Exception error) { failed++; Report.AppendLine("FAIL: " + error); }
        }
        private static void Pump(int milliseconds)
        {
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < milliseconds) { Forms.Application.DoEvents(); Thread.Sleep(5); }
        }
        private static T Field<T>(object value, string name)
        {
            return (T)value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(value);
        }
        private static void SetField(object value, string name, object field)
        {
            value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(value, field);
        }
        private sealed class Fixture : IDisposable
        {
            internal readonly WorkboardStore Store;
            internal readonly WorkboardWindow Card;
            internal readonly WorkbenchWindow Bench;
            internal int CardSaving, BenchSaving, Saved;
            private readonly TextBlock cardStatus, benchStatus;
            private readonly DependencyPropertyDescriptor descriptor;
            private readonly EventHandler cardChanged, benchChanged;
            internal string CardStatus { get { return cardStatus.Text; } }
            internal string BenchStatus { get { return benchStatus.Text; } }
            internal Fixture(string name)
            {
                Store = new WorkboardStore(Path.Combine(Storage.Folder, "fixture", name));
                Store.TodayProvider = delegate { return new DateTime(2026, 10, 22); };
                Store.AddTask("今天任务", Store.Today); Store.AddTask("明天任务", Store.Today.AddDays(1)); Store.Save();
                Card = new WorkboardWindow(Store); Bench = new WorkbenchWindow(Store);
                cardStatus = (TextBlock)Card.View.FindName("CardSaveStatus"); benchStatus = Field<TextBlock>(Bench, "status");
                descriptor = DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock));
                cardChanged = delegate { if (cardStatus.Text.Contains("正在保存")) CardSaving++; };
                benchChanged = delegate { if (benchStatus.Text.Contains("正在保存")) BenchSaving++; };
                descriptor.AddValueChanged(cardStatus, cardChanged); descriptor.AddValueChanged(benchStatus, benchChanged);
                Store.Saved += delegate { Saved++; };
            }
            internal int HistoryCount { get { return Directory.Exists(Store.HistoryFolder) ? Directory.GetFiles(Store.HistoryFolder, "*.json", SearchOption.AllDirectories).Length : 0; } }
            internal void Mark() { File.SetLastWriteTimeUtc(Store.PathName, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)); }
            public void Dispose()
            {
                descriptor.RemoveValueChanged(cardStatus, cardChanged); descriptor.RemoveValueChanged(benchStatus, benchChanged);
                Card.Dispose(); Bench.Dispose();
            }
        }
        private static void WpfScopeClicks()
        {
            using (var f = new Fixture("wpf-scope-clicks"))
            {
                f.Mark(); DateTime before = File.GetLastWriteTimeUtc(f.Store.PathName); int history = f.HistoryCount;
                foreach (string scope in new[] { "today", "tomorrow", "later", "all", "today" })
                    ((Button)f.Card.View.FindName("Scope_" + scope)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                foreach (string scope in new[] { "today", "tomorrow", "later", "all", "today" })
                    f.Bench.ScopeButtons[scope].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(f.CardSaving == 0 && f.BenchSaving == 0, "actual WPF scope Click routes and peer refresh stay free of saving feedback");
                Check(!Field<bool>(f.Card, "dirty") && !Field<bool>(f.Bench, "dirty") && !Field<Forms.Timer>(f.Card, "saveTimer").Enabled && !Field<Forms.Timer>(f.Bench, "saveTimer").Enabled, "scope Click routes create only view preferences, never content dirty or a content-save timer");
                Check(f.Saved == 0 && File.GetLastWriteTimeUtc(f.Store.PathName) == before && f.HistoryCount == history, "WPF filter clicks alone do not write disk or history");
                f.Card.Flush(); f.Bench.Flush();
                Check(File.GetLastWriteTimeUtc(f.Store.PathName) == before && f.HistoryCount == history, "returning to original scope then lifecycle flush does not rewrite identical content");
                Report.AppendLine("MEASURE scopes: cardSaving=" + f.CardSaving + "; benchSaving=" + f.BenchSaving + "; history=" + f.HistoryCount);
            }
        }
        private static void WpfFocusNoEdit()
        {
            using (var f = new Fixture("wpf-focus-no-edit"))
            {
                byte[] before = File.ReadAllBytes(f.Store.PathName);
                foreach (var title in f.Card.TaskEditors.Concat(f.Bench.TaskEditors))
                    title.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, Environment.TickCount, title, null) { RoutedEvent = Keyboard.LostKeyboardFocusEvent });
                f.Card.Flush(); f.Bench.Flush();
                Check(f.CardSaving == 0 && f.BenchSaving == 0 && File.ReadAllBytes(f.Store.PathName).SequenceEqual(before), "actual title LostKeyboardFocus routes without edits preserve quiet status and disk");
                Check(!Field<bool>(f.Card, "dirty") && !Field<bool>(f.Bench, "dirty") && f.Saved == 0, "focus without content edits does not schedule or invoke Store.Save");
            }
        }
        private static void CornerClickNoMovement()
        {
            using (var f = new Fixture("corner-click-no-movement"))
            {
                SetField(f.Card, "editing", true); // Prevent native desktop parenting; keep hidden.
                f.Card.Place(new System.Drawing.Rectangle(100, 100, 450, 340), false); f.Store.Save();
                f.Saved = 0; f.Mark(); DateTime before = File.GetLastWriteTimeUtc(f.Store.PathName); byte[] bytes = File.ReadAllBytes(f.Store.PathName); int history = f.HistoryCount;
                f.Card.BeginCornerResize(ResizeCorner.BottomRight, new System.Drawing.Point(550, 440)); f.Card.EndCornerResize();
                Check(f.CardSaving == 0, "click/release corner without movement does not claim content saving");
                Check(!Field<bool>(f.Card, "dirty") && !Field<Forms.Timer>(f.Card, "saveTimer").Enabled, "no-movement corner gesture does not mark content dirty or start a save timer");
                f.Card.Flush();
                Check(File.ReadAllBytes(f.Store.PathName).SequenceEqual(bytes) && File.GetLastWriteTimeUtc(f.Store.PathName) == before && f.HistoryCount == history, "no-movement corner gesture does not physically rewrite file or history");
                Check(f.Saved == 0, "no-movement gesture plus lifecycle flush need not invoke the store at all");
                Check(!f.Card.Visible && !f.Card.Attached, "corner feedback fixture remains hidden and unattached");
                Report.AppendLine("MEASURE no-motion: cardSaving=" + f.CardSaving + "; SavedEvents=" + f.Saved + "; physicalWrite=" + (File.GetLastWriteTimeUtc(f.Store.PathName) != before));
            }
        }
        private static void ChangedGeometry()
        {
            using (var f = new Fixture("changed-geometry"))
            {
                SetField(f.Card, "editing", true);
                f.Card.Place(new System.Drawing.Rectangle(100, 100, 450, 340), false); f.Store.Save(); f.Saved = 0;
                f.Mark(); DateTime before = File.GetLastWriteTimeUtc(f.Store.PathName); int history = f.HistoryCount;
                f.Card.BeginCornerResize(ResizeCorner.BottomRight, new System.Drawing.Point(550, 440));
                for (int i = 1; i <= 20; i++) f.Card.ResizeCornerTo(new System.Drawing.Point(550 + i, 440 + i));
                f.Card.EndCornerResize();
                Check(!Field<bool>(f.Card, "dirty") && Field<bool>(f.Card, "viewDirty") && f.CardSaving == 0, "actual geometry change is a quiet view preference, not content dirty");
                Check(f.Saved == 0 && File.GetLastWriteTimeUtc(f.Store.PathName) == before && f.HistoryCount == history, "twenty resize Move steps do not write disk or history before lifecycle flush");
                f.Card.Flush(); var reopened = new WorkboardStore(Path.GetDirectoryName(f.Store.PathName));
                Check(reopened.State.Width == 470 && reopened.State.Height == 360 && f.HistoryCount == history, "one geometry flush persists final dimensions without creating a content revision");
            }
        }
        private static void TypingDebounce(bool card)
        {
            using (var f = new Fixture(card ? "card-typing" : "bench-typing"))
            {
                f.Mark(); DateTime before = File.GetLastWriteTimeUtc(f.Store.PathName); int history = f.HistoryCount;
                (card ? f.Card.IdeasEditor : f.Bench.IdeasEditor).Text = "真实输入的备忘";
                Check(f.Saved == 0 && File.GetLastWriteTimeUtc(f.Store.PathName) == before && f.HistoryCount == history, (card ? "card" : "workbench") + " actual TextChanged queues content before any physical write");
                Check(Field<bool>(card ? (object)f.Card : f.Bench, "dirty") && Field<Forms.Timer>(card ? (object)f.Card : f.Bench, "saveTimer").Enabled, (card ? "card" : "workbench") + " real text mutation keeps content dirty and the existing debounce timer");
                Check((card ? f.CardSaving : f.BenchSaving) == 0, (card ? "card" : "workbench") + " debounce waiting does not mislabel queued text as actively saving");
                bool ok = card ? f.Card.Flush() : f.Bench.Flush();
                Check(ok && new WorkboardStore(Path.GetDirectoryName(f.Store.PathName)).State.Ideas == "真实输入的备忘" && f.HistoryCount == history + 1, (card ? "card" : "workbench") + " real content remains persisted with one previous-content snapshot");
            }
        }
        private static void SameTaskDate()
        {
            using (var f = new Fixture("same-task-date"))
            {
                f.Mark(); DateTime before = File.GetLastWriteTimeUtc(f.Store.PathName); int history = f.HistoryCount;
                typeof(WorkbenchWindow).GetMethod("ChangeTaskDate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(f.Bench, new object[] { f.Store.State.Tasks[0], f.Store.Today });
                Check(f.BenchSaving == 0, "selecting the already-stored task date does not enter saving feedback");
                Check(File.GetLastWriteTimeUtc(f.Store.PathName) == before && f.HistoryCount == history, "same-date task action does not physically rewrite file or history");
                Check(f.Saved == 0 && !Field<bool>(f.Bench, "dirty") && !Field<Forms.Timer>(f.Bench, "saveTimer").Enabled, "same-date handler is a true no-op without Store.Save notification or content scheduling");
                Report.AppendLine("MEASURE same-date: benchSaving=" + f.BenchSaving + "; SavedEvents=" + f.Saved + "; physicalWrite=" + (File.GetLastWriteTimeUtc(f.Store.PathName) != before));
            }
        }
        private static void InvalidTitle(bool card)
        {
            using (var f = new Fixture(card ? "card-invalid-title" : "bench-invalid-title"))
            {
                TextBox editor = (card ? f.Card.TaskEditors : f.Bench.TaskEditors)[0]; string title = ((WorkTask)editor.Tag).Text;
                editor.Text = "";
                editor.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, Environment.TickCount, editor, null) { RoutedEvent = Keyboard.LostKeyboardFocusEvent });
                object window = card ? (object)f.Card : f.Bench;
                Check(((WorkTask)editor.Tag).Text == title && editor.Text == title, (card ? "card" : "workbench") + " clearing then leaving a title retains the last valid title");
                Check(!Field<bool>(window, "dirty") && !Field<Forms.Timer>(window, "saveTimer").Enabled && f.Saved == 0, (card ? "card" : "workbench") + " rejected empty title without a content mutation does not become content dirty");
            }
        }
        private static void SameCompletion()
        {
            using (var f = new Fixture("same-completion"))
            {
                f.Mark(); DateTime before = File.GetLastWriteTimeUtc(f.Store.PathName); int history = f.HistoryCount;
                var task = f.Store.State.Tasks[0];
                f.Card.SetTaskCompleted(task, task.Done);
                Check(!Field<bool>(f.Card, "dirty") && !Field<Forms.Timer>(f.Card, "saveTimer").Enabled && f.CardSaving == 0, "same-completion card handler does not schedule a content save");
                f.Card.Flush();
                Check(f.Saved == 0 && File.GetLastWriteTimeUtc(f.Store.PathName) == before && f.HistoryCount == history, "same-completion no-op does not invoke store, rewrite bytes or add history");
                string status = f.BenchStatus;
                f.Bench.TaskChecks[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(!Field<bool>(f.Bench, "dirty") && !Field<Forms.Timer>(f.Bench, "saveTimer").Enabled && f.BenchStatus == status, "same-completion workbench WPF Click handler leaves content scheduling and status unchanged");
                Check(f.Saved == 0 && File.GetLastWriteTimeUtc(f.Store.PathName) == before && f.HistoryCount == history, "same-completion workbench WPF Click is a true no-op without Store.Save notification, physical write or history");
            }
        }
        private static void WhitespaceOnlyTitle()
        {
            using (var f = new Fixture("bench-whitespace-only-title"))
            {
                TextBox title = f.Bench.TaskEditors[0]; string original = ((WorkTask)title.Tag).Text;
                title.Text += " ";
                Check(((WorkTask)title.Tag).Text == original, "workbench title normalization retains the same semantic content for trailing whitespace");
                Check(!Field<bool>(f.Bench, "dirty") && !Field<Forms.Timer>(f.Bench, "saveTimer").Enabled && f.BenchSaving == 0, "semantically unchanged normalized title does not queue content saving");
            }
        }
        private static void StartupViewSetting()
        {
            using (var f = new Fixture("startup-view-setting"))
            {
                int history = f.HistoryCount; string status = f.BenchStatus; bool contentDirtyObserved = false;
                f.Store.Changed += delegate { contentDirtyObserved |= Field<bool>(f.Bench, "dirty"); };
                bool requested = !f.Store.State.ShowCardOnStartup;
                f.Bench.StartupCardCheck.IsChecked = requested;
                f.Bench.StartupCardCheck.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(!contentDirtyObserved && f.BenchStatus == status, "startup visibility checkbox is a quiet view setting throughout its actual WPF Click handler");
                Check(new WorkboardStore(Path.GetDirectoryName(f.Store.PathName)).State.ShowCardOnStartup == requested && f.HistoryCount == history, "startup visibility preference persists without creating content history");
                int saves = f.Saved;
                f.Bench.StartupCardCheck.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(f.Saved == saves && !contentDirtyObserved && f.BenchStatus == status, "repeated same-state startup setting route is a true no-op");
            }
        }
        private static void ContentDeadlineWithGeometry(bool card)
        {
            using (var f = new Fixture(card ? "card-deadline-geometry" : "bench-deadline-geometry"))
            {
                SetField(f.Card, "editing", true); f.Card.Place(new System.Drawing.Rectangle(100, 100, 450, 340), false); f.Store.Save(); f.Saved = 0;
                int history = f.HistoryCount;
                (card ? f.Card.IdeasEditor : f.Bench.IdeasEditor).Text = "原600毫秒期限内落盘";
                Pump(330);
                f.Card.BeginCornerResize(ResizeCorner.BottomRight, new System.Drawing.Point(550, 440));
                f.Card.ResizeCornerTo(new System.Drawing.Point(570, 460)); f.Card.EndCornerResize();
                Pump(400);
                var saved = new WorkboardStore(Path.GetDirectoryName(f.Store.PathName));
                Check(saved.State.Ideas == "原600毫秒期限内落盘" && saved.State.Width == 470 && saved.State.Height == 360, (card ? "card" : "workbench") + " pending content reaches disk within its original 600ms debounce despite later card geometry");
                Check(f.Saved == 1 && f.HistoryCount == history + 1 && !Field<bool>(f.Card, "dirty") && !Field<bool>(f.Bench, "dirty") && !Field<Forms.Timer>(f.Card, "saveTimer").Enabled && !Field<Forms.Timer>(f.Bench, "saveTimer").Enabled,
                    (card ? "card" : "workbench") + " first actual save clears both peer timers and creates exactly one content revision");
                Pump(650);
                Check(f.Saved == 1, (card ? "card" : "workbench") + " peer view timer cannot repeat a just-committed shared save");
            }
        }
        private static void QuietGeometryTimer()
        {
            using (var f = new Fixture("quiet-geometry-timer"))
            {
                SetField(f.Card, "editing", true); f.Card.Place(new System.Drawing.Rectangle(100, 100, 450, 340), false); f.Store.Save(); f.Saved = 0;
                int history = f.HistoryCount; string status = f.CardStatus;
                f.Card.BeginCornerResize(ResizeCorner.Right, new System.Drawing.Point(550, 300));
                f.Card.ResizeCornerTo(new System.Drawing.Point(575, 300)); f.Card.EndCornerResize();
                Check(!Field<bool>(f.Card, "dirty") && Field<bool>(f.Card, "viewDirty") && Field<Forms.Timer>(f.Card, "saveTimer").Enabled && f.Saved == 0 && f.CardStatus == status,
                    "ended geometry gesture arms one quiet preference timer without content dirty or content status");
                Pump(750);
                var saved = new WorkboardStore(Path.GetDirectoryName(f.Store.PathName));
                Check(saved.State.Width == 475 && saved.State.Height == 340 && f.Saved == 1 && f.HistoryCount == history && f.CardStatus == status && !Field<bool>(f.Card, "viewDirty"),
                    "quiet geometry timer persists final bounds once with unchanged content history and feedback");
            }
        }
        private static void PeerSuccessfulRetry(bool card)
        {
            using (var f = new Fixture(card ? "card-peer-retry" : "bench-peer-retry"))
            {
                (card ? f.Card.IdeasEditor : f.Bench.IdeasEditor).Text = "失败后通过共享窗口保存";
                Directory.CreateDirectory(f.Store.PathName + ".tmp");
                Check(!(card ? f.Card.Flush() : f.Bench.Flush()) && Field<bool>(card ? (object)f.Card : f.Bench, "saveFailed"), (card ? "card" : "workbench") + " real failed save retains an explicit failure flag");
                Directory.Delete(f.Store.PathName + ".tmp");
                if (card) f.Bench.SelectScope("tomorrow"); else f.Card.SelectScope("tomorrow");
                Check(card ? f.Bench.Flush() : f.Card.Flush(), (card ? "workbench" : "card") + " view flush can persist pending shared content after write permission recovers");
                Check(new WorkboardStore(Path.GetDirectoryName(f.Store.PathName)).State.Ideas == "失败后通过共享窗口保存" && !Field<bool>(f.Card, "dirty") && !Field<bool>(f.Bench, "dirty") && !Field<bool>(card ? (object)f.Card : f.Bench, "saveFailed") && !(card ? f.CardStatus : f.BenchStatus).Contains("保存失败"),
                    (card ? "card" : "workbench") + " actual peer save clears dirty, failure feedback and failure flag only after content reaches disk");
                int saves = f.Saved; Pump(750);
                Check(f.Saved == saves, "successful peer retry cancels obsolete content timers");
            }
        }
        private static void ErrorFeedback(bool card)
        {
            using (var f = new Fixture(card ? "card-error-feedback" : "bench-error-feedback"))
            {
                TextBox editor = card ? f.Card.IdeasEditor : f.Bench.IdeasEditor; editor.Text = "写入失败仍保留";
                Directory.CreateDirectory(f.Store.PathName + ".tmp");
                bool ok = card ? f.Card.Flush() : f.Bench.Flush();
                Check(!ok && (card ? f.CardStatus : f.BenchStatus).Contains("保存失败"), (card ? "card" : "workbench") + " fixture induces a real atomic-save failure");
                editor.Text += "新内容";
                Check((card ? f.CardStatus : f.BenchStatus).Contains("保存失败"), (card ? "card" : "workbench") + " further typing cannot erase unresolved save-failure feedback before a successful retry");
                f.Card.SelectScope("tomorrow");
                Check(f.Store.State.Ideas == "写入失败仍保留新内容" && (card ? f.CardStatus : f.BenchStatus).Contains("保存失败"), (card ? "card" : "workbench") + " peer view refresh preserves newer content and unresolved error");
                Directory.Delete(f.Store.PathName + ".tmp");
                Check((card ? f.Card.Flush() : f.Bench.Flush()) && new WorkboardStore(Path.GetDirectoryName(f.Store.PathName)).State.Ideas == "写入失败仍保留新内容", (card ? "card" : "workbench") + " explicit retry saves latest content after obstacle removed");
            }
        }
        private static void FailureValidationFeedback()
        {
            using (var f = new Fixture("bench-failure-validation-feedback"))
            {
                f.Bench.IdeasEditor.Text = "失败期间的未保存内容";
                Directory.CreateDirectory(f.Store.PathName + ".tmp");
                Check(!f.Bench.Flush() && f.BenchStatus.Contains("保存失败") && Field<bool>(f.Bench, "saveFailed") && f.Saved == 0,
                    "workbench validation fixture begins with a real unresolved save failure");
                TextBox title = f.Bench.TaskEditors[0]; string originalTitle = ((WorkTask)title.Tag).Text;
                title.Text = "";
                title.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, Environment.TickCount, title, null) { RoutedEvent = Keyboard.LostKeyboardFocusEvent });
                Check(title.Text == originalTitle && f.BenchStatus.Contains("保存失败") && Field<bool>(f.Bench, "saveFailed") && Field<bool>(f.Bench, "dirty") && f.Saved == 0,
                    "empty title validation cannot hide an unresolved save failure or claim persistence");
                f.Bench.IdeasEditor.Text = "";
                Check(!f.Bench.ConvertMemo(false) && f.BenchStatus.Contains("保存失败") && Field<bool>(f.Bench, "saveFailed") && Field<bool>(f.Bench, "dirty") && f.Saved == 0,
                    "empty memo conversion feedback preserves the unresolved save failure and retryable content state");
                Directory.Delete(f.Store.PathName + ".tmp");
                f.Bench.IdeasEditor.Text = "验证后重试保存的最新内容";
                Check(f.Bench.Flush() && f.Saved == 1 && !Field<bool>(f.Bench, "saveFailed") && !Field<bool>(f.Bench, "dirty") && !f.BenchStatus.Contains("保存失败") && new WorkboardStore(Path.GetDirectoryName(f.Store.PathName)).State.Ideas == "验证后重试保存的最新内容",
                    "a successful real retry clears the validation-preserved failure only after the latest content reaches disk");
            }
        }
        [STAThread]
        private static int Main()
        {
            try
            {
                Forms.Application.EnableVisualStyles(); Forms.Application.SetCompatibleTextRenderingDefault(false);
                Run("actual WPF scope routes", WpfScopeClicks); Run("focus without edits", WpfFocusNoEdit);
                Run("no-movement corner click", CornerClickNoMovement); Run("changed geometry", ChangedGeometry);
                Run("card debounce feedback", delegate { TypingDebounce(true); }); Run("workbench debounce feedback", delegate { TypingDebounce(false); });
                Run("same-date task action", SameTaskDate);
                Run("card invalid empty title", delegate { InvalidTitle(true); }); Run("workbench invalid empty title", delegate { InvalidTitle(false); });
                Run("same completion", SameCompletion); Run("normalized title no-op", WhitespaceOnlyTitle);
                Run("card failure feedback", delegate { ErrorFeedback(true); }); Run("workbench failure feedback", delegate { ErrorFeedback(false); });
                Run("workbench validation after save failure", FailureValidationFeedback);
                Run("startup visibility view setting", StartupViewSetting);
                Run("card pending content and later geometry", delegate { ContentDeadlineWithGeometry(true); });
                Run("workbench pending content and later geometry", delegate { ContentDeadlineWithGeometry(false); });
                Run("quiet geometry timer", QuietGeometryTimer);
                Run("card peer successful retry", delegate { PeerSuccessfulRetry(true); }); Run("workbench peer successful retry", delegate { PeerSuccessfulRetry(false); });
                Report.AppendLine("Passed: " + passed + "; Failed: " + failed);
                Report.AppendLine("LIMIT: hidden WinForms/WPF controls, routed Click and focus events, actual TextChanged and exact gesture/date handler paths; no Show/Attach, native focus activation, physical input, production JSON or registry. Focus events are routed without native focus ownership.");
                return failed == 0 ? 0 : 1;
            }
            finally { File.WriteAllText(Path.Combine(Storage.Folder, "verification-save-feedback.txt"), Report.ToString(), new UTF8Encoding(false)); }
        }
    }
}
