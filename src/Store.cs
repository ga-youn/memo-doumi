using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;

namespace MemoDoumi
{
    [DataContract]
    public class NoteData
    {
        [DataMember(Order = 0)] public string Id;
        [DataMember(Order = 1)] public string Text;
        [DataMember(Order = 2)] public int X;
        [DataMember(Order = 3)] public int Y;
        [DataMember(Order = 4)] public int W;
        [DataMember(Order = 5)] public int H;
        [DataMember(Order = 6)] public int Color;
        [DataMember(Order = 7)] public bool Pinned;
        [DataMember(Order = 8)] public bool Anchored;     // true 면 X,Y 는 강아지 기준 상대 위치
        [DataMember(Order = 9)] public bool Closed;
        [DataMember(Order = 10)] public long Sort;
        [DataMember(Order = 11)] public bool Md;
        [DataMember(Order = 12)] public long CreatedUtc;
        [DataMember(Order = 13)] public long ModifiedUtc;

        // 서식 기호를 뺀 본문을 한 번만 계산해 두는 자리 (저장하지 않는다)
        internal string PlainSource, PlainValue;
    }

    [DataContract]
    public class AppData
    {
        [DataMember(Order = 0)] public List<NoteData> Notes;
        [DataMember(Order = 1)] public bool NotesVisible;
        [DataMember(Order = 2)] public int LastColor;
        [DataMember(Order = 3)] public bool HasMascotPos;
        [DataMember(Order = 4)] public int MascotX;
        [DataMember(Order = 5)] public int MascotY;
        [DataMember(Order = 6)] public bool StayPut;
        [DataMember(Order = 7)] public string CurrentId;
    }

    internal class SaveException : Exception
    {
        public readonly List<string> Failures;

        public SaveException(List<string> failures, Exception first)
            : base(failures.Count == 1 ? failures[0] : failures[0] + " 외 " + (failures.Count - 1) + "건", first)
        {
            Failures = failures;
        }
    }

    internal static class Store
    {
        public const int MaxNoteBytes = 1024 * 1024;
        public const int MaxNotes = 20000;
        public const int MaxSide = 10000;
        public const int MaxCoord = 100000;

        internal static string LegacyDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MemoHelper");

        static readonly string FallbackDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MemoDoumi");
        public static readonly string Dir = ResolveDir();
        static readonly string NotesDir = Path.Combine(Dir, "notes");
        static readonly string TrashDir = Path.Combine(Dir, "trash");
        static readonly string OriginalsDir = Path.Combine(Dir, "originals");
        static readonly string SettingsPath = Path.Combine(Dir, "settings.json");
        static readonly string OldJsonPath = Path.Combine(Dir, "notes.json");
        static readonly DataContractJsonSerializer Serializer = new DataContractJsonSerializer(typeof(AppData));
        static readonly Encoding Utf8 = new UTF8Encoding(false);
        static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

        class Saved
        {
            public string Path;
            public string Canon;
            public int Gen;
            string text;
            int x, y, w, h, color;
            bool pinned, anchored, closed, md;
            long sort, created, modified;

            public bool Matches(NoteData n)
            {
                return ReferenceEquals(text, n.Text) && w == n.W && h == n.H && color == n.Color
                    && anchored == n.Anchored && (!n.Anchored || (x == n.X && y == n.Y))
                    && pinned == n.Pinned && closed == n.Closed && md == n.Md
                    && sort == n.Sort && created == n.CreatedUtc && modified == n.ModifiedUtc;
            }

            public void Take(NoteData n)
            {
                text = n.Text; x = n.X; y = n.Y; w = n.W; h = n.H; color = n.Color;
                pinned = n.Pinned; anchored = n.Anchored; closed = n.Closed; md = n.Md;
                sort = n.Sort; created = n.CreatedUtc; modified = n.ModifiedUtc;
            }
        }

        static readonly Dictionary<string, Saved> saved = new Dictionary<string, Saved>(StringComparer.Ordinal);
        static int generation;
        static string savedSettings;

        public static readonly List<string> Problems = new List<string>();

        static string ResolveDir()
        {
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
            try
            {
                Directory.CreateDirectory(dir);
                string probe = Path.Combine(dir, ".write-test");
                File.WriteAllText(probe, "");
                File.Delete(probe);
                return dir;
            }
            catch
            {
                return FallbackDir;
            }
        }

        public static void Close() { }

        static readonly long MinTicks = new DateTime(2000, 1, 1).Ticks;
        static readonly long MaxTicks = new DateTime(9000, 1, 1).Ticks;

        public static bool ValidTicks(long t) { return t >= MinTicks && t <= MaxTicks; }

        public static bool ImportedNewFiles;

