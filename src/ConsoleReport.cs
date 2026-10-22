using System;
using System.Collections.Generic;
using System.Security.Principal;
using System.Text;
using System.Threading;
using BattWhale.Providers;

namespace BattWhale
{
    internal static class ConsoleReport
    {
        public static int Run(bool diagnostics)
        {
            if (diagnostics)
                WriteEnvironment();

            var service = new BatteryService();
            service.Start(3600);
            DateTime deadline = DateTime.UtcNow.AddSeconds(12);
            while (DateTime.UtcNow < deadline && service.Readings.Count == 0)
                Thread.Sleep(120);
            Thread.Sleep(400);

            Console.WriteLine();
            if (service.Readings.Count == 0)
            {
                Console.WriteLine("没有读到任何设备电量。");
            }
            else
            {
                Console.WriteLine("设备电量:");
                foreach (BatteryReading r in service.Readings)
                {
                    Console.WriteLine(string.Format("  {0,-28} {1,-12} {2}",
                        r.Name, r.StatusText, r.Kind));
                }
            }

            if (diagnostics)
            {
                Console.WriteLine();
                DumpInterfaces();
                Console.WriteLine();
                DumpLogitech();
                Console.WriteLine();
                DumpRazer();
                Console.WriteLine();
                DumpXInput();
            }

            service.Dispose();
            return 0;
        }

