using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace BattWhale
{
    /// <summary>Light popup panel: device rows plus the actions that used to live in a
    /// separate context menu (refresh / poll interval / autostart / quit).</summary>
    internal sealed class FlyoutForm : Form
    {
        private const int RowHeight = 54;
        private const int HeaderHeight = 40;
        private const int FooterHeight = 26;
        private const int ActionHeight = 40;
        private const int Width_ = 316;

        private readonly List<BatteryReading> _readings;
        private DateTime _shownAt;
        private readonly Rectangle _refreshButton;
        private readonly Rectangle _intervalButton;
        private readonly Rectangle _autostartButton;
        private readonly Rectangle _quitButton;

        public bool Sticky;
        public bool AutostartOn;
        public int IntervalSeconds = 60;

        public event EventHandler RefreshClicked;
        public event EventHandler QuitClicked;
        public event EventHandler AutostartToggled;
        public event EventHandler IntervalChanged;

        public FlyoutForm(List<BatteryReading> readings)
        {
            _readings = readings;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;
            BackColor = Palette.Panel;
            int rows = Math.Max(1, Math.Min(readings.Count, 6));
            ClientSize = new Size(Width_, HeaderHeight + rows * RowHeight + FooterHeight + ActionHeight);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

            int y = Height - ActionHeight + 7;
            _refreshButton = new Rectangle(12, y, 66, 26);
            _intervalButton = new Rectangle(84, y, 86, 26);
            _autostartButton = new Rectangle(176, y, 76, 26);
            _quitButton = new Rectangle(258, y, 46, 26);
        }

        public void ShowNearTray()
        {
            Rectangle work = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(work.Right - Width_ - 12, work.Bottom - Height - 12);
            _shownAt = DateTime.Now;
            Show();
            Activate();
            BringToFront();
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (Sticky)
                return;
            // A click on the tray icon can hand us focus a beat after Show(); ignore that
            // first blip or the panel would close itself immediately.
            if ((DateTime.Now - _shownAt).TotalMilliseconds < 350)
                return;
            Close();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            using (var background = new SolidBrush(Palette.Panel))
                g.FillRectangle(background, ClientRectangle);
            using (var border = new Pen(Palette.Border, 1))
                g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);

            using (var titleFont = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold))
            using (var smallFont = new Font("Microsoft YaHei UI", 8.5f))
            using (var nameFont = new Font("Microsoft YaHei UI", 9.5f))
            using (var boldFont = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold))
            using (var text = new SolidBrush(Palette.Text))
            using (var dim = new SolidBrush(Palette.Secondary))
            {
                g.DrawString("鲸电量", titleFont, text, 14, 12);
                string summary = _readings.Count == 0 ? "未发现设备" : _readings.Count + " 个设备";
                SizeF summarySize = g.MeasureString(summary, smallFont);
                g.DrawString(summary, smallFont, dim, Width - summarySize.Width - 14, 16);

                if (_readings.Count == 0)
                    g.DrawString("插上 2.4G 接收器，或把无线设备唤醒后再试。", smallFont, dim, 14, HeaderHeight + 8);

                int y = HeaderHeight;
                foreach (BatteryReading r in _readings)
                {
                    DrawRow(g, r, y, nameFont, boldFont, smallFont, text, dim);
                    y += RowHeight;
                }

                string footer = DateTime.Now.ToString("HH:mm:ss") + " 刷新 · 左键再次点击收起";
                g.DrawString(footer, smallFont, dim, 14, Height - ActionHeight - FooterHeight + 4);

                DrawButton(g, _refreshButton, "刷新", false, smallFont);
                DrawButton(g, _intervalButton, "间隔 " + FormatInterval(IntervalSeconds), false, smallFont);
                DrawButton(g, _autostartButton, AutostartOn ? "自启 开" : "自启 关", AutostartOn, smallFont);
                DrawButton(g, _quitButton, "退出", false, smallFont);
            }
        }

        private static string FormatInterval(int seconds)
        {
            if (seconds < 60)
                return seconds + "s";
            return (seconds / 60) + "m";
        }

        private void DrawButton(Graphics g, Rectangle rect, string label, bool active, Font font)
        {
            using (var path = Rounded(rect, 6))
            {
                using (var fill = new SolidBrush(active ? Palette.ButtonActiveFill : Palette.ButtonFill))
                    g.FillPath(fill, path);
                using (var border = new Pen(active ? Palette.ButtonActiveBorder : Palette.Border))
                    g.DrawPath(border, path);
            }
            SizeF size = g.MeasureString(label, font);
            using (var brush = new SolidBrush(active ? Palette.ButtonActiveText : Palette.Text))
                g.DrawString(label, font, brush,
                    rect.X + (rect.Width - size.Width) / 2,
                    rect.Y + (rect.Height - size.Height) / 2);
        }

        private static GraphicsPath Rounded(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private void DrawRow(Graphics g, BatteryReading r, int y, Font nameFont, Font boldFont, Font smallFont,
                             Brush text, Brush dim)
        {
            var rowRect = new Rectangle(1, y, Width - 2, RowHeight);
            using (var rowBrush = new SolidBrush(Palette.Row))
                g.FillRectangle(rowBrush, rowRect);
            using (var line = new Pen(Palette.RowSeparator))
                g.DrawLine(line, 14, y + RowHeight - 1, Width - 14, y + RowHeight - 1);

            DrawKindGlyph(g, r.Kind, new Rectangle(14, y + 12, 26, 26));

            string percentText = r.Percent >= 0 ? r.Percent + "%" : "—";
            SizeF percentSize = g.MeasureString(percentText, boldFont);
            g.DrawString(percentText, boldFont, text, Width - percentSize.Width - 16, y + 8);

            string name = r.Name.Length > 22 ? r.Name.Substring(0, 21) + "…" : r.Name;
            g.DrawString(name, nameFont, text, 50, y + 7);

            string status = r.StatusText;
            SizeF statusSize = g.MeasureString(status, smallFont);
            g.DrawString(status, smallFont, dim, Width - statusSize.Width - 16, y + 28);

            var bar = new Rectangle(50, y + 31, Width - 50 - 70, 7);
            using (var back = new SolidBrush(Palette.BarTrack))
                g.FillRectangle(back, bar);
            if (r.Percent > 0)
            {
                int width = (int)Math.Round(bar.Width * Math.Min(100, r.Percent) / 100.0);
                using (var fill = new SolidBrush(IconFactory.LevelColor(r.Percent)))
                    g.FillRectangle(fill, new Rectangle(bar.X, bar.Y, Math.Max(2, width), bar.Height));
            }
        }

        private static void DrawKindGlyph(Graphics g, DeviceKind kind, Rectangle box)
        {
            using (var pen = new Pen(Palette.Glyph, 1.6f))
            using (var brush = new SolidBrush(Palette.GlyphFill))
            {
                switch (kind)
                {
                    case DeviceKind.Keyboard:
                        g.FillRectangle(brush, box.X, box.Y + 6, box.Width, box.Height - 12);
                        g.DrawRectangle(pen, box.X, box.Y + 6, box.Width, box.Height - 12);
                        break;
                    case DeviceKind.Mouse:
                        g.FillEllipse(brush, box.X + 6, box.Y, box.Width - 12, box.Height);
                        g.DrawEllipse(pen, box.X + 6, box.Y, box.Width - 12, box.Height);
                        break;
                    case DeviceKind.Controller:
                        g.FillRectangle(brush, box.X, box.Y + 5, box.Width, box.Height - 10);
                        g.DrawRectangle(pen, box.X, box.Y + 5, box.Width, box.Height - 10);
                        g.DrawEllipse(pen, box.X + 5, box.Y + 11, 6, 6);
                        break;
                    default:
                        g.DrawEllipse(pen, box.X + 7, box.Y + 7, box.Width - 14, box.Height - 14);
                        break;
                }
            }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (_refreshButton.Contains(e.Location))
            {
                Raise(RefreshClicked);
                Invalidate();
                return;
            }
            if (_intervalButton.Contains(e.Location))
            {
                int next = IntervalSeconds == 30 ? 60 : IntervalSeconds == 60 ? 120 : IntervalSeconds == 120 ? 300 : 30;
                IntervalSeconds = next;
                Raise(IntervalChanged);
                Invalidate();
                return;
            }
            if (_autostartButton.Contains(e.Location))
            {
                AutostartOn = !AutostartOn;
                Raise(AutostartToggled);
                Invalidate();
                return;
            }
            if (_quitButton.Contains(e.Location))
            {
                Raise(QuitClicked);
                return;
            }
            Close();
        }

        private void Raise(EventHandler handler)
        {
            if (handler != null)
                handler(this, EventArgs.Empty);
        }
    }
}
