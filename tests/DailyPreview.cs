using System;
using System.IO;
using Forms = System.Windows.Forms;
namespace LocalDesktopNote
{
    internal static class DailyPreview
    {
        [STAThread]
        private static void Main()
        {
            try { Desktop.SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch (EntryPointNotFoundException) { }
            Forms.Application.EnableVisualStyles(); Forms.Application.SetCompatibleTextRenderingDefault(false);
            var store = new WorkboardStore(Storage.Folder);
            if (!File.Exists(store.PathName))
            {
                store.AddTask("整理参考资料", store.Today.AddDays(-1));
                store.AddTask("整理今天的工作记录", store.Today);
                store.AddTask("准备明天的任务清单", store.Today.AddDays(1));
                store.AddTask("以后再研究的想法", null);
                store.State.Ideas = "临时想到的事情先记在这里。\r\n需要落实时，再转成任务。"; store.Save();
            }
            using (var card = new WorkboardWindow(store))
            using (var workspace = new WorkbenchWindow(store))
            using (var deadline = new Forms.Timer { Interval = 900000 })
            {
                card.ShowInTaskbar = true;
                card.Text = "Yourself Copilot · 桌面卡片（独立测试）";
                workspace.Text = "Yourself Copilot · 工作台（独立测试）";
                card.OpenWorkbench = delegate { workspace.RefreshContent(); workspace.Show(); workspace.Activate(); };
                workspace.ShowDesktopCard = delegate { card.ShowCard(false); };
                workspace.GetAutoStart = delegate { return false; };
                workspace.SetAutoStart = delegate { throw new InvalidOperationException("独立测试不会修改 Windows 启动设置。"); };
                deadline.Tick += delegate { workspace.Flush(); card.Flush(); workspace.AllowClose = true; workspace.Close(); Forms.Application.ExitThread(); };
                deadline.Start(); card.Attach(); card.ShowCard(false); Forms.Application.Run(workspace);
            }
        }
    }
}
