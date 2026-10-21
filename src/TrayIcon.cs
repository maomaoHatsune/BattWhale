using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BattWhale
{
    /// <summary>
    /// Own tray icon instead of WinForms' NotifyIcon: the shell result is checked, the
    /// icon is identified by GUID (so placement survives restarts) and a self-test can
    /// ask the shell where the icon actually ended up.
    /// </summary>
    internal sealed class TrayIcon : IDisposable
    {
        private const int WM_APP = 0x8000;
        private const int WM_TRAYICON = WM_APP + 1;

        private const uint NIF_MESSAGE = 0x01;
        private const uint NIF_ICON = 0x02;
        private const uint NIF_TIP = 0x04;
        private const uint NIF_STATE = 0x08;
        private const uint NIF_GUID = 0x20;
        private const uint NIF_SHOWTIP = 0x80;
        private const uint NIF_INFO = 0x10;
        private const uint NIM_ADD = 0x00;
        private const uint NIM_MODIFY = 0x01;
        private const uint NIM_DELETE = 0x02;
        private const uint NIM_SETVERSION = 0x04;
        private const uint NOTIFYICON_VERSION_4 = 4;

        private static readonly Guid IconId = new Guid("6b1f3c2a-8d1e-4a6b-9f3c-2d7a5e4c1b90");

        private readonly MessageWindow _window;
        private readonly uint _taskbarCreated;
        private Icon _icon;
        private string _tip = "鲸电量";
        private bool _added;

        public bool Added { get { return _added; } }
        public string LastError = "";
        public event EventHandler LeftClick;
        public event EventHandler RightClick;

        /// <summary>UI-thread anchor for marshaling work from background threads.</summary>
        public Control UiAnchor { get { return _window; } }

        public bool Balloon(string title, string text, bool warning)
        {
            if (!_added)
                return false;
            NOTIFYICONDATA data = BuildData();
            data.uFlags = NIF_INFO | NIF_GUID;
            data.szInfo = text.Length > 250 ? text.Substring(0, 250) : text;
            data.szInfoTitle = title.Length > 60 ? title.Substring(0, 60) : title;
            data.dwInfoFlags = warning ? 0x02u : 0x01u;   // NIIF_WARNING / NIIF_INFO
            return Shell_NotifyIcon(NIM_MODIFY, ref data);
        }

        public TrayIcon()
        {
            _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
            _window = new MessageWindow(this);
        }

        public bool Set(Icon icon, string tip)
        {
            if (icon != null)
                _icon = icon;
            if (!string.IsNullOrEmpty(tip))
                _tip = tip.Length > 120 ? tip.Substring(0, 120) : tip;
            return Update();
        }

        private NOTIFYICONDATA BuildData()
        {
            var data = new NOTIFYICONDATA();
            data.cbSize = Marshal.SizeOf(typeof(NOTIFYICONDATA));
            data.hWnd = _window.Handle;
            data.uID = 1;
            data.guidItem = IconId;
            data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_GUID | NIF_SHOWTIP;
            data.uCallbackMessage = WM_TRAYICON;
            data.hIcon = _icon != null ? _icon.Handle : IntPtr.Zero;
            data.szTip = _tip;
            data.uVersion = NOTIFYICON_VERSION_4;
            return data;
        }

        private bool Update()
        {
            if (_icon == null || _icon.Handle == IntPtr.Zero)
            {
                LastError = "no icon handle";
                return false;
            }
            NOTIFYICONDATA data = BuildData();
            if (_added)
            {
                if (Shell_NotifyIcon(NIM_MODIFY, ref data))
                    return true;
                // the shell may have restarted and forgotten us: fall through to a fresh add
                _added = false;
            }
            _added = Shell_NotifyIcon(NIM_ADD, ref data);
            if (_added)
            {
                NOTIFYICONDATA version = BuildData();
                Shell_NotifyIcon(NIM_SETVERSION, ref version);
            }
            else
            {
                LastError = "Shell_NotifyIcon(NIM_ADD) failed, win32=" + Marshal.GetLastWin32Error();
            }
            return _added;
        }

        /// <summary>Asks the shell where our icon is; S_OK means it is really in the tray.</summary>
        public string Probe()
        {
            var id = new NOTIFYICONIDENTIFIER();
            id.cbSize = Marshal.SizeOf(typeof(NOTIFYICONIDENTIFIER));
            id.guidItem = IconId;
            RECT rect;
            int hr = Shell_NotifyIconGetRect(ref id, out rect);
            if (hr == 0)
                return string.Format("ok rect=({0},{1},{2},{3})", rect.Left, rect.Top, rect.Right, rect.Bottom);
            return string.Format("hr=0x{0:X8}", hr);
        }

        public void Remove()
        {
            if (!_added)
                return;
            NOTIFYICONDATA data = BuildData();
            Shell_NotifyIcon(NIM_DELETE, ref data);
            _added = false;
        }

        public void Dispose()
        {
            Remove();
            if (_window != null)
                _window.Close();
        }

        private void OnShellMessage(ref Message m)
        {
            if (m.Msg == WM_TRAYICON)
            {
                // NOTIFYICON_VERSION_4: lParam = MAKELONG(event, iconId), wParam carries
                // the cursor position. Read the event from lParam - the earlier attempt
                // to swap this read the x coordinate and made both buttons dead.
                int packed = m.LParam.ToInt32();
                int evt = packed & 0xFFFF;
                int iconId = (packed >> 16) & 0xFFFF;

                bool left = evt == 0x0202 || evt == 0x0400 || evt == 0x0401;
                bool right = evt == 0x0205 || evt == 0x007B;
                if (!left && !right)
                {
                    // mouse move / hover notifications: ignore (and do not log, they flood)
                    return;
                }
                if (!Debounce(left))
                    return;

                DebugLog.Always(string.Format("托盘点击 {0} w=0x{1:X8} l=0x{2:X8} id={3}",
                    left ? "左键" : "右键", m.WParam.ToInt32(), packed, iconId));
                EventHandler handler = left ? LeftClick : RightClick;
                if (handler != null)
                    handler(this, EventArgs.Empty);
            }
            else if (_taskbarCreated != 0 && (uint)m.Msg == _taskbarCreated)
            {
                // Explorer restarted: our registration is gone
                _added = false;
                Update();
            }
        }

        private DateTime _lastLeft;
        private DateTime _lastRight;

        /// <summary>A single click produces several notifications (down, up, NIN_SELECT);
        /// only the first within a short window counts.</summary>
        private bool Debounce(bool left)
        {
            DateTime now = DateTime.UtcNow;
            if (left)
            {
                if ((now - _lastLeft).TotalMilliseconds < 200)
                    return false;
                _lastLeft = now;
            }
            else
            {
                if ((now - _lastRight).TotalMilliseconds < 200)
                    return false;
                _lastRight = now;
            }
            return true;
        }

        private sealed class MessageWindow : Form
        {
            private readonly TrayIcon _owner;

            public MessageWindow(TrayIcon owner)
            {
                _owner = owner;
                ShowInTaskbar = false;
                FormBorderStyle = FormBorderStyle.None;
                StartPosition = FormStartPosition.Manual;
                SetBounds(-4000, -4000, 1, 1);
                IntPtr unused = Handle;   // force window creation without showing anything
            }

            protected override void WndProc(ref Message m)
            {
                if (_owner != null)
                    _owner.OnShellMessage(ref m);
                base.WndProc(ref m);
            }

            protected override CreateParams CreateParams
            {
                get
                {
                    CreateParams cp = base.CreateParams;
                    cp.ExStyle |= 0x80;   // WS_EX_TOOLWINDOW: never in the taskbar
                    return cp;
                }
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NOTIFYICONDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public uint uID;
            public uint uFlags;
            public uint uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;
            public uint dwState;
            public uint dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szInfo;
            public uint uVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string szInfoTitle;
            public uint dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NOTIFYICONIDENTIFIER
        {
            public int cbSize;
            public IntPtr hWnd;
            public uint uID;
            public Guid guidItem;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool Shell_NotifyIcon(uint message, ref NOTIFYICONDATA data);

        [DllImport("shell32.dll")]
        private static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER identifier, out RECT iconLocation);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern uint RegisterWindowMessage(string name);
    }
}
