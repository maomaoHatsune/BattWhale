using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

namespace BattWhale
{
    internal sealed class TrayApp : ApplicationContext
    {
        private readonly TrayIcon _tray;
        private readonly BatteryService _service;
        private readonly AppSettings _settings;
        private readonly HashSet<string> _lowNotified = new HashSet<string>();
        private readonly System.Threading.EventWaitHandle _showSignal;
        private readonly Timer _signalTimer;
        private volatile bool _showRequested;
        private FlyoutForm _flyout;
        private Icon _currentIcon;
        private bool _fallbackShown;
        private bool _disposed;

        public TrayApp(System.Threading.EventWaitHandle showSignal)
        {
            _showSignal = showSignal;
            _settings = AppSettings.Load();
            _service = new BatteryService();
            _service.Refreshed += OnRefreshed;

            _tray = new TrayIcon();
            _tray.LeftClick += delegate { ShowPanel(); };
            // Right click opens the same panel: the actions live in its footer now, so
            // no separate context-menu window pops up.
            _tray.RightClick += delegate { ShowPanel(); };

            UpdateIcon(new List<BatteryReading>());
            if (_tray.Added)
                DebugLog.Always("托盘图标已注册 " + _tray.Probe());
            else
                DebugLog.Always("托盘图标注册失败: " + _tray.LastError + " / " + _tray.Probe());

            if (_showSignal != null)
            {
                var thread = new System.Threading.Thread(delegate()
                {
                    while (!_disposed)
                    {
                        try
                        {
                            if (!_showSignal.WaitOne(1000))
                                continue;
                            _showRequested = true;
                        }
                        catch (Exception)
                        {
                            return;
                        }
                    }
                });
                thread.IsBackground = true;
                thread.Start();

                _signalTimer = new Timer();
                _signalTimer.Interval = 300;
                _signalTimer.Tick += delegate
                {
                    if (!_showRequested)
                        return;
                    _showRequested = false;
                    ShowPanel();
                };
                _signalTimer.Start();
            }

            DebugLog.Write("tray started, interval=" + _settings.IntervalSeconds + "s");
            _service.Start(_settings.IntervalSeconds);
        }

        private void OnRefreshed(object sender, EventArgs e)
        {
            try
            {
                Control anchor = _tray.UiAnchor;
                if (anchor != null && anchor.InvokeRequired)
                {
                    anchor.BeginInvoke(new EventHandler(delegate { ApplyRefresh(); }));
                    return;
                }
                ApplyRefresh();
            }
            catch (Exception)
            {
            }
        }

        private void ApplyRefresh()
        {
            if (_disposed) return;
            List<BatteryReading> readings = VisibleReadings();
            var summary = new StringBuilder("refresh: " + readings.Count + " device(s)");
            foreach (BatteryReading r in readings)
                summary.Append(" | ").Append(r.Name).Append(' ').Append(r.Percent).Append('%').Append(' ')
                       .Append(r.State);
            DebugLog.Write(summary.ToString());
            UpdateIcon(readings);
            if (!_tray.Added && !_fallbackShown && readings.Count > 0)
            {
                // No tray icon means no way to reach the app: show the panel once and leave
                // a note in the log. A modal box here can strand the UI thread, so avoid it.
                _fallbackShown = true;
                DebugLog.Always("托盘图标缺失，降级为面板模式: " + _tray.LastError);
                ShowPanel();
            }
            if (!_settings.HintShown && readings.Count > 0)
            {
                _settings.HintShown = true;
                _settings.Save();
                _tray.Balloon("鲸电量已启动",
                    "图标在任务栏右下角的 ^ 里，点一下就能看到面板，操作都在面板底部。", false);
            }
            if (_flyout != null && !_flyout.IsDisposed)
            {
                _flyout.Close();
                _flyout = null;
            }
            NotifyLowBatteries(readings);
        }

