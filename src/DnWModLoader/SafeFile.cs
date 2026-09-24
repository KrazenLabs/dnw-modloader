using System;
using System.IO;
using System.Text;

namespace DnWModLoader
{
    internal static class SafeFile
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        public static void WriteAllText(string path, string contents)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            string temp = path + ".tmp";
            byte[] bytes = Utf8.GetBytes(contents);
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temp, path, null, true);
            else File.Move(temp, path);
        }
    }
}
