using System;
using System.Collections.Generic;
using System.Threading;
using BattWhale.Providers;

namespace BattWhale
{
    internal sealed class BatteryService : IDisposable
    {
        private readonly List<IBatteryProvider> _providers = new List<IBatteryProvider>();
        private Timer _timer;
        private int _polling;
        private int _interval = 60;

        public List<BatteryReading> Readings = new List<BatteryReading>();
        public event EventHandler Refreshed;

        public BatteryService()
        {
            _providers.Add(new LogitechProvider());
            _providers.Add(new RazerProvider());
            _providers.Add(new ControllerProvider());
        }

        public void Start(int intervalSeconds)
        {
            _interval = Math.Max(10, intervalSeconds);
            Refresh();
            _timer = new Timer(delegate { Refresh(); }, null, _interval * 1000, _interval * 1000);
        }

        public void SetInterval(int intervalSeconds)
        {
            _interval = Math.Max(10, intervalSeconds);
            if (_timer != null)
                _timer.Change(_interval * 1000, _interval * 1000);
        }

        public void Refresh()
        {
            if (Interlocked.CompareExchange(ref _polling, 1, 0) != 0)
                return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    var all = new List<BatteryReading>();
                    foreach (IBatteryProvider provider in _providers)
                    {
                        try
                        {
                            provider.Poll(all);
                        }
                        catch (Exception ex)
                        {
                            // one broken provider must not stop the others
                            if (Environment.GetEnvironmentVariable("BATTWHALE_DEBUG") == "1")
                                Console.WriteLine("[" + provider.Name + "] " + ex);
                        }
                    }
                    all = Sort(all);
                    all = MergeCache(all);
                    ReadingCache.Save(all);
                    Readings = all;
                    EventHandler handler = Refreshed;
                    if (handler != null)
                        handler(this, EventArgs.Empty);
                }
                finally
                {
                    Interlocked.Exchange(ref _polling, 0);
                }
            });
        }

        private static List<BatteryReading> Sort(List<BatteryReading> input)
        {
            var unique = new List<BatteryReading>();
            var keys = new HashSet<string>();
            foreach (BatteryReading r in input)
            {
                if (keys.Add(r.Key))
                    unique.Add(r);
            }
            unique.Sort(delegate(BatteryReading a, BatteryReading b)
            {
                int ka = KindOrder(a.Kind), kb = KindOrder(b.Kind);
                if (ka != kb) return ka.CompareTo(kb);
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            return unique;
        }

        private static List<BatteryReading> MergeCache(List<BatteryReading> fresh)
        {
            var keys = new HashSet<string>();
            var names = new HashSet<string>();
            foreach (BatteryReading r in fresh)
            {
                keys.Add(r.Key);
                if (r.Name.Length > 0 && r.Name != "Logitech 设备")
                    names.Add(r.Name.ToLowerInvariant());
            }
            List<BatteryReading> cached = ReadingCache.Load();
            var merged = new List<BatteryReading>(fresh);
            foreach (BatteryReading r in cached)
            {
                if (keys.Contains(r.Key))
                    continue;
                // A cache written by an older build may hold one row per device index for
                // the same device; do not let those ghosts come back.
                string name = r.Name.ToLowerInvariant();
                if (r.Name == "Logitech 设备")
                    continue;
                if (r.Name.Length > 0 && !names.Add(name))
                    continue;
                merged.Add(r);
            }
            return Sort(merged);
        }

        private static int KindOrder(DeviceKind kind)
        {
            switch (kind)
            {
                case DeviceKind.Keyboard: return 0;
                case DeviceKind.Mouse: return 1;
                case DeviceKind.Controller: return 2;
                case DeviceKind.Headset: return 3;
                default: return 4;
            }
        }

        public void Dispose()
        {
            if (_timer != null)
            {
                _timer.Dispose();
                _timer = null;
            }
        }
    }
}
