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

            using (var bmp = new Bitmap(32, 32, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    DrawBattery(g, new Rectangle(4, 9, 22, 14), level, charging, lightBackground);
                }
                return ToIcon(bmp);
            }
        }

        public static Icon BuildDeviceIcon(DeviceKind kind, int percent, BatteryState state, bool lightBackground)
        {
            using (var bmp = new Bitmap(16, 16, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    DrawBattery(g, new Rectangle(1, 4, 12, 8), percent, state == BatteryState.Charging, lightBackground);
                }
                return ToIcon(bmp);
            }
        }

        public static void DrawBattery(Graphics g, Rectangle body, int percent, bool charging, bool lightBackground)
        {
            Color color = LevelColor(percent);
            Color outlineColor = lightBackground ? Color.FromArgb(48, 52, 58) : Color.FromArgb(245, 245, 245);
            int tipWidth = Math.Max(2, body.Width / 7);
            var tip = new Rectangle(body.Right, body.Top + body.Height / 3, tipWidth, body.Height / 3);
            using (var outline = new Pen(outlineColor, 1.6f))
            using (var fill = new SolidBrush(color))
            using (var tipBrush = new SolidBrush(outlineColor))
            {
                var inner = new Rectangle(body.X + 2, body.Y + 2, body.Width - 4, body.Height - 4);
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
                        g.FillEllipse(dot, inner.X + inner.Width / 2 - 2, inner.Y + inner.Height / 2 - 2, 4, 4);
                }
            }
        }

        private static Icon ToIcon(Bitmap bmp)
        {
            return IcoBuilder.FromBitmap(bmp);
        }
    }
}
