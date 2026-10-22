using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml;
using Forms = System.Windows.Forms;

namespace LocalDesktopNote
{
    internal static class BrandVerification
    {
        private static readonly StringBuilder Report = new StringBuilder();
        private static int assertions;
        private static readonly int[] Sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
        private static void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException("FAIL: " + message);
            assertions++; Report.AppendLine("PASS: " + message);
        }
        private static byte[] Render(int size)
        {
            FrameworkElement image = AppVisuals.BrandIcon(size);
            image.Measure(new Size(size, size)); image.Arrange(new Rect(0, 0, size, size)); image.UpdateLayout();
            var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32); bitmap.Render(image);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = new MemoryStream()) { encoder.Save(stream); return stream.ToArray(); }
        }
        private static void CreateIcon(string destination)
        {
            var frames = new List<byte[]>(); foreach (int size in Sizes) frames.Add(Render(size));
            using (var writer = new BinaryWriter(File.Create(destination)))
            {
                writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)Sizes.Length);
                int offset = 6 + 16 * Sizes.Length;
                for (int i = 0; i < Sizes.Length; i++)
                {
                    writer.Write((byte)(Sizes[i] == 256 ? 0 : Sizes[i])); writer.Write((byte)(Sizes[i] == 256 ? 0 : Sizes[i]));
                    writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)1); writer.Write((ushort)32);
                    writer.Write(frames[i].Length); writer.Write(offset); offset += frames[i].Length;
                }
                foreach (byte[] frame in frames) writer.Write(frame);
            }
        }
        private static void CheckPng(byte[] png, int size)
        {
            BitmapFrame source;
            using (var stream = new MemoryStream(png)) source = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            Check(source.PixelWidth == size && source.PixelHeight == size, size + "px frame has exact PNG dimensions");
            var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            byte[] pixels = new byte[size * size * 4]; converted.CopyPixels(pixels, size * 4, 0);
            bool green = false, ivory = false, intermediateAlpha = false, transparent = false;
            for (int p = 0; p < pixels.Length; p += 4)
            {
                byte b = pixels[p], g = pixels[p + 1], r = pixels[p + 2], a = pixels[p + 3];
                green |= a == 255 && r == 88 && g == 113 && b == 94;
                ivory |= a == 255 && r == 252 && g == 245 && b == 233;
                transparent |= a == 0; intermediateAlpha |= a > 0 && a < 255;
            }
            Check(green && ivory && transparent && intermediateAlpha, size + "px frame retains sage/ivory colors, transparent corners and antialiased alpha");
        }
        private static void VerifyIcon()
        {
            using (var stream = File.OpenRead(Path.Combine(Storage.Folder, "icons", "brand", "companion.ico")))
            using (var reader = new BinaryReader(stream))
            {
                Check(reader.ReadUInt16() == 0 && reader.ReadUInt16() == 1 && reader.ReadUInt16() == Sizes.Length, "ICO contains all nine requested sizes");
                for (int i = 0; i < Sizes.Length; i++)
                {
                    stream.Position = 6 + 16 * i; int width = reader.ReadByte(), height = reader.ReadByte();
                    if (width == 0) width = 256; if (height == 0) height = 256;
                    reader.ReadByte(); reader.ReadByte(); int planes = reader.ReadUInt16(), depth = reader.ReadUInt16();
                    int length = reader.ReadInt32(), offset = reader.ReadInt32();
                    Check(width == Sizes[i] && height == Sizes[i] && planes == 1 && depth == 32 && offset >= 150 && offset + length <= stream.Length, Sizes[i] + "px ICO directory has a valid 32-bit frame range");
                    stream.Position = offset; byte[] png = reader.ReadBytes(length); CheckPng(png, Sizes[i]);
                    Check(Convert.ToBase64String(png) == Convert.ToBase64String(Render(Sizes[i])), Sizes[i] + "px ICO is rendered from the actual SVG drawing");
                }
            }
        }
        private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent is T) yield return (T)parent;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
                foreach (T value in Descendants<T>(VisualTreeHelper.GetChild(parent, i))) yield return value;
        }
        private static bool NativeIconMatches(System.Drawing.Icon actual, System.Drawing.Icon expected)
        {
            using (var left = System.Drawing.Bitmap.FromHicon(actual.Handle))
            using (var right = System.Drawing.Bitmap.FromHicon(expected.Handle))
            {
                if (left.Size != right.Size) return false;
                for (int y = 0; y < left.Height; y++) for (int x = 0; x < left.Width; x++)
                    if (left.GetPixel(x, y) != right.GetPixel(x, y)) return false;
                return left.Width > 0;
            }
        }
        [STAThread]
        private static int Main(string[] args)
        {
            string mode = args.Length == 0 ? "bundled" : args[0];
            WorkboardWindow card = null; WorkbenchWindow bench = null;
            try
            {
                if (mode == "generate") { CreateIcon(Path.Combine(Storage.Folder, "icons", "brand", "companion.ico")); return 0; }
                if (mode == "resource")
                {
                    using (var embedded = System.Drawing.Icon.ExtractAssociatedIcon(args[1]))
                        Check(embedded != null && NativeIconMatches(embedded, AppVisuals.ApplicationIcon), "compiled candidate EXE resource renders the same native icon as the application ICO");
                    Report.AppendLine("Assertions: " + assertions); return 0;
                }
                Forms.Application.EnableVisualStyles(); Forms.Application.SetCompatibleTextRenderingDefault(false);
                var mark = (Image)AppVisuals.BrandIcon(27); var drawing = ((DrawingImage)mark.Source).Drawing as DrawingGroup;
                Check(drawing != null && drawing.Children.Count == 3 && drawing.IsFrozen && drawing.Bounds == new Rect(0, 0, 64, 64), "brand is a frozen three-layer 64px vector");
                Check(((SolidColorBrush)((GeometryDrawing)drawing.Children[0]).Brush).Color == Color.FromRgb(88, 113, 94)
                    && ((SolidColorBrush)((GeometryDrawing)drawing.Children[1]).Brush).Color == Color.FromRgb(252, 245, 233), "vector retains two independent brand colors");
                foreach (int size in new[] { 16, 20, 23, 27, 32, 64, 256 }) CheckPng(Render(size), size);
                Check(AppVisuals.BrandIcon(Double.NaN).Width == 24 && AppVisuals.BrandIcon(-1).Height == 24 && AppVisuals.BrandIcon(Double.PositiveInfinity).Width == 24, "invalid requested sizes select a safe default");
                if (mode == "bundled") VerifyIcon();
                // .NET Framework Icon.ToBitmap misreads PNG frames; Windows consumes HICON.
                using (var bitmap = System.Drawing.Bitmap.FromHicon(AppVisuals.ApplicationIcon.Handle))
                {
                    bitmap.Save(Path.Combine(Storage.Folder, "application-icon-readback.png"));
                    bool green = false, ivory = false;
                    for (int y = 0; y < bitmap.Height; y++) for (int x = 0; x < bitmap.Width; x++)
                    {
                        var color = bitmap.GetPixel(x, y); green |= color.A == 255 && color.R == 88 && color.G == 113 && color.B == 94;
                        ivory |= color.A == 255 && color.R == 252 && color.G == 245 && color.B == 233;
                    }
                    Check(green && ivory, mode + " application/tray ICO keeps both brand colors");
                }
                var store = new WorkboardStore(Path.Combine(Storage.Folder, "fixture"));
                card = new WorkboardWindow(store); bench = new WorkbenchWindow(store);
                Check(((ContentControl)card.View.FindName("CompanionBrand")).Content is Image, "desktop card brand host contains the vector mark");
                bool benchBrand = false;
                foreach (Image image in Descendants<Image>(bench.View))
                    benchBrand |= image.Width == 27 && image.Source is DrawingImage;
                Check(benchBrand && Object.ReferenceEquals(card.Icon, AppVisuals.ApplicationIcon) && Object.ReferenceEquals(bench.Icon, AppVisuals.ApplicationIcon), "workbench brand and both forms use the shared original mark");
                Check(!card.Visible && !bench.Visible && !card.IsHandleCreated && !bench.IsHandleCreated, "UI references are verified without showing native windows");
                using (var tray = new Forms.NotifyIcon { Icon = AppVisuals.ApplicationIcon, Visible = false })
                    Check(Object.ReferenceEquals(tray.Icon, AppVisuals.ApplicationIcon), "unshown tray shares the application icon");
                card.Dispose(); card = null;
                Check(NativeIconMatches(bench.Icon, AppVisuals.ApplicationIcon), "disposing card and tray leaves the workbench native icon alive");
                bench.Dispose(); bench = null;
                using (var bitmap = System.Drawing.Bitmap.FromHicon(AppVisuals.ApplicationIcon.Handle))
                    Check(bitmap.Width > 0 && bitmap.Height > 0, "disposing both forms leaves the shared application icon alive");
                FrameworkElement fluent = AppVisuals.Icon("tasks", 18, Brushes.Black);
                Check(fluent is Viewbox && ((Viewbox)fluent).Child is Canvas, "existing Fluent SVG renderer remains active");
                File.WriteAllBytes(Path.Combine(Storage.Folder, "brand-preview-256.png"), Render(256));
                if (mode != "bundled") Check(File.ReadAllText(Path.Combine(Storage.Folder, "运行日志.txt")).Contains("已使用内置陪伴标志"), mode + " assets select the embedded vector fallback");
                Report.AppendLine("LIMIT: offscreen rendering and resource checks do not observe Explorer taskbar cache refresh.");
                Report.AppendLine("Assertions: " + assertions); return 0;
            }
            catch (Exception error) { Report.AppendLine(error.ToString()); return 1; }
            finally
            {
                if (card != null) card.Dispose(); if (bench != null) bench.Dispose();
                File.WriteAllText(Path.Combine(Storage.Folder, mode == "resource" ? "verification-brand-resource.txt" : "verification-brand.txt"), Report.ToString(), Encoding.UTF8);
            }
        }
    }
}
