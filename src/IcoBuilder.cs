using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace BattWhale
{
    /// <summary>
    /// Builds a real .ico from a bitmap. Icons made with Bitmap.GetHicon() share a handle
    /// with the clone, so destroying the handle leaves a dangling HICON that the shell
    /// refuses to put in the tray.
    /// </summary>
    internal static class IcoBuilder
    {
        public static Icon FromBitmap(Bitmap bmp)
        {
            int width = bmp.Width;
            int height = bmp.Height;

            var xor = new byte[width * height * 4];
            BitmapData data = bmp.LockBits(new Rectangle(0, 0, width, height),
                ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var row = new byte[width * 4];
                for (int y = 0; y < height; y++)
                {
                    Marshal.Copy(new IntPtr(data.Scan0.ToInt64() + y * data.Stride), row, 0, row.Length);
                    // DIB rows are stored bottom-up
                    Array.Copy(row, 0, xor, (height - 1 - y) * width * 4, row.Length);
                }
            }
            finally
            {
                bmp.UnlockBits(data);
            }

            int maskStride = ((width + 31) / 32) * 4;
            var and = new byte[maskStride * height];  // zeroed: alpha channel decides transparency

            var stream = new MemoryStream();
            var writer = new BinaryWriter(stream);
            writer.Write((ushort)0);   // reserved
            writer.Write((ushort)1);   // type: icon
            writer.Write((ushort)1);   // one image
            writer.Write((byte)(width >= 256 ? 0 : width));
            writer.Write((byte)(height >= 256 ? 0 : height));
            writer.Write((byte)0);     // palette size
            writer.Write((byte)0);     // reserved
            writer.Write((ushort)1);   // planes
            writer.Write((ushort)32);  // bpp
            writer.Write(40 + xor.Length + and.Length);
            writer.Write(22);          // offset of the image

            writer.Write(40);          // BITMAPINFOHEADER size
            writer.Write(width);
            writer.Write(height * 2);  // XOR + AND masks
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(0);           // BI_RGB
            writer.Write(xor.Length + and.Length);
            writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0);
            writer.Write(xor);
            writer.Write(and);
            writer.Flush();
            stream.Position = 0;
            return new Icon(stream, new Size(width, height));
        }

        /// <summary>Sanity check used by --icontest: is this HICON something GDI still knows?</summary>
        public static bool IsValid(Icon icon)
        {
            if (icon == null || icon.Handle == IntPtr.Zero)
                return false;
            ICONINFO info;
            return GetIconInfo(icon.Handle, out info);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ICONINFO
        {
            public bool fIcon;
            public int xHotspot;
            public int yHotspot;
            public IntPtr hbmMask;
            public IntPtr hbmColor;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO info);
    }
}
