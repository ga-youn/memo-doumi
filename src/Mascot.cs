using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace MemoDoumi
{

    internal class Mascot : Form
    {
        enum Mode { Idle, Walk, Sleep }

        readonly App app;
        readonly Sprites sprites;
        readonly int pw, ph;
        IntPtr memDc, dib, dibBits, oldObj;

        readonly Timer animTimer = new Timer();
        readonly Timer thinkTimer = new Timer();
        readonly Random rnd = new Random();

        Mode mode = Mode.Idle;
        bool faceLeft, hovering;
        int baseX, baseY, jumpOff;
        int targetX, walkTick;
        int jumpFrame = -1, wagLeft, blinkLeft;
        Pose shownPose = (Pose)(-1);
        bool shownLeft;

        bool mouseDown, dragging, ignoreClick;
        Point downScreen, downLoc;
        IntPtr fgAtDown;
        bool snappedBottom;

        readonly ContextMenuStrip menu;
        readonly ToolTip tip = new ToolTip();
        readonly int showMsg, quitMsg;
        readonly uint myPid;
        Native.WinEventDelegate winEventProc;
        IntPtr winEventHook;
        bool hiddenForFullscreen;

        public Mascot(App app)
        {
            this.app = app;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            TopMost = true;
            Text = App.MascotTitle;
            Cursor = Cursors.Hand;

            sprites = Sprites.Load(Math.Max(1, (int)Math.Round(2 * App.Scale)));
            pw = sprites.W;
            ph = sprites.H;
            Size = new Size(pw, ph);

            showMsg = Native.RegisterWindowMessage(App.ShowMessageName);
            quitMsg = Native.RegisterWindowMessage(App.QuitMessageName);
            myPid = (uint)Process.GetCurrentProcess().Id;

            animTimer.Interval = 60;
            animTimer.Tick += OnAnimTick;
            thinkTimer.Tick += OnThink;

            menu = BuildMenu();
            tip.ShowAlways = true;
            tip.InitialDelay = 900;
            tip.SetToolTip(this, "클릭: 메모 열기/닫기\n우클릭: 메뉴\nCtrl+Alt+N: 새 메모\nCtrl+Alt+F: 메모 검색");
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_TOPMOST | Native.WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        Rectangle WorkArea()
        {
            return Screen.FromPoint(new Point(baseX + pw / 2, baseY + ph / 2)).WorkingArea;
        }

        public Point DogPos { get { return new Point(baseX, baseY); } }
        public Rectangle DogWorkArea { get { return WorkArea(); } }
        public void Halt() { StopWalk(); }

        void ApplyLocation()
        {
            var p = new Point(baseX, baseY - jumpOff);
            if (Location != p) Location = p;
        }

        void SetDefaultPosition()
        {
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            baseX = wa.Right - pw - App.S(24);
            baseY = wa.Bottom - ph;
            snappedBottom = true;
            ApplyLocation();
        }

        public void PlaceInitial()
        {
            AppData d = app.Data;
            if (d.HasMascotPos)
            {
                var center = new Point(d.MascotX + pw / 2, d.MascotY + ph / 2);
                foreach (Screen s in Screen.AllScreens)
                {
                    if (s.WorkingArea.Contains(center))
                    {
                        baseX = d.MascotX;
                        baseY = d.MascotY;
                        SnapAndClamp();
                        return;
                    }
                }
            }
            SetDefaultPosition();
        }

        void SnapAndClamp()
        {
            Rectangle wa = WorkArea();
            baseX = Math.Max(wa.Left, Math.Min(baseX, wa.Right - pw));
            baseY = Math.Max(wa.Top, Math.Min(baseY, wa.Bottom - ph));
            snappedBottom = wa.Bottom - (baseY + ph) < App.S(48);
            if (snappedBottom) baseY = wa.Bottom - ph;
            ApplyLocation();
        }

        void Reflow()
        {
            if (snappedBottom) baseY = WorkArea().Bottom - ph;
            SnapAndClamp();
            app.PlaceNotes();
        }

        void ResetPosition()
        {
            StopWalk();
            SetDefaultPosition();
            app.PlaceNotes();
            app.SaveMascotPos(new Point(baseX, baseY));
        }

        public Point SuggestNotePos(int w, int h)
        {
            Rectangle wa = WorkArea();
            int x = baseX + pw / 2 + App.S(36) - w;
            int y = baseY - App.S(2) - h;
            x = Math.Max(wa.Left + App.S(8), Math.Min(x, wa.Right - w - App.S(8)));
            y = Math.Max(wa.Top + App.S(8), Math.Min(y, wa.Bottom - h - App.S(8)));
            return new Point(x, y);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            var bmi = new Native.BITMAPINFOHEADER();
            bmi.biSize = Marshal.SizeOf(typeof(Native.BITMAPINFOHEADER));
            bmi.biWidth = pw;
            bmi.biHeight = -ph;
            bmi.biPlanes = 1;
            bmi.biBitCount = 32;
            IntPtr screen = Native.GetDC(IntPtr.Zero);
            memDc = Native.CreateCompatibleDC(screen);
            dib = Native.CreateDIBSection(screen, ref bmi, 0, out dibBits, IntPtr.Zero, 0);
            oldObj = Native.SelectObject(memDc, dib);
            Native.ReleaseDC(IntPtr.Zero, screen);

            UpdatePose(true);

            uint mods = Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT;
            Native.RegisterHotKey(Handle, 1, mods, (uint)Keys.N);
            Native.RegisterHotKey(Handle, 2, mods, (uint)Keys.M);
            Native.RegisterHotKey(Handle, 3, mods, (uint)Keys.F);

            winEventProc = OnWinEvent;
            winEventHook = Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero, winEventProc, 0, 0, Native.WINEVENT_OUTOFCONTEXT);

            ScheduleThink(3000, 6000);
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            animTimer.Stop();
            thinkTimer.Stop();
            if (winEventHook != IntPtr.Zero) { Native.UnhookWinEvent(winEventHook); winEventHook = IntPtr.Zero; }
            Native.UnregisterHotKey(Handle, 1);
            Native.UnregisterHotKey(Handle, 2);
            Native.UnregisterHotKey(Handle, 3);
            if (memDc != IntPtr.Zero)
            {
                Native.SelectObject(memDc, oldObj);
                Native.DeleteObject(dib);
                Native.DeleteDC(memDc);
                memDc = IntPtr.Zero;
            }
            base.OnHandleDestroyed(e);
        }

        void UpdatePose(bool force)
        {
            Pose p;
            if (mode == Mode.Sleep) p = Pose.Sleep;
            else if (mode == Mode.Walk) p = (walkTick / 3) % 2 == 0 ? Pose.Walk1 : Pose.Walk2;
            else if (jumpFrame >= 0 || hovering) p = Pose.Happy;
            else if (wagLeft > 0) p = (wagLeft / 3) % 2 == 0 ? Pose.Wag : Pose.Stand;
            else if (blinkLeft > 0) p = Pose.Blink;
            else p = Pose.Stand;

            if (!force && p == shownPose && faceLeft == shownLeft) return;
            if (memDc == IntPtr.Zero) return;
            shownPose = p;
            shownLeft = faceLeft;

            byte[] px = sprites.Get(p, faceLeft);
            Marshal.Copy(px, 0, dibBits, px.Length);
            var dst = new Native.POINT(Left, Top);
            var size = new Native.SIZE(pw, ph);
            var src = new Native.POINT(0, 0);
            var bf = new Native.BLENDFUNCTION();
            bf.SourceConstantAlpha = 255;
            bf.AlphaFormat = 1;
            Native.UpdateLayeredWindow(Handle, IntPtr.Zero, ref dst, ref size, memDc, ref src, 0, ref bf, Native.ULW_ALPHA);
        }

        bool Busy { get { return hovering || mouseDown || menu.Visible; } }

        void ScheduleThink(int minMs, int maxMs)
        {
            thinkTimer.Stop();
            thinkTimer.Interval = rnd.Next(minMs, maxMs);
            thinkTimer.Start();
        }

        void OnThink(object sender, EventArgs e)
        {
            thinkTimer.Stop();
            if (!Visible) return;

            if (mode == Mode.Sleep)
            {
                mode = Mode.Idle;
                wagLeft = 12;
                animTimer.Start();
                UpdatePose(false);
                ScheduleThink(3000, 6000);
                return;
            }
            if (Busy || mode == Mode.Walk)
            {
                ScheduleThink(3000, 6000);
                return;
            }

            int roll = rnd.Next(100);
            if (roll < 40)
            {
                blinkLeft = 2;
                animTimer.Start();
                ScheduleThink(3000, 7000);
            }
            else if (roll < 60)
            {
                wagLeft = 18;
                animTimer.Start();
                ScheduleThink(4000, 8000);
            }
            else if (roll < 90 && !app.Data.StayPut && !app.KeepStill)
            {
                StartWalk();
            }
            else if (roll >= 90)
            {
                mode = Mode.Sleep;
                ScheduleThink(20000, 60000);
            }
            else
            {
                ScheduleThink(3000, 7000);
            }
            UpdatePose(false);
        }

        void StartWalk()
        {
            Rectangle wa = WorkArea();
            int min = wa.Left, max = wa.Right - pw;
            int dist = rnd.Next(App.S(80), App.S(420)) * (rnd.Next(2) == 0 ? -1 : 1);
            int t = Math.Max(min, Math.Min(baseX + dist, max));
            if (Math.Abs(t - baseX) < App.S(40)) t = Math.Max(min, Math.Min(baseX - dist, max));
            if (t == baseX) { ScheduleThink(3000, 6000); return; }
            targetX = t;
            faceLeft = targetX < baseX;
            walkTick = 0;
            mode = Mode.Walk;
            animTimer.Start();
        }

        void StopWalk()
        {
            if (mode != Mode.Walk) return;
            mode = Mode.Idle;
            ScheduleThink(4000, 12000);
        }

        void Wake()
        {
            if (mode != Mode.Sleep) return;
            mode = Mode.Idle;
            ScheduleThink(4000, 8000);
        }

        void OnAnimTick(object sender, EventArgs e)
        {
            bool active = false;

            if (jumpFrame >= 0)
            {
                jumpFrame++;
                if (jumpFrame > 8) { jumpFrame = -1; jumpOff = 0; }
                else { jumpOff = (int)(Math.Sin(jumpFrame / 8.0 * Math.PI) * App.S(14)); active = true; }
            }

            if (mode == Mode.Walk)
            {
                if (Busy || app.KeepStill) StopWalk();
                else
                {
                    int step = Math.Max(1, App.S(2));
                    int dx = targetX - baseX;
                    walkTick++;
                    if (Math.Abs(dx) <= step) { baseX = targetX; StopWalk(); }
                    else { baseX += Math.Sign(dx) * step; active = true; }
                }
            }

            if (wagLeft > 0) { wagLeft--; active = true; }
            if (blinkLeft > 0) { blinkLeft--; active = true; }

            if (!dragging) ApplyLocation();
            UpdatePose(false);
            if (!active) animTimer.Stop();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            hovering = true;
            Wake();
            StopWalk();
            wagLeft = 12;
            animTimer.Start();
            UpdatePose(false);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hovering = false;
            UpdatePose(false);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            StopWalk();
            mouseDown = true;
            dragging = false;
            ignoreClick = e.Clicks > 1;
            downScreen = Cursor.Position;
            downLoc = new Point(baseX, baseY);
            fgAtDown = Native.GetForegroundWindow();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!mouseDown) return;
            Point p = Cursor.Position;
            int dx = p.X - downScreen.X, dy = p.Y - downScreen.Y;
            if (!dragging && (Math.Abs(dx) > App.S(4) || Math.Abs(dy) > App.S(4)))
            {
                dragging = true;
                tip.Hide(this);
            }
            if (dragging)
            {
                baseX = downLoc.X + dx;
                baseY = downLoc.Y + dy;
                ApplyLocation();
                app.PlaceNotes();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left && mouseDown)
            {
                mouseDown = false;
                if (dragging)
                {
                    dragging = false;
                    SnapAndClamp();
                    app.PlaceNotes();
                    app.SaveMascotPos(new Point(baseX, baseY));
                }
                else if (!ignoreClick)
                {
                    jumpFrame = 0;
                    animTimer.Start();
                    UpdatePose(false);
                    app.Toggle(fgAtDown);
                }
            }
            else if (e.Button == MouseButtons.Right)
            {
                Native.SetForegroundWindow(Handle);
                menu.Show(this, e.Location);
            }
        }

        ContextMenuStrip BuildMenu()
        {
            var m = new ContextMenuStrip();
            m.Font = App.MenuFont;

            var miNew = new ToolStripMenuItem("새 메모", null, (s, e) => app.NewNote());
            miNew.ShortcutKeyDisplayString = "Ctrl+Alt+N";
            var miToggle = new ToolStripMenuItem("메모 보이기", null, (s, e) => app.Toggle(IntPtr.Zero));
            miToggle.ShortcutKeyDisplayString = "Ctrl+Alt+M";
            var miSearch = new ToolStripMenuItem("메모 검색", null, (s, e) => app.OpenSearch());
            miSearch.ShortcutKeyDisplayString = "Ctrl+Alt+F";
            var miWander = new ToolStripMenuItem("돌아다니기", null, (s, e) =>
            {
                app.Data.StayPut = !app.Data.StayPut;
                if (app.Data.StayPut) StopWalk();
                app.ScheduleSave();
            });
            var miAuto = new ToolStripMenuItem("Windows 시작 시 자동 실행", null, (s, e) => App.SetAutostart(!App.IsAutostart()));
            var miReset = new ToolStripMenuItem("캐릭터 원래 자리로", null, (s, e) => ResetPosition());
            var miExit = new ToolStripMenuItem("종료", null, (s, e) => app.Exit());

            m.Items.AddRange(new ToolStripItem[] {
                miNew, miToggle, miSearch, new ToolStripSeparator(), miWander, miAuto, miReset, new ToolStripSeparator(), miExit });
            m.Opening += (s, e) =>
            {
                miWander.Checked = !app.Data.StayPut;
                miAuto.Checked = App.IsAutostart();
                miToggle.Text = app.AnyVisible ? "메모 숨기기" : "메모 보이기";
            };
            return m;
        }

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case Native.WM_MOUSEACTIVATE:
                    m.Result = (IntPtr)Native.MA_NOACTIVATE;
                    return;
                case Native.WM_HOTKEY:
                    if (m.WParam.ToInt32() == 1) app.NewNote();
                    else if (m.WParam.ToInt32() == 2) app.Toggle(Native.GetForegroundWindow());
                    else if (m.WParam.ToInt32() == 3) app.OpenSearch();
                    return;
                case Native.WM_DISPLAYCHANGE:
                    BeginInvoke(new Action(Reflow));
                    break;
                case Native.WM_SETTINGCHANGE:
                    if (m.WParam.ToInt64() == Native.SPI_SETWORKAREA) BeginInvoke(new Action(Reflow));
                    break;
            }
            if (quitMsg != 0 && m.Msg == quitMsg)
            {
                app.Exit();
                return;
            }
            if (showMsg != 0 && m.Msg == showMsg)
            {
                app.ShowAll();
                return;
            }
            base.WndProc(ref m);
        }

        void OnWinEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            if (hwnd == IntPtr.Zero || IsDisposed) return;
            uint pid;
            Native.GetWindowThreadProcessId(hwnd, out pid);
            if (pid == myPid) return;

            if (IsFullscreenOnMyScreen(hwnd))
            {
                if (Visible)
                {
                    hiddenForFullscreen = true;
                    StopWalk();
                    thinkTimer.Stop();
                    animTimer.Stop();
                    Hide();
                }
            }
            else if (hiddenForFullscreen)
            {
                hiddenForFullscreen = false;
                Show();
                UpdatePose(true);
                ScheduleThink(3000, 6000);
            }
        }

        bool IsFullscreenOnMyScreen(IntPtr hwnd)
        {
            if (hwnd == Native.GetShellWindow() || hwnd == Native.GetDesktopWindow()) return false;
            var sb = new StringBuilder(64);
            Native.GetClassName(hwnd, sb, sb.Capacity);
            string cls = sb.ToString();
            if (cls == "WorkerW" || cls == "Progman" || cls == "Shell_TrayWnd") return false;

            Native.RECT r;
            if (!Native.GetWindowRect(hwnd, out r)) return false;
            Screen target = Screen.FromHandle(hwnd);
            Rectangle b = target.Bounds;
            if (r.Left > b.Left || r.Top > b.Top || r.Right < b.Right || r.Bottom < b.Bottom) return false;

            Screen mine = Screen.FromPoint(new Point(baseX + pw / 2, baseY + ph / 2));
            return mine.DeviceName == target.DeviceName;
        }
    }
}
