// Probe: read controller battery through Windows.Gaming.Input (WinRT) from .NET Framework.
using System;
using System.Collections.Generic;
using System.Threading;
using Windows.Devices.Power;
using Windows.Gaming.Input;

internal static class WgiProbe
{
    private static int Main(string[] args)
    {
        int seconds = args.Length > 0 ? int.Parse(args[0]) : 5;
        DateTime end = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < end && RawGameController.RawGameControllers.Count == 0)
            Thread.Sleep(100);
        Thread.Sleep(300);

        IReadOnlyList<RawGameController> list = RawGameController.RawGameControllers;
        Console.WriteLine("count=" + list.Count);
        foreach (RawGameController c in list)
        {
            Console.WriteLine(string.Format("{0} {1:X4}:{2:X4} wireless={3}",
                c.DisplayName, c.HardwareVendorId, c.HardwareProductId, c.IsWireless));
            BatteryReport b = null;
            try { b = c.TryGetBatteryReport(); }
            catch (Exception ex) { Console.WriteLine("  battery ex: " + ex.Message); }
            if (b == null)
            {
                Console.WriteLine("  battery=<null>");
                continue;
            }
            Console.WriteLine(string.Format("  status={0} remain={1} full={2} rate={3}",
                b.Status, b.RemainingCapacityInMilliwattHours, b.FullChargeCapacityInMilliwattHours,
                b.ChargeRateInMilliwatts));
        }
        return 0;
    }
}
