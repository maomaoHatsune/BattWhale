using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace BattWhale
{
    /// <summary>Last known levels, so a sleeping device still has a number after a restart.</summary>
    internal static class ReadingCache
    {
        private static string FilePath
        {
            get { return DataStore.WritePath("last.ini"); }
        }

        public static void Save(List<BatteryReading> readings)
        {
            try
            {
                var sb = new StringBuilder();
                foreach (BatteryReading r in readings)
                {
                    if (r.Percent < 0 || !r.IsOnline)
                        continue;
                    sb.Append(r.Key.Replace("|", "/")).Append('|')
                      .Append(r.Name.Replace("|", "/")).Append('|')
                      .Append((int)r.Kind).Append('|')
                      .Append(r.Percent).Append('|')
                      .Append((int)r.State).Append('|')
                      .Append(ToUnix(r.Updated)).AppendLine();
                }
                string folder = Path.GetDirectoryName(FilePath);
                Directory.CreateDirectory(folder);
                File.WriteAllText(FilePath, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                // the cache is a nicety, never a reason to fail
                DebugLog.Always("cache save failed: " + ex.Message);
            }
        }

        public static List<BatteryReading> Load()
        {
            var result = new List<BatteryReading>();
            try
            {
                string path = DataStore.ReadPath("last.ini");
                if (!File.Exists(path))
                    return result;
                foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
                {
                    string[] parts = line.Split('|');
                    if (parts.Length < 6)
                        continue;
                    int kind, percent, state;
                    long stamp;
                    if (!int.TryParse(parts[2], out kind) || !int.TryParse(parts[3], out percent) ||
                        !int.TryParse(parts[4], out state) || !long.TryParse(parts[5], out stamp))
                        continue;
                    DateTime updated = FromUnix(stamp);
                    if ((DateTime.Now - updated).TotalHours > 6)
                        continue;
                    var reading = new BatteryReading();
                    reading.Key = parts[0];
                    reading.Name = parts[1];
                    reading.Kind = (DeviceKind)kind;
                    reading.Percent = percent;
                    reading.State = BatteryState.Offline;
                    reading.Note = "休眠中";
                    reading.Updated = updated;
                    result.Add(reading);
                }
            }
            catch (Exception)
            {
            }
            return result;
        }

        private static long ToUnix(DateTime time)
        {
            return (long)(time.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        }

        private static DateTime FromUnix(long stamp)
        {
            return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(stamp).ToLocalTime();
        }
    }
}
