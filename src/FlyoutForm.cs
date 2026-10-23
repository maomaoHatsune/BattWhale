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
        private readonly float _scale;
        private readonly Rectangle _refreshButton;
        private readonly Rectangle _intervalButton;
        private readonly Rectangle _autostartButton;
        private readonly Rectangle _quitButton;
        private readonly Rectangle _langButton;

        public bool Sticky;
        public bool AutostartOn;
        public int IntervalSeconds = 60;

        public event EventHandler RefreshClicked;
        public event EventHandler QuitClicked;
        public event EventHandler AutostartToggled;
        public event EventHandler IntervalChanged;
        public event EventHandler LanguageToggled;

        public FlyoutForm(List<BatteryReading> readings)
        {
            _readings = readings;
            _scale = DpiScale();
            // Style flags must be set before the window handle exists; toggling UserPaint
            // afterwards leaves the first paints in a half-initialised GDI+ state (that is
            // what made Graphics.set_SmoothingMode throw "参数无效").
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            DoubleBuffered = true;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Palette.Panel;
            int rows = Math.Max(1, Math.Min(readings.Count, 6));
            int logicalHeight = HeaderHeight + rows * RowHeight + FooterHeight + ActionHeight;
            ClientSize = new Size(S(Width_), S(logicalHeight));

            int y = S(logicalHeight - ActionHeight + 7);
            _refreshButton = new Rectangle(S(12), y, S(66), S(26));
            _intervalButton = new Rectangle(S(84), y, S(86), S(26));
            _autostartButton = new Rectangle(S(176), y, S(76), S(26));
            _quitButton = new Rectangle(S(258), y, S(46), S(26));
            _langButton = new Rectangle(ClientSize.Width - S(40), S(9), S(28), S(22));
        }

        /// <summary>Current display scale (1.0 at 96 dpi, 1.5 at 150%).</summary>
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

        /// <summary>Converts a 96-dpi design pixel into a real pixel.</summary>
        private int S(int logical)
        {
            return (int)Math.Round(logical * _scale);
        }

        /// <summary>
        /// Builds a font at the same physical size on any display. GDI+ sizes fonts in
        /// points, so we convert to pixels against the real DPI and keep the unit explicit;
        /// that keeps ClearType working instead of letting the compositor stretch a bitmap.
        /// </summary>
        private Font MakeFont(float points, FontStyle style)
        {
            return new Font("Microsoft YaHei UI", points * 96f / 72f * _scale, style, GraphicsUnit.Pixel);
        }

        public void ShowNearTray()
        {
            Rectangle work = Screen.PrimaryScreen.WorkingArea;
            // Width/Height are real pixels here, so the margin has to be scaled too.
            Location = new Point(work.Right - Width - S(12), work.Bottom - Height - S(12));
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
            try
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                PaintBody(g);
            }
            catch (Exception ex)
            {
                // A paint exception must never take the whole app down.
                if (!_paintErrorLogged)
                {
                    _paintErrorLogged = true;
                    DebugLog.Always("面板绘制异常: " + ex.Message + " handle=" + IsHandleCreated +
                                    " visible=" + Visible + " size=" + ClientSize);
                }
            }
        }

        private bool _paintErrorLogged;

        private void PaintBody(Graphics g)
        {
            using (var background = new SolidBrush(Palette.Panel))
                g.FillRectangle(background, ClientRectangle);
            using (var border = new Pen(Palette.Border, 1))
                g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);

            using (var titleFont = MakeFont(10.5f, FontStyle.Bold))
            using (var smallFont = MakeFont(8.5f, FontStyle.Regular))
            using (var nameFont = MakeFont(9.5f, FontStyle.Regular))
            using (var boldFont = MakeFont(10.5f, FontStyle.Bold))
            using (var text = new SolidBrush(Palette.Text))
            using (var dim = new SolidBrush(Palette.Secondary))
            {
                g.DrawString(Strings.AppTitle, titleFont, text, S(14), S(12));
                string summary = _readings.Count == 0 ? Strings.NoDevices : Strings.DeviceCount(_readings.Count);
                SizeF summarySize = g.MeasureString(summary, smallFont);
                g.DrawString(summary, smallFont, dim, Width - summarySize.Width - S(48), S(16));
                DrawButton(g, _langButton, Strings.LangButton, false, smallFont);

                if (_readings.Count == 0)
                    g.DrawString(Strings.NoDevicesHint, smallFont, dim, S(14), S(HeaderHeight + 8));

                int y = S(HeaderHeight);
                foreach (BatteryReading r in _readings)
                {
                    DrawRow(g, r, y, nameFont, boldFont, smallFont, text, dim);
                    y += S(RowHeight);
                }

                string footer = Strings.Footer(DateTime.Now.ToString("HH:mm:ss"));
                g.DrawString(footer, smallFont, dim, S(14), Height - S(ActionHeight + FooterHeight - 4));

                DrawButton(g, _refreshButton, Strings.Refresh, false, smallFont);
                DrawButton(g, _intervalButton, Strings.IntervalLabel(IntervalSeconds), false, smallFont);
                DrawButton(g, _autostartButton, Strings.AutostartLabel(AutostartOn), AutostartOn, smallFont);
                DrawButton(g, _quitButton, Strings.Quit, false, smallFont);
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
            using (var path = Rounded(rect, S(6)))
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
            var rowRect = new Rectangle(S(1), y, Width - S(2), S(RowHeight));
            using (var rowBrush = new SolidBrush(Palette.Row))
                g.FillRectangle(rowBrush, rowRect);
            using (var line = new Pen(Palette.RowSeparator))
                g.DrawLine(line, S(14), y + S(RowHeight) - 1, Width - S(14), y + S(RowHeight) - 1);

            DrawKindGlyph(g, r.Kind, new Rectangle(S(14), y + S(12), S(26), S(26)));

            string percentText = r.Percent >= 0 ? r.Percent + "%" : "—";
            SizeF percentSize = g.MeasureString(percentText, boldFont);
            g.DrawString(percentText, boldFont, text, Width - percentSize.Width - S(16), y + S(8));

            string name = r.Name.Length > 22 ? r.Name.Substring(0, 21) + "…" : r.Name;
            g.DrawString(name, nameFont, text, S(50), y + S(7));

            string status = r.StatusText;
            SizeF statusSize = g.MeasureString(status, smallFont);
            g.DrawString(status, smallFont, dim, Width - statusSize.Width - S(16), y + S(28));

            var bar = new Rectangle(S(50), y + S(31), Width - S(50) - S(70), S(7));
            using (var back = new SolidBrush(Palette.BarTrack))
                g.FillRectangle(back, bar);
            if (r.Percent > 0)
            {
                int width = (int)Math.Round(bar.Width * Math.Min(100, r.Percent) / 100.0);
                using (var fill = new SolidBrush(IconFactory.LevelColor(r.Percent)))
                    g.FillRectangle(fill, new Rectangle(bar.X, bar.Y, Math.Max(S(2), width), bar.Height));
            }
        }

        private void DrawKindGlyph(Graphics g, DeviceKind kind, Rectangle box)
        {
            using (var pen = new Pen(Palette.Glyph, 1.6f * _scale))
            using (var brush = new SolidBrush(Palette.GlyphFill))
            {
                switch (kind)
                {
                    case DeviceKind.Keyboard:
                        g.FillRectangle(brush, box.X, box.Y + S(6), box.Width, box.Height - S(12));
                        g.DrawRectangle(pen, box.X, box.Y + S(6), box.Width, box.Height - S(12));
                        break;
                    case DeviceKind.Mouse:
                        g.FillEllipse(brush, box.X + S(6), box.Y, box.Width - S(12), box.Height);
                        g.DrawEllipse(pen, box.X + S(6), box.Y, box.Width - S(12), box.Height);
                        break;
                    case DeviceKind.Controller:
                        g.FillRectangle(brush, box.X, box.Y + S(5), box.Width, box.Height - S(10));
                        g.DrawRectangle(pen, box.X, box.Y + S(5), box.Width, box.Height - S(10));
                        g.DrawEllipse(pen, box.X + S(5), box.Y + S(11), S(6), S(6));
                        break;
                    default:
                        g.DrawEllipse(pen, box.X + S(7), box.Y + S(7), box.Width - S(14), box.Height - S(14));
                        break;
                }
            }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (_langButton.Contains(e.Location))
            {
                Strings.Current = Strings.IsEnglish ? Lang.Zh : Lang.En;
                Raise(LanguageToggled);
                Invalidate();
                return;
            }
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
