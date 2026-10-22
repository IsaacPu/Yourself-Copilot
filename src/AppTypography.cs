using System;
using System.IO;
using System.Windows;
using System.Windows.Media;

namespace LocalDesktopNote
{
    internal static class AppTypography
    {
        internal static readonly FontFamily Body = Load("NotoSansSC", "NotoSansSC-Regular.otf", "Noto Sans SC", new FontFamily("Microsoft YaHei UI, Segoe UI"));
        internal static readonly FontFamily Memo = Load("LXGWWenKai", "LXGWWenKai-Regular.ttf", "LXGW WenKai", Body);

        private static FontFamily Load(string directory, string fileName, string familyName, FontFamily fallback)
        {
            string folder = Path.Combine(Storage.Folder, "fonts", directory) + Path.DirectorySeparatorChar;
            string fontFile = Path.Combine(folder, fileName);
            if (!File.Exists(fontFile)) return fallback;
            try
            {
                // Private font files stay with the app; never install or depend on
                // a same-named system font that may have a different version.
                var family = new FontFamily(new Uri(folder, UriKind.Absolute), "./#" + familyName);
                GlyphTypeface face;
                if (!new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal).TryGetGlyphTypeface(out face)
                    || !face.FontUri.IsFile || !String.Equals(Path.GetFullPath(face.FontUri.LocalPath), Path.GetFullPath(fontFile), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("未能读取程序附带字体：" + familyName);
                return family;
            }
            catch (Exception error)
            {
                Storage.Log("字体读取失败，使用后备字体：" + error.Message);
                return fallback;
            }
        }
    }
}
