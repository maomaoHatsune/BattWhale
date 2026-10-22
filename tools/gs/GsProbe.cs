// GameSir vendor-channel probe / GameSir 鍘傚晢閫氶亾鎺㈤拡
// Build: csc /nologo /target:exe /out:GsProbe.exe ..\..\src\Interop.cs ..\..\src\Hid.cs GsProbe.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace BattWhale
{
    internal static class GsProbe
    {
        private const ushort Vid = 0x3537;

        private static int Main(string[] args)
        {
            string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "desc";
            try
            {
                switch (mode)
                {
                    case "desc":
                        DumpDescriptors();
                        return 0;
                    case "listen":
                        Listen(args.Length > 1 ? int.Parse(args[1]) : 5);
                        return 0;
                    case "send":
                        Send(args[1], args.Length > 2 ? int.Parse(args[2]) : 1200);
                        return 0;
                    case "sweep":
                        Sweep();
                        return 0;
                    case "sweepcmd":
                        SweepCmd(args.Length > 1 ? byte.Parse(args[1], NumberStyles.HexNumber) : (byte)0x00,
                                 args.Length > 2 ? int.Parse(args[2]) : 60);
                        return 0;
                    case "q":
                        Send(args[1], args.Length > 2 ? int.Parse(args[2]) : 600, true);
                        return 0;
                    case "crc":
                        CrcProbe();
                        return 0;
                    case "listenif":
                        ListenIf(ushort.Parse(args[1], NumberStyles.HexNumber), ushort.Parse(args[2], NumberStyles.HexNumber),
                                 args.Length > 3 ? int.Parse(args[3]) : 3);
                        return 0;
                    case "recv":
                        Recv(args.Length > 1 ? int.Parse(args[1]) : 3000);
                        return 0;
                    case "cand":
                        Candidates();
                        return 0;
                }
                Console.WriteLine("modes: desc | listen <sec> | recv <ms> | send <hex> [ms] | sweep | cand");
                return 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine("ERR " + ex.Message);
                return 2;
            }
        }

        private static List<HidInterface> Devices()
        {
            var all = new List<HidInterface>();
            foreach (HidInterface hi in HidInterface.Enumerate())
                if (hi.Vid == Vid)
                    all.Add(hi);
            return all;
        }

        private static HidInterface VendorChannel()
        {
            foreach (HidInterface hi in Devices())
                if (hi.UsagePage == 0xFFA0)
                    return hi;
            return null;
        }

        private static void DumpDescriptors()
        {
            foreach (HidInterface hi in Devices())
            {
                Console.WriteLine(hi.Label);
                Console.WriteLine("  " + hi.Path);
                byte[] desc = ReportDescriptor(hi.Path);
                if (desc == null)
                {
                    Console.WriteLine("  <no descriptor>");
                    continue;
                }
                Console.WriteLine("  descriptor " + desc.Length + " bytes");
                for (int i = 0; i < desc.Length; i += 16)
                {
                    var sb = new StringBuilder();
                    sb.Append("    ").Append(i.ToString("X3")).Append("  ");
                    for (int j = i; j < i + 16 && j < desc.Length; j++)
                        sb.Append(desc[j].ToString("X2")).Append(' ');
                    Console.WriteLine(sb.ToString());
                }
                Console.WriteLine("  ids: " + ReportIds(desc));
                Console.WriteLine();
            }
        }

        private static string ReportIds(byte[] desc)
        {
            var ids = new List<string>();
            for (int i = 0; i < desc.Length; i++)
            {
                byte b = desc[i];
                if (b == 0x85 && i + 1 < desc.Length)
                    ids.Add(desc[i + 1].ToString("X2"));
                if (b == 0x86 && i + 1 < desc.Length)
                    i++;
            }
            return ids.Count == 0 ? "none(0)" : string.Join(",", ids.ToArray());
        }

        private const uint IOCTL_HID_GET_REPORT_DESCRIPTOR = 0x000B0000;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(IntPtr h, uint code, byte[] inBuf, int inLen,
            byte[] outBuf, int outLen, out int returned, IntPtr overlapped);

        private static byte[] ReportDescriptor(string path)
        {
            uint[] access = new uint[]
            {
                0,
                Interop.GENERIC_READ,
                Interop.GENERIC_READ | Interop.GENERIC_WRITE
            };
            foreach (uint acc in access)
            {
                IntPtr h = Interop.CreateFileW(path, acc, Interop.FILE_SHARE_READ | Interop.FILE_SHARE_WRITE,
                    IntPtr.Zero, Interop.OPEN_EXISTING, 0, IntPtr.Zero);
                if (h == IntPtr.Zero || h == new IntPtr(-1))
                    continue;
                try
                {
                    var buf = new byte[4096];
                    int got;
                    if (!DeviceIoControl(h, IOCTL_HID_GET_REPORT_DESCRIPTOR, null, 0, buf, buf.Length, out got, IntPtr.Zero) || got <= 0)
                        continue;
                    var trimmed = new byte[got];
                    Array.Copy(buf, trimmed, got);
                    return trimmed;
                }
                finally
                {
                    Interop.CloseHandle(h);
                }
            }
            return null;
        }

        private static void Listen(int seconds)
        {
            HidInterface ch = VendorChannel();
            if (ch == null)
            {
                Console.WriteLine("no 0xFFA0 channel");
                return;
            }
            Console.WriteLine("listening " + seconds + "s on " + ch.Label.Replace("\n", " "));
            using (var chan = new HidChannel(ch.Path))
            {
                if (!chan.IsOpen)
                {
                    Console.WriteLine("open failed " + Marshal.GetLastWin32Error());
                    return;
                }
                DateTime end = DateTime.UtcNow.AddSeconds(seconds);
                int n = 0;
                while (DateTime.UtcNow < end)
                {
                    byte[] r = chan.Read(ch.InputReportLen > 0 ? ch.InputReportLen : 64, 500);
                    if (r == null)
                        continue;
                    Console.WriteLine("  rx " + Hex(r));
                    if (++n > 300)
                        break;
                }
                Console.WriteLine("reports: " + n);
            }
        }

        private static void Send(string hex, int listenMs)
        {
            Send(hex, listenMs, false);
        }

        private static void Send(string hex, int listenMs, bool quiet)
        {
            HidInterface ch = VendorChannel();
            if (ch == null)
            {
                Console.WriteLine("no 0xFFA0 channel");
                return;
            }
            byte[] payload = ParseHex(hex);
            int outLen = ch.OutputReportLen > 0 ? ch.OutputReportLen : 32;
            var buf = new byte[outLen];
            Array.Copy(payload, buf, Math.Min(payload.Length, outLen));
            using (var chan = new HidChannel(ch.Path))
            {
                if (!chan.IsOpen)
                {
                    Console.WriteLine("open failed " + Marshal.GetLastWin32Error());
                    return;
                }
                bool ok = chan.Write(buf, 1000);
                Console.WriteLine("tx " + Hex(buf) + (ok ? " ok" : " FAIL " + Marshal.GetLastWin32Error()));
                DateTime end = DateTime.UtcNow.AddMilliseconds(listenMs);
                int n = 0, interesting = 0;
                var seen = new HashSet<string>();
                while (DateTime.UtcNow < end)
                {
                    byte[] r = chan.Read(ch.InputReportLen > 0 ? ch.InputReportLen : 64, 300);
                    if (r == null)
                        continue;
                    n++;
                    bool hot = Interesting(r);
                    if (hot)
                        interesting++;
                    if (quiet && !hot)
                        continue;
                    string s = Hex(r);
                    if (!seen.Add(s) && !quiet)
                        continue;
                    Console.WriteLine((hot ? "  * " : "    ") + s);
                    if (n > 60)
                        break;
                }
                Console.WriteLine("reports=" + n + " interesting=" + interesting);
            }
        }

        // Try a few checksum conventions on a known command body.
        private static void CrcProbe()
        {
            HidInterface ch = VendorChannel();
            int outLen = ch.OutputReportLen > 0 ? ch.OutputReportLen : 32;
            string[] names = new string[] { "none", "xor-last", "sum-last", "xor2-last", "len-last", "xor-idx1" };
            foreach (string name in names)
            {
                var buf = new byte[outLen];
                buf[0] = 0x07;
                buf[1] = 0x01;
                byte x = 0;
                for (int i = 1; i < outLen - 1; i++)
                    x ^= buf[i];
                switch (name)
                {
                    case "xor-last": buf[outLen - 1] = x; break;
                    case "sum-last":
                        int sum = 0;
                        for (int i = 1; i < outLen - 1; i++) sum += buf[i];
                        buf[outLen - 1] = (byte)sum;
                        break;
                    case "xor2-last": buf[outLen - 2] = x; break;
                    case "len-last": buf[outLen - 1] = 0x1F; break;
                    case "xor-idx1": buf[1] = (byte)(buf[1] ^ 0xFF); break;
                }
                using (var chan = new HidChannel(ch.Path))
                {
                    chan.Write(buf, 800);
                    DateTime end = DateTime.UtcNow.AddMilliseconds(250);
                    string first = "-";
                    while (DateTime.UtcNow < end)
                    {
                        byte[] r = chan.Read(64, 100);
                        if (r == null || !Interesting(r))
                            continue;
                        first = Hex(r);
                        if (first.Length > 60) first = first.Substring(0, 60) + "...";
                        break;
                    }
                    Console.WriteLine(string.Format("{0,-10} -> {1}", name, first));
                }
            }
        }

        private static void ListenIf(ushort usagePage, ushort usage, int seconds)
        {
            HidInterface target = null;
            foreach (HidInterface hi in Devices())
                if (hi.UsagePage == usagePage && hi.Usage == usage)
                    target = hi;
            if (target == null)
            {
                Console.WriteLine("no interface");
                return;
            }
            Console.WriteLine("listening " + seconds + "s on " + target.Path);
            using (var chan = new HidChannel(target.Path))
            {
                if (!chan.IsOpen)
                {
                    Console.WriteLine("open failed " + Marshal.GetLastWin32Error());
                    return;
                }
                DateTime end = DateTime.UtcNow.AddSeconds(seconds);
                int n = 0;
                var last = "";
                while (DateTime.UtcNow < end)
                {
                    byte[] r = chan.Read(target.InputReportLen > 0 ? target.InputReportLen : 64, 300);
                    if (r == null)
                        continue;
                    n++;
                    string s = Hex(r);
                    if (s == last)
                        continue;
                    last = s;
                    Console.WriteLine("  rx " + s);
                }
                Console.WriteLine("reports=" + n);
            }
        }

        // Print only input reports that carry a non-zero payload beyond byte 0.
        private static bool Interesting(byte[] r)
        {
            for (int i = 1; i < r.Length; i++)
                if (r[i] != 0)
                    return true;
            return false;
        }

        private static void Recv(int ms)
        {
            HidInterface ch = VendorChannel();
            if (ch == null)
            {
                Console.WriteLine("no 0xFFA0 channel");
                return;
            }
            using (var chan = new HidChannel(ch.Path))
            {
                DateTime end = DateTime.UtcNow.AddMilliseconds(ms);
                int all = 0, hit = 0;
                var seen = new HashSet<string>();
                while (DateTime.UtcNow < end)
                {
                    byte[] r = chan.Read(ch.InputReportLen > 0 ? ch.InputReportLen : 64, 300);
                    if (r == null)
                        continue;
                    all++;
                    if (!Interesting(r))
                        continue;
                    hit++;
                    string key = Hex(r);
                    if (!seen.Add(key))
                        continue;
                    Console.WriteLine("  rx " + key);
                }
                Console.WriteLine("total=" + all + " interesting=" + hit);
            }
        }

        // Try every report id with an all-zero body and report which ones answer.
        private static void Sweep()
        {
            HidInterface ch = VendorChannel();
            if (ch == null)
            {
                Console.WriteLine("no 0xFFA0 channel");
                return;
            }
            int outLen = ch.OutputReportLen > 0 ? ch.OutputReportLen : 32;
            using (var chan = new HidChannel(ch.Path))
            {
                for (int id = 1; id <= 40; id++)
                {
                    var buf = new byte[outLen];
                    buf[0] = (byte)id;
                    bool ok = chan.Write(buf, 800);
                    var replies = new List<string>();
                    DateTime end = DateTime.UtcNow.AddMilliseconds(220);
                    while (DateTime.UtcNow < end)
                    {
                        byte[] r = chan.Read(ch.InputReportLen > 0 ? ch.InputReportLen : 64, 120);
                        if (r == null)
                            continue;
                        if (Interesting(r))
                            replies.Add(Hex(r));
                    }
                    string first = replies.Count > 0 ? replies[0] : "-";
                    if (first.Length > 90)
                        first = first.Substring(0, 90) + "...";
                    Console.WriteLine(string.Format("id {0:X2} write={1} replies={2} {3}",
                        id, ok ? "ok" : "fail", replies.Count, first));
                }
            }
        }

        // Sweep every value of out[1] (the opcode) and summarise the reply.
        private static void SweepCmd(byte prefix, int settleMs)
        {
            HidInterface ch = VendorChannel();
            if (ch == null)
            {
                Console.WriteLine("no 0xFFA0 channel");
                return;
            }
            int outLen = ch.OutputReportLen > 0 ? ch.OutputReportLen : 32;
            using (var chan = new HidChannel(ch.Path))
            {
                for (int cmd = 0; cmd <= 0xFF; cmd++)
                {
                    var buf = new byte[outLen];
                    buf[0] = 0x07;
                    buf[1] = (byte)cmd;
                    if (prefix != 0)
                        buf[2] = prefix;
                    if (!chan.Write(buf, 800))
                    {
                        Console.WriteLine(string.Format("cmd {0:X2} write fail", cmd));
                        continue;
                    }
                    var replies = new List<string>();
                    DateTime end = DateTime.UtcNow.AddMilliseconds(settleMs);
                    while (DateTime.UtcNow < end)
                    {
                        byte[] r = chan.Read(ch.InputReportLen > 0 ? ch.InputReportLen : 64, 40);
                        if (r == null)
                            continue;
                        if (Interesting(r))
                            replies.Add(Hex(r));
                    }
                    if (replies.Count == 0)
                        continue;
                    string s = replies[0];
                    if (s.Length > 70)
                        s = s.Substring(0, 70) + "...";
                    Console.WriteLine(string.Format("cmd {0:X2} n={1} {2}", cmd, replies.Count, s));
                }
            }
        }

        // Candidate vendor commands; several per line, first byte = report body start.
        private static void Candidates()
        {
            string[] list = new string[]
            {
                // GameSir Nexus style short commands (header + opcode)
                "0F 00", "0F 01", "0F 02", "0F 03", "0F 04", "0F 05",
                "02 00", "02 01", "02 02", "02 03", "02 04", "02 05", "02 06", "02 07", "02 08",
                "05 00", "05 01", "05 02",
                "01 00", "01 01",
                "A1 C8", "A2 01 00", "A2 05",
                "25 01", "26 01", "27 01", "28 01", "29 01", "2A 01",
                "0C 00", "0D 00", "10 00", "11 00",
            };
            foreach (string c in list)
                Send(c, 350);
        }

        private static byte[] ParseHex(string s)
        {
            var parts = s.Split(new char[] { ' ', ',', ':', '-' }, StringSplitOptions.RemoveEmptyEntries);
            var bytes = new byte[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                bytes[i] = byte.Parse(parts[i], NumberStyles.HexNumber);
            return bytes;
        }

        private static string Hex(byte[] b)
        {
            var sb = new StringBuilder(b.Length * 3);
            for (int i = 0; i < b.Length; i++)
            {
                sb.Append(b[i].ToString("X2"));
                if (i + 1 < b.Length)
                    sb.Append(' ');
            }
            return sb.ToString();
        }
    }
}

