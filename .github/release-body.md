**BattWhale 1.0.1** — tray battery monitor for 2.4 GHz wireless gear (Logitech / Razer / XInput).

Single 78 KB exe, .NET Framework 4.8 only. Read-only HID: no driver, no service, no network.

### Devices

| Brand | How it is read | Notes |
|---|---|---|
| Logitech (G517 LS TKL, …) | HID++ 2.0, feature `0x1004` | receiver and wired; reports charging |
| Razer (Orochi V2, …) | Razer's RZCONTROL control device | no admin needed; level only |
| Xbox-mode controllers | `XInputGetBatteryInformation` | coarse buckets, not a percentage |

### Usage

Run `BattWhale.exe`, then click the tray icon for the panel: battery per device plus
Refresh / Interval / Autostart / Quit. The panel has an `EN / 中` language toggle.
`--once` prints a reading, `--diag` dumps HID details.

### Notes

- The exe is **unsigned**: SmartScreen may warn once, choose *More info → Run anyway*.
- Started from a terminal/IDE it gets a low-integrity token and Windows refuses the tray icon
  (`win32=5`); the app relaunches itself with normal rights, or start it from File Explorer.

---

**鲸电量 1.0.1** —— 常驻托盘的 2.4G 无线外设电量监视器（罗技 / 雷蛇 / XInput）。

单文件 78 KB，只用系统自带的 .NET Framework 4.8。只读 HID：不装驱动、不常驻服务、不联网。

### 支持的设备

| 品牌 | 读取方式 | 备注 |
|---|---|---|
| 罗技（G517 LS TKL 等） | HID++ 2.0，feature `0x1004` | 接收器和有线都行，带充电状态 |
| 雷蛇（八岐大蛇 V2 等） | 雷云自己的 RZCONTROL 控制设备 | 不需要管理员，只读电量 |
| Xbox 模式手柄 | `XInputGetBatteryInformation` | 只有档位，不精确到百分比 |

### 用法

运行 `BattWhale.exe`，点托盘图标弹出面板：每个设备的电量 + 刷新 / 间隔 / 自启 / 退出，
右上角 `EN / 中` 切语言。`--once` 打印一次，`--diag` 输出协议细节。

### 注意

- exe **未签名**，SmartScreen 可能拦一次，点「更多信息 → 仍要运行」。
- 从终端 / IDE 启动会拿到低完整性令牌，Windows 会拒绝注册托盘图标（`win32=5`）；
  程序会自己换正常权限重新拉起，或者直接在资源管理器里双击。
