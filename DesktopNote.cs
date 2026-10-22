using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace LocalDesktopNote
{
    [DataContract]
    public sealed class Preferences
    {
        [DataMember] public int X;
        [DataMember] public int Y;
        [DataMember] public int Width;
        [DataMember] public bool Positioned;
        [DataMember] public bool Locked;
        [DataMember] public string ImagePath;
        [DataMember] public string Id;
        [DataMember] public string Title;
        [DataMember] public string ContentHash;
        [DataMember] public int PixelWidth;
        [DataMember] public int PixelHeight;
        [DataMember] public bool Hidden;
    }

    internal static class Storage
    {
        internal static readonly string Folder = AppDomain.CurrentDomain.BaseDirectory;
        internal static readonly string SettingsFile = Path.Combine(Folder, "settings.json");
        internal static Preferences Load()
        {
            if (File.Exists(SettingsFile))
            {
                try
                {
                    using (var stream = File.OpenRead(SettingsFile))
                        return (Preferences)new DataContractJsonSerializer(typeof(Preferences)).ReadObject(stream);
                }
                catch (Exception e) { Log("读取设置失败，将使用默认位置：" + e.Message); }
            }
            return new Preferences();
        }
        internal static void Save(Preferences value)
        {
            string temporary = SettingsFile + ".tmp";
            using (var stream = File.Create(temporary))
                new DataContractJsonSerializer(typeof(Preferences)).WriteObject(stream, value);
            if (File.Exists(SettingsFile)) File.Replace(temporary, SettingsFile, null);
            else File.Move(temporary, SettingsFile);
        }
        internal static void Log(string text)
        {
            try { File.AppendAllText(Path.Combine(Folder, "运行日志.txt"), DateTime.Now.ToString("s") + " " + text + Environment.NewLine, Encoding.UTF8); }
            catch { }
        }
    }

    internal static class Desktop
    {
        internal const int GWL_STYLE = -16, GWL_EXSTYLE = -20;
        internal const long WS_CHILD = 0x40000000L, WS_POPUP = 0x80000000L;
        internal const long WS_EX_TOPMOST = 0x8L;
        internal const uint SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40, SWP_FRAMECHANGED = 0x20;
        internal delegate bool EnumWindowsProc(IntPtr handle, IntPtr parameter);
        [StructLayout(LayoutKind.Sequential)] internal struct POINT { internal int X, Y; internal POINT(int x, int y) { X = x; Y = y; } }
        [StructLayout(LayoutKind.Sequential)] internal struct RECT { internal int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumWindowsProc callback, IntPtr value);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string title);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(IntPtr handle, StringBuilder text, int length);
        [DllImport("user32.dll", SetLastError = true)] internal static extern IntPtr SetParent(IntPtr child, IntPtr parent);
        [DllImport("user32.dll")] internal static extern IntPtr GetParent(IntPtr handle);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static extern IntPtr GetWindowLongPtr(IntPtr handle, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] internal static extern IntPtr SetWindowLongPtr(IntPtr handle, int index, IntPtr value);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowPos(IntPtr handle, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] internal static extern bool ScreenToClient(IntPtr handle, ref POINT point);
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr handle, out RECT rectangle);
        [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr handle);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr handle);
        [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr handle, int command);
        [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr handle);
        [DllImport("user32.dll")] internal static extern bool BringWindowToTop(IntPtr handle);
        [DllImport("user32.dll")] internal static extern bool SetProcessDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern bool SystemParametersInfo(uint action, uint parameter, StringBuilder value, uint flags);

        internal static string ClassName(IntPtr handle)
        {
            var value = new StringBuilder(256); GetClassName(handle, value, value.Capacity); return value.ToString();
        }
        internal static string Wallpaper()
        {
            var value = new StringBuilder(1024); SystemParametersInfo(0x0073, (uint)value.Capacity, value, 0); return value.ToString();
        }
        internal static IntPtr FindView()
        {
            IntPtr result = IntPtr.Zero;
            EnumWindows(delegate(IntPtr handle, IntPtr ignored)
            {
                string name = ClassName(handle);
                if (name != "Progman" && name != "WorkerW") return true;
                IntPtr view = FindWindowEx(handle, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (view == IntPtr.Zero) return true;
                result = view; return false;
            }, IntPtr.Zero);
            return result;
        }
        internal static Rectangle Fit(Rectangle value)
        {
            Screen screen = Screen.FromRectangle(value);
            Rectangle area = screen.WorkingArea;
            if (value.Width > area.Width || value.Height > area.Height)
            {
                double ratio = Math.Min((double)area.Width / value.Width, (double)area.Height / value.Height);
                value.Size = new Size(Math.Max(1, (int)Math.Round(value.Width * ratio)), Math.Max(1, (int)Math.Round(value.Height * ratio)));
            }
            value.X = Math.Max(area.Left, Math.Min(value.X, area.Right - value.Width));
            value.Y = Math.Max(area.Top, Math.Min(value.Y, area.Bottom - value.Height));
            return value;
        }
    }

    internal sealed class ImageNote : Form
    {
        private readonly Preferences preferences;
        private Image picture;
        private IntPtr desktopView;
        private bool dragging;
        private Point dragPoint;
        private Rectangle dragBounds, resizeWorkingArea;
        private ResizeCorner resizeCorner;
        private const int CornerGrip = 14;
        private readonly System.Windows.Forms.Timer saveTimer;
        internal event EventHandler Changed;
        internal Action SaveRequested;
        internal event Action<string[]> FilesDropped;
        internal IntPtr DesktopView { get { return desktopView; } }
        internal bool Attached { get { return IsHandleCreated && desktopView != IntPtr.Zero && Desktop.GetParent(Handle) == desktopView; } }
        internal Size OriginalSize { get { return picture.Size; } }
        internal bool Locked
        {
            get { return preferences.Locked; }
            set
            {
                if (value && dragging) FinishPointerGesture(false);
                preferences.Locked = value; Cursor = value ? Cursors.Default : Cursors.SizeAll; NotifyChanged();
            }
        }
        internal Rectangle ScreenRectangle
        {
            get
            {
                Desktop.RECT rectangle;
                Desktop.GetWindowRect(Handle, out rectangle);
                return Rectangle.FromLTRB(rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Bottom);
            }
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get { CreateParams value = base.CreateParams; value.ExStyle |= 0x00000080 | 0x08000000; return value; }
        }
        internal ImageNote(Preferences value)
        {
            preferences = value;
            Text = "Yourself Copilot · 桌面图片";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;
            BackColor = Color.White;
            Cursor = value.Locked ? Cursors.Default : Cursors.SizeAll;
            AccessibleName = "Yourself Copilot 桌面图片，拖动移动，四角或滚轮缩放，右键设置";
            AllowDrop = true;
            DragEnter += delegate(object sender, DragEventArgs e) { e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; };
            DragDrop += delegate(object sender, DragEventArgs e) { if (e.Data.GetDataPresent(DataFormats.FileDrop) && FilesDropped != null) FilesDropped((string[])e.Data.GetData(DataFormats.FileDrop)); };
            LoadPicture(value.ImagePath);
            saveTimer = new System.Windows.Forms.Timer();
            saveTimer.Interval = 350;
            saveTimer.Tick += delegate { saveTimer.Stop(); Save(); };
        }
        internal void LoadPicture(string path)
        {
            string actual = String.IsNullOrEmpty(path) ? Path.Combine(Storage.Folder, "计划表.png") : path;
            if (!Path.IsPathRooted(actual)) actual = Path.Combine(Storage.Folder, actual);
            if (!File.Exists(actual)) throw new FileNotFoundException("这张图片暂时找不到，请在图片管理器里更换图片。", actual);
            Image next;
            using (var stream = File.OpenRead(actual))
            using (var original = Image.FromStream(stream)) next = new Bitmap(original);
            if (picture != null) picture.Dispose();
            picture = next;
            if (String.IsNullOrEmpty(preferences.ImagePath)) preferences.ImagePath = actual;
            Invalidate();
        }
        internal void Attach()
        {
            desktopView = Desktop.FindView();
            if (desktopView == IntPtr.Zero) throw new InvalidOperationException("暂时无法连接 Windows 桌面，请稍后重新打开。");
            long style = Desktop.GetWindowLongPtr(Handle, Desktop.GWL_STYLE).ToInt64();
            Desktop.SetWindowLongPtr(Handle, Desktop.GWL_STYLE, new IntPtr((style | Desktop.WS_CHILD) & ~Desktop.WS_POPUP));
            Desktop.SetParent(Handle, desktopView);
            if (Desktop.GetParent(Handle) != desktopView) throw new InvalidOperationException("连接桌面层失败，错误码 " + Marshal.GetLastWin32Error());
            Rectangle bounds;
            if (preferences.Positioned && preferences.Width > 0)
                bounds = new Rectangle(preferences.X, preferences.Y, preferences.Width, HeightFor(preferences.Width));
            else bounds = DefaultRectangle();
            Place(bounds, false);
        }
        internal void ShowNote()
        {
            if (!IsHandleCreated) { var ignored = Handle; }
            if (!Attached) Attach();
            Show();
            Desktop.ShowWindow(Handle, 4);
            Place(ScreenRectangle, false);
        }
        internal int HeightFor(int width) { return Math.Max(1, (int)Math.Round((double)width * picture.Height / picture.Width)); }
        internal Rectangle DefaultRectangle()
        {
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            int width = Math.Min(picture.Width, Math.Min(804, Math.Max(160, area.Width - 64)));
            int maximumHeight = Math.Max(120, (int)(area.Height * 0.72));
            if (HeightFor(width) > maximumHeight) width = Math.Max(40, (int)Math.Round((double)maximumHeight * picture.Width / picture.Height));
            return Desktop.Fit(new Rectangle(area.Right - width - 36, area.Top + 100, width, HeightFor(width)));
        }
        internal void ResetPosition() { Place(DefaultRectangle(), true); }
        internal void OriginalScale()
        {
            Rectangle bounds = ScreenRectangle;
            Place(new Rectangle(bounds.Location, picture.Size), true);
        }
        internal void SetScale(double percentage)
        {
            if (Locked) return;
            Rectangle bounds = ScreenRectangle;
            int width = Math.Max(40, (int)Math.Round(picture.Width * percentage / 100));
            Place(new Rectangle(bounds.X, bounds.Y, width, HeightFor(width)), true);
        }
        internal void Reposition()
        {
            Rectangle bounds = ScreenRectangle;
            Rectangle area = Screen.FromRectangle(bounds).WorkingArea;
            Place(new Rectangle(area.Right - bounds.Width - 36, area.Top + 60, bounds.Width, bounds.Height), true);
        }
        internal void Zoom(double factor)
        {
            if (Locked) return;
            Rectangle bounds = ScreenRectangle;
            int minimum = Math.Min(320, picture.Width);
            int maximum = Math.Max(minimum, Math.Min(2400, Screen.FromRectangle(bounds).WorkingArea.Width));
            int width = Math.Max(minimum, Math.Min(maximum, (int)Math.Round(bounds.Width * factor)));
            Place(new Rectangle(bounds.X, bounds.Y, width, HeightFor(width)), true);
        }
        internal void Place(Rectangle bounds, bool persist)
        {
            bounds = Desktop.Fit(bounds);
            var point = new Desktop.POINT(bounds.X, bounds.Y);
            Desktop.ScreenToClient(desktopView, ref point);
            if (!Desktop.SetWindowPos(Handle, IntPtr.Zero, point.X, point.Y, bounds.Width, bounds.Height,
                Desktop.SWP_NOACTIVATE | Desktop.SWP_FRAMECHANGED))
                throw new InvalidOperationException("调整贴图位置失败，错误码 " + Marshal.GetLastWin32Error());
            preferences.X = bounds.X; preferences.Y = bounds.Y; preferences.Width = bounds.Width; preferences.Positioned = true;
            Invalidate();
            if (persist) NotifyChanged();
        }
        private void NotifyChanged()
        {
            saveTimer.Stop(); saveTimer.Start();
            if (Changed != null) Changed(this, EventArgs.Empty);
        }
        internal void Save()
        {
            try { if (SaveRequested != null) SaveRequested(); else Storage.Save(preferences); }
            catch (Exception e) { Storage.Log("保存位置失败：" + e.Message); }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            e.Graphics.DrawImage(picture, ClientRectangle);
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || Locked) return;
            // The cursor may already be farther along when a queued button-down
            // is processed. Anchor to that event so fast drags keep their delta.
            dragging = true; dragPoint = PointToScreen(e.Location); dragBounds = ScreenRectangle;
            resizeWorkingArea = Screen.FromRectangle(dragBounds).WorkingArea;
            resizeCorner = CornerResize.HitTest(e.Location, ClientSize, CornerGrip);
            Capture = true; UpdatePointerCursor(e.Location);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            UpdatePointerCursor(e.Location);
            if (!dragging || Locked) return;
            Point current = MousePosition;
            var delta = new Point(current.X - dragPoint.X, current.Y - dragPoint.Y);
            if (resizeCorner != ResizeCorner.None)
            {
                // Existing tiny custom scales remain stable when grabbing a corner.
                int minimumWidth = Math.Min(dragBounds.Width, Math.Min(160, picture.Width));
                Place(CornerResize.Bounds(dragBounds, delta, resizeCorner, new Size(minimumWidth, HeightFor(minimumWidth)), resizeWorkingArea,
                    (double)picture.Width / picture.Height), false);
            }
            else
            {
                Size size = ScreenRectangle.Size;
                Place(new Rectangle(dragBounds.X + delta.X, dragBounds.Y + delta.Y, size.Width, size.Height), false);
            }
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left) FinishPointerGesture(true);
            UpdatePointerCursor(e.Location);
        }
        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (!Capture) FinishPointerGesture(true);
        }
        private void FinishPointerGesture(bool persist)
        {
            if (!dragging) return;
            dragging = false; resizeCorner = ResizeCorner.None;
            if (Capture) Capture = false;
            if (persist) NotifyChanged();
        }
        private void UpdatePointerCursor(Point local)
        {
            ResizeCorner corner = dragging ? resizeCorner : CornerResize.HitTest(local, ClientSize, CornerGrip);
            Cursor = Locked ? Cursors.Default : corner == ResizeCorner.TopLeft || corner == ResizeCorner.BottomRight ? Cursors.SizeNWSE
                : corner == ResizeCorner.TopRight || corner == ResizeCorner.BottomLeft ? Cursors.SizeNESW : Cursors.SizeAll;
        }
        protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); Zoom(e.Delta > 0 ? 1.06 : 1.0 / 1.06); }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x21) { message.Result = new IntPtr(3); return; }
            base.WndProc(ref message);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { FinishPointerGesture(false); if (saveTimer != null) saveTimer.Dispose(); if (picture != null) picture.Dispose(); }
            base.Dispose(disposing);
        }
    }

    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            try { Desktop.SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch (EntryPointNotFoundException) { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length == 1 && args[0] == "--self-test") return SelfTest();
            if (args.Length == 1 && args[0] == "--verify-library") return LibraryVerification.Run();
            if (args.Length == 1 && args[0] == "--smoke-test") return LibraryVerification.Smoke();
            if (args.Length == 1 && args[0] == "--verify-workboard") return WorkboardVerification.Run();
            bool startup = args.Length == 1 && args[0] == "--startup";
            bool created;
            using (var single = new Mutex(true, "Local\\LocalDesktopNote_01a14f34", out created))
            using (var request = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\LocalDesktopNote_Show_01a14f34"))
            {
                if (!created) { if (!startup) { request.Set(); Storage.Log("已通知现有实例打开工作台"); } return 0; }
                try { var context = new NoteContext(request, startup); if (!startup) context.ShowWorkbench(); Application.Run(context); return 0; }
                catch (Exception e)
                {
                    Storage.Log(e.ToString());
                    if (!startup) MessageBox.Show("Yourself Copilot 未能启动：\n" + e.Message, "Yourself Copilot", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 1;
                }
            }
        }
        private static void Check(bool condition, string name, StringBuilder output)
        {
            if (!condition) throw new InvalidOperationException("FAIL: " + name);
            output.AppendLine("PASS: " + name);
        }
        private static int SelfTest()
        {
            var result = new StringBuilder();
            string originalSettings = File.Exists(Storage.SettingsFile) ? File.ReadAllText(Storage.SettingsFile, Encoding.UTF8) : null;
            try
            {
                string wallpaper = Desktop.Wallpaper();
                var preferences = new Preferences();
                using (var image = new ImageNote(preferences))
                {
                    image.Attach(); image.ShowNote(); Application.DoEvents();
                    Check(image.Attached && Desktop.ClassName(image.DesktopView) == "SHELLDLL_DefView", "attached to desktop shell view", result);
                    Check((Desktop.GetWindowLongPtr(image.Handle, Desktop.GWL_STYLE).ToInt64() & Desktop.WS_CHILD) != 0, "native desktop child window", result);
                    Check((Desktop.GetWindowLongPtr(image.Handle, Desktop.GWL_EXSTYLE).ToInt64() & Desktop.WS_EX_TOPMOST) == 0, "does not float above ordinary apps", result);
                    Check(Desktop.IsWindowVisible(image.Handle), "image window visible", result);
                    Rectangle before = image.ScreenRectangle;
                    image.Zoom(0.85); Application.DoEvents();
                    Check(image.ScreenRectangle.Width < before.Width, "resize changes actual native window", result);
                    double expected = (double)image.OriginalSize.Width / image.OriginalSize.Height;
                    Rectangle scaled = image.ScreenRectangle;
                    Check(Math.Abs((double)scaled.Width / scaled.Height - expected) < 0.01, "aspect ratio preserved", result);
                    image.Locked = true; image.Zoom(0.8);
                    Check(image.ScreenRectangle == scaled, "lock prevents resizing", result);
                    image.Locked = false;
                    image.Place(new Rectangle(100000, 100000, 700, image.HeightFor(700)), false);
                    Check(Screen.FromRectangle(image.ScreenRectangle).WorkingArea.Contains(image.ScreenRectangle), "offscreen position recovered", result);
                    image.Save(); var saved = Storage.Load();
                    Check(saved.X == preferences.X && saved.Width == preferences.Width && saved.Positioned, "position settings round trip", result);
                    image.ResetPosition();
                    Check(image.ScreenRectangle == image.DefaultRectangle(), "default position restored", result);
                    image.Hide(); Check(!Desktop.IsWindowVisible(image.Handle), "hide works", result);
                    image.ShowNote(); Check(Desktop.IsWindowVisible(image.Handle), "show restores desktop image", result);
                }
                Check(wallpaper == Desktop.Wallpaper(), "wallpaper setting unchanged", result);
                result.AppendLine("Completed " + DateTime.Now.ToString("s"));
                File.WriteAllText(Path.Combine(Storage.Folder, "verification.txt"), result.ToString(), Encoding.UTF8);
                return 0;
            }
            catch (Exception e)
            {
                result.AppendLine(e.ToString());
                File.WriteAllText(Path.Combine(Storage.Folder, "verification.txt"), result.ToString(), Encoding.UTF8);
                return 1;
            }
            finally
            {
                if (originalSettings != null) File.WriteAllText(Storage.SettingsFile, originalSettings, Encoding.UTF8);
                else if (File.Exists(Storage.SettingsFile)) File.Delete(Storage.SettingsFile);
            }
        }
    }
}
