**BattWhale 1.0.3** — tray battery monitor for 2.4 GHz wireless gear (Logitech / Razer / XInput).

Single 82 KB exe, .NET Framework 4.8 only. Read-only HID: no driver, no service, no network.

### What's new in 1.0.3

- **Sharp text on high-DPI screens** — the app now declares DPI awareness and draws at real
  pixel size instead of being bitmap-stretched by the compositor, so the panel stays crisp at
  125% / 150% scaling.
- **Tray icon generated at the display's DPI**, no longer a fixed 32 px bitmap blown up by the shell.
- **Tray icon reliability** — a low-integrity launch (terminal / IDE / sandbox) now always asks for
  elevation. Declining exits, instead of leaving a copy that holds the single-instance lock with no
  icon; that used to make every later launch show neither a UAC prompt nor a tray icon.
- The panel is positioned from real pixel sizes, so it no longer drifts off-screen at 150%.

### Devices

| Brand | How it is read | Notes |
|---|---|---|
| Logitech (G517 LS TKL, …) | HID++ 2.0, feature `0x1004` | receiver and wired; reports charging |
| Razer (Orochi V2, …) | Razer's RZCONTROL control device | no admin needed; level only |
| Xbox-mode controllers | `Windows.Gaming.Input` battery report | exact percentage and charging state; XInput buckets as fallback |

### Usage

Run `BattWhale.exe`, then click the tray icon for the panel: battery per device plus
Refresh / Interval / Autostart / Quit. The panel has an `EN / 中` language toggle.
`--once` prints a reading, `--diag` dumps HID details.

### Notes

- The exe is **unsigned**: SmartScreen may warn once, choose *More info → Run anyway*.
- Started from a terminal / IDE it gets a low-integrity token and Windows refuses the tray icon
  (`win32=5`); the app asks for elevation, or start it from File Explorer / `启动.cmd`.
- GameSir Supernova still reports a fake 100%: the vendor protocol's `battery` field reads 0.
  Details in `docs/gamesir-protocol.md`.

---

**鲸电量 1.0.3** —— 常驻托盘的 2.4G 无线外设电量监视器（罗技 / 雷蛇 / XInput）。

单文件 82 KB，只用系统自带的 .NET Framework 4.8。只读 HID：不装驱动、不常驻服务、不联网。

### 1.0.3 更新

- **高分屏文字不糊了**：声明 DPI 感知、按真实像素绘制，不再是 96 DPI 画完再被拉伸，125% / 150% 缩放下都清晰。
- **托盘图标按屏幕 DPI 生成**，不再固定 32 px 被系统放大。
- **托盘图标更可靠**：低完整性启动（终端 / IDE / 沙箱）一律请求提权，拒绝就退出，不再留下一个没有图标却占着单实例锁的进程——那会让之后每次启动既不弹 UAC 也没有图标。
- 面板定位改用真实像素，150% 下不会再偏出屏幕。

### 支持的设备

| 品牌 | 读取方式 | 备注 |
|---|---|---|
| 罗技（G517 LS TKL 等） | HID++ 2.0，feature `0x1004` | 接收器和有线都行，能报充电状态 |
| 雷蛇（八岐大蛇 V2 等） | 雷云自己的 RZCONTROL 控制设备 | 不需要管理员，只能读电量 |
| Xbox 模式手柄 | `Windows.Gaming.Input` 的电量报告 | 精确百分比 + 充电状态，XInput 四档兜底 |

### 用法

运行 `BattWhale.exe`，点托盘图标打开面板：每个设备的电量，底部是 刷新 / 间隔 / 自启 / 退出。
面板右上角可切 `EN / 中`。`--once` 打印一次读数，`--diag` 输出 HID 细节。

### 注意

- exe **没有签名**：SmartScreen 可能拦一次，点「更多信息 → 仍要运行」。
- 从终端 / IDE 启动会拿到低完整性令牌，Windows 拒绝注册托盘图标（`win32=5`）；程序会请求提权，或者直接从资源管理器 / `启动.cmd` 启动。
- 盖世小鸡超新星仍然报假的 100%：厂商协议的 `battery` 字段恒为 0。细节见 `docs/gamesir-protocol.md`。
