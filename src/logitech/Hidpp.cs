using System;
using System.Collections.Generic;
using System.Text;

namespace BattWhale.Providers
{
    internal sealed class HidppReply
    {
        public byte ReportId;
        public byte DeviceIndex;
        public byte FeatureIndex;
        public byte Function;
        public byte SwId;
        public byte ErrorCode;
        public byte[] Params = new byte[0];
        public bool IsError;

        public static HidppReply Parse(byte[] data)
        {
            if (data == null || data.Length < 4)
                return null;
            var r = new HidppReply();
            r.ReportId = data[0];
            r.DeviceIndex = data[1];
            r.FeatureIndex = data[2];
            r.Function = (byte)(data[3] >> 4);
            r.SwId = (byte)(data[3] & 0x0F);
            r.IsError = (data[2] == 0xFF);
            if (r.IsError)
            {
                r.ErrorCode = data.Length > 4 ? data[4] : (byte)0;
            }
            else
            {
                int len = data.Length - 4;
                if (len > 0)
                {
                    r.Params = new byte[len];
                    Array.Copy(data, 4, r.Params, 0, len);
                }
            }
            return r;
        }
    }

    /// <summary>Minimal HID++ 2.0 client bound to one Logitech HID++ collection.</summary>
    internal sealed class HidppLink : IDisposable
    {
        public const byte SoftwareId = 0x0B;

        private readonly HidChannel _channel;
        private readonly int _messageSize;

        public string Path { get; private set; }

        public HidppLink(string path, int reportLen)
        {
            Path = path;
            _messageSize = reportLen >= 20 ? 20 : 7;
            _channel = new HidChannel(path);
        }

        public bool IsOpen { get { return _channel.IsOpen; } }

        private HidppReply Exchange(byte dev, byte feature, byte function, byte[] parameters, int timeoutMs)
        {
            byte fnWithSw = (byte)((function << 4) | SoftwareId);
            var req = new byte[_messageSize];
            req[0] = _messageSize >= 20 ? (byte)0x11 : (byte)0x10;
            req[1] = dev;
            req[2] = feature;
            req[3] = fnWithSw;
            if (parameters != null)
                Array.Copy(parameters, 0, req, 4, Math.Min(parameters.Length, _messageSize - 4));
            if (!_channel.Write(req, Math.Max(120, timeoutMs)))
                return null;

            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                int wait = Math.Max(40, (int)(deadline - DateTime.UtcNow).TotalMilliseconds);
                byte[] data = _channel.Read(_messageSize, wait);
                if (data == null)
                    continue;
                HidppReply reply = HidppReply.Parse(data);
                if (reply == null)
                    continue;
                if (reply.DeviceIndex != dev && reply.DeviceIndex != (byte)(dev ^ 0xFF))
                    continue;
                if (reply.IsError)
                {
                    if (reply.Function == (byte)(function) && reply.SwId == (byte)(fnWithSw & 0x0F))
                        return reply;
                    continue;
                }
                if (reply.FeatureIndex != feature || reply.SwId != (byte)(fnWithSw & 0x0F))
                    continue;
                return reply;
            }
            return null;
        }

        public int GetFeature(byte dev, ushort featureId, int timeoutMs)
        {
            var p = new byte[] { (byte)(featureId >> 8), (byte)(featureId & 0xFF) };
            HidppReply r = Exchange(dev, 0x00, 0x00, p, timeoutMs);
            if (r == null || r.IsError || r.Params.Length < 1)
                return -1;
            return r.Params[0];
        }

        public byte[] Call(byte dev, byte feature, byte function, byte[] parameters, int timeoutMs)
        {
            HidppReply r = Exchange(dev, feature, function, parameters, timeoutMs);
            if (r == null || r.IsError)
                return null;
            return r.Params;
        }

        public void Dispose()
        {
            _channel.Dispose();
        }
    }
}
