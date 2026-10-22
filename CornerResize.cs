using System;
using System.Drawing;

namespace LocalDesktopNote
{
    internal enum ResizeCorner { None, TopLeft, TopRight, BottomLeft, BottomRight, Left, Top, Right, Bottom }

    internal static class CornerResize
    {
        internal static ResizeCorner HitTest(Point local, Size size, int grip)
        {
            if (size.Width <= 0 || size.Height <= 0 || grip <= 0 || local.X < 0 || local.Y < 0 || local.X >= size.Width || local.Y >= size.Height)
                return ResizeCorner.None;
            bool left = local.X < grip, right = local.X >= size.Width - grip;
            bool top = local.Y < grip, bottom = local.Y >= size.Height - grip;
            if ((!left && !right) || (!top && !bottom)) return ResizeCorner.None;
            // Very small images can have overlapping corner targets; choose the nearest.
            if (left && right) { left = local.X < size.Width / 2.0; right = !left; }
            if (top && bottom) { top = local.Y < size.Height / 2.0; bottom = !top; }
            return top ? (left ? ResizeCorner.TopLeft : ResizeCorner.TopRight)
                : (left ? ResizeCorner.BottomLeft : ResizeCorner.BottomRight);
        }

        internal static Rectangle Bounds(Rectangle start, Point delta, ResizeCorner corner, Size minimum, Rectangle workingArea, double aspectRatio = 0)
        {
            if (corner == ResizeCorner.None || start.Width <= 0 || start.Height <= 0 || workingArea.Width <= 0 || workingArea.Height <= 0)
                return start;
            // A straight-edge drag only changes that edge's axis. Keep the
            // perpendicular position and size exact even for diagonal movement.
            if (corner == ResizeCorner.Left || corner == ResizeCorner.Right)
            {
                bool fromLeft = corner == ResizeCorner.Left;
                int anchor = fromLeft ? start.Right : start.Left;
                int maximum = Math.Max(1, fromLeft ? anchor - workingArea.Left : workingArea.Right - anchor);
                int lower = Math.Min(maximum, Math.Max(1, minimum.Width));
                int edgeWidth = (int)Math.Max(lower, Math.Min(maximum, start.Width + (fromLeft ? -(double)delta.X : delta.X)));
                return new Rectangle(fromLeft ? anchor - edgeWidth : anchor, start.Top, edgeWidth, start.Height);
            }
            if (corner == ResizeCorner.Top || corner == ResizeCorner.Bottom)
            {
                bool fromTop = corner == ResizeCorner.Top;
                int anchor = fromTop ? start.Bottom : start.Top;
                int maximum = Math.Max(1, fromTop ? anchor - workingArea.Top : workingArea.Bottom - anchor);
                int lower = Math.Min(maximum, Math.Max(1, minimum.Height));
                int edgeHeight = (int)Math.Max(lower, Math.Min(maximum, start.Height + (fromTop ? -(double)delta.Y : delta.Y)));
                return new Rectangle(start.Left, fromTop ? anchor - edgeHeight : anchor, start.Width, edgeHeight);
            }
            bool left = corner == ResizeCorner.TopLeft || corner == ResizeCorner.BottomLeft;
            bool top = corner == ResizeCorner.TopLeft || corner == ResizeCorner.TopRight;
            int anchorX = left ? start.Right : start.Left, anchorY = top ? start.Bottom : start.Top;
            int maximumWidth = Math.Max(1, left ? anchorX - workingArea.Left : workingArea.Right - anchorX);
            int maximumHeight = Math.Max(1, top ? anchorY - workingArea.Top : workingArea.Bottom - anchorY);
            double wantedWidth = start.Width + (left ? -(double)delta.X : delta.X);
            double wantedHeight = start.Height + (top ? -(double)delta.Y : delta.Y);
            int width, height;
            if (aspectRatio > 0 && !Double.IsNaN(aspectRatio) && !Double.IsInfinity(aspectRatio))
            {
                double widthChange = Math.Abs((double)delta.X) / start.Width;
                double heightChange = Math.Abs((double)delta.Y) / start.Height;
                double wanted = widthChange >= heightChange ? wantedWidth : wantedHeight * aspectRatio;
                // Account for integer height rounding so a fitted starting size does
                // not shrink a pixel merely because it already touches the screen.
                int maximum = (int)Math.Max(1, Math.Min(maximumWidth, Math.Floor((maximumHeight + 0.5) * aspectRatio)));
                if (maximum > 1 && Math.Round(maximum / aspectRatio) > maximumHeight) maximum--;
                int minimumHeight = Math.Max(1, minimum.Height);
                double requestedMinimum = Math.Max(Math.Max(1, minimum.Width), Math.Ceiling((minimumHeight - 0.5) * aspectRatio));
                int lower = (int)Math.Min(maximum, requestedMinimum);
                if (lower < maximum && Math.Round(lower / aspectRatio) < minimumHeight) lower++;
                width = (int)Math.Max(lower, Math.Min(maximum, Math.Round(wanted)));
                height = (int)Math.Max(1, Math.Min(maximumHeight, Math.Round(width / aspectRatio)));
            }
            else
            {
                int lowerWidth = Math.Min(maximumWidth, Math.Max(1, minimum.Width));
                int lowerHeight = Math.Min(maximumHeight, Math.Max(1, minimum.Height));
                width = (int)Math.Max(lowerWidth, Math.Min(maximumWidth, Math.Round(wantedWidth)));
                height = (int)Math.Max(lowerHeight, Math.Min(maximumHeight, Math.Round(wantedHeight)));
            }
            return new Rectangle(left ? anchorX - width : anchorX, top ? anchorY - height : anchorY, width, height);
        }
    }
}
