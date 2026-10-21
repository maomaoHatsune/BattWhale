using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace BattWhale.Providers
{
    /// <summary>XInput controllers (Xbox mode dongles, including third-party 2.4G pads).</summary>
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

        public void Poll(List<BatteryReading> sink)
        {
            for (int index = 0; index < 4; index++)
            {
                XINPUT_STATE state = new XINPUT_STATE();
                int rc = SafeGetState(index, ref state);
                if (rc != 0)
                    continue;

                var info = new XINPUT_BATTERY_INFORMATION();
                int brc = SafeGetBattery(index, ref info);
                LastRaw = string.Format("index={0} rc={1} type={2} level={3}", index, brc, info.BatteryType, info.BatteryLevel);

                var reading = new BatteryReading();
                reading.Key = "xinput:" + index;
                bool gameSirDongle;
                reading.Name = PadName(out gameSirDongle);
                reading.Kind = DeviceKind.Controller;
                reading.Updated = DateTime.Now;

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

        private static string PadName(out bool gameSirDongle)
        {
            gameSirDongle = false;
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
