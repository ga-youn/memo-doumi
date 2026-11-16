using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace MemoDoumi
{
    internal static class DemoMain
    {
        const int Width = 960, Height = 540;

        static Bitmap Desktop()
        {
            var image = new Bitmap(Width, Height);
            using (Graphics g = Graphics.FromImage(image))
            using (var background = new LinearGradientBrush(new Rectangle(0, 0, Width, Height), Color.FromArgb(15, 51, 112), Color.FromArgb(97, 169, 231), 25f))
            {
                g.FillRectangle(background, 0, 0, Width, Height);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                for (int i = 0; i < 16; i++)
                {
                    using (var ribbon = new Pen(Color.FromArgb(18 + i * 2, 125, 206, 255), 32))
                        g.DrawBezier(ribbon, 200 + i * 13, 580, 900 - i * 8, 60 + i * 4, 50 + i * 16, -150, 740 + i * 5, 170 + i * 14);
                }
                using (var glass = new SolidBrush(Color.FromArgb(240, 226, 238, 250)))
                    g.FillRectangle(glass, 0, 502, Width, 38);
                using (var line = new Pen(Color.FromArgb(220, 244, 250, 255))) g.DrawLine(line, 0, 502, Width, 502);
                using (var blue = new SolidBrush(Color.FromArgb(30, 117, 217)))
                    for (int y = 0; y < 2; y++) for (int x = 0; x < 2; x++) g.FillRectangle(blue, 412 + x * 11, 512 + y * 11, 9, 9);
                using (var pen = new Pen(Color.FromArgb(68, 83, 100), 2)) { g.DrawEllipse(pen, 453, 513, 13, 13); g.DrawLine(pen, 465, 525, 471, 531); }
                using (var folder = new SolidBrush(Color.FromArgb(239, 184, 68))) { g.FillRectangle(folder, 491, 514, 22, 16); g.FillRectangle(folder, 491, 510, 10, 7); }
                using (var edge = new SolidBrush(Color.FromArgb(24, 161, 175))) g.FillEllipse(edge, 533, 511, 22, 22);
                using (var center = new SolidBrush(Color.FromArgb(226, 238, 250))) g.FillEllipse(center, 540, 516, 12, 10);
                using (var text = new SolidBrush(Color.FromArgb(74, 89, 107)))
                using (var font = new Font("Segoe UI", 8))
                    g.DrawString("10:24", font, text, 900, 514);
            }
            return image;
        }

        static void DrawBubble(Graphics g, float dogCenter, int dogTop, string text)
        {
            const int w = 250, h = 126;
            int x = Math.Max(24, Math.Min(Width - w - 24, (int)dogCenter - 190));
            int y = dogTop - h + 5;
            var state = g.Save();
            g.TranslateTransform(x, y);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using (var path = Bubble.Path(w, h, true, (int)dogCenter - x))
            using (var fill = new SolidBrush(Color.FromArgb(255, 250, 229)))
            using (var border = new Pen(Color.FromArgb(143, 124, 101), 1.5f))
            using (var ink = new SolidBrush(Color.FromArgb(91, 73, 60)))
            using (var faded = new SolidBrush(Color.FromArgb(161, 141, 112)))
            using (var title = Fonts.Make(15))
            using (var small = Fonts.Make(10))
            using (var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.FillPath(fill, path);
                g.DrawPath(border, path);
                g.DrawString("+", small, faded, 14, 10);
                g.DrawString("×", small, faded, w - 28, 10);
                g.DrawString(text, title, ink, new RectangleF(12, 36, w - 24, 52), center);
            }
            g.Restore(state);
        }

        static void DrawCursor(Graphics g, int frame)
        {
            if (frame < 55 || frame >= 110) return;
            float approach = Math.Min(1f, (frame - 55) / 15f);
            approach = approach * approach * (3 - 2 * approach);
            float leave = Math.Max(0f, Math.Min(1f, (frame - 90) / 19f));
            leave = leave * leave * (3 - 2 * leave);
            float x = 890 - 155 * approach + 245 * leave;
            float y = 310 + 140 * approach - 160 * leave;
            var state = g.Save();
            g.TranslateTransform(x, y);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (frame >= 80 && frame < 85)
            {
                float radius = 7 + (frame - 80) * 4;
                using (var ring = new Pen(Color.FromArgb(200 - (frame - 80) * 35, 255, 255, 255), 2))
                    g.DrawEllipse(ring, -radius, -radius, radius * 2, radius * 2);
            }
            PointF[] arrow = { new PointF(0, 0), new PointF(0, 22), new PointF(6, 17), new PointF(10, 26), new PointF(14, 24), new PointF(10, 15), new PointF(18, 15) };
            using (var border = new Pen(Color.FromArgb(35, 35, 35), 1.4f))
            {
                g.FillPolygon(Brushes.White, arrow);
                g.DrawPolygon(border, arrow);
            }
            g.Restore(state);
        }

        static int Main(string[] args)
        {
            try
            {
                Directory.CreateDirectory(args[0]);
                using (Bitmap desktop = Desktop())
                {
                    string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sprites");
                    string[] names = { "stand", "walk1", "walk2", "wag", "blink", "sleep", "happy" };
                    var dogs = new Bitmap[names.Length];
                    try
                    {
                        for (int i = 0; i < names.Length; i++) dogs[i] = new Bitmap(Path.Combine(folder, names[i] + ".png"));
                        for (int frame = 0; frame < 200; frame++)
                        {
                            bool walking = frame < 70 || (frame >= 110 && frame < 170);
                            bool left = frame >= 110 && frame < 170;
                            float x = frame < 70 ? 180 + frame * 520f / 70 : frame < 110 ? 700 : frame < 170 ? 700 - (frame - 110) * 520f / 60 : 180;
                            int pose = walking ? 1 + ((frame / 3) % 2) : frame < 92 ? 6 : frame < 110 ? (frame % 6 < 3 ? 3 : 0) : frame < 180 ? 4 : 5;
                            int bob = walking ? (frame % 6 < 3 ? 0 : -2) : 0;
                            using (var canvas = new Bitmap(desktop))
                            using (Graphics g = Graphics.FromImage(canvas))
                            using (var dog = new Bitmap(dogs[pose]))
                            {
                                if (left) dog.RotateFlip(RotateFlipType.RotateNoneFlipX);
                                int h = 78, w = dog.Width * h / dog.Height;
                                using (var shadow = new SolidBrush(Color.FromArgb(30, 0, 20, 60))) g.FillEllipse(shadow, x + 8, 496, Math.Max(10, w - 16), 6);
                                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                                g.PixelOffsetMode = PixelOffsetMode.Half;
                                g.DrawImage(dog, new Rectangle((int)x, 502 - h + bob, w, h));
                                if (frame >= 80 && frame < 110)
                                    DrawBubble(g, x + w / 2f, 502 - h + bob, "우유 사기\n택배 보내기");
                                DrawCursor(g, frame);
                                canvas.Save(Path.Combine(args[0], frame.ToString("D3") + ".png"), ImageFormat.Png);
                            }
                        }
                    }
                    finally { foreach (Bitmap dog in dogs) if (dog != null) dog.Dispose(); }
                }
                return 0;
            }
            catch (Exception ex) { File.WriteAllText("demo-error.txt", ex.ToString()); return 1; }
        }
    }
}
