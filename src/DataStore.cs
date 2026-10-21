using System;
using System.Collections.Generic;
using System.IO;

namespace BattWhale
{
    /// <summary>
    /// Settings/cache location with fallbacks. %APPDATA% is the polite place, but
    /// security software (controlled folder access, third-party AV) sometimes refuses
    /// writes from an unsigned exe, so try the alternatives before giving up.
    /// </summary>
    internal static class DataStore
    {
        private static readonly object Sync = new object();
        private static string _writable;
        private static bool _resolved;

        private static IEnumerable<string> Candidates()
        {
            yield return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            yield return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            yield return Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath);
            yield return Path.GetTempPath();
        }

        public static string Folder
        {
            get
            {
                lock (Sync)
                {
                    if (!_resolved)
                    {
                        _resolved = true;
                        foreach (string root in Candidates())
                        {
                            if (string.IsNullOrEmpty(root))
                                continue;
                            try
                            {
                                string dir = Path.Combine(root, "BattWhale");
                                Directory.CreateDirectory(dir);
                                string probe = Path.Combine(dir, ".probe");
                                File.WriteAllText(probe, "ok");
                                File.Delete(probe);
                                _writable = dir;
                                break;
                            }
                            catch (Exception)
                            {
                            }
                        }
                        if (_writable == null)
                            _writable = Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath);
                        DebugLog.Always("数据目录 " + _writable);
                    }
                    return _writable;
                }
            }
        }

        public static string WritePath(string fileName)
        {
            return Path.Combine(Folder, fileName);
        }

        /// <summary>Writable copy first (that is where saves go), then any older copy elsewhere.</summary>
        public static string ReadPath(string fileName)
        {
            string primary = WritePath(fileName);
            try
            {
                if (File.Exists(primary))
                    return primary;
            }
            catch (Exception)
            {
            }
            foreach (string root in Candidates())
            {
                if (string.IsNullOrEmpty(root))
                    continue;
                try
                {
                    string path = Path.Combine(Path.Combine(root, "BattWhale"), fileName);
                    if (File.Exists(path))
                        return path;
                }
                catch (Exception)
                {
                }
            }
            return primary;
        }
    }
}
