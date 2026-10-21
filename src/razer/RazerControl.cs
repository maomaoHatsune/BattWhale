using System;
using System.Collections.Generic;
using System.Threading;

namespace BattWhale.Providers
{
    /// <summary>
    /// Razer's own control device (interface {e3be005d-d130-4910-88ff-09ae02f680e9}).
    /// RzDev_0094.sys creates it for Synapse; two of its IOCTLs forward a feature report
    /// to the real HID device (0x88883010 = IOCTL_HID_SET_FEATURE) and read the reply
    /// back (0x88883014 = IOCTL_HID_GET_FEATURE). Going through it sidesteps the
    /// user-mode HID collection that the driver virtualises.
    /// </summary>
    internal static class RazerControl
    {
        public static readonly Guid InterfaceGuid = new Guid("e3be005d-d130-4910-88ff-09ae02f680e9");

        private const uint IoctlSetFeature = 0x88883010;
        private const uint IoctlGetFeature = 0x88883014;
        private const int ReportSize = 91;      // report id + 90-byte Razer report
        private const byte TransactionId = 0x1F;
        private const byte PowerClass = 0x07;
        private const byte BatteryLevel = 0x80;
        private const byte ChargingState = 0x84;

        public static List<string> FindDevices()
        {
            return HidInterface.EnumeratePaths(InterfaceGuid);
        }

        public sealed class Result
        {
            public int Percent = -1;
            public bool Charging;
            public string Error = "";
        }

        public static Result Read(string path)
        {
            var result = new Result();
            IntPtr handle = Interop.CreateFileW(path,
                Interop.GENERIC_READ | Interop.GENERIC_WRITE,
                Interop.FILE_SHARE_READ | Interop.FILE_SHARE_WRITE, IntPtr.Zero,
                Interop.OPEN_EXISTING, 0, IntPtr.Zero);
            if (handle == IntPtr.Zero || handle == new IntPtr(-1))
            {
                // generic rights are not mapped on this device object; use the raw bits
                handle = Interop.CreateFileW(path, 0x0003,
                    Interop.FILE_SHARE_READ | Interop.FILE_SHARE_WRITE, IntPtr.Zero,
                    Interop.OPEN_EXISTING, 0, IntPtr.Zero);
            }
            if (handle == IntPtr.Zero || handle == new IntPtr(-1))
            {
                result.Error = "无法打开控制设备, err=" + Interop.LastError();
                return result;
            }
            try
            {
                int raw = Query(handle, BatteryLevel, out result.Error);
                if (raw < 0)
                    return result;
                result.Percent = (int)Math.Round(raw / 255.0 * 100.0);
                result.Charging = Query(handle, ChargingState, out result.Error) > 0;
                return result;
            }
            finally
            {
                Interop.CloseHandle(handle);
            }
        }

        private static byte[] BuildRequest(byte commandId)
        {
            var buffer = new byte[ReportSize];
            buffer[0] = 0x00;          // HID report id
            buffer[1] = 0x00;          // status of a request
            buffer[2] = TransactionId;
            buffer[6] = 0x02;          // data size
            buffer[7] = PowerClass;
            buffer[8] = commandId;
            byte crc = 0;
            for (int i = 3; i <= 88; i++)
                crc ^= buffer[i];
            buffer[89] = crc;
            return buffer;
        }

        /// <summary>Returns the raw level byte (0..255) or -1, with a short human reason.</summary>
        private static int Query(IntPtr handle, byte commandId, out string error)
        {
            error = "";
            int written;
            byte[] request = BuildRequest(commandId);
            if (!Interop.DeviceIoControl(handle, IoctlSetFeature, request, ReportSize, null, 0, out written, IntPtr.Zero))
            {
                error = "SET_FEATURE 失败, err=" + Interop.LastError();
                return -1;
            }

            DateTime deadline = DateTime.UtcNow.AddMilliseconds(1200);
            while (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(90);
                var reply = new byte[ReportSize];
                reply[0] = 0x00;
                int got;
                if (!Interop.DeviceIoControl(handle, IoctlGetFeature, reply, ReportSize, reply, ReportSize, out got, IntPtr.Zero))
                {
                    error = "GET_FEATURE 失败, err=" + Interop.LastError();
                    return -1;
                }
                // reply layout: [0] report id, [1] status, [2] tid, [7] class, [8] cmd, [10] arg1
                if (reply[1] == 0x02 && reply[7] == PowerClass && reply[8] == commandId)
                    return reply[10];
                if (reply[1] == 0x05)
                {
                    error = commandId == ChargingState ? "不支持充电状态查询" : "设备不支持该命令";
                    return -1;
                }
                if (reply[1] == 0x04)
                {
                    error = "设备休眠";
                    return -1;
                }
            }
            error = "设备无应答";
            return -1;
        }
    }
}
