using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace BattWhale.Providers
{
    /// <summary>
    /// XInput controllers (Xbox mode dongles, including third-party 2.4G pads).
    /// Battery: Windows.Gaming.Input first (real percentage + charging state, what the
    /// Xbox Accessories app reads), XInput's four coarse levels as the fallback.
    /// </summary>
    internal sealed class ControllerProvider : IBatteryProvider
    {
        private const byte BATTERY_DEVTYPE_GAMEPAD = 0x00;
        private const byte BATTERY_TYPE_DISCONNECTED = 0x00;
        private const byte BATTERY_TYPE_WIRED = 0x01;

        [StructLayout(LayoutKind.Sequential)]
        private struct XINPUT_BATTERY_INFORMATION
        {
            public byte BatteryType;
            public byte BatteryLevel;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XINPUT_STATE
        {
            public uint dwPacketNumber;
            public ushort wButtons;
            public byte bLeftTrigger;
            public byte bRightTrigger;
            public short sThumbLX;
            public short sThumbLY;
            public short sThumbRX;
            public short sThumbRY;
        }

        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
        private static extern int XInputGetState14(int index, ref XINPUT_STATE state);

        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetBatteryInformation")]
        private static extern int XInputGetBatteryInformation14(int index, byte type, ref XINPUT_BATTERY_INFORMATION info);

        [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
        private static extern int XInputGetState910(int index, ref XINPUT_STATE state);

        [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetBatteryInformation")]
        private static extern int XInputGetBatteryInformation910(int index, byte type, ref XINPUT_BATTERY_INFORMATION info);

        public string Name { get { return "XInput"; } }

        public static string LastRaw = "";
        public static string LastWgiRaw = "";

        public void Poll(List<BatteryReading> sink)
        {
            var connected = new List<int>();
            for (int index = 0; index < 4; index++)
            {
                XINPUT_STATE state = new XINPUT_STATE();
                if (SafeGetState(index, ref state) == 0)
                    connected.Add(index);
            }
            if (connected.Count == 0)
                return;

            List<WgiPad> pads = WgiBattery.Query(1200);
            LastWgiRaw = DescribeWgi(pads);

            for (int i = 0; i < connected.Count; i++)
            {
                int index = connected[i];
                var info = new XINPUT_BATTERY_INFORMATION();
                int brc = SafeGetBattery(index, ref info);
                LastRaw = string.Format("index={0} rc={1} type={2} level={3}", index, brc, info.BatteryType, info.BatteryLevel);

                var reading = new BatteryReading();
                reading.Key = "xinput:" + index;
                bool gameSirDongle;
                WgiPad pad = MatchPad(pads, i, connected.Count);
                reading.Name = PadName(pad, out gameSirDongle);
                reading.Kind = DeviceKind.Controller;
                reading.Updated = DateTime.Now;

                if (pad != null && pad.HasBattery)
                {
                    reading.Percent = pad.Percent;
                    if (pad.Charging)
                        reading.State = BatteryState.Charging;
                    else if (pad.Full)
                        reading.State = BatteryState.Full;
                    else
                        reading.State = BatteryState.Discharging;
                    sink.Add(reading);
                    continue;
                }

                if (brc != 0 || info.BatteryType == BATTERY_TYPE_DISCONNECTED)
                {
                    reading.State = BatteryState.Unknown;
                    reading.Note = "读不到电量";
                }
                else if (info.BatteryType == BATTERY_TYPE_WIRED)
                {
                    reading.State = BatteryState.Unknown;
                    // A 2.4G dongle usually claims to be wired, so do not call it that.
                    reading.Note = gameSirDongle ? "手柄未上报电量" : "有线供电";
                }
                else
                {
                    int level = info.BatteryLevel;
                    if (level > 4) level = 4;
                    reading.Percent = level * 25;
                    reading.State = BatteryState.Discharging;
                    if (level == 4)
                        reading.Percent = 100;
                }
                sink.Add(reading);
            }
        }

        /// <summary>Pairs the i-th connected pad with the i-th controller Windows lists.</summary>
        private static WgiPad MatchPad(List<WgiPad> pads, int position, int count)
        {
            if (pads == null || pads.Count == 0)
                return null;
            if (pads.Count == count && position < pads.Count)
                return pads[position];
            if (count == 1)
            {
                foreach (WgiPad pad in pads)
                    if (pad.HasBattery)
                        return pad;
                return pads[0];
            }
            return position < pads.Count ? pads[position] : null;
        }

        private static string DescribeWgi(List<WgiPad> pads)
        {
            if (!WgiBattery.Available)
                return "unavailable: " + WgiBattery.LastError;
            if (pads == null || pads.Count == 0)
                return "no controllers listed";
            var parts = new List<string>();
            foreach (WgiPad pad in pads)
                parts.Add(string.Format("{0} {1:X4}:{2:X4} {3}", pad.Name, pad.Vid, pad.Pid, pad.Raw));
            return string.Join(" | ", parts.ToArray());
        }

        private static string PadName(WgiPad pad, out bool gameSirDongle)
        {
            gameSirDongle = false;
            if (pad != null && pad.Vid == 0x3537)
            {
                gameSirDongle = true;
                return "GameSir 手柄";
            }
            foreach (HidInterface hi in HidInterface.Enumerate())
            {
                if (hi.Vid == 0x3537)
                {
                    gameSirDongle = true;
                    return "GameSir 手柄";
                }
            }
            return "Xbox 手柄";
        }

        private static int SafeGetState(int index, ref XINPUT_STATE state)
        {
            try
            {
                return XInputGetState14(index, ref state);
            }
            catch (DllNotFoundException)
            {
                return XInputGetState910(index, ref state);
            }
            catch (EntryPointNotFoundException)
            {
                return XInputGetState910(index, ref state);
            }
        }

        private static int SafeGetBattery(int index, ref XINPUT_BATTERY_INFORMATION info)
        {
            try
            {
                return XInputGetBatteryInformation14(index, BATTERY_DEVTYPE_GAMEPAD, ref info);
            }
            catch (DllNotFoundException)
            {
                return XInputGetBatteryInformation910(index, BATTERY_DEVTYPE_GAMEPAD, ref info);
            }
            catch (EntryPointNotFoundException)
            {
                return XInputGetBatteryInformation910(index, BATTERY_DEVTYPE_GAMEPAD, ref info);
            }
        }
    }
}