        public static AppData Load()
        {
            Problems.Clear();
            try { Migration.CopyLegacy(LegacyDir, Dir); }
            catch (Exception ex)
            {
                App.Log(ex);
                Problems.Add("예전 버전(MemoHelper)의 메모를 가져오지 못했습니다. 원본은 그대로 있으며 다음 실행 때 다시 시도합니다.\n  " + ex.Message);
            }

            bool firstRun = !Directory.Exists(NotesDir);
            Directory.CreateDirectory(NotesDir);

            bool hadSettings = File.Exists(SettingsPath);
            AppData d = ReadJson(SettingsPath);
            if (d == null)
            {
                if (hadSettings) KeepUnreadableSettings();
                hadSettings = false;
                d = new AppData();
                d.NotesVisible = true;
            }
            d.Notes = new List<NoteData>();

            saved.Clear();
            ignored.Clear();
            bool pending;
            ReadNoteFiles(d, false, out pending);

            if (File.Exists(OldJsonPath) && (firstRun || File.Exists(Migration.PendingPath(Dir))))
                ConvertOldJson(d, hadSettings);

            d.Notes.Sort((a, b) => a.Sort.CompareTo(b.Sort));
            Normalize(d);
            if (savedSettings == null && hadSettings) savedSettings = SettingsJson(d);
            return d;
        }

        static void KeepUnreadableSettings()
        {
            try
            {
                string keep = SettingsPath + ".unreadable";
                if (!File.Exists(keep)) File.Copy(SettingsPath, keep);
                Problems.Add("settings.json 을 읽지 못해 기본 설정으로 시작합니다. 원래 파일은 settings.json.unreadable 로 남겼습니다.");
            }
            catch (Exception ex) { App.Log(ex); }
        }

        class Entry
        {
            public string Path, Content, Stamp;
            public byte[] Bytes;
            public NoteData Note;
            public bool HadHeader, BadId;
            public int Rank;
        }

        static readonly Dictionary<string, string> ignored = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static List<NoteData> ImportNew(AppData d, out bool pending)
        {
            pending = false;
            if (!Directory.Exists(NotesDir)) return new List<NoteData>();
            List<NoteData> added = ReadNoteFiles(d, true, out pending);
            if (added.Count > 0)
            {
                d.Notes.Sort((a, b) => a.Sort.CompareTo(b.Sort));
                Normalize(d);
            }
            return added;
        }

        internal static string NotesFolder { get { return NotesDir; } }

        static string StampOf(FileInfo fi) { return fi.Length + ":" + fi.LastWriteTimeUtc.Ticks; }

        static List<NoteData> ReadNoteFiles(AppData d, bool live, out bool pending)
        {
            pending = false;
            var added = new List<NoteData>();
            string[] files = Directory.GetFiles(NotesDir, "*.md");
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Saved s in saved.Values) if (s.Path != null) known.Add(s.Path);
            var entries = new List<Entry>();
            int skippedForCount = 0;
            foreach (string path in files)
            {
                if (!path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) continue;
                if (known.Contains(path)) continue;
                string name = Path.GetFileName(path);
                if (d.Notes.Count + entries.Count >= MaxNotes) { skippedForCount++; continue; }
                string stamp = null;
                try
                {
                    var fi = new FileInfo(path);
                    stamp = StampOf(fi);
                    string seen;
                    if (ignored.TryGetValue(path, out seen) && seen == stamp) continue;

                    if (live && Math.Abs((DateTime.UtcNow - fi.LastWriteTimeUtc).TotalSeconds) < 1.5) { pending = true; continue; }
                    long length = fi.Length;
                    if (length > MaxNoteBytes)
                    {
                        ignored[path] = stamp;
                        Skip(name, "파일이 너무 큽니다 (" + (length / 1024) + "KB, 한도 " + (MaxNoteBytes / 1024) + "KB)");
                        continue;
                    }

                    byte[] bytes = File.ReadAllBytes(path);
                    if (bytes.Length > MaxNoteBytes)
                    {
                        ignored[path] = stamp;
                        Skip(name, "파일이 너무 큽니다 (" + (bytes.Length / 1024) + "KB, 한도 " + (MaxNoteBytes / 1024) + "KB)");
                        continue;
                    }
                    string kind = FileKind.Binary(bytes);
                    if (kind != null)
                    {
                        ignored[path] = stamp;
                        Skip(name, "메모가 아니라 " + kind + " 파일로 보입니다");
                        continue;
                    }
                    string content = Decode(bytes);
                    if (content.IndexOf('\0') >= 0)
                    {
                        ignored[path] = stamp;
                        Skip(name, "글자 파일이 아닙니다");
                        continue;
                    }
                    var e = new Entry();
                    e.Stamp = stamp;
                    e.Path = path;
                    e.Bytes = bytes;
                    e.Content = content;
                    e.Note = Parse(content, out e.HadHeader, out e.BadId);
                    string id = e.Note.Id;
                    e.Rank = id == null ? 2 : name.IndexOf(id, StringComparison.OrdinalIgnoreCase) >= 0 ? 0 : 1;
                    entries.Add(e);
                }
                catch (DecoderFallbackException)
                {
                    if (stamp != null) ignored[path] = stamp;
                    Skip(name, "UTF-8 로 읽을 수 없는 글자가 있습니다");
                }
                catch (Exception ex)
                {

                    if (live && (ex is IOException || ex is UnauthorizedAccessException)) { pending = true; continue; }
                    if (stamp != null) ignored[path] = stamp;
                    App.Log(ex);
                    Skip(name, ex.Message);
                }
            }
            if (skippedForCount > 0 && !live)
                Problems.Add("메모 파일이 너무 많아 " + skippedForCount + "개는 읽지 않았습니다 (한도 " + MaxNotes + "개). 파일은 그대로 있습니다.");

