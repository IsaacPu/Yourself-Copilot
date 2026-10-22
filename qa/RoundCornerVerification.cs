using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Forms = System.Windows.Forms;
using WpfPoint = System.Windows.Point;
using WpfSize = System.Windows.Size;

namespace LocalDesktopNote
{
    internal static class RoundCornerVerification
    {
        [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
        [DllImport("gdi32.dll")] private static extern bool PtInRegion(IntPtr region, int x, int y);
        [DllImport("user32.dll")] private static extern int GetWindowRgn(IntPtr window, IntPtr region);
        [DllImport("user32.dll")] private static extern int GetGuiResources(IntPtr process, int flags);
        private static readonly StringBuilder Report = new StringBuilder();
        private static int passed, failed;
        private static void Check(bool value, string text)
        {
            Report.AppendLine((value ? "PASS: " : "FAIL: ") + text);
            if (value) passed++; else failed++;
        }
        private static T Field<T>(object target, string name) { return (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target); }
        private static void Refresh(WorkboardWindow card) { typeof(WorkboardWindow).GetMethod("UpdateWindowRegion", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(card, null); }
        private static bool Disposed(Region region)
        {
            try { region.IsVisible(1, 1); return false; }
            catch (ArgumentException) { return true; }
        }
        private static void Ownership()
        {
            var form = new Forms.Form();
            var first = new Region(new Rectangle(0, 0, 20, 20));
            var second = new Region(new Rectangle(0, 0, 30, 30));
            form.Region = first; form.Region = second;
            Check(Disposed(first) && !form.IsHandleCreated, "this .NET Framework Control.Region setter disposes its predecessor without creating a handle");
            form.Dispose();
            Check(!Disposed(second), "this .NET Framework Form.Dispose retains its final Region, so the card needs explicit cleanup");
            form.Region = null;
            Check(Disposed(second) && !form.IsHandleCreated, "clearing Region after Form disposal releases the last Region without creating a handle");
            Report.AppendLine("Runtime: " + Environment.Version + "; WinForms: " + typeof(Forms.Form).Assembly.Location);
        }
        private static Matrix Device(WorkboardWindow card)
        {
            var source = PresentationSource.FromVisual(card.View);
            return source != null && source.CompositionTarget != null ? source.CompositionTarget.TransformToDevice : Matrix.Identity;
        }
        private static void Layout(WorkboardWindow card, double width, double height)
        {
            card.View.Measure(new WpfSize(width, height)); card.View.Arrange(new Rect(0, 0, width, height)); card.View.UpdateLayout();
        }
        private static bool Within(DependencyObject child, DependencyObject parent)
        {
            while (child != null) { if (child == parent) return true; child = VisualTreeHelper.GetParent(child); }
            return false;
        }
        private static DependencyObject Hit(Visual view, WpfPoint point)
        {
            DependencyObject hit = null;
            VisualTreeHelper.HitTest(view,
                delegate(DependencyObject target) { var ui = target as UIElement; return ui != null && !ui.IsHitTestVisible ? HitTestFilterBehavior.ContinueSkipSelfAndChildren : HitTestFilterBehavior.Continue; },
                delegate(HitTestResult result) { hit = result.VisualHit; return HitTestResultBehavior.Stop; }, new PointHitTestParameters(point));
            return hit;
        }
        private static void NativeContour(WorkboardWindow card, string label)
        {
            IntPtr region = CreateRectRgn(0, 0, 0, 0);
            try
            {
                int kind = GetWindowRgn(card.Handle, region), width = card.ClientSize.Width, height = card.ClientSize.Height;
                var device = Device(card); int x = (int)Math.Round(6 * device.M11), y = (int)Math.Round(6 * device.M22);
                Check(kind == 3 && !PtInRegion(region, 0, 0) && !PtInRegion(region, width - 1, 0)
                    && !PtInRegion(region, 0, height - 1) && !PtInRegion(region, width - 1, height - 1), label + " excludes all four outer corner pixels in the real hidden HWND region");
                Check(PtInRegion(region, x, y) && PtInRegion(region, width - 1 - x, y)
                    && PtInRegion(region, x, height - 1 - y) && PtInRegion(region, width - 1 - x, height - 1 - y), label + " retains all four visible corner grips at 6 DIP inside");
                Check(PtInRegion(region, width / 2, 0) && PtInRegion(region, 0, height / 2)
                    && PtInRegion(region, width - 1, height / 2) && PtInRegion(region, width / 2, height - 1), label + " retains the middle of every straight edge");
            }
            finally { DeleteObject(region); }
        }
        private static void Card()
        {
            var store = new WorkboardStore(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "round-fixture"));
            Region finalRegion;
            using (var card = new WorkboardWindow(store))
            {
                Refresh(card);
                Layout(card, 280, 220);
                Check(!card.IsHandleCreated && card.Region == null, "constructing, laying out and refreshing a hidden card never creates its HWND");
                var border = (Border)card.View.Children[0];
                Check(border.CornerRadius == new CornerRadius(14) && ((SolidColorBrush)border.Background).Color == System.Windows.Media.Color.FromRgb(250, 248, 243)
                    && ((SolidColorBrush)card.View.Background).Color.A == 0, "the rounded Border owns the original warm paper fill and the root Grid is transparent");
                card.ClientSize = new System.Drawing.Size(450, 340);
                IntPtr window = card.Handle;
                var host = Field<System.Windows.Forms.Integration.ElementHost>(card, "host");
                IntPtr child = host.Handle;
                Check(card.Region != null && !card.Visible && !card.Attached, "HandleCreated installs a Region while the native card stays hidden and unattached");
                var sizes = new[] { new System.Drawing.Size(450, 340), new System.Drawing.Size(350, 290), new System.Drawing.Size(680, 480) };
                foreach (var size in sizes)
                {
                    Region previous = card.Region;
                    bool changedSize = card.ClientSize != size;
                    card.ClientSize = size; card.PerformLayout();
                    if (changedSize) Check(!Object.ReferenceEquals(previous, card.Region) && Disposed(previous), "ClientSizeChanged replaces and disposes the previous Region at " + size);
                    var device = Device(card);
                    Layout(card, size.Width / device.M11, size.Height / device.M22);
                    NativeContour(card, size.ToString());
                    double scale = Math.Min(1, Math.Min(card.View.ActualWidth / 350.0, card.View.ActualHeight / 290.0));
                    Check(Math.Abs(Field<double>(card, "roundedRadiusX") - 14 * scale * device.M11) < 0.0001
                        && Math.Abs(Field<double>(card, "roundedRadiusY") - 14 * scale * device.M22) < 0.0001, "effective native radius follows the Border, compact layout scale and actual WPF device matrix");
                    Region current = card.Region; Refresh(card); Refresh(card);
                    Check(Object.ReferenceEquals(current, card.Region), "unchanged dimensions and effective radii reuse the same Region");
                }
                Region full = card.Region;
                card.ClientSize = new System.Drawing.Size(280, 220); card.PerformLayout();
                var compactDevice = Device(card);
                Layout(card, 280 / compactDevice.M11, 220 / compactDevice.M22);
                Check(!Object.ReferenceEquals(full, card.Region) && Disposed(full) && Field<double>(card, "roundedRadiusX") < 14 * Device(card).M11,
                    "a WPF SizeChanged event updates the native Region for a compact logical layout");
                NativeContour(card, "compact layout");
                card.ClientSize = new System.Drawing.Size(450, 340); card.PerformLayout();
                var gripDevice = Device(card);
                Layout(card, 450 / gripDevice.M11, 340 / gripDevice.M22);
                double viewWidth = card.View.ActualWidth, viewHeight = card.View.ActualHeight;
                string[] names = { "Corner_TopLeft", "Corner_TopRight", "Corner_BottomLeft", "Corner_BottomRight", "Edge_Left", "Edge_Top", "Edge_Right", "Edge_Bottom" };
                WpfPoint[] points = { new WpfPoint(6, 6), new WpfPoint(viewWidth - 6, 6), new WpfPoint(6, viewHeight - 6), new WpfPoint(viewWidth - 6, viewHeight - 6), new WpfPoint(3, viewHeight / 2), new WpfPoint(viewWidth / 2, 3), new WpfPoint(viewWidth - 3, viewHeight / 2), new WpfPoint(viewWidth / 2, viewHeight - 3) };
                for (int i = 0; i < names.Length; i++)
                {
                    var handle = card.View.FindName(names[i]) as FrameworkElement;
                    bool dimensions = handle != null && (i < 4 ? handle.Width == 14 && handle.Height == 14 : i == 4 || i == 6 ? handle.Width == 6 : handle.Height == 6);
                    Check(dimensions && Within(Hit(card.View, points[i]), handle), names[i] + " keeps its existing dimensions and visible WPF hit target");
                }
                using (var process = System.Diagnostics.Process.GetCurrentProcess())
                {
                    int before = GetGuiResources(process.Handle, 0);
                    for (int i = 0; i < 80; i++) { card.ClientSize = new System.Drawing.Size(450 + i % 2, 340 + i % 2); Refresh(card); }
                    int after = GetGuiResources(process.Handle, 0);
                    Check(after <= before + 2, "repeated native Region replacement does not accumulate GDI handles (" + before + " -> " + after + ")");
                }
                Check(!card.Visible && !card.Attached && card.Handle == window, "all contour and resize-target checks retain the same hidden unattached HWND");
                finalRegion = card.Region;
            }
            Check(Disposed(finalRegion), "disposing WorkboardWindow explicitly releases its last Region");
        }
        [STAThread]
        private static int Main()
        {
            try
            {
                try { Desktop.SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch (EntryPointNotFoundException) { }
                Forms.Application.EnableVisualStyles(); Forms.Application.SetCompatibleTextRenderingDefault(false);
                Ownership(); Card();
            }
            catch (Exception error) { failed++; Report.AppendLine("FAIL: " + error); }
            finally
            {
                Report.AppendLine("Passed: " + passed + "; Failed: " + failed);
                Report.AppendLine("LIMIT: hidden native HWND and synthetic WPF layout only; no Show/Attach, physical input, real monitor DPI transition, foreground activation, registry, production EXE or production JSON.");
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "verification-round-corners.txt"), Report.ToString(), new UTF8Encoding(false));
            }
            return failed == 0 ? 0 : 1;
        }
    }
}
