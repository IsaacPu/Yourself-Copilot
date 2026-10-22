using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Windows.Controls;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace LocalDesktopNote
{
    internal static class LibrarySaveFailureVerification
    {
        private static readonly StringBuilder Report = new StringBuilder();
        private static int passed, failed;
        private static void Set(NoteContext context, string name, object value)
        {
            typeof(NoteContext).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).SetValue(context, value);
        }
        private static T Get<T>(NoteContext context, string name)
        {
            return (T)typeof(NoteContext).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(context);
        }
        private static IntPtr TrayHandle(Forms.NotifyIcon tray)
        {
            FieldInfo field = typeof(Forms.NotifyIcon).GetField("window", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null) throw new NotSupportedException("Cannot inspect the NotifyIcon native window on this runtime.");
            var window = field.GetValue(tray) as Forms.NativeWindow;
            return window == null ? IntPtr.Zero : window.Handle;
        }
        private static void Check(bool value, string description)
        {
            Report.AppendLine((value ? "PASS: " : "FAIL: ") + description);
            if (value) passed++; else failed++;
        }
        private static Exception TryExit(NoteContext context)
        {
            try { typeof(NoteContext).GetMethod("ExitThreadCore", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(context, null); return null; }
            catch (TargetInvocationException error) { return error.InnerException; }
        }
        [STAThread]
        private static int Main()
        {
            ManagerWindow manager = null; Forms.Timer timer = null; Forms.NotifyIcon tray = null; FileStream locked = null;
            try
            {
                var store = new LibraryStore(Path.Combine(Storage.Folder, "fixture"));
                store.State.SelectedId = "before-save"; store.Save();
                store.State.SelectedId = "unsaved-change";
                // Skip the constructor, which would create desktop windows and a real tray icon.
                var context = (NoteContext)FormatterServices.GetUninitializedObject(typeof(NoteContext));
                int exitEvents = 0; context.ThreadExit += delegate { exitEvents++; };
                Set(context, "Store", store); Set(context, "Rows", new ObservableCollection<PictureRow>());
                Set(context, "notes", new Dictionary<string, ImageNote>());
                timer = new Forms.Timer(); tray = new Forms.NotifyIcon(); Set(context, "monitor", timer); Set(context, "tray", tray);
                manager = new ManagerWindow(context); Set(context, "manager", manager);
                var interop = new WindowInteropHelper(manager);
                if (interop.Handle != IntPtr.Zero || manager.IsVisible || TrayHandle(tray) != IntPtr.Zero)
                    throw new InvalidOperationException("Fixture unexpectedly created a native window before the test.");

                locked = new FileStream(store.StatePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                Exception error = TryExit(context);
                bool exiting = Get<bool>(context, "exiting");
                var message = (TextBlock)typeof(ManagerWindow).GetField("message", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(manager);
                string errorMessage = message.Text;
                locked.Dispose(); locked = null;
                string persisted = new LibraryStore(store.Folder).State.SelectedId;
                bool canceled = error == null && !exiting && exitEvents == 0 && !manager.AllowClose
                    && interop.Handle == IntPtr.Zero && !manager.IsVisible && TrayHandle(tray) == IntPtr.Zero
                    && persisted == "before-save" && store.State.SelectedId == "unsaved-change"
                    && errorMessage.Contains("暂时无法保存");
                Check(canceled, "a real library save failure cancels exit, preserves pending changes and actionable error feedback, and creates no native windows");
                Report.AppendLine("MEASURE failed-save: exiting=" + exiting + "; ThreadExit=" + exitEvents + "; managerHWND=" + interop.Handle
                    + "; trayHWND=" + TrayHandle(tray) + "; persisted=" + persisted + "; status=" + errorMessage
                    + "; error=" + (error == null ? "none" : error.ToString()));
                if (canceled)
                {
                    Exception retryError = TryExit(context);
                    Check(retryError == null && Get<bool>(context, "exiting") && exitEvents == 1
                        && new LibraryStore(store.Folder).State.SelectedId == "unsaved-change"
                        && interop.Handle == IntPtr.Zero && !manager.IsVisible && TrayHandle(tray) == IntPtr.Zero,
                        "releasing the storage lock allows one retry to persist the original pending state and emit exactly one ThreadExit");
                }
                Report.AppendLine("Passed: " + passed + "; Failed: " + failed);
                Report.AppendLine("LIMIT: the real NoteContext constructor, Show, Attach, dialogs, registry and production JSON are never used. Four operation success-message call sites are covered by source review only.");
                return failed == 0 ? 0 : 1;
            }
            catch (Exception error) { Report.AppendLine(error.ToString()); return 2; }
            finally
            {
                if (locked != null) locked.Dispose();
                if (timer != null) timer.Dispose();
                if (tray != null) tray.Dispose();
                if (manager != null) { manager.AllowClose = true; manager.Close(); }
                File.WriteAllText(Path.Combine(Storage.Folder, "verification-library-save-failure.txt"), Report.ToString(), new UTF8Encoding(false));
            }
        }
    }
}