            var ordered = new List<Entry>(entries.Count);
            for (int rank = 0; rank <= 2; rank++)
                foreach (Entry e in entries) if (e.Rank == rank) ordered.Add(e);

            int backedUp = 0;
            foreach (Entry e in ordered)
            {
                NoteData n = e.Note;
                string name = Path.GetFileName(e.Path);
                bool fresh = false;
                string remark = null;
                if (n.Id == null)
                {

                    if (e.BadId) remark = name + ": 머리말의 메모 아이디가 올바르지 않아 새 아이디를 붙였습니다. 본문은 그대로입니다.";
                    if (!e.HadHeader) n.Sort = SafeTicks(() => File.GetCreationTimeUtc(e.Path).Ticks);
                    fresh = true;
                }
                else if (saved.ContainsKey(n.Id))
                {
                    remark = name + ": 다른 메모와 아이디가 겹쳐 별도 메모로 읽었습니다. 두 파일 모두 그대로 남습니다.";
                    fresh = true;
                }

                if (!ValidTicks(n.CreatedUtc))
                    n.CreatedUtc = ValidTicks(n.Sort) ? n.Sort : SafeTicks(() => File.GetCreationTimeUtc(e.Path).Ticks);
                if (!ValidTicks(n.ModifiedUtc))
                    n.ModifiedUtc = Math.Max(n.CreatedUtc, SafeTicks(() => File.GetLastWriteTimeUtc(e.Path).Ticks));

                // 앱의 정규 형식과 다른 파일은 변환 전에 원본을 보존한다.
                bool sameBytes = !fresh && SameBytes(Utf8.GetBytes(Compose(n)), e.Bytes);
                string canon = sameBytes ? FileNameOf(n) : null;
                bool ownName = sameBytes && IsOwnName(name, canon);
                if (!ownName && !(sameBytes && IsLegacyName(name, canon, n.Id)))
                {

                    try
                    {
                        if (Originals.Backup(OriginalsDir, name, e.Bytes)) backedUp++;
                    }
                    catch (Exception ex)
                    {
                        App.Log(ex);
                        ignored[e.Path] = e.Stamp;
                        Problems.Add(name + ": 읽지 않았습니다 — 바꿔 쓰기 전에 원본을 백업하지 못했습니다 (" + ex.Message +
                                     "). 파일은 notes 폴더에 그대로 있으며 다음 실행 때 다시 시도합니다.");
                        continue;
                    }
                }
                if (remark != null) Problems.Add(remark);
                if (fresh)
                {
                    n.Id = Guid.NewGuid().ToString("N");
                    ImportedNewFiles = true;
                }

                var s = new Saved();
                s.Path = e.Path;
                if (sameBytes)
                {

                    if (ownName) s.Canon = canon;
                    s.Take(n);
                }
                saved[n.Id] = s;
                d.Notes.Add(n);
                added.Add(n);
            }
            if (backedUp > 0)
                App.LogText("바깥에서 온 메모 파일 " + backedUp + "개의 원본을 data\\originals 에 남겼습니다.");
            return added;
        }

        static string Decode(byte[] bytes)
        {
            using (var r = new StreamReader(new MemoryStream(bytes), StrictUtf8, true)) return r.ReadToEnd();
        }

