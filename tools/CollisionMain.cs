using System;
using System.IO;
using System.Text;

namespace MemoDoumi
{

    internal static class CollisionMain
    {
        static int Main()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string notes = Path.Combine(baseDir, "data", "notes");
            Directory.CreateDirectory(notes);
            var utf8 = new UTF8Encoding(false);
            string[] ids = { "abcdef1200000000000000000000aaaa", "abcdef1200000000000000000000bbbb" };
            for (int i = 0; i < 2; i++)
                File.WriteAllText(Path.Combine(notes, "c" + (i + 1) + ".md"),
                    ("---\nid: " + ids[i] + "\ncolor: 0\nsize: 270x250\npinned: false\nclosed: false\nformat: md\norder: " + (i + 1) + "\n---\n같은 제목\nTWIN-" + (i + 1) + "\n").Replace("\n", "\r\n"), utf8);

            AppData d = Store.Load();
            string save = "ok";
            try { Store.Save(d); } catch (Exception ex) { save = ex.GetType().Name + ": " + ex.Message; }

            int found = 0;
            var sb = new StringBuilder();
            sb.AppendLine("loaded=" + d.Notes.Count + " save=" + save);
            foreach (string f in Directory.GetFiles(notes, "*.md"))
            {
                string t = File.ReadAllText(f, utf8);
                sb.AppendLine(Path.GetFileName(f) + "  " + (t.Contains("TWIN-1") ? "TWIN-1 " : "") + (t.Contains("TWIN-2") ? "TWIN-2" : ""));
            }
            foreach (string mark in new[] { "TWIN-1", "TWIN-2" })
            {
                bool any = false;
                foreach (string f in Directory.GetFiles(notes, "*.md")) any |= File.ReadAllText(f, utf8).Contains(mark);
                if (any) found++;
            }
            sb.AppendLine(found == 2 ? "RESULT both notes survive on disk" : "RESULT a note was lost on disk (" + found + " of 2 remain)");
            File.WriteAllText(Path.Combine(baseDir, "collision.txt"), sb.ToString(), utf8);
            return found == 2 ? 0 : 1;
        }
    }
}
