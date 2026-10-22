using System;
using System.Collections.Generic;
using File = System.IO.File;
using FileInfo = System.IO.FileInfo;
using MemoryStream = System.IO.MemoryStream;
using BinaryWriter = System.IO.BinaryWriter;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Xml;

namespace LocalDesktopNote
{
    internal static class AppVisuals
    {
        private static readonly Dictionary<string, string> Files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "tasks", "checkbox_checked" }, { "note", "note" }, { "history", "history" },
            { "settings", "settings" }, { "images", "image_multiple" }, { "plus", "add" },
            { "more", "more_horizontal" }, { "expand", "arrow_expand" },
            { "calendar", "calendar_ltr" }, { "remove", "dismiss" }
        };
        private static readonly Dictionary<string, Geometry> Cache = new Dictionary<string, Geometry>(StringComparer.OrdinalIgnoreCase);
        private static readonly object BrandLock = new object();
        private static DrawingGroup brandDrawing;
        private static System.Drawing.Icon applicationIcon;
        // Same original vector as icons/brand/companion.svg, kept for offline asset failure.
        private static readonly string[] BrandPaths = {
            "M18.5 0H45.5C56.2 0 64 7.8 64 18.5V45.5C64 56.2 56.2 64 45.5 64H18.5C7.8 64 0 56.2 0 45.5V18.5C0 7.8 7.8 0 18.5 0Z",
            "M17.4 52.6C14.1 52.8 11.9 51 11.7 48.6C11.3 45.6 14.7 42 19.4 41.9C31.4 41.8 39.2 34.2 38.7 23C38.2 18.5 41.6 14.7 45.8 14.7C49.7 14.7 52.8 17.9 52.7 22C51.6 37.6 37.3 50.3 17.4 52.6Z",
            "M25 37.5C21.9 38.6 19.7 37.1 20.5 33.3C21.3 29.6 24.8 25.6 27.9 25.6C31.4 25.4 33.2 28.1 32.3 31.3C31.5 34 27.9 36.9 25 37.5Z"
        };
        private static readonly Dictionary<string, string> Labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "tasks", "任务" }, { "note", "备忘" }, { "history", "历史" }, { "settings", "设置" },
            { "images", "图片" }, { "plus", "添加" }, { "more", "更多" }, { "expand", "展开" },
            { "calendar", "日期" }, { "remove", "移除" }
        };

        internal static FrameworkElement Icon(string name, double size, Brush foreground)
        {
            name = (name ?? "").Trim();
            if (Double.IsNaN(size) || Double.IsInfinity(size) || size <= 0) size = 16;
            Brush brush = foreground ?? SystemColors.ControlTextBrush;
            Geometry geometry = Load(name);
            FrameworkElement content;
            if (geometry != null)
            {
                var canvas = new Canvas { Width = 24, Height = 24 };
                canvas.Children.Add(new Path { Data = geometry, Fill = brush });
                content = canvas;
            }
            else
            {
                // Resource failure remains readable text, never an emoji imitation.
                string label;
                if (!Labels.TryGetValue(name, out label)) label = "图标";
                content = new TextBlock { Text = label, FontFamily = SystemFonts.MessageFontFamily, Foreground = brush, FontSize = 12 };
            }
            return new Viewbox { Width = size, Height = size, Stretch = Stretch.Uniform, Child = content, IsHitTestVisible = false, Focusable = false };
        }

        internal static FrameworkElement BrandIcon(double size)
        {
            if (Double.IsNaN(size) || Double.IsInfinity(size) || size <= 0) size = 24;
            var result = new Image { Width = size, Height = size, Source = new DrawingImage(LoadBrand()), Stretch = Stretch.Uniform, IsHitTestVisible = false, Focusable = false };
            System.Windows.Automation.AutomationProperties.SetName(result, "Yourself Copilot 陪伴标志");
            return result;
        }

        internal static System.Drawing.Icon ApplicationIcon
        {
            get
            {
                lock (BrandLock)
                {
                    if (applicationIcon != null) return applicationIcon;
                    try
                    {
                        string file = System.IO.Path.Combine(Storage.Folder, "icons", "brand", "companion.ico");
                        if (new FileInfo(file).Length > 1048576) throw new FormatException("应用图标文件过大");
                        using (var stream = File.OpenRead(file))
                        using (var icon = new System.Drawing.Icon(stream)) applicationIcon = (System.Drawing.Icon)icon.Clone();
                    }
                    catch (Exception error)
                    {
                        Storage.Log("应用 ICO 不可用，已使用内置陪伴标志：" + error.Message);
                        // Encode a small alpha PNG as an ICO frame without unmanaged handles.
                        var visual = new DrawingVisual();
                        using (var context = visual.RenderOpen()) context.DrawDrawing(LoadBrand());
                        var bitmap = new RenderTargetBitmap(64, 64, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
                        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        using (var png = new MemoryStream())
                        using (var ico = new MemoryStream())
                        {
                            encoder.Save(png); byte[] bytes = png.ToArray();
                            var writer = new BinaryWriter(ico);
                            writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)1);
                            writer.Write((byte)64); writer.Write((byte)64); writer.Write((byte)0); writer.Write((byte)0);
                            writer.Write((ushort)1); writer.Write((ushort)32); writer.Write(bytes.Length); writer.Write(22); writer.Write(bytes); writer.Flush();
                            ico.Position = 0;
                            using (var icon = new System.Drawing.Icon(ico)) applicationIcon = (System.Drawing.Icon)icon.Clone();
                        }
                    }
                    return applicationIcon;
                }
            }
        }

        private static DrawingGroup LoadBrand()
        {
            lock (BrandLock)
            {
                if (brandDrawing != null) return brandDrawing;
                var group = new DrawingGroup();
                try
                {
                    string path = System.IO.Path.Combine(Storage.Folder, "icons", "brand", "companion.svg");
                    if (new FileInfo(path).Length > 16384) throw new FormatException("品牌 SVG 文件过大");
                    var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
                    var document = new XmlDocument { XmlResolver = null };
                    using (var reader = XmlReader.Create(path, settings)) document.Load(reader);
                    XmlElement svg = document.DocumentElement;
                    if (svg == null || svg.LocalName != "svg" || svg.GetAttribute("viewBox") != "0 0 64 64" || svg.HasAttribute("transform"))
                        throw new FormatException("品牌图标需要 64x64 SVG viewBox");
                    foreach (XmlNode node in svg.ChildNodes)
                    {
                        var element = node as XmlElement;
                        if (element == null || element.LocalName == "title" || element.LocalName == "desc") continue;
                        string fill = element.GetAttribute("fill");
                        if (element.LocalName != "path" || element.HasAttribute("transform") || !System.Text.RegularExpressions.Regex.IsMatch(fill, "^#[0-9a-fA-F]{6}$"))
                            throw new FormatException("品牌图标仅支持分层 path 与实色填充");
                        group.Children.Add(new GeometryDrawing((Brush)new BrushConverter().ConvertFromInvariantString(fill), null, Geometry.Parse("F1 " + element.GetAttribute("d"))));
                    }
                    if (group.Children.Count != 3) throw new FormatException("品牌图标需要三个填色层");
                }
                catch (Exception error)
                {
                    group.Children.Clear();
                    for (int i = 0; i < BrandPaths.Length; i++)
                        group.Children.Add(new GeometryDrawing((Brush)new BrushConverter().ConvertFromInvariantString(i == 0 ? "#58715E" : "#FCF5E9"), null, Geometry.Parse("F1 " + BrandPaths[i])));
                    Storage.Log("品牌 SVG 不可用，已使用内置陪伴标志：" + error.Message);
                }
                group.Freeze(); brandDrawing = group; return group;
            }
        }

        private static Geometry Load(string name)
        {
            lock (Cache)
            {
                Geometry cached;
                if (Cache.TryGetValue(name, out cached)) return cached;
                try
                {
                    string file;
                    if (!Files.TryGetValue(name, out file)) throw new ArgumentException("未知图标名称：" + name);
                    string path = System.IO.Path.Combine(Storage.Folder, "icons", "svg", "ic_fluent_" + file + "_24_regular.svg");
                    var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
                    var document = new XmlDocument { XmlResolver = null };
                    using (var reader = XmlReader.Create(path, settings)) document.Load(reader);
                    XmlElement svg = document.DocumentElement;
                    if (svg == null || svg.LocalName != "svg" || svg.GetAttribute("viewBox") != "0 0 24 24" || svg.HasAttribute("transform"))
                        throw new FormatException("图标需要原库的 24x24 SVG viewBox：" + path);
                    var group = new GeometryGroup { FillRule = FillRule.Nonzero };
                    foreach (XmlNode node in svg.ChildNodes)
                    {
                        var element = node as XmlElement;
                        if (element == null || element.LocalName == "title" || element.LocalName == "desc") continue;
                        if (element.LocalName != "path" || element.HasAttribute("transform"))
                            throw new FormatException("图标包含当前渲染器不支持的 SVG 元素：" + path);
                        if (element.GetAttribute("fill") == "none") continue;
                        string data = element.GetAttribute("d");
                        if (String.IsNullOrWhiteSpace(data)) throw new FormatException("图标 path 缺少几何数据：" + path);
                        string rule = element.GetAttribute("fill-rule");
                        if (String.IsNullOrEmpty(rule)) rule = svg.GetAttribute("fill-rule");
                        group.Children.Add(Geometry.Parse((rule == "evenodd" ? "F0 " : "F1 ") + data));
                    }
                    if (group.Children.Count == 0) throw new FormatException("图标没有可绘制的 path：" + path);
                    group.Freeze(); Cache[name] = group; return group;
                }
                catch (Exception error)
                {
                    // Missing assets are expected in older isolated QA directories.
                    // Cache the failure so normal interaction does not flood the log.
                    Cache[name] = null;
                    Storage.Log("离线图标不可用 [" + name + "]，已使用文字回退：" + error.Message);
                    return null;
                }
            }
        }
    }
}
