using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MemoDoumi
{

    internal class ScrollThumb : Control
    {
        public Color ThumbColor = Color.Gray;
        public Action<double> Dragged;

        double pos, size = 1;
        bool shown, dragging, hot;
        int grab;

        public ScrollThumb()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        public void Set(double position, double sizeFraction, bool visible)
        {
            pos = Math.Max(0, Math.Min(1, position));
            size = Math.Max(0, Math.Min(1, sizeFraction));
            shown = visible;
            Invalidate();
        }

        int ThumbHeight { get { return Math.Min(Height, Math.Max(App.S(20), (int)Math.Round(size * Height))); } }

        Rectangle ThumbRect
        {
            get
            {
                int h = ThumbHeight;
                return new Rectangle(0, (int)Math.Round(pos * (Height - h)), Width, h);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            if (!shown) return;
            Rectangle r = ThumbRect;
            if (r.Height <= 0 || r.Width <= 0) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color c = (hot || dragging) ? Palette.Darken(ThumbColor, 0.15f) : ThumbColor;
            using (var b = new SolidBrush(c))
            using (var path = new GraphicsPath())
            {
                int d = Math.Min(r.Width, r.Height);
                path.AddArc(r.Left, r.Top, d, d, 180, 180);
                path.AddArc(r.Left, r.Bottom - d, d, d, 0, 180);
                path.CloseFigure();
                e.Graphics.FillPath(b, path);
            }
        }

        void MoveTo(int thumbTop)
        {
            int range = Height - ThumbHeight;
            if (range <= 0 || Dragged == null) return;
            Dragged(Math.Max(0, Math.Min(1, thumbTop / (double)range)));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!shown || e.Button != MouseButtons.Left) return;
            Rectangle r = ThumbRect;
            dragging = true;
            if (r.Contains(e.Location)) grab = e.Y - r.Top;
            else
            {
                grab = r.Height / 2;
                MoveTo(e.Y - grab);
            }
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragging) MoveTo(e.Y - grab);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            dragging = false;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hot = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hot = false; Invalidate(); }
    }
}
