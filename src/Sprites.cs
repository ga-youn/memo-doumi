using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace MemoDoumi
{
    internal enum Pose { Stand, Blink, Wag, Walk1, Walk2, Sleep, Happy }

    internal class Sprites
    {
        const int GridW = 36, GridH = 28;
        static readonly string[] FileNames = { "stand", "blink", "wag", "walk1", "walk2", "sleep", "happy" };

        public int W, H;
        readonly byte[][] right = new byte[FileNames.Length][];
        readonly byte[][] left = new byte[FileNames.Length][];

        public byte[] Get(Pose p, bool faceLeft) { return (faceLeft ? left : right)[(int)p]; }

        static readonly string[] StandRows = {
            "....................oooooooo........",
            "..................oowwwwwwwwoo......",
            "...............ooowwwwwwwwwwwwooo...",
            "..............oeeowwwwwwwwwwwwoeeo..",
            ".............oeeeowwwwwwwwwwwwoeeeo.",
            ".............oeeeowwwwwwwwwwwwoeeeo.",
            "............oeeeeowwwwwwwwwwwwoeeeeo",
            "............oeeeeowkkkwwwwkkkwoeeeeo",
            "............oeeeeowkkkwwwwkkkwoeeeeo",
            "..oo........oeeeeowkkkwwwwkkkwoeeeeo",
            ".occo.......oeeeeowwwwnnnnwwwwoeeeeo",
            ".occco.......oeeeowwwwwnnwwwwwoeeeo.",
            "..occcooooooooeeeowwwmmnnmmwwwoeeeo.",
            "...occccccccccoooowwwwmmmmwwwwoooo..",
            "..occccccccccccccoswwwwwwwwwwso.....",
            "..occcccccccccccccoosswwwwssoo......",
            "..occcccccccccccccccoooooooo........",
            "..occccccccccccccccccccccccco.......",
            "..occccccccccccccccccccccccco.......",
            "..occccccccccccccccccccccccco.......",
            "..occccccccccccccccccccccccco.......",
            "..oscccccccccccccccccccccccso.......",
            "...ossccccccccccccccccccssso........",
            "....occcoooooooooooooccco...........",
            "....occco...........occco...........",
            "....occco...........occco...........",
            "....occco...........occco...........",
            "....ooooo...........ooooo...........",
        };

        static Color ColorOf(char c)
        {
            switch (c)
            {
                case 'o': return Color.FromArgb(255, 0x7A, 0x6A, 0x64);
                case 'w': return Color.FromArgb(255, 0xFF, 0xFF, 0xFF);
                case 'c': return Color.FromArgb(255, 0xFB, 0xF1, 0xEE);
                case 's': return Color.FromArgb(255, 0xEB, 0xDA, 0xD8);
                case 'e': return Color.FromArgb(255, 0xF3, 0xE4, 0xDF);
                case 'p': return Color.FromArgb(255, 0xF4, 0xA9, 0xB4);
                case 'k': return Color.FromArgb(255, 0x24, 0x1E, 0x1C);
                case 'n': return Color.FromArgb(255, 0x4A, 0x3D, 0x3B);
                case 'm': return Color.FromArgb(255, 0xA5, 0x9C, 0x9A);
                default: return Color.Transparent;
            }
        }

        static string[] Clone(string[] rows) { return (string[])rows.Clone(); }

        static string SetChar(string row, int col, char c)
        {
            return row.Substring(0, col) + c + row.Substring(col + 1);
        }

        static Dictionary<Pose, string[]> BuiltinRows()
        {
            var d = new Dictionary<Pose, string[]>();
            string[] stand = StandRows;
            d[Pose.Stand] = stand;
            d[Pose.Walk1] = stand;

            string[] blink = Clone(stand);
            blink[7] = blink[7].Replace('k', 'w');
            blink[8] = blink[8].Replace("kkwk", "kkkk");
            blink[9] = blink[9].Replace('k', 'w');
            d[Pose.Blink] = blink;

            string[] wag = Clone(stand);
            wag[8] = ".oo........." + stand[8].Substring(12);
            wag[9] = "occo........" + stand[9].Substring(12);
            wag[10] = ".occo......." + stand[10].Substring(12);
            wag[11] = "..occo......" + stand[11].Substring(12);
            d[Pose.Wag] = wag;

            string[] walk2 = Clone(wag);
            walk2[23] = "....oooooccc" + "oooocccoooooo...........";
            walk2[24] = walk2[25] = walk2[26] = "........occc" + "o..occco................";
            walk2[27] = "........oooo" + "o..ooooo................";
            d[Pose.Walk2] = walk2;

            string[] happy = Clone(wag);
            happy[13] = SetChar(SetChar(happy[13], 23, 'p'), 24, 'p');
            happy[14] = SetChar(SetChar(happy[14], 23, 'p'), 24, 'p');
            d[Pose.Happy] = happy;

            var sleep = new string[GridH];
            sleep[0] = sleep[1] = sleep[2] = sleep[3] = "";
            for (int i = 0; i < 23; i++) sleep[i + 4] = blink[i];
            sleep[27] = "....oooooooooooooooooooooooo........";
            d[Pose.Sleep] = sleep;
            return d;
        }

        static Bitmap RenderRows(string[] rows, int px)
        {
            var bmp = new Bitmap(GridW * px, GridH * px, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                for (int y = 0; y < GridH; y++)
                {
                    string row = (rows[y] ?? "").PadRight(GridW, '.');
                    for (int x = 0; x < GridW; x++)
                    {
                        Color c = ColorOf(row[x]);
                        if (c.A == 0) continue;
                        using (var b = new SolidBrush(c)) g.FillRectangle(b, x * px, y * px, px, px);
                    }
                }
            }
            return bmp;
        }

        static byte[] Premultiplied(Bitmap bmp)
        {
            int w = bmp.Width, h = bmp.Height;
            var buf = new byte[w * h * 4];
            BitmapData data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            try
            {
                for (int y = 0; y < h; y++)
                    Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), buf, y * w * 4, w * 4);
            }
            finally { bmp.UnlockBits(data); }
            return buf;
        }

        void Store(Pose p, Bitmap bmp)
        {
            right[(int)p] = Premultiplied(bmp);
            bmp.RotateFlip(RotateFlipType.RotateNoneFlipX);
            left[(int)p] = Premultiplied(bmp);
        }

        public static Sprites Load(int px)
        {
            Sprites custom = null;
            try { custom = LoadCustom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sprites"), px); }
            catch (Exception ex) { App.Log(ex); }
            if (custom != null) return custom;

            var s = new Sprites();
            s.W = GridW * px;
            s.H = GridH * px;
            foreach (var kv in BuiltinRows())
                using (Bitmap bmp = RenderRows(kv.Value, px)) s.Store(kv.Key, bmp);
            return s;
        }

        static Sprites LoadCustom(string dir, int px)
        {
            string standPath = Path.Combine(dir, "stand.png");
            if (!File.Exists(standPath)) return null;

            var s = new Sprites();
            int targetH = GridH * px;
            bool pixelArt;
            using (var src = new Bitmap(standPath))
            {
                pixelArt = src.Height <= 64;
                if (pixelArt)
                {
                    int f = px;
                    s.W = src.Width * f;
                    s.H = src.Height * f;
                }
                else
                {
                    s.H = (int)Math.Round(targetH * 1.2);
                    s.W = Math.Max(1, (int)Math.Round(src.Width * (s.H / (double)src.Height)));
                }
            }

            for (int i = 0; i < FileNames.Length; i++)
            {
                string path = Path.Combine(dir, FileNames[i] + ".png");
                if (!File.Exists(path)) path = standPath;
                using (var src = new Bitmap(path))
                using (var bmp = new Bitmap(s.W, s.H, PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.Transparent);
                        g.InterpolationMode = pixelArt ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.Half;
                        g.DrawImage(src, new Rectangle(0, 0, s.W, s.H));
                    }
                    s.Store((Pose)i, bmp);
                }
            }
            return s;
        }

        public static void ExportIcon(string path)
        {
            int[] sizes = { 16, 24, 32, 48, 64, 256 };
            var pngs = new List<byte[]>();
            using (Bitmap art = RenderRows(StandRows, 1))
            {
                foreach (int size in sizes)
                {
                    using (var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb))
                    {
                        using (Graphics g = Graphics.FromImage(bmp))
                        {
                            g.Clear(Color.Transparent);
                            g.InterpolationMode = InterpolationMode.NearestNeighbor;
                            g.PixelOffsetMode = PixelOffsetMode.Half;
                            float f = size / (float)GridW;
                            g.DrawImage(art, 0f, (size - GridH * f) / 2f, GridW * f, GridH * f);
                        }
                        using (var ms = new MemoryStream())
                        {
                            bmp.Save(ms, ImageFormat.Png);
                            pngs.Add(ms.ToArray());
                        }
                    }
                }
            }
            using (var fs = File.Create(path))
            using (var w = new BinaryWriter(fs))
            {
                w.Write((short)0);
                w.Write((short)1);
                w.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    byte b = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
                    w.Write(b); w.Write(b); w.Write((byte)0); w.Write((byte)0);
                    w.Write((short)1); w.Write((short)32);
                    w.Write(pngs[i].Length); w.Write(offset);
                    offset += pngs[i].Length;
                }
                foreach (var png in pngs) w.Write(png);
            }
        }
    }
}