        private List<BatteryReading> VisibleReadings()
        {
            var result = new List<BatteryReading>();
            foreach (BatteryReading r in _service.Readings)
            {
                if (!_settings.Hidden.Contains(r.Key))
                    result.Add(r);
            }
            return result;
        }

        private void UpdateIcon(List<BatteryReading> readings)
        {
            Icon next = IconFactory.BuildTrayIcon(readings, Palette.LightTaskbar());
            if (_currentIcon != null)
                _currentIcon.Dispose();
            _currentIcon = next;
            _tray.Set(next, BuildTooltip(readings));
        }

        private static string BuildTooltip(List<BatteryReading> readings)
        {
            if (readings.Count == 0)
                return "鲸电量 · 未发现无线设备";
            var sb = new StringBuilder("鲸电量");
            foreach (BatteryReading r in readings)
            {
                if (sb.Length > 46) break;
                string shortName = r.Name.Length > 12 ? r.Name.Substring(0, 11) : r.Name;
                sb.Append(" · ").Append(shortName).Append(' ');
                if (r.Percent >= 0 && r.IsOnline)
                    sb.Append(r.Percent).Append('%');
                else
                    sb.Append(r.State == BatteryState.Offline ? "休眠" : "—");
            }
            string text = sb.ToString();
            return text.Length > 62 ? text.Substring(0, 62) : text;
        }

        private void NotifyLowBatteries(List<BatteryReading> readings)
        {
            foreach (BatteryReading r in readings)
            {
                if (r.Percent < 0 || !r.IsOnline)
                {
                    _lowNotified.Remove(r.Key);
                    continue;
                }
                bool low = r.Percent <= _settings.LowThreshold && r.State != BatteryState.Charging;
                if (!low)
                {
                    _lowNotified.Remove(r.Key);
                    continue;
                }
                if (_lowNotified.Add(r.Key))
                {
                    _tray.Balloon("电量偏低",
                        string.Format("{0} 只剩 {1}%，该充电了。", r.Name, r.Percent), true);
                }
            }
        }

        private void ShowPanel()
        {
            if (_flyout != null && !_flyout.IsDisposed)
            {
                _flyout.Close();
                _flyout = null;
                return;
            }
            var panel = new FlyoutForm(VisibleReadings());
            panel.AutostartOn = AppSettings.AutostartEnabled();
            panel.IntervalSeconds = _settings.IntervalSeconds;
            panel.RefreshClicked += delegate { _service.Refresh(); };
            panel.IntervalChanged += delegate
            {
                _settings.IntervalSeconds = panel.IntervalSeconds;
                _settings.Save();
                _service.SetInterval(panel.IntervalSeconds);
            };
            panel.AutostartToggled += delegate
            {
                AppSettings.SetAutostart(panel.AutostartOn);
                _settings.Autostart = panel.AutostartOn;
                DebugLog.Always("开机自启 = " + panel.AutostartOn);
            };
            panel.QuitClicked += delegate
            {
                DebugLog.Always("面板退出");
                _tray.Remove();
                ExitThread();
                Application.Exit();
                Environment.Exit(0);   // never leave a zombie holding the single-instance mutex
            };
            panel.FormClosed += delegate
            {
                _flyout = null;
                // Degraded (no tray icon): do not linger. A headless instance would keep the
                // single-instance mutex and make every later launch look like a no-op.
                if (!_tray.Added)
                {
                    ExitThread();
                    Environment.Exit(0);
                }
            };
            _flyout = panel;
            panel.ShowNearTray();
        }


        protected override void Dispose(bool disposing)
        {
            DebugLog.Always("TrayApp.Dispose disposing=" + disposing);
            if (disposing && !_disposed)
            {
                _disposed = true;
                _service.Dispose();
                if (_signalTimer != null)
                {
                    _signalTimer.Stop();
                    _signalTimer.Dispose();
                }
                if (_tray != null)
                    _tray.Dispose();
                if (_currentIcon != null)
                    _currentIcon.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
