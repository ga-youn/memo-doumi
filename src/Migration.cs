using System;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using System.Globalization;

namespace MemoDoumi
{

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
