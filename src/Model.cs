using System;

namespace BattWhale
{
    internal enum DeviceKind
    {
        Unknown,
        Keyboard,
        Mouse,
        Controller,
        Headset
    }

    internal enum BatteryState
    {
        Unknown,
        Discharging,
        Charging,
        Full,
        Offline,
        Blocked
    }

    internal sealed class BatteryReading
    {
        public string Key = "";
        public string Name = "";
        public DeviceKind Kind = DeviceKind.Unknown;
        public int Percent = -1;
        public BatteryState State = BatteryState.Unknown;
        public string Note = "";
        public DateTime Updated = DateTime.Now;

        public bool IsOnline
        {
            get { return State != BatteryState.Offline && State != BatteryState.Blocked; }
        }

        public string StatusText
        {
            get
            {
                switch (State)
                {
                    case BatteryState.Charging: return "充电中";
                    case BatteryState.Full: return "已充满";
                    case BatteryState.Offline: return Note.Length > 0 ? Note : "休眠中";
                    case BatteryState.Blocked: return Note.Length > 0 ? Note : "无法读取";
                    default: return Note.Length > 0 ? Note : (Percent >= 0 ? Percent + "%" : "未知");
                }
            }
        }
    }

    internal interface IBatteryProvider
    {
        string Name { get; }
        void Poll(System.Collections.Generic.List<BatteryReading> sink);
    }
}
