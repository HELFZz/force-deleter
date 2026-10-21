using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace ForceDeleter
{
    internal sealed class SettingsForm : Form
    {
        private readonly CheckBox chkMenu;
        private readonly CheckBox chkSchedule;
        private readonly CheckBox chkNotifications;
        private readonly CheckBox chkAutostart;
        private readonly Label lblStatus;

        internal SettingsForm()
        {
            Text = "ForceDeleter — настройки";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(560, 390);
            Font = new Font("Segoe UI", 9f);

            Label title = new Label();
            title.Text = "Удаление в любом случае";
            title.Font = new Font("Segoe UI", 14f, FontStyle.Bold);
            title.SetBounds(18, 14, 520, 30);
            Controls.Add(title);

            Label subtitle = new Label();
            subtitle.Text = "Обходит «Отказано в доступе», read-only, длинные пути, junction и занятые файлы.\r\n" +
                            "Удаление выполняется без корзины — восстановить нельзя.";
            subtitle.SetBounds(20, 50, 520, 40);
            Controls.Add(subtitle);

            chkMenu = new CheckBox();
            chkMenu.Text = "Пункт «Удалить в любом случае» в контекстном меню проводника";
            chkMenu.SetBounds(20, 105, 520, 24);
            chkMenu.Checked = ExplorerMenu.IsInstalled();
            Controls.Add(chkMenu);

            chkSchedule = new CheckBox();
            chkSchedule.Text = "Занятые файлы удалять при следующей перезагрузке";
            chkSchedule.SetBounds(20, 135, 520, 24);
            chkSchedule.Checked = Settings.ScheduleOnReboot;
            Controls.Add(chkSchedule);

            chkNotifications = new CheckBox();
            chkNotifications.Text = "Показывать уведомления о результате";
            chkNotifications.SetBounds(20, 165, 520, 24);
            chkNotifications.Checked = Settings.ShowNotifications;
            Controls.Add(chkNotifications);

            chkAutostart = new CheckBox();
            chkAutostart.Text = "Запускать при входе в систему в фоне (без UAC)";
            chkAutostart.SetBounds(20, 195, 520, 24);
            chkAutostart.Checked = Autostart.IsEnabled();
            Controls.Add(chkAutostart);

            Label status = new Label();
            status.Text = Program.IsAdmin()
                ? "Статус: работает с правами администратора."
                : "Статус: без прав администратора — защищённые объекты удалить не получится.";
            status.ForeColor = Program.IsAdmin() ? Color.SeaGreen : Color.Firebrick;
            status.SetBounds(20, 232, 520, 20);
            Controls.Add(status);

            lblStatus = new Label();
            lblStatus.SetBounds(20, 256, 520, 60);
            lblStatus.ForeColor = Color.DimGray;
            Controls.Add(lblStatus);

            Button save = new Button();
            save.Text = "Сохранить";
            save.SetBounds(20, 336, 115, 34);
            save.Click += delegate { ApplySettings(); };
            Controls.Add(save);

            Button logButton = new Button();
            logButton.Text = "Журнал";
            logButton.SetBounds(145, 336, 115, 34);
            logButton.Click += delegate { OpenLog(); };
            Controls.Add(logButton);

            Button close = new Button();
            close.Text = "Закрыть";
            close.SetBounds(425, 336, 115, 34);
            close.Click += delegate { Close(); };
            Controls.Add(close);

            AcceptButton = save;
            CancelButton = close;
        }

        private void ApplySettings()
        {
            try
            {
                Settings.ScheduleOnReboot = chkSchedule.Checked;
                Settings.ShowNotifications = chkNotifications.Checked;

                if (chkMenu.Checked) ExplorerMenu.Install();
                else ExplorerMenu.Uninstall();

                bool wantedAutostart = chkAutostart.Checked;
                if (wantedAutostart != Autostart.IsEnabled())
                {
                    string error;
                    bool ok = wantedAutostart ? Autostart.Enable(out error) : Autostart.Disable(out error);
                    if (!ok)
                    {
                        chkAutostart.Checked = Autostart.IsEnabled();
                        lblStatus.ForeColor = Color.Firebrick;
                        lblStatus.Text = "Часть настроек сохранена, но автозапуск изменить не удалось: " + error;
                        return;
                    }
                }

                lblStatus.ForeColor = Color.SeaGreen;
                lblStatus.Text = "Сохранено.";
            }
            catch (Exception ex)
            {
                AppLog.Write("SETTINGS SAVE: " + ex);
                lblStatus.ForeColor = Color.Firebrick;
                lblStatus.Text = "Ошибка: " + ex.Message;
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
    }
}

