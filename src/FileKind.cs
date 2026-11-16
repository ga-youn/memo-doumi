using System.Text;

namespace MemoDoumi
{

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
}
