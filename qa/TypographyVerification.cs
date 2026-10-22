using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace LocalDesktopNote
{
    internal static class TypographyVerification
    {
        private static readonly StringBuilder Report = new StringBuilder();
        private static int assertions;
        private const string Sample = "今天安排明天任务，随手记下想法；中文标点（测试）。ABC abc 0123";

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("FAIL: " + message);
            assertions++; Report.AppendLine("PASS: " + message);
        }

        private static bool FinitePositive(double value)
        {
            return value > 0 && !Double.IsNaN(value) && !Double.IsInfinity(value);
        }

        private static GlyphTypeface Face(FontFamily family)
        {
            GlyphTypeface face;
            return new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal).TryGetGlyphTypeface(out face) ? face : null;
        }

        private static bool UsesFile(FontFamily family, string relativePath)
        {
            GlyphTypeface face = Face(family);
            return face != null && face.FontUri.IsFile && String.Equals(Path.GetFullPath(face.FontUri.LocalPath), Path.GetFullPath(Path.Combine(Storage.Folder, relativePath)), StringComparison.OrdinalIgnoreCase);
        }

        private static bool Covers(FontFamily family)
        {
            GlyphTypeface face = Face(family);
            return face != null && Sample.Where(c => !Char.IsWhiteSpace(c)).All(delegate(char value)
            {
                ushort glyph; return face.CharacterToGlyphMap.TryGetValue(value, out glyph) && glyph != 0;
            });
        }

        private static FormattedText Text(FontFamily family, double scale)
        {
            var text = new FormattedText(Sample + Sample, CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight,
                new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), 14 * scale, Brushes.Black, 1.0);
            text.MaxTextWidth = 280 * scale;
            return text;
        }

        private static T VisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                T found = child as T ?? VisualChild<T>(child);
                if (found != null) return found;
            }
            return null;
        }

        private static void MeasureCard(WorkboardWindow card, int width, int height)
        {
            card.View.Width = width; card.View.Height = height;
            card.View.Measure(new Size(width, height));
            card.View.Arrange(new Rect(0, 0, width, height));
            card.View.UpdateLayout();
            var memoScroll = VisualChild<ScrollViewer>(card.IdeasEditor);
            var taskScroll = (ScrollViewer)card.View.FindName("TaskScroll");
            Check(FinitePositive(card.IdeasEditor.ActualWidth) && FinitePositive(card.IdeasEditor.ActualHeight)
                && memoScroll != null && memoScroll.ScrollableHeight > 0 && taskScroll.ScrollableHeight > 0
                && card.View.RenderSize.Width == width && card.View.RenderSize.Height == height,
                "long Chinese memo and task rows lay out with scroll overflow at " + width + "x" + height + " without showing a window");
            Report.AppendLine("LAYOUT: " + width + "x" + height + " memo viewport=" + memoScroll.ViewportHeight.ToString("F2", CultureInfo.InvariantCulture)
                + " extent=" + memoScroll.ExtentHeight.ToString("F2", CultureInfo.InvariantCulture)
                + "; tasks viewport=" + taskScroll.ViewportHeight.ToString("F2", CultureInfo.InvariantCulture)
                + " extent=" + taskScroll.ExtentHeight.ToString("F2", CultureInfo.InvariantCulture));
        }

        [STAThread]
        private static int Main(string[] args)
        {
            string mode = args.Length == 1 ? args[0] : "bundled";
            WorkboardWindow card = null; WorkbenchWindow bench = null;
            try
            {
                Forms.Application.EnableVisualStyles(); Forms.Application.SetCompatibleTextRenderingDefault(false);
                bool bundled = mode == "bundled", bodyOnly = mode == "memo-missing";
                if (bundled || bodyOnly)
                    Check(UsesFile(AppTypography.Body, @"fonts\NotoSansSC\NotoSansSC-Regular.otf"), "body glyphs resolve to the exact application-private Noto file");
                else
                    Check(!UsesFile(AppTypography.Body, @"fonts\NotoSansSC\NotoSansSC-Regular.otf") && AppTypography.Body.Source == "Microsoft YaHei UI, Segoe UI", mode + " fonts safely select the system body fallback");
                if (bundled)
                {
                    Check(UsesFile(AppTypography.Memo, @"fonts\LXGWWenKai\LXGWWenKai-Regular.ttf"), "memo glyphs resolve to the exact application-private WenKai file");
                    Check(Covers(AppTypography.Body) && Covers(AppTypography.Memo), "both private fonts contain representative Chinese, punctuation, Latin letters and digits");
                    Check(Storage.Folder.Contains("中文") && Storage.Folder.Contains("&") && Storage.Folder.Contains(" "), "private fonts load from an application path containing Chinese, ampersand and spaces");
                }
                else
                    Check(Object.ReferenceEquals(AppTypography.Memo, AppTypography.Body), mode + " memo falls back to the same usable body family");

                var store = new WorkboardStore(Path.Combine(Storage.Folder, "fixture"));
                store.State.Ideas = String.Join("\n", Enumerable.Repeat(Sample + Sample, 18).ToArray());
                for (int i = 0; i < 12; i++) store.AddTask("长中文任务 " + i + "：" + Sample);
                card = new WorkboardWindow(store); bench = new WorkbenchWindow(store);
                Check(!card.IsHandleCreated && !bench.IsHandleCreated && !card.Visible && !bench.Visible, "workboard and workbench instantiate without showing or activating a native window in " + mode + " mode");
                if (bundled)
                {
                    Check(UsesFile(card.IdeasEditor.FontFamily, @"fonts\LXGWWenKai\LXGWWenKai-Regular.ttf")
                        && UsesFile(bench.IdeasEditor.FontFamily, @"fonts\LXGWWenKai\LXGWWenKai-Regular.ttf")
                        && UsesFile(((TextBlock)card.View.FindName("IdeasPlaceholder")).FontFamily, @"fonts\LXGWWenKai\LXGWWenKai-Regular.ttf")
                        && UsesFile(card.TaskEditors[0].FontFamily, @"fonts\NotoSansSC\NotoSansSC-Regular.otf")
                        && UsesFile(bench.TaskEditors[0].FontFamily, @"fonts\NotoSansSC\NotoSansSC-Regular.otf"),
                        "actual memo controls and placeholder use WenKai while both task editors resolve to Noto");
                    MeasureCard(card, 350, 290); MeasureCard(card, 450, 340);
                }

                bool measurable = true;
                foreach (double scale in new[] { 1.0, 1.25, 1.5 })
                {
                    FormattedText body = Text(AppTypography.Body, scale), memo = Text(AppTypography.Memo, scale);
                    measurable &= FinitePositive(body.Width) && FinitePositive(body.Height) && FinitePositive(memo.Width) && FinitePositive(memo.Height);
                    Report.AppendLine("METRIC: " + mode + " scale=" + scale.ToString("F2", CultureInfo.InvariantCulture) + " body=" + body.Width.ToString("F2", CultureInfo.InvariantCulture) + "x" + body.Height.ToString("F2", CultureInfo.InvariantCulture)
                        + " memo=" + memo.Width.ToString("F2", CultureInfo.InvariantCulture) + "x" + memo.Height.ToString("F2", CultureInfo.InvariantCulture));
                }
                Check(measurable, "mixed Chinese text remains measurable at 100/125/150 percent proportional sizes in " + mode + " mode");
                Report.AppendLine("LIMIT: proportional font-size measurement is not a real monitor-DPI, screenshot, IME or focus test.");
                Report.AppendLine("Assertions: " + assertions);
                return 0;
            }
            catch (Exception error) { Report.AppendLine(error.ToString()); return 1; }
            finally
            {
                if (card != null) card.Dispose(); if (bench != null) bench.Dispose();
                File.WriteAllText(Path.Combine(Storage.Folder, "verification-typography.txt"), Report.ToString(), Encoding.UTF8);
            }
        }
    }
}