        static bool SameBytes(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        static bool IsLegacyName(string name, string canon, string id)
        {
            return string.Equals(name, canon.Substring(0, canon.Length - 3 - id.Length) + id.Substring(0, 8) + ".md", StringComparison.Ordinal);
        }

        static bool IsOwnName(string name, string canon)
        {
            if (string.Equals(name, canon, StringComparison.Ordinal)) return true;
            string head = canon.Substring(0, canon.Length - 3) + " (";
            if (!name.StartsWith(head, StringComparison.Ordinal) || !name.EndsWith(").md", StringComparison.Ordinal)) return false;
            int k;
            return int.TryParse(name.Substring(head.Length, name.Length - head.Length - 4), out k) && k >= 2;
        }

        static void Skip(string name, string reason)
        {
            Problems.Add(name + ": 읽지 않았습니다 — " + reason + ". 파일은 notes 폴더에 그대로 있습니다.");
        }

        static long SafeTicks(Func<long> read)
        {
            try
            {
                long t = read();
                return ValidTicks(t) ? t : DateTime.UtcNow.Ticks;
            }
            catch { return DateTime.UtcNow.Ticks; }
        }

        static void Normalize(AppData d)
        {
            long last = 0;
            foreach (NoteData n in d.Notes)
            {
                if (n.Text == null) n.Text = "";
                if (string.IsNullOrEmpty(n.Id)) n.Id = Guid.NewGuid().ToString("N");
                if (n.Sort <= 0) n.Sort = last + 1;
                last = Math.Max(last, n.Sort);
            }
        }

        internal static string NormalizeId(string raw)
        {
            Guid g;
            if (raw == null || !Guid.TryParse(raw.Trim(), out g) || g == Guid.Empty) return null;
            return g.ToString("N");
        }

        internal static NoteData Parse(string content, out bool hadHeader, out bool badId)
        {
            var n = new NoteData();
            hadHeader = badId = false;
            string text = content.Replace("\r\n", "\n");
            if (text.StartsWith("---\n"))
            {
                int end = text.IndexOf("\n---\n", 3, StringComparison.Ordinal);
                if (end >= 0)
                {
                    hadHeader = true;
                    foreach (string line in text.Substring(4, end - 4 + 1).Split('\n'))
                    {
                        int c = line.IndexOf(':');
                        if (c <= 0) continue;
                        string key = line.Substring(0, c).Trim(), val = line.Substring(c + 1).Trim();
                        switch (key)
                        {
                            case "id": n.Id = NormalizeId(val); badId = n.Id == null; break;
                            case "color": n.Color = Palette.Clamp(Int(val)); break;
                            case "size": { string[] p = val.Split('x'); if (p.Length == 2) { n.W = Int(p[0]); n.H = Int(p[1]); } } break;
                            case "pos": { string[] p = val.Split(','); if (p.Length == 2) { n.X = Int(p[0]); n.Y = Int(p[1]); n.Anchored = true; } } break;
                            case "pinned": n.Pinned = val == "true"; break;
                            case "closed": n.Closed = val == "true"; break;
                            case "format": n.Md = val == "md"; break;
                            case "created": n.CreatedUtc = ParseTime(val); break;
                            case "modified": n.ModifiedUtc = ParseTime(val); break;
                            case "order": long.TryParse(val, out n.Sort); break;
                        }
                    }
                    text = text.Substring(end + 5);
                }
            }
            if (!hadHeader) n.Md = true;
            if (text.EndsWith("\n")) text = text.Substring(0, text.Length - 1);
            n.Text = text;
            Sanitize(n);
            return n;
        }

        static void Sanitize(NoteData n)
        {
            n.Color = Palette.Clamp(n.Color);
            if (n.W < 0 || n.H < 0) n.W = n.H = 0;
            n.W = Math.Min(n.W, MaxSide);
            n.H = Math.Min(n.H, MaxSide);
            if (Math.Abs((long)n.X) > MaxCoord || Math.Abs((long)n.Y) > MaxCoord) { n.X = n.Y = 0; n.Anchored = false; }
            if (n.Sort < 0) n.Sort = 0;
            if (!ValidTicks(n.CreatedUtc)) n.CreatedUtc = 0;
            if (!ValidTicks(n.ModifiedUtc)) n.ModifiedUtc = 0;
        }

        const string TimeFormat = "yyyy-MM-dd HH:mm:ss";

        static long ParseTime(string s)
        {
            DateTime t;
            if (DateTime.TryParseExact(s, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out t))
                return t.ToUniversalTime().Ticks;
            return 0;
        }

        static string FormatTime(long utcTicks)
        {
            return new DateTime(utcTicks, DateTimeKind.Utc).ToLocalTime().ToString(TimeFormat, CultureInfo.InvariantCulture);
        }

        static int Int(string s)
        {
            int v;
            int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v);
            return v;
        }

