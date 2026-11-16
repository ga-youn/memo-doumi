using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace MemoDoumi
{

    internal static class VerifyMain
    {
        [DllImport("user32.dll")] static extern uint GetGuiResources(IntPtr h, uint flag);

        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly Encoding Utf8 = new UTF8Encoding(false);
        static string Base, DataDir, NotesDir, TrashDir;
        static int failures;
        static string scenario, phase;

        static void Check(bool ok, string name, string detail = "")
        {
            if (!ok) failures++;
            File.AppendAllText(Path.Combine(Base, "results.txt"),
                (ok ? "PASS" : "FAIL") + "\t" + scenario + "/" + phase + "\t" + name + (detail.Length > 0 ? "\t" + detail : "") + "\r\n", Utf8);
        }

        static void Info(string name, string detail)
        {
            File.AppendAllText(Path.Combine(Base, "results.txt"), "INFO\t" + scenario + "/" + phase + "\t" + name + "\t" + detail + "\r\n", Utf8);
        }

        static string Sha(string path)
        {
            using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(path)));
        }

        static string Id(int n) { return new Guid(n, 0x1234, 0x5678, 1, 2, 3, 4, 5, 6, 7, (byte)n).ToString("N"); }

        static string NoteText(string id, string body, int order, string extra = "")
        {
            return ("---\nid: " + id + "\ncolor: 1\nsize: 270x250\npos: -200,-260\npinned: false\nclosed: false\nformat: md\n" +
                    "created: 2026-01-02 03:04:05\nmodified: 2026-01-02 03:04:05\n" + extra + "order: " + order + "\n---\n" + body + "\n").Replace("\n", "\r\n");
        }

        static void Write(string name, string content) { File.WriteAllText(Path.Combine(NotesDir, name), content, Utf8); }

        static object Field(object o, string name) { return o.GetType().GetField(name, Private).GetValue(o); }
        static NoteForm FormOf(App app, NoteData d) { return (NoteForm)typeof(App).GetMethod("FormOf", Private).Invoke(app, new object[] { d }); }
        static NoteForm FindForm(App app, NoteData d) { return (NoteForm)typeof(App).GetMethod("FindForm", Private).Invoke(app, new object[] { d }); }
        static RichTextBox BoxOf(NoteForm f) { return (RichTextBox)Field(f, "box"); }
        static System.Collections.ICollection Forms(App app) { return (System.Collections.ICollection)Field(app, "notes"); }
        static uint UserObjects() { using (var p = System.Diagnostics.Process.GetCurrentProcess()) return GetGuiResources(p.Handle, 1); }
        static uint GdiObjects() { using (var p = System.Diagnostics.Process.GetCurrentProcess()) return GetGuiResources(p.Handle, 0); }

        static Dictionary<string, string> FilesById()
        {
            var map = new Dictionary<string, string>();
            foreach (string f in Directory.GetFiles(NotesDir, "*.md"))
            {
                if (new FileInfo(f).Length > Store.MaxNoteBytes) continue;
                string t;
                try { t = File.ReadAllText(f, new UTF8Encoding(false, true)); } catch { continue; }
                if (t.IndexOf('\0') >= 0 || !t.StartsWith("---")) continue;
                int at = t.IndexOf("id: ");
                if (at < 0) continue;
                string id = t.Substring(at + 4, t.IndexOf('\r', at) - at - 4);
                map[id + (map.ContainsKey(id) ? "#dup" + map.Count : "")] = f;
            }
            return map;
        }

        static int CountContaining(string marker)
        {
            int n = 0;
            foreach (string f in Directory.GetFiles(NotesDir, "*.md"))
            {
                if (new FileInfo(f).Length > Store.MaxNoteBytes) continue;
                if (File.ReadAllText(f, Utf8).Contains(marker)) n++;
            }
            return n;
        }

        static void RunApp(Action<App> before, params Action<App>[] steps)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var app = new App();
            int i = -1;
            bool busy = false;
            var timer = new Timer();
            timer.Interval = 200;
            Application.ThreadException += (s, e) => { Check(false, "처리되지 않은 예외", e.Exception.ToString().Replace("\r\n", " | ")); timer.Stop(); app.ConfirmExit = m => true; app.Exit(); };
            timer.Tick += (s, e) =>
            {
                if (app.Data == null || busy) return;
                busy = true;
                i++;
                if (i >= steps.Length) { timer.Stop(); app.ConfirmExit = m => true; app.Exit(); return; }
                try { steps[i](app); }
                catch (Exception ex) { Check(false, "단계 " + i + " 예외", ex.ToString().Replace("\r\n", " | ")); i = steps.Length; }
                busy = false;
            };
            timer.Start();
            if (before != null) before(app);
            app.Run();
        }

        [STAThread]
        static int Main(string[] args)
        {
            scenario = args[0];
            phase = args.Length > 1 ? args[1] : "";
            Base = AppDomain.CurrentDomain.BaseDirectory;
            DataDir = Path.Combine(Base, "data");
            NotesDir = Path.Combine(DataDir, "notes");
            TrashDir = Path.Combine(DataDir, "trash");
            try
            {
                switch (scenario + "/" + phase)
                {
                    case "links/run": Links(); break;
                    case "ids/prepare": IdsPrepare(); break;
                    case "ids/run1": IdsRun1(); break;
                    case "ids/run2": IdsRun2(); break;
                    case "migrate/prepare": MigratePrepare(); break;
                    case "migrate/run1": MigrateRun(1); break;
                    case "migrate/run2": MigrateRun(2); break;
                    case "migrate/run3": MigrateRun(3); break;
                    case "resume/prepare": ResumePrepare(); break;
                    case "resume/run1": ResumeRun(1); break;
                    case "resume/run2": ResumeRun(2); break;
                    case "resume/run3": ResumeRun(3); break;
                    case "savefail/prepare": Plain(6); break;
                    case "savefail/run": SaveFail(); break;
                    case "evict/prepare": Plain(80); break;
                    case "evict/run1": Evict(); break;
                    case "evict/run2": EvictReload(); break;
                    case "search/prepare": SearchPrepare(); break;
                    case "search/run": Search(); break;
                    case "external/prepare": ExternalPrepare(); break;
                    case "external/run1": ExternalBlocked(false); break;
                    case "external/run2": ExternalBlocked(true); break;
                    case "external/run3": ExternalConvert(); break;
                    case "external/run4": ExternalRerun(); break;
                    case "fonts/prepare": FontsPrepare(); break;
                    case "fonts/run": FontsRun(); break;
                    case "live/prepare": Plain(3); break;
                    case "live/run": LiveImport(); break;
                    case "delete/prepare": Plain(6); break;
                    case "delete/run": DeleteShowsPrevious(); break;
                    default: Check(false, "알 수 없는 시나리오"); break;
                }
            }
            catch (Exception ex) { Check(false, "예외", ex.ToString().Replace("\r\n", " | ")); }
            return failures;
        }

        static void Plain(int count)
        {
            Directory.CreateDirectory(NotesDir);
            for (int i = 0; i < count; i++)
            {
                NoteData n = new NoteData { Id = Id(i + 1) };
                Write("합성 " + i + "-" + n.Id + ".md", NoteText(n.Id, "합성 " + i + "\nBODY-" + i + " 내용", i + 1));
            }
            File.WriteAllText(Path.Combine(DataDir, "settings.json"), "{\"NotesVisible\":false,\"StayPut\":true}");
        }

        static void Links()
        {
            string[] allowed = { "http://example.com", "https://example.com/a?b=c#d", "HTTPS://EXAMPLE.COM/Path", "www.example.com", "http://127.0.0.1:8080/x", "https://한글.kr/길" };
            string[] blocked = {
                "file:///C:/Windows/System32/calc.exe", "FILE://server/share/a.exe", @"C:\Windows\System32\calc.exe", @"\\server\share\run.exe",
                "//server/share/run.exe", "calc.exe", "cmd /c calc", "mailto:a@example.com", "javascript:alert(1)", "ms-settings:display",
                "ftp://example.com/a", "shell:startup", "search-ms:query=x", "http:", "https://", "http:///nohost", " ", "", null,
                "http://example.com\r\ncalc.exe", "about:blank", "data:text/html,x", "vbscript:x", "ldap://x", "news:x", "telnet://x", @"file:\\server\x",
                "www.", "hxxp://example.com", ".\\run.bat", "..\\..\\run.cmd" };
            foreach (string a in allowed)
            {
                string r = LinkPolicy.SafeUrl(a);
                Check(r != null && (r.StartsWith("http://") || r.StartsWith("https://")), "허용: " + a, r ?? "(null)");
            }
            foreach (string b in blocked)
                Check(LinkPolicy.SafeUrl(b) == null, "차단: " + (b ?? "(null)").Replace("\r\n", "\\r\\n"), LinkPolicy.SafeUrl(b) ?? "");
        }

        const string Twin1 = "abcdef1200000000000000000000aaaa", Twin2 = "abcdef1200000000000000000000bbbb";
        static readonly string[] Skipped = { "huge.md", "broken.md", "nul.md" };

        static string OccupiedName { get { return "막힘-" + Id(15) + ".md"; } }

        static void IdsPrepare()
        {
            Directory.CreateDirectory(NotesDir);
            File.WriteAllText(Path.Combine(DataDir, "settings.json"), "{\"NotesVisible\":false,\"StayPut\":true}");

            Write("정상 메모-" + Id(1) + ".md", NoteText(Id(1), "정상 메모\nMARK-01", 1));

            Write("옛 이름-" + Id(2).Substring(0, 8) + ".md", NoteText(Id(2), "옛 이름\nMARK-02", 2));

            Write("c1.md", NoteText(Twin1, "같은 제목\nMARK-03", 3));
            Write("c2.md", NoteText(Twin2, "같은 제목\nMARK-04", 4));

            Write("badid1.md", NoteText(@"..\..\evil", "나쁜 아이디1\nMARK-05", 5));
            Write("badid2.md", NoteText("not-a-guid", "나쁜 아이디2\nMARK-06", 6));

            Write("중복-" + Id(7) + ".md", NoteText(Id(7), "중복\nMARK-07A", 7));
            Write("중복 사본.md", NoteText(Id(7), "중복\nMARK-07B", 8));

            Write("대문자.md", NoteText(new Guid(Id(8)).ToString("D").ToUpperInvariant(), "대문자 아이디\nMARK-08", 9));

            Write("plain.md", "머리말 없음\nMARK-09\n");

            Write("huge.md", NoteText(Id(10), "큰 파일\nMARK-10\n" + new string('x', 2 * 1024 * 1024), 10));
            var broken = new List<byte>(Utf8.GetBytes(NoteText(Id(11), "깨진 글자\nMARK-11 ", 11)));
            broken.AddRange(new byte[] { 0xFF, 0xFE, 0x80, 0xC3 });
            File.WriteAllBytes(Path.Combine(NotesDir, "broken.md"), broken.ToArray());
            Write("nul.md", NoteText(Id(12), "널 문자\nMARK-12 \0\0 끝", 12));

            Write("meta.md", ("---\nid: " + Id(13) + "\ncolor: 99\nsize: 999999x999999\npos: 99999999,5\npinned: true\nclosed: maybe\nformat: md\n" +
                              "created: garbage\nmodified: 9999-99-99 99:99:99\nunknown: x\norder: 9223372036854775807\n---\n이상한 값\nMARK-13\n").Replace("\n", "\r\n"));
            Write("meta2.md", ("---\nid: " + Id(14) + "\ncolor: -4\nsize: -5x-7\npos: abc,def\npinned: false\norder: -3\n---\n음수 값\nMARK-14\n").Replace("\n", "\r\n"));

            Write("k.md", NoteText(Id(15), "막힘\nMARK-15", 15));
            Write(OccupiedName, "남의 파일\n" + new string('y', 2 * 1024 * 1024));

            var sb = new StringBuilder();
            foreach (string s in Skipped) sb.AppendLine(s + "\t" + Sha(Path.Combine(NotesDir, s)));
            sb.AppendLine(OccupiedName + "\t" + Sha(Path.Combine(NotesDir, OccupiedName)));
            string normal = Path.Combine(NotesDir, "정상 메모-" + Id(1) + ".md");
            sb.AppendLine("정상 메모-" + Id(1) + ".md\t" + Sha(normal));
            File.WriteAllText(Path.Combine(Base, "hashes.txt"), sb.ToString(), Utf8);
            File.SetLastWriteTimeUtc(normal, new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        }

        static readonly string[] Marks = { "MARK-01", "MARK-02", "MARK-03", "MARK-04", "MARK-05", "MARK-06", "MARK-07A", "MARK-07B", "MARK-08", "MARK-09", "MARK-13", "MARK-14", "MARK-15" };

        static void CheckPreserved()
        {
            foreach (string line in File.ReadAllLines(Path.Combine(Base, "hashes.txt"), Utf8))
            {
                string[] p = line.Split('\t');
                string path = Path.Combine(NotesDir, p[0]);
                Check(File.Exists(path) && Sha(path) == p[1], "원본 그대로: " + p[0]);
            }
        }

        static void CheckNotes(App app)
        {
            Check(app.Data.Notes.Count == Marks.Length, "읽은 메모 수", app.Data.Notes.Count + " / 기대 " + Marks.Length);
            var ids = new HashSet<string>();
            foreach (NoteData n in app.Data.Notes)
            {
                Guid g;
                Check(n.Id.Length == 32 && Guid.TryParseExact(n.Id, "N", out g) && n.Id == n.Id.ToLowerInvariant(), "아이디 형식: " + n.Id);
                Check(ids.Add(n.Id), "아이디 중복 없음: " + n.Id);
            }
            foreach (string m in Marks)
            {
                Check(app.Data.Notes.FindAll(n => n.Text.Contains(m)).Count == 1, "메모리에 본문 하나: " + m);
                Check(CountContaining(m) == 1, "디스크에 본문 하나: " + m, CountContaining(m).ToString());
            }
            Dictionary<string, string> files = FilesById();
            foreach (NoteData n in app.Data.Notes)
            {
                string path;
                bool found = files.TryGetValue(n.Id, out path);
                Check(found, "아이디의 파일 있음: " + n.Id);
                if (found) Check(Path.GetFileName(path).Contains(n.Id), "파일 이름에 아이디 전체: " + Path.GetFileName(path));
            }
            Check(Directory.GetFiles(Base, "*evil*", SearchOption.AllDirectories).Length == 0
                  && Directory.GetFiles(Path.GetDirectoryName(Base.TrimEnd('\\')), "*evil*").Length == 0, "notes 폴더 밖에 쓰지 않음");
            Check(Directory.GetFiles(DataDir, "*.tmp", SearchOption.AllDirectories).Length == 0, "임시 파일 남지 않음");
            CheckPreserved();
        }

        static void IdsRun1()
        {
            RunApp(null,
                app =>
                {
                    Check(app.SaveNow(), "잘못된 머리말이 있어도 저장 성공");
                    CheckNotes(app);
                    Rectangle all = SystemInformation.VirtualScreen;
                    foreach (NoteData n in app.Data.Notes)
                    {
                        Check(n.W <= all.Width && n.H <= all.Height && n.W > 0 && n.H > 0, "창 크기 범위: " + n.W + "x" + n.H);
                        Check(n.Color >= 0 && n.Color < Palette.Names.Length, "색 범위: " + n.Color);
                        Check(Store.ValidTicks(n.CreatedUtc) && Store.ValidTicks(n.ModifiedUtc), "시각 범위");
                    }
                    NoteData pinned = app.Data.Notes.Find(n => n.Text.Contains("MARK-13"));
                    NoteForm pf = FindForm(app, pinned);
                    Check(pf != null && pf.Visible && pf.Width <= all.Width && pf.Height <= all.Height, "과도한 크기의 고정 메모 창이 화면 안 크기로 뜸",
                        pf == null ? "창 없음" : pf.Width + "x" + pf.Height);

                    Check(Store.Problems.Count >= 7, "문제 목록", Store.Problems.Count + "건");
                    foreach (string s in new[] { "huge.md", "broken.md", "nul.md", OccupiedName, "badid1.md", "badid2.md" })
                        Check(Store.Problems.Exists(p => p.StartsWith(s)), "이유를 알림: " + s, Store.Problems.Find(p => p.StartsWith(s)) ?? "");
                    Check(Store.Problems.Exists(p => p.Contains("아이디가 겹쳐")), "이유를 알림: 중복 아이디");
                    Notice notice = app.NoticeWindow;
                    Check(notice != null && notice.Visible && notice.Has(Notice.LoadKey), "읽기 문제 알림 창이 떠 있음");

                    string normal = Path.Combine(NotesDir, "정상 메모-" + Id(1) + ".md");
                    Check(File.GetLastWriteTimeUtc(normal) == new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), "정상 메모 파일은 다시 쓰지 않음");
                    Check(File.Exists(Path.Combine(NotesDir, "옛 이름-" + Id(2) + ".md")) && !File.Exists(Path.Combine(NotesDir, "옛 이름-" + Id(2).Substring(0, 8) + ".md")),
                        "예전 이름(앞 8자)이 전체 아이디 이름으로 바뀜");
                    Check(File.Exists(Path.Combine(NotesDir, "같은 제목-" + Twin1 + ".md")) && File.Exists(Path.Combine(NotesDir, "같은 제목-" + Twin2 + ".md")),
                        "제목·앞 8자가 같은 두 메모가 각자 파일로 남음");
                    Check(File.Exists(Path.Combine(NotesDir, "막힘-" + Id(15) + " (2).md")), "차지된 이름은 덮어쓰지 않고 다른 이름으로 저장");

                    var sb = new StringBuilder();
                    foreach (NoteData n in app.Data.Notes) sb.AppendLine(n.Id);
                    File.WriteAllText(Path.Combine(Base, "ids1.txt"), sb.ToString());
                    var names = new List<string>(Directory.GetFiles(NotesDir));
                    names.Sort(StringComparer.Ordinal);
                    File.WriteAllLines(Path.Combine(Base, "files1.txt"), names.ToArray(), Utf8);
                });
        }

        static void IdsRun2()
        {
            var stamps = new Dictionary<string, DateTime>();
            foreach (string f in Directory.GetFiles(NotesDir)) stamps[f] = File.GetLastWriteTimeUtc(f);
            RunApp(null,
                app =>
                {
                    Check(app.SaveNow(), "다시 실행: 저장 성공");
                    CheckNotes(app);
                    var ids = new List<string>();
                    foreach (NoteData n in app.Data.Notes) ids.Add(n.Id);
                    ids.Sort(StringComparer.Ordinal);
                    var before = new List<string>(File.ReadAllLines(Path.Combine(Base, "ids1.txt")));
                    before.Sort(StringComparer.Ordinal);
                    Check(string.Join(",", ids.ToArray()) == string.Join(",", before.ToArray()), "다시 실행해도 아이디가 그대로");
                    var names = new List<string>(Directory.GetFiles(NotesDir));
                    names.Sort(StringComparer.Ordinal);
                    Check(string.Join("|", names.ToArray()) == string.Join("|", File.ReadAllLines(Path.Combine(Base, "files1.txt"), Utf8)), "다시 실행해도 파일 목록이 그대로");
                    int rewritten = 0;
                    foreach (var kv in stamps) if (File.GetLastWriteTimeUtc(kv.Key) != kv.Value) rewritten++;
                    Check(rewritten == 0, "다시 실행 때 다시 쓴 파일 없음", rewritten + "개");
                    Check(Store.Problems.Count == 4, "다시 실행 때는 읽지 못한 파일 4개만 알림", Store.Problems.Count + "건");
                },
                app =>
                {

                    NoteData d = app.Data.Notes.Find(n => n.Text.Contains("MARK-02"));
                    string name = "옛 이름-" + d.Id + ".md";
                    Directory.CreateDirectory(TrashDir);
                    File.WriteAllText(Path.Combine(TrashDir, name), "OLD TRASH", Utf8);
                    app.DeleteNote(FormOf(app, d));
                    Check(!File.Exists(Path.Combine(NotesDir, name)), "삭제한 메모가 notes 에서 사라짐");
                    string[] trash = Directory.GetFiles(TrashDir);
                    Check(trash.Length == 2, "휴지통에 두 파일 모두 있음", trash.Length.ToString());
                    Check(File.ReadAllText(Path.Combine(TrashDir, name), Utf8) == "OLD TRASH", "휴지통의 기존 파일을 덮어쓰지 않음");
                    bool moved = false;
                    foreach (string t in trash) if (File.ReadAllText(t, Utf8).Contains("MARK-02")) moved = true;
                    Check(moved, "삭제한 메모 내용이 휴지통에 있음");
                    CheckPreserved();
                });
        }

        static string OriginalsDir { get { return Path.Combine(DataDir, "originals"); } }
        static string HashOf(string path) { return Originals.Hash(File.ReadAllBytes(path)); }

        static byte[] Bytes(string latin1)
        {
            var b = new byte[latin1.Length];
            for (int i = 0; i < b.Length; i++) b[i] = (byte)latin1[i];
            return b;
        }

        static byte[] Join(params byte[][] parts)
        {
            var all = new List<byte>();
            foreach (byte[] p in parts) all.AddRange(p);
            return all.ToArray();
        }

        class Ext
        {
            public string Kind, Name, Hash, Mark;
        }

        static readonly List<string> manifest = new List<string>();

        static void Put(string kind, string name, byte[] bytes, string mark = "")
        {
            string path = Path.Combine(NotesDir, name);
            File.WriteAllBytes(path, bytes);
            manifest.Add(kind + "\t" + name + "\t" + HashOf(path) + "\t" + mark);
        }

        static void ExternalPrepare()
        {
            Directory.CreateDirectory(NotesDir);
            File.WriteAllText(Path.Combine(DataDir, "settings.json"), "{\"NotesVisible\":false,\"StayPut\":true}");
            byte[] bom = { 0xEF, 0xBB, 0xBF };
            const string ascii = " plain ascii filler without any control bytes 0123456789\n";

            Put("own", "정상-" + Id(1) + ".md", Utf8.GetBytes(NoteText(Id(1), "정상\nEXT-O1", 1)), "EXT-O1");
            Put("legacy", "옛 이름-" + Id(2).Substring(0, 8) + ".md", Utf8.GetBytes(NoteText(Id(2), "옛 이름\nEXT-O2", 2)), "EXT-O2");
            Put("own", "중복-" + Id(5) + ".md", Utf8.GetBytes(NoteText(Id(5), "중복\nEXT-O5", 5)), "EXT-O5");

            Put("convert", "plain.md", Utf8.GetBytes("머리말 없음\nEXT-01\n"), "EXT-01");
            Put("convert", "bom.md", Join(bom, Utf8.GetBytes("BOM 있는 글\r\nEXT-02\r\n")), "EXT-02");
            Put("convert", "utf16.md", Join(new byte[] { 0xFF, 0xFE }, Encoding.Unicode.GetBytes("UTF-16 글\nEXT-03\n")), "EXT-03");
            Put("convert", "badid.md", Utf8.GetBytes(NoteText("not-a-guid", "나쁜 아이디\nEXT-04", 4)), "EXT-04");
            Put("convert", "중복 사본.md", Utf8.GetBytes(NoteText(Id(5), "중복\nEXT-05B", 6)), "EXT-05B");
            Put("convert", "lf.md", Utf8.GetBytes(NoteText(Id(6), "줄바꿈이 LF\nEXT-06", 7).Replace("\r\n", "\n")), "EXT-06");
            Put("convert", "renamed.md", Utf8.GetBytes(NoteText(Id(7), "이름만 다른 파일\nEXT-07", 8)), "EXT-07");
            Put("convert", "mz.md", Utf8.GetBytes("MZ세대 메모\nEXT-08\n"), "EXT-08");
            Put("convert", "pdf-mention.md", Utf8.GetBytes("메모: %PDF- 로 시작하면 PDF\nEXT-09\n"), "EXT-09");
            Put("convert", "BOM 정상-" + Id(10) + ".md", Join(bom, Utf8.GetBytes(NoteText(Id(10), "BOM 정상\nEXT-10", 9))), "EXT-10");
            Put("convert", "ascii.md", Bytes("plain ascii note\r\nEXT-11\r\n"), "EXT-11");

            Put("reject", "pdf.md", Bytes("%PDF-1.4\n1 0 obj\n<< /Type /Catalog >>\nendobj\ntrailer\n<< /Root 1 0 R >>\n%%EOF\n"));
            Put("reject", "pdf-bom.md", Join(bom, Bytes("%PDF-1.7\n" + ascii)));
            Put("reject", "rtf.md", Bytes("{\\rtf1\\ansi\\deff0 {\\fonttbl {\\f0 Arial;}} hello}\n"));
            Put("reject", "ps.md", Bytes("%!PS-Adobe-3.0\n%%Pages: 1\nshowpage\n"));
            Put("reject", "zip.md", Bytes("PK\x03\x04" + ascii));
            Put("reject", "zip-empty.md", Bytes("PK\x05\x06" + ascii));
            Put("reject", "gif.md", Bytes("GIF89a" + ascii));
            Put("reject", "png.md", Bytes("\x89PNG\r\n\x1A\n" + ascii));
            Put("reject", "jpg.md", Bytes("\xFF\xD8\xFF\xE0" + ascii));
            Put("reject", "ole.md", Bytes("\xD0\xCF\x11\xE0\xA1\xB1\x1A\xE1" + ascii));
            Put("reject", "webp.md", Bytes("RIFF$$$$WEBPVP8 " + ascii));
            Put("reject", "wav.md", Bytes("RIFF$$$$WAVEfmt " + ascii));
            Put("reject", "ogg.md", Bytes("OggS" + ascii));
            Put("reject", "flac.md", Bytes("fLaC" + ascii));
            Put("reject", "rar.md", Bytes("Rar!\x1A\x07" + ascii));
            Put("reject", "7z.md", Bytes("7z\xBC\xAF\x27\x1C" + ascii));
            Put("reject", "gz.md", Bytes("\x1F\x8B\x08" + ascii));
            Put("reject", "elf.md", Bytes("\x7F" + "ELF" + ascii));
            Put("reject", "sqlite.md", Bytes("SQLite format 3" + ascii));
            Put("reject", "nul.md", Utf8.GetBytes(NoteText(Id(12), "널 문자\n \0\0 끝", 12)));
            Put("reject", "exe.md", Bytes("MZ\x90\x00\x03\x00\x00\x00" + ascii));
            Put("reject", "broken.md", Join(Utf8.GetBytes("깨진 글자 "), new byte[] { 0xFF, 0xFE, 0x80, 0xC3 }));
            Put("reject", "huge.md", Utf8.GetBytes("큰 파일\n" + new string('x', 2 * 1024 * 1024)));

            File.WriteAllLines(Path.Combine(Base, "manifest.txt"), manifest.ToArray(), Utf8);
        }

        static List<Ext> ReadManifest()
        {
            var list = new List<Ext>();
            foreach (string line in File.ReadAllLines(Path.Combine(Base, "manifest.txt"), Utf8))
            {
                string[] p = line.Split('\t');
                list.Add(new Ext { Kind = p[0], Name = p[1], Hash = p[2], Mark = p[3] });
            }
            return list;
        }

        static int NotesWithHash(string hash)
        {
            int n = 0;
            foreach (string f in Directory.GetFiles(NotesDir)) if (HashOf(f) == hash) n++;
            return n;
        }

        static string Listing(string dir)
        {
            if (!Directory.Exists(dir)) return "";
            var lines = new List<string>();
            foreach (string f in Directory.GetFiles(dir)) lines.Add(Path.GetFileName(f) + "=" + HashOf(f));
            lines.Sort(StringComparer.Ordinal);
            return string.Join("|", lines.ToArray());
        }

        static bool SaveOk(AppData d)
        {
            try { Store.Save(d); return true; }
            catch (Exception ex) { Info("저장 예외", ex.Message); return false; }
        }

        static void ExternalBlocked(bool corrupt)
        {
            List<Ext> all = ReadManifest();
            string blocker = OriginalsDir;
            if (corrupt)
            {

                if (File.Exists(blocker)) File.Delete(blocker);
                Originals.BeforeVerify = tmp => File.AppendAllText(tmp, "x");
            }
            else File.WriteAllText(blocker, "폴더 자리를 차지한 파일");
            string blockerHash = corrupt ? null : HashOf(blocker);
            string before = Listing(NotesDir);

            AppData d = Store.Load();
            Check(SaveOk(d), "저장 성공");

            int own = all.FindAll(x => x.Kind == "own" || x.Kind == "legacy").Count;
            Check(d.Notes.Count == own, "앱 파일만 메모로 읽음", d.Notes.Count + " / 기대 " + own);
            foreach (Ext x in all)
            {
                string path = Path.Combine(NotesDir, x.Name);
                if (x.Kind == "legacy")
                {
                    Check(NotesWithHash(x.Hash) == 1, "앱의 예전 이름 파일: 내용 그대로 (이름만 새 방식)", x.Name);
                    continue;
                }
                Check(File.Exists(path) && HashOf(path) == x.Hash, "원본 해시·이름 그대로 [" + x.Kind + "]: " + x.Name);
                if (x.Kind == "own") continue;
                string why = Store.Problems.Find(p => p.StartsWith(x.Name + ":")) ?? "";
                Check(why.Length > 0, "이유를 알림: " + x.Name, why.Replace("\n", " "));
                if (x.Kind == "convert")
                {
                    Check(why.Contains("백업하지 못했습니다"), "백업 실패로 읽지 않음: " + x.Name);
                    Check(!d.Notes.Exists(n => n.Text.Contains(x.Mark)), "메모로 올리지 않음: " + x.Name);
                }
            }
            if (corrupt)
                Check(!Directory.Exists(OriginalsDir) || Directory.GetFiles(OriginalsDir).Length == 0, "확인에 실패한 백업은 남지 않음",
                    Directory.Exists(OriginalsDir) ? Directory.GetFiles(OriginalsDir).Length + "개" : "폴더 없음");
            else
                Check(File.Exists(blocker) && HashOf(blocker) == blockerHash, "originals 자리의 남의 파일을 건드리지 않음");
            Check(Directory.GetFiles(DataDir, "*.tmp", SearchOption.AllDirectories).Length == 0, "임시 파일 남지 않음");

            string after = Listing(NotesDir), legacyOld = "옛 이름-" + Id(2).Substring(0, 8) + ".md=", legacyNew = "옛 이름-" + Id(2) + ".md=";
            Check(after.Replace(legacyNew, legacyOld) == before.Replace(legacyNew, legacyOld), "notes 폴더의 다른 파일은 이름·내용이 모두 그대로");
        }

        static void ExternalConvert()
        {
            List<Ext> all = ReadManifest();
            if (File.Exists(OriginalsDir)) File.Delete(OriginalsDir);
            string normal = Path.Combine(NotesDir, "정상-" + Id(1) + ".md");
            DateTime stamp = File.GetLastWriteTimeUtc(normal);

            AppData d = Store.Load();
            Check(Store.ImportedNewFiles, "새로 읽어 들인 파일이 있다고 표시");
            Check(SaveOk(d), "저장 성공");

            List<Ext> converts = all.FindAll(x => x.Kind == "convert"), rejects = all.FindAll(x => x.Kind == "reject");
            Check(d.Notes.Count == all.Count - rejects.Count, "읽은 메모 수", d.Notes.Count + " / 기대 " + (all.Count - rejects.Count));
            string[] backups = Directory.Exists(OriginalsDir) ? Directory.GetFiles(OriginalsDir) : new string[0];
            Check(backups.Length == converts.Count, "백업 수 = 변환한 바깥 파일 수", backups.Length + " / 기대 " + converts.Count);

            foreach (Ext x in all)
            {
                string path = Path.Combine(NotesDir, x.Name);
                string backup = Path.Combine(OriginalsDir, x.Name + "." + x.Hash.Substring(0, 16) + ".orig");
                switch (x.Kind)
                {
                    case "reject":
                        Check(File.Exists(path) && HashOf(path) == x.Hash, "거부: 원본 해시·이름 그대로: " + x.Name);
                        Check(Store.Problems.Exists(p => p.StartsWith(x.Name + ":")), "거부: 이유를 알림: " + x.Name,
                            (Store.Problems.Find(p => p.StartsWith(x.Name + ":")) ?? "").Replace("\n", " "));
                        Check(Array.FindAll(backups, b => Path.GetFileName(b).StartsWith(x.Name + ".")).Length == 0, "거부: 백업·변환 없음: " + x.Name);
                        break;
                    case "convert":
                        Check(File.Exists(backup) && HashOf(backup) == x.Hash, "허용: 백업의 해시가 원본과 같음: " + x.Name);
                        Check(d.Notes.FindAll(n => n.Text.Contains(x.Mark)).Count == 1, "허용: 메모로 한 번 읽음: " + x.Name);
                        Check(CountContaining(x.Mark) == 1, "허용: 변환된 파일이 하나: " + x.Name, CountContaining(x.Mark).ToString());
                        NoteData note = d.Notes.Find(n => n.Text.Contains(x.Mark));
                        Check(note != null && Directory.GetFiles(NotesDir, "*-" + note.Id + ".md").Length == 1, "허용: 앱 방식 이름으로 저장됨: " + x.Name);
                        break;
                    case "own":
                        Check(File.Exists(path) && HashOf(path) == x.Hash, "앱 파일: 원본 해시·이름 그대로: " + x.Name);
                        Check(Array.FindAll(backups, b => Path.GetFileName(b).StartsWith(x.Name + ".")).Length == 0, "앱 파일: 백업하지 않음: " + x.Name);
                        break;
                    case "legacy":
                        Check(NotesWithHash(x.Hash) == 1, "앱의 예전 이름 파일: 내용 그대로: " + x.Name);
                        break;
                }
            }
            Check(File.GetLastWriteTimeUtc(normal) == stamp, "앱 파일은 다시 쓰지 않음");
            Check(Directory.GetFiles(DataDir, "*.tmp", SearchOption.AllDirectories).Length == 0, "임시 파일 남지 않음");
            File.WriteAllText(Path.Combine(Base, "originals1.txt"), Listing(OriginalsDir), Utf8);
            File.WriteAllText(Path.Combine(Base, "notes1.txt"), Listing(NotesDir), Utf8);
            File.WriteAllText(Path.Combine(Base, "count1.txt"), d.Notes.Count.ToString());
        }

        static void ExternalRerun()
        {
            List<Ext> all = ReadManifest();
            AppData d = Store.Load();
            Check(SaveOk(d), "저장 성공");
            int rejects = all.FindAll(x => x.Kind == "reject").Count;
            Check(d.Notes.Count.ToString() == File.ReadAllText(Path.Combine(Base, "count1.txt")), "메모 수 그대로", d.Notes.Count.ToString());
            Check(Listing(OriginalsDir) == File.ReadAllText(Path.Combine(Base, "originals1.txt"), Utf8), "백업이 늘거나 바뀌지 않음");
            Check(Listing(NotesDir) == File.ReadAllText(Path.Combine(Base, "notes1.txt"), Utf8), "notes 폴더의 이름·내용이 그대로");
            Check(Store.Problems.Count == rejects, "거부한 파일만 다시 알림", Store.Problems.Count + " / 기대 " + rejects);
            foreach (Ext x in all)
                if (x.Kind == "reject") Check(HashOf(Path.Combine(NotesDir, x.Name)) == x.Hash, "거부: 원본 해시 그대로: " + x.Name);
        }

        static string BodyOfCurrent(App app)
        {
            NoteData cur = app.Data.Notes.Find(n => n.Id == app.Data.CurrentId);
            if (cur == null) return "(없음)";
            NoteForm f = FindForm(app, cur);
            return cur.Text.Split('\n')[0] + (f != null && f.Visible ? " [보임]" : " [숨음]") + " " + app.PositionText(cur);
        }

        static void DeleteShowsPrevious()
        {
            RunApp(null,
                app =>
                {

                    app.GoTo(4);
                    NoteForm f = FindForm(app, app.Data.Notes[3]);
                    Check(f != null && f.Visible && BodyOfCurrent(app) == "합성 3 [보임] 4 / 6", "4번째 메모가 떠 있음", BodyOfCurrent(app));
                    app.DeleteNote(f);
                    Check(!f.Visible, "지운 메모 창은 사라짐");
                    Check(BodyOfCurrent(app) == "합성 2 [보임] 3 / 5", "4번째를 지우면 3번째가 보임", BodyOfCurrent(app));
                    Check(app.Data.NotesVisible, "말풍선이 떠 있는 상태로 기록");
                },
                app =>
                {

                    app.GoTo(1);
                    app.DeleteNote(FindForm(app, app.Data.Notes[0]));
                    Check(BodyOfCurrent(app) == "합성 1 [보임] 1 / 4", "첫 번째를 지우면 새 첫 번째가 보임", BodyOfCurrent(app));
                },
                app =>
                {

                    app.GoLast();
                    app.DeleteNote(FindForm(app, app.Data.Notes[app.Data.Notes.Count - 1]));
                    Check(BodyOfCurrent(app) == "합성 4 [보임] 3 / 3", "마지막을 지우면 그 앞 메모가 보임", BodyOfCurrent(app));
                },
                app =>
                {

                    NoteData other = app.Data.Notes[0];
                    app.DeleteNote(FormOf(app, other));
                    Check(BodyOfCurrent(app) == "합성 4 [보임] 2 / 2", "다른 메모를 지워도 보던 메모가 그대로", BodyOfCurrent(app));
                },
                app =>
                {
                    app.DeleteNote(FindForm(app, app.Data.Notes[1]));
                    Check(BodyOfCurrent(app) == "합성 2 [보임] 1 / 1", "둘 중 뒤를 지우면 남은 하나가 보임", BodyOfCurrent(app));
                },
                app =>
                {

                    app.DeleteNote(FindForm(app, app.Data.Notes[0]));
                    Check(app.Data.Notes.Count == 0 && app.Data.CurrentId == null && !app.Data.NotesVisible, "남은 메모가 없으면 닫힘");
                    Check(Forms(app).Count == 0, "창이 남지 않음", Forms(app).Count.ToString());
                    Check(Directory.GetFiles(TrashDir).Length == 6 && Directory.GetFiles(NotesDir).Length == 0, "지운 6개가 모두 휴지통에 있음",
                        Directory.GetFiles(TrashDir).Length + "개");
                });
        }

        static bool WaitFor(Func<bool> cond, int ms)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < ms)
            {
                Application.DoEvents();
                if (cond()) return true;
                System.Threading.Thread.Sleep(20);
            }
            return cond();
        }

        static string Drop(string name, byte[] bytes, bool old = true)
        {
            string staging = Path.Combine(Base, "drop-" + name);
            File.WriteAllBytes(staging, bytes);
            if (old) File.SetLastWriteTimeUtc(staging, DateTime.UtcNow.AddMinutes(-5));
            string dest = Path.Combine(NotesDir, name);
            File.Move(staging, dest);
            return HashOf(dest);
        }

        static void LiveImport()
        {
            string dropHash = null, pdfHash = null;
            int problems = 0;
            RunApp(null,
                app =>
                {
                    Check(app.Data.Notes.Count == 3, "처음 메모 3개");
                    app.GoTo(1);
                    dropHash = Drop("drop.md", Utf8.GetBytes("실시간 메모\nLIVE-01\n"));
                    pdfHash = Drop("pdf.md", Bytes("%PDF-1.4\n1 0 obj\nendobj\n%%EOF\n"));
                    Check(WaitFor(() => app.Data.Notes.Count == 4, 8000), "앱을 다시 켜지 않아도 넣은 파일이 메모로 읽힘", app.Data.Notes.Count + "개");
                    NoteData n = app.Data.Notes.Find(x => x.Text.Contains("LIVE-01"));
                    Check(n != null && File.Exists(Path.Combine(NotesDir, "실시간 메모-" + n.Id + ".md")) && !File.Exists(Path.Combine(NotesDir, "drop.md")),
                        "앱 방식 이름으로 저장됨");
                    string backup = Path.Combine(OriginalsDir, "drop.md." + dropHash.Substring(0, 16) + ".orig");
                    Check(File.Exists(backup) && HashOf(backup) == dropHash, "변환 전 원본이 originals 에 그대로 있음");
                    Check(app.NoticeWindow != null && app.NoticeWindow.Has(Notice.ImportKey), "불러왔다는 알림");
                    Check(HashOf(Path.Combine(NotesDir, "pdf.md")) == pdfHash && Store.Problems.Exists(p => p.StartsWith("pdf.md:")), "거부한 파일은 그대로 두고 이유를 알림");
                    Check(app.PositionText(app.Data.Notes[0]) == "1 / 4", "번호 표시가 늘어난 개수를 반영", app.PositionText(app.Data.Notes[0]));
                    problems = Store.Problems.Count;
                },
                app =>
                {
                    // 앱이 스스로 저장한 파일을 새 파일로 착각하지 않는다
                    NoteForm f = FormOf(app, app.Data.Notes[0]);
                    BoxOf(f).AppendText(" 고침");
                    Check(app.SaveNow(), "편집 저장");
                    app.NewNote();
                    BoxOf(FindForm(app, app.Data.Notes.Find(x => x.Id == app.Data.CurrentId))).AppendText("앱에서 만든 메모");
                    Check(app.SaveNow(), "새 메모 저장");
                    WaitFor(() => false, 2500);
                    Check(app.Data.Notes.Count == 5 && app.ImportExternal() == 0, "앱이 쓴 파일은 다시 읽어 들이지 않음", app.Data.Notes.Count + "개");
                    Check(Store.Problems.Count == problems, "거부한 파일을 되풀이해 알리지 않음", Store.Problems.Count + " / " + problems);
                },
                app =>
                {

                    NoteData src = app.Data.Notes[1];
                    string srcFile = Directory.GetFiles(NotesDir, "*-" + src.Id + ".md")[0], srcHash = HashOf(srcFile);
                    Drop("사본.md", File.ReadAllBytes(srcFile));
                    Check(WaitFor(() => app.Data.Notes.Count == 6, 8000), "같은 아이디의 사본이 별도 메모로 읽힘", app.Data.Notes.Count + "개");
                    Check(HashOf(srcFile) == srcHash, "원래 메모 파일은 그대로");
                    var ids = new HashSet<string>();
                    foreach (NoteData n in app.Data.Notes) ids.Add(n.Id);
                    Check(ids.Count == 6, "아이디가 겹치지 않음");
                },
                app =>
                {

                    string path = Path.Combine(NotesDir, "locked.md");
                    using (var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        byte[] part = Utf8.GetBytes("쓰는 중\n");
                        fs.Write(part, 0, part.Length);
                        fs.Flush();
                        WaitFor(() => false, 3000);
                        Check(app.Data.Notes.Count == 6 && !Store.Problems.Exists(p => p.StartsWith("locked.md")), "쓰는 중인 파일은 읽지 않고 기다림");
                        byte[] rest = Utf8.GetBytes("LIVE-LOCK\n");
                        fs.Write(rest, 0, rest.Length);
                    }
                    Check(WaitFor(() => app.Data.Notes.Count == 7, 10000), "다 쓰이면 읽어 들임", app.Data.Notes.Count + "개");
                    Check(app.Data.Notes.Exists(x => x.Text.Contains("쓰는 중") && x.Text.Contains("LIVE-LOCK")), "끝까지 쓰인 내용으로 읽음");
                    Check(Directory.GetFiles(DataDir, "*.tmp", SearchOption.AllDirectories).Length == 0, "임시 파일 남지 않음");
                    Check(WaitFor(() => !app.NoticeWindow.Has(Notice.ImportKey), 9000), "불러왔다는 알림은 스스로 사라짐");
                });
        }

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern int GetTextFace(IntPtr hdc, int count, StringBuilder name);

        static string RealFace(Font font)
        {
            IntPtr hfont = font.ToHfont(), dc = Native.CreateCompatibleDC(IntPtr.Zero);
            try
            {
                IntPtr old = Native.SelectObject(dc, hfont);
                var sb = new StringBuilder(64);
                GetTextFace(dc, 64, sb);
                Native.SelectObject(dc, old);
                return sb.ToString();
            }
            finally { Native.DeleteDC(dc); Native.DeleteObject(hfont); }
        }

        static void FontsPrepare()
        {
            Directory.CreateDirectory(NotesDir);
            Write("글꼴-" + Id(1) + ".md", NoteText(Id(1), "글꼴 확인 Pretendard\n가나다라마바사 **굵은 글씨** *기울임*\nABC abc 0123456789\n- 목록 하나\n- 목록 둘", 1));
            File.WriteAllText(Path.Combine(DataDir, "settings.json"), "{\"NotesVisible\":false,\"StayPut\":true}");
        }

        static void FontsRun()
        {
            RunApp(null,
                app =>
                {
                    Check(Fonts.Name == "Pretendard", "앱 글꼴", Fonts.Name);
                    app.GoTo(1);
                    NoteForm f = FindForm(app, app.Data.Notes[0]);
                    RichTextBox box = BoxOf(f);
                    Check(box.Font.Name == "Pretendard" && RealFace(box.Font) == "Pretendard", "메모 글꼴이 실제로 Pretendard 로 잡힘", box.Font.Name + " / " + RealFace(box.Font));
                    using (Font bold = Fonts.Make(10.5f, FontStyle.Bold))
                        Check(RealFace(bold) == "Pretendard", "굵은 글씨도 Pretendard", RealFace(bold));
                    Check(RealFace(App.MenuFont) == "Pretendard", "메뉴 글꼴", RealFace(App.MenuFont));

                    var faces = new List<string>();
                    foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(box.Rtf, @"\{\\f\d+[^;{}]*? ([^;{}]+);\}"))
                        if (!faces.Contains(m.Groups[1].Value)) faces.Add(m.Groups[1].Value);
                    Check(faces.Count > 0 && faces.Contains("Pretendard") && faces.TrueForAll(x => x == "Pretendard" || x == "Symbol"), "편집 칸의 글자가 모두 Pretendard (글머리표의 Symbol 제외)", string.Join(", ", faces.ToArray()));
                    app.OpenSearch();
                },
                app =>
                {
                    WaitFor(() => false, 600);
                    Rectangle all = SystemInformation.VirtualScreen;
                    foreach (Form w in Application.OpenForms)
                    {
                        if (!w.Visible || w.Width < 100) continue;
                        Rectangle r = Rectangle.Intersect(w.Bounds, all);
                        using (var bmp = new Bitmap(r.Width, r.Height))
                        {
                            using (Graphics g = Graphics.FromImage(bmp)) g.CopyFromScreen(r.Location, Point.Empty, r.Size);
                            bmp.Save(Path.Combine(Base, "shot-" + w.GetType().Name + ".png"));
                        }
                    }
                },
                app =>
                {
                    typeof(App).GetMethod("SearchClosed").Invoke(app, new object[] { true });
                    foreach (Form w in Application.OpenForms) if (w is SearchForm) w.Hide();
                    WaitFor(() => false, 600);
                    NoteForm f = FindForm(app, app.Data.Notes[0]);
                    Rectangle r = Rectangle.Intersect(f.Bounds, SystemInformation.VirtualScreen);
                    using (var bmp = new Bitmap(r.Width, r.Height))
                    {
                        using (Graphics g = Graphics.FromImage(bmp)) g.CopyFromScreen(r.Location, Point.Empty, r.Size);
                        bmp.Save(Path.Combine(Base, "shot-note.png"));
                    }
                });
        }

        static string LegacyDir { get { return Path.Combine(Base, "legacy-MemoHelper"); } }

        static string OldJson()
        {

            return "{\"Notes\":[" +
                "{\"Id\":\"" + Id(1) + "\",\"Text\":\"첫째\\nOLD-1\",\"X\":10,\"Y\":10,\"W\":260,\"H\":220,\"Color\":1,\"Sort\":1}," +
                "{\"Id\":\"\",\"Text\":\"둘째\\nOLD-2\",\"W\":260,\"H\":220,\"Color\":2}," +
                "{\"Id\":\"note-3\",\"Text\":\"셋째\\nOLD-3\",\"W\":260,\"H\":220,\"Sort\":3}," +
                "{\"Id\":\"" + Id(1) + "\",\"Text\":\"넷째 (아이디 겹침)\\nOLD-4\",\"W\":260,\"H\":220,\"Sort\":4}," +
                "{\"Text\":\"다섯째\\nOLD-5\",\"W\":260,\"H\":220,\"Md\":true}" +
                "],\"NotesVisible\":false,\"LastColor\":3,\"StayPut\":true}";
        }

        static readonly string[] OldMarks = { "OLD-1", "OLD-2", "OLD-3", "OLD-4", "OLD-5" };

        static string TreeHash(string dir, string ignore)
        {
            var sb = new StringBuilder();
            var files = new List<string>(Directory.GetFiles(dir, "*", SearchOption.AllDirectories));
            files.Sort(StringComparer.Ordinal);
            foreach (string f in files)
            {
                if (Path.GetFileName(f) == ignore) continue;
                sb.Append(f.Substring(dir.Length)).Append('=').Append(Sha(f)).Append(';');
            }
            return sb.ToString();
        }

        static void MigratePrepare()
        {
            Directory.CreateDirectory(Path.Combine(LegacyDir, "attachments", "deep"));
            File.WriteAllText(Path.Combine(LegacyDir, "notes.json"), OldJson(), Utf8);
            File.WriteAllText(Path.Combine(LegacyDir, "notes.json.bak"), OldJson(), Utf8);
            File.WriteAllText(Path.Combine(LegacyDir, "other.txt"), "최상위의 다른 파일", Utf8);
            File.WriteAllText(Path.Combine(LegacyDir, "attachments", "keep.txt"), "하위 폴더 자료", Utf8);
            File.WriteAllText(Path.Combine(LegacyDir, "attachments", "deep", "keep2.txt"), "더 깊은 자료", Utf8);
            File.WriteAllText(Path.Combine(Base, "legacy-hash.txt"), TreeHash(LegacyDir, "migrated-to-MemoDoumi.txt"), Utf8);
        }

        static void CheckMigrated(AppData d, int run)
        {
            Check(d.Notes.Count == OldMarks.Length, "메모 수", d.Notes.Count + " / 기대 " + OldMarks.Length);
            foreach (string m in OldMarks)
            {
                Check(d.Notes.FindAll(n => n.Text.Contains(m)).Count == 1, "메모리에 하나: " + m);
                Check(CountContaining(m) == 1, "파일로 하나: " + m, CountContaining(m).ToString());
            }
            Check(Directory.GetFiles(NotesDir, "*.md").Length == OldMarks.Length, "메모 파일 수 (중복 없음)", Directory.GetFiles(NotesDir, "*.md").Length.ToString());
            Check(File.Exists(Path.Combine(DataDir, "migration.done")), "완료 기록 있음");
            Check(!File.Exists(Path.Combine(DataDir, "migration.pending")), "진행 중 표시 없음");
            Check(!File.Exists(Path.Combine(DataDir, "notes.json")) && File.Exists(Path.Combine(DataDir, "notes.json.migrated")), "원본 json 은 이름만 바꿔 보존");
        }

        static void MigrateRun(int run)
        {
            Store.LegacyDir = LegacyDir;
            if (run == 3)
            {
                // data 폴더를 통째로 지운 뒤: 예전 폴더의 표시 때문에 옛 메모가 다시 들어오지 않아야 한다
                Directory.Delete(DataDir, true);
                Directory.CreateDirectory(DataDir);
            }
            AppData d = Store.Load();
            Check(TreeHash(LegacyDir, "migrated-to-MemoDoumi.txt") == File.ReadAllText(Path.Combine(Base, "legacy-hash.txt"), Utf8),
                "예전 폴더가 하위 폴더까지 그대로 남아 있음");
            Check(File.Exists(Path.Combine(LegacyDir, "attachments", "deep", "keep2.txt")), "하위 폴더 파일 존재");
            if (run == 3)
            {
                Check(d.Notes.Count == 0, "끝난 마이그레이션은 data 를 지워도 다시 하지 않음", d.Notes.Count.ToString());
                return;
            }
            CheckMigrated(d, run);
            Check(File.Exists(Path.Combine(LegacyDir, "migrated-to-MemoDoumi.txt")), "예전 폴더에 옮김 표시");
            Check(d.LastColor == 3 && d.StayPut, "예전 설정을 이어받음");
            if (run == 2) Check(Store.Problems.Count == 0, "다시 실행 때 문제 없음", string.Join(" / ", Store.Problems.ToArray()));
            Store.Save(d);
            CheckMigrated(d, run);
        }

        static void ResumePrepare()
        {
            Directory.CreateDirectory(DataDir);
            File.WriteAllText(Path.Combine(DataDir, "notes.json"), OldJson(), Utf8);
        }

        static void ResumeRun(int run)
        {
            Store.LegacyDir = Path.Combine(Base, "no-such-legacy");
            FileStream blocker = null;
            if (run == 1)
            {

                // notes 폴더가 이미 있으므로 "변환을 시작한 뒤 멈춘 상태" 표시를 함께 둔다.
                Directory.CreateDirectory(NotesDir);
                File.WriteAllText(Path.Combine(DataDir, "migration.pending"), "test");
                string id3 = Migration.StableId(3, "note-3", "셋째\nOLD-3");
                blocker = new FileStream(Path.Combine(NotesDir, "셋째-" + id3 + ".md.tmp"), FileMode.Create, FileAccess.Write, FileShare.None);
            }
            AppData d = Store.Load();
            if (run == 1)
            {
                Check(Store.Problems.Exists(p => p.Contains("모두 옮기지 못했습니다")), "실패를 알림", string.Join(" / ", Store.Problems.ToArray()));
                Check(File.Exists(Path.Combine(DataDir, "notes.json")), "실패 시 원본 json 그대로");
                Check(!File.Exists(Path.Combine(DataDir, "migration.done")), "실패 시 완료로 기록하지 않음");
                Check(File.Exists(Path.Combine(DataDir, "migration.pending")), "실패 시 진행 중 표시 유지");
                Check(Directory.GetFiles(NotesDir, "*.md").Length == 4, "성공한 4개만 파일로 있음", Directory.GetFiles(NotesDir, "*.md").Length.ToString());
                Check(d.Notes.Count == 5, "실패한 메모도 메모리에는 있음", d.Notes.Count.ToString());
                blocker.Dispose();
                File.Delete(blocker.Name);
                return;     // 저장하지 않고 끝낸다 (도중에 꺼진 상황)
            }
            CheckMigrated(d, run);
            if (run == 3) Check(Store.Problems.Count == 0, "세 번째 실행: 문제 없음");
            Store.Save(d);
            CheckMigrated(d, run);
        }

        static int NoticeWindows()
        {
            int n = 0;
            foreach (Form f in Application.OpenForms) if (f is Notice && f.Visible) n++;
            return n;
        }

        static void SaveFail()
        {
            FileStream blocker = null;
            string path = null;
            NoteData d0 = null;
            int asked = 0;
            RunApp(null,
                app =>
                {
                    d0 = app.Data.Notes[0];
                    NoteData d1 = app.Data.Notes[1];
                    app.ShowNote(d1, 0, 0);
                    RichTextBox b1 = BoxOf(FormOf(app, d1));
                    b1.Select(b1.TextLength, 0);
                    b1.SelectedText = " OTHER-EDIT";
                    app.ShowNote(d0, 0, 0);
                    RichTextBox b0 = BoxOf(FormOf(app, d0));
                    b0.Select(b0.TextLength, 0);
                    b0.SelectedText = " UNSAVED-EDIT";
                    path = FilesById()[d0.Id];
                    blocker = new FileStream(path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None);
                    Check(!app.SaveNow(), "저장 실패가 실패로 보고됨");
                    Check(app.SaveFailed, "실패 상태 표시");
                    Notice n = app.NoticeWindow;
                    Check(n != null && n.Visible && n.Has(Notice.SaveKey), "저장 실패 알림 창이 뜸", n == null ? "" : n.MessageOf(Notice.SaveKey).Replace("\n", " / "));
                    Application.DoEvents();
                    using (var bmp = new Bitmap(n.Width, n.Height))
                    {
                        n.DrawToBitmap(bmp, new Rectangle(0, 0, n.Width, n.Height));
                        bmp.Save(Path.Combine(Base, "notice.png"));
                    }
                    Check(d0.Text.Contains("UNSAVED-EDIT"), "실패한 변경이 메모리에 남아 있음");
                    Check(!File.ReadAllText(path, Utf8).Contains("UNSAVED-EDIT"), "디스크의 기존 파일은 그대로 (깨지지 않음)");
                    Check(CountContaining("OTHER-EDIT") == 1, "실패하지 않은 다른 메모는 저장됨");
                },
                app =>
                {
                    for (int i = 0; i < 20; i++) { app.SaveNow(); Application.DoEvents(); }
                    Check(app.NoticeWindow.ShownCount == 1 && NoticeWindows() == 1, "20번 더 실패해도 알림 창은 하나", "나타난 횟수 " + app.NoticeWindow.ShownCount + ", 창 " + NoticeWindows());
                    string log = File.ReadAllText(Path.Combine(DataDir, "error.log"), Utf8);
                    int count = 0, at = 0;
                    while ((at = log.IndexOf("SaveException", at) + 1) > 0) count++;
                    Check(count == 1, "같은 오류는 로그에 한 번만", count + "번");
                },
                app =>
                {
                    // 사용자가 알림을 닫은 뒤 자동 재시도가 또 실패해도 바로 다시 띄우지 않는다
                    app.NoticeWindow.Close();
                    Check(NoticeWindows() == 0, "알림을 닫을 수 있음");
                    for (int i = 0; i < 5; i++) { app.SaveNow(); Application.DoEvents(); }
                    Check(NoticeWindows() == 0 && app.NoticeWindow.ShownCount == 1, "닫은 직후에는 다시 뜨지 않음");
                    Check(d0.Text.Contains("UNSAVED-EDIT") && app.SaveFailed, "닫아도 변경은 계속 보관");
                },
                app =>
                {

                    app.ConfirmExit = m => { asked++; return false; };
                    app.Exit();
                    Check(asked == 1 && !app.Exiting, "저장 실패 상태의 종료는 확인을 거치고, 취소하면 계속 실행");
                },
                app =>
                {
                    blocker.Dispose();
                    File.Delete(path + ".tmp");
                    Check(app.SaveNow(), "원인이 풀리면 다시 저장 성공");
                    Check(!app.SaveFailed && NoticeWindows() == 0, "성공하면 실패 상태와 알림이 풀림");
                    Check(CountContaining("UNSAVED-EDIT") == 1 && File.ReadAllText(path, Utf8).Contains("UNSAVED-EDIT"), "보관하던 변경이 파일에 저장됨");
                    Check(Directory.GetFiles(NotesDir, "*.md").Length == 6, "메모 파일 수 그대로");
                    Check(Directory.GetFiles(DataDir, "*.tmp", SearchOption.AllDirectories).Length == 0, "임시 파일 남지 않음");
                });
        }

        static void SearchPrepare()
        {
            Plain(300);
            Write("한글-" + Id(900) + ".md", NoteText(Id(900), "한글 메모\n앞부분 **굵은 글자** 다음에 검색어 가 있고 *기울임* 도 있다", 900));
        }

        static void Search()
        {
            SearchForm search = null;
            TextBox input = null;
            System.Collections.IList hits = null;
            VirtualList list = null;
            FieldInfo cost = typeof(SearchForm).GetField("lastCostMs", Private);
            MethodInfo key = typeof(SearchForm).GetMethod("OnInputKeyDown", Private);
            Func<int, NoteData> noteAt = i => (NoteData)hits[i].GetType().GetField("Note").GetValue(hits[i]);
            Func<App, string, int> expected = (app, q) => app.Data.Notes.FindAll(n => Markdown.Plain(n).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0).Count;
            int before = 0;
            RunApp(null,
                app =>
                {
                    app.OpenSearch();
                    search = (SearchForm)Field(app, "search");
                    input = (TextBox)Field(search, "input");
                    hits = (System.Collections.IList)Field(search, "hits");
                    list = (VirtualList)Field(search, "list");
                    Check(hits.Count == 301 && list.Count == 301, "빈 검색어: 전체 목록", hits.Count.ToString());
                    Check(noteAt(0).Id == Id(900), "최근 메모가 맨 위");
                    cost.SetValue(search, 0.0);
                    input.Text = "BODY-1";
                    Check(hits.Count == expected(app, "BODY-1") && hits.Count == 111, "입력 즉시 결과 (가벼울 때는 지연 없음)", hits.Count.ToString());
                    Check(list.Current == 0, "첫 결과가 선택됨", list.Current.ToString());
                    search.Update();
                    using (var bmp = new Bitmap(list.Width, list.Height))
                    {
                        list.DrawToBitmap(bmp, new Rectangle(Point.Empty, list.Size));
                        int ink = 0;
                        for (int y = 2; y < Math.Min(bmp.Height, 60); y += 2)
                            for (int x = 2; x < bmp.Width; x += 2)
                                if (bmp.GetPixel(x, y).GetBrightness() < 0.6f) ink++;
                        Check(ink > 20, "결과 줄이 그려짐", "어두운 점 " + ink);
                    }
                    using (var bmp = new Bitmap(search.Width, search.Height))
                    {
                        search.DrawToBitmap(bmp, new Rectangle(0, 0, search.Width, search.Height));
                        bmp.Save(Path.Combine(Base, "search.png"));
                    }
                    for (int i = 0; i < 3; i++) key.Invoke(search, new object[] { input, new KeyEventArgs(Keys.Down) });
                    Check(list.Current == 3, "↓ 세 번: 넷째 줄 선택", list.Current.ToString());
                    NoteData want = noteAt(3);
                    key.Invoke(search, new object[] { input, new KeyEventArgs(Keys.Enter) });
                    NoteForm f = FindForm(app, want);
                    Check(f != null && f.Visible && app.Data.CurrentId == want.Id, "Enter: 고른 메모가 열림");
                    Check(f != null && BoxOf(f).SelectedText == "BODY-1", "찾은 글자가 선택됨", f == null ? "" : BoxOf(f).SelectedText);
                },
                app =>
                {
                    app.OpenSearch();
                    input.Text = "검색어";
                    Check(hits.Count == 1 && noteAt(0).Id == Id(900), "한글 검색어", hits.Count.ToString());
                    key.Invoke(search, new object[] { input, new KeyEventArgs(Keys.Enter) });
                    NoteForm f = FindForm(app, app.Data.Notes.Find(n => n.Id == Id(900)));
                    Check(f != null && BoxOf(f).SelectedText == "검색어", "서식이 있는 메모에서도 찾은 위치가 맞음", f == null ? "" : BoxOf(f).SelectedText);
                },
                app =>
                {

                    app.OpenSearch();
                    input.Text = "BODY-2";
                    before = hits.Count;
                    cost.SetValue(search, 100.0);
                    input.Text = "BODY-29";
                    input.Text = "BODY-299";
                    Check(hits.Count == before, "무거울 때: 입력 직후에는 목록이 그대로", hits.Count + " / " + before);
                },
                app => { },
                app =>
                {
                    Check(hits.Count == 1 && noteAt(0).Text.Contains("BODY-299"), "무거울 때: 입력이 멈춘 뒤 마지막 입력으로 한 번 찾음", hits.Count.ToString());

                    cost.SetValue(search, 100.0);
                    input.Text = "BODY-77";
                    key.Invoke(search, new object[] { input, new KeyEventArgs(Keys.Enter) });
                    NoteData cur = app.Data.Notes.Find(n => n.Id == app.Data.CurrentId);
                    Check(cur != null && cur.Text.Contains("BODY-77") && BoxOf(FindForm(app, cur)).SelectedText == "BODY-77",
                        "지연 중 Enter: 지금 입력의 결과가 열림", cur == null ? "" : cur.Text.Replace("\n", " "));
                });
        }

        static bool Alive(Font f) { try { return f.Height > 0; } catch { return false; } }

        static void Evict()
        {
            NoteForm f0 = null, pinnedForm = null;
            NoteData d0 = null, dp = null;
            string md0 = null;
            Size size0 = Size.Empty;
            uint user0 = 0, gdi0 = 0;
            RunApp(null,
                app =>
                {
                    d0 = app.Data.Notes[0];
                    app.ShowNote(d0, 0, 0);
                    f0 = FormOf(app, d0);
                    RichTextBox box = BoxOf(f0);
                    box.SelectAll();
                    box.SelectedText = "굵게 보통 기울임 별*표 끝\n둘째 줄 밑줄";
                    box.Select(0, 2); Markdown.Toggle(box, Markdown.Bold);
                    box.Select(6, 3); Markdown.Toggle(box, Markdown.Italic);
                    box.Select(box.TextLength - 2, 2); Markdown.Toggle(box, Markdown.Underline);
                    f0.Size = new Size(App.S(333), App.S(277));
                    md0 = Markdown.FromRich(box);
                    size0 = f0.Size;
                    Info("기록한 서식", md0.Replace("\n", "\\n"));
                    Check(md0.Contains("**굵게**") && md0.Contains("*기울임*") && md0.Contains("<u>밑줄</u>") && md0.Contains("별\\*표"), "시험용 서식이 들어감");

                    dp = app.Data.Notes[60];
                    app.ShowNote(dp, 0, 0);
                    pinnedForm = FormOf(app, dp);
                    pinnedForm.SetPinned(true);
                    app.OnPinChanged(pinnedForm);
                    pinnedForm.Location = new Point(300, 200);
                },
                app =>
                {
                    for (int i = 1; i <= 10; i++) { app.ShowNote(app.Data.Notes[i], 0, 0); Application.DoEvents(); }
                    user0 = UserObjects(); gdi0 = GdiObjects();
                    Info("창 10개를 연 뒤", "USER " + user0 + ", GDI " + gdi0 + ", 보관 창 " + Forms(app).Count);
                },
                app =>
                {
                    for (int i = 11; i <= 50; i++) { app.ShowNote(app.Data.Notes[i], 0, 0); Application.DoEvents(); }
                },
                app =>
                {
                    uint user1 = UserObjects(), gdi1 = GdiObjects();
                    Info("창 50개를 연 뒤", "USER " + user1 + ", GDI " + gdi1 + ", 보관 창 " + Forms(app).Count);
                    Check(Forms(app).Count <= App.MaxHiddenForms + 2, "보관 창 수가 상한 이내 (숨긴 8 + 말풍선 1 + 고정 1)", Forms(app).Count.ToString());
                    Check(user1 <= user0 + 120, "USER 객체가 창 수만큼 늘지 않음", user0 + " -> " + user1);
                    Check(f0.IsDisposed, "오래 숨긴 창은 해제됨");
                    Check(d0.Text == md0, "해제 전에 편집 내용을 메모에 반영", d0.Text.Replace("\n", "\\n"));
                    Check(!pinnedForm.IsDisposed && pinnedForm.Visible, "고정한 창은 유지");
                    NoteForm cur = FindForm(app, app.Data.Notes[50]);
                    Check(cur != null && cur.Visible, "표시 중인 말풍선은 유지");

                    bool fonts = Alive(App.MenuFont);
                    foreach (string name in new[] { "NoteFont", "PosFont", "TimeFont" })
                        fonts &= Alive((Font)typeof(NoteForm).GetField(name, BindingFlags.Static | BindingFlags.NonPublic).GetValue(null));
                    fonts &= Alive((Font)typeof(IconButton).GetField("IconFont", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null));
                    Check(fonts, "공유 글꼴이 살아 있음");
                    bool swatches = true;
                    for (int i = 0; i < Palette.Names.Length; i++) { try { swatches &= Palette.Swatch(i).Width == 16; } catch { swatches = false; } }
                    Check(swatches, "공유 색 그림이 살아 있음");
                },
                app =>
                {
                    app.ShowNote(d0, 0, 0);
                    NoteForm again = FormOf(app, d0);
                    Check(again != f0 && !again.IsDisposed && again.Visible, "다시 열면 새 창이 뜸");
                    RichTextBox box = BoxOf(again);
                    Check(Markdown.FromRich(box) == md0, "다시 연 창의 내용·서식이 같음", Markdown.FromRich(box).Replace("\n", "\\n"));
                    box.Select(0, 2);
                    Check(Markdown.SelectionHas(box, Markdown.Bold), "굵게 서식 유지");
                    box.Select(6, 3);
                    Check(Markdown.SelectionHas(box, Markdown.Italic), "기울임 서식 유지");
                    Check(again.Size == size0, "크기 유지", again.Size + " / " + size0);
                    Point expect = app.NoteScreenPos(d0);
                    Check(again.Location == expect, "위치 유지 (말풍선은 강아지 기준)", again.Location + " / " + expect);
                    Check(pinnedForm.Location == new Point(300, 200) && dp.X == 300 && dp.Y == 200, "고정 메모 위치 유지", pinnedForm.Location.ToString());
                    // 색 메뉴와 편집 메뉴가 새 창에서도 열리는지 (공유 리소스를 쓰는 부분)
                    var colorMenu = (ContextMenuStrip)Field(again, "colorMenu");
                    colorMenu.Show(again, new Point(5, 5));
                    Application.DoEvents();
                    Check(colorMenu.Visible, "색 메뉴 열림");
                    colorMenu.Close();
                },
                app =>
                {

                    for (int round = 0; round < 3; round++)
                        for (int i = 1; i <= 50; i++) { app.ShowNote(app.Data.Notes[i], 0, 0); Application.DoEvents(); }
                },
                app =>
                {
                    uint user2 = UserObjects(), gdi2 = GdiObjects();
                    Info("창 200번을 연 뒤", "USER " + user2 + ", GDI " + gdi2 + ", 보관 창 " + Forms(app).Count);
                    Check(user2 <= user0 + 120, "되풀이해도 USER 객체가 늘지 않음", user0 + " -> " + user2);
                    Check(app.SaveNow(), "저장");
                    File.WriteAllText(Path.Combine(Base, "md0.txt"), md0, Utf8);
                    File.WriteAllText(Path.Combine(Base, "size0.txt"), d0.W + "x" + d0.H);
                });
        }

        static void EvictReload()
        {
            AppData d = Store.Load();
            NoteData d0 = d.Notes.Find(n => n.Id == Id(1));
            Check(d0 != null && d0.Text == File.ReadAllText(Path.Combine(Base, "md0.txt"), Utf8), "다시 실행해도 내용·서식 그대로");
            Check(d0 != null && d0.W + "x" + d0.H == File.ReadAllText(Path.Combine(Base, "size0.txt")), "다시 실행해도 크기 그대로");
            Check(d.Notes.Count == 80, "메모 수", d.Notes.Count.ToString());
            NoteData dp = d.Notes.Find(n => n.Id == Id(61));
            Info("고정 메모의 저장된 위치 (실행 중에는 300,200)", dp == null ? "없음" : "pinned=" + dp.Pinned + " anchored=" + dp.Anchored + " X=" + dp.X + " Y=" + dp.Y);
        }
    }
}
