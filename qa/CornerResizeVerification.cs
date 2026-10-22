using System;
using System.Drawing;
using System.IO;
using System.Text;
#if !GEOMETRY_ONLY
using System.Reflection;
using Forms = System.Windows.Forms;
#endif

namespace LocalDesktopNote
{
    internal static class CornerResizeVerification
    {
        private static readonly StringBuilder Report = new StringBuilder();
        private static int passed, failed;
        private static readonly ResizeCorner[] Corners = { ResizeCorner.TopLeft, ResizeCorner.TopRight, ResizeCorner.BottomLeft, ResizeCorner.BottomRight };
        private static void Check(bool value, string description)
        {
            Report.AppendLine((value ? "PASS: " : "FAIL: ") + description);
            if (value) passed++; else failed++;
        }
        private static Point Anchor(Rectangle bounds, ResizeCorner corner)
        {
            switch (corner)
            {
                case ResizeCorner.TopLeft: return new Point(bounds.Right, bounds.Bottom);
                case ResizeCorner.TopRight: return new Point(bounds.Left, bounds.Bottom);
                case ResizeCorner.BottomLeft: return new Point(bounds.Right, bounds.Top);
                default: return bounds.Location;
            }
        }
        private static Point Outward(ResizeCorner corner, int x, int y)
        {
            return new Point(corner == ResizeCorner.TopLeft || corner == ResizeCorner.BottomLeft ? -x : x,
                corner == ResizeCorner.TopLeft || corner == ResizeCorner.TopRight ? -y : y);
        }
        private static bool Fits(Rectangle bounds, Rectangle area) { return bounds.Width > 0 && bounds.Height > 0 && area.Contains(bounds); }
        private static bool RatioPreserved(Rectangle bounds, double ratio) { return Math.Abs(bounds.Height - bounds.Width / ratio) <= 0.501; }
        private static void Run(string name, Action check)
        {
            Report.AppendLine("CASE: " + name);
            try { check(); } catch (Exception error) { failed++; Report.AppendLine("FAIL: " + error); }
        }
        private static void HitTargets()
        {
            var size = new Size(450, 340);
            var targets = new[] { new Point(0, 0), new Point(449, 0), new Point(0, 339), new Point(449, 339) };
            for (int i = 0; i < Corners.Length; i++) Check(CornerResize.HitTest(targets[i], size, 16) == Corners[i], "the " + Corners[i] + " visible corner is a resize target");
            bool untouched = true;
            foreach (Point point in new[] { new Point(225, 170), new Point(225, 0), new Point(0, 170), new Point(449, 170), new Point(225, 339), new Point(16, 16), new Point(-1, 0), new Point(450, 340) })
                untouched &= CornerResize.HitTest(point, size, 16) == ResizeCorner.None;
            Check(untouched, "body, straight edges, grip boundary and out-of-bounds points remain outside corner resizing");
            Check(CornerResize.HitTest(new Point(1, 1), new Size(8, 8), 14) == ResizeCorner.TopLeft
                && CornerResize.HitTest(new Point(6, 6), new Size(8, 8), 14) == ResizeCorner.BottomRight,
                "overlapping targets on tiny images choose the nearest actual corner");
        }
        private static void CardGeometry()
        {
            Rectangle start = new Rectangle(400, 300, 450, 340), area = new Rectangle(0, 0, 1600, 1000);
            var minimum = new Size(350, 290);
            var grown = new[] { new Rectangle(280, 210, 570, 430), new Rectangle(400, 210, 570, 430), new Rectangle(280, 300, 570, 430), new Rectangle(400, 300, 570, 430) };
            var shrunk = new[] { new Rectangle(480, 330, 370, 310), new Rectangle(400, 330, 370, 310), new Rectangle(480, 300, 370, 310), new Rectangle(400, 300, 370, 310) };
            for (int i = 0; i < Corners.Length; i++)
            {
                ResizeCorner corner = Corners[i];
                Check(CornerResize.Bounds(start, Outward(corner, 120, 90), corner, minimum, area) == grown[i], corner + " expands while its opposite corner stays fixed");
                Check(CornerResize.Bounds(start, Outward(corner, -80, -30), corner, minimum, area) == shrunk[i], corner + " shrinks independently in width and height without moving its opposite corner");
                Rectangle tiny = CornerResize.Bounds(start, Outward(corner, -10000, -10000), corner, minimum, area);
                Check(tiny.Size == minimum && Anchor(tiny, corner) == Anchor(start, corner), corner + " stops at the card's 350 by 290 minimum without flipping over");
                Rectangle edge = CornerResize.Bounds(start, Outward(corner, 10000, 10000), corner, minimum, area);
                Check(Fits(edge, area) && Anchor(edge, corner) == Anchor(start, corner), corner + " stops inside the working-area edge while keeping its anchor fixed");
            }
            Check(CornerResize.Bounds(start, new Point(200, 200), ResizeCorner.None, minimum, area) == start, "non-corner gestures leave geometry unchanged for the ordinary movement handler");
        }
        private static void NegativeScreen()
        {
            Rectangle area = new Rectangle(-1920, -100, 1920, 1080), start = new Rectangle(-1400, 150, 450, 340);
            bool valid = true;
            foreach (ResizeCorner corner in Corners)
            {
                Rectangle grown = CornerResize.Bounds(start, Outward(corner, 10000, 10000), corner, new Size(350, 290), area);
                Rectangle shrunk = CornerResize.Bounds(start, Outward(corner, -10000, -10000), corner, new Size(350, 290), area);
                valid &= Fits(grown, area) && Fits(shrunk, area) && shrunk.Width == 350 && shrunk.Height == 290
                    && Anchor(grown, corner) == Anchor(start, corner) && Anchor(shrunk, corner) == Anchor(start, corner);
            }
            Check(valid, "all four corners preserve anchors and limits on a monitor with negative X and Y coordinates");
        }
        private static void ImageRatios()
        {
            var dimensions = new[] { new Size(400, 200), new Size(200, 400), new Size(400, 50), new Size(50, 400), new Size(12, 600), new Size(600, 12) };
            Rectangle area = new Rectangle(-1600, -100, 3200, 1200);
            foreach (Size size in dimensions)
            {
                double ratio = (double)size.Width / size.Height;
                Rectangle start = new Rectangle(-400, 100, size.Width, size.Height);
                int minimumWidth = Math.Min(160, size.Width);
                Size minimum = new Size(minimumWidth, Math.Max(1, (int)Math.Round(minimumWidth / ratio)));
                bool grow = true, shrink = true, zero = true;
                foreach (ResizeCorner corner in Corners)
                {
                    Rectangle large = CornerResize.Bounds(start, Outward(corner, 10000, 10000), corner, minimum, area, ratio);
                    Rectangle small = CornerResize.Bounds(start, Outward(corner, -10000, -10000), corner, minimum, area, ratio);
                    grow &= Fits(large, area) && RatioPreserved(large, ratio) && Anchor(large, corner) == Anchor(start, corner);
                    shrink &= Fits(small, area) && RatioPreserved(small, ratio) && Anchor(small, corner) == Anchor(start, corner)
                        && small.Width >= minimum.Width && small.Height >= minimum.Height;
                    zero &= CornerResize.Bounds(start, Point.Empty, corner, minimum, area, ratio) == start;
                }
                Check(grow && shrink && zero, "landscape/portrait ratio " + size.Width + ":" + size.Height + " stays proportional at minimum and screen limits, with no zero-movement jump");
            }
            Rectangle touch = new Rectangle(100, 100, 683, 384), fittedArea = new Rectangle(100, 100, 683, 384);
            Check(CornerResize.Bounds(touch, Point.Empty, ResizeCorner.BottomRight, new Size(160, 90), fittedArea, 16.0 / 9.0) == touch,
                "an already edge-fitted image does not shrink one pixel solely from aspect-ratio rounding");
        }

#if !GEOMETRY_ONLY
        private static T Field<T>(object target, string name) { return (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target); }
        private static void SetField(object target, string name, object value) { target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value); }
        private static void InvokeMouse(ImageNote image, string method, Forms.MouseEventArgs args) { typeof(ImageNote).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(image, new object[] { args }); }
        private static void DriveImage(ImageNote image, ResizeCorner corner, Point delta)
        {
            Rectangle before = image.ScreenRectangle;
            SetField(image, "dragging", true); SetField(image, "resizeCorner", corner); SetField(image, "dragBounds", before);
            SetField(image, "resizeWorkingArea", Forms.Screen.FromRectangle(before).WorkingArea);
            Point physical = Forms.Control.MousePosition; SetField(image, "dragPoint", new Point(physical.X - delta.X, physical.Y - delta.Y));
            // Do not call the unlocked MouseDown branch: that would Capture the real pointer.
            InvokeMouse(image, "OnMouseMove", new Forms.MouseEventArgs(Forms.MouseButtons.Left, 1, 40, 40, 0));
            InvokeMouse(image, "OnMouseUp", new Forms.MouseEventArgs(Forms.MouseButtons.Left, 1, 40, 40, 0));
        }
        private static void ImageIntegration()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "image-fixture.png");
            using (var bitmap = new Bitmap(400, 200)) bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            var preference = new Preferences { ImagePath = path, Width = 400, Positioned = true };
            using (var image = new ImageNote(preference))
            {
                Rectangle area = Forms.Screen.PrimaryScreen.WorkingArea;
                Rectangle initial = new Rectangle(area.Left + 120, area.Top + 100, 400, 200);
                image.Place(initial, false);
                Point eventLocation = new Point(4, 6);
                if (image.PointToScreen(eventLocation) == Forms.Control.MousePosition) eventLocation.X++;
                Point eventOrigin = image.PointToScreen(eventLocation);
                InvokeMouse(image, "OnMouseDown", new Forms.MouseEventArgs(Forms.MouseButtons.Left, 1, eventLocation.X, eventLocation.Y, 0));
                Check(Field<Point>(image, "dragPoint") == eventOrigin, "a queued image button-down anchors to its event position even when the cursor has already moved");
                InvokeMouse(image, "OnMouseUp", new Forms.MouseEventArgs(Forms.MouseButtons.Left, 1, eventLocation.X, eventLocation.Y, 0));
                bool corners = true;
                foreach (ResizeCorner corner in Corners)
                {
                    image.Place(initial, false); Rectangle before = image.ScreenRectangle;
                    DriveImage(image, corner, Outward(corner, 80, 40));
                    Rectangle after = image.ScreenRectangle;
                    corners &= after.Width > before.Width && RatioPreserved(after, 2) && Anchor(after, corner) == Anchor(before, corner)
                        && preference.Width == after.Width && !Field<bool>(image, "dragging") && !image.Capture && !image.Visible && !image.Attached;
                }
                Check(corners, "the real image move/up path resizes all four corners proportionally and updates persisted geometry without Show/Attach or physical pointer movement");
                image.Place(initial, false); Rectangle moveStart = image.ScreenRectangle;
                DriveImage(image, ResizeCorner.None, new Point(35, 24));
                Check(image.ScreenRectangle == new Rectangle(moveStart.X + 35, moveStart.Y + 24, moveStart.Width, moveStart.Height), "ordinary image body movement preserves its size after corner support is added");
                image.Locked = true; Rectangle locked = image.ScreenRectangle;
                InvokeMouse(image, "OnMouseDown", new Forms.MouseEventArgs(Forms.MouseButtons.Left, 1, 0, 0, 0));
                InvokeMouse(image, "OnMouseMove", new Forms.MouseEventArgs(Forms.MouseButtons.Left, 1, 20, 20, 0));
                Check(image.ScreenRectangle == locked && !Field<bool>(image, "dragging") && !image.Capture, "a locked image ignores a real corner MouseDown without acquiring pointer capture");
                image.Locked = false; DriveImage(image, ResizeCorner.BottomRight, new Point(40, 20));
                Check(image.ScreenRectangle.Width > locked.Width, "unlocking the image restores proportional resizing");
                SetField(image, "dragging", true); SetField(image, "resizeCorner", ResizeCorner.BottomRight); image.Locked = true;
                Check(!Field<bool>(image, "dragging") && Field<ResizeCorner>(image, "resizeCorner") == ResizeCorner.None, "locking during an image gesture cancels the resize state");
            }
        }
        private static void CardIntegration()
        {
            var store = new WorkboardStore(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "card-fixture"));
            using (var card = new WorkboardWindow(store))
            {
                MethodInfo begin = typeof(WorkboardWindow).GetMethod("BeginCornerResize", BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo move = typeof(WorkboardWindow).GetMethod("ResizeCornerTo", BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo end = typeof(WorkboardWindow).GetMethod("EndCornerResize", BindingFlags.Instance | BindingFlags.NonPublic);
                Check(begin != null && move != null && end != null, "the real card exposes non-showing corner gesture driver methods");
                if (begin == null || move == null || end == null) return;
                Rectangle area = Forms.Screen.PrimaryScreen.WorkingArea;
                Rectangle initial = new Rectangle(area.Left + 180, area.Top + 120, 450, 340);
                SetField(card, "editing", true); // Screen-coordinate placement only; never BeginEditing, Show or Attach.
                card.Place(initial, false);
                bool handles = true;
                foreach (ResizeCorner corner in Corners)
                {
                    object handle = card.View.FindName("Corner_" + corner) ?? card.View.FindName("CardResize" + corner) ?? card.View.FindName("Resize_" + corner) ?? card.View.FindName("CardResize_" + corner);
                    handles &= handle != null;
                    card.Place(initial, false); Rectangle before = card.ScreenRectangle;
                    Point pointer = new Point(500, 400), delta = Outward(corner, 60, 45);
                    begin.Invoke(card, new object[] { corner, pointer }); move.Invoke(card, new object[] { new Point(pointer.X + delta.X, pointer.Y + delta.Y) }); end.Invoke(card, null);
                    Rectangle after = card.ScreenRectangle;
                    Check(after.Width == before.Width + 60 && after.Height == before.Height + 45 && Anchor(after, corner) == Anchor(before, corner)
                        && !card.Visible && !card.Attached, "real card " + corner + " gesture expands with a fixed opposite corner while remaining hidden and unattached");
                }
                Check(handles, "all four card corner handle controls exist in the real view");
                card.Flush();
                var reopened = new WorkboardStore(Path.GetDirectoryName(store.PathName));
                Check(reopened.State.Width == store.State.Width && reopened.State.Height == store.State.Height && reopened.State.X == store.State.X && reopened.State.Y == store.State.Y,
                    "ending and flushing card resize persists actual position and dimensions");
                store.State.Locked = true; Rectangle locked = card.ScreenRectangle;
                begin.Invoke(card, new object[] { ResizeCorner.BottomRight, Point.Empty }); move.Invoke(card, new object[] { new Point(200, 200) }); end.Invoke(card, null);
                Check(card.ScreenRectangle == locked, "a locked card cannot start or apply a corner resize");
                store.State.Locked = false;
                begin.Invoke(card, new object[] { ResizeCorner.BottomRight, Point.Empty }); move.Invoke(card, new object[] { new Point(40, 30) }); end.Invoke(card, null);
                Check(card.ScreenRectangle.Width == locked.Width + 40 && card.ScreenRectangle.Height == locked.Height + 30, "unlocking the card restores corner resizing without restarting the window");
                Rectangle beforeLock = card.ScreenRectangle;
                begin.Invoke(card, new object[] { ResizeCorner.TopLeft, Point.Empty }); store.State.Locked = true;
                move.Invoke(card, new object[] { new Point(-80, -60) }); end.Invoke(card, null);
                Check(card.ScreenRectangle == beforeLock && !Field<bool>(card, "resizing"), "locking during a card gesture cancels its resize state and prevents the next move");
            }
        }
#endif
        [STAThread]
        private static int Main()
        {
            try
            {
                Run("corner hit targets", HitTargets); Run("card geometry", CardGeometry); Run("negative monitor geometry", NegativeScreen); Run("image aspect ratios", ImageRatios);
#if !GEOMETRY_ONLY
                Forms.Application.EnableVisualStyles(); Forms.Application.SetCompatibleTextRenderingDefault(false);
                Run("real image integration", ImageIntegration); Run("real card integration", CardIntegration);
#endif
                Report.AppendLine("Passed: " + passed + "; Failed: " + failed);
                Report.AppendLine("LIMIT: no Show/Attach, physical pointer movement, focus activation, registry or production JSON. Synthetic image gesture fields bypass native mouse capture; actual pointer targeting remains manual QA.");
                return failed == 0 ? 0 : 1;
            }
            finally { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "verification-corner-resize.txt"), Report.ToString(), new UTF8Encoding(false)); }
        }
    }
}
