using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Input;
using Forms = System.Windows.Forms;
namespace LocalDesktopNote
{
    internal static class EditTransitionVerification
    {
        private static readonly StringBuilder Report = new StringBuilder();
        [DllImport("user32.dll")] private static extern IntPtr GetProp(IntPtr handle, string name);
        [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, Desktop.EnumWindowsProc callback, IntPtr data);
        private static void Check(bool value, string name) { if (!value) throw new InvalidOperationException("FAIL: " + name); Report.AppendLine("PASS: " + name); }
        private static void CheckPresentation(WorkboardWindow card, string phase)
        {
            bool visible = Desktop.IsWindowVisible(card.Controls[0].Handle);
            bool redrawReleased = GetProp(card.Handle, "SysSetRedraw") == IntPtr.Zero;
            int descendants = 0;
            EnumChildWindows(card.Handle, delegate(IntPtr child, IntPtr ignored)
            {
                descendants++;
                visible &= Desktop.IsWindowVisible(child);
                redrawReleased &= GetProp(child, "SysSetRedraw") == IntPtr.Zero;
                return true;
            }, IntPtr.Zero);
            Check(descendants >= 2 && visible, "ElementHost and WPF native children remain visible " + phase);
            Check(redrawReleased, "redraw locks are released on the form and all native children " + phase);
        }
        private sealed class NativeProbe : Forms.NativeWindow
        {
            internal int VisibleMismatches, Paints;
            private readonly IntPtr shell;
            internal NativeProbe(IntPtr handle) { shell = Desktop.FindView(); AssignHandle(handle); }
            protected override void WndProc(ref Forms.Message message)
            {
                base.WndProc(ref message);
                if (message.Msg == 0xF || message.Msg == 0x85 || message.Msg == 0x14) Paints++;
                if ((message.Msg == 0x7D || message.Msg == 0x47 || message.Msg == 0x18 || message.Msg == 0xF || message.Msg == 0x14) && Desktop.IsWindowVisible(Handle))
                {
                    bool nativeChild = (Desktop.GetWindowLongPtr(Handle, Desktop.GWL_STYLE).ToInt64() & Desktop.WS_CHILD) != 0;
                    if (nativeChild != (Desktop.GetParent(Handle) == shell)) VisibleMismatches++;
                }
            }
        }
        [STAThread]
        private static int Main()
        {
            WorkboardWindow card = null; NativeProbe probe = null;
            try
            {
                try { Desktop.SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch (EntryPointNotFoundException) { }
                Forms.Application.EnableVisualStyles(); Forms.Application.SetCompatibleTextRenderingDefault(false);
                var store = new WorkboardStore(Path.Combine(Storage.Folder, "fixture"));
                store.AddTask("输入与桌面层回归");
                card = new WorkboardWindow(store); card.ReturnToDesktopOnDeactivate = false; card.Attach(); card.ShowCard(false); Forms.Application.DoEvents();
                CheckPresentation(card, "after first display");
                Rectangle rectangle = card.ScreenRectangle; IntPtr handle = card.Handle;
                probe = new NativeProbe(handle);
                for (int i = 0; i < 12; i++)
                {
                    card.BeginEditing(card.IdeasEditor); Forms.Application.DoEvents();
                    Check(card.IsEditing && !card.Attached && card.IdeasEditor.IsKeyboardFocusWithin, "inline editor gains real keyboard focus " + i);
                    CheckPresentation(card, "during editing " + i);
                    card.IdeasEditor.Text = "中文备忘 " + i;
                    card.EndEditing(); Forms.Application.DoEvents();
                    Check(card.Attached && !card.IsEditing && card.Visible, "leaving input restores a visible desktop card " + i);
                    CheckPresentation(card, "after returning to the desktop " + i);
                    Check(card.Handle == handle && card.ScreenRectangle == rectangle, "transition retains the same HWND and screen rectangle " + i);
                }
                Report.AppendLine("Observed visible parent/style mismatches: " + probe.VisibleMismatches);
                Report.AppendLine("Observed native paint messages: " + probe.Paints);
                Check(probe.VisibleMismatches == 0, "native reparenting never exposes a visible inconsistent parent/style state");
                Check(card.View.FindName("EditCardButton") == null, "manual edit and finish controls are removed from the card");
                Check(new WorkboardStore(Path.Combine(Storage.Folder, "fixture")).State.Ideas == "中文备忘 11", "Chinese memo is saved after inline editing");
                return 0;
            }
            catch (Exception error) { Report.AppendLine(error.ToString()); return 1; }
            finally { if (probe != null) probe.ReleaseHandle(); if (card != null) card.Dispose(); File.WriteAllText(Path.Combine(Storage.Folder, "verification-inline-transition.txt"), Report.ToString(), Encoding.UTF8); }
        }
    }
}
