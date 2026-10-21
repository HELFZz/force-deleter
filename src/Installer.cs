using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Microsoft.Win32;
using System.Windows.Forms;

namespace ForceDeleter
{
    internal static class Installer
    {
        private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\ForceDeleter";
        private const string Version = "1.0.0";
        private const string ProjectUrl = "https://github.com/HELFZz/force-deleter";

        internal static string InstallDir
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    Path.Combine("Programs", "ForceDeleter"));
            }
        }

        internal static string InstalledExe
        {
            get { return Path.Combine(InstallDir, "ForceDeleter.exe"); }
        }

        internal static bool IsInstalledLocation()
        {
            try
            {
                return SamePath(Application.ExecutablePath, InstalledExe);
            }
            catch
            {
                return false;
            }
        }

        internal static int Install()
        {
            string current = Application.ExecutablePath;
            string target = InstalledExe;

            if (!SamePath(current, target))
            {
                try
                {
                    Directory.CreateDirectory(InstallDir);
                    if (!TryCopy(current, target, 3))
                    {
                        Program.SendRequest("EXIT");
                        WaitForNoInstance(5000);
                        KillInstancesExceptSelf();
                        if (!TryCopy(current, target, 20))
                        {
                            throw new IOException("Файл программы занят другим процессом.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Write("INSTALL COPY: " + ex);
                    MessageBox.Show(
                        "Не удалось установить ForceDeleter:\r\n\r\n" + ex.Message,
                        "ForceDeleter",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return 1;
                }

                ProcessStartInfo psi = new ProcessStartInfo(target, "--install");
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                try
                {
                    Process.Start(psi);
                }
                catch (Exception ex)
                {
                    AppLog.Write("INSTALL ELEVATE: " + ex.Message);
                    bool cancelled = ex is System.ComponentModel.Win32Exception &&
                                     ((System.ComponentModel.Win32Exception)ex).NativeErrorCode == 1223;
                    if (!cancelled)
                    {
                        MessageBox.Show(
                            "Не удалось запустить установку с правами администратора:\r\n\r\n" + ex.Message,
                            "ForceDeleter",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                    }
                    return cancelled ? 0 : 1;
                }

                return 0;
            }

            if (!Program.IsAdmin())
            {
                Program.RelaunchElevated(new string[] { "--install" });
                return 0;
            }

            AppLog.Write("Installing to " + InstallDir);

            Program.SendRequest("EXIT");
            if (!WaitForNoInstance(6000))
            {
                KillInstancesExceptSelf();
                WaitForNoInstance(2000);
            }

            ExplorerMenu.Install();

            string autostartError;
            if (!Autostart.Enable(out autostartError))
            {
                AppLog.Write("INSTALL AUTOSTART: " + autostartError);
            }

            WriteUninstallEntry();

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(target, "--tray");
                psi.UseShellExecute = false;
                psi.WorkingDirectory = InstallDir;
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                AppLog.Write("INSTALL START: " + ex);
                MessageBox.Show(
                    "Программа установлена, но фоновый агент не запустился:\r\n\r\n" + ex.Message +
                    "\r\n\r\nЗапустите вручную: " + target,
                    "ForceDeleter",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return 1;
            }

            AppLog.Write("Installed. Menu and autostart point to " + target);
            return 0;
        }

        internal static int Uninstall()
        {
            if (!Program.IsAdmin())
            {
                Program.RelaunchElevated(new string[] { "--uninstall" });
                return 0;
            }

            AppLog.Write("Uninstalling. Current exe: " + Application.ExecutablePath);

            ExplorerMenu.Uninstall();

            string autostartError;
            Autostart.Disable(out autostartError);

            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);
            }
            catch
            {
            }

            Program.SendRequest("EXIT");
            if (!WaitForNoInstance(6000))
            {
                KillInstancesExceptSelf();
                WaitForNoInstance(2000);
            }

            if (Directory.Exists(InstallDir))
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo(
                        "cmd.exe",
                        "/c ping 127.0.0.1 -n 3 >nul & rmdir /s /q \"" + InstallDir + "\"");
                    psi.UseShellExecute = false;
                    psi.CreateNoWindow = true;
                    psi.WindowStyle = ProcessWindowStyle.Hidden;
                    Process.Start(psi);
                    AppLog.Write("Uninstall cleanup scheduled for " + InstallDir);
                }
                catch (Exception ex)
                {
                    AppLog.Write("UNINSTALL DELETE: " + ex.Message);
                    MessageBox.Show(
                        "Не удалось удалить папку программы:\r\n\r\n" + ex.Message +
                        "\r\n\r\nУдалите вручную: " + InstallDir,
                        "ForceDeleter",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            }

            return 0;
        }

        private static bool TryCopy(string source, string target, int attempts)
        {
            for (int i = 0; i < attempts; i++)
            {
                try
                {
                    File.Copy(source, target, true);
                    return true;
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }

                if (i + 1 < attempts) Thread.Sleep(300);
            }

            return false;
        }

        private static bool WaitForNoInstance(int timeoutMs)
        {
            Stopwatch watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < timeoutMs)
            {
                try
                {
                    bool created;
                    using (Mutex mutex = new Mutex(true, Program.MutexName, out created))
                    {
                        if (created)
                        {
                            mutex.ReleaseMutex();
                            return true;
                        }
                    }
                }
                catch
                {
                }

                Thread.Sleep(200);
            }

            return false;
        }

        private static void KillInstancesExceptSelf()
        {
            int self = Process.GetCurrentProcess().Id;
            foreach (Process process in Process.GetProcessesByName("ForceDeleter"))
            {
                if (process.Id == self) continue;

                try
                {
                    AppLog.Write("Killing stale instance " + process.Id);
                    process.Kill();
                    process.WaitForExit(3000);
                }
                catch
                {
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        private static void WriteUninstallEntry()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(UninstallKey))
                {
                    key.SetValue("DisplayName", "ForceDeleter", RegistryValueKind.String);
                    key.SetValue("DisplayVersion", Version, RegistryValueKind.String);
                    key.SetValue("Publisher", "HELFZz", RegistryValueKind.String);
                    key.SetValue("DisplayIcon", InstalledExe, RegistryValueKind.String);
                    key.SetValue("InstallLocation", InstallDir, RegistryValueKind.String);
                    key.SetValue("UninstallString", "\"" + InstalledExe + "\" --uninstall", RegistryValueKind.String);
                    key.SetValue("URLInfoAbout", ProjectUrl, RegistryValueKind.String);
                    key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    key.SetValue("NoRepair", 1, RegistryValueKind.DWord);

                    try
                    {
                        FileInfo info = new FileInfo(InstalledExe);
                        if (info.Exists)
                        {
                            key.SetValue("EstimatedSize", (int)(info.Length / 1024) + 1, RegistryValueKind.DWord);
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Write("UNINSTALL ENTRY: " + ex.Message);
            }
        }

        private static bool SamePath(string left, string right)
        {
            return string.Equals(
                Path.GetFullPath(left),
                Path.GetFullPath(right),
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
