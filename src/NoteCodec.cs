using System;
using System.Globalization;
using System.Text;

namespace MemoDoumi
{

    internal static class NoteCodec
    {
        const int MaxSide = Store.MaxSide, MaxCoord = Store.MaxCoord;
        static bool ValidTicks(long value) { return value >= new DateTime(2000, 1, 1).Ticks && value <= new DateTime(9000, 1, 1).Ticks; }

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
            bool hasScreen = false;
            int screenX = 0, screenY = 0;
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
                            case "screen-pos": { string[] p = val.Split(','); if (p.Length == 2) { screenX = Int(p[0]); screenY = Int(p[1]); hasScreen = true; } } break;
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
            if (hasScreen) { n.X = screenX; n.Y = screenY; n.Anchored = false; }
            n.Text = text;
            Sanitize(n);
            return n;
        }

        internal static void Sanitize(NoteData n)
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

        internal static string Compose(NoteData n)
        {
            var sb = new StringBuilder((n.Text ?? "").Length + 256);
            sb.Append("---\n");
            sb.Append("id: ").Append(n.Id).Append('\n');
            sb.Append("color: ").Append(n.Color).Append('\n');
            sb.Append("size: ").Append(n.W).Append('x').Append(n.H).Append('\n');
            if (n.Pinned && !n.Anchored) sb.Append("screen-pos: ").Append(n.X).Append(',').Append(n.Y).Append('\n');
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

    }
}
