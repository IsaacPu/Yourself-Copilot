using System;
using System.Windows.Forms;

namespace LocalDesktopNote
{
    internal static class WorkboardPreview
    {
        [STAThread]
        private static void Main()
        {
            try { Desktop.SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch (EntryPointNotFoundException) { }
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            using (var card = new WorkboardWindow(new WorkboardStore(Storage.Folder)))
            using (var deadline = new Timer { Interval = 600000 })
            {
                // A taskbar entry makes the isolated test instance discoverable
                // to accessibility tools; the production desktop card stays quiet.
                card.ShowInTaskbar = true;
                card.ReturnToDesktopOnDeactivate = false;
                card.Text = "Yourself Copilot · 输入验证（独立数据）";
                deadline.Tick += delegate { card.Flush(); Application.ExitThread(); };
                deadline.Start(); card.Attach(); card.ShowCard(true); Application.Run();
            }
        }
    }
}
