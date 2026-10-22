using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace LocalDesktopNote
{
    internal static class NativeFlowVerification
    {
        private static void Check(bool value, string message, StringBuilder report)
        {
            if (!value) throw new InvalidOperationException("FAIL: " + message);
            report.AppendLine("PASS: " + message);
        }
        [STAThread]
        private static int Main()
        {
            var report = new StringBuilder(); var windows = new List<ImageNote>();
            try
            {
                try { Desktop.SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch (EntryPointNotFoundException) { }
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                string wallpaper = Desktop.Wallpaper();
                var store = new LibraryStore(Storage.Folder);
                var first = store.Import(Path.Combine(Storage.Folder, "计划表.png"));
                string fixture = Path.Combine(Storage.Folder, "flow-fixture.png");
                using (var bitmap = new Bitmap(80, 48)) { using (var graphics = Graphics.FromImage(bitmap)) graphics.Clear(Color.DarkSeaGreen); bitmap.Save(fixture, System.Drawing.Imaging.ImageFormat.Png); }
                var second = store.Import(fixture);
                foreach (var item in store.State.Items)
                {
                    var note = new ImageNote(item); windows.Add(note); note.SaveRequested = store.Save;
                    note.Attach(); note.ShowNote();
                }
                Check(windows.Count == 2 && windows.All(w => w.Attached), "two images attach independently to desktop", report);
                Check(windows.All(w => (Desktop.GetWindowLongPtr(w.Handle, Desktop.GWL_EXSTYLE).ToInt64() & Desktop.WS_EX_TOPMOST) == 0), "both images stay below ordinary apps", report);
                var original = windows[0]; var other = windows[1]; Rectangle otherBounds = other.ScreenRectangle;
                original.SetScale(80);
                Check(Math.Abs(original.ScreenRectangle.Width - first.PixelWidth * 0.8) <= 1, "scale setting changes the native image", report);
                Check(other.ScreenRectangle == otherBounds, "resizing one image does not change another", report);
                original.Locked = true; Rectangle locked = original.ScreenRectangle; original.SetScale(50);
                Check(original.ScreenRectangle == locked, "lock blocks the manager scale command", report);
                first.Hidden = true; original.Hide(); store.Save();
                Check(!Desktop.IsWindowVisible(original.Handle) && Desktop.IsWindowVisible(other.Handle), "hide is independent per image", report);
                Check(new LibraryStore(Storage.Folder).State.Items.First(p => p.Id == first.Id).Hidden, "hidden state is persisted", report);
                original.ShowNote(); first.Hidden = false; original.Locked = false;
                Check(Desktop.IsWindowVisible(original.Handle), "show restores the desktop image", report);
                int before = first.Width; Rectangle oldBounds = original.ScreenRectangle;
                store.Replace(first, fixture); original.LoadPicture(first.ImagePath);
                original.Place(new Rectangle(oldBounds.X, oldBounds.Y, oldBounds.Width, original.HeightFor(oldBounds.Width)), false);
                Check(first.Width == before && first.X == oldBounds.X && first.Y == oldBounds.Y, "replacement preserves position and display width when it fits", report);
                string portrait = Path.Combine(Storage.Folder, "portrait-fixture.png");
                using (var bitmap = new Bitmap(32, 200)) { using (var graphics = Graphics.FromImage(bitmap)) graphics.Clear(Color.FromArgb(147, 168, 139)); bitmap.Save(portrait, System.Drawing.Imaging.ImageFormat.Png); }
                oldBounds = original.ScreenRectangle;
                store.Replace(first, portrait); original.LoadPicture(first.ImagePath);
                original.Place(new Rectangle(oldBounds.X, oldBounds.Y, oldBounds.Width, original.HeightFor(oldBounds.Width)), false);
                Check(Screen.FromRectangle(original.ScreenRectangle).WorkingArea.Contains(original.ScreenRectangle), "portrait replacement fits entirely on screen", report);
                Check(Math.Abs((double)original.ScreenRectangle.Width / original.ScreenRectangle.Height - 32.0 / 200) < 0.01, "portrait replacement keeps original aspect ratio", report);
                int restoredWidth = first.Width;
                original.Dispose(); windows.Remove(original); store.Archive(first); store.Save();
                Check(store.State.Items.Count == 1 && File.Exists(store.Resolve(first)), "removal retains the stored image", report);
                var restored = store.Undo(); var restoredNote = new ImageNote(restored); windows.Add(restoredNote);
                restoredNote.SaveRequested = store.Save; restoredNote.Attach(); restoredNote.ShowNote(); store.Save();
                Check(restoredNote.Attached && restored.Width == restoredWidth && store.State.Items.Count == 2, "undo restores an actual desktop image", report);
                var saved = new LibraryStore(Storage.Folder);
                foreach (var window in windows) window.Dispose(); windows.Clear();
                foreach (var item in saved.State.Items)
                {
                    var window = new ImageNote(item); windows.Add(window); window.SaveRequested = saved.Save;
                    window.Attach(); if (!item.Hidden) window.ShowNote();
                }
                Check(windows.Count == 2 && windows.All(w => w.Attached), "saved library recreates multiple native images", report);
                Check(wallpaper == Desktop.Wallpaper(), "full flow leaves wallpaper unchanged", report);
                File.WriteAllText(Path.Combine(Storage.Folder, "verification-flow.txt"), report.ToString(), Encoding.UTF8); return 0;
            }
            catch (Exception error) { report.AppendLine(error.ToString()); File.WriteAllText(Path.Combine(Storage.Folder, "verification-flow.txt"), report.ToString(), Encoding.UTF8); return 1; }
            finally { foreach (var window in windows) window.Dispose(); }
        }
    }
}
