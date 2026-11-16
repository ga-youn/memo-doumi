using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MemoDoumi
{
    internal static class Palette
    {
        public static readonly string[] Names = { "하양", "노랑", "분홍", "초록", "하늘", "보라", "회색" };
        public static readonly Color[] Body = { C(0xFFFFFF), C(0xFFF4B8), C(0xFFE0EA), C(0xE3F6D8), C(0xDDEFFF), C(0xEDE3FF), C(0xF1F1EF) };
        public static readonly Color[] Head = { C(0xF4F4F6), C(0xFFE680), C(0xFFC2D6), C(0xC7EBB3), C(0xB9DCFF), C(0xD7C6FF), C(0xDCDCD8) };
        public static readonly Color Ink = Color.FromArgb(43, 43, 43);
        public static readonly Color Outline = Color.FromArgb(0x6B, 0x5F, 0x58);
        static readonly Image[] swatches = new Image[Names.Length];

        static Color C(int rgb) { return Color.FromArgb(255, (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255); }

        public static int Clamp(int i) { return Math.Max(0, Math.Min(Names.Length - 1, i)); }

        public static Color Darken(Color c, float f)
        {
            return Color.FromArgb(c.A, (int)(c.R * (1 - f)), (int)(c.G * (1 - f)), (int)(c.B * (1 - f)));
        }

        public static Image Swatch(int i)
        {
            if (swatches[i] == null)
            {
                var bmp = new Bitmap(16, 16);
                using (Graphics g = Graphics.FromImage(bmp))
                using (var b = new SolidBrush(Head[i]))
                using (var p = new Pen(Darken(Head[i], 0.25f)))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.FillEllipse(b, 2, 2, 12, 12);
                    g.DrawEllipse(p, 2, 2, 12, 12);
                }
                swatches[i] = bmp;
            }
            return swatches[i];
        }
    }

    internal class EdgePanel : Panel
    {
        public EdgePanel() { DoubleBuffered = true; }

        protected override void WndProc(ref Message m)
        {
            if (NoteForm.PassEdgeHit(this, ref m)) return;
            base.WndProc(ref m);
        }
    }

    internal partial class IconButton : Label
    {
        static readonly Font IconFont = new Font("Segoe MDL2 Assets", 9f);
        Color normal, hover;
        bool hot, active;

        public IconButton(string glyph)
        {
            Text = glyph;
            Font = IconFont;
            AutoSize = false;
            UseMnemonic = false;
            TextAlign = ContentAlignment.MiddleCenter;
            Cursor = Cursors.Hand;
            Width = App.S(26);
        }

        public bool Active { get { return active; } set { active = value; UpdateBack(); } }

        public void SetColors(Color normal, Color hover, Color fore)
        {
            this.normal = normal;
            this.hover = hover;
            ForeColor = fore;
            UpdateBack();
        }

        void UpdateBack() { BackColor = (hot || active) ? hover : normal; }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hot = true; UpdateBack(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hot = false; UpdateBack(); }

        protected override void WndProc(ref Message m)
        {
            if (NoteForm.PassEdgeHit(this, ref m)) return;
            base.WndProc(ref m);
        }
    }

    internal partial class NoteForm : Form
    {
        // 공유 글꼴은 개별 창에서 Dispose하지 않는다.
        static readonly Font NoteFont = Fonts.Make(10.5f);
        static readonly Font PosFont = Fonts.Make(8.5f);
        static readonly Font TimeFont = Fonts.Make(8f);

        internal long Stamp;

        public readonly NoteData Data;
        readonly App app;
        readonly EdgePanel header, body, footer, formatBar;
        IconButton btnBold, btnItalic, btnUnderline, btnStrike, btnList;
        bool dirty;
        readonly Label timeLabel = new Label();
        readonly Panel clip = new Panel();
        readonly ScrollThumb thumb = new ScrollThumb();
        readonly Panel navBox = new Panel();
        readonly RichTextBox box;
        readonly IconButton btnAdd, btnSearch, btnFirst, btnPrev, btnNext, btnLast, btnColor, btnPin, btnHide, btnDel;
        readonly Label posLabel = new Label();

        readonly ToolTip tips = new ToolTip();
        readonly ContextMenuStrip colorMenu, editMenu;
        bool ready, released;

        public NoteForm(App app, NoteData data)
        {
            this.app = app;
            Data = data;

            SuspendLayout();
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            MaximizeBox = false;
            MinimizeBox = false;
            KeyPreview = true;
            Text = "메모";
            MinimumSize = new Size(App.S(180), App.S(130));
            ResizeRedraw = true;
            Padding = new Padding(Edge, Edge, Edge, Edge + TailH);
            Bounds = new Rectangle(app.NoteScreenPos(data), new Size(data.W, data.H));
            TopMost = data.Pinned;
            hasTail = !data.Pinned;

            header = new EdgePanel();
            header.Dock = DockStyle.Top;
            header.Height = App.S(30);
            header.Padding = new Padding(App.S(3), App.S(3), App.S(3), App.S(2));
            header.MouseDown += OnHeaderMouseDown;

            btnAdd = MakeButton("", "새 메모 (Ctrl+N)", DockStyle.Left, (s, e) => app.NewNote());
            btnSearch = MakeButton("\uE721", "메모 목록 · 검색 (Ctrl+F)", DockStyle.Left, (s, e) => app.OpenSearch());
            btnFirst = MakeButton("\uE892", "처음 메모로 (Alt+Home)", DockStyle.Left, (s, e) => app.GoTo(1));
            btnPrev = MakeButton("\uE76B", "이전 메모 (Alt+←)", DockStyle.Left, (s, e) => app.Step(-1));
            btnNext = MakeButton("\uE76C", "다음 메모 (Alt+→)", DockStyle.Left, (s, e) => app.Step(1));
            btnLast = MakeButton("\uE893", "마지막 메모로 (Alt+End)", DockStyle.Left, (s, e) => app.GoLast());
            btnColor = MakeButton("", "색 바꾸기", DockStyle.Right, (s, e) => ShowColorMenu());
            btnPin = MakeButton("", "", DockStyle.Right, (s, e) => TogglePin());
            btnHide = MakeButton("\uE8BB", "닫기 (지워지지 않아요)", DockStyle.Right, (s, e) => HideNote());
            btnDel = MakeButton("", "메모 삭제", DockStyle.Right, (s, e) => DeleteNote());
            // WinForms 도킹 순서는 컨트롤 추가 순서의 역순이다.
            header.Controls.AddRange(new Control[] { btnSearch, btnAdd, btnColor, btnPin, btnDel, btnHide });

            posLabel.Dock = DockStyle.Left;
            posLabel.Width = App.S(48);
            posLabel.TextAlign = ContentAlignment.MiddleCenter;
            posLabel.Font = PosFont;
            posLabel.UseMnemonic = false;
            tips.SetToolTip(posLabel, "지금 메모 / 전체 메모 수 (고정한 메모 제외)");
            navBox.Height = App.S(22);
            navBox.Controls.AddRange(new Control[] { btnLast, btnNext, posLabel, btnPrev, btnFirst });
            footer = new EdgePanel();
            footer.Dock = DockStyle.Bottom;
            footer.Height = App.S(28);
            footer.Controls.Add(navBox);
            footer.Resize += (s, e) => LayoutNav();

            body = new EdgePanel();
            body.Dock = DockStyle.Fill;
            body.Padding = new Padding(App.S(10), App.S(2), App.S(6), App.S(2));

            box = new RichTextBox();
            box.BorderStyle = BorderStyle.None;
            box.ScrollBars = RichTextBoxScrollBars.ForcedVertical;
            box.VScroll += (s, e) => UpdateThumb();
            box.Resize += (s, e) => UpdateThumb();
            box.DetectUrls = true;
            box.AcceptsTab = true;
            box.Font = NoteFont;
            box.ForeColor = Palette.Ink;
            box.SelectionChanged += (s, e) => UpdateFormatButtons();
            box.HandleCreated += (s, e) => FixImeFont();
            box.TextChanged += OnTextChanged;
            box.KeyDown += OnBoxKeyDown;
            box.LinkClicked += OnLinkClicked;
            editMenu = BuildEditMenu();
            box.ContextMenuStrip = editMenu;
            clip.Dock = DockStyle.Fill;
            clip.Controls.Add(thumb);
            clip.Controls.Add(box);
            clip.Resize += (s, e) => LayoutBox();
            thumb.Dragged = ScrollTo;
            body.Controls.Add(clip);

            formatBar = new EdgePanel();
            formatBar.Dock = DockStyle.Bottom;
            formatBar.Height = App.S(26);
            formatBar.Padding = new Padding(App.S(6), App.S(1), App.S(6), App.S(1));
            btnBold = MakeButton("\uE8DD", "굵게 (Ctrl+B)", DockStyle.Left, (s, e) => ToggleStyle(Markdown.Bold));
            btnItalic = MakeButton("\uE8DB", "기울임 (Ctrl+I)", DockStyle.Left, (s, e) => ToggleStyle(Markdown.Italic));
            btnUnderline = MakeButton("\uE8DC", "밑줄 (Ctrl+U)", DockStyle.Left, (s, e) => ToggleStyle(Markdown.Underline));
            btnStrike = MakeButton("\uEDE0", "취소선 (Ctrl+T)", DockStyle.Left, (s, e) => ToggleStyle(Markdown.Strike));
            btnList = MakeButton("\uE8FD", "글머리 목록 (Ctrl+Shift+L)", DockStyle.Left, (s, e) => ToggleList());
            timeLabel.Dock = DockStyle.Fill;
            timeLabel.TextAlign = ContentAlignment.MiddleRight;
            timeLabel.Font = TimeFont;
            timeLabel.UseMnemonic = false;
            timeLabel.AutoEllipsis = true;
            formatBar.Controls.Add(timeLabel);
            formatBar.Controls.AddRange(new Control[] { btnList, btnStrike, btnUnderline, btnItalic, btnBold });

            Controls.Add(body);
            Controls.Add(formatBar);
            Controls.Add(footer);
            Controls.Add(header);
            ActiveControl = box;

            colorMenu = BuildColorMenu();
            ApplyColors();
            UpdatePinTip();
            footer.Visible = !data.Pinned;
            ResumeLayout();
            Markdown.Fill(box, data);
            ready = true;
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

        protected override bool ShowWithoutActivation { get { return app != null && app.QuietShow; } }

        protected override void Dispose(bool disposing)
        {
            if (!disposing || released)
            {
                base.Dispose(disposing);
                return;
            }
            released = true;
            Region shape = Region;
            tips.Dispose();
            box.ContextMenuStrip = null;
            base.Dispose(true);
            colorMenu.Dispose();
            editMenu.Dispose();
            if (shape != null) shape.Dispose();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            UpdateShape();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            box.Select(box.TextLength, 0);
        }

        IconButton MakeButton(string glyph, string tipText, DockStyle dock, EventHandler onClick)
        {
            var b = new IconButton(glyph);
            b.Dock = dock;
            b.Click += onClick;
            if (tipText.Length > 0) tips.SetToolTip(b, tipText);
            return b;
        }

        void ApplyColors()
        {
            int i = Palette.Clamp(Data.Color);
            Color bodyC = Palette.Body[i], headC = Palette.Head[i];
            BackColor = bodyC;
            header.BackColor = bodyC;
            body.BackColor = bodyC;
            box.BackColor = bodyC;
            Color hover = Palette.Darken(headC, 0.08f);
            Color fore = Palette.Darken(headC, 0.55f);
            foreach (IconButton b in new[] { btnAdd, btnSearch, btnFirst, btnPrev, btnNext, btnLast, btnColor, btnPin, btnHide, btnDel,
                                             btnBold, btnItalic, btnUnderline, btnStrike, btnList })
                b.SetColors(bodyC, hover, fore);
            Invalidate();
            btnPin.Active = Data.Pinned;
            posLabel.BackColor = bodyC;
            posLabel.ForeColor = fore;
            footer.BackColor = bodyC;
            navBox.BackColor = bodyC;
            formatBar.BackColor = bodyC;
            timeLabel.BackColor = bodyC;
            timeLabel.ForeColor = Palette.Darken(bodyC, 0.45f);
            clip.BackColor = bodyC;
            thumb.BackColor = bodyC;
            thumb.ThumbColor = Palette.Darken(bodyC, 0.2f);
            thumb.Invalidate();
            UpdateFormatButtons();
        }

        void LayoutNav()
        {
            navBox.Width = btnFirst.Width * 4 + posLabel.Width;
            navBox.Location = new Point(Math.Max(0, (footer.ClientSize.Width - navBox.Width) / 2), App.S(1));
        }

        public void RefreshPosition()
        {
            string text = app.PositionText(Data);
            posLabel.Text = text;

            posLabel.Width = TextRenderer.MeasureText(text.Length == 0 ? "0 / 0" : text, posLabel.Font).Width + App.S(12);
            LayoutNav();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (released || Disposing) return;
            if (Visible) { RefreshPosition(); RefreshTime(); LayoutBox(); }
            else Commit();
            app.NoteVisibilityChanged(this);
        }

        void UpdatePinTip()
        {
            tips.SetToolTip(btnPin, Data.Pinned ? "고정됨: 이 자리에 계속 떠 있어요 (눌러서 해제)" : "고정: 말풍선에서 떼어 이 자리에 계속 띄워 두기");
        }

        void ShowColorMenu()
        {
            colorMenu.Show(btnColor, new Point(0, btnColor.Height));
        }

        ContextMenuStrip BuildColorMenu()
        {
            var m = new ContextMenuStrip();
            m.Font = App.MenuFont;
            for (int i = 0; i < Palette.Names.Length; i++)
            {
                int idx = i;
                m.Items.Add(new ToolStripMenuItem(Palette.Names[i], Palette.Swatch(i), (s, e) => SetColor(idx)));
            }
            m.Opening += (s, e) =>
            {
                for (int i = 0; i < m.Items.Count; i++)
                    ((ToolStripMenuItem)m.Items[i]).Checked = i == Palette.Clamp(Data.Color);
            };
            return m;
        }

        void SetColor(int i)
        {
            Data.Color = i;
            app.Data.LastColor = i;
            ApplyColors();
            app.ScheduleSave();
        }

        void TogglePin()
        {
            SetPinned(!Data.Pinned);
            app.OnPinChanged(this);
        }

        public void SetPinned(bool value)
        {
            Data.Pinned = value;
            TopMost = value;
            btnPin.Active = value;
            footer.Visible = !value;
            UpdatePinTip();
        }

        public void SelectText(int index, int length)
        {
            box.Focus();
            index = Math.Max(0, Math.Min(index, box.TextLength));
            box.Select(index, Math.Max(0, Math.Min(length, box.TextLength - index)));
            box.ScrollToCaret();
        }

        public void FocusText()
        {
            box.Focus();
            box.Select(box.TextLength, 0);
        }

        void HideNote()
        {
            app.CloseNote(this);
        }

        void DeleteNote()
        {
            if (box.Text.Trim().Length > 0)
            {
                DialogResult r = MessageBox.Show(this, "이 메모를 삭제할까요?\n삭제한 메모는 되돌릴 수 없어요.", "메모 삭제",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
                if (r != DialogResult.Yes) return;
            }
            app.DeleteNote(this);
        }

        void LayoutBox()
        {
            int bar = SystemInformation.VerticalScrollBarWidth, w = App.S(5), gap = App.S(4);
            box.SetBounds(0, 0, clip.Width + bar, clip.Height);
            box.RightMargin = Math.Max(App.S(40), clip.Width - w - gap);
            thumb.SetBounds(clip.Width - w, 0, w, clip.Height);
            UpdateThumb();
        }

        void UpdateThumb()
        {
            if (!box.IsHandleCreated) return;
            var si = new Native.SCROLLINFO();
            si.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.SCROLLINFO));
            si.fMask = Native.SIF_ALL;
            Native.GetScrollInfo(box.Handle, Native.SB_VERT, ref si);
            int total = si.nMax - si.nMin + 1, range = total - si.nPage;
            if (si.nPage <= 0 || range <= 0) thumb.Set(0, 1, false);
            else thumb.Set((si.nPos - si.nMin) / (double)range, si.nPage / (double)total, true);
        }

        void ScrollTo(double fraction)
        {
            var si = new Native.SCROLLINFO();
            si.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.SCROLLINFO));
            si.fMask = Native.SIF_ALL;
            Native.GetScrollInfo(box.Handle, Native.SB_VERT, ref si);
            int range = si.nMax - si.nMin + 1 - si.nPage;
            if (range <= 0) return;
            var p = new Native.POINT(0, si.nMin + (int)Math.Round(fraction * range));
            Native.SendMessage(box.Handle, Native.EM_SETSCROLLPOS, IntPtr.Zero, ref p);
            UpdateThumb();
        }

        public bool Commit()
        {
            if (!dirty || box.IsDisposed) return false;
            Data.Text = Markdown.FromRich(box);
            Data.Md = true;
            Data.ModifiedUtc = DateTime.UtcNow.Ticks;
            dirty = false;
            RefreshTime();
            return true;
        }

        public void RefreshTime()
        {
            timeLabel.Text = "수정 " + App.ShortTime(Data.ModifiedUtc);
            tips.SetToolTip(timeLabel, "등록  " + App.FullTime(Data.CreatedUtc) + "\n수정  " + App.FullTime(Data.ModifiedUtc));
        }

        void ToggleStyle(int flag)
        {
            Markdown.Toggle(box, flag);
            AfterFormat();
        }

        void ToggleList()
        {
            box.BulletIndent = App.S(14);
            box.SelectionBullet = !box.SelectionBullet;
            AfterFormat();
        }

        void AfterFormat()
        {
            box.Focus();
            UpdateFormatButtons();
            if (!ready) return;
            dirty = true;               // 서식만 바꾸면 TextChanged 가 오지 않으므로 직접 표시한다
            app.ScheduleSave();
        }

        void UpdateFormatButtons()
        {
            if (btnBold == null || !box.IsHandleCreated) return;
            btnBold.Active = Markdown.SelectionHas(box, Markdown.Bold);
            btnItalic.Active = Markdown.SelectionHas(box, Markdown.Italic);
            btnUnderline.Active = Markdown.SelectionHas(box, Markdown.Underline);
            btnStrike.Active = Markdown.SelectionHas(box, Markdown.Strike);
            btnList.Active = box.SelectionBullet;
        }

        void OnTextChanged(object sender, EventArgs e)
        {
            UpdateThumb();
            if (!ready) return;
            dirty = true;
            app.ScheduleSave();
        }

        void SyncBounds()
        {
            if (!ready || WindowState != FormWindowState.Normal) return;
            Data.W = Width; Data.H = Height;
            if (!app.Positioning)
            {
                if (Data.Pinned)
                {
                    Data.Anchored = false;
                    Data.X = Left; Data.Y = Top;
                }
                else
                {
                    Point dog = app.DogPos;                  // 말풍선: 강아지 기준
                    Data.Anchored = true;
                    Data.X = Left - dog.X; Data.Y = Top - dog.Y;
                }
            }
            app.ScheduleSave();
        }

        protected override void OnLocationChanged(EventArgs e)
        {
            base.OnLocationChanged(e);
            if (hasTail && TailTip() != shownTip) UpdateShape();
            SyncBounds();
        }
        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); UpdateShape(); SyncBounds(); }

        static int Edge { get { return Bubble.Edge; } }
        static int TailH { get { return Bubble.TailH; } }

        bool hasTail = true;
        public bool HasTail
        {
            get { return hasTail; }
            set { if (hasTail != value) { hasTail = value; UpdateShape(); } }
        }

        int shownTip = int.MinValue;

        int TailTip() { return app.DogCenterX - Left; }

        GraphicsPath BubblePath()
        {
            return Bubble.Path(ClientSize.Width, ClientSize.Height, hasTail, TailTip());
        }

        void UpdateShape()
        {
            if (!IsHandleCreated || ClientSize.Width < 10 || ClientSize.Height < 10) return;
            shownTip = TailTip();
            using (GraphicsPath p = BubblePath())
            {
                Region old = Region;
                Region = new Region(p);
                if (old != null) old.Dispose();
            }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            // Region이 바깥 절반을 잘라내므로 테두리를 두 배 굵기로 그린다.
            using (GraphicsPath p = BubblePath())
            using (var pen = new Pen(Palette.Outline, App.S(2) * 2))
                e.Graphics.DrawPath(pen, p);
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            app.LastActive = this;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {

            if (e.CloseReason == CloseReason.UserClosing && !app.Exiting)
            {
                e.Cancel = true;
                HideNote();
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Control && !e.Alt && e.KeyCode == Keys.N)
            {
                e.Handled = e.SuppressKeyPress = true;
                app.NewNote();
                return;
            }
            if (e.Control && !e.Alt && e.KeyCode == Keys.F)
            {
                e.Handled = e.SuppressKeyPress = true;
                app.OpenSearch();
                return;
            }
            if (e.Alt && !e.Control && !Data.Pinned && (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right))
            {
                e.Handled = e.SuppressKeyPress = true;
                app.Step(e.KeyCode == Keys.Left ? -1 : 1);
                return;
            }
            if (e.Alt && !e.Control && !Data.Pinned && (e.KeyCode == Keys.Home || e.KeyCode == Keys.End))
            {
                e.Handled = e.SuppressKeyPress = true;
                if (e.KeyCode == Keys.Home) app.GoTo(1); else app.GoLast();
                return;
            }
            if (e.KeyCode == Keys.Escape)
            {
                e.Handled = e.SuppressKeyPress = true;
                if (!Data.Pinned) app.HideAll();
                return;
            }
            base.OnKeyDown(e);
        }

        void OnBoxKeyDown(object sender, KeyEventArgs e)
        {

            if (e.Control && !e.Alt)
            {
                int flag = 0;
                if (!e.Shift && e.KeyCode == Keys.B) flag = Markdown.Bold;
                else if (!e.Shift && e.KeyCode == Keys.I) flag = Markdown.Italic;
                else if (!e.Shift && e.KeyCode == Keys.U) flag = Markdown.Underline;
                else if (!e.Shift && e.KeyCode == Keys.T) flag = Markdown.Strike;
                if (flag != 0)
                {
                    e.Handled = e.SuppressKeyPress = true;
                    ToggleStyle(flag);
                    return;
                }
                if (e.Shift && e.KeyCode == Keys.L)
                {
                    e.Handled = e.SuppressKeyPress = true;
                    ToggleList();
                    return;
                }
            }

            if ((e.Control && !e.Alt && e.KeyCode == Keys.V) || (e.Shift && e.KeyCode == Keys.Insert))
            {
                e.Handled = e.SuppressKeyPress = true;
                PastePlain();
                return;
            }

            if (e.Control && !e.Alt)
            {
                switch (e.KeyCode)
                {
                    case Keys.L: case Keys.R: case Keys.E: case Keys.J:
                    case Keys.D1: case Keys.D2: case Keys.D5:
                        e.Handled = e.SuppressKeyPress = true;
                        break;
                }
            }
        }

        void PastePlain()
        {
            try
            {
                if (Clipboard.ContainsText()) box.SelectedText = Clipboard.GetText();
            }
            catch { }
        }

        void OnLinkClicked(object sender, LinkClickedEventArgs e)
        {

            string url = LinkPolicy.SafeUrl(e.LinkText);
            if (url == null)
            {
                string shown = e.LinkText ?? "";
                if (shown.Length > 80) shown = shown.Substring(0, 80) + "…";
                app.Notify(Notice.LinkKey, "보안을 위해 http, https 로 시작하는 웹 주소만 엽니다.\n열지 않은 링크: " + shown);
                return;
            }
            try { Process.Start(url); }
            catch (Exception ex) { App.Log(ex); }
        }

        void FixImeFont()
        {

            long opts = Native.SendMessage(box.Handle, Native.EM_GETLANGOPTIONS, IntPtr.Zero, IntPtr.Zero).ToInt64();
            opts &= ~(long)(Native.IMF_AUTOFONT | Native.IMF_DUALFONT);
            Native.SendMessage(box.Handle, Native.EM_SETLANGOPTIONS, IntPtr.Zero, new IntPtr(opts));
        }

        ContextMenuStrip BuildEditMenu()
        {
            var m = new ContextMenuStrip();
            m.Font = App.MenuFont;
            var undo = new ToolStripMenuItem("실행 취소", null, (s, e) => box.Undo());
            var cut = new ToolStripMenuItem("잘라내기", null, (s, e) => box.Cut());
            var copy = new ToolStripMenuItem("복사", null, (s, e) => box.Copy());
            var paste = new ToolStripMenuItem("붙여넣기", null, (s, e) => PastePlain());
            var all = new ToolStripMenuItem("모두 선택", null, (s, e) => box.SelectAll());
            m.Items.AddRange(new ToolStripItem[] { undo, new ToolStripSeparator(), cut, copy, paste, new ToolStripSeparator(), all });
            m.Opening += (s, e) =>
            {
                undo.Enabled = box.CanUndo;
                cut.Enabled = copy.Enabled = box.SelectionLength > 0;
                bool hasText = false;
                try { hasText = Clipboard.ContainsText(); } catch { }
                paste.Enabled = hasText;
            };
            return m;
        }

        void OnHeaderMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            Native.ReleaseCapture();
            Native.SendMessage(Handle, Native.WM_NCLBUTTONDOWN, (IntPtr)Native.HTCAPTION, IntPtr.Zero);
        }

        int EdgeHit(Point p)
        {
            int g = App.S(6), c = App.S(12);
            int w = ClientSize.Width, h = ClientSize.Height - TailH;
            if (p.Y >= h) return 0;
            if (p.X >= w - c && p.Y >= h - c && (p.X >= w - g || p.Y >= h - g)) return Native.HTBOTTOMRIGHT;
            bool l = p.X < g, r = p.X >= w - g, t = p.Y < g, b = p.Y >= h - g;
            if (t && l) return Native.HTTOPLEFT;
            if (t && r) return Native.HTTOPRIGHT;
            if (b && l) return Native.HTBOTTOMLEFT;
            if (b && r) return Native.HTBOTTOMRIGHT;
            if (l) return Native.HTLEFT;
            if (r) return Native.HTRIGHT;
            if (t) return Native.HTTOP;
            if (b) return Native.HTBOTTOM;
            return 0;
        }

        public static bool PassEdgeHit(Control c, ref Message m)
        {
            if (m.Msg != Native.WM_NCHITTEST) return false;
            var f = c.FindForm() as NoteForm;
            if (f == null) return false;
            if (f.EdgeHit(f.PointToClient(Native.PointFromLParam(m.LParam))) == 0) return false;
            m.Result = (IntPtr)Native.HTTRANSPARENT;
            return true;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_NCHITTEST)
            {
                base.WndProc(ref m);
                Point pt = PointToClient(Native.PointFromLParam(m.LParam));
                int ht = EdgeHit(pt);
                if (ht == 0 && pt.Y >= ClientSize.Height - TailH) ht = Native.HTCAPTION;
                if (ht != 0) m.Result = (IntPtr)ht;
                return;
            }
            base.WndProc(ref m);
        }
    }
}
