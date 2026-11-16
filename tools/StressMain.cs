using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace MemoDoumi
{

    internal static class StressMain
    {
        [DllImport("user32.dll")] static extern uint GetGuiResources(IntPtr h, uint flag);
        [DllImport("ntdll.dll")] static extern int NtQueryObject(IntPtr handle, int infoClass, IntPtr buffer, int length, out int returned);
        [DllImport("kernel32.dll")] static extern int GetFileType(IntPtr handle);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern int GetFinalPathNameByHandle(IntPtr handle, StringBuilder path, int size, int flags);
        [DllImport("kernel32.dll")] static extern int GetThreadId(IntPtr handle);

        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        const int OpenSet = 100;

        static App app;
        static Timer timer;
        static readonly Stopwatch elapsed = new Stopwatch();
        static StreamWriter metrics, latency, handles;
        static int tick, duration, savedDeletes, unsavedDeletes, inserted;
        static bool forceGc = true;
        static MethodInfo formOf, flushSearch;

        static object Field(object o, string name) { return o.GetType().GetField(name, Private).GetValue(o); }

        static NoteForm FormOf(NoteData d) { return (NoteForm)formOf.Invoke(app, new object[] { d }); }
        static NoteForm Current() { return FormOf(app.Data.Notes.Find(n => n.Id == app.Data.CurrentId)); }

        static void Time(string op, Action action)
        {
            var sw = Stopwatch.StartNew();
            action();
            latency.WriteLine(tick + "," + op + "," + sw.Elapsed.TotalMilliseconds.ToString("F4", CultureInfo.InvariantCulture));
        }

        static void Sample(string phase, bool collect, bool types, bool names)
        {
            if (collect && forceGc) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
            using (var p = Process.GetCurrentProcess())
            {
                p.Refresh();
                metrics.WriteLine(string.Join(",", new string[] {
                    elapsed.Elapsed.TotalSeconds.ToString("F2", CultureInfo.InvariantCulture), phase, tick.ToString(),
                    p.WorkingSet64.ToString(), p.PrivateMemorySize64.ToString(), GC.GetTotalMemory(false).ToString(),
                    p.HandleCount.ToString(), GetGuiResources(p.Handle, 0).ToString(), GetGuiResources(p.Handle, 1).ToString(),
                    ((System.Collections.ICollection)Field(app, "notes")).Count.ToString(),
                    p.TotalProcessorTime.TotalSeconds.ToString(CultureInfo.InvariantCulture),
                    p.Threads.Count.ToString() }));
            }
            if (types) SampleHandles(phase, names);
            metrics.Flush(); latency.Flush(); handles.Flush();
        }

        static void SampleHandles(string phase, bool names)
        {
            var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
            StreamWriter dump = names ? new StreamWriter("handle-names-" + tick.ToString("D5") + "-" + phase + ".txt") : null;
            IntPtr buf = Marshal.AllocHGlobal(4096);
            try
            {
                for (int v = 4; v < 0x20000; v += 4)
                {
                    var h = new IntPtr(v);
                    int ret;
                    if (NtQueryObject(h, 2, buf, 4096, out ret) != 0) continue;
                    string type = ReadUnicodeString(buf);
                    int c;
                    counts.TryGetValue(type, out c);
                    counts[type] = c + 1;
                    if (dump != null) dump.WriteLine(type + "\t" + NameOf(h, type, buf));
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
                if (dump != null) dump.Dispose();
            }
            string sec = elapsed.Elapsed.TotalSeconds.ToString("F2", CultureInfo.InvariantCulture);
            foreach (var kv in counts) handles.WriteLine(sec + "," + phase + "," + tick + "," + kv.Key + "," + kv.Value);
        }

        static string ReadUnicodeString(IntPtr p)
        {
            int len = (ushort)Marshal.ReadInt16(p);
            IntPtr s = Marshal.ReadIntPtr(p, IntPtr.Size);     // Length, MaximumLength 다음 (64비트는 채움 포함)
            return s == IntPtr.Zero || len == 0 ? "" : Marshal.PtrToStringUni(s, len / 2);
        }

        static string NameOf(IntPtr h, string type, IntPtr buf)
        {
            try
            {
                if (type == "File")
                {
                    // 파이프 등은 이름 조회가 멈출 수 있으므로 디스크 파일만 경로를 읽는다
                    int ft = GetFileType(h);
                    if (ft != 1) return "(filetype " + ft + ")";
                    var sb = new StringBuilder(1024);
                    return GetFinalPathNameByHandle(h, sb, sb.Capacity, 0) > 0 ? sb.ToString() : "";
                }
                if (type == "Thread") return "tid " + GetThreadId(h);
                int ret;
                if (NtQueryObject(h, 1, buf, 4096, out ret) != 0) return "";
                return ReadUnicodeString(buf);
            }
            catch { return "?"; }
        }

        [STAThread]
        static void Main(string[] args)
        {
            duration = int.Parse(args[0]);
            foreach (string a in args) if (a == "--no-gc") forceGc = false;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            metrics = new StreamWriter("metrics.csv");
            latency = new StreamWriter("latency.csv");
            handles = new StreamWriter("handles.csv");
            metrics.WriteLine("seconds,phase,cycle,working_bytes,private_bytes,managed_bytes,handles,gdi,user,cached_forms,cpu_seconds,threads");
            latency.WriteLine("cycle,operation,ms");
            handles.WriteLine("seconds,phase,cycle,type,count");
            formOf = typeof(App).GetMethod("FormOf", Private);
            flushSearch = typeof(SearchForm).GetMethod("FlushPending", Private | BindingFlags.Public);
            app = new App();
            timer = new Timer();
            timer.Interval = 1000;
            timer.Tick += Step;
            Application.ThreadException += (s, e) => Fail(e.Exception);
            elapsed.Start();
            timer.Start();
            app.Run();
            File.WriteAllText("counts.txt", "cycles=" + tick + "\r\ninserted=" + inserted + "\r\nsaved_deletes=" + savedDeletes +
                "\r\nunsaved_deletes=" + unsavedDeletes + "\r\nforced_gc=" + forceGc + "\r\nsearch_flush=" + (flushSearch != null) + "\r\n");
            metrics.Dispose(); latency.Dispose(); handles.Dispose();
        }

        static void Fail(Exception ex)
        {
            File.WriteAllText("failure.txt", ex.ToString());
            timer.Stop();
            app.Exit();
        }

        static void Insert(NoteForm f, string text)
        {
            var box = (RichTextBox)Field(f, "box");
            box.SelectionStart = box.TextLength;
            box.SelectedText = text;
        }

        static void Step(object sender, EventArgs e)
        {
            try
            {
                if (tick == 0) Sample("baseline", true, true, true);
                if (elapsed.Elapsed.TotalSeconds >= duration)
                {
                    timer.Stop();
                    app.SaveNow();
                    Sample(forceGc ? "final_gc" : "final", true, true, true);
                    app.Exit();
                    return;
                }

                if (tick > 0)
                {
                    NoteData prev = app.Data.Notes[(tick - 1) % OpenSet];
                    Time("reopen_recent", () => app.ShowNote(prev, 0, 0));
                    app.CloseNote(FormOf(prev));
                }

                Time("open", () => app.ShowNote(app.Data.Notes[tick % OpenSet], 0, 0));
                NoteForm f = Current();
                var box = (RichTextBox)Field(f, "box");
                box.SelectionStart = box.TextLength;
                Time("insert", () => box.SelectedText = "a");
                inserted++;
                Time("save", () => app.SaveNow());

                Time("search_open", () => app.OpenSearch());
                var search = (SearchForm)Field(app, "search");
                var input = (TextBox)Field(search, "input");
                foreach (string query in new string[] { "b", "be", "bench", "missing_needle", "" })
                {
                    string q = query;
                    Time("search_" + (q.Length == 0 ? "all" : q), () =>
                    {
                        input.Text = q;
                        if (flushSearch != null) flushSearch.Invoke(search, null);
                        search.Update();
                    });
                }
                search.Cancel(false);
                app.CloseNote(f);

                if (tick % 5 == 0)
                {
                    app.NewNote();
                    app.DeleteNote(Current());
                    unsavedDeletes++;
                }

                if (tick % 5 == 2)
                {
                    Time("new_note", () => app.NewNote());
                    NoteForm nf = Current();
                    Insert(nf, "saved then deleted " + tick);
                    Time("save_new", () => app.SaveNow());
                    Time("delete_saved", () => app.DeleteNote(nf));
                    savedDeletes++;
                }

                tick++;
                if (tick % 10 == 0) Sample("running", false, false, false);
                if (tick % 60 == 0) Sample(forceGc ? "checkpoint_gc" : "checkpoint", true, true, tick == 120 || tick % 600 == 0);
            }
            catch (Exception ex) { Fail(ex); }
        }
    }
}
