using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MemoDoumi
{

    internal partial class App
    {
        public const string ShowMessageName = "MemoDoumi.ShowNotes";
        public const string QuitMessageName = "MemoDoumi.Quit";
        public const string MascotTitle = "MemoDoumi.MascotWindow";
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunName = "MemoDoumi";

        public static float Scale = 1f;
        public static readonly Font MenuFont = Fonts.Make(9f);
        public static int S(float v) { return (int)Math.Round(v * Scale); }

        public AppData Data;
        public NoteForm LastActive;
        public bool Exiting, QuietShow;

        public const int MaxHiddenForms = 8;

        public const int MaxPinnedAtStartup = 30;
        static readonly TimeSpan RemindAfter = TimeSpan.FromMinutes(10);

        readonly List<NoteForm> notes = new List<NoteForm>();
        readonly Timer saveTimer = new Timer();
        readonly Timer retryTimer = new Timer();
        Mascot mascot;
        SearchForm search;
        Notice notice;
        bool saveFailed, trimQueued;
        string lastSaveError;
        DateTime saveNoticeDismissed = DateTime.MinValue;
        long useCounter;

        public bool SaveFailed { get { return saveFailed; } }
        internal Notice NoticeWindow { get { return notice; } }

        internal Func<string, bool> ConfirmExit { get; set; }

        public void Run()
        {
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) Scale = g.DpiX / 96f;

            saveTimer.Interval = 800;
            saveTimer.Tick += (s, e) => SaveNow();

            retryTimer.Interval = 15000;
            retryTimer.Tick += (s, e) => SaveNow();

            Data = Store.Load();
            if (Store.ImportedNewFiles) SaveNow();

            mascot = new Mascot(this);
            mascot.PlaceInitial();
            foreach (NoteData d in Data.Notes) FixBounds(d);

            mascot.Shown += (s, e) =>
            {
                QuietShow = true;
                int pinned = 0, held = 0;
                foreach (NoteData d in Data.Notes)
                {
                    if (!d.Pinned) continue;
                    if (++pinned <= MaxPinnedAtStartup) FormOf(d).Show();
                    else held++;
                }
                if (held > 0)
                    Store.Problems.Add("고정한 메모가 너무 많아 " + MaxPinnedAtStartup + "개만 띄웠습니다. 나머지 " + held + "개는 메모 검색에서 열 수 있습니다.");
                NoteData cur = Current;
                if (Data.NotesVisible && cur != null)
                {
                    PlaceCurrent(cur);
                    FormOf(cur).Show();
                }
                QuietShow = false;
                ReportLoadProblems(0);
                WatchNotesFolder();
            };

            SystemEvents.SessionEnding += OnSessionEnding;
            Application.Run(mascot);
            SystemEvents.SessionEnding -= OnSessionEnding;
            SaveNow();
            Store.Close();
        }

        void OnSessionEnding(object sender, SessionEndingEventArgs e) { SaveNow(); }

        void FixBounds(NoteData d)
        {
            if (d.W < S(180) || d.H < S(130)) { d.W = S(260); d.H = S(220); }

            Rectangle all = SystemInformation.VirtualScreen;
            d.W = Math.Min(d.W, Math.Max(S(180), all.Width));
            d.H = Math.Min(d.H, Math.Max(S(130), all.Height));
            if (!d.Pinned) return;

            // 기존 상대 좌표를 화면 좌표로 변환하고 화면 밖 위치를 보정한다.
            if (d.Anchored)
            {
                Point dog = mascot.DogPos;
                d.X += dog.X;
                d.Y += dog.Y;
                d.Anchored = false;
            }
            var head = new Rectangle(d.X, d.Y, d.W, S(30));
            foreach (Screen s in Screen.AllScreens)
            {
                Rectangle i = Rectangle.Intersect(s.WorkingArea, head);
                if (i.Width >= S(60) && i.Height >= S(10)) return;
            }
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            d.X = wa.Left + (wa.Width - d.W) / 2;
            d.Y = wa.Top + (wa.Height - d.H) / 2;
        }

        NoteForm FindForm(NoteData d)
        {
            foreach (NoteForm n in notes) if (n.Data == d) return n;
            return null;
        }

        NoteForm FormOf(NoteData d)
        {
            NoteForm f = FindForm(d);
            if (f == null)
            {
                f = new NoteForm(this, d);
                notes.Add(f);
            }
            return f;
        }

        internal void NoteVisibilityChanged(NoteForm f)
        {
            f.Stamp = ++useCounter;
            if (f.Visible || trimQueued || Exiting || mascot == null || !mascot.IsHandleCreated) return;
            int hidden = 0;
            foreach (NoteForm n in notes) if (!n.Visible) hidden++;
            if (hidden <= MaxHiddenForms) return;
            trimQueued = true;
            // 이벤트 처리 중인 창은 즉시 해제하지 않는다.
            mascot.BeginInvoke(new Action(TrimHidden));
        }

        internal void TrimHidden()
        {
            trimQueued = false;
            if (Exiting) return;
            List<NoteForm> hidden = notes.FindAll(n => !n.Visible);
            if (hidden.Count <= MaxHiddenForms) return;
            hidden.Sort((a, b) => { long sa = a.Stamp, sb = b.Stamp; return sb.CompareTo(sa); });
            for (int i = MaxHiddenForms; i < hidden.Count; i++)
            {
                NoteForm f = hidden[i];
                if (f.Commit()) ScheduleSave();
                notes.Remove(f);
                if (LastActive == f) LastActive = null;
                f.Dispose();
            }
        }

        public bool Positioning;
        public Point DogPos { get { return mascot.DogPos; } }
        public int DogCenterX { get { return mascot.DogPos.X + mascot.Width / 2; } }

        public Point NoteScreenPos(NoteData d)
        {
            if (d.Pinned) return new Point(d.X, d.Y);
            Point dog = mascot.DogPos;
            Rectangle wa = mascot.DogWorkArea;
            int x = Math.Max(wa.Left, Math.Min(dog.X + d.X, wa.Right - d.W));
            int y = Math.Max(wa.Top, Math.Min(dog.Y + d.Y, wa.Bottom - d.H));
            return new Point(x, y);
        }

        public void PlaceNotes()
        {
            Positioning = true;
            try
            {
                foreach (NoteForm n in notes)
                    if (!n.Data.Pinned) n.Location = NoteScreenPos(n.Data);
            }
            finally { Positioning = false; }
        }

        void PlaceCurrent(NoteData d)
        {
            Point p = mascot.SuggestNotePos(d.W, d.H), dog = mascot.DogPos;
            d.X = p.X - dog.X;
            d.Y = p.Y - dog.Y;
            d.Anchored = true;
            PlaceNotes();
        }

        NoteData Current
        {
            get
            {
                if (Data.CurrentId == null) return null;
                return Data.Notes.Find(n => n.Id == Data.CurrentId && !n.Pinned);
            }
        }

        List<NoteData> Unpinned() { return Data.Notes.FindAll(n => !n.Pinned); }

        public bool AnyVisible
        {
            get
            {
                NoteData cur = Current;
                NoteForm f = cur != null ? FindForm(cur) : null;
                return f != null && f.Visible;
            }
        }

        public bool KeepStill { get { return AnyVisible || (search != null && search.Visible); } }

        NoteForm SetCurrent(NoteData d)
        {
            NoteData old = Current;
            if (old != null && old != d)
            {
                NoteForm of = FindForm(old);
                if (of != null) of.Hide();
            }
            Data.CurrentId = d.Id;
            mascot.Halt();
            PlaceCurrent(d);
            NoteForm f = FormOf(d);
            f.HasTail = true;
            f.RefreshPosition();
            if (!f.Visible) f.Show();
            Raise(f);
            f.Activate();
            Data.NotesVisible = true;
            ScheduleSave();
            return f;
        }

        public void Toggle(IntPtr fg)
        {
            if (search != null && search.Visible) { search.Cancel(true); return; }
            NoteData cur = Current;
            NoteForm f = cur != null ? FindForm(cur) : null;
            bool visible = f != null && f.Visible;
            if (visible && (fg == IntPtr.Zero || f.Handle == fg)) HideAll();
            else ShowAll();
        }

        public void ShowAll()
        {
            NoteData cur = Current;
            if (cur == null)
            {
                List<NoteData> u = Unpinned();
                if (u.Count > 0) cur = u[u.Count - 1];
            }
            if (cur == null)
            {
                if (Data.Notes.Count == 0) NewNote();
                else OpenSearch();
                return;
            }
            SetCurrent(cur);
        }

        public void HideAll()
        {
            NoteData cur = Current;
            NoteForm f = cur != null ? FindForm(cur) : null;
            if (f != null) f.Hide();
            Data.NotesVisible = false;
            ScheduleSave();
        }

        public void Step(int dir)
        {
            List<NoteData> u = Unpinned();
            if (u.Count < 2) return;
            int i = u.IndexOf(Current);
            i = i < 0 ? 0 : (i + dir + u.Count) % u.Count;
            SetCurrent(u[i]).FocusText();
        }

        public void GoTo(int number)
        {
            List<NoteData> u = Unpinned();
            if (u.Count == 0) return;
            int i = Math.Max(0, Math.Min(u.Count - 1, number - 1));
            SetCurrent(u[i]).FocusText();
        }

        public void GoLast() { GoTo(int.MaxValue); }

        public string PositionText(NoteData d)
        {
            List<NoteData> u = Unpinned();
            int i = u.IndexOf(d);
            return i < 0 ? "" : (i + 1) + " / " + u.Count;
        }

        static void Raise(NoteForm n)
        {
            const uint flags = 0x1 | 0x2 | 0x10;   // NOSIZE | NOMOVE | NOACTIVATE
            Native.SetWindowPos(n.Handle, new IntPtr(-1), 0, 0, 0, 0, flags);
            if (!n.Data.Pinned) Native.SetWindowPos(n.Handle, new IntPtr(-2), 0, 0, 0, 0, flags);
        }

        public void OnPinChanged(NoteForm f)
        {
            if (f.Data.Pinned)
            {
                f.Data.Anchored = false;
                f.Data.X = f.Left;
                f.Data.Y = f.Top;
                f.HasTail = false;
                if (Data.CurrentId == f.Data.Id)
                {
                    Data.CurrentId = null;
                    Data.NotesVisible = false;
                }
                ScheduleSave();
            }
            else SetCurrent(f.Data);
        }

        public void CloseNote(NoteForm f)
        {
            if (f.Data.Pinned)
            {
                f.SetPinned(false);
                f.Hide();
                ScheduleSave();
            }
            else if (f.Data == Current) HideAll();
            else f.Hide();
        }

        public List<NoteData> NotesNewestFirst()
        {
            var list = new List<NoteData>(Data.Notes);
            list.Reverse();
            return list;
        }

        public void OpenSearch()
        {
            if (search == null) search = new SearchForm(this);
            if (search.Visible) return;
            foreach (NoteForm n in notes) n.Commit();
            mascot.Halt();

            NoteData cur = Current;
            NoteForm f = cur != null ? FindForm(cur) : null;
            Rectangle bounds;
            bool replacing = f != null && f.Visible;
            if (replacing)
            {
                searchReturn = cur;
                bounds = f.Bounds;
            }
            else
            {
                searchReturn = null;
                var size = new Size(S(270), S(290));
                bounds = new Rectangle(mascot.SuggestNotePos(size.Width, size.Height), size);
            }

            search.Popup(bounds);
            if (replacing) f.Hide();
        }

        NoteData searchReturn;

        public void SearchClosed(bool activate)
        {
            NoteData d = searchReturn;
            searchReturn = null;
            if (d == null || d.Pinned || !Data.Notes.Contains(d)) return;
            if (activate) SetCurrent(d).FocusText();
            else
            {
                PlaceCurrent(d);
                NoteForm f = FormOf(d);
                QuietShow = true;
                f.Show();
                QuietShow = false;
            }
        }

        public void ShowNote(NoteData d, int index, int length)
        {
            if (!Data.Notes.Contains(d)) return;
            searchReturn = null;
            NoteForm f;
            if (d.Pinned)
            {
                f = FormOf(d);
                if (!f.Visible) f.Show();
                Raise(f);
                f.Activate();
            }
            else f = SetCurrent(d);
            f.SelectText(index, length);
        }

        public void NewNote()
        {
            var d = new NoteData();
            d.Id = Guid.NewGuid().ToString("N");
            d.Text = "";
            d.W = S(270);
            d.H = S(250);
            d.Md = true;
            d.Color = Palette.Clamp(Data.LastColor);
            d.Sort = DateTime.UtcNow.Ticks;
            d.CreatedUtc = d.ModifiedUtc = d.Sort;
            Data.Notes.Add(d);
            SetCurrent(d).FocusText();
        }

        public void DeleteNote(NoteForm f)
        {
            bool wasCurrent = Data.CurrentId == f.Data.Id && !f.Data.Pinned;
            int index = wasCurrent ? Unpinned().IndexOf(f.Data) : -1;
            notes.Remove(f);
            Data.Notes.Remove(f.Data);
            if (LastActive == f) LastActive = null;
            if (Data.CurrentId == f.Data.Id)
            {
                Data.CurrentId = null;
                Data.NotesVisible = false;
            }
            List<NoteData> rest = Unpinned();

            if (wasCurrent && f.Visible && rest.Count > 0)
                SetCurrent(rest[Math.Max(0, Math.Min(rest.Count - 1, index - 1))]).FocusText();
            f.Hide();
            SaveNow();
            // 클릭 이벤트가 끝난 뒤 창을 해제한다.
            mascot.BeginInvoke(new Action(f.Dispose));
        }

        public static string ShortTime(long utcTicks)
        {
            if (!Store.ValidTicks(utcTicks)) return "";
            DateTime t = new DateTime(utcTicks, DateTimeKind.Utc).ToLocalTime(), now = DateTime.Now;
            if (t.Date == now.Date) return "오늘 " + t.ToString("HH:mm");
            if (t.Year == now.Year) return t.ToString("M.d HH:mm");
            return t.ToString("yyyy.M.d");
        }

        public static string FullTime(long utcTicks)
        {
            return !Store.ValidTicks(utcTicks) ? "-" : new DateTime(utcTicks, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        }

        public void ScheduleSave()
        {
            saveTimer.Stop();
            saveTimer.Start();
        }

        public bool SaveNow() { return SaveNow(false); }

        bool SaveNow(bool manual)
        {
            saveTimer.Stop();
            foreach (NoteForm n in notes) n.Commit();
            try
            {
                Store.Save(Data);
            }
            catch (Exception ex)
            {
                OnSaveFailed(ex, manual);
                return false;
            }
            if (saveFailed)
            {
                saveFailed = false;
                lastSaveError = null;
                saveNoticeDismissed = DateTime.MinValue;
                retryTimer.Stop();
                if (notice != null) notice.Remove(Notice.SaveKey);
                LogText("저장이 다시 되기 시작했습니다.");
            }
            return true;
        }

        void OnSaveFailed(Exception ex, bool manual)
        {

            if (ex.Message != lastSaveError)
            {
                Log(ex);
                var se = ex as SaveException;
                if (se != null && se.Failures.Count > 1) LogText("저장 실패 목록:\r\n  " + string.Join("\r\n  ", se.Failures.ToArray()));
            }
            lastSaveError = ex.Message;
            saveFailed = true;
            if (!retryTimer.Enabled) retryTimer.Start();
            if (Exiting) return;

            bool showing = notice != null && notice.Visible && notice.Has(Notice.SaveKey);
            if (!showing && !manual && DateTime.UtcNow - saveNoticeDismissed < RemindAfter) return;
            Notify(Notice.SaveKey, "메모를 저장하지 못했습니다.\n" + Shorten(ex.Message, 300) +
                "\n\n바뀐 내용은 앱이 켜져 있는 동안 보관하며, 자동으로 다시 저장을 시도합니다.\n디스크 공간과 data 폴더의 쓰기 권한을 확인해 주세요.");
        }

        static string Shorten(string s, int max) { return s.Length <= max ? s : s.Substring(0, max) + "…"; }

        public void Notify(string key, string message)
        {
            if (Exiting) return;
            if (notice == null || notice.IsDisposed)
            {
                notice = new Notice(() => Exiting);
                notice.Retry = () => SaveNow(true);
                notice.Dismissed = () => { if (saveFailed) saveNoticeDismissed = DateTime.UtcNow; };
            }
            notice.Put(key, message);
        }

        FileSystemWatcher watcher;
        readonly Timer importTimer = new Timer(), importNoticeTimer = new Timer();
        int importTries;

        void WatchNotesFolder()
        {
            importTimer.Interval = 1000;
            importTimer.Tick += (s, e) => ImportExternal();

            importNoticeTimer.Interval = 6000;
            importNoticeTimer.Tick += (s, e) => { importNoticeTimer.Stop(); if (notice != null && !notice.IsDisposed) notice.Remove(Notice.ImportKey); };
            try
            {
                Directory.CreateDirectory(Store.NotesFolder);
                watcher = new FileSystemWatcher(Store.NotesFolder, "*.md");
                watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size;
                watcher.SynchronizingObject = mascot;
                FileSystemEventHandler changed = (s, e) => QueueImport();
                watcher.Created += changed;
                watcher.Changed += changed;
                watcher.Renamed += (s, e) => QueueImport();
                watcher.Error += (s, e) => QueueImport();
                watcher.EnableRaisingEvents = true;
            }
            catch (Exception ex) { Log(ex); }
        }

        void QueueImport()
        {
            if (Exiting) return;
            importTries = 0;
            importTimer.Stop();
            importTimer.Start();
        }

        internal int ImportExternal()
        {
            importTimer.Stop();
            if (Exiting || Data == null) return 0;
            int problems = Store.Problems.Count;
            bool pending;
            List<NoteData> added;
            try { added = Store.ImportNew(Data, out pending); }
            catch (Exception ex) { Log(ex); return 0; }

            if (pending && ++importTries < 30) importTimer.Start();
            if (added.Count > 0)
            {
                foreach (NoteData d in added) FixBounds(d);
                SaveNow();
                foreach (NoteForm n in notes) if (n.Visible) n.RefreshPosition();
                var titles = new List<string>();
                for (int i = 0; i < added.Count && i < 3; i++) titles.Add("· " + Shorten(Markdown.Plain(added[i]).Trim().Split('\n')[0], 40));
                if (added.Count > 3) titles.Add("… 외 " + (added.Count - 3) + "개");
                Notify(Notice.ImportKey, "notes 폴더에 넣은 메모 " + added.Count + "개를 불러왔습니다.\n" + string.Join("\n", titles.ToArray()));
                importNoticeTimer.Stop();
                importNoticeTimer.Start();
            }
            ReportLoadProblems(problems);
            return added.Count;
        }

        void ReportLoadProblems(int from)
        {
            if (Store.Problems.Count <= from) return;
            List<string> fresh = Store.Problems.GetRange(from, Store.Problems.Count - from);
            LogText("메모를 읽는 중 확인한 문제:\r\n  " + string.Join("\r\n  ", fresh.ToArray()));
            var lines = new List<string>();
            for (int i = 0; i < fresh.Count && i < 5; i++) lines.Add("· " + Shorten(fresh[i], 220));
            if (fresh.Count > 5) lines.Add("… 외 " + (fresh.Count - 5) + "건");
            Notify(Notice.LoadKey, "메모를 읽는 중 확인이 필요한 항목이 있습니다.\n" + string.Join("\n", lines.ToArray()) +
                "\n\n전체 내용은 data\\error.log 에 적어 두었습니다.");
        }

        public void SaveMascotPos(Point p)
        {
            Data.HasMascotPos = true;
            Data.MascotX = p.X;
            Data.MascotY = p.Y;
            ScheduleSave();
        }

        public void Exit()
        {
            Exiting = true;
            if (!SaveNow())
            {

                bool quit = ConfirmExit != null
                    ? ConfirmExit(lastSaveError)
                    : MessageBox.Show("저장하지 못한 변경이 있습니다.\n" + Shorten(lastSaveError ?? "", 300) +
                          "\n\n지금 종료하면 저장되지 않은 내용은 사라집니다. 그래도 종료할까요?", "메모도우미",
                          MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
                if (!quit)
                {
                    Exiting = false;
                    return;
                }
            }
            retryTimer.Stop();
            importTimer.Stop();
            importNoticeTimer.Stop();
            if (watcher != null) { watcher.Dispose(); watcher = null; }
            Application.Exit();
        }

        static string RunCommand { get { return "\"" + Application.ExecutablePath + "\""; } }

        public static bool IsAutostart()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey))
                {
                    if (k == null) return false;
                    return string.Equals(k.GetValue(RunName) as string, RunCommand, StringComparison.OrdinalIgnoreCase);
                }
            }
            catch { return false; }
        }

        public static void SetAutostart(bool on)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (on) k.SetValue(RunName, RunCommand);
                    else k.DeleteValue(RunName, false);
                }
            }
            catch (Exception ex) { Log(ex); }
        }

        public static void Log(Exception ex)
        {
            if (ex != null) LogText(ex.ToString());
        }

        public static void LogText(string text)
        {
            try
            {
                Directory.CreateDirectory(Store.Dir);
                string path = Path.Combine(Store.Dir, "error.log");

                var info = new FileInfo(path);
                if (info.Exists && info.Length > 1024 * 1024)
                {
                    string old = Path.Combine(Store.Dir, "error.old.log");
                    if (File.Exists(old)) File.Delete(old);
                    File.Move(path, old);
                }
                File.AppendAllText(path, DateTime.Now + "  " + text + "\r\n\r\n");
            }
            catch { }
        }
    }
}
