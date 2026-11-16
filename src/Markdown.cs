using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace MemoDoumi
{

    internal static class Markdown
    {
        public const int Bold = 1, Italic = 2, Underline = 4, Strike = 8;
        const int AllStyles = Bold | Italic | Underline | Strike;

        public class Parsed
        {
            public string Plain;
            public List<byte> Styles = new List<byte>();
            public List<int> BulletLines = new List<int>();    // 목록인 줄의 시작 위치 (Plain 기준)
        }

        public static Parsed Parse(string md)
        {
            var p = new Parsed();
            var sb = new StringBuilder();
            string[] lines = (md ?? "").Replace("\r\n", "\n").Split('\n');
            for (int li = 0; li < lines.Length; li++)
            {
                string s = lines[li];
                if (s.StartsWith("- ", StringComparison.Ordinal))
                {
                    p.BulletLines.Add(sb.Length);
                    s = s.Substring(2);
                }
                else if (s.StartsWith("\\- ", StringComparison.Ordinal)) s = s.Substring(1);

                int style = 0;
                for (int i = 0; i < s.Length; i++)
                {
                    char c = s[i];
                    if (c == '\\' && i + 1 < s.Length && "*~\\<".IndexOf(s[i + 1]) >= 0)
                    {
                        i++;
                        c = s[i];
                    }
                    else if (At(s, i, "**")) { style ^= Bold; i += 1; continue; }
                    else if (c == '*') { style ^= Italic; continue; }
                    else if (At(s, i, "~~")) { style ^= Strike; i += 1; continue; }
                    else if (At(s, i, "<u>")) { style |= Underline; i += 2; continue; }
                    else if (At(s, i, "</u>")) { style &= ~Underline; i += 3; continue; }
                    sb.Append(c);
                    p.Styles.Add((byte)style);
                }
                if (li < lines.Length - 1)
                {
                    sb.Append('\n');
                    p.Styles.Add(0);
                }
            }
            p.Plain = sb.ToString();
            return p;
        }

        static bool At(string s, int i, string token)
        {
            return string.CompareOrdinal(s, i, token, 0, token.Length) == 0;
        }

        public static string Plain(NoteData n)
        {
            string text = n.Text ?? "";
            if (!n.Md) return text;
            if (!ReferenceEquals(n.PlainSource, text))
            {
                n.PlainValue = Parse(text).Plain;
                n.PlainSource = text;
            }
            return n.PlainValue;
        }

        static string Escape(string t, bool lineStart)
        {
            var sb = new StringBuilder(t.Length + 8);
            for (int i = 0; i < t.Length; i++)
            {
                char c = t[i];
                if (c == '*' || c == '~' || c == '\\') sb.Append('\\');
                else if (c == '<' && (At(t, i, "<u>") || At(t, i, "</u>"))) sb.Append('\\');
                sb.Append(c);
            }
            if (lineStart && t.StartsWith("- ", StringComparison.Ordinal)) sb.Insert(0, '\\');
            return sb.ToString();
        }

        static string Open(int st)
        {
            return ((st & Bold) != 0 ? "**" : "") + ((st & Italic) != 0 ? "*" : "") +
                   ((st & Strike) != 0 ? "~~" : "") + ((st & Underline) != 0 ? "<u>" : "");
        }

        static string Close(int st)
        {
            return ((st & Underline) != 0 ? "</u>" : "") + ((st & Strike) != 0 ? "~~" : "") +
                   ((st & Italic) != 0 ? "*" : "") + ((st & Bold) != 0 ? "**" : "");
        }

        public static string Compose(string plain, IList<byte> styles, ICollection<int> bulletLines)
        {
            var sb = new StringBuilder(plain.Length + 16);
            int lineStart = 0;
            while (true)
            {
                int end = plain.IndexOf('\n', lineStart);
                if (end < 0) end = plain.Length;
                bool bullet = bulletLines.Contains(lineStart);
                if (bullet) sb.Append("- ");

                int i = lineStart;
                while (i < end)
                {
                    int st = styles[i] & AllStyles, j = i + 1;
                    while (j < end && (styles[j] & AllStyles) == st) j++;
                    string run = plain.Substring(i, j - i);
                    bool first = i == lineStart && !bullet;
                    if (st == 0) sb.Append(Escape(run, first));
                    else
                    {
                        // Markdown 호환성을 위해 서식 기호 바깥으로 공백을 옮긴다.
                        string core = run.Trim();
                        if (core.Length == 0) sb.Append(run);
                        else
                        {
                            int lead = run.Length - run.TrimStart().Length;
                            sb.Append(run, 0, lead);
                            sb.Append(Open(st)).Append(Escape(core, false)).Append(Close(st));
                            sb.Append(run, lead + core.Length, run.Length - lead - core.Length);
                        }
                    }
                    i = j;
                }
                if (end >= plain.Length) break;
                sb.Append('\n');
                lineStart = end + 1;
            }
            return sb.ToString();
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 4)]
        struct CHARFORMAT
        {
            public int cbSize, dwMask, dwEffects, yHeight, yOffset, crTextColor;
            public byte bCharSet, bPitchAndFamily;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szFaceName;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct CHARRANGE { public int cpMin, cpMax; }

        const int EM_EXSETSEL = 0x437, EM_GETCHARFORMAT = 0x43A, EM_SETCHARFORMAT = 0x444, SCF_SELECTION = 1;

        [DllImport("user32.dll", EntryPoint = "SendMessageW")]
        static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, ref CHARFORMAT cf);
        [DllImport("user32.dll", EntryPoint = "SendMessageW")]
        static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, ref CHARRANGE cr);

        static CHARFORMAT NewFormat()
        {
            var cf = new CHARFORMAT();
            cf.cbSize = Marshal.SizeOf(typeof(CHARFORMAT));
            return cf;
        }

        static void SetSel(IntPtr h, int from, int to)
        {
            var cr = new CHARRANGE();
            cr.cpMin = from;
            cr.cpMax = to;
            SendMessage(h, EM_EXSETSEL, IntPtr.Zero, ref cr);
        }

        public static void Fill(RichTextBox box, NoteData n)
        {
            box.BulletIndent = App.S(14);
            if (!n.Md)
            {
                box.Text = n.Text ?? "";
                return;
            }
            Parsed p = Parse(n.Text);
            box.Text = p.Plain;
            IntPtr h = box.Handle;
            int i = 0;
            while (i < p.Styles.Count)
            {
                int st = p.Styles[i], j = i + 1;
                while (j < p.Styles.Count && p.Styles[j] == st) j++;
                if (st != 0)
                {
                    SetSel(h, i, j);
                    CHARFORMAT cf = NewFormat();
                    cf.dwMask = AllStyles;
                    cf.dwEffects = st;
                    SendMessage(h, EM_SETCHARFORMAT, (IntPtr)SCF_SELECTION, ref cf);
                }
                i = j;
            }
            foreach (int line in p.BulletLines)
            {
                box.Select(line, 0);
                box.SelectionBullet = true;
            }
            box.Select(0, 0);
            box.ClearUndo();
        }

        static RichTextBox scratch;

        public static string FromRich(RichTextBox box)
        {
            if (scratch == null) scratch = new RichTextBox();
            scratch.Rtf = box.Rtf;
            string plain = scratch.Text;
            IntPtr h = scratch.Handle;

            var styles = new byte[plain.Length];
            int lineStart = 0;
            while (lineStart < plain.Length)
            {
                int end = plain.IndexOf('\n', lineStart);
                if (end < 0) end = plain.Length;
                if (end > lineStart) ReadStyles(h, styles, lineStart, end);
                lineStart = end + 1;
            }

            var bullets = new HashSet<int>();
            int start = 0;
            while (true)
            {
                scratch.Select(start, 0);
                if (scratch.SelectionBullet) bullets.Add(start);
                int nl = plain.IndexOf('\n', start);
                if (nl < 0) break;
                start = nl + 1;
            }
            return Compose(plain, styles, bullets);
        }

        static void ReadStyles(IntPtr h, byte[] styles, int from, int to)
        {
            SetSel(h, from, to);
            CHARFORMAT cf = NewFormat();
            SendMessage(h, EM_GETCHARFORMAT, (IntPtr)SCF_SELECTION, ref cf);
            if (to - from == 1 || (cf.dwMask & AllStyles) == AllStyles)
            {
                byte st = (byte)(cf.dwEffects & AllStyles);
                if (st != 0) for (int i = from; i < to; i++) styles[i] = st;
                return;
            }
            int mid = from + (to - from) / 2;
            ReadStyles(h, styles, from, mid);
            ReadStyles(h, styles, mid, to);
        }

        public static bool SelectionHas(RichTextBox box, int flag)
        {
            CHARFORMAT cf = NewFormat();
            SendMessage(box.Handle, EM_GETCHARFORMAT, (IntPtr)SCF_SELECTION, ref cf);
            return (cf.dwMask & flag) != 0 && (cf.dwEffects & flag) != 0;
        }

        public static void Toggle(RichTextBox box, int flag)
        {
            bool on = !SelectionHas(box, flag);
            CHARFORMAT cf = NewFormat();
            cf.dwMask = flag;
            cf.dwEffects = on ? flag : 0;
            SendMessage(box.Handle, EM_SETCHARFORMAT, (IntPtr)SCF_SELECTION, ref cf);
        }
    }
}
