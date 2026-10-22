using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Windows.Controls;
using Forms = System.Windows.Forms;

namespace LocalDesktopNote
{
    internal static class WorkbenchEntryFailureVerification
    {
        private static readonly StringBuilder Report = new StringBuilder();
        private static int passed, failed;
        private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private static void Check(bool condition, string message)
        {
            Report.AppendLine((condition ? "PASS: " : "FAIL: ") + message);
            if (condition) passed++; else failed++;
        }
        private static void SetField(object target, string name, object value)
        {
            target.GetType().GetField(name, PrivateInstance).SetValue(target, value);
        }
        private static object GetField(object target, string name)
        {
            return target.GetType().GetField(name, PrivateInstance).GetValue(target);
        }
        private static void FailedEntry(MethodInfo entry, bool existingBench)
        {
            string name = existingBench ? "existing-hidden-workbench" : "no-workbench";
            var store = new WorkboardStore(Path.Combine(Storage.Folder, "fixture", name));
            store.AddTask("已保存的任务"); store.State.Ideas = "磁盘中的原备忘"; store.Save();
            byte[] primaryBefore = File.ReadAllBytes(store.PathName);
            string blocked = store.PathName + ".tmp";
            using (var card = new WorkboardWindow(store))
            using (var bench = existingBench ? new WorkbenchWindow(store) : null)
            {
                // Skip NoteContext's constructor: no tray icon, startup policy, Attach,
                // migration, monitor timer, or real user data are involved in this test.
                var context = (NoteContext)FormatterServices.GetUninitializedObject(typeof(NoteContext));
                SetField(context, "workboardStore", store); SetField(context, "workboard", card); SetField(context, "workbench", bench);
                string memo = "这段刚输入的备忘必须保留 · " + name;
                card.IdeasEditor.Text = memo;
                SetField(card, "editing", true);
                Directory.CreateDirectory(blocked);
                try
                {
                    Check(!card.IsHandleCreated && !card.Visible && (bench == null || (!bench.IsHandleCreated && !bench.Visible)), name + ": fixture starts without native windows");
                    Check((bool)GetField(card, "dirty"), name + ": content is genuinely dirty before entry");
                    bool opened = (bool)entry.Invoke(context, null);
                    Check(!opened, name + ": failed card persistence returns false");
                    Check(Object.ReferenceEquals(GetField(context, "workbench"), bench), name + ": failed entry neither creates nor replaces the workbench");
                    Check(!card.IsHandleCreated && !card.Visible && (bench == null || (!bench.IsHandleCreated && !bench.Visible)), name + ": failed entry creates no native handle and shows no window");
                    Check(card.IsEditing && (bool)GetField(card, "dirty"), name + ": failed entry preserves editing and retryable dirty state");
                    Check(store.State.Ideas == memo && card.IdeasEditor.Text == memo && (bench == null || bench.IdeasEditor.Text == memo), name + ": latest memo remains in shared state and existing editor views");
                    Check(File.ReadAllBytes(store.PathName).SequenceEqual(primaryBefore), name + ": failed persistence leaves the saved primary bytes unchanged");
                    Check(((TextBlock)card.View.FindName("CardSaveStatus")).Text.Contains("保存失败"), name + ": the original card still shows its save failure");
                    Check(Directory.Exists(blocked), name + ": the write blocker remains in place throughout the attempted entry");
                }
                finally
                {
                    // Keep persistence blocked while windows dispose: this QA checks
                    // only the failure branch and never invokes a successful entry.
                    card.Dispose(); if (bench != null) bench.Dispose();
                    if (Directory.Exists(blocked)) Directory.Delete(blocked);
                }
            }
        }
        [STAThread]
        private static int Main()
        {
            try
            {
                Forms.Application.EnableVisualStyles(); Forms.Application.SetCompatibleTextRenderingDefault(false);
                MethodInfo entry = typeof(NoteContext).GetMethod("TryShowWorkbench", PrivateInstance);
                Check(entry != null && entry.ReturnType == typeof(bool), "workbench entry exposes an explicit success result before any UI is constructed");
                if (entry == null || entry.ReturnType != typeof(bool)) return 1;
                FailedEntry(entry, false); FailedEntry(entry, true);
                return failed == 0 ? 0 : 1;
            }
            catch (Exception error) { Report.AppendLine(error.ToString()); failed++; return 1; }
            finally
            {
                Report.AppendLine("Passed: " + passed + "; Failed: " + failed);
                Report.AppendLine("LIMIT: only an isolated persistence-failure entry was invoked; no Show/Attach, successful-entry retry, native input, or production data access.");
                File.WriteAllText(Path.Combine(Storage.Folder, "verification-workbench-entry-failure.txt"), Report.ToString(), Encoding.UTF8);
            }
        }
    }
}
