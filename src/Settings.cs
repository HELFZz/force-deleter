using System;
using Microsoft.Win32;

namespace ForceDeleter
{
    internal static class Settings
    {
        private const string KeyName = @"Software\ForceDeleter";

        internal static bool ScheduleOnReboot
        {
            get { return ReadInt("ScheduleOnReboot", 1) != 0; }
            set { WriteInt("ScheduleOnReboot", value ? 1 : 0); }
        }

        internal static bool ShowNotifications
        {
            get { return ReadInt("ShowNotifications", 1) != 0; }
            set { WriteInt("ShowNotifications", value ? 1 : 0); }
        }

        private static int ReadInt(string name, int fallback)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(KeyName))
                {
                    if (key == null) return fallback;
                    object value = key.GetValue(name);
                    if (value == null) return fallback;
                    return Convert.ToInt32(value);
                }
            }
            catch
            {
                return fallback;
            }
        }

        private static void WriteInt(string name, int value)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(KeyName))
                {
                    key.SetValue(name, value, RegistryValueKind.DWord);
                }
            }
            catch (Exception ex)
            {
                AppLog.Write("SETTINGS WRITE: " + ex.Message);
            }
        }
    }
}