        static void ConvertOldJson(AppData d, bool hadSettings)
        {
            AppData old = ReadJson(OldJsonPath) ?? ReadJson(OldJsonPath + ".bak");
            if (old == null)
            {
                Problems.Add("예전 형식의 notes.json 을 읽지 못해 옮기지 않았습니다. 파일은 data 폴더에 그대로 있습니다.");
                return;
            }
            try
            {
                Migration.WritePending(Dir);
                if (old.Notes == null) old.Notes = new List<NoteData>();
                if (!hadSettings)
                {
                    d.NotesVisible = old.NotesVisible;
                    d.LastColor = old.LastColor;
                    d.HasMascotPos = old.HasMascotPos;
                    d.MascotX = old.MascotX;
                    d.MascotY = old.MascotY;
                    d.StayPut = old.StayPut;
                }

                // 재시도 시 중복 생성을 막기 위해 원본 순서로 ID를 결정한다.
                var moved = new List<NoteData>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                int index = 0, already = 0;
                foreach (NoteData o in old.Notes)
                {
                    index++;
                    if (o == null) continue;
                    if (o.Text == null) o.Text = "";
                    string rawId = o.Id;
                    o.Id = NormalizeId(rawId) ?? Migration.StableId(index, rawId, o.Text);
                    if (!hadSettings && rawId != null && rawId == old.CurrentId) d.CurrentId = o.Id;
                    if (!seen.Add(o.Id)) o.Id = Migration.StableId(index, "dup:" + rawId, o.Text);
                    seen.Add(o.Id);
                    if (o.Sort <= 0) o.Sort = index;
                    Sanitize(o);
                    if (saved.ContainsKey(o.Id)) { already++; continue; }
                    moved.Add(o);
                    d.Notes.Add(o);
                }
                d.Notes.Sort((a, b) => a.Sort.CompareTo(b.Sort));
                Normalize(d);
                Save(d);

                foreach (NoteData o in moved)
                {
                    Saved s;
                    bool h, b;
                    if (!saved.TryGetValue(o.Id, out s) || !File.Exists(s.Path)
                        || Parse(File.ReadAllText(s.Path, StrictUtf8), out h, out b).Text != (o.Text ?? "").Replace("\r\n", "\n"))
                        throw new IOException("옮긴 메모를 다시 읽어 확인하지 못했습니다: " + Path.GetFileName(s == null ? o.Id : s.Path));
                }
                string kept = Migration.Finish(Dir, LegacyDir, OldJsonPath, moved.Count + already);
                App.LogText("예전 notes.json 의 메모 " + (moved.Count + already) + "개를 옮겼습니다 (이번 실행 " + moved.Count + "개). 원본: " + kept);
            }
            catch (Exception ex)
            {
                App.Log(ex);
                Problems.Add("예전 형식의 메모를 모두 옮기지 못했습니다. notes.json 은 그대로 있으며 다음 실행 때 이어서 옮깁니다.\n  " + ex.Message);
            }
        }

        static string Compose(NoteData n)
        {
            var sb = new StringBuilder((n.Text ?? "").Length + 256);
            sb.Append("---\n");
            sb.Append("id: ").Append(n.Id).Append('\n');
            sb.Append("color: ").Append(n.Color).Append('\n');
            sb.Append("size: ").Append(n.W).Append('x').Append(n.H).Append('\n');
            if (n.Anchored) sb.Append("pos: ").Append(n.X).Append(',').Append(n.Y).Append('\n');
            sb.Append("pinned: ").Append(n.Pinned ? "true" : "false").Append('\n');
            sb.Append("closed: ").Append(n.Closed ? "true" : "false").Append('\n');
            if (n.Md) sb.Append("format: md\n");
            if (ValidTicks(n.CreatedUtc)) sb.Append("created: ").Append(FormatTime(n.CreatedUtc)).Append('\n');
            if (ValidTicks(n.ModifiedUtc)) sb.Append("modified: ").Append(FormatTime(n.ModifiedUtc)).Append('\n');
            sb.Append("order: ").Append(n.Sort).Append('\n');
            sb.Append("---\n");
            sb.Append((n.Text ?? "").Replace("\r\n", "\n")).Append('\n');
            return sb.ToString().Replace("\n", "\r\n");
        }

        static string FileNameOf(NoteData n)
        {
            string plain = Markdown.Plain(n), title = "";
            int at = 0;
            while (at < plain.Length)
            {
                int nl = plain.IndexOf('\n', at);
                if (nl < 0) nl = plain.Length;
                string line = plain.Substring(at, nl - at).Trim();
                if (line.Length > 0) { title = line; break; }
                at = nl + 1;
            }
            var sb = new StringBuilder();
            foreach (char ch in title)
            {
                if (sb.Length >= 30) break;
                if (Array.IndexOf(InvalidNameChars, ch) >= 0 || char.IsControl(ch) || char.IsSurrogate(ch)) sb.Append(' ');
                else sb.Append(ch);
            }
            title = sb.ToString().Trim().TrimEnd('.');
            if (title.Length == 0) title = "메모";
            return title + "-" + n.Id + ".md";
        }

        static readonly char[] InvalidNameChars = Path.GetInvalidFileNameChars();
        static readonly string NotesRoot = Path.GetFullPath(NotesDir).TrimEnd('\\', '/') + "\\";

