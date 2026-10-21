using System;
using System.Collections.Generic;

namespace BattWhale.Providers
{
    /// <summary>Razer wireless devices: 90-byte HID feature report (Synapse / OpenRazer protocol).</summary>
    internal sealed class RazerProvider : IBatteryProvider
    {
        public const int ReportSize = 91;   // report id 0x00 + 90 byte payload
        private const byte BatteryLevelCommand = 0x80;  // class 0x07
        private const byte ChargingCommand = 0x84;      // class 0x07

        private static readonly Dictionary<int, string> Known = new Dictionary<int, string>
        {
            { 0x0094, "Razer Orochi V2" },
            { 0x0095, "Razer Orochi V2" },
            { 0x007A, "Razer Viper Ultimate" },
            { 0x007B, "Razer Viper Ultimate" },
            { 0x0086, "Razer Basilisk Ultimate" },
            { 0x0088, "Razer Basilisk Ultimate" },
            { 0x008F, "Razer Naga Pro" },
            { 0x0090, "Razer Naga Pro" },
            { 0x009E, "Razer Viper Mini SE" },
            { 0x009F, "Razer Viper Mini SE" },
            { 0x00A5, "Razer Viper V2 Pro" },
            { 0x00A6, "Razer Viper V2 Pro" },
            { 0x00A7, "Razer Naga V2 Pro" },
            { 0x00A8, "Razer Naga V2 Pro" },
            { 0x00AA, "Razer Basilisk V3 Pro" },
            { 0x00AB, "Razer Basilisk V3 Pro" },
            { 0x00AF, "Razer Cobra Pro" },
            { 0x00B0, "Razer Cobra Pro" },
            { 0x00B6, "Razer DeathAdder V3 Pro" },
            { 0x00B7, "Razer DeathAdder V3 Pro" },
            { 0x00B8, "Razer Viper V3 HyperSpeed" },
            { 0x00B9, "Razer Basilisk V3 X HyperSpeed" },
            { 0x00BE, "Razer DeathAdder V4 Pro" },
            { 0x00BF, "Razer DeathAdder V4 Pro" },
            { 0x00C0, "Razer Viper V3 Pro" },
            { 0x00C1, "Razer Viper V3 Pro" },
            { 0x00D0, "Razer Pro Click V2" },
            { 0x00D1, "Razer Pro Click V2" },
            { 0x009A, "Razer Pro Click Mini" },
            { 0x00B4, "Razer Naga V2 HyperSpeed" },
        };

        public string Name { get { return "Razer"; } }

        public void Poll(List<BatteryReading> sink)
        {
            var handled = new HashSet<string>();

            // Preferred route: Razer's own control device, which forwards the feature
            // report to the real hardware even though the HID collection is virtualised.
            foreach (string path in RazerControl.FindDevices())
            {
                int pid = ParsePid(path);
                var reading = new BatteryReading();
                reading.Key = "razer:ctl:" + path;
                reading.Name = NameFor(pid, null);
                reading.Kind = DeviceKind.Mouse;
                reading.Updated = DateTime.Now;

                RazerControl.Result control = RazerControl.Read(path);
                LogOnce("ctl:" + path, string.Format(
                    "雷蛇控制设备 {0} 电量={1}% 充电={2} {3}",
                    path, control.Percent, control.Charging, control.Error));

                reading.Percent = control.Percent;
                if (control.Percent < 0)
                {
                    reading.State = BatteryState.Blocked;
                    reading.Note = control.Error.Length > 0 ? control.Error : "读不到电量";
                }
                else
                {
                    reading.State = control.Charging ? BatteryState.Charging : BatteryState.Discharging;
                    if (control.Percent >= 100 && control.Charging)
                        reading.State = BatteryState.Full;
                }
                sink.Add(reading);
                if (pid > 0)
                    handled.Add(pid.ToString("X4"));
            }

            // Fallback for machines without the Razer driver: plain HID feature reports.
            foreach (HidInterface hi in HidInterface.Enumerate())
            {
                if (hi.Vid != 0x1532 || hi.FeatureReportLen < 90)
                    continue;
                if (handled.Contains(hi.Pid.ToString("X4")))
                    continue;

                var reading = new BatteryReading();
                reading.Key = "razer:" + hi.Path;
                reading.Name = NameFor(hi.Pid, hi.Product);
                reading.Kind = DeviceKind.Mouse;
                reading.Updated = DateTime.Now;

                ReadResult result = Read(hi.Path);
                reading.Percent = result.Percent;
                reading.State = result.State;
                reading.Note = result.Note;
                sink.Add(reading);
            }
        }

        private static string NameFor(int pid, string product)
        {
            string name;
            if (pid > 0 && Known.TryGetValue(pid, out name))
                return name;
            return string.IsNullOrEmpty(product) ? "Razer 设备" : product;
        }

