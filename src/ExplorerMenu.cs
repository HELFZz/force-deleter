using System;
using Microsoft.Win32;
using System.Windows.Forms;

namespace ForceDeleter
{
    internal static class ExplorerMenu
    {
        private const string Verb = "ForceDeleter";
        private const string MenuText = "Удалить в любом случае";
        private const string FileKey = @"Software\Classes\*\shell\" + Verb;
        private const string DirectoryKey = @"Software\Classes\Directory\shell\" + Verb;

        internal static string Command
        {
            get { return "\"" + Application.ExecutablePath + "\" --delete \"%1\""; }
        }

        internal static void Install()
        {
            WriteVerbKey(FileKey);
            WriteVerbKey(DirectoryKey);
            AppLog.Write("Context menu installed: " + Command);
        }

        private static void WriteVerbKey(string keyName)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(keyName))
            {
                key.SetValue("MUIVerb", MenuText, RegistryValueKind.String);
                key.SetValue("MultiSelectModel", "Player", RegistryValueKind.String);
                using (RegistryKey command = key.CreateSubKey("command"))
                {
                    command.SetValue("", Command, RegistryValueKind.String);
                }
            }
        }

        internal static void Uninstall()
        {
            Registry.CurrentUser.DeleteSubKeyTree(FileKey, false);
            Registry.CurrentUser.DeleteSubKeyTree(DirectoryKey, false);
            AppLog.Write("Context menu removed");
        }

        internal static bool IsInstalled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(DirectoryKey))
                {
                    return key != null;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}

