using System;
using System.Collections.Generic;
using System.Text;

namespace BattWhale.Providers
{
    /// <summary>Logitech Lightspeed / Unifying receivers and direct HID++ devices.</summary>
    internal sealed class LogitechProvider : IBatteryProvider
    {
        public string Name { get { return "Logitech"; } }

        // remembers devices across polls so a sleeping keyboard keeps its icon
        private readonly Dictionary<string, BatteryReading> _known = new Dictionary<string, BatteryReading>();
        private const string FallbackName = "Logitech 设备";

        public void Poll(List<BatteryReading> sink)
        {
            List<HidInterface> links = FindLinks();
            if (links.Count == 0)
                return;

            // A cabled device answers on index 0xFF - and, annoyingly, on 1..6 as well,
            // so probing every index would report the same keyboard four times. A receiver
            // never answers at 0xFF, which is what tells the two apart.
            var direct = new HashSet<string>();
            var gate = new object();
            RunParallel(links, delegate(HidInterface iface)
            {
                using (var link = new HidppLink(iface.Path, iface.OutputReportLen))
                {
                    if (!link.IsOpen)
                        return;
                    if (link.GetFeature(0xFF, 0x0000, 1200) >= 0)
                    {
                        lock (gate)
                        {
                            direct.Add(iface.Path);
                        }
                    }
                }
            });

            var jobs = new List<KeyValuePair<HidInterface, byte>>();
            foreach (HidInterface iface in links)
            {
                if (direct.Contains(iface.Path))
                {
                    jobs.Add(new KeyValuePair<HidInterface, byte>(iface, 0xFF));
                }
                else
                {
                    for (byte dev = 1; dev <= 4; dev++)
                        jobs.Add(new KeyValuePair<HidInterface, byte>(iface, dev));
                }
            }

            var results = new List<BatteryReading>();
            var guard = new object();
            var threads = new List<System.Threading.Thread>();
            foreach (KeyValuePair<HidInterface, byte> job in jobs)
            {
                HidInterface capturedIface = job.Key;
                byte capturedDev = job.Value;
                var thread = new System.Threading.Thread(delegate()
                {
                    try
                    {
                        using (var link = new HidppLink(capturedIface.Path, capturedIface.OutputReportLen))
                        {
                            if (!link.IsOpen)
                                return;
                            BatteryReading reading = ReadDevice(link, capturedIface, capturedDev);
                            if (reading != null)
                            {
                                lock (guard)
                                {
                                    results.Add(reading);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        if (Environment.GetEnvironmentVariable("BATTWHALE_DEBUG") == "1")
                            Console.WriteLine("[logitech] dev " + capturedDev + " error " + ex.Message);
                    }
                });
                thread.IsBackground = true;
                threads.Add(thread);
                thread.Start();
            }
            foreach (System.Threading.Thread thread in threads)
                thread.Join(3000);

            var seen = new HashSet<string>();
            var names = new HashSet<string>();
            foreach (BatteryReading r in results)
            {
                // Same physical keyboard can surface as cable + receiver; keep one row.
                string dedupe = (r.Name == FallbackName || r.Name.Length == 0)
                    ? null
                    : r.Name.ToLowerInvariant() + "|" + r.Kind;
                if (dedupe != null && !names.Add(dedupe))
                {
                    if (Environment.GetEnvironmentVariable("BATTWHALE_DEBUG") == "1")
                        Console.WriteLine("[logitech] merged duplicate " + r.Name + " (" + r.Key + ")");
                    continue;
                }
                seen.Add(r.Key);
                _known[r.Key] = r;
                sink.Add(r);
            }

            // Devices that were present before but stayed silent this round keep their
            // row as long as their receiver is still plugged in - that is how a sleeping
            // keyboard keeps a greyed-out percentage instead of vanishing from the tray.
            var paths = new HashSet<string>();
            foreach (HidInterface iface in links)
                paths.Add("logi:" + iface.Path + ":");
            foreach (KeyValuePair<string, BatteryReading> kv in _known)
            {
                if (seen.Contains(kv.Key))
                    continue;
                bool receiverPresent = false;
                foreach (string prefix in paths)
                {
                    if (kv.Key.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        receiverPresent = true;
                        break;
                    }
                }
                if (!receiverPresent)
                {
                    _known.Remove(kv.Key);
                    continue;
                }
                BatteryReading last = kv.Value;
                var offline = new BatteryReading();
                offline.Key = last.Key;
                offline.Name = last.Name;
                offline.Kind = last.Kind;
                offline.Percent = last.Percent;
                offline.State = BatteryState.Offline;
                offline.Note = "休眠中";
                offline.Updated = DateTime.Now;
                sink.Add(offline);
            }
        }

        private static void RunParallel(List<HidInterface> items, Action<HidInterface> action)
        {
            var threads = new List<System.Threading.Thread>();
            foreach (HidInterface item in items)
            {
                HidInterface captured = item;
                var thread = new System.Threading.Thread(delegate()
                {
                    try
                    {
                        action(captured);
                    }
                    catch (Exception)
                    {
                    }
                });
                thread.IsBackground = true;
                threads.Add(thread);
                thread.Start();
            }
            foreach (System.Threading.Thread thread in threads)
                thread.Join(2500);
        }

        private BatteryReading ReadDevice(HidppLink link, HidInterface iface, byte dev)
        {
            // IRoot.getFeature(0x0000) answers on every live HID++2 device. The first
            // request may have to wake the radio link, so give it room.
            int root = link.GetFeature(dev, 0x0000, 1200);
            if (root < 0)
                return null;

            string key = "logi:" + iface.Path + ":" + dev;
            string name = ReadName(link, dev);
            if (string.IsNullOrEmpty(name))
                name = dev == 0xFF ? iface.Product : FallbackName;

            int percent = -1;
            BatteryState state = BatteryState.Unknown;

            int unified = link.GetFeature(dev, 0x1004, 600);
            if (unified > 0)
            {
                byte[] info = link.Call(dev, (byte)unified, 0x01, new byte[] { 0x00 }, 900);
                if (info != null && info.Length >= 4)
                {
                    if (info[0] <= 100)
                        percent = info[0];
                    state = ChargingState(info[2]);
                }
            }

            if (percent < 0)
            {
                int status = link.GetFeature(dev, 0x1000, 600);
                if (status > 0)
                {
                    byte[] info = link.Call(dev, (byte)status, 0x00, null, 900);
                    if (info != null && info.Length >= 4)
                    {
                        if (info[0] <= 100)
                            percent = info[0];
                        state = StatusState(info[2]);
                    }
                }
            }

            if (percent < 0 && state == BatteryState.Unknown)
                return null;

            var reading = new BatteryReading();
            reading.Key = key;
            reading.Name = CleanName(name);
            reading.Kind = GuessKind(reading.Name);
            reading.Percent = percent;
            reading.State = state;
            reading.Updated = DateTime.Now;
            return reading;
        }

        private static BatteryState ChargingState(byte status)
        {
            switch (status)
            {
                case 0x00: return BatteryState.Discharging;
                case 0x01: return BatteryState.Charging;
                case 0x02: return BatteryState.Charging;
                case 0x03: return BatteryState.Full;
                default: return BatteryState.Discharging;
            }
        }

        private static BatteryState StatusState(byte status)
        {
            switch (status)
            {
                case 0x00: return BatteryState.Discharging;
                case 0x01: return BatteryState.Charging;
                case 0x02: return BatteryState.Charging;
                case 0x03: return BatteryState.Full;
                case 0x04: return BatteryState.Charging;
                default: return BatteryState.Discharging;
            }
        }

        private static string ReadName(HidppLink link, byte dev)
        {
            int feature = link.GetFeature(dev, 0x0005, 500);
            if (feature <= 0)
                return null;
            byte[] len = link.Call(dev, (byte)feature, 0x00, null, 700);
            if (len == null || len.Length < 1 || len[0] == 0 || len[0] == 0xFF)
                return null;
            var sb = new StringBuilder();
            for (int chunk = 0; chunk * 16 < len[0]; chunk++)
            {
                byte[] part = link.Call(dev, (byte)feature, 0x01, new byte[] { (byte)(chunk * 16) }, 700);
                if (part == null)
                    break;
                foreach (byte b in part)
                {
                    if (b == 0 || b == 0xFF)
                        break;
                    sb.Append((char)b);
                }
            }
            return sb.ToString();
        }

        private static string CleanName(string raw)
        {
            if (raw == null) return "";
            string s = raw.Trim();
            return s.Length > 48 ? s.Substring(0, 48) : s;
        }

        internal static DeviceKind GuessKind(string name)
        {
            string n = (name ?? "").ToLowerInvariant();
            if (n.Contains("tkl") || n.Contains("keyboard") || n.Contains("g5") || n.Contains("g7") ||
                n.Contains("g9") || n.Contains("pro x"))
            {
                if (n.Contains("mouse")) return DeviceKind.Mouse;
                if (n.Contains("keyboard") || n.Contains("tkl") || n.Contains("kbd")) return DeviceKind.Keyboard;
            }
            if (n.Contains("mouse") || n.Contains("superlight") || n.Contains("viper") || n.Contains("orochi") ||
                n.Contains("deathadder") || n.Contains("basilisk"))
                return DeviceKind.Mouse;
            if (n.Contains("headset") || n.Contains("pro x 2") || n.Contains("g pro x wireless"))
                return DeviceKind.Headset;
            if (n.Contains("controller") || n.Contains("gamepad") || n.Contains("pad") || n.Contains("supernova"))
                return DeviceKind.Controller;
            return DeviceKind.Unknown;
        }

        private static List<HidInterface> FindLinks()
        {
            var result = new List<HidInterface>();
            var seen = new HashSet<string>();
            foreach (HidInterface hi in HidInterface.Enumerate())
            {
                if (hi.Vid != 0x046D || hi.UsagePage != 0xFF00 || hi.OutputReportLen < 20)
                    continue;
                string group = GroupKey(hi.Path);
                if (!seen.Add(group))
                    continue;
                result.Add(hi);
            }
            return result;
        }

        // "\\?\hid#vid_046d&pid_c547&mi_02&col02#7&669e67f&0&0001#{guid}" -> "...&mi_02#7&669e67f&0&0001"
        private static string GroupKey(string path)
        {
            int col = path.IndexOf("&col", StringComparison.OrdinalIgnoreCase);
            if (col > 0)
            {
                int hash = path.IndexOf('#', col);
                if (hash > 0)
                    return path.Substring(0, col) + path.Substring(hash);
            }
            return path;
        }
    }
}
