using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace ForceDeleter
{
    internal sealed class TrayApp : ApplicationContext
    {
        private readonly Mutex mutex;
        private readonly Control marshaler;
        private readonly IpcServer server;
        private NotifyIcon icon;
        private SettingsForm settingsForm;
        private int activeDeletes;

        internal TrayApp(Mutex mutex, string pendingDelete, bool autostartDelay)
        {
            this.mutex = mutex;

            marshaler = new Control();
            marshaler.CreateControl();

            server = new IpcServer(HandleRequest);
            server.Start();

            AppLog.Write("Tray started. admin=" + Program.IsAdmin());
            if (pendingDelete != null) AppLog.Write("Pending request: " + pendingDelete);

            if (autostartDelay)
            {
                Thread.Sleep(2500);
            }

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("Настройки", null, delegate { ShowSettings(); });
            menu.Items.Add("Удалить файл...", null, delegate { DeleteByDialog(false); });
            menu.Items.Add("Удалить папку...", null, delegate { DeleteByDialog(true); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Открыть журнал", null, delegate { OpenLog(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Выход", null, delegate { ExitThread(); });

            Icon appIcon = null;
            try { appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }
            if (appIcon == null) appIcon = SystemIcons.Application;

            icon = new NotifyIcon();
            icon.Icon = appIcon;
            icon.Text = "ForceDeleter";
            icon.ContextMenuStrip = menu;
            icon.DoubleClick += delegate { ShowSettings(); };
            icon.Visible = true;

            if (pendingDelete != null)
            {
                DeleteAsync(pendingDelete);
            }
        }

        private void HandleRequest(string line)
        {
            string[] parts = line.Split('\t');
            if (parts.Length == 0) return;

            if (parts[0] == "SHOW")
            {
                OnUi(ShowSettings);
                return;
            }

            if (parts[0] == "EXIT")
            {
                OnUi(delegate { ExitThread(); });
                return;
            }

            if (parts[0] == "DELETE" && parts.Length >= 2)
            {
                string path;
                try { path = Uri.UnescapeDataString(parts[1]); }
                catch { path = parts[1]; }
                OnUi(delegate { DeleteAsync(path); });
            }
        }

        private void OnUi(Action action)
        {
            try
            {
                if (marshaler.IsDisposed) return;
                if (marshaler.InvokeRequired) marshaler.BeginInvoke(action);
                else action();
            }
            catch
            {
            }
        }

        private void SetTrayText(string text)
        {
            if (icon != null) icon.Text = text;
        }

        private void DeleteByDialog(bool folder)
        {
            if (folder)
            {
                using (FolderBrowserDialog dialog = new FolderBrowserDialog())
                {
                    dialog.Description = "Выберите папку для принудительного удаления";
                    if (dialog.ShowDialog() == DialogResult.OK)
                    {
                        DeleteAsync(dialog.SelectedPath);
                    }
                }
            }
            else
            {
                using (OpenFileDialog dialog = new OpenFileDialog())
                {
                    dialog.Title = "Выберите файл для принудительного удаления";
                    dialog.Multiselect = true;
                    if (dialog.ShowDialog() == DialogResult.OK)
                    {
                        foreach (string file in dialog.FileNames)
                        {
                            DeleteAsync(file);
                        }
                    }
                }
            }
        }

        private void DeleteAsync(string path)
        {
            if (string.IsNullOrEmpty(path)) return;

            activeDeletes++;
            SetTrayText("ForceDeleter — удаление...");
            AppLog.Write("REQUEST: " + path);

            ThreadPool.QueueUserWorkItem(delegate(object state)
            {
                DeleteResult result;
                try
                {
                    result = DeletionEngine.Delete(path, Settings.ScheduleOnReboot, AppLog.Write);
                }
                catch (Exception ex)
                {
                    AppLog.Write("DELETE ERROR: " + ex);
                    result = new DeleteResult();
                    result.Failed.Add(path + " (" + ex.Message + ")");
                }

                OnUi(delegate { OnDeleted(path, result); });
            });
        }

        private void OnDeleted(string path, DeleteResult result)
        {
            activeDeletes--;
            if (activeDeletes <= 0) SetTrayText("ForceDeleter");

            AppLog.Write(
                "DONE " + path + ": files=" + result.DeletedFiles +
                " dirs=" + result.DeletedDirs +
                " scheduled=" + result.Scheduled.Count +
                " failed=" + result.Failed.Count);

            if (!Settings.ShowNotifications || icon == null) return;

            if (result.Failed.Count == 0)
            {
                string text;
                if (result.NotFound)
                {
                    text = "Объект не найден (возможно, уже удалён).";
                }
                else
                {
                    text = "Удалено: " + result.DeletedFiles + " файлов, " + result.DeletedDirs + " папок.";
                }
                if (result.Scheduled.Count > 0)
                {
                    text += " Отложено до перезагрузки: " + result.Scheduled.Count + ".";
                }
                icon.ShowBalloonTip(5000, "ForceDeleter", text, ToolTipIcon.Info);
            }
            else
            {
                string text = "Не удалось удалить: " + result.Failed.Count + " объектов.";
                if (result.Scheduled.Count > 0)
                {
                    text += " Отложено до перезагрузки: " + result.Scheduled.Count + ".";
                }
                text += " Подробности в журнале.";
                icon.ShowBalloonTip(8000, "ForceDeleter", text, ToolTipIcon.Error);
            }
        }

        private void ShowSettings()
        {
            try
            {
                if (settingsForm == null || settingsForm.IsDisposed)
                {
                    settingsForm = new SettingsForm();
                }

                if (settingsForm.Visible)
                {
                    settingsForm.Activate();
                    return;
                }

                settingsForm.Show();
                settingsForm.Activate();
                settingsForm.BringToFront();
            }
            catch (Exception ex)
            {
                AppLog.Write("SETTINGS ERROR: " + ex);
            }
        }

        private void OpenLog()
        {
            try
            {
                Process.Start("notepad.exe", "\"" + AppLog.LogPath + "\"");
            }
            catch (Exception ex)
            {
                AppLog.Write("OPEN LOG: " + ex.Message);
            }
        }

        protected override void ExitThreadCore()
        {
            try { server.Stop(); } catch { }

            if (icon != null)
            {
                icon.Visible = false;
                icon.Dispose();
                icon = null;
            }

            if (settingsForm != null && !settingsForm.IsDisposed)
            {
                settingsForm.Close();
            }

            try { mutex.ReleaseMutex(); } catch { }
            try { mutex.Dispose(); } catch { }

            AppLog.Write("Tray stopped");
            base.ExitThreadCore();
        }
    }
}
