using System;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;

namespace MemoDoumi
{

    internal static class Fonts
    {
        const string Wanted = "Pretendard", Fallback = "Malgun Gothic";
        const int FR_PRIVATE = 0x10;
        static readonly string[] Files = { "Pretendard-Regular.ttf", "Pretendard-Bold.ttf" };

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
        static extern int AddFontResourceEx(string file, int flags, IntPtr reserved);

        static readonly PrivateFontCollection collection = new PrivateFontCollection();
        static readonly FontFamily family;

        public static readonly string Name = Fallback;

        static Fonts()
        {
            try
            {
                string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fonts");
                foreach (string file in Files)
                {
                    string path = Path.Combine(dir, file);
                    if (!File.Exists(path)) return;
                }
                foreach (string file in Files)
                {
                    string path = Path.Combine(dir, file);
                    if (AddFontResourceEx(path, FR_PRIVATE, IntPtr.Zero) == 0) return;
                    collection.AddFontFile(path);
                }
                foreach (FontFamily f in collection.Families)
                {
                    if (f.Name != Wanted) continue;
                    family = f;
                    Name = Wanted;
                }
            }
            catch { }
        }

        public static Font Make(float size, FontStyle style = FontStyle.Regular)
        {
            if (family != null && family.IsStyleAvailable(style))
                return new Font(family, size, style, GraphicsUnit.Point, 129);
            return new Font(Fallback, size, style, GraphicsUnit.Point, 129);
        }
    }
}
