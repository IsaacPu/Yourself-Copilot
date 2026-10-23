using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using Forms = System.Windows.Forms;
using DrawingPoint = System.Drawing.Point;
using WpfBrushes = System.Windows.Media.Brushes;

namespace LocalDesktopNote
{
    // The idle card belongs to the desktop. Editing temporarily makes the same
    // window a normal tool window so Explorer cannot steal keyboard/IME focus.
    internal sealed class WorkboardWindow : Forms.Form
    {
        internal readonly WorkboardStore Store;
        internal readonly Grid View;
        internal readonly TextBox IdeasEditor, NewTaskEditor;
        internal readonly List<TextBox> TaskEditors = new List<TextBox>();
        private readonly HashSet<TextBox> editedTaskTitles = new HashSet<TextBox>();
        internal readonly List<CheckBox> TaskChecks = new List<CheckBox>();
        internal readonly List<CheckBox> CompletedChecks = new List<CheckBox>();
        internal readonly List<TextBlock> CompletedLabels = new List<TextBlock>();
        internal readonly Expander CompletedGroup;
        private readonly System.Windows.Forms.Integration.ElementHost host;
        private readonly Border cardBorder;
        private readonly ScaleTransform compactScale;
        private IntPtr roundedRegionHandle;
        private System.Drawing.Size roundedRegionSize;
        private double roundedRadiusX, roundedRadiusY;
        private readonly Forms.Timer saveTimer;
        private readonly MemoSplitLayout memoSplit;
        private readonly MemoPaperControls paperControls;
        private readonly StackPanel rows, completedRows;
        private readonly TextBlock status;
        private readonly ContextMenu menu;
        private ContextMenu taskPlanMenu;
        private IntPtr desktopView;
        private bool editing, transitioning, disposing, moving, resizing, loading, renderingTasks;
        private bool dirty, viewDirty, saveFailed;
        private DrawingPoint pointerStart;
        private Rectangle boundsStart;
        private ResizeCorner activeCorner;
        private Rectangle resizeWorkingArea;
        private readonly List<FrameworkElement> cornerHandles = new List<FrameworkElement>();
        internal Action OpenManager;
        internal Action OpenWorkbench;
        internal Action OpenMemo;
        private bool publishing;
        private bool pendingDayRefresh;
        private DateTime displayedDay;
        private readonly EventHandler storeChanged;
        private readonly EventHandler storeSaved;
        internal bool ReturnToDesktopOnDeactivate = true;
        internal event EventHandler VisibilityChanged;
        internal bool IsEditing { get { return editing; } }
        internal bool Attached { get { return IsHandleCreated && desktopView != IntPtr.Zero && Desktop.GetParent(Handle) == desktopView; } }
        internal bool ConnectionAlive { get { return editing || (Attached && Desktop.IsWindow(desktopView)); } }
        internal Rectangle ScreenRectangle
        {
            get { Desktop.RECT rect; Desktop.GetWindowRect(Handle, out rect); return Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom); }
        }
        protected override bool ShowWithoutActivation { get { return !editing; } }
        protected override Forms.CreateParams CreateParams
        {
            get { var value = base.CreateParams; value.ExStyle |= ShowInTaskbar ? 0x40000 : 0x80; return value; }
        }

