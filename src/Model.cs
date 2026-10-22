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
                    case BatteryState.Charging: return Strings.Charging;
                    case BatteryState.Full: return Strings.Full;
                    // Discharging: the big number on the right already shows the percentage, so
                    // this line says what the device is doing instead of repeating it.
                    case BatteryState.Discharging: return Note.Length > 0 ? Note : Strings.InUse;
                    case BatteryState.Offline: return Note.Length > 0 ? Note : Strings.Asleep;
                    case BatteryState.Blocked: return Note.Length > 0 ? Note : Strings.Unreadable;
                    default: return Note.Length > 0 ? Note : (Percent >= 0 ? Percent + "%" : Strings.Unknown);
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
