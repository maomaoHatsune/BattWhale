# BattWhale

![build](https://github.com/maomaoHatsune/BattWhale/actions/workflows/build.yml/badge.svg)

A tiny always-on tray app that shows the battery level of your 2.4 GHz wireless gear on Windows —
Logitech (HID++), Razer (RZCONTROL) and XInput controllers.
Read-only HID: no driver, no background service, no network access.

![preview](docs/preview.png)

---

## English

### What it does

- One **82 KB** `BattWhale.exe`, uses the .NET Framework 4.8 that ships with Windows 10/11
- Tray icon coloured by the lowest battery: green ≥50%, yellow ≥20%, red <20%
- Click the icon → panel with per-device level, plus Refresh / Interval / Autostart / Quit
- `EN / 中` toggle in the panel, remembered across restarts
- DPI-aware: drawn at real pixel size, so text stays sharp at 125% / 150% scaling
- About 24 MB private memory; 0% CPU when idle

### Devices

| Brand | How it is read | Notes |
|---|---|---|
| Logitech (G517 LS TKL, …) | HID++ 2.0, feature `0x1004` | receiver and wired both work; reports charging |
| Razer (Orochi V2, …) | Razer's own RZCONTROL control device | no admin needed; level only |
| Xbox-mode controllers (GameSir, …) | `Windows.Gaming.Input` battery report | exact percentage and charging state; XInput's four buckets as the fallback |

### Run it

1. Download `BattWhale.exe` from [Releases](https://github.com/maomaoHatsune/BattWhale/releases).
2. Double-click it. SmartScreen may warn once because the exe is unsigned — choose
   **More info → Run anyway**.
3. The icon appears in the tray overflow (`^`); drag it onto the taskbar if you want it visible.

Command line: `--once` prints one reading, `--diag` dumps HID and protocol details,
`--hidscan <vid>` / `--hidread <vid>` for reverse engineering.

### Autostart

Turn on **Autostart** in the panel. It writes `HKCU\...\Run` with the exe's absolute path —
no script host, no admin rights, and SmartScreen is not involved at logon.

### Using other peripherals

The three readers were reverse-engineered from real devices, not copied from documentation, so a
new device will probably need its own pass:

1. `--hidscan` lists HID interfaces, `--hidread <vid>` dumps raw feature reports, `--diag` shows
   how the existing protocols answer your device;
2. hand that output to an AI agent and ask it to add a provider under `src/providers/`, modelled on
   `LogitechProvider` / `RazerProvider`;
3. build with `build.cmd`, verify with `--once`.

Keep it read-only: never write to the device.

### Build

`build.cmd` (or `powershell -ExecutionPolicy Bypass -File build.ps1`) uses the `csc.exe` bundled
with .NET Framework — no SDK required. CI builds the same way on every push and publishes a release
when a `v*` tag is pushed.

### SmartScreen on a downloaded exe

An exe downloaded from GitHub Releases with a browser gets a **Mark-of-the-Web**
(the NTFS `Zone.Identifier` stream), and SmartScreen stops it once on first launch. That has
nothing to do with signing — SmartScreen only looks at that mark. An exe built locally with
`build.cmd` carries no mark and is never stopped.

Three ways around it, pick one:

- `下载最新版.cmd` — fetches the latest release with `Invoke-WebRequest` and runs `Unblock-File`,
  so the file lands clean;
- for a file you already have: right-click → Properties → tick **Unblock**, or run
  `powershell -c "Unblock-File .\BattWhale.exe"`;
- `git pull` + `build.cmd` — a local build never has the mark.

### Troubleshooting

- **No tray icon** — you probably started it from a terminal or IDE that hands out a low-integrity
  token; Windows then refuses `Shell_NotifyIcon` (`win32=5`). This is a hard Windows limit: a
  low-integrity process **cannot** spawn a medium-integrity one (forking `explorer.exe` dies with
  `0xc0000142`, and `Shell.Application` still runs the copy under the caller's low token), so
  consenting to elevation is the only way out.
  - A low-integrity launch **always asks for elevation**; declining exits, so no copy is left
    holding the single-instance lock without an icon (that used to make every later launch show
    neither a UAC prompt nor a tray icon);
  - starting from a desktop shortcut, `启动.cmd` or autostart runs at medium integrity — the icon
    registers fine and no UAC appears.
- `bin\battwhale.log` records every start (integrity, elevation result, icon registration) —
  check it first.

### License

MIT.

---

## 中文

### 它是干什么的

- 一个 **82 KB** 的 `BattWhale.exe`，只用 Win10/11 自带的 .NET Framework 4.8
- 托盘图标按最低电量变色：绿 ≥50%、黄 ≥20%、红 <20%
- 点图标弹出面板：每个设备的电量，底部是 刷新 / 间隔 / 自启 / 退出
- 面板右上角 `EN / 中` 切换语言，选择会记住
- 声明 DPI 感知，按真实像素绘制，125% / 150% 缩放下文字不发糊
- 私有内存约 24 MB，空闲时 CPU 占用 0

### 支持的设备

| 品牌 | 读取方式 | 备注 |
|---|---|---|
| 罗技（G517 LS TKL 等） | HID++ 2.0，feature `0x1004` | 接收器和有线都行，能报充电状态 |
| 雷蛇（八岐大蛇 V2 等） | 雷云自己的 RZCONTROL 控制设备 | 不需要管理员，只能读电量 |
| Xbox 模式手柄（盖世小鸡等） | `Windows.Gaming.Input` 的电量报告 | 精确百分比 + 充电状态，XInput 的四个档位作兜底 |

### 怎么用

1. 去 [Releases](https://github.com/maomaoHatsune/BattWhale/releases) 下载 `BattWhale.exe`；
2. 双击运行。exe 没签名，SmartScreen 可能拦一次，点 **更多信息 → 仍要运行**；
3. 图标会出现在任务栏 `^` 溢出区里，想固定在任务栏上直接拖出来。

命令行：`--once` 打印一次电量，`--diag` 输出 HID 与协议原始回复，
`--hidscan <vid>` / `--hidread <vid>` 用于逆向新设备。

### 开机自启

在面板底部点开 **自启** 即可。写的是 `HKCU\...\Run` + exe 绝对路径，不经过脚本宿主，
不需要管理员，登录时也不会被 SmartScreen 拦。

### 想支持别的外设

三种读法都是拿真机报文逆出来的，不是照文档抄的，所以换个新设备大概率要重新走一遍：

1. `--hidscan` 列 HID 接口，`--hidread <vid>` 导出原始报文，`--diag` 看现有协议在你设备上的应答；
2. 把这些输出交给 AI agent，让它在 `src/providers/` 里照 `LogitechProvider` / `RazerProvider`
   的结构补一个 provider；
3. `build.cmd` 编译，`--once` 验证。

务必保持只读：绝不写设备。

### 编译

`build.cmd`（或 `powershell -ExecutionPolicy Bypass -File build.ps1`）用的是 .NET Framework
自带的 `csc.exe`，不用装 SDK。CI 用同样的方式构建，推 `v*` tag 时自动发 Release。

### 下载的 exe 会被 SmartScreen 拦

从 GitHub Releases 用浏览器下载的 `BattWhale.exe` 会被打上 **Mark-of-the-Web**
（NTFS 的 `Zone.Identifier` 数据流），第一次运行时 SmartScreen 就会拦一下，跟有没有签名
无关——它只看这个标记。自己 `build.cmd` 编译出来的 exe 不带标记，永远不会被拦。

三种解法，任选其一：

- `下载最新版.cmd`：走 `Invoke-WebRequest` 拉取并顺手 `Unblock-File`，落地即干净；
- 已经下好的文件，右键 → 属性 → 勾「解除锁定」，或执行
  `powershell -c "Unblock-File .\BattWhale.exe"`；
- 直接 `git pull` + `build.cmd`，本地构建的产物本来就没有标记。

### 出问题时

- **托盘里没有图标** —— 多半是从终端或 IDE 启动的，进程被降成低完整性，Windows 会拒绝
  `Shell_NotifyIcon`（`win32=5`）。这是 Windows 的硬限制：低完整性进程**无法**启动一个
  中完整性进程（fork `explorer.exe` 会崩 `0xc0000142`，`Shell.Application` 转交则仍用
  调用者的低完整性令牌），唯一出口是征得你同意的提权。
  - 低完整性启动时**一律请求提权**；拒绝就直接退出，不会留下一个没有图标却占着单实例锁的
    进程（那样会让之后每次启动都变成"既不弹 UAC 也没有图标"）；
  - 从桌面快捷方式、`启动.cmd` 或开机自启启动是普通权限，本来就能注册图标，不弹 UAC。
- `bin\battwhale.log` 会记录每次启动（完整性、提权结果、图标注册结果），排查先看它。

### 许可证

MIT。

---

## 项目结构

```
src/            C# 源码（logitech/ razer/ providers/ 为各协议实现）
build.ps1       编译脚本（只依赖系统自带 csc.exe）
docs/           截图；gamesir-protocol.md 是超新星厂商通道的逆向记录
tools/          fetch-latest.ps1（下载并清掉 MOTW）、gs/（GameSir 探测）、capstone 反汇编脚本
下载最新版.cmd   双击下载最新 Release，落地即无 SmartScreen 标记
```