        static void EnsureInNotes(string path)
        {
            string full = Path.GetFullPath(path);
            if (!full.StartsWith(NotesRoot, StringComparison.OrdinalIgnoreCase)
                || full.IndexOfAny(new[] { '\\', '/' }, NotesRoot.Length) >= 0
                || full.Length == NotesRoot.Length)
                throw new IOException("저장 경로가 notes 폴더 밖입니다: " + path);
        }

        static string ChoosePath(string canon, string ownPath)
        {
            string stem = canon.Substring(0, canon.Length - 3);
            string path = Path.Combine(NotesDir, canon);
            for (int i = 2; i < 1000; i++)
            {
                if (ownPath != null && string.Equals(path, ownPath, StringComparison.OrdinalIgnoreCase)) return path;
                if (!File.Exists(path) && !Directory.Exists(path)) return path;
                path = Path.Combine(NotesDir, stem + " (" + i + ").md");
            }
            throw new IOException("쓸 수 있는 파일 이름을 찾지 못했습니다: " + canon);
        }

        public static void Save(AppData d)
        {
            List<string> failures = null;
            Exception first = null;
            try { Directory.CreateDirectory(NotesDir); }
            catch (Exception ex) { Fail(ref failures, ref first, "notes 폴더", ex); }

            generation++;
            int known = 0;
            for (int i = 0; i < d.Notes.Count; i++)
            {
                NoteData n = d.Notes[i];
                Saved s;
                if (saved.TryGetValue(n.Id, out s))
                {
                    if (s.Gen != generation) { s.Gen = generation; known++; }
                    if (s.Canon != null && s.Matches(n)) continue;
                }
                try { if (WriteNote(n, s)) known++; }
                catch (Exception ex) { Fail(ref failures, ref first, "메모 \"" + TitleOf(n) + "\"", ex); }
            }

            if (known != saved.Count)
            {
                foreach (string id in new List<string>(saved.Keys))
                {
                    Saved s = saved[id];
                    if (s.Gen == generation) continue;
                    try
                    {
                        if (s.Path != null && File.Exists(s.Path))
                        {
                            Directory.CreateDirectory(TrashDir);
                            File.Move(s.Path, FreeTrashPath(Path.GetFileName(s.Path)));
                        }
                        saved.Remove(id);
                    }
                    catch (Exception ex) { Fail(ref failures, ref first, "삭제한 메모 " + Path.GetFileName(s.Path), ex); }
                }
            }

            try
            {
                string settings = SettingsJson(d);
                if (settings != savedSettings)
                {
                    WriteAtomic(SettingsPath, settings);
                    savedSettings = settings;
                }
            }
            catch (Exception ex) { Fail(ref failures, ref first, "설정", ex); }

            if (failures != null) throw new SaveException(failures, first);
        }

        static void Fail(ref List<string> failures, ref Exception first, string what, Exception ex)
        {
            if (failures == null) failures = new List<string>();
            if (first == null) first = ex;
            failures.Add(what + ": " + ex.Message);
        }

        static string TitleOf(NoteData n)
        {
            string name = FileNameOf(n);
            return name.Substring(0, Math.Max(0, name.Length - 3 - 1 - n.Id.Length));
        }

        static bool WriteNote(NoteData n, Saved s)
        {
            if (NormalizeId(n.Id) != n.Id) throw new IOException("메모 아이디가 올바르지 않습니다");
            string canon = FileNameOf(n);
            string path = s != null && s.Path != null && canon == s.Canon ? s.Path : ChoosePath(canon, s == null ? null : s.Path);
            EnsureInNotes(path);
            bool contentCurrent = s != null && s.Matches(n);

            if (s != null && s.Path != null && !string.Equals(s.Path, path, StringComparison.Ordinal))
            {

                bool moved = File.Exists(s.Path);
                if (moved)
                {

                    if (!contentCurrent)
                    {
                        WriteAtomic(s.Path, Compose(n));
                        s.Canon = null;
                        s.Take(n);
                        contentCurrent = true;
                    }
                    File.Move(s.Path, path);
                }
                s.Path = path;
                s.Canon = null;
                contentCurrent &= moved;
            }
            if (!contentCurrent || !File.Exists(path)) WriteAtomic(path, Compose(n));

            bool added = s == null;
            if (added)
            {
                s = new Saved();
                s.Gen = generation;
                saved[n.Id] = s;
            }
            s.Path = path;
            s.Canon = canon;
            s.Take(n);
            return added;
        }

        static string FreeTrashPath(string name)
        {
            string dest = Path.Combine(TrashDir, name);
            if (!File.Exists(dest)) return dest;

            string stem = Path.GetFileNameWithoutExtension(name), ext = Path.GetExtension(name);
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            for (int i = 1; ; i++)
            {
                dest = Path.Combine(TrashDir, stem + " (" + stamp + (i == 1 ? "" : "-" + i) + ")" + ext);
                if (!File.Exists(dest)) return dest;
            }
        }

