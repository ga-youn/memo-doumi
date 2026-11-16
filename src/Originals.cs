using System;
using System.IO;
using System.Security.Cryptography;

namespace MemoDoumi
{

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
}
