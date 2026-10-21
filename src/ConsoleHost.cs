using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace BattWhale
{
    /// <summary>Lets a /target:winexe build still print to the console it was started from.</summary>
    internal static class ConsoleHost
    {
        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ = 1;
        private const uint FILE_SHARE_WRITE = 2;
        private const uint OPEN_EXISTING = 3;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AllocConsole();

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFileW(string name, uint access, uint share, IntPtr security,
            uint creation, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(int which);

        public static bool Attach()
        {
            try
            {
                // launched from a shell with redirected output: the inherited handle works
                IntPtr existing = GetStdHandle(-11);
                if (existing != IntPtr.Zero && existing != new IntPtr(-1))
                    return true;

                if (!AttachConsole(-1))
                    return false;
                IntPtr stdout = CreateFileW("CONOUT$", GENERIC_READ | GENERIC_WRITE,
                    FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                if (stdout == IntPtr.Zero || stdout == new IntPtr(-1))
                    return false;
                var stream = new FileStream(new Microsoft.Win32.SafeHandles.SafeFileHandle(stdout, false),
                    FileAccess.Write);
                var writer = new StreamWriter(stream, new UTF8Encoding(false));
                writer.AutoFlush = true;
                Console.SetOut(writer);
                try { Console.OutputEncoding = Encoding.UTF8; } catch (Exception) { }
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}