        internal WorkboardWindow(WorkboardStore store)
        {
            Store = store;
            Text = "Yourself Copilot · 想法与任务";
            Icon = AppVisuals.ApplicationIcon;
            FormBorderStyle = Forms.FormBorderStyle.None; ShowInTaskbar = false;
            AutoScaleMode = Forms.AutoScaleMode.None; StartPosition = Forms.FormStartPosition.Manual;
            BackColor = System.Drawing.Color.FromArgb(250, 248, 243);
            AccessibleName = "Yourself Copilot 桌面工作卡";
            using (var stream = File.OpenRead(Path.Combine(Storage.Folder, "WorkboardView.xaml"))) View = (Grid)XamlReader.Load(stream);
            // Stored geometry is in screen pixels. A small card at a higher DPI
            // must still keep its memo and add controls inside those same bounds.
            cardBorder = (Border)View.Children[0];
            compactScale = new ScaleTransform(1, 1);
            cardBorder.LayoutTransform = compactScale;
            View.SizeChanged += delegate(object sender, SizeChangedEventArgs e)
            {
                double scale = Math.Min(1, Math.Min(e.NewSize.Width / 350.0, e.NewSize.Height / 290.0));
                if (scale > 0) compactScale.ScaleX = compactScale.ScaleY = scale;
                UpdateWindowRegion();
            };
            View.SetValue(TextElement.FontFamilyProperty, AppTypography.Body);
            host = new System.Windows.Forms.Integration.ElementHost { Dock = Forms.DockStyle.Fill, Child = View };
            Controls.Add(host);
            IdeasEditor = Find<TextBox>("IdeasInput"); NewTaskEditor = Find<TextBox>("NewTaskInput");
            IdeasEditor.FontFamily = AppTypography.Memo; IdeasEditor.FontSize = 14;
            Find<TextBlock>("IdeasPlaceholder").FontFamily = AppTypography.Memo; Find<TextBlock>("IdeasPlaceholder").FontSize = 14;
            rows = Find<StackPanel>("TaskRows"); status = Find<TextBlock>("CardSaveStatus");
            completedRows = Find<StackPanel>("CompletedRows"); CompletedGroup = Find<Expander>("CompletedGroup");
            Find<ContentControl>("CompanionBrand").Content = AppVisuals.BrandIcon(23);
            Find<Button>("AddTaskButton").Content = AppVisuals.Icon("plus", 19, WpfBrushes.White);
            Find<Button>("CardMenuButton").Content = AppVisuals.Icon("more", 18, new SolidColorBrush(System.Windows.Media.Color.FromRgb(88, 116, 94)));
            Find<Button>("ExpandMemoButton").Content = AppVisuals.Icon("expand", 14, new SolidColorBrush(System.Windows.Media.Color.FromRgb(104, 121, 99)));
            Find<Button>("ExpandMemoButton").Click += delegate { EndEditing(); if (OpenMemo != null) OpenMemo(); else if (OpenWorkbench != null) OpenWorkbench(); };
            saveTimer = new Forms.Timer { Interval = 600 };
            saveTimer.Tick += delegate { saveTimer.Stop(); Flush(); };
            paperControls = new MemoPaperControls(Store, Flush, delegate(string text, bool error) { status.Text = text; status.ToolTip = text; }, delegate { if (editing) { IdeasEditor.Focus(); Keyboard.Focus(IdeasEditor); } }, true);
            Find<ContentControl>("MemoPaperActionsHost").Content = paperControls.View;
            paperControls.PaperSurface = Find<Border>("MemoRegion");
            paperControls.View.PreviewMouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) { if (!editing) BeginEditing(e.OriginalSource as UIElement ?? paperControls.Grabber); };
            var memoDivider = Find<GridSplitter>("MemoDivider");
            memoDivider.PreviewMouseLeftButtonDown += delegate { if (!editing) BeginEditing(memoDivider); };
            memoSplit = new MemoSplitLayout(Find<Grid>("MemoSplitPanel"), Find<StackPanel>("TaskContents"), memoDivider, 136,
                delegate { return Store.State.CardMemoTaskRatio; },
                delegate(double? value) { Store.State.CardMemoTaskRatio = value; QueueViewSave(); Flush(); });
            loading = true; IdeasEditor.Text = Store.State.Ideas; NewTaskEditor.Text = Store.State.TaskDraft; loading = false;
            IdeasEditor.TextChanged += delegate { if (loading) return; Store.State.Ideas = IdeasEditor.Text; UpdatePlaceholders(); QueueSave(); };
            NewTaskEditor.TextChanged += delegate { if (loading) return; Store.State.TaskDraft = NewTaskEditor.Text; if (String.IsNullOrWhiteSpace(NewTaskEditor.Text)) Store.State.DraftPlanDate = null; else if (Store.State.DraftPlanDate == null) Store.State.DraftPlanDate = WorkboardStore.FormatPlanDate(DefaultDraftDate()) ?? ""; UpdatePlaceholders(); QueueSave(); };
            HookEditor(IdeasEditor); HookEditor(NewTaskEditor);
            NewTaskEditor.PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                // IME-composition keys arrive as ImeProcessed and must not submit a task.
                if (e.Key == Key.Enter) { AddDraft(); e.Handled = true; }
            };
            Find<Button>("AddTaskButton").Click += delegate { if (!editing) BeginEditing(NewTaskEditor); AddDraft(); };
            Find<Button>("UndoTaskButton").Click += delegate { UndoRemoveTask(); };
            Find<Button>("OpenWorkbenchButton").Click += delegate { if (OpenWorkbench != null) { EndEditing(); OpenWorkbench(); } };
            foreach (string scope in new[] { "today", "tomorrow", "later", "all" })
            {
                string selected = scope;
                Find<Button>("Scope_" + scope).Click += delegate { SelectScope(selected); };
            }
            CompletedGroup.Expanded += delegate { if (!renderingTasks && !Store.State.CompletedExpanded) { Store.State.CompletedExpanded = true; QueueViewSave(); memoSplit.Refresh(); } };
            CompletedGroup.Collapsed += delegate { if (!renderingTasks && Store.State.CompletedExpanded) { Store.State.CompletedExpanded = false; QueueViewSave(); memoSplit.Refresh(); } };
            menu = new ContextMenu { FontFamily = AppTypography.Body };
            var lockItem = new MenuItem { Header = "锁定位置和大小", IsCheckable = true };
            lockItem.Click += delegate { if (Store.State.Locked == lockItem.IsChecked) return; Store.State.Locked = lockItem.IsChecked; UpdateLock(); QueueViewSave(true); };
            menu.Items.Add(lockItem);
            var reset = new MenuItem { Header = "恢复卡片位置" }; reset.Click += delegate { if (!Store.State.Locked) { Place(DefaultRectangle(), true); } }; menu.Items.Add(reset);
            var manage = new MenuItem { Header = "管理桌面图片" }; manage.Click += delegate { if (OpenManager != null) OpenManager(); }; menu.Items.Add(manage);
            var workspace = new MenuItem { Header = "打开工作台" }; workspace.Click += delegate { if (OpenWorkbench != null) { EndEditing(); OpenWorkbench(); } }; menu.Items.Insert(0, workspace);
            var hide = new MenuItem { Header = "隐藏卡片" }; hide.Click += delegate { HideCard(); }; menu.Items.Add(hide);
            Find<Button>("CardMenuButton").Click += delegate
            {
                lockItem.IsChecked = Store.State.Locked; reset.IsEnabled = !Store.State.Locked;
                menu.PlacementTarget = Find<Button>("CardMenuButton"); menu.IsOpen = true;
            };
            BindDrag(Find<Grid>("CardHeader"));
            BindEdge(ResizeCorner.Left, HorizontalAlignment.Left, VerticalAlignment.Stretch, Cursors.SizeWE, "左边缩放");
            BindEdge(ResizeCorner.Top, HorizontalAlignment.Stretch, VerticalAlignment.Top, Cursors.SizeNS, "上边缩放");
            BindEdge(ResizeCorner.Right, HorizontalAlignment.Right, VerticalAlignment.Stretch, Cursors.SizeWE, "右边缩放");
            BindEdge(ResizeCorner.Bottom, HorizontalAlignment.Stretch, VerticalAlignment.Bottom, Cursors.SizeNS, "下边缩放");
            BindCorner(ResizeCorner.TopLeft, HorizontalAlignment.Left, VerticalAlignment.Top, Cursors.SizeNWSE, "左上角缩放");
            BindCorner(ResizeCorner.TopRight, HorizontalAlignment.Right, VerticalAlignment.Top, Cursors.SizeNESW, "右上角缩放");
            BindCorner(ResizeCorner.BottomLeft, HorizontalAlignment.Left, VerticalAlignment.Bottom, Cursors.SizeNESW, "左下角缩放");
            BindCorner(ResizeCorner.BottomRight, HorizontalAlignment.Right, VerticalAlignment.Bottom, Cursors.SizeNWSE, "右下角缩放");
            View.PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Escape && editing) { EndEditing(); e.Handled = true; }
                if (e.Key == Key.S && (Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) != 0) { FinishMemoHistory(); e.Handled = true; }
            };
            Deactivate += delegate
            {
                if (!ReturnToDesktopOnDeactivate || transitioning || disposing || !editing) return;
                BeginInvoke(new Action(delegate { if (!disposing && editing && !ContainsFocus && !menu.IsOpen && (taskPlanMenu == null || !taskPlanMenu.IsOpen)) EndEditing(); }));
            };
            menu.Closed += delegate
            {
                if (ReturnToDesktopOnDeactivate && editing && !disposing)
                    BeginInvoke(new Action(delegate { if (!disposing && editing && !ContainsFocus) EndEditing(); }));
            };
            FormClosing += delegate(object sender, Forms.FormClosingEventArgs e)
            {
                if (!disposing && !FinishMemoHistory()) { e.Cancel = true; return; }
                if (!disposing && e.CloseReason == Forms.CloseReason.UserClosing) { e.Cancel = true; HideCard(); }
            };
            RenderTasks(); UpdatePlaceholders(); UpdateLock();
            storeChanged = delegate { if (!publishing && !disposing) RefreshSharedContent(); };
            Store.Changed += storeChanged;
            storeSaved = delegate
            {
                if (publishing || disposing) return;
                bool contentSave = dirty;
                dirty = viewDirty = false; saveTimer.Stop();
                if (contentSave) ShowSavedStatus();
            };
            Store.Saved += storeSaved;
            Store.MemoReplaced += MemoReplaced;
            if (!String.IsNullOrEmpty(Store.Notice)) { status.Text = "已恢复 · 详情见提示"; status.ToolTip = Store.Notice; }
        }

        private T Find<T>(string name) where T : class { return View.FindName(name) as T; }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            roundedRegionHandle = IntPtr.Zero;
            UpdateWindowRegion();
        }
        protected override void OnClientSizeChanged(EventArgs e)
        {
            base.OnClientSizeChanged(e);
            UpdateWindowRegion();
        }
        private void UpdateWindowRegion()
        {
            // A WPF rounded border cannot clip the surrounding native HWND.
            // Keep this in device pixels and never create a handle just to clip it.
            if (!IsHandleCreated || disposing || cardBorder == null || compactScale == null || ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
            IntPtr window = Handle;
            double dpiX, dpiY;
            var source = PresentationSource.FromVisual(View);
            if (source != null && source.CompositionTarget != null)
            {
                var device = source.CompositionTarget.TransformToDevice;
                dpiX = device.M11; dpiY = device.M22;
            }
            else
            {
                using (var graphics = System.Drawing.Graphics.FromHwnd(window)) { dpiX = graphics.DpiX / 96.0; dpiY = graphics.DpiY / 96.0; }
            }
            var size = ClientSize;
            double radiusX = Math.Min(size.Width / 2.0, cardBorder.CornerRadius.TopLeft * compactScale.ScaleX * dpiX);
            double radiusY = Math.Min(size.Height / 2.0, cardBorder.CornerRadius.TopLeft * compactScale.ScaleY * dpiY);
            if (roundedRegionHandle == window && roundedRegionSize == size && roundedRadiusX == radiusX && roundedRadiusY == radiusY && Region != null) return;
            using (var path = new System.Drawing.Drawing2D.GraphicsPath())
            {
                float diameterX = (float)(radiusX * 2), diameterY = (float)(radiusY * 2);
                if (diameterX > 0 && diameterY > 0)
                {
                    path.AddArc(0, 0, diameterX, diameterY, 180, 90);
                    path.AddArc(size.Width - diameterX, 0, diameterX, diameterY, 270, 90);
                    path.AddArc(size.Width - diameterX, size.Height - diameterY, diameterX, diameterY, 0, 90);
                    path.AddArc(0, size.Height - diameterY, diameterX, diameterY, 90, 90);
                    path.CloseFigure();
                }
                else path.AddRectangle(new Rectangle(0, 0, size.Width, size.Height));
                // Control.Region disposes its predecessor when a new Region is assigned.
                Region = new System.Drawing.Region(path);
            }
            roundedRegionHandle = window; roundedRegionSize = size; roundedRadiusX = radiusX; roundedRadiusY = radiusY;
        }
        private void HookEditor(TextBox editor)
        {
            editor.PreviewMouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (editing) return;
                BeginEditing(editor);
            };
        }

        internal Rectangle DefaultRectangle()
        {
            Rectangle area = Forms.Screen.PrimaryScreen.WorkingArea;
            int width = Math.Min(450, area.Width - 32), height = Math.Min(340, area.Height - 32);
            return Desktop.Fit(new Rectangle(area.Right - width - 74, area.Top + (int)(area.Height * 0.55), width, height));
        }

        internal void Attach()
        {
            Rectangle rectangle = Store.State.Positioned && Store.State.Width > 0 && Store.State.Height > 0
                ? new Rectangle(Store.State.X, Store.State.Y, Store.State.Width, Store.State.Height) : DefaultRectangle();
            ChangeNativeParent(true, rectangle);
        }

        internal void ShowCard(bool focus)
        {
            bool changed = Store.State.Hidden;
            Store.State.Hidden = false;
            if (!editing && !Attached) Attach();
            if (!Visible) { Show(); Desktop.ShowWindow(Handle, 4); }
            if (focus) BeginEditing(IdeasEditor);
            if (changed) QueueViewSave(true);
            if (VisibilityChanged != null) VisibilityChanged(this, EventArgs.Empty);
        }

        internal void HideCard()
        {
            if (editing) EndEditing();
            bool changed = !Store.State.Hidden;
            Store.State.Hidden = true; Hide(); if (changed) QueueViewSave(true); FinishMemoHistory();
            if (VisibilityChanged != null) VisibilityChanged(this, EventArgs.Empty);
        }

        internal void BeginEditing(UIElement editor)
        {
            if (disposing) return;
            if (!editing)
            {
                try { ChangeNativeParent(false, ScreenRectangle); }
                catch (Exception error) { Storage.Log(error.ToString()); status.Text = "暂时无法输入 · 请重试"; status.ToolTip = error.Message; return; }
            }
            if (!Visible) Show();
            Desktop.SetForegroundWindow(Handle); host.Focus();
            editor.Dispatcher.BeginInvoke(new Action(delegate { if (editing && !disposing) { editor.Focus(); Keyboard.Focus(editor); } }));
        }

        internal void EndEditing()
        {
            if (!editing) return;
            foreach (var input in TaskEditors) CommitTaskTitle(input, (WorkTask)input.Tag);
            if (!FinishMemoHistory()) return;
            try { ChangeNativeParent(true, ScreenRectangle); }
            catch (Exception error) { Storage.Log(error.ToString()); status.Text = "内容已保留 · 下次离开输入区时重试连接桌面"; status.ToolTip = error.Message; }
        }

        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool RedrawWindow(IntPtr handle, IntPtr rectangle, IntPtr region, uint flags);

        // Keep the visible frame stable while switching native parent and styles.
        // Freeze only the outer form: ElementHost manages its own redraw state.
        // WM_SETREDRAW affects WS_VISIBLE, so read the current style after freezing.
        private void ChangeNativeParent(bool toDesktop, Rectangle rectangle)
        {
            IntPtr target = toDesktop ? Desktop.FindView() : IntPtr.Zero;
            if (toDesktop && target == IntPtr.Zero) throw new InvalidOperationException("暂时无法连接 Windows 桌面，内容仍保存在本机。");
            rectangle.Width = Math.Max(350, rectangle.Width); rectangle.Height = Math.Max(290, rectangle.Height);
            rectangle = Desktop.Fit(rectangle);
            IntPtr window = Handle;
            long originalStyle = Desktop.GetWindowLongPtr(window, Desktop.GWL_STYLE).ToInt64();
            bool originalEditing = editing;
            IntPtr originalParent = (originalStyle & Desktop.WS_CHILD) != 0 ? Desktop.GetParent(window) : IntPtr.Zero;
            Rectangle originalRectangle = ScreenRectangle;
            bool frozen = Desktop.IsWindowVisible(window);
            transitioning = true;
            try
            {
                if (frozen) SendMessage(window, 0xB, IntPtr.Zero, IntPtr.Zero);
                editing = !toDesktop;
                IdeasEditor.IsReadOnly = toDesktop; NewTaskEditor.IsReadOnly = toDesktop;
                foreach (var input in TaskEditors) input.IsReadOnly = toDesktop;
                if (toDesktop) Keyboard.ClearFocus();
                long style = Desktop.GetWindowLongPtr(window, Desktop.GWL_STYLE).ToInt64();
                if (toDesktop)
                {
                    Desktop.SetWindowLongPtr(window, Desktop.GWL_STYLE, new IntPtr((style | Desktop.WS_CHILD) & ~Desktop.WS_POPUP));
                    Desktop.SetParent(window, target); desktopView = target;
                }
                else
                {
                    Desktop.SetParent(window, IntPtr.Zero);
                    style = Desktop.GetWindowLongPtr(window, Desktop.GWL_STYLE).ToInt64();
                    Desktop.SetWindowLongPtr(window, Desktop.GWL_STYLE, new IntPtr((style | Desktop.WS_POPUP) & ~Desktop.WS_CHILD));
                }
                bool childStyle = (Desktop.GetWindowLongPtr(window, Desktop.GWL_STYLE).ToInt64() & Desktop.WS_CHILD) != 0;
                if (childStyle != toDesktop || (toDesktop && Desktop.GetParent(window) != target)) throw new InvalidOperationException("卡片窗口连接未完成。");
                var point = new Desktop.POINT(rectangle.X, rectangle.Y);
                if (toDesktop) Desktop.ScreenToClient(target, ref point);
                if (!Desktop.SetWindowPos(window, IntPtr.Zero, point.X, point.Y, rectangle.Width, rectangle.Height, 0x4 | 0x8 | Desktop.SWP_NOACTIVATE | Desktop.SWP_FRAMECHANGED))
                    throw new InvalidOperationException("卡片窗口定位失败：" + Marshal.GetLastWin32Error());
                UpdateWindowRegion();
                Store.State.X = rectangle.X; Store.State.Y = rectangle.Y; Store.State.Width = rectangle.Width; Store.State.Height = rectangle.Height; Store.State.Positioned = true;
            }
            catch
            {
                editing = originalEditing;
                IdeasEditor.IsReadOnly = !editing; NewTaskEditor.IsReadOnly = !editing;
                foreach (var input in TaskEditors) input.IsReadOnly = !editing;
                try
                {
                    long rollbackStyle = (originalStyle & ~0x10000000L) | (Desktop.GetWindowLongPtr(window, Desktop.GWL_STYLE).ToInt64() & 0x10000000L);
                    Desktop.SetWindowLongPtr(window, Desktop.GWL_STYLE, new IntPtr(rollbackStyle));
                    Desktop.SetParent(window, originalParent);
                    var point = new Desktop.POINT(originalRectangle.X, originalRectangle.Y);
                    if (originalParent != IntPtr.Zero) Desktop.ScreenToClient(originalParent, ref point);
                    Desktop.SetWindowPos(window, IntPtr.Zero, point.X, point.Y, originalRectangle.Width, originalRectangle.Height, 0x4 | 0x8 | Desktop.SWP_NOACTIVATE | Desktop.SWP_FRAMECHANGED);
                }
                catch (Exception rollbackError) { Storage.Log("卡片窗口还原失败：" + rollbackError.Message); }
                throw;
            }
            finally
            {
                if (frozen && Desktop.IsWindow(window))
                {
                    SendMessage(window, 0xB, new IntPtr(1), IntPtr.Zero);
                    RedrawWindow(window, IntPtr.Zero, IntPtr.Zero, 0x1 | 0x80 | 0x100 | 0x400);
                }
                transitioning = false;
            }
        }

        internal void Place(Rectangle rectangle, bool save)
        {
            Rectangle before = ScreenRectangle;
            rectangle.Width = Math.Max(350, rectangle.Width); rectangle.Height = Math.Max(290, rectangle.Height);
            rectangle = Desktop.Fit(rectangle);
            var point = new Desktop.POINT(rectangle.X, rectangle.Y);
            if (!editing) Desktop.ScreenToClient(desktopView, ref point);
            if (!Desktop.SetWindowPos(Handle, IntPtr.Zero, point.X, point.Y, rectangle.Width, rectangle.Height, Desktop.SWP_NOACTIVATE))
                throw new InvalidOperationException("调整卡片位置失败。");
            Store.State.X = rectangle.X; Store.State.Y = rectangle.Y; Store.State.Width = rectangle.Width; Store.State.Height = rectangle.Height; Store.State.Positioned = true;
            if (save && ScreenRectangle != before) QueueViewSave(true);
        }

        private void BindDrag(UIElement element)
        {
            element.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (Store.State.Locked || (e.OriginalSource as DependencyObject != null && ParentButton(e.OriginalSource as DependencyObject))) return;
                pointerStart = PointerPosition(e); boundsStart = ScreenRectangle;
                activeCorner = ResizeCorner.None;
                moving = true; resizing = false; element.CaptureMouse(); e.Handled = true;
            };
            element.MouseMove += delegate(object sender, System.Windows.Input.MouseEventArgs e)
            {
                if (!moving || !element.IsMouseCaptured) return;
                DrawingPoint current = PointerPosition(e);
                var rectangle = boundsStart;
                rectangle.Location = new DrawingPoint(boundsStart.X + current.X - pointerStart.X, boundsStart.Y + current.Y - pointerStart.Y);
                Place(rectangle, false);
            };
            element.MouseLeftButtonUp += delegate { if (moving) { moving = false; if (ScreenRectangle != boundsStart) QueueViewSave(true); } element.ReleaseMouseCapture(); };
            element.LostMouseCapture += delegate { if (moving) { moving = false; if (ScreenRectangle != boundsStart) QueueViewSave(true); } };
        }

        private void BindCorner(ResizeCorner corner, HorizontalAlignment horizontal, VerticalAlignment vertical, System.Windows.Input.Cursor cursor, string label)
        {
            var handle = new Border { Name = "Corner_" + corner, Width = 14, Height = 14, HorizontalAlignment = horizontal, VerticalAlignment = vertical,
                Background = WpfBrushes.Transparent, Cursor = cursor, ToolTip = "拖动" + label + "；锁定后不可调整" };
            BindResizeHandle(handle, corner, label);
        }

        private void BindEdge(ResizeCorner edge, HorizontalAlignment horizontal, VerticalAlignment vertical, System.Windows.Input.Cursor cursor, string label)
        {
            bool side = horizontal != HorizontalAlignment.Stretch;
            // Stay inside the outer 6 DIP and leave all corner targets clear.
            // The existing content has an 18 DIP side inset and 6 DIP top inset.
            var handle = new Border { Name = "Edge_" + edge, HorizontalAlignment = horizontal, VerticalAlignment = vertical,
                Margin = side ? new Thickness(0, 14, 0, 14) : new Thickness(14, 0, 14, 0),
                Background = WpfBrushes.Transparent, Cursor = cursor, ToolTip = "拖动" + label + "；锁定后不可调整" };
            if (side) handle.Width = 6; else handle.Height = 6;
            BindResizeHandle(handle, edge, label);
        }

        private void BindResizeHandle(Border handle, ResizeCorner corner, string label)
        {
            System.Windows.Automation.AutomationProperties.SetName(handle, label);
            handle.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (!BeginCornerResize(corner, PointerPosition(e))) return;
                handle.CaptureMouse(); e.Handled = true;
            };
            handle.MouseMove += delegate(object sender, System.Windows.Input.MouseEventArgs e) { if (resizing && handle.IsMouseCaptured) ResizeCornerTo(PointerPosition(e)); };
            handle.MouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e) { if (!handle.IsMouseCaptured) return; EndCornerResize(); handle.ReleaseMouseCapture(); e.Handled = true; };
            handle.LostMouseCapture += delegate { EndCornerResize(); };
            cornerHandles.Add(handle); View.Children.Add(handle); View.RegisterName(handle.Name, handle);
        }

        internal bool BeginCornerResize(ResizeCorner corner, DrawingPoint screenPoint)
        {
            if (Store.State.Locked || disposing || corner == ResizeCorner.None) return false;
            pointerStart = screenPoint; boundsStart = ScreenRectangle;
            resizeWorkingArea = Forms.Screen.FromRectangle(boundsStart).WorkingArea;
            activeCorner = corner; moving = false; resizing = true; return true;
        }
        internal void ResizeCornerTo(DrawingPoint screenPoint)
        {
            if (!resizing) return;
            if (Store.State.Locked) { EndCornerResize(); return; }
            Place(CornerResize.Bounds(boundsStart, new DrawingPoint(screenPoint.X - pointerStart.X, screenPoint.Y - pointerStart.Y),
                activeCorner, new System.Drawing.Size(350, 290), resizeWorkingArea), false);
        }
        internal void EndCornerResize()
        {
            if (!resizing) return;
            resizing = false; activeCorner = ResizeCorner.None;
            if (ScreenRectangle != boundsStart) QueueViewSave(true);
        }

        private DrawingPoint PointerPosition(System.Windows.Input.MouseEventArgs e)
        {
            var point = View.PointToScreen(e.GetPosition(View));
            return new DrawingPoint((int)Math.Round(point.X), (int)Math.Round(point.Y));
        }

        private static bool ParentButton(DependencyObject current)
        {
            while (current != null)
            {
                if (current is Button) return true;
                current = VisualTreeHelper.GetParent(current);
            }
            return false;
        }

        private void UpdateLock()
        {
            Find<Grid>("CardHeader").Cursor = Store.State.Locked ? Cursors.Arrow : Cursors.SizeAll;
            foreach (var handle in cornerHandles) handle.Visibility = Store.State.Locked ? Visibility.Collapsed : Visibility.Visible;
        }
        private void UpdatePlaceholders()
        {
            Find<TextBlock>("IdeasPlaceholder").Visibility = IdeasEditor.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            Find<TextBlock>("TaskPlaceholder").Visibility = NewTaskEditor.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            DateTime date;
            DateTime? planned = Store.State.DraftPlanDate == "" ? (DateTime?)null : WorkboardStore.TryPlanDate(Store.State.DraftPlanDate, out date) ? date : DefaultDraftDate();
            NewTaskEditor.ToolTip = "计划日期：" + (planned.HasValue ? planned.Value.ToString("yyyy-MM-dd") : "未安排") + "；按 Enter 添加";
        }
        internal void AddDraft()
        {
            CommitTaskTitles();
            // The model may be newer than this editor after a deferred refresh.
            if (NewTaskEditor.Text != Store.State.TaskDraft)
            {
                bool wasLoading = loading; loading = true;
                try { NewTaskEditor.Text = Store.State.TaskDraft; }
                finally { loading = wasLoading; }
                UpdatePlaceholders();
            }
            DateTime planned;
            DateTime? date = Store.State.DraftPlanDate == "" ? (DateTime?)null : WorkboardStore.TryPlanDate(Store.State.DraftPlanDate, out planned) ? planned : DefaultDraftDate();
            WorkTask task = Store.AddTask(NewTaskEditor.Text, date);
            if (task == null) return;
            if (!Store.SelectTasks(Store.State.TaskScope).Contains(task)) Store.State.TaskScope = ScopeFor(task);
            NewTaskEditor.Clear(); Store.State.DraftPlanDate = null; UpdatePlaceholders(); RenderTasks(); QueueSave();
            BringTaskIntoView(task);
            if (editing) NewTaskEditor.Focus();
        }
        internal void RemoveTask(WorkTask task) { CommitTaskTitles(); Store.Remove(task); RenderTasks(); QueueSave(); }
        internal void UndoRemoveTask()
        {
            CommitTaskTitles();
            var restored = Store.Undo(); if (restored == null) return;
            if (restored.Done) Store.State.CompletedExpanded = true;
            if (!Store.SelectTasks(Store.State.TaskScope).Contains(restored))
                Store.State.TaskScope = ScopeFor(restored);
            RenderTasks(); QueueSave(); BringTaskIntoView(restored);
        }
        private void BringTaskIntoView(WorkTask task)
        {
            FrameworkElement target = task.Done
                ? CompletedLabels.FirstOrDefault(t => t.Tag == task) as FrameworkElement
                : TaskEditors.FirstOrDefault(t => t.Tag == task) as FrameworkElement;
            if (target != null) target.Dispatcher.BeginInvoke(new Action(delegate { if (!disposing) target.BringIntoView(); }));
        }
        internal void SetTaskCompleted(WorkTask task, bool done)
        {
            if (!Store.State.Tasks.Contains(task) || task.Done == done) return;
            CommitTaskTitles();
            int oldIndex = TaskChecks.FindIndex(c => c.Tag == task);
            Store.SetCompleted(task, done); RenderTasks(); QueueSave();
            if (editing)
            {
                UIElement focus = !done ? TaskChecks.FirstOrDefault(c => c.Tag == task)
                    : TaskChecks.Count > 0 ? TaskChecks[Math.Max(0, Math.Min(oldIndex, TaskChecks.Count - 1))] as UIElement : NewTaskEditor;
                if (focus != null) focus.Dispatcher.BeginInvoke(new Action(delegate { if (editing && !disposing) focus.Focus(); }));
            }
        }
        private void CommitTaskTitle(TextBox input, WorkTask task)
        {
            string value = input.Text.Trim();
            bool edited = editedTaskTitles.Remove(input);
            bool changed = edited && value.Length > 0 && value != task.Text && Store.State.Tasks.Contains(task);
            bool wasLoading = loading; loading = true;
            try
            {
                // Only a local edit can submit a title; an old mirror refresh
                // never promotes itself over a newer committed task.
                if (changed) task.Text = value;
                if (input.Text != task.Text) input.Text = task.Text;
            }
            finally { loading = wasLoading; }
            if (changed) QueueSave();
        }
        private void CommitTaskTitles() { foreach (var input in TaskEditors.ToArray()) CommitTaskTitle(input, (WorkTask)input.Tag); }
        internal void RenderTasks()
        {
            if (editedTaskTitles.Count > 0) { pendingDayRefresh = true; return; }
            renderingTasks = true;
            rows.Children.Clear(); completedRows.Children.Clear(); TaskEditors.Clear(); TaskChecks.Clear(); CompletedChecks.Clear(); CompletedLabels.Clear();
            displayedDay = Store.Today;
            string scope = Store.State.TaskScope;
            var visible = Store.SelectTasks(scope).ToList();
            var pendingTasks = visible.Where(t => !t.Done).ToList();
            if (scope == "today") pendingTasks = pendingTasks.OrderBy(t => t.PlanDate == WorkboardStore.FormatPlanDate(Store.Today) ? 1 : 0).ToList();
            string lastGroup = null;
            foreach (WorkTask task in pendingTasks.Concat(visible.Where(t => t.Done)))
            {
                DateTime plan;
                string group = WorkboardStore.TryPlanDate(task.PlanDate, out plan) && plan < Store.Today ? "之前未完成" : "今天";
                if (!task.Done && scope == "today" && lastGroup != group)
                {
                    rows.Children.Add(new TextBlock { Text = group, FontSize = 11, Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(104, 121, 99)), Margin = new Thickness(0, 9, 0, 3) });
                    lastGroup = group;
                }
                var grid = new Grid { Margin = new Thickness(0, 0, 0, 3), MinHeight = 33 };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
                grid.ColumnDefinitions.Add(new ColumnDefinition());
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(27) });
                var check = new CheckBox { Tag = task, IsChecked = task.Done, VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand };
                System.Windows.Automation.AutomationProperties.SetName(check, (task.Done ? "恢复为待办：" : "完成任务：") + task.Text);
                check.Click += delegate { SetTaskCompleted(task, check.IsChecked == true); };
                var remove = new Button { Content = AppVisuals.Icon("more", 16, new SolidColorBrush(System.Windows.Media.Color.FromRgb(124, 135, 120))), Padding = new Thickness(4, 5, 4, 5), ToolTip = "任务操作：安排日期或移除" };
                System.Windows.Automation.AutomationProperties.SetName(remove, "任务操作：" + task.Text);
                remove.Click += delegate { ShowPlanMenu(task, remove, true); };
                grid.Children.Add(check); Grid.SetColumn(remove, 3); grid.Children.Add(remove);
                var dateButton = new Button { Content = PlanLabel(task), FontSize = 10, Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(124, 135, 120)), Padding = new Thickness(3, 2, 3, 2), ToolTip = "更改计划日期", VerticalAlignment = VerticalAlignment.Center };
                if (scope == "today" && task.PlanDate == WorkboardStore.FormatPlanDate(Store.Today)) dateButton.Visibility = Visibility.Collapsed;
                System.Windows.Automation.AutomationProperties.SetName(dateButton, "安排日期：" + task.Text);
                dateButton.Click += delegate { ShowPlanMenu(task, dateButton); };
                Grid.SetColumn(dateButton, 2); grid.Children.Add(dateButton);
                if (task.Done)
                {
                    var label = new TextBlock { Tag = task, Text = task.Text, FontSize = 13, Margin = new Thickness(0, 5, 3, 5), TextWrapping = TextWrapping.Wrap,
                        VerticalAlignment = VerticalAlignment.Center, TextDecorations = TextDecorations.Strikethrough,
                        Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(145, 151, 139)) };
                    Grid.SetColumn(label, 1); grid.Children.Add(label); completedRows.Children.Add(grid); CompletedLabels.Add(label); CompletedChecks.Add(check);
                }
                else
                {
                    var input = new TextBox { Tag = task, Text = task.Text, IsReadOnly = !editing, MaxLength = 240, FontSize = 13, Padding = new Thickness(0, 5, 3, 5), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
                    System.Windows.Automation.AutomationProperties.SetName(input, "任务内容");
                    HookEditor(input);
                    input.TextChanged += delegate
                    {
                        if (loading || renderingTasks) return;
                        editedTaskTitles.Add(input);
                    };
                    input.LostKeyboardFocus += delegate { if (loading || renderingTasks) return; CommitTaskTitle(input, task); Flush(); if (pendingDayRefresh && !disposing) input.Dispatcher.BeginInvoke(new Action(RenderTasks)); };
                    input.PreviewKeyDown += delegate(object sender, KeyEventArgs e)
                    {
                        if (e.Key == Key.Enter) { CommitTaskTitle(input, task); Flush(); NewTaskEditor.Focus(); e.Handled = true; }
                    };
                    Grid.SetColumn(input, 1); grid.Children.Add(input); rows.Children.Add(grid); TaskEditors.Add(input); TaskChecks.Add(check);
                }
            }
            int pending = pendingTasks.Count, completed = visible.Count(t => t.Done);
            Find<TextBlock>("TasksPlaceholder").Visibility = pending == 0 ? Visibility.Visible : Visibility.Collapsed;
            int unplanned = Store.State.Tasks.Count(t => !t.Done && String.IsNullOrEmpty(t.PlanDate));
            Find<TextBlock>("TasksPlaceholder").Text = completed > 0 ? "当前待办都完成了，给自己一点空白。" : scope == "today" && unplanned > 0 ? "今天还没有安排；有 " + unplanned + " 项未安排任务，可在“以后”查看。" : "这个安排里还没有任务，从一件小事开始。";
            CompletedGroup.Visibility = completed == 0 ? Visibility.Collapsed : Visibility.Visible;
            CompletedGroup.Header = "已完成（" + completed + "）";
            System.Windows.Automation.AutomationProperties.SetName(CompletedGroup, "已完成 " + completed + " 项，展开或收起");
            CompletedGroup.IsExpanded = Store.State.CompletedExpanded;
            Find<Button>("UndoTaskButton").Visibility = Store.State.Removed.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            UpdateTaskCount(); renderingTasks = false; pendingDayRefresh = false;
            memoSplit.Refresh();
        }
        internal void SelectScope(string scope)
        {
            if (scope == Store.State.TaskScope || (scope != "today" && scope != "tomorrow" && scope != "later" && scope != "all")) return;
            CommitTaskTitles();
            if (dirty && !Flush()) return;
            Store.State.TaskScope = scope;
            UpdatePlaceholders(); RenderTasks(); QueueViewSave();
        }
        private DateTime? DefaultDraftDate()
        {
            return Store.State.TaskScope == "tomorrow" ? Store.Today.AddDays(1) : Store.State.TaskScope == "later" ? (DateTime?)null : Store.Today;
        }
        internal void RefreshDay()
        {
            if (displayedDay == Store.Today) return;
            displayedDay = Store.Today; UpdatePlaceholders();
            if (TaskEditors.Any(t => t.IsKeyboardFocusWithin)) { pendingDayRefresh = true; UpdateTaskCount(); }
            else RenderTasks();
        }
        private string ScopeFor(WorkTask task)
        {
            DateTime date;
            if (!WorkboardStore.TryPlanDate(task.PlanDate, out date)) return "later";
            if (date < Store.Today && task.Done && !Store.SelectTasks("today").Contains(task)) return "all";
            return date <= Store.Today ? "today" : date == Store.Today.AddDays(1) ? "tomorrow" : "later";
        }
        private string PlanLabel(WorkTask task)
        {
            DateTime date;
            if (!WorkboardStore.TryPlanDate(task.PlanDate, out date)) return "未安排";
            return date == Store.Today ? "今天" : date == Store.Today.AddDays(1) ? "明天" : date.ToString("M/d");
        }
        private void ShowPlanMenu(WorkTask task, Button anchor, bool includeRemove = false)
        {
            var choices = new ContextMenu { FontFamily = AppTypography.Body }; taskPlanMenu = choices;
            choices.Closed += delegate { if (editing && ReturnToDesktopOnDeactivate && !ContainsFocus) BeginInvoke(new Action(EndEditing)); };
            foreach (string key in new[] { "today", "tomorrow", "later" })
            {
                string choice = key;
                var item = new MenuItem { Header = key == "today" ? "安排到今天" : key == "tomorrow" ? "安排到明天" : "暂不安排日期" };
                item.Click += delegate
                {
                    DateTime? date = choice == "later" ? (DateTime?)null : choice == "tomorrow" ? Store.Today.AddDays(1) : Store.Today;
                    if (task.PlanDate == WorkboardStore.FormatPlanDate(date)) return;
                    CommitTaskTitles();
                    Store.SetPlanDate(task, date);
                    RenderTasks(); QueueSave();
                };
                choices.Items.Add(item);
            }
            var custom = new MenuItem { Header = "在工作台选择其他日期…" };
            custom.Click += delegate { if (OpenWorkbench != null) { EndEditing(); OpenWorkbench(); } };
            choices.Items.Add(custom);
            if (includeRemove)
            {
                choices.Items.Add(new Separator());
                var remove = new MenuItem { Header = "移除任务（可撤销）" };
                remove.Click += delegate { RemoveTask(task); };
                choices.Items.Add(remove);
            }
            choices.PlacementTarget = anchor; choices.IsOpen = true;
        }
        internal void RefreshSharedContent()
        {
            loading = true;
            try
            {
                if (!IdeasEditor.IsKeyboardFocusWithin && IdeasEditor.Text != Store.State.Ideas) IdeasEditor.Text = Store.State.Ideas;
                if (!NewTaskEditor.IsKeyboardFocusWithin && NewTaskEditor.Text != Store.State.TaskDraft) NewTaskEditor.Text = Store.State.TaskDraft;
            }
            finally { loading = false; }
            UpdatePlaceholders();
            if (TaskEditors.Any(t => t.IsKeyboardFocusWithin)) pendingDayRefresh = true; else RenderTasks();
        }
        private void MemoReplaced(object sender, EventArgs e)
        {
            if (disposing) return;
            saveTimer.Stop(); loading = true;
            try { IdeasEditor.Text = Store.State.Ideas ?? ""; }
            finally { loading = false; }
            dirty = viewDirty = saveFailed = false;
            UpdatePlaceholders();
        }
        private void UpdateTaskCount()
        {
            var visible = Store.SelectTasks(Store.State.TaskScope).ToList();
            int pending = visible.Count(t => !t.Done);
            Find<TextBlock>("TaskCount").Text = visible.Count == 0 ? "" : pending == 0 ? "全部完成" : pending + " 项待办";
            Find<TextBlock>("ScopeDate").Text = Store.Today.ToString("M月d日") + " · " + (Store.State.TaskScope == "tomorrow" ? "明天 " + Store.Today.AddDays(1).ToString("M/d") : Store.State.TaskScope == "later" ? "以后／未安排" : Store.State.TaskScope == "all" ? "全部任务" : "今天");
            foreach (string key in new[] { "today", "tomorrow", "later", "all" })
            {
                var button = Find<Button>("Scope_" + key);
                button.Background = Store.State.TaskScope == key ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(232, 238, 229)) : WpfBrushes.Transparent;
                int count = Store.SelectTasks(key).Count(t => !t.Done);
                button.Content = (key == "today" ? "今天" : key == "tomorrow" ? "明天" : key == "later" ? "以后" : "全部") + (count > 0 ? " " + count : "");
            }
        }
        internal void QueueSave()
        {
            if (disposing) return;
            dirty = true;
            if (!saveFailed)
            {
                status.Text = "修改待保存";
                status.ToolTip = "停止输入后自动保存到本机；按 Ctrl+S 可立即保存。";
                status.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(125, 137, 119));
            }
            saveTimer.Stop(); saveTimer.Start();
            try { publishing = true; Store.NotifyChanged(); }
            finally { publishing = false; }
        }
        internal void QueueViewSave(bool persistSoon = false)
        {
            if (disposing) return;
            // Browsing stays in memory until a content save or explicit flush.
            // Do not restart the content timer or overwrite its status.
            viewDirty = true;
            // Geometry/visibility gets one quiet save after the gesture ends.
            // Browsing alone stays in memory; never delay a pending content save.
            if (persistSoon && !dirty) { saveTimer.Stop(); saveTimer.Start(); }
            try { publishing = true; Store.NotifyChanged(); }
            finally { publishing = false; }
        }
        internal bool Flush()
        {
            saveTimer.Stop();
            if (!dirty && !viewDirty) return true;
            bool contentSave = dirty;
            try
            {
                publishing = true; Store.Save(); dirty = viewDirty = false;
                if (!contentSave) return true;
                ShowSavedStatus(); return true;
            }
            catch (Exception error)
            {
                if (!contentSave) { Storage.Log("视图偏好暂未保存：" + error.Message); return true; }
                status.Text = "保存失败 · Ctrl+S 重试"; status.Foreground = WpfBrushes.IndianRed;
                saveFailed = true;
                status.ToolTip = "内容仍在当前窗口。请检查程序文件夹是否可写。\n" + error.Message;
                Storage.Log("卡片保存失败：" + error.Message); return false;
            }
            finally { publishing = false; }
        }
        private void ShowSavedStatus()
        {
            saveFailed = false;
            status.Text = String.IsNullOrEmpty(Store.HistoryNotice) ? "已保存到本机" : "已保存 · 历史备份需检查"; status.ToolTip = Store.HistoryNotice ?? Store.Notice;
            status.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(126, 144, 117));
        }
        internal bool FinishMemoHistory()
        {
            CommitTaskTitles();
            if (!Flush()) return false;
            bool hadNotice = !String.IsNullOrEmpty(Store.HistoryNotice);
            if (!Store.CheckpointMemoHistory(true) || hadNotice) ShowSavedStatus();
            return true;
        }
        internal void RefreshHistoryStatus()
        {
            if (!dirty && !saveFailed && !disposing) ShowSavedStatus();
        }
        protected override void WndProc(ref Forms.Message message)
        {
            if (message.Msg == 0x21 && !editing) { message.Result = new IntPtr(3); return; }
            base.WndProc(ref message);
            if (message.Msg == 0x02E0 || message.Msg == 0x02E3) UpdateWindowRegion();
        }
        protected override void Dispose(bool value)
        {
            if (value && !disposing)
            {
                FinishMemoHistory(); disposing = true; Store.Changed -= storeChanged; Store.Saved -= storeSaved; Store.MemoReplaced -= MemoReplaced; saveTimer.Dispose(); memoSplit.Dispose(); paperControls.Dispose(); menu.IsOpen = false; if (taskPlanMenu != null) taskPlanMenu.IsOpen = false; host.Dispose();
            }
            base.Dispose(value);
            if (value) Region = null;
        }
    }
}