        private static int ParsePid(string path)
        {
            int at = path.IndexOf("PID_", StringComparison.OrdinalIgnoreCase);
            if (at < 0)
                return 0;
            at += 4;
            int value = 0;
            int digits = 0;
            while (at < path.Length && digits < 4 && Uri.IsHexDigit(path[at]))
            {
                value = value * 16 + Convert.ToInt32(path[at].ToString(), 16);
                at++;
                digits++;
            }
            return digits == 4 ? value : 0;
        }

        private sealed class ReadResult
        {
            public int Percent = -1;
            public BatteryState State = BatteryState.Unknown;
            public string Note = "";
        }

        private ReadResult Read(string path)
        {
            var result = new ReadResult();
            string mode;
            IntPtr handle = Open(path, out mode);
            if (!IsValid(handle))
            {
                result.State = BatteryState.Blocked;
                result.Note = "无法打开控制口";
                return result;
            }
            try
            {
                byte status;
                int raw = Query(handle, 0x07, BatteryLevelCommand, out status);
                LogOnce(path + "|" + mode, string.Format(
                    "雷蛇 {0} 打开模式={1} 电量回复={2} status=0x{3:X2}", path, mode, raw, status));
                if (raw < 0)
                {
                    result.State = status == 0x04 ? BatteryState.Offline : BatteryState.Blocked;
                    result.Note = status == 0x04 ? "休眠中"
                        : (mode == "只查询" ? "雷云驱动占用" : "控制口无响应");
                    return result;
                }
                result.Percent = (int)Math.Round(raw / 255.0 * 100.0);
                byte ignored;
                int charging = Query(handle, 0x07, ChargingCommand, out ignored);
                bool isCharging = charging > 0;
                result.State = isCharging ? BatteryState.Charging : BatteryState.Discharging;
                if (result.Percent >= 100 && isCharging)
                    result.State = BatteryState.Full;
                return result;
            }
            finally
            {
                Interop.CloseHandle(handle);
            }
        }

        // FILE_READ_DATA / FILE_WRITE_DATA. The generic rights map to nothing on this
        // device object - asking for GENERIC_READ|GENERIC_WRITE is answered with
        // ERROR_ACCESS_DENIED even though the DACL grants Everyone read/write.
        private const uint ReadData = 0x0001;
        private const uint WriteData = 0x0002;

        private static IntPtr Open(string path, out string mode)
        {
            uint[] attempts = new uint[] { ReadData | WriteData, WriteData, 0 };
            string[] labels = new string[] { "读写", "只写", "只查询" };
            for (int i = 0; i < attempts.Length; i++)
            {
                IntPtr handle = Interop.CreateFileW(path, attempts[i],
                    Interop.FILE_SHARE_READ | Interop.FILE_SHARE_WRITE, IntPtr.Zero,
                    Interop.OPEN_EXISTING, 0, IntPtr.Zero);
                if (IsValid(handle))
                {
                    mode = labels[i];
                    return handle;
                }
            }
            mode = "";
            return IntPtr.Zero;
        }

        private static bool IsValid(IntPtr handle)
        {
            return handle != IntPtr.Zero && handle != new IntPtr(-1);
        }

        private static readonly HashSet<string> Logged = new HashSet<string>();

        private static void LogOnce(string key, string message)
        {
            lock (Logged)
            {
                if (!Logged.Add(key))
                    return;
            }
            DebugLog.Always(message);
        }

        /// <summary>Sends one Razer command and waits for a real reply. Returns arg1 or -1.</summary>
        private static int Query(IntPtr handle, byte commandClass, byte commandId, out byte status)
        {
            status = 0xFF;
            var request = new byte[ReportSize];
            request[0] = 0x00;      // status of a request
            request[1] = 0x1F;      // transaction id used by Razer mice
            request[5] = 0x02;      // data size
            request[6] = commandClass;
            request[7] = commandId;
            byte crc = 0;
            for (int i = 2; i <= 87; i++)
                crc ^= request[i];
            request[88] = crc;

            if (!Interop.HidD_SetFeature(handle, request, ReportSize))
                return -1;

            DateTime deadline = DateTime.UtcNow.AddMilliseconds(1200);
            while (DateTime.UtcNow < deadline)
            {
                System.Threading.Thread.Sleep(70);
                var reply = new byte[ReportSize];
                if (!Interop.HidD_GetFeature(handle, reply, ReportSize))
                    return -1;

                byte replyStatus = reply[0];
                status = replyStatus;
                if (reply[6] == commandClass && reply[7] == commandId)
                {
                    if (replyStatus == 0x02)
                        return reply[9];              // arg1 = level (0..255)
                    if (replyStatus == 0x04)
                        return -1;                    // device asleep / not answering
                    if (replyStatus == 0x01)
                        continue;                     // busy, keep polling
                    if (replyStatus == 0x05)
                        return -1;                    // not supported
                }
            }
            return -1;
        }
    }
}
