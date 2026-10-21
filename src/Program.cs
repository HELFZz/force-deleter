using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace ForceDeleter
{
    internal static class Program
    {
        internal static readonly string UserSid = WindowsIdentity.GetCurrent().User.Value;
        internal static readonly string PipeName = "ForceDeleter_" + UserSid.Replace("-", "");
        internal static readonly string MutexName = @"Local\ForceDeleter_" + UserSid.Replace("-", "");

        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "";

                if (mode == "--exit")
                {
                    SendRequest("EXIT");
                    return 0;
                }

                if (mode == "--install")
                {
                    return Installer.Install();
                }

                if (mode == "--uninstall")
                {
                    return Installer.Uninstall();
                }

                if (mode == "" && !Installer.IsInstalledLocation())
                {
                    return Installer.Install();
                }

                if (mode == "--delete-local")
                {
                    if (args.Length < 2) return 2;
                    int exitCode = 0;
                    for (int i = 1; i < args.Length; i++)
                    {
                        DeleteResult result = DeletionEngine.Delete(args[i], Settings.ScheduleOnReboot, AppLog.Write);
                        AppLog.Write("LOCAL: " + args[i] +
                                     " files=" + result.DeletedFiles +
                                     " dirs=" + result.DeletedDirs +
                                     " scheduled=" + result.Scheduled.Count +
                                     " failed=" + result.Failed.Count);
                        if (result.Failed.Count > 0) exitCode = 1;
                    }
                    return exitCode;
                }

                if (mode == "--install-menu")
                {
                    ExplorerMenu.Install();
                    return 0;
                }

                if (mode == "--uninstall-menu")
                {
                    ExplorerMenu.Uninstall();
                    return 0;
                }

                if (mode == "--menu-status")
                {
                    return ExplorerMenu.IsInstalled() ? 0 : 1;
                }

                if (mode == "--autostart-on" || mode == "--autostart-off")
                {
                    if (!IsAdmin())
                    {
                        RelaunchElevated(args);
                        return 0;
                    }
                    string autostartError;
                    bool enabled = mode == "--autostart-on"
                        ? Autostart.Enable(out autostartError)
                        : Autostart.Disable(out autostartError);
                    AppLog.Write("CLI " + mode + ": " + enabled + " " + (autostartError == null ? "" : autostartError));
                    return enabled ? 0 : 1;
                }

                if (mode == "--delete")
                {
                    if (args.Length < 2) return 2;
                    string target = args[1];
                    if (SendRequest("DELETE\t" + Uri.EscapeDataString(target))) return 0;
                    RelaunchElevated(new string[] { "--tray", "--delete", target });
                    return 0;
                }

                bool createdNew;
                Mutex mutex = new Mutex(true, MutexName, out createdNew);

                if (!createdNew)
                {
                    int existingDelete = Array.IndexOf(args, "--delete");
                    if (existingDelete >= 0 && existingDelete + 1 < args.Length)
                    {
                        SendRequest("DELETE\t" + Uri.EscapeDataString(args[existingDelete + 1]));
                    }
                    else if (Array.IndexOf(args, "--autostart") < 0)
                    {
                        SendRequest("SHOW");
                    }
                    return 0;
                }

                if (!IsAdmin())
                {
                    mutex.ReleaseMutex();
                    mutex.Dispose();

                    string[] relaunchArgs = args.Length == 0 ? new string[] { "--tray" } : args;
                    RelaunchElevated(relaunchArgs);
                    return 0;
                }

                string pendingDelete = null;
                int deleteIndex = Array.IndexOf(args, "--delete");
                if (deleteIndex >= 0 && deleteIndex + 1 < args.Length)
                {
                    pendingDelete = args[deleteIndex + 1];
                }

                bool autostartDelay = Array.IndexOf(args, "--autostart") >= 0;

                Application.Run(new TrayApp(mutex, pendingDelete, autostartDelay));
                return 0;
            }
            catch (Exception ex)
            {
                AppLog.Write("FATAL: " + ex);
                MessageBox.Show(
                    "ForceDeleter: " + ex.Message,
                    "ForceDeleter",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return 1;
            }
        }

        internal static bool IsAdmin()
        {
            WindowsPrincipal principal = new WindowsPrincipal(WindowsIdentity.GetCurrent());
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        internal static bool SendRequest(string payload)
        {
            try
            {
                using (NamedPipeClientStream client = new NamedPipeClientStream(
                    ".", PipeName, PipeDirection.InOut, PipeOptions.None))
                {
                    client.Connect(800);
                    using (StreamWriter writer = new StreamWriter(client, new UTF8Encoding(false)))
                    using (StreamReader reader = new StreamReader(client, Encoding.UTF8))
                    {
                        writer.AutoFlush = true;
                        writer.WriteLine(payload);
                        reader.ReadLine();
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        internal static void RelaunchElevated(string[] args)
        {
            StringBuilder commandLine = new StringBuilder();
            foreach (string arg in args)
            {
                if (commandLine.Length > 0) commandLine.Append(' ');
                commandLine.Append('"');
                commandLine.Append(arg.Replace("\"", "\\\""));
                commandLine.Append('"');
            }

            ProcessStartInfo psi = new ProcessStartInfo(Application.ExecutablePath, commandLine.ToString());
            psi.UseShellExecute = true;
            psi.Verb = "runas";

            try
            {
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                AppLog.Write("ELEVATION FAILED: " + ex.Message);
                MessageBox.Show(
                    "Не удалось запустить ForceDeleter с правами администратора.\r\n\r\n" + ex.Message,
                    "ForceDeleter",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }
    }
}

