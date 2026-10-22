using System;

namespace BattWhale
{
    internal enum Lang
    {
        Zh = 0,
        En = 1
    }

    /// <summary>All user-visible text. The panel has a 中/EN toggle; the choice is stored in
    /// settings.ini so the next start comes up in the same language.</summary>
    internal static class Strings
    {
        public static Lang Current = Lang.Zh;

        public static bool IsEnglish
        {
            get { return Current == Lang.En; }
        }

        public static string Pick(string zh, string en)
        {
            return Current == Lang.En ? en : zh;
        }

        public static string FromCode(string code)
        {
            return string.Equals(code, "en", StringComparison.OrdinalIgnoreCase) ? "en" : "zh";
        }

        public static string Code
        {
            get { return IsEnglish ? "en" : "zh"; }
        }

        // ---- panel ----
        public static string AppTitle { get { return Pick("鲸电量", "BattWhale"); } }

        public static string DeviceCount(int count)
        {
            if (!IsEnglish)
                return count + " 个设备";
            return count == 1 ? "1 device" : count + " devices";
        }

        public static string NoDevices { get { return Pick("未发现设备", "No devices"); } }

        public static string NoDevicesHint
        {
            get
            {
                return Pick("插上 2.4G 接收器，或把无线设备唤醒后再试。",
                            "Plug in a 2.4G receiver, or wake the device and try again.");
            }
        }

        public static string Refresh { get { return Pick("刷新", "Refresh"); } }
        public static string Quit { get { return Pick("退出", "Quit"); } }
        public static string LangButton { get { return IsEnglish ? "中" : "EN"; } }

        public static string ShortInterval(int seconds)
        {
            return seconds < 60 ? seconds + "s" : (seconds / 60) + "m";
        }

        public static string IntervalLabel(int seconds)
        {
            return Pick("间隔 " + ShortInterval(seconds), "Every " + (seconds < 60 ? seconds + " sec" : (seconds / 60) + " min"));
        }

        public static string AutostartLabel(bool on)
        {
            if (!IsEnglish)
                return on ? "自启 开" : "自启 关";
            return on ? "Autostart on" : "Autostart off";
        }

        public static string Footer(string time)
        {
            return time + Pick(" 刷新 · 左键再次点击收起", "  updated · click the icon again to hide");
        }

        // ---- device state ----
        public static string Charging { get { return Pick("充电中", "Charging"); } }
        public static string Full { get { return Pick("已充满", "Full"); } }
        public static string InUse { get { return Pick("使用中", "In use"); } }
        public static string Asleep { get { return Pick("休眠中", "Asleep"); } }
        public static string Unreadable { get { return Pick("无法读取", "Unreadable"); } }
        public static string Unknown { get { return Pick("未知", "Unknown"); } }

        // ---- tray ----
        public static string TooltipNoDevices
        {
            get { return Pick("鲸电量 · 未发现无线设备", "BattWhale · no wireless devices"); }
        }

        public static string AsleepShort { get { return Pick("休眠", "asleep"); } }

        public static string LowBatteryTitle { get { return Pick("电量偏低", "Battery low"); } }

        public static string LowBatteryText(string name, int percent)
        {
            return IsEnglish
                ? string.Format("{0} is down to {1}%. Time to charge.", name, percent)
                : string.Format("{0} 只剩 {1}%，该充电了。", name, percent);
        }

        public static string FirstRunTitle { get { return Pick("鲸电量已启动", "BattWhale is running"); } }

        public static string FirstRunText
        {
            get
            {
                return Pick("图标在任务栏右下角的 ^ 里，点一下就能看到面板，操作都在面板底部。",
                            "The icon sits in the taskbar overflow (^). Click it to open the panel.");
            }
        }
    }
}
