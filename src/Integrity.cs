using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace BattWhale
{
    /// <summary>
    /// DSH (and other sandboxes) start children with a low-integrity token. A low-integrity
    /// process can still open windows, but Shell_NotifyIcon is refused by UIPI with
    /// ERROR_ACCESS_DENIED, so the tray icon never appears. Explorer runs at medium
    /// integrity, so handing the exe back to it fixes the token.
    /// </summary>
    internal static class Integrity
    {
        public static string Level()
        {
            try
            {
                IntPtr token;
                if (!OpenProcessToken(GetCurrentProcess(), 0x0008, out token))
                    return "?";
                try
                {
                    int size;
                    GetTokenInformation(token, 25, IntPtr.Zero, 0, out size);
                    IntPtr buffer = Marshal.AllocHGlobal(size);
                    try
                    {
                        if (!GetTokenInformation(token, 25, buffer, size, out size))
                            return "?";
                        IntPtr sid = Marshal.ReadIntPtr(buffer);
                        byte count = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
                        int rid = Marshal.ReadInt32(GetSidSubAuthority(sid, (uint)(count - 1)));
                        switch (rid)
                        {
                            case 0x0000: return "untrusted";
                            case 0x1000: return "low";
                            case 0x2000: return "medium";
                            case 0x3000: return "high";
                            case 0x4000: return "system";
                            default: return "0x" + rid.ToString("X");
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(buffer);
                    }
                }
                finally
                {
                    CloseHandle(token);
                }
            }
            catch (Exception)
            {
                return "?";
            }
        }

        public static bool IsLow()
        {
            string level = Level();
            return level == "low" || level == "untrusted";
        }

        /// <summary>
        /// Do not try to hand the exe to Explorer as a way around elevation. Both variants
        /// were measured and both fail, because Windows will not let a low-integrity process
        /// produce a medium-integrity one:
        ///   * Process.Start("explorer.exe", exe) forks a second explorer.exe that inherits
        ///     our low token and dies with 0xc0000142 while initialising shell32;
        ///   * Shell.Application.ShellExecute does start a copy, but Explorer runs it under
        ///     the caller's low token, so Shell_NotifyIcon still fails with win32=5.
        /// Elevation is the only exit, and it needs the user's consent.
        /// </summary>
        public static bool RelaunchElevated(string exePath)
        {
            try
            {
                var info = new ProcessStartInfo(exePath);
                info.UseShellExecute = true;
                info.Verb = "runas";
                Process.Start(info);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Tells the running copy to show its panel.</summary>
        public static void SignalExistingInstance()
        {
            try
            {
                using (System.Threading.EventWaitHandle signal =
                       System.Threading.EventWaitHandle.OpenExisting("BattWhale.ShowPanel"))
                    signal.Set();
            }
            catch (Exception)
            {
            }
        }

        /// <summary>Waits until another copy owns the single-instance mutex.</summary>
        public static bool WaitForOtherInstance(int timeoutMs)
        {
            int waited = 0;
            while (waited < timeoutMs)
            {
                System.Threading.Thread.Sleep(100);
                waited += 100;
                try
                {
                    using (System.Threading.Mutex probe = System.Threading.Mutex.OpenExisting("BattWhale.SingleInstance"))
                    {
                        probe.Dispose();
                        return true;
                    }
                }
                catch (System.Threading.WaitHandleCannotBeOpenedException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                    return true;
                }
            }
            return false;
        }

        public static string DesktopName()
        {
            try
            {
                IntPtr desk = GetThreadDesktop(GetCurrentThreadId());
                var buffer = new StringBuilder(256);
                int needed;
                if (GetUserObjectInformation(desk, 2, buffer, 512, out needed))
                    return buffer.ToString();
            }
            catch (Exception)
            {
            }
            return "?";
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetThreadDesktop(uint threadId);

        [DllImport("user32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetUserObjectInformation(IntPtr handle, int index,
            StringBuilder buffer, int length, out int needed);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool GetTokenInformation(IntPtr token, int infoClass, IntPtr info, int length, out int needed);

        [DllImport("advapi32.dll")]
        private static extern IntPtr GetSidSubAuthority(IntPtr sid, uint index);

        [DllImport("advapi32.dll")]
        private static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
