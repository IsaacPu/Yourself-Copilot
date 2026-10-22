using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Forms = System.Windows.Forms;
using Point = System.Drawing.Point;
using WpfPoint = System.Windows.Point;
using WpfSize = System.Windows.Size;

namespace LocalDesktopNote
{
    internal static class EdgeResizeVerification
    {
        private static readonly StringBuilder Report = new StringBuilder();
        private static int passed, failed;
        private static readonly string[] Edges = { "Left", "Top", "Right", "Bottom" };
        private static readonly Rectangle Start = new Rectangle(400, 300, 450, 340), Area = new Rectangle(0, 0, 1600, 1000);
        private static readonly System.Drawing.Size Minimum = new System.Drawing.Size(350, 290);
        private static void Check(bool value, string description)
        {
            Report.AppendLine((value ? "PASS: " : "FAIL: ") + description);
            if (value) passed++; else failed++;
        }
        private static bool Edge(string name, out ResizeCorner edge)
        {
            edge = ResizeCorner.None;
            return Enum.IsDefined(typeof(ResizeCorner), name) && Enum.TryParse<ResizeCorner>(name, out edge);
        }
        private static bool SingleAxis(Rectangle before, Rectangle after, string edge)
        {
            if (edge == "Left") return after.Right == before.Right && after.Top == before.Top && after.Height == before.Height;
            if (edge == "Right") return after.Left == before.Left && after.Top == before.Top && after.Height == before.Height;
            if (edge == "Top") return after.Bottom == before.Bottom && after.Left == before.Left && after.Width == before.Width;
            return after.Top == before.Top && after.Left == before.Left && after.Width == before.Width;
        }
        private static Point Delta(string edge, int amount, int perpendicular)
        {
            if (edge == "Left") return new Point(-amount, perpendicular);
            if (edge == "Right") return new Point(amount, perpendicular);
            if (edge == "Top") return new Point(perpendicular, -amount);
            return new Point(perpendicular, amount);
        }
        private static void Geometry()
        {
            foreach (string name in Edges)
            {
                ResizeCorner edge;
                Check(Edge(name, out edge), name + " is available as a single-axis resize direction");
                if (edge == ResizeCorner.None) continue;
                Rectangle grown = CornerResize.Bounds(Start, Delta(name, 90, 175), edge, Minimum, Area);
                bool horizontal = name == "Left" || name == "Right";
                Check(SingleAxis(Start, grown, name) && (horizontal ? grown.Width == 540 : grown.Height == 430), name + " grows only its own axis despite diagonal pointer motion");
                Rectangle shrunk = CornerResize.Bounds(Start, Delta(name, -30, -175), edge, Minimum, Area);
                Check(SingleAxis(Start, shrunk, name) && (horizontal ? shrunk.Width == 420 : shrunk.Height == 310), name + " shrinks only its own axis and fixes the opposite edge");
                Rectangle tiny = CornerResize.Bounds(Start, Delta(name, -10000, 10000), edge, Minimum, Area);
                Check(SingleAxis(Start, tiny, name) && (horizontal ? tiny.Width == 350 : tiny.Height == 290), name + " clamps at minimum without moving the perpendicular axis");
                Rectangle large = CornerResize.Bounds(Start, Delta(name, 10000, -10000), edge, Minimum, Area);
                Check(Area.Contains(large) && SingleAxis(Start, large, name), name + " stops at the screen boundary with its opposite edge fixed");
                Rectangle negative = new Rectangle(-1400, 100, 450, 340), negativeArea = new Rectangle(-1920, -100, 1920, 1080);
                Rectangle negativeLarge = CornerResize.Bounds(negative, Delta(name, 10000, 10000), edge, Minimum, negativeArea);
                Check(negativeArea.Contains(negativeLarge) && SingleAxis(negative, negativeLarge, name), name + " preserves its axis on a negative-coordinate monitor");
                Check(CornerResize.Bounds(Start, Point.Empty, edge, Minimum, Area) == Start, name + " does not jump on a zero-movement gesture");
            }
            var corners = new[] { ResizeCorner.TopLeft, ResizeCorner.TopRight, ResizeCorner.BottomLeft, ResizeCorner.BottomRight };
            bool proportional = true;
            foreach (ResizeCorner corner in corners)
            {
                Rectangle image = CornerResize.Bounds(new Rectangle(300, 200, 400, 200), new Point(90, 60), corner, new System.Drawing.Size(160, 80), Area, 2);
                proportional &= Math.Abs(image.Width / 2.0 - image.Height) <= 0.5;
            }
            Check(proportional, "existing image corner resizing still preserves the original aspect ratio");
            Check(CornerResize.HitTest(new Point(225, 0), new System.Drawing.Size(450, 340), 16) == ResizeCorner.None
                && CornerResize.HitTest(new Point(0, 170), new System.Drawing.Size(450, 340), 16) == ResizeCorner.None,
                "image hit testing still offers four corners and keeps its straight edges available for moving");
        }
        private static T Field<T>(object target, string name) { return (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target); }
        private static bool IsWithin(DependencyObject child, DependencyObject parent)
        {
            while (child != null) { if (child == parent) return true; child = VisualTreeHelper.GetParent(child); }
            return false;
        }
        private static DependencyObject VisualHit(Visual root, WpfPoint point)
        {
            DependencyObject hit = null;
            VisualTreeHelper.HitTest(root,
                delegate(DependencyObject target) { var element = target as UIElement; return element != null && !element.IsHitTestVisible ? HitTestFilterBehavior.ContinueSkipSelfAndChildren : HitTestFilterBehavior.Continue; },
                delegate(HitTestResult result) { hit = result.VisualHit; return HitTestResultBehavior.Stop; }, new PointHitTestParameters(point));
            return hit;
        }
        private static void Controls()
        {
            var store = new WorkboardStore(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "edge-fixture"));
            using (var card = new WorkboardWindow(store))
            {
                typeof(WorkboardWindow).GetField("editing", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(card, true);
                Rectangle area = Forms.Screen.PrimaryScreen.WorkingArea;
                Rectangle initial = new Rectangle(area.Left + 180, area.Top + 120, 450, 340);
                card.Place(initial, false);
                card.View.Measure(new WpfSize(450, 340)); card.View.Arrange(new Rect(0, 0, 450, 340)); card.View.UpdateLayout();
                Check(card.View.FindName("CardResizeGrip") == null, "the duplicate inset bottom-right grip is removed");
                string[] corners = { "TopLeft", "TopRight", "BottomLeft", "BottomRight" };
                WpfPoint[] cornerTargets = { new WpfPoint(3, 3), new WpfPoint(447, 3), new WpfPoint(3, 337), new WpfPoint(447, 337) };
                for (int i = 0; i < corners.Length; i++)
                {
                    var handle = card.View.FindName("Corner_" + corners[i]) as FrameworkElement;
                    Check(handle != null && IsWithin(VisualHit(card.View, cornerTargets[i]), handle), corners[i] + " remains the only resize hit target at its corner");
                }
                WpfPoint[] edgeTargets = { new WpfPoint(3, 170), new WpfPoint(225, 3), new WpfPoint(447, 170), new WpfPoint(225, 337) };
                for (int i = 0; i < Edges.Length; i++)
                {
                    string name = Edges[i]; var handle = card.View.FindName("Edge_" + name) as FrameworkElement;
                    Check(handle != null && IsWithin(VisualHit(card.View, edgeTargets[i]), handle), name + " edge has a real thin hit target");
                    if (handle == null) continue;
                    Check(handle.Cursor == (name == "Left" || name == "Right" ? Cursors.SizeWE : Cursors.SizeNS), name + " edge uses its standard single-axis cursor");
                    ResizeCorner edge; Edge(name, out edge);
                    card.Place(initial, false); Rectangle before = card.ScreenRectangle;
                    Point pointer = new Point(500, 400), delta = Delta(name, 35, 120);
                    card.BeginCornerResize(edge, pointer); card.ResizeCornerTo(new Point(pointer.X + delta.X, pointer.Y + delta.Y)); card.EndCornerResize();
                    Rectangle after = card.ScreenRectangle;
                    Check(SingleAxis(before, after, name) && (name == "Left" || name == "Right" ? after.Width == before.Width + 35 : after.Height == before.Height + 35)
                        && store.State.Width == after.Width && store.State.Height == after.Height && !card.Visible && !card.Attached,
                        "real hidden " + name + " edge gesture changes one axis and records resulting geometry");
                }
                WpfPoint inputCenter = card.NewTaskEditor.TranslatePoint(new WpfPoint(card.NewTaskEditor.ActualWidth / 2, card.NewTaskEditor.ActualHeight / 2), card.View);
                Check(IsWithin(VisualHit(card.View, inputCenter), card.NewTaskEditor), "resize targets do not cover the task input");
                var add = card.View.FindName("AddTaskButton") as FrameworkElement;
                WpfPoint addCenter = add.TranslatePoint(new WpfPoint(add.ActualWidth / 2, add.ActualHeight / 2), card.View);
                Check(IsWithin(VisualHit(card.View, addCenter), add), "resize targets do not cover the add task button");
                store.State.Locked = true;
                typeof(WorkboardWindow).GetMethod("UpdateLock", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(card, null);
                bool hidden = true;
                foreach (string name in corners) { var element = card.View.FindName("Corner_" + name) as FrameworkElement; hidden &= element != null && element.Visibility == Visibility.Collapsed; }
                foreach (string name in Edges) { var element = card.View.FindName("Edge_" + name) as FrameworkElement; hidden &= element != null && element.Visibility == Visibility.Collapsed; }
                Check(hidden, "locking hides all eight resize targets");
                Rectangle locked = card.ScreenRectangle; bool blocked = true;
                foreach (string name in Edges) { ResizeCorner edge; if (Edge(name, out edge)) blocked &= !card.BeginCornerResize(edge, Point.Empty); }
                card.ResizeCornerTo(new Point(100, 100)); card.EndCornerResize();
                Check(blocked && card.ScreenRectangle == locked, "locked edge gestures cannot alter geometry");
                store.State.Locked = false;
                typeof(WorkboardWindow).GetMethod("UpdateLock", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(card, null);
                ResizeCorner right; if (Edge("Right", out right)) { card.BeginCornerResize(right, Point.Empty); store.State.Locked = true; card.ResizeCornerTo(new Point(120, 80)); card.EndCornerResize(); }
                Check(card.ScreenRectangle == locked && !Field<bool>(card, "resizing"), "locking during an edge resize cancels its next movement");
                card.Flush(); var reopened = new WorkboardStore(Path.GetDirectoryName(store.PathName));
                Check(reopened.State.Width == store.State.Width && reopened.State.Height == store.State.Height && reopened.State.X == store.State.X && reopened.State.Y == store.State.Y,
                    "flush and reopen retain edge-resized geometry in isolated fixture data");
            }
        }
        [STAThread]
        private static int Main()
        {
            try
            {
                Geometry(); Forms.Application.EnableVisualStyles(); Forms.Application.SetCompatibleTextRenderingDefault(false); Controls();
            }
            catch (Exception error) { failed++; Report.AppendLine("FAIL: " + error); }
            finally
            {
                Report.AppendLine("Passed: " + passed + "; Failed: " + failed);
                Report.AppendLine("LIMIT: isolated fixture only; no Show, Attach, actual cursor movement, foreground activation, registry, or production JSON.");
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "verification-edge-resize.txt"), Report.ToString(), new UTF8Encoding(false));
            }
            return failed == 0 ? 0 : 1;
        }
    }
}