        static void WriteAtomic(string path, string content)
        {
            string tmp = path + ".tmp";
            byte[] bytes = Utf8.GetBytes(content);
            try
            {
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    fs.Write(bytes, 0, bytes.Length);
                    fs.Flush(true);
                }

                if (!Native.MoveFileEx(tmp, path, Native.MOVEFILE_REPLACE_EXISTING))
                    throw new IOException(new System.ComponentModel.Win32Exception().Message + ": " + Path.GetFileName(path),
                        System.Runtime.InteropServices.Marshal.GetHRForLastWin32Error());
            }
            catch
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                throw;
            }
        }

        static string SettingsJson(AppData d)
        {
            var s = new AppData();
            s.NotesVisible = d.NotesVisible;
            s.LastColor = d.LastColor;
            s.HasMascotPos = d.HasMascotPos;
            s.MascotX = d.MascotX;
            s.MascotY = d.MascotY;
            s.StayPut = d.StayPut;
            s.CurrentId = d.CurrentId;
            using (var ms = new MemoryStream())
            {
                Serializer.WriteObject(ms, s);
                return Utf8.GetString(ms.ToArray());
            }
        }

        static AppData ReadJson(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                using (var fs = File.OpenRead(path))
                    return (AppData)Serializer.ReadObject(fs);
            }
            catch
            {
                return null;
            }
        }
    }

    internal static class FileKind
    {
        struct Sig
        {
            public readonly string Name;
            public readonly byte[] Head;
            public readonly int At2;
            public readonly byte[] Head2;
            public Sig(string name, string head, int at2 = 0, string head2 = null)
            {
                Name = name;
                Head = Latin1(head);
                At2 = at2;
                Head2 = head2 == null ? null : Latin1(head2);
            }
        }

        static byte[] Latin1(string s)
        {
            var b = new byte[s.Length];
            for (int i = 0; i < s.Length; i++) b[i] = (byte)s[i];
            return b;
        }

        static readonly Sig[] Sigs =
        {
            new Sig("PDF", "%PDF-"),
            new Sig("PostScript", "%!PS-Adobe"),
            new Sig("RTF 문서", "{\\rtf"),
            new Sig("ZIP 계열(docx·xlsx·hwpx 등)", "PK\x03\x04"),
            new Sig("ZIP 계열(docx·xlsx·hwpx 등)", "PK\x05\x06"),
            new Sig("ZIP 계열(docx·xlsx·hwpx 등)", "PK\x07\x08"),
            new Sig("옛 Office·한글(hwp) 문서", "\xD0\xCF\x11\xE0\xA1\xB1\x1A\xE1"),
            new Sig("PNG 그림", "\x89PNG\r\n\x1A\n"),
            new Sig("GIF 그림", "GIF87a"),
            new Sig("GIF 그림", "GIF89a"),
            new Sig("JPEG 그림", "\xFF\xD8\xFF"),
            new Sig("WebP 그림", "RIFF", 8, "WEBP"),
            new Sig("WAV 소리", "RIFF", 8, "WAVE"),
            new Sig("AVI 영상", "RIFF", 8, "AVI "),
            new Sig("MP4 영상", "\x00\x00\x00", 4, "ftyp"),
            new Sig("Ogg 소리", "OggS"),
            new Sig("FLAC 소리", "fLaC"),
            new Sig("RAR 압축", "Rar!\x1A\x07"),
            new Sig("7z 압축", "7z\xBC\xAF\x27\x1C"),
            new Sig("gzip 압축", "\x1F\x8B\x08"),
            new Sig("ELF 실행", "\x7F" + "ELF"),
            new Sig("SQLite 데이터베이스", "SQLite format 3"),
        };

        public static string Binary(byte[] b)
        {
            int start = b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF ? 3 : 0;
            foreach (Sig s in Sigs)
                if (At(b, start, s.Head) && (s.Head2 == null || At(b, start + s.At2, s.Head2))) return s.Name;
            return null;
        }

        static bool At(byte[] b, int at, byte[] head)
        {
            if (b.Length - at < head.Length) return false;
            for (int i = 0; i < head.Length; i++) if (b[at + i] != head[i]) return false;
            return true;
        }
    }

    internal static class Originals
    {

        internal static Action<string> BeforeVerify { get; set; }

        public static bool Backup(string dir, string name, byte[] bytes)
        {
            string hash = Hash(bytes);
            Directory.CreateDirectory(dir);
            string stem = name + "." + hash.Substring(0, 16);
            string dest = Path.Combine(dir, stem + ".orig");
            for (int i = 2; File.Exists(dest) || Directory.Exists(dest); i++)
            {
                if (File.Exists(dest) && Matches(dest, bytes.Length, hash)) return false;
                if (i > 100) throw new IOException("백업 파일 이름을 정하지 못했습니다");
                dest = Path.Combine(dir, stem + " (" + i + ").orig");
            }
            string tmp = dest + ".tmp";
            try
            {
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    fs.Write(bytes, 0, bytes.Length);
                    fs.Flush(true);
                }
                if (BeforeVerify != null) BeforeVerify(tmp);
                if (!Matches(tmp, bytes.Length, hash)) throw new IOException("백업한 내용이 원본과 다릅니다");
                File.Move(tmp, dest);
            }
            catch
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                throw;
            }
            return true;
        }

        static bool Matches(string path, int length, string hash)
        {
            if (new FileInfo(path).Length != length) return false;
            return Hash(File.ReadAllBytes(path)) == hash;
        }

        public static string Hash(byte[] bytes)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
    }

    internal static class Migration
    {
        const string DoneName = "migration.done";
        const string PendingName = "migration.pending";
        const string LegacyMarkerName = "migrated-to-MemoDoumi.txt";

        public static string PendingPath(string dataDir) { return Path.Combine(dataDir, PendingName); }

        public static bool CopyLegacy(string legacyDir, string dataDir)
        {
            string src = Path.Combine(legacyDir, "notes.json");
            if (!File.Exists(src)) return false;

            if (Directory.Exists(Path.Combine(dataDir, "notes")) || File.Exists(Path.Combine(dataDir, "notes.json"))) return false;
            if (File.Exists(Path.Combine(dataDir, DoneName)) || File.Exists(Path.Combine(legacyDir, LegacyMarkerName))) return false;

            Directory.CreateDirectory(dataDir);
            string bak = src + ".bak";
            if (File.Exists(bak)) CopyVerified(bak, Path.Combine(dataDir, "notes.json.bak"));
            CopyVerified(src, Path.Combine(dataDir, "notes.json"));
            return true;
        }

        static void CopyVerified(string src, string dest)
        {
            if (File.Exists(dest)) return;
            string tmp = dest + ".copying";
            File.Copy(src, tmp, true);
            if (!SameContent(src, tmp))
            {
                File.Delete(tmp);
                throw new IOException("복사한 파일이 원본과 다릅니다: " + Path.GetFileName(src));
            }
            File.Move(tmp, dest);
        }

        static bool SameContent(string a, string b)
        {
            if (new FileInfo(a).Length != new FileInfo(b).Length) return false;
            return Hash(a) == Hash(b);
        }

        static string Hash(string path)
        {
            using (var sha = SHA256.Create())
            using (var fs = File.OpenRead(path))
                return Convert.ToBase64String(sha.ComputeHash(fs));
        }

        public static string StableId(int index, string rawId, string text)
        {
            using (var md5 = MD5.Create())
                return new Guid(md5.ComputeHash(Encoding.UTF8.GetBytes("legacy|" + index + "|" + rawId + "|" + text))).ToString("N");
        }

        public static void WritePending(string dataDir)
        {
            File.WriteAllText(PendingPath(dataDir), "notes.json 을 .md 로 옮기는 중입니다. 끝나면 이 파일은 사라집니다.\r\n" + DateTime.Now.ToString("o") + "\r\n");
        }

        public static string Finish(string dataDir, string legacyDir, string oldJsonPath, int notes)
        {
            string kept = oldJsonPath + ".migrated";
            for (int i = 2; File.Exists(kept); i++) kept = oldJsonPath + ".migrated" + i;
            bool fromLegacy = false;
            try
            {
                string legacyJson = Path.Combine(legacyDir, "notes.json");
                fromLegacy = File.Exists(legacyJson) && SameContent(legacyJson, oldJsonPath);
            }
            catch { }
            File.Move(oldJsonPath, kept);

            string text = "예전 메모를 옮겼습니다.\r\n시각: " + DateTime.Now.ToString("o") + "\r\n메모 수: " + notes +
                          "\r\n원본(보존): " + Path.GetFileName(kept) + (fromLegacy ? "\r\n가져온 곳(보존): " + legacyDir : "") + "\r\n";
            File.WriteAllText(Path.Combine(dataDir, DoneName), text);
            File.Delete(PendingPath(dataDir));
            if (fromLegacy)
            {

                try
                {
                    File.WriteAllText(Path.Combine(legacyDir, LegacyMarkerName),
                        "이 폴더의 메모는 메모도우미(MemoDoumi)로 복사되었습니다. 이 폴더는 자동으로 지워지지 않습니다.\r\n" +
                        "새 위치: " + dataDir + "\r\n시각: " + DateTime.Now.ToString("o") + "\r\n");
                }
                catch (Exception ex) { App.Log(ex); }
            }
            return Path.GetFileName(kept);
        }
    }
}
