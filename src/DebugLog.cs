using System;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace BattWhale
{
    /// <summary>Opt-in file log (set BATTWHALE_DEBUG=1) for field diagnosis.</summary>
    internal static class DebugLog
    {
        private static readonly object Sync = new object();

        public static bool Enabled
        {
            get { return Environment.GetEnvironmentVariable("BATTWHALE_DEBUG") == "1"; }
        }

        public static void Write(string message)
        {
            if (!Enabled)
                return;
            Append(message);
        }

        /// <summary>Always written: used for the few lines worth having when a user reports a problem.</summary>
        public static void Always(string message)
        {
            Append(message);
        }

        private static void Append(string message)
        {
            try
            {
                lock (Sync)
                {
                    string folder = Application.StartupPath;
                    string path = Path.Combine(folder, Enabled ? "debug.log" : "battwhale.log");
                    File.AppendAllText(path,
                        DateTime.Now.ToString("HH:mm:ss.fff") + "  " + message + Environment.NewLine,
                        Encoding.UTF8);
                }
            }
            catch (Exception)
            {
            }
        }
    }
}
