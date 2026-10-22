using System;
using System.Collections.Generic;
using System.Threading;
using Windows.Devices.Power;
using Windows.Gaming.Input;
using Windows.System.Power;

namespace BattWhale.Providers
{
    /// <summary>
    /// One controller Windows knows about, with the battery report Windows.Gaming.Input exposes.
    /// WGI is the API the Xbox Accessories app uses: many third-party Xbox-protocol pads on a
    /// 2.4G dongle never report a battery through XInput (the dongle claims "wired"), but WGI
    /// still returns a real report with the level and the charging state.
    /// </summary>
    internal sealed class WgiPad
    {
        public string Name = "";
        public ushort Vid;
        public ushort Pid;
        public bool HasBattery;
        public int Percent;
        public bool Charging;
        public bool Full;
        public string Raw = "";
    }

    internal static class WgiBattery
    {
        /// <summary>Cleared after the first hard failure so we do not pay for it on every poll.</summary>
        public static bool Available = true;
        public static string LastError = "";

        /// <summary>Reads every controller once; waits up to waitMs for the list to fill on first use.</summary>
        public static List<WgiPad> Query(int waitMs)
        {
            var pads = new List<WgiPad>();
            if (!Available)
                return pads;

            try
            {
                IReadOnlyList<RawGameController> list = RawGameController.RawGameControllers;
                DateTime end = DateTime.UtcNow.AddMilliseconds(waitMs);
                while ((list == null || list.Count == 0) && DateTime.UtcNow < end)
                {
                    Thread.Sleep(100);
                    list = RawGameController.RawGameControllers;
                }
                if (list == null)
                    return pads;

                foreach (RawGameController c in list)
                {
                    var pad = new WgiPad();
                    try
                    {
                        pad.Name = c.DisplayName ?? "";
                        pad.Vid = c.HardwareVendorId;
                        pad.Pid = c.HardwareProductId;
                    }
                    catch
                    {
                        continue;
                    }

                    try
                    {
                        BatteryReport report = c.TryGetBatteryReport();
                        if (report != null)
                        {
                            int? remain = report.RemainingCapacityInMilliwattHours;
                            int? full = report.FullChargeCapacityInMilliwattHours;
                            BatteryStatus status = report.Status;
                            pad.Raw = string.Format("status={0} remain={1} full={2}",
                                status, remain, full);
                            // Windows hands a dongle's percentage over as a 1000 mWh scale.
                            if (full.HasValue && full.Value > 0 && remain.HasValue && remain.Value >= 0)
                            {
                                int pct = (int)Math.Round(remain.Value * 100.0 / full.Value);
                                if (pct < 0) pct = 0;
                                if (pct > 100) pct = 100;
                                pad.Percent = pct;
                                pad.HasBattery = true;
                            }
                            pad.Charging = status == BatteryStatus.Charging;
                            pad.Full = status == BatteryStatus.Idle && pad.HasBattery && pad.Percent >= 95;
                        }
                    }
                    catch (Exception ex)
                    {
                        pad.Raw = "battery error: " + ex.Message;
                    }
                    pads.Add(pad);
                }
            }
            catch (Exception ex)
            {
                Available = false;
                LastError = ex.GetType().Name + ": " + ex.Message;
            }
            return pads;
        }
    }
}
