using System;
using System.IO;
using System.Text;

namespace ForceDeleter
{
    internal static class AppLog
    {
        private static readonly object Sync = new object();
        private static readonly string Folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ForceDeleter");

        internal static readonly string LogPath = Path.Combine(Folder, "ForceDeleter.log");

        internal static void Write(string message)
        {
            lock (Sync)
            {
                try
                {
                    Directory.CreateDirectory(Folder);
                    FileInfo info = new FileInfo(LogPath);
                    if (info.Exists && info.Length > 5 * 1024 * 1024)
                    {
                        string old = LogPath + ".old";
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(LogPath, old);
                    }
                    File.AppendAllText(
                        LogPath,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine,
                        Encoding.UTF8);
                }
                catch
                {
                }
            }
        }
    }
}
