using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace ForceDeleter
{
    internal sealed class DeleteResult
    {
        internal bool NotFound;
        internal int DeletedFiles;
        internal int DeletedDirs;
        internal readonly List<string> Scheduled = new List<string>();
        internal readonly List<string> Failed = new List<string>();
    }

    internal static class DeletionEngine
    {
        private const int MoveFileDelayUntilReboot = 0x4;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool MoveFileEx(string existingFileName, string newFileName, int flags);

        internal static DeleteResult Delete(string path, bool scheduleOnReboot, Action<string> log)
        {
            DeleteResult result = new DeleteResult();
            string root = ToExtendedPath(path);

            if (IsVolumeRoot(root))
            {
                log("REFUSED volume root: " + path);
                result.Failed.Add(path);
                return result;
            }

            bool isFile = File.Exists(root);
            bool isDirectory = Directory.Exists(root);

            if (!isFile && !isDirectory)
            {
                log("NOT FOUND: " + path);
                result.NotFound = true;
                return result;
            }

            if (isFile)
            {
                RemoveFile(root, scheduleOnReboot, result, log);
                return result;
            }

            FileAttributes rootAttributes;
            try { rootAttributes = File.GetAttributes(root); }
            catch { rootAttributes = FileAttributes.Normal; }

            if ((rootAttributes & FileAttributes.ReparsePoint) != 0)
            {
                RemoveDirectory(root, scheduleOnReboot, result, log);
                return result;
            }

            List<string> files = new List<string>();
            List<string> directories = new List<string>();
            directories.Add(root);

            Stack<string> stack = new Stack<string>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                string current = stack.Pop();
                string[] entries;

                try
                {
                    entries = Directory.GetFileSystemEntries(current);
                }
                catch
                {
                    RepairAccess(current, true, log);
                    try
                    {
                        entries = Directory.GetFileSystemEntries(current);
                    }
                    catch (Exception ex)
                    {
                        log("CANNOT ENUMERATE " + current + ": " + ex.Message);
                        continue;
                    }
                }

                foreach (string entry in entries)
                {
                    FileAttributes attributes;
                    try { attributes = File.GetAttributes(entry); }
                    catch { attributes = FileAttributes.Normal; }

                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        directories.Add(entry);
                        if ((attributes & FileAttributes.ReparsePoint) == 0)
                        {
                            stack.Push(entry);
                        }
                    }
                    else
                    {
                        files.Add(entry);
                    }
                }
            }

            log("FOUND " + files.Count + " files, " + (directories.Count - 1) + " folders in " + path);

            foreach (string file in files)
            {
                RemoveFile(file, scheduleOnReboot, result, log);
            }

            directories.Sort(delegate(string a, string b) { return b.Length.CompareTo(a.Length); });
            foreach (string directory in directories)
            {
                RemoveDirectory(directory, scheduleOnReboot, result, log);
            }

            return result;
        }

        private static void RemoveFile(string path, bool schedule, DeleteResult result, Action<string> log)
        {
            for (int attempt = 0; attempt < 4; attempt++)
            {
                try
                {
                    ClearAttributes(path);
                    File.Delete(path);
                    result.DeletedFiles++;
                    return;
                }
                catch (UnauthorizedAccessException)
                {
                    RepairAccess(path, false, log);
                    if (attempt >= 1) RepairAccess(GetParent(path), false, log);
                }
                catch (SecurityException)
                {
                    RepairAccess(path, false, log);
                }
                catch (Exception ex)
                {
                    if (attempt == 0)
                    {
                        RepairAccess(path, false, log);
                    }
                    else
                    {
                        RepairAccess(GetParent(path), false, log);
                    }
                    log("RETRY " + path + ": " + ex.Message);
                    Thread.Sleep(80);
                }
            }

            if (schedule && ScheduleDeleteOnReboot(path, log))
            {
                result.Scheduled.Add(path);
            }
            else
            {
                result.Failed.Add(path);
                log("FAILED FILE " + path);
            }
        }

        private static void RemoveDirectory(string path, bool schedule, DeleteResult result, Action<string> log)
        {
            for (int attempt = 0; attempt < 4; attempt++)
            {
                try
                {
                    ClearAttributes(path);
                    Directory.Delete(path, false);
                    result.DeletedDirs++;
                    return;
                }
                catch (Exception ex)
                {
                    if (attempt == 0)
                    {
                        RepairAccess(path, false, log);
                    }
                    else
                    {
                        RepairAccess(GetParent(path), false, log);
                    }
                    log("RETRY DIR " + path + ": " + ex.Message);
                    Thread.Sleep(80);
                }
            }

            if (schedule && ScheduleDeleteOnReboot(path, log))
            {
                result.Scheduled.Add(path);
            }
            else
            {
                result.Failed.Add(path);
                log("FAILED DIR " + path);
            }
        }

        private static bool ScheduleDeleteOnReboot(string path, Action<string> log)
        {
            try
            {
                bool ok = MoveFileEx(path, null, MoveFileDelayUntilReboot);
                if (ok)
                {
                    log("SCHEDULED " + path);
                    return true;
                }
                int error = Marshal.GetLastWin32Error();
                log("SCHEDULE FAILED " + path + " (Win32 " + error + ")");
            }
            catch (Exception ex)
            {
                log("SCHEDULE ERROR " + path + ": " + ex.Message);
            }
            return false;
        }

        private static void ClearAttributes(string path)
        {
            try
            {
                FileAttributes attributes = File.GetAttributes(path);
                FileAttributes drop = FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System;
                FileAttributes updated = attributes & ~drop;
                if (updated != attributes) File.SetAttributes(path, updated);
            }
            catch
            {
            }
        }

        private static void RepairAccess(string path, bool recurse, Action<string> log)
        {
            if (string.IsNullOrEmpty(path)) return;
            bool isDirectory = Directory.Exists(path);
            log("REPAIR " + path + (recurse && isDirectory ? " (recursive)" : ""));

            try
            {
                string takeownArgs = recurse && isDirectory
                    ? "/F " + Quote(path) + " /R /D Y"
                    : "/F " + Quote(path);
                RunTool("takeown.exe", takeownArgs);
            }
            catch
            {
            }

            try
            {
                string resetArgs = Quote(path) + " /reset /C /Q" + (recurse && isDirectory ? " /T" : "");
                RunTool("icacls.exe", resetArgs);
            }
            catch
            {
            }

            try
            {
                string userSid = WindowsIdentity.GetCurrent().User.Value;
                StringBuilder args = new StringBuilder();
                args.Append(Quote(path));
                args.Append(" /grant *").Append(userSid).Append(isDirectory ? ":(OI)(CI)F" : ":F");
                args.Append(" /grant *S-1-5-32-544").Append(isDirectory ? ":(OI)(CI)F" : ":F");
                args.Append(" /C /Q");
                if (recurse && isDirectory) args.Append(" /T");
                RunTool("icacls.exe", args.ToString());
            }
            catch
            {
            }
        }

        private static void RunTool(string fileName, string arguments)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(fileName, arguments);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                using (Process process = Process.Start(psi))
                {
                    if (!process.WaitForExit(120000))
                    {
                        try { process.Kill(); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Write("TOOL " + fileName + ": " + ex.Message);
            }
        }

        private static string GetParent(string path)
        {
            try
            {
                return Path.GetDirectoryName(path);
            }
            catch
            {
                return null;
            }
        }

        private static string Quote(string path)
        {
            return "\"" + path + "\"";
        }

        private static string ToExtendedPath(string path)
        {
            if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
            {
                return path;
            }

            string full = Path.GetFullPath(path);
            if (full.StartsWith(@"\\", StringComparison.Ordinal))
            {
                return @"\\?\UNC\" + full.Substring(2);
            }
            return @"\\?\" + full;
        }

        private static bool IsVolumeRoot(string extendedPath)
        {
            if (extendedPath.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string path = extendedPath;
            if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
            {
                path = path.Substring(4);
            }

            try
            {
                path = Path.GetFullPath(path);
            }
            catch
            {
                return false;
            }

            return Regex.IsMatch(path, @"^[A-Za-z]:\\?$");
        }
    }
}
