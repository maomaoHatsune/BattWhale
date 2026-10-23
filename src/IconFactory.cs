using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace BattWhale
{
    internal static class IconFactory
    {
        public static Color LevelColor(int percent)
        {
            return Palette.LevelColor(percent);
        }

        /// <summary>Tray icon showing the lowest battery among known devices.</summary>
        public static Icon BuildTrayIcon(List<BatteryReading> readings, bool lightBackground)
        {
            int level = -1;
            bool charging = false;
            foreach (BatteryReading r in readings)
            {
                if (!r.IsOnline || r.Percent < 0) continue;
                if (level < 0 || r.Percent < level)
                    level = r.Percent;
                if (r.State == BatteryState.Charging)
                    charging = true;
            }

            // Draw at the real pixel size, not 32x32 stretched by the shell: a 150% display
            // asks the tray for a 48 px icon, and handing it a 32 px bitmap blurs it.
            float scale = DpiScale();
            using (var bmp = new Bitmap(S(32, scale), S(32, scale), PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    DrawBattery(g, new Rectangle(S(4, scale), S(9, scale), S(22, scale), S(14, scale)),
                                level, charging, lightBackground, scale);
                }
                return ToIcon(bmp);
            }
        }

        public static Icon BuildDeviceIcon(DeviceKind kind, int percent, BatteryState state, bool lightBackground)
        {
            float scale = DpiScale();
            using (var bmp = new Bitmap(S(16, scale), S(16, scale), PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    DrawBattery(g, new Rectangle(S(1, scale), S(4, scale), S(12, scale), S(8, scale)),
                                percent, state == BatteryState.Charging, lightBackground, scale);
                }
                return ToIcon(bmp);
            }
        }

        private static float DpiScale()
        {
            try
            {
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                    return g.DpiX / 96f;
            }
            catch (Exception)
            {
                return 1f;
            }
        }

        private static int S(int logical, float scale)
        {
            return Math.Max(1, (int)Math.Round(logical * scale));
        }

        public static void DrawBattery(Graphics g, Rectangle body, int percent, bool charging,
                                       bool lightBackground, float scale)
        {
            Color color = LevelColor(percent);
            Color outlineColor = lightBackground ? Color.FromArgb(48, 52, 58) : Color.FromArgb(245, 245, 245);
            int tipWidth = Math.Max(S(2, scale), body.Width / 7);
            var tip = new Rectangle(body.Right, body.Top + body.Height / 3, tipWidth, body.Height / 3);
            using (var outline = new Pen(outlineColor, 1.6f * scale))
            using (var fill = new SolidBrush(color))
            using (var tipBrush = new SolidBrush(outlineColor))
            {
                var inner = new Rectangle(body.X + S(2, scale), body.Y + S(2, scale),
                                          body.Width - S(4, scale), body.Height - S(4, scale));
                g.FillRectangle(tipBrush, tip);
                if (percent > 0)
                {
                    int width = (int)Math.Round(inner.Width * Math.Min(100, percent) / 100.0);
                    g.FillRectangle(fill, new Rectangle(inner.X, inner.Y, Math.Max(1, width), inner.Height));
                }
                g.DrawRectangle(outline, body);
                if (percent < 0)
                {
                    using (var dot = new SolidBrush(lightBackground
                        ? Color.FromArgb(110, 116, 124) : Color.FromArgb(200, 205, 210)))
                        g.FillEllipse(dot,
                            inner.X + inner.Width / 2 - S(2, scale),
                            inner.Y + inner.Height / 2 - S(2, scale),
                            S(4, scale), S(4, scale));
                }
            }
        }

        private static Icon ToIcon(Bitmap bmp)
        {
            return IcoBuilder.FromBitmap(bmp);
        }
    }
}
