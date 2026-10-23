using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace BattWhale
{
    internal static class Program
    {
        /// <summary>Shown by --help; keep in step with the git tag.</summary>
        public const string Version = "1.0.3";

        [STAThread]
        private static int Main(string[] args)
        {
            // Without this, Windows treats us as a 96 dpi app and bitmap-stretches the whole
            // window to the real scale. That is what makes the panel text look soft on a
            // 125%/150% display: GDI+ draws 14 px glyphs, then the compositor blows them up.
            // Declaring awareness hands us real pixels and lets ClearType run at native size.
            try { SetProcessDPIAware(); }
            catch (Exception) { }

            string command = args.Length > 0 ? args[0].ToLowerInvariant() : "";

            // A sandboxed launch (chat file card, IDE, some terminals) hands us a low-integrity
            // token, and UIPI then refuses Shell_NotifyIcon with win32=5 - the app runs but has
            // no tray icon. Windows will not let a low-integrity process spawn a
            // medium-integrity one, so consenting to elevation is the only way out.
            //
            // Always ask. An earlier revision quietly fell back to panel mode here, and that
            // copy kept holding the single-instance mutex while showing no icon, so every
            // later launch was handed to it and never got a tray icon either. Leaving is
            // better than lingering without an icon.
            if (args.Length == 0 && Integrity.IsLow())
            {
                DebugLog.Always("低完整性进程 (" + Integrity.Level() + ")，请求提权以保证托盘图标");

                if (Integrity.RelaunchElevated(Application.ExecutablePath) &&
                    Integrity.WaitForOtherInstance(20000))
                {
                    DebugLog.Always("提权拉起成功，本进程退出");
                    return 0;
                }

                DebugLog.Always("提权被拒或失败，退出（没有托盘图标就不留着占锁）");
                return 0;
            }

            try
            {
                if (command == "--once")
                {
                    ConsoleHost.Attach();
                    return ConsoleReport.Run(false);
                }
                if (command == "--diag")
                {
                    ConsoleHost.Attach();
                    return ConsoleReport.Run(true);
                }
                if (command == "--hidscan" && args.Length > 1)
                {
                    ConsoleHost.Attach();
                    return ConsoleReport.ScanFeatureReports(Convert.ToUInt16(args[1], 16));
                }
                if (command == "--hidread" && args.Length > 1)
                {
                    ConsoleHost.Attach();
                    int seconds = args.Length > 2 ? int.Parse(args[2]) : 6;
                    return ConsoleReport.DumpInput(Convert.ToUInt16(args[1], 16), seconds);
                }
                if (command == "--razertest")
                {
                    ConsoleHost.Attach();
                    return ConsoleReport.RazerTest();
                }
                if (command == "--flyout")
                    return PreviewFlyout(args);
                if (command == "--icontest")
                {
                    ConsoleHost.Attach();
                    return IconTest();
                }
                if (command == "--traytest")
                {
                    ConsoleHost.Attach();
                    return TrayTest();
                }
                if (command == "--help" || command == "-h")
                {
                    ConsoleHost.Attach();
                    Console.WriteLine("BattWhale " + Version);
                    Console.WriteLine("  (no args)    stay in the tray / 常驻托盘");
                    Console.WriteLine("  --once       print battery readings once / 打印一次电量后退出");
                    Console.WriteLine("  --diag       HID interfaces + raw protocol replies / 诊断");
                    Console.WriteLine("  --hidscan xx scan feature reports of that VID / 扫描 feature report");
                    Console.WriteLine("  --hidread xx dump input reports of that VID / 监听输入报文");
                    return 0;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("error: " + ex);
                return 2;
            }

            bool created;
            using (var mutex = new Mutex(true, "BattWhale.SingleInstance", out created))
            {
                if (!created)
                {
                    // Second launch: tell the running copy to pop its panel instead of
                    // nagging with a message box the user cannot act on.
                    Integrity.SignalExistingInstance();
                    return 0;
                }
                bool signalCreated;
                using (var signal = new EventWaitHandle(false, EventResetMode.AutoReset,
                           "BattWhale.ShowPanel", out signalCreated))
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                    Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
                    {
                        LogFatal(e.Exception);
                    };
                    AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
                    {
                        LogFatal(e.ExceptionObject as Exception);
                    };
                    AppDomain.CurrentDomain.ProcessExit += delegate
                    {
                        DebugLog.Always("ProcessExit");
                    };
                    try
                    {
                        using (var app = new TrayApp(signal))
                        {
                            Application.Run(app);
                            DebugLog.Always("Application.Run 返回");
                        }
                    }
                    catch (Exception ex)
                    {
                        LogFatal(ex);
                        return 3;
                    }
                }
            }
            return 0;
        }

        /// <summary>Dev helper: shows the popup with sample rows so it can be eyeballed.</summary>
        private static int PreviewFlyout(string[] args)
        {
            var readings = new List<BatteryReading>();

            var keyboard = new BatteryReading();
            keyboard.Key = "preview:1";
            keyboard.Name = "G517 LS TKL";
            keyboard.Kind = DeviceKind.Keyboard;
            keyboard.Percent = 99;
            keyboard.State = BatteryState.Discharging;
            readings.Add(keyboard);

            var mouse = new BatteryReading();
            mouse.Key = "preview:2";
            mouse.Name = "Razer Orochi V2";
            mouse.Kind = DeviceKind.Mouse;
            mouse.Percent = 42;
            mouse.State = BatteryState.Charging;
            readings.Add(mouse);

            var pad = new BatteryReading();
            pad.Key = "preview:3";
            pad.Name = "GameSir 手柄";
            pad.Kind = DeviceKind.Controller;
            pad.Percent = 17;
            pad.State = BatteryState.Discharging;
            readings.Add(pad);

            Application.EnableVisualStyles();
            Strings.Current = AppSettings.Load().Language == "en" ? Lang.En : Lang.Zh;
            var form = new FlyoutForm(readings);
            form.AutostartOn = AppSettings.AutostartEnabled();
            form.IntervalSeconds = AppSettings.Load().IntervalSeconds;
            form.Sticky = Array.IndexOf(args, "--stay") >= 0;
            form.ShowNearTray();
            DebugLog.Write("flyout: bounds=" + form.Bounds + " visible=" + form.Visible +
                           " handle=" + form.Handle + " screen=" + Screen.PrimaryScreen.WorkingArea);
            var timer = new System.Windows.Forms.Timer();
            timer.Interval = form.Sticky ? 60000 : 4000;
            timer.Tick += delegate { timer.Stop(); Application.Exit(); };
            timer.Start();
            Application.Run();
            return 0;
        }

        /// <summary>Adds a stock tray icon, asks the shell where it is, then shows the result.</summary>
        private static int TrayTest()
        {
            Application.EnableVisualStyles();
            string report;
            using (var tray = new TrayIcon())
            {
                bool added = tray.Set(SystemIcons.Application, "BattWhale 托盘测试");
                string probe;
                var timer = new System.Windows.Forms.Timer();
                timer.Interval = 1200;
                timer.Tick += delegate
                {
                    timer.Stop();
                    Application.Exit();
                };
                timer.Start();
                Application.Run();
                probe = tray.Probe();
                report = string.Format(
                    "session   : {0}\r\ndesktop   : {1}\r\nintegrity : {2}\r\nNIM_ADD   : {3} {4}\r\nprobe     : {5}",
                    System.Diagnostics.Process.GetCurrentProcess().SessionId,
                    Integrity.DesktopName(), Integrity.Level(),
                    added, tray.LastError, probe);
                if (!added && Integrity.IsLow())
                    report += "\r\n\r\n原因：当前进程是「低完整性」（从聊天窗口 / 沙箱里打开的），" +
                              "\r\n低完整性进程不允许往任务栏注册图标。" +
                              "\r\n请到文件资源管理器里直接双击 exe（脱离沙箱）。";
                DebugLog.Write("traytest: " + report.Replace("\r\n", " | "));
                WriteTestLog(report);
                MessageBox.Show(report, "鲸电量 · 托盘自检",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            return 0;
        }

        private static void WriteTestLog(string text)
        {
            try
            {
                string path = System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(Application.ExecutablePath), "traytest.log");
                System.IO.File.WriteAllText(path, text, System.Text.Encoding.UTF8);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>Checks that the tray icon handle survives after the bitmap is gone.</summary>
        private static int IconTest()
        {
            var empty = new List<BatteryReading>();
            using (Icon good = IconFactory.BuildTrayIcon(empty, false))
                Console.WriteLine("new ICO path : " + (IcoBuilder.IsValid(good) ? "valid" : "INVALID"));

            using (var bmp = new System.Drawing.Bitmap(32, 32))
            {
                IntPtr h = bmp.GetHicon();
                Icon clone;
                using (Icon temp = Icon.FromHandle(h))
                    clone = (Icon)temp.Clone();
                DestroyIcon(h);
                Console.WriteLine("old clone path: " + (IcoBuilder.IsValid(clone) ? "valid" : "INVALID"));
                clone.Dispose();
            }
            return 0;
        }


        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        private static void LogFatal(Exception ex)
        {
            if (ex == null)
                return;
            try
            {
                string path = System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(Application.ExecutablePath), "crash.log");
                System.IO.File.AppendAllText(path,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine + ex + Environment.NewLine);
            }
            catch (Exception)
            {
            }
        }
    }
}
