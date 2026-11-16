using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MemoDoumi
{

    internal static class Bubble
    {
        public static int Edge { get { return App.S(6); } }
        public static int TailH { get { return App.S(16); } }

        public static GraphicsPath Path(int w, int h, bool tail)
        {
            return Path(w, h, tail, w - App.S(36));
        }

        public static GraphicsPath Path(int w, int h, bool tail, int tipX)
        {
            int bh = h - TailH;
            int d = App.S(12) * 2, r = d / 2;
            int near = App.S(8), far = App.S(30);
            int t1, t2;
            if (tipX >= w / 2)
            {
                tipX = Math.Max(r + far, Math.Min(tipX, w - App.S(4)));
                t2 = Math.Min(tipX - near, w - r);
                t1 = t2 - (far - near);
            }
            else
            {
                tipX = Math.Min(w - r - far, Math.Max(tipX, App.S(4)));
                t1 = Math.Max(tipX + near, r);
                t2 = t1 + (far - near);
            }
            var p = new GraphicsPath();
            p.AddArc(0, 0, d, d, 180, 90);
            p.AddArc(w - d, 0, d, d, 270, 90);
            p.AddArc(w - d, bh - d, d, d, 0, 90);
            if (tail)
            {
                p.AddLine(w - r, bh, t2, bh);
                p.AddLine(t2, bh, tipX, h);
                p.AddLine(tipX, h, t1, bh);
                p.AddLine(t1, bh, r, bh);
            }
            else p.AddLine(w - r, bh, r, bh);
            p.AddArc(0, bh - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    internal class VirtualList : ListBox
    {
        const int LBS_HASSTRINGS = 0x40, LBS_NODATA = 0x2000;
        const int LB_SETCURSEL = 0x186, LB_GETCURSEL = 0x188, LB_SETCOUNT = 0x1A7;
        int count;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.Style = (cp.Style & ~LBS_HASSTRINGS) | LBS_NODATA;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.SendMessage(Handle, LB_SETCOUNT, (IntPtr)count, IntPtr.Zero);
        }

        public int Count
        {
            get { return count; }
            set
            {
                count = Math.Max(0, value);
                if (IsHandleCreated) Native.SendMessage(Handle, LB_SETCOUNT, (IntPtr)count, IntPtr.Zero);
            }
        }

        public int Current
        {
            get
            {
                if (!IsHandleCreated) return -1;
                int i = (int)Native.SendMessage(Handle, LB_GETCURSEL, IntPtr.Zero, IntPtr.Zero).ToInt64();
                return i >= 0 && i < count ? i : -1;
            }
            set { if (IsHandleCreated) Native.SendMessage(Handle, LB_SETCURSEL, (IntPtr)value, IntPtr.Zero); }
        }
    }

    internal class SearchForm : Form
    {
        class Hit
        {
            public NoteData Note;
            public int Index, Length;
            public string Label;
            public int MarkStart, MarkLength;   // Label 안에서 강조할 범위
        }

        const int SlowMs = 12;
        const int DelayMs = 120;

        static readonly Color Line = Color.FromArgb(226, 220, 214);
        static readonly Color Selected = Color.FromArgb(244, 240, 235);
        static readonly Color Mark = Color.FromArgb(255, 230, 128);
        static readonly Color Dim = Color.FromArgb(140, 134, 128);

        readonly App app;
        readonly TextBox input = new TextBox();
        readonly Label hint = new Label();
        readonly VirtualList list = new VirtualList();
        readonly List<Hit> hits = new List<Hit>();
        readonly Timer delay = new Timer();
        readonly ToolTip tips = new ToolTip();
        readonly Font listFont = Fonts.Make(9.5f);
        readonly Font tagFont = Fonts.Make(8f);
        double lastCostMs;
        bool pending;

        public SearchForm(App app)
        {
            this.app = app;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            TopMost = true;
            KeyPreview = true;
            ResizeRedraw = true;
            Text = "메모 검색";
            BackColor = Color.White;
            Padding = new Padding(Bubble.Edge, Bubble.Edge, Bubble.Edge, Bubble.Edge + Bubble.TailH);
            Size = new Size(App.S(300), App.S(290));

            var inner = new Panel();
            inner.Dock = DockStyle.Fill;
            inner.BackColor = Color.White;
            inner.Padding = new Padding(App.S(8), App.S(6), App.S(8), App.S(4));

            var row = new Panel();
            row.Dock = DockStyle.Top;
            row.Height = App.S(30);
            var icon = new Label();
            icon.Text = "";
            icon.Font = new Font("Segoe MDL2 Assets", 10f);
            icon.ForeColor = Dim;
            icon.Dock = DockStyle.Left;
            icon.Width = App.S(26);
            icon.TextAlign = ContentAlignment.MiddleCenter;
            var holder = new Panel();
            holder.Dock = DockStyle.Fill;
            holder.Padding = new Padding(App.S(2), App.S(5), 0, 0);
            input.BorderStyle = BorderStyle.None;
            input.Dock = DockStyle.Fill;
            input.Font = Fonts.Make(11f);
            input.TextChanged += (s, e) => OnQueryChanged();
            delay.Interval = DelayMs;
            delay.Tick += (s, e) => FlushPending();
            input.KeyDown += OnInputKeyDown;
            holder.Controls.Add(input);

            var back = new Label();
            back.Text = "\uE72B";
            back.Font = new Font("Segoe MDL2 Assets", 10f);
            back.ForeColor = Dim;
            back.Dock = DockStyle.Left;
            back.Width = App.S(28);
            back.TextAlign = ContentAlignment.MiddleCenter;
            back.Cursor = Cursors.Hand;
            back.Click += (s, e) => Cancel(true);
            back.MouseEnter += (s, e) => back.BackColor = Selected;
            back.MouseLeave += (s, e) => back.BackColor = Color.White;
            tips.SetToolTip(back, "보던 메모로 돌아가기 (Esc)");
            row.Controls.Add(holder);
            row.Controls.Add(icon);
            row.Controls.Add(back);

            var underline = new Panel();
            underline.Dock = DockStyle.Top;
            underline.Height = Math.Max(1, App.S(1));
            underline.BackColor = Line;

            hint.Dock = DockStyle.Top;
            hint.Height = App.S(24);
            hint.TextAlign = ContentAlignment.MiddleLeft;
            hint.Font = Fonts.Make(8.5f);
            hint.ForeColor = Dim;

            list.Dock = DockStyle.Fill;
            list.BorderStyle = BorderStyle.None;
            list.IntegralHeight = false;
            list.DrawMode = DrawMode.OwnerDrawFixed;
            list.ItemHeight = App.S(28);
            list.Font = listFont;
            list.DrawItem += OnDrawItem;
            list.MouseClick += (s, e) => { if (list.IndexFromPoint(e.Location) >= 0) OpenSelected(); };
            list.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.Handled = true; OpenSelected(); } };

            inner.Controls.Add(list);
            inner.Controls.Add(hint);
            inner.Controls.Add(underline);
            inner.Controls.Add(row);
            Controls.Add(inner);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= Native.CS_DROPSHADOW;
                return cp;
            }
        }

        void UpdateShape()
        {
            if (!IsHandleCreated) return;
            using (GraphicsPath p = Bubble.Path(ClientSize.Width, ClientSize.Height, true, app.DogCenterX - Left))
            {
                Region old = Region;
                Region = new Region(p);
                if (old != null) old.Dispose();
            }
            Invalidate();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            UpdateShape();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (GraphicsPath p = Bubble.Path(ClientSize.Width, ClientSize.Height, true, app.DogCenterX - Left))
            using (var pen = new Pen(Palette.Outline, App.S(2) * 2))
                e.Graphics.DrawPath(pen, p);
        }

        bool closing;

        public void Popup(Rectangle bounds)
        {
            int w = Math.Max(bounds.Width, App.S(250)), h = Math.Max(bounds.Height, App.S(250));
            Bounds = new Rectangle(bounds.Right - w, bounds.Bottom - h, w, h);
            UpdateShape();
            Show();
            Activate();
            input.Focus();
            input.SelectAll();
            Refill();
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (closing || !Visible) return;
            Finish(() => app.SearchClosed(false));
        }

        void Finish(Action after)
        {
            delay.Stop();
            pending = false;
            closing = true;
            Hide();
            closing = false;
            after();
        }

        public void Cancel(bool activate)
        {
            if (!Visible) return;
            Finish(() => app.SearchClosed(activate));
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                e.Handled = e.SuppressKeyPress = true;
                Cancel(true);
                return;
            }
            base.OnKeyDown(e);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && !app.Exiting)
            {
                e.Cancel = true;
                Cancel(true);
                return;
            }
            base.OnFormClosing(e);
        }

        void OnInputKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Up)
            {
                e.Handled = e.SuppressKeyPress = true;
                FlushPending();
                if (hits.Count == 0) return;
                int i = list.Current + (e.KeyCode == Keys.Down ? 1 : -1);
                list.Current = Math.Max(0, Math.Min(hits.Count - 1, i));
            }
            else if (e.KeyCode == Keys.Enter)
            {
                e.Handled = e.SuppressKeyPress = true;
                FlushPending();
                OpenSelected();
            }
        }

        void OnQueryChanged()
        {
            if (lastCostMs < SlowMs)
            {
                Refill();
                return;
            }
            pending = true;
            delay.Stop();
            delay.Start();
        }

        public void FlushPending()
        {
            delay.Stop();
            if (!pending) return;
            Refill();
        }

        void Refill()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            pending = false;
            delay.Stop();
            string q = input.Text.Trim();
            int total = 0;
            hits.Clear();
            foreach (NoteData n in app.NotesNewestFirst())
            {
                total++;
                int at = 0;
                if (q.Length > 0)
                {
                    at = Markdown.Plain(n).IndexOf(q, StringComparison.OrdinalIgnoreCase);
                    if (at < 0) continue;
                }
                var h = new Hit();
                h.Note = n;
                h.Index = at;
                h.Length = q.Length;
                hits.Add(h);
            }
            list.Count = hits.Count;
            if (hits.Count > 0) list.Current = 0;
            list.Invalidate();

            if (q.Length == 0) hint.Text = "메모 " + total + "개  ·  글자를 입력하면 내용에서 찾아요";
            else if (hits.Count == 0) hint.Text = "찾는 메모가 없어요";
            else hint.Text = hits.Count + "개 찾음  ·  ↑↓ 로 고르고 Enter 로 열기";
            lastCostMs = sw.Elapsed.TotalMilliseconds;
        }

        static void MakeLabel(Hit h, string text)
        {
            if (text.Trim().Length == 0)
            {
                h.Label = "(빈 메모)";
                return;
            }
            int start = Math.Max(0, h.Index - 12);
            if (h.Length == 0)
            {

                while (start < text.Length && char.IsWhiteSpace(text[start])) start++;
            }
            int len = Math.Min(70, text.Length - start);
            string s = text.Substring(start, len).Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ');
            string head = start > 0 ? "…" : "";
            h.Label = head + s + (start + len < text.Length ? "…" : "");
            if (h.Length > 0)
            {
                h.MarkStart = head.Length + (h.Index - start);
                h.MarkLength = Math.Min(h.Length, h.Label.Length - h.MarkStart);
            }
        }

        void OnDrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= hits.Count) return;
            Hit h = hits[e.Index];
            if (h.Label == null) MakeLabel(h, Markdown.Plain(h.Note));
            Graphics g = e.Graphics;
            Rectangle r = e.Bounds;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            using (var back = new SolidBrush(selected ? Selected : Color.White)) g.FillRectangle(back, r);

            int c = Palette.Clamp(h.Note.Color);
            int dot = App.S(10);
            var dotRect = new Rectangle(r.Left + App.S(6), r.Top + (r.Height - dot) / 2, dot, dot);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(Palette.Head[c])) g.FillEllipse(b, dotRect);
            using (var p = new Pen(Palette.Darken(Palette.Head[c], 0.25f))) g.DrawEllipse(p, dotRect);
            g.SmoothingMode = SmoothingMode.None;

            int right = r.Right - App.S(4);
            string tagText = (h.Note.Pinned ? "고정 · " : "") + App.ShortTime(h.Note.ModifiedUtc).Replace("오늘 ", "");
            if (tagText.Length > 0)
            {
                Size tag = TextRenderer.MeasureText(g, tagText, tagFont);
                TextRenderer.DrawText(g, tagText, tagFont, new Point(right - tag.Width, r.Top + (r.Height - tag.Height) / 2), Dim);
                right -= tag.Width + App.S(4);
            }

            const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
            int x = dotRect.Right + App.S(8);
            Color ink = Palette.Ink;
            string[] parts = h.MarkLength > 0
                ? new[] { h.Label.Substring(0, h.MarkStart), h.Label.Substring(h.MarkStart, h.MarkLength), h.Label.Substring(h.MarkStart + h.MarkLength) }
                : new[] { h.Label, "", "" };
            for (int i = 0; i < 3 && x < right; i++)
            {
                if (parts[i].Length == 0) continue;
                Size size = TextRenderer.MeasureText(g, parts[i], listFont, new Size(int.MaxValue, r.Height), flags);
                var cell = new Rectangle(x, r.Top, Math.Min(size.Width, right - x), r.Height);
                if (i == 1)
                {
                    using (var mark = new SolidBrush(Mark))
                        g.FillRectangle(mark, cell.Left, r.Top + App.S(5), cell.Width, r.Height - App.S(10));
                }
                TextRenderer.DrawText(g, parts[i], listFont, cell, i == 1 ? Palette.Ink : ink,
                    flags | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                x += size.Width;
            }
        }

        void OpenSelected()
        {
            int i = list.Current;
            if (i < 0 || i >= hits.Count) return;
            Hit h = hits[i];
            Finish(() => app.ShowNote(h.Note, h.Index, h.Length));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                delay.Dispose();
                tips.Dispose();
            }
            base.Dispose(disposing);
            if (disposing)
            {
                listFont.Dispose();
                tagFont.Dispose();
            }
        }
    }
}
