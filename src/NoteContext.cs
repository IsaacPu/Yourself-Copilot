using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace LocalDesktopNote
{
    internal sealed class NoteContext : ApplicationContext
    {
        internal readonly LibraryStore Store;
        internal readonly ObservableCollection<PictureRow> Rows = new ObservableCollection<PictureRow>();
        private readonly Dictionary<string, ImageNote> notes = new Dictionary<string, ImageNote>();
        private readonly NotifyIcon tray;
        private readonly System.Windows.Forms.Timer monitor;
        private readonly EventWaitHandle request;
        private ManagerWindow manager;
        private WorkboardStore workboardStore;
        private WorkboardWindow workboard;
        private WorkbenchWindow workbench;
        private readonly bool backgroundStartup;
        private bool startupPolicyApplied;
        private bool exiting, hideExplained;
        internal Preferences Selected { get { return Store.State.Items.FirstOrDefault(p => p.Id == Store.State.SelectedId); } }
        internal bool CanUndo { get { return Store.State.Archived.Count != 0; } }
        internal NoteContext(EventWaitHandle showRequest) : this(showRequest, false) { }
        internal NoteContext(EventWaitHandle showRequest, bool startup)
        {
            backgroundStartup = startup;
            request = showRequest;
            Store = new LibraryStore(Storage.Folder);
            Store.Migrate(Storage.SettingsFile, Path.Combine(Storage.Folder, "计划表.png"));
            var trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("打开工作台", null, delegate { ShowWorkbench(); });
            trayMenu.Items.Add("快速添加任务", null, delegate { if (TryShowWorkbench()) workbench.FocusQuickTask(); });
            trayMenu.Items.Add("快速记备忘", null, delegate { if (TryShowWorkbench()) workbench.FocusQuickMemo(); });
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("显示桌面任务卡", null, delegate { ShowWorkboard(false); });
            trayMenu.Items.Add("管理桌面图片", null, delegate { ShowManager(); });
            trayMenu.Items.Add("隐藏桌面内容", null, delegate { SetAllVisible(false); });
            trayMenu.Items.Add("显示桌面内容", null, delegate { SetAllVisible(true); });
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("退出 Yourself Copilot", null, delegate { ExitThread(); });
            tray = new NotifyIcon { Icon = AppVisuals.ApplicationIcon, Text = "Yourself Copilot · 想法、任务与桌面图片", ContextMenuStrip = trayMenu, Visible = true };
            tray.DoubleClick += delegate { ShowWorkbench(); };
            foreach (var item in Store.State.Items) TryCreate(item);
            TryCreateWorkboard();
            if (Selected == null && Store.State.Items.Count > 0) Store.State.SelectedId = Store.State.Items[0].Id;
            Refresh(); Save();
            monitor = new System.Windows.Forms.Timer { Interval = 1000 };
            monitor.Tick += delegate
            {
                if (request.WaitOne(0)) ShowWorkbench();
                if (workboard != null && !workboard.IsDisposed) workboard.RefreshDay();
                if (workbench != null && !workbench.IsDisposed) workbench.RefreshDay();
                CheckpointMemoHistory();
                if (Desktop.FindView() != IntPtr.Zero)
                {
                    if (workboard == null || workboard.IsDisposed) TryCreateWorkboard();
                    else if (!workboard.ConnectionAlive)
                    {
                        try { workboard.Attach(); if (!workboardStore.State.Hidden) workboard.ShowCard(false); }
                        catch (Exception e) { Storage.Log("工作卡重新连接失败：" + e.Message); }
                    }
                }
                foreach (var item in Store.State.Items.ToArray())
                {
                    ImageNote note;
                    if (notes.TryGetValue(item.Id, out note) && (note.IsDisposed || !Desktop.IsWindow(note.DesktopView)))
                    {
                        if (Desktop.FindView() == IntPtr.Zero) continue;
                        notes.Remove(item.Id); if (!note.IsDisposed) note.Dispose(); TryCreate(item);
                    }
                }
            };
            monitor.Start();
            if (!String.IsNullOrEmpty(Store.Notice)) Message(Store.Notice, true);
        }
        private void TryCreateWorkboard()
        {
            try
            {
                if (workboardStore == null) workboardStore = new WorkboardStore(Storage.Folder);
                // Apply once before attaching: Explorer may not be ready at login.
                // Later retries must retain this choice, and must not undo a user's hide.
                if (backgroundStartup && !startupPolicyApplied)
                {
                    workboardStore.State.TaskScope = "today";
                    workboardStore.State.Hidden = !workboardStore.State.ShowCardOnStartup;
                    startupPolicyApplied = true;
                }
                workboard = new WorkboardWindow(workboardStore);
                workboard.OpenManager = ShowManager;
                workboard.OpenWorkbench = ShowWorkbench;
                workboard.OpenMemo = delegate { if (TryShowWorkbench()) workbench.FocusQuickMemo(); };
                workboard.Attach();
                workboard.QueueViewSave(true);
                if (!workboardStore.State.Hidden && (!backgroundStartup || workboardStore.State.ShowCardOnStartup)) workboard.ShowCard(false);
                if (!String.IsNullOrEmpty(workboardStore.Notice))
                    tray.ShowBalloonTip(5000, "工作卡恢复提示", workboardStore.Notice, ToolTipIcon.Info);
            }
            catch (Exception e)
            {
                if (workboard != null) { workboard.Dispose(); workboard = null; }
                Storage.Log("工作卡加载失败：" + e);
            }
        }
        internal void ShowWorkboard(bool edit)
        {
            if (workboard == null || workboard.IsDisposed) TryCreateWorkboard();
            if (workboard != null) workboard.ShowCard(edit);
            else Message("暂时无法打开工作卡，内容文件已保留。请检查工作卡界面文件和运行日志。", true);
        }
        internal void ShowWorkbench()
        {
            TryShowWorkbench();
        }
        private bool TryShowWorkbench()
        {
            if (workboardStore == null) TryCreateWorkboard();
            if (workboardStore == null) return false;
            if (workboard != null) { workboard.EndEditing(); if (!workboard.Flush()) return false; }
            if (workbench == null || workbench.IsDisposed)
            {
                workbench = new WorkbenchWindow(workboardStore);
                workbench.OpenPictures = ShowManager;
                workbench.ShowDesktopCard = delegate { ShowWorkboard(false); };
                workbench.GetAutoStart = StartupManager.IsEnabled;
                workbench.SetAutoStart = StartupManager.SetEnabled;
            }
            workbench.RefreshContent(); workbench.Show();
            if (workbench.WindowState == FormWindowState.Minimized) workbench.WindowState = FormWindowState.Normal;
            workbench.Activate();
            return true;
        }
        private void TryCreate(Preferences item)
        {
            ImageNote note = null;
            try
            {
                bool positionNew = !item.Positioned;
                note = new ImageNote(item);
                note.SaveRequested = Save;
                note.Changed += delegate { Refresh(); if (manager != null) manager.SetSaveStatus("正在保存…"); };
                note.FilesDropped += AddImages;
                note.ContextMenuStrip = CreateMenu(item);
                note.Attach();
                if (positionNew && notes.Count > 0)
                {
                    Rectangle bounds = note.ScreenRectangle; int offset = (notes.Count % 5) * 28;
                    note.Place(new Rectangle(bounds.X - offset, bounds.Y + offset, bounds.Width, bounds.Height), false);
                }
                if (!item.Hidden) note.ShowNote();
                notes[item.Id] = note;
                Storage.Log("图片已连接桌面：" + item.Title + " " + note.ScreenRectangle);
            }
            catch (Exception e) { if (note != null) note.Dispose(); Storage.Log("图片加载失败：" + e.Message); }
        }
        private ContextMenuStrip CreateMenu(Preferences item)
        {
            var menu = new ContextMenuStrip();
            var locked = new ToolStripMenuItem("锁定位置和大小", null, delegate { Select(item.Id); SetLocked(!item.Locked); });
            menu.Items.Add("管理这张图片", null, delegate { Select(item.Id); ShowManager(); });
            menu.Items.Add("更换这张图片…", null, delegate { Select(item.Id); ChooseReplacement(); });
            menu.Items.Add(locked);
            menu.Items.Add("放大一点", null, delegate { Select(item.Id); WithNote(n => n.Zoom(1.1)); });
            menu.Items.Add("缩小一点", null, delegate { Select(item.Id); WithNote(n => n.Zoom(1.0 / 1.1)); });
            menu.Items.Add("恢复原图大小", null, delegate { Select(item.Id); OriginalSize(); });
            menu.Items.Add("重新定位", null, delegate { Select(item.Id); Reposition(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("隐藏这张图片", null, delegate { Select(item.Id); SetVisible(false); });
            menu.Items.Add("添加其他图片…", null, delegate { ChooseImages(); });
            menu.Opening += delegate { locked.Checked = item.Locked; };
            return menu;
        }
        internal void ShowManager()
        {
            if (manager == null) manager = new ManagerWindow(this);
            manager.Refresh(); manager.Show();
            if (manager.WindowState == System.Windows.WindowState.Minimized) manager.WindowState = System.Windows.WindowState.Normal;
            manager.Activate();
            var interop = new System.Windows.Interop.WindowInteropHelper(manager);
            Desktop.ShowWindow(interop.Handle, 1);
        }
        internal void ManagerHidden()
        {
            if (hideExplained) return; hideExplained = true;
            tray.ShowBalloonTip(2500, "Yourself Copilot 继续运行", "想法、任务和图片留在桌面。托盘右键可管理图片或退出。", ToolTipIcon.Info);
        }
        private void Message(string text, bool error)
        {
            if (backgroundStartup && manager == null && workbench == null)
            {
                Storage.Log(text); tray.ShowBalloonTip(5000, "Yourself Copilot", text, error ? ToolTipIcon.Warning : ToolTipIcon.Info); return;
            }
            ShowManager(); manager.ShowMessage(text, error);
        }
        internal void Save()
        {
            TrySave();
        }
        private bool TrySave()
        {
            try { Store.Save(); if (manager != null) manager.SetSaveStatus("已自动保存"); return true; }
            catch (Exception e) { Storage.Log("保存失败：" + e.Message); if (manager != null) manager.ShowMessage("暂时无法保存，请检查工具文件夹是否可写。当前图片仍会保留在桌面上。", true); return false; }
        }
        private void Refresh()
        {
            foreach (var row in Rows.ToArray()) if (!Store.State.Items.Contains(row.Source)) Rows.Remove(row);
            foreach (var item in Store.State.Items)
            {
                var row = Rows.FirstOrDefault(r => r.Source.Id == item.Id);
                if (row == null) Rows.Add(new PictureRow(item, Store)); else row.Refresh();
            }
            if (manager != null) manager.Refresh();
        }
        internal void Select(string id) { Store.State.SelectedId = id; Refresh(); Save(); }
        internal void Rename(string title)
        {
            if (Selected == null || Selected.Title == title) return;
            Selected.Title = title; Refresh(); Save();
        }
        internal void ChooseImages()
        {
            using (var dialog = new OpenFileDialog { Title = "添加图片，可一次选择多张", Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif", Multiselect = true, CheckFileExists = true })
                if (dialog.ShowDialog() == DialogResult.OK) AddImages(dialog.FileNames);
        }
        internal void AddImages(string[] paths)
        {
            int added = 0, duplicates = 0; var failures = new List<string>();
            foreach (string path in paths)
            {
                try
                {
                    int count = Store.State.Items.Count;
                    Preferences item = Store.Import(path);
                    if (count != Store.State.Items.Count) { TryCreate(item); added++; }
                    else { duplicates++; item.Hidden = false; ImageNote note; if (notes.TryGetValue(item.Id, out note)) note.ShowNote(); }
                }
                catch (Exception e) { failures.Add(Path.GetFileName(path) + "：" + e.Message); }
            }
            Refresh(); if (!TrySave()) return;
            string text = added > 0 ? "已添加 " + added + " 张图片，本地副本已保存。" : duplicates > 0 ? "图片已经在列表中，已为你选中。" : "没有添加图片。";
            if (failures.Count > 0) text += " " + String.Join("；", failures.Take(2).ToArray());
            Message(text, failures.Count > 0);
        }
        internal void PasteImage()
        {
            string temporary = null;
            try
            {
                if (Clipboard.ContainsFileDropList()) { AddImages(Clipboard.GetFileDropList().Cast<string>().ToArray()); return; }
                if (!Clipboard.ContainsImage()) { Message("剪贴板中没有图片。可以先复制一张图片，或直接拖入图片文件。", false); return; }
                temporary = Path.Combine(Store.Folder, "images", "粘贴图片-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".png");
                using (Image image = Clipboard.GetImage()) image.Save(temporary, System.Drawing.Imaging.ImageFormat.Png);
                AddImages(new[] { temporary });
            }
            catch (Exception e) { Message("暂时无法粘贴图片：" + e.Message, true); }
            finally { if (temporary != null && File.Exists(temporary)) try { File.Delete(temporary); } catch { } }
        }
        private void WithNote(Action<ImageNote> action)
        {
            if (Selected == null) return;
            ImageNote note;
            if (!notes.TryGetValue(Selected.Id, out note)) { Message("图片暂不可用，请更换图片后重试。", true); return; }
            action(note); Refresh();
        }
        internal void SetVisible(bool visible)
        {
            if (Selected == null) return;
            Selected.Hidden = !visible;
            WithNote(delegate(ImageNote note) { if (visible) note.ShowNote(); else note.Hide(); }); Save();
        }
        internal void SetLocked(bool locked)
        {
            if (Selected == null) return;
            Selected.Locked = locked; WithNote(n => n.Locked = locked); Refresh(); Save();
        }
        internal void SetScale(double scale) { WithNote(n => n.SetScale(scale)); }
        internal void OriginalSize() { if (Selected != null && !Selected.Locked) WithNote(n => n.OriginalScale()); }
        internal void Reposition() { if (Selected != null && !Selected.Locked) WithNote(n => n.Reposition()); }
        internal void SetAllVisible(bool visible)
        {
            if (workboard != null) { if (visible) workboard.ShowCard(false); else workboard.HideCard(); }
            foreach (var item in Store.State.Items)
            {
                item.Hidden = !visible; ImageNote note;
                if (notes.TryGetValue(item.Id, out note)) { if (visible) note.ShowNote(); else note.Hide(); }
            }
            Refresh(); Save();
        }
        internal void ChooseReplacement()
        {
            if (Selected == null) return;
            using (var dialog = new OpenFileDialog { Title = "更换选中的图片", Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif", CheckFileExists = true })
            {
                if (dialog.ShowDialog() != DialogResult.OK) return;
                try
                {
                    Preferences item = Selected; Store.Replace(item, dialog.FileName);
                    ImageNote note;
                    if (notes.TryGetValue(item.Id, out note))
                    {
                        Rectangle bounds = note.ScreenRectangle; note.LoadPicture(item.ImagePath);
                        note.Place(new Rectangle(bounds.X, bounds.Y, bounds.Width, note.HeightFor(bounds.Width)), false);
                    }
                    else TryCreate(item);
                    Refresh(); if (!TrySave()) return; Message("图片已更换，显示已自动适配屏幕。", false);
                }
                catch (Exception e) { Message("无法更换图片：" + e.Message, true); }
            }
        }
        internal void RemoveSelected()
        {
            Preferences item = Selected; if (item == null) return;
            ImageNote note; if (notes.TryGetValue(item.Id, out note)) { note.Dispose(); notes.Remove(item.Id); }
            Store.Archive(item); Refresh(); if (!TrySave()) return; Message("已从列表移除。图片文件仍保留，可点击左下角“撤销移除”。", false);
        }
        internal void UndoRemove()
        {
            Preferences item = Store.Undo(); if (item == null) return;
            TryCreate(item); Refresh(); if (!TrySave()) return; Message("已恢复图片及原来的位置。", false);
        }
        internal void VerifyUi()
        {
            if (manager == null) throw new InvalidOperationException("图片管理器未打开。");
            manager.VerifyLayout();
            if (Store.State.Items.Count != notes.Count) throw new InvalidOperationException("有图片未能连接桌面。");
            foreach (var entry in notes) if (!entry.Value.Attached) throw new InvalidOperationException("图片未连接到桌面层。");
            if (workboard == null || !workboard.Attached) throw new InvalidOperationException("工作卡未连接到桌面层。");
        }
        internal void CheckpointMemoHistory()
        {
            if (workboardStore == null) return;
            string historyNotice = workboardStore.HistoryNotice;
            bool checkpointDue = workboardStore.MemoAutosaveDue;
            bool flushed = !checkpointDue ||
                ((workbench == null || workbench.IsDisposed || workbench.Flush()) &&
                 (workboard == null || workboard.IsDisposed || workboard.Flush()));
            workboardStore.CheckpointMemoHistory(checkpointDue && flushed);
            if (historyNotice != workboardStore.HistoryNotice)
            {
                if (workboard != null && !workboard.IsDisposed) workboard.RefreshHistoryStatus();
                if (workbench != null && !workbench.IsDisposed) workbench.RefreshHistoryStatus();
            }
        }
        protected override void ExitThreadCore()
        {
            if (exiting) return;
            if (workbench != null && !workbench.Flush()) return;
            if (workboard != null && !workboard.Flush()) { Message("工作卡尚未保存成功，退出已取消。请检查程序文件夹是否可写，或先复制当前内容。", true); return; }
            if (workboardStore != null) workboardStore.CheckpointMemoHistory(true);
            if (!TrySave())
            {
                if (manager == null) tray.ShowBalloonTip(5000, "退出已取消", "桌面图片列表尚未保存成功，请检查程序文件夹是否可写后重试。", ToolTipIcon.Warning);
                return;
            }
            exiting = true;
            monitor.Stop(); monitor.Dispose();
            if (manager != null) { manager.AllowClose = true; manager.Close(); }
            if (workbench != null) { workbench.AllowClose = true; workbench.Close(); workbench.Dispose(); }
            foreach (var note in notes.Values) { if (note.ContextMenuStrip != null) note.ContextMenuStrip.Dispose(); note.Dispose(); }
            if (workboard != null) workboard.Dispose();
            tray.Visible = false; if (tray.ContextMenuStrip != null) tray.ContextMenuStrip.Dispose(); tray.Dispose();
            base.ExitThreadCore();
        }
    }
}
