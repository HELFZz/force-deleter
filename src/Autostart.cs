using System;
using System.Diagnostics;
using System.Windows.Forms;

namespace ForceDeleter
{
    internal static class Autostart
    {
        private const string TaskName = "ForceDeleter";

        internal static bool IsEnabled()
        {
            return Run("schtasks.exe", "/Query /TN \"" + TaskName + "\"") == 0;
        }

        internal static bool Enable(out string error)
        {
            error = null;
            string target = "\"" + Application.ExecutablePath + "\" --tray --autostart";
            string escaped = target.Replace("\"", "\\\"");
            string args = "/Create /F /SC ONLOGON /RL HIGHEST /TN \"" + TaskName + "\" /TR \"" + escaped + "\"";
            int code = Run("schtasks.exe", args);
            if (code == 0)
            {
                AppLog.Write("Autostart enabled");
                return true;
            }
            error = "schtasks exit code " + code;
            AppLog.Write("Autostart enable failed: " + error + " | " + args);
            return false;
        }

        internal static bool Disable(out string error)
        {
            error = null;
            int code = Run("schtasks.exe", "/Delete /F /TN \"" + TaskName + "\"");
            if (code == 0)
            {
                AppLog.Write("Autostart disabled");
                return true;
            }
            error = "schtasks exit code " + code;
            AppLog.Write("Autostart disable failed: " + error);
            return false;
        }

        private static int Run(string file, string args)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(file, args);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                using (Process p = Process.Start(psi))
                {
                    if (!p.WaitForExit(30000))
                    {
                        try { p.Kill(); } catch { }
                        return -1;
                    }
                    return p.ExitCode;
                }
            }
            catch (Exception ex)
            {
                AppLog.Write("RUN " + file + ": " + ex.Message);
                return -1;
            }
        }
    }
}
