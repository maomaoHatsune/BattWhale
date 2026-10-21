using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace BattWhale
{
    internal sealed class HidInterface
    {
        public string Path;
        public ushort Vid;
        public ushort Pid;
        public ushort Version;
        public string Manufacturer = "";
        public string Product = "";
        public string Serial = "";
        public ushort UsagePage;
        public ushort Usage;
        public int InputReportLen;
        public int OutputReportLen;
        public int FeatureReportLen;
        public bool Restricted;   // only openable without read/write access (keyboard & mouse collections)

        public string VidPid
        {
            get { return string.Format("{0:X4}:{1:X4}", Vid, Pid); }
        }

        public string Label
        {
            get
            {
                string name = !string.IsNullOrEmpty(Product) ? Product : "HID";
                return string.Format("{0} [{1}] up=0x{2:X4} u=0x{3:X4} in={4} out={5} feat={6}",
                    name, VidPid, UsagePage, Usage, InputReportLen, OutputReportLen, FeatureReportLen);
            }
        }

        public static readonly Guid HidGuid = new Guid("4D1E55B2-F16F-11CF-88CB-001111000030");

        public static List<HidInterface> Enumerate()
        {
            var list = new List<HidInterface>();
            Guid guid = HidGuid;
            IntPtr set = Interop.SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero,
                Interop.DIGCF_PRESENT | Interop.DIGCF_DEVICEINTERFACE);
            if (set == IntPtr.Zero || set == new IntPtr(-1))
                return list;

            try
            {
                for (int i = 0; ; i++)
                {
                    var did = new Interop.SP_DEVICE_INTERFACE_DATA();
                    did.cbSize = Marshal.SizeOf(typeof(Interop.SP_DEVICE_INTERFACE_DATA));
                    if (!Interop.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref did))
                        break;

                    string path = PathFromInterface(set, ref did);
                    if (string.IsNullOrEmpty(path))
                        continue;
                    HidInterface hi = OpenInfo(path);
                    if (hi != null)
                        list.Add(hi);
                }
            }
            finally
            {
                Interop.SetupDiDestroyDeviceInfoList(set);
            }
            return list;
        }

        /// <summary>Device interface paths for any interface class (used for Razer's control device).</summary>
        public static List<string> EnumeratePaths(Guid guid)
        {
            var list = new List<string>();
            IntPtr set = Interop.SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero,
                Interop.DIGCF_PRESENT | Interop.DIGCF_DEVICEINTERFACE);
            if (set == IntPtr.Zero || set == new IntPtr(-1))
                return list;
            try
            {
                for (int i = 0; ; i++)
                {
                    var did = new Interop.SP_DEVICE_INTERFACE_DATA();
                    did.cbSize = Marshal.SizeOf(typeof(Interop.SP_DEVICE_INTERFACE_DATA));
                    if (!Interop.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref did))
                        break;
                    string path = PathFromInterface(set, ref did);
                    if (!string.IsNullOrEmpty(path))
                        list.Add(path);
                }
            }
            finally
            {
                Interop.SetupDiDestroyDeviceInfoList(set);
            }
            return list;
        }

        private static string PathFromInterface(IntPtr set, ref Interop.SP_DEVICE_INTERFACE_DATA did)
        {
            int needed = 0;
            Interop.SetupDiGetDeviceInterfaceDetail(set, ref did, IntPtr.Zero, 0, ref needed, IntPtr.Zero);
            if (needed <= 0)
                return null;
            IntPtr detail = Marshal.AllocHGlobal(needed);
            try
            {
                Marshal.WriteInt32(detail, 0, IntPtr.Size == 8 ? 8 : 6);
                if (!Interop.SetupDiGetDeviceInterfaceDetail(set, ref did, detail, needed, ref needed, IntPtr.Zero))
                    return null;
                return Marshal.PtrToStringUni(new IntPtr(detail.ToInt64() + 4));
            }
            finally
            {
                Marshal.FreeHGlobal(detail);
            }
        }

        // Reads attributes/caps without keeping the handle open.
        private static HidInterface OpenInfo(string path)
        {
            var hi = new HidInterface();
            hi.Path = path;
            IntPtr h = Open(path, false);
            if (h == IntPtr.Zero || h == new IntPtr(-1))
            {
                // Keyboard / mouse collections reject read+write access for user apps.
                // A query-only handle still reports attributes and caps.
                h = Open(path, 0);
                hi.Restricted = true;
            }
            if (h == IntPtr.Zero || h == new IntPtr(-1))
                return null;
            try
            {
                var attr = new Interop.HIDD_ATTRIBUTES();
                attr.Size = Marshal.SizeOf(typeof(Interop.HIDD_ATTRIBUTES));
                if (!Interop.HidD_GetAttributes(h, ref attr))
                    return null;
                hi.Vid = attr.VendorID;
                hi.Pid = attr.ProductID;
                hi.Version = attr.VersionNumber;

                var sb = new StringBuilder(260);
                if (Interop.HidD_GetProductString(h, sb, 520)) hi.Product = sb.ToString();
                sb = new StringBuilder(260);
                if (Interop.HidD_GetManufacturerString(h, sb, 520)) hi.Manufacturer = sb.ToString();
                sb = new StringBuilder(260);
                if (Interop.HidD_GetSerialNumberString(h, sb, 520)) hi.Serial = sb.ToString();

                IntPtr pre;
                if (Interop.HidD_GetPreparsedData(h, out pre))
                {
                    try
                    {
                        var caps = new Interop.HIDP_CAPS();
                        caps.Reserved = new ushort[17];
                        if (Interop.HidP_GetCaps(pre, ref caps) == 0x00110000)
                        {
                            hi.UsagePage = caps.UsagePage;
                            hi.Usage = caps.Usage;
                            hi.InputReportLen = caps.InputReportByteLength;
                            hi.OutputReportLen = caps.OutputReportByteLength;
                            hi.FeatureReportLen = caps.FeatureReportByteLength;
                        }
                    }
                    finally
                    {
                        Interop.HidD_FreePreparsedData(pre);
                    }
                }
            }
            finally
            {
                Interop.CloseHandle(h);
            }
            return hi;
        }

        public static IntPtr Open(string path, bool write)
        {
            return Open(path, write ? (Interop.GENERIC_READ | Interop.GENERIC_WRITE) : Interop.GENERIC_READ);
        }

        public static IntPtr Open(string path, uint access)
        {
            IntPtr h = Interop.CreateFileW(path, access,
                Interop.FILE_SHARE_READ | Interop.FILE_SHARE_WRITE, IntPtr.Zero,
                Interop.OPEN_EXISTING, 0, IntPtr.Zero);
            return h;
        }
    }

    /// <summary>Tiny synchronous HID channel with a hard timeout on every blocking call.</summary>
    internal sealed class HidChannel : IDisposable
    {
        private readonly IntPtr _handle;
        private readonly object _lock = new object();

        public IntPtr Handle { get { return _handle; } }
        public bool IsOpen { get { return _handle != IntPtr.Zero && _handle != new IntPtr(-1); } }

        public HidChannel(string path)
        {
            _handle = HidInterface.Open(path, true);
        }

        public byte[] GetFeature(int reportId, int length, int timeoutMs)
        {
            var buf = new byte[length];
            buf[0] = (byte)reportId;
            int rc = TimeoutRunner.Run(timeoutMs, delegate
            {
                return Interop.HidD_GetFeature(_handle, buf, length) ? 0 : -1;
            });
            if (rc != 0)
                return null;
            return buf;
        }

        public bool SetFeature(byte[] buf, int timeoutMs)
        {
            int rc = TimeoutRunner.Run(timeoutMs, delegate
            {
                return Interop.HidD_SetFeature(_handle, buf, buf.Length) ? 0 : -1;
            });
            return rc == 0;
        }

        public bool Write(byte[] buf, int timeoutMs)
        {
            int written;
            int rc = TimeoutRunner.Run(timeoutMs, delegate
            {
                return Interop.WriteFile(_handle, buf, buf.Length, out written, IntPtr.Zero) ? 0 : -1;
            });
            return rc == 0;
        }

        public byte[] Read(int length, int timeoutMs)
        {
            var buf = new byte[length];
            var got = new int[1];
            int rc = TimeoutRunner.Run(timeoutMs, delegate
            {
                int n;
                if (Interop.ReadFile(_handle, buf, length, out n, IntPtr.Zero))
                {
                    got[0] = n;
                    return 0;
                }
                return -1;
            });
            if (rc != 0 || got[0] <= 0)
                return null;
            if (got[0] < length)
            {
                var trimmed = new byte[got[0]];
                Array.Copy(buf, trimmed, got[0]);
                return trimmed;
            }
            return buf;
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (IsOpen)
                    Interop.CloseHandle(_handle);
            }
        }
    }

    internal static class TimeoutRunner
    {
        /// <summary>Runs a blocking IO call on a worker thread; cancels it via CancelSynchronousIo after the timeout.</summary>
        public static int Run(int timeoutMs, Func<int> call)
        {
            int result = 0;
            Exception error = null;
            uint threadId = 0;
            var done = new ManualResetEventSlim(false);
            var thread = new Thread(delegate()
            {
                threadId = Interop.GetCurrentThreadId();
                try
                {
                    result = call();
                }
                catch (Exception ex)
                {
                    error = ex;
                    result = -2;
                }
                finally
                {
                    done.Set();
                }
            });
            thread.IsBackground = true;
            thread.Start();
            bool finished = done.Wait(timeoutMs);
            if (!finished)
            {
                IntPtr th = Interop.OpenThread(Interop.THREAD_TERMINATE | Interop.THREAD_SUSPEND_RESUME, false, threadId);
                if (th != IntPtr.Zero)
                {
                    Interop.CancelSynchronousIo(th);
                    Interop.CloseHandle(th);
                }
                done.Wait(500);
                return -3;
            }
            if (error != null)
                return -2;
            return result;
        }
    }
}