        private static void WriteEnvironment()
        {
            Console.WriteLine("BattWhale 诊断");
            Console.WriteLine("  时间        " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            Console.WriteLine("  Windows     " + Environment.OSVersion.VersionString);
            Console.WriteLine("  64 位进程   " + Environment.Is64BitProcess);
            Console.WriteLine("  管理员      " + IsElevated());
            Console.WriteLine("  .NET        " + Environment.Version);
        }

        public static bool IsElevated()
        {
            try
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    var principal = new WindowsPrincipal(identity);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void DumpInterfaces()
        {
            Console.WriteLine("HID 接口（罗技 / 雷蛇 / GameSir / 任天堂）:");
            foreach (HidInterface hi in HidInterface.Enumerate())
            {
                if (hi.Vid != 0x046D && hi.Vid != 0x1532 && hi.Vid != 0x3537 && hi.Vid != 0x057E)
                    continue;
                Console.WriteLine(string.Format("  {0}  up=0x{1:X4} u=0x{2:X4} in={3} out={4} feat={5}{6}",
                    hi.VidPid, hi.UsagePage, hi.Usage, hi.InputReportLen, hi.OutputReportLen,
                    hi.FeatureReportLen, hi.Restricted ? "  (受限)" : ""));
                Console.WriteLine("      " + (string.IsNullOrEmpty(hi.Product) ? "(无名称)" : hi.Product) + "  " + hi.Path);
            }
        }

        private static void DumpLogitech()
        {
            Console.WriteLine("罗技 HID++ 探测:");
            var links = new List<HidInterface>();
            foreach (HidInterface hi in HidInterface.Enumerate())
            {
                if (hi.Vid == 0x046D && hi.UsagePage == 0xFF00 && hi.OutputReportLen >= 20)
                    links.Add(hi);
            }
            if (links.Count == 0)
            {
                Console.WriteLine("  没有找到 HID++ 接口");
                return;
            }
            foreach (HidInterface hi in links)
            {
                Console.WriteLine("  接口 " + hi.Path);
                using (var link = new HidppLink(hi.Path, hi.OutputReportLen))
                {
                    if (!link.IsOpen)
                    {
                        Console.WriteLine("    打开失败: " + Interop.LastError());
                        continue;
                    }
                    for (byte dev = 0xFF; dev >= 1; dev--)
                    {
                        if (dev != 0xFF && dev > 6) continue;
                        int root = link.GetFeature(dev, 0x0000, 1500);
                        if (root < 0)
                        {
                            Console.WriteLine(string.Format("    设备 0x{0:X2}: 无应答", dev));
                            if (dev == 1) break;
                            continue;
                        }
                        Console.WriteLine(string.Format("    设备 0x{0:X2}: IRoot 应答", dev));
                        int name = link.GetFeature(dev, 0x0005, 400);
                        int unified = link.GetFeature(dev, 0x1004, 400);
                        int status = link.GetFeature(dev, 0x1000, 400);
                        Console.WriteLine(string.Format("      DEVICE_NAME={0} UNIFIED_BATTERY={1} BATTERY_STATUS={2}",
                            name, unified, status));
                        if (unified > 0)
                            Console.WriteLine("      1004.fn1 = " + Hex(link.Call(dev, (byte)unified, 0x01, new byte[] { 0 }, 900)));
                        if (status > 0)
                            Console.WriteLine("      1000.fn0 = " + Hex(link.Call(dev, (byte)status, 0x00, null, 900)));
                        if (dev == 1) break;
                    }
                }
            }
        }

        private static void DumpRazer()
        {
            Console.WriteLine("雷蛇 90 字节控制口探测:");
            bool found = false;
            foreach (HidInterface hi in HidInterface.Enumerate())
            {
                if (hi.Vid != 0x1532 || hi.FeatureReportLen < 90)
                    continue;
                found = true;
                Console.WriteLine("  接口 " + hi.Path);
                IntPtr h = Interop.CreateFileW(hi.Path, 0, Interop.FILE_SHARE_READ | Interop.FILE_SHARE_WRITE,
                    IntPtr.Zero, Interop.OPEN_EXISTING, 0, IntPtr.Zero);
                if (h == IntPtr.Zero || h == new IntPtr(-1))
                {
                    Console.WriteLine("    打开失败: " + Interop.LastError());
                    continue;
                }
                try
                {
                    foreach (byte cmd in new byte[] { 0x80, 0x84 })
                    {
                        var req = new byte[91];
                        req[1] = 0x1F;
                        req[5] = 0x02;
                        req[6] = 0x07;
                        req[7] = cmd;
                        byte crc = 0;
                        for (int i = 2; i <= 87; i++) crc ^= req[i];
                        req[88] = crc;
                        bool set = Interop.HidD_SetFeature(h, req, 91);
                        Console.WriteLine(string.Format("    07:{0:X2} SetFeature={1}", cmd, set));
                        for (int i = 0; i < 4; i++)
                        {
                            Thread.Sleep(80);
                            var resp = new byte[91];
                            if (!Interop.HidD_GetFeature(h, resp, 91))
                            {
                                Console.WriteLine("      GetFeature 失败: " + Interop.LastError());
                                break;
                            }
                            Console.WriteLine(string.Format("      status=0x{0:X2} tid=0x{1:X2} cmd={2:X2}:{3:X2} arg1={4}",
                                resp[0], resp[1], resp[6], resp[7], resp[9]));
                        }
                    }
                }
                finally
                {
                    Interop.CloseHandle(h);
                }
            }
            if (!found)
                Console.WriteLine("  没有找到 90 字节特征报告的雷蛇接口");
        }

        private static void DumpXInput()
        {
            Console.WriteLine("XInput 手柄探测:");
            var sink = new List<BatteryReading>();
            new ControllerProvider().Poll(sink);
            if (sink.Count == 0)
            {
                Console.WriteLine("  没有连接的手柄");
                return;
            }
            foreach (BatteryReading r in sink)
                Console.WriteLine(string.Format("  {0}: {1} ({2})", r.Name, r.Percent >= 0 ? r.Percent + "%" : "未知", r.Note));
            Console.WriteLine("  原始: " + ControllerProvider.LastRaw);
            Console.WriteLine("  WGI:  " + ControllerProvider.LastWgiRaw);
        }

        /// <summary>Brute-force feature report scan, used while reverse engineering a device.</summary>
        public static int ScanFeatureReports(ushort vid)
        {
            foreach (HidInterface hi in HidInterface.Enumerate())
            {
                if (hi.Vid != vid || hi.FeatureReportLen <= 0)
                    continue;
                Console.WriteLine(string.Format("=== {0} up=0x{1:X4} u=0x{2:X4} feat={3}",
                    hi.VidPid, hi.UsagePage, hi.Usage, hi.FeatureReportLen));
                Console.WriteLine("    " + hi.Path);
                IntPtr h = Interop.CreateFileW(hi.Path, 0, Interop.FILE_SHARE_READ | Interop.FILE_SHARE_WRITE,
                    IntPtr.Zero, Interop.OPEN_EXISTING, 0, IntPtr.Zero);
                if (h == IntPtr.Zero || h == new IntPtr(-1))
                {
                    Console.WriteLine("    打开失败 " + Interop.LastError());
                    continue;
                }
                try
                {
                    int len = hi.FeatureReportLen;
                    if (len < 2) len = 2;
                    if (len > 256) len = 256;
                    for (int id = 0; id <= 15; id++)
                    {
                        var buf = new byte[len];
                        buf[0] = (byte)id;
                        bool ok = Interop.HidD_GetFeature(h, buf, len);
                        if (!ok)
                            continue;
                        bool any = false;
                        for (int i = 1; i < len; i++)
                            if (buf[i] != 0) { any = true; break; }
                        if (any)
                            Console.WriteLine(string.Format("    feat 0x{0:X2} -> {1}", id, Hex(buf)));
                    }
                }
                finally
                {
                    Interop.CloseHandle(h);
                }
            }
            return 0;
        }

        /// <summary>Passively dumps input reports for a vendor id, used to look for battery fields.</summary>
        public static int DumpInput(ushort vid, int seconds)
        {
            var links = new List<HidInterface>();
            foreach (HidInterface hi in HidInterface.Enumerate())
                if (hi.Vid == vid && hi.InputReportLen > 0)
                    links.Add(hi);

            var threads = new List<Thread>();
            foreach (HidInterface hi in links)
            {
                HidInterface captured = hi;
                var t = new Thread(delegate()
                {
                    using (var channel = new HidChannel(captured.Path))
                    {
                        if (!channel.IsOpen)
                        {
                            Console.WriteLine("  [打开失败] " + captured.Path);
                            return;
                        }
                        Console.WriteLine(string.Format("  [监听] up=0x{0:X4} u=0x{1:X4} in={2} {3}",
                            captured.UsagePage, captured.Usage, captured.InputReportLen, captured.Path));
                        DateTime end = DateTime.UtcNow.AddSeconds(seconds);
                        while (DateTime.UtcNow < end)
                        {
                            byte[] data = channel.Read(captured.InputReportLen, 600);
                            if (data == null)
                                continue;
                            Console.WriteLine(string.Format("    {0} [{1}] {2}",
                                DateTime.Now.ToString("HH:mm:ss.fff"), data.Length, Hex(data)));
                        }
                    }
                });
                t.IsBackground = true;
                threads.Add(t);
                t.Start();
            }
            foreach (Thread t in threads)
                t.Join(seconds * 1000 + 2000);
            return 0;
        }

        /// <summary>Razer control collection: how access level changes what the device answers.</summary>
        public static int RazerTest()
        {
            foreach (HidInterface hi in HidInterface.Enumerate())
            {
                if (hi.Vid != 0x1532 || hi.FeatureReportLen < 90)
                    continue;
                Console.WriteLine("接口 " + hi.Path);
                Console.WriteLine("  usage=0x" + hi.UsagePage.ToString("X4") + ":0x" + hi.Usage.ToString("X4"));

                uint[] modes = new uint[] { 0x0003, 0x0002, 0 };
                foreach (uint access in modes)
                {
                    IntPtr h = Interop.CreateFileW(hi.Path, access,
                        Interop.FILE_SHARE_READ | Interop.FILE_SHARE_WRITE, IntPtr.Zero,
                        Interop.OPEN_EXISTING, 0, IntPtr.Zero);
                    bool open = h != IntPtr.Zero && h != new IntPtr(-1);
                    Console.WriteLine(string.Format("  access=0x{0:X} open={1}{2}",
                        access, open, open ? "" : " err=" + Interop.LastError()));
                    if (!open)
                        continue;
                    try
                    {
                        var request = new byte[91];
                        request[1] = 0x1F;
                        request[5] = 0x02;
                        request[6] = 0x07;
                        request[7] = 0x80;
                        byte crc = 0;
                        for (int i = 2; i <= 87; i++) crc ^= request[i];
                        request[88] = crc;
                        bool set = Interop.HidD_SetFeature(h, request, 91);
                        Console.WriteLine("    SetFeature=" + set + (set ? "" : " err=" + Interop.LastError()));
                        for (int i = 0; i < 3; i++)
                        {
                            Thread.Sleep(120);
                            var reply = new byte[91];
                            bool got = Interop.HidD_GetFeature(h, reply, 91);
                            if (!got)
                            {
                                Console.WriteLine("    GetFeature err=" + Interop.LastError());
                                break;
                            }
                            Console.WriteLine(string.Format("    status=0x{0:X2} tid=0x{1:X2} cmd={2:X2}:{3:X2} arg1={4} raw={5}",
                                reply[0], reply[1], reply[6], reply[7], reply[9], Hex(reply).Substring(0, 30)));
                        }
                    }
                    finally
                    {
                        Interop.CloseHandle(h);
                    }
                }
            }
            return 0;
        }

        private static string Hex(byte[] data)
        {
            if (data == null) return "(null)";
            var sb = new StringBuilder();
            foreach (byte b in data) sb.Append(b.ToString("X2")).Append(' ');
            return sb.ToString().Trim();
        }
    }
}
