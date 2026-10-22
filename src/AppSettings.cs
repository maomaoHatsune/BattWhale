using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BattWhale
{
    internal sealed class AppSettings
    {
        public int IntervalSeconds = 60;
        public int LowThreshold = 20;
        public bool Autostart;
        public bool HintShown;
        public readonly HashSet<string> Hidden = new HashSet<string>();

        private static string Folder
        {
            get { return DataStore.Folder; }
        }

        public static string FilePath
        {
            get { return DataStore.WritePath("settings.ini"); }
        }

        public static AppSettings Load()
        {
            var settings = new AppSettings();
            settings.Autostart = AutostartEnabled();
            try
            {
                string path = DataStore.ReadPath("settings.ini");
                if (!File.Exists(path))
                    return settings;
                foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                    string value = line.Substring(eq + 1).Trim();
                    switch (key)
                    {
                        case "interval":
                            int interval;
                            if (int.TryParse(value, out interval) && interval >= 10)
                                settings.IntervalSeconds = interval;
                            break;
                        case "low":
                            int low;
                            if (int.TryParse(value, out low) && low > 0 && low <= 100)
                                settings.LowThreshold = low;
                            break;
                        case "hidden":
                            foreach (string part in value.Split(new char[] { '|' }, StringSplitOptions.RemoveEmptyEntries))
                                settings.Hidden.Add(part);
                            break;
                        case "hinted":
                            settings.HintShown = value == "1";
                            break;
                    }
                }
            }
            catch (Exception)
            {
                // unreadable settings must never keep the tray app from starting
            }
            return settings;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                var sb = new StringBuilder();
                sb.AppendLine("interval=" + IntervalSeconds);
                sb.AppendLine("low=" + LowThreshold);
                sb.AppendLine("hidden=" + string.Join("|", new List<string>(Hidden).ToArray()));
                sb.AppendLine("hinted=" + (HintShown ? "1" : "0"));
                File.WriteAllText(FilePath, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception)
            {
            }
        }

        // ---- autostart (HKCU Run key, no admin needed) ----
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValue = "BattWhale";

        public static bool AutostartEnabled()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey))
                {
                    if (key == null) return false;
                    return key.GetValue(RunValue) != null;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static void SetAutostart(bool enabled)
        {
            try
            {
                using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (key == null) return;
                    if (enabled)
                        key.SetValue(RunValue, BuildLaunchCommand());
                    else
                        key.DeleteValue(RunValue, false);
                }
            }
            catch (Exception)
            {
            }
        }

        /// <summary>Run the exe directly. The vbs launcher only helps against the manual
        /// double-click SmartScreen prompt, but at logon it is less reliable (script host
        /// can be restricted), so autostart must not depend on it.</summary>
        public static string BuildLaunchCommand()
        {
            return "\"" + System.Windows.Forms.Application.ExecutablePath + "\"";
        }
    }
}
