using System;
using System.Drawing;
using Microsoft.Win32;

namespace BattWhale
{
    /// <summary>Colours for the panel and for the tray glyph, which has to stay visible on
    /// both the dark and the light Windows taskbar.</summary>
    internal static class Palette
    {
        // --- panel (light) ---
        public static readonly Color Panel = Color.FromArgb(250, 250, 251);
        public static readonly Color Border = Color.FromArgb(212, 216, 223);
        public static readonly Color Row = Color.FromArgb(255, 255, 255);
        public static readonly Color RowSeparator = Color.FromArgb(232, 235, 240);
        public static readonly Color Text = Color.FromArgb(30, 32, 36);
        public static readonly Color Secondary = Color.FromArgb(108, 114, 124);
        public static readonly Color BarTrack = Color.FromArgb(228, 231, 236);
        public static readonly Color Glyph = Color.FromArgb(72, 112, 172);
        public static readonly Color GlyphFill = Color.FromArgb(214, 227, 245);

        public static readonly Color ButtonFill = Color.FromArgb(245, 246, 249);
        public static readonly Color ButtonActiveFill = Color.FromArgb(225, 237, 252);
        public static readonly Color ButtonActiveBorder = Color.FromArgb(128, 168, 220);
        public static readonly Color ButtonActiveText = Color.FromArgb(28, 80, 150);

        public static Color LevelColor(int percent)
        {
            if (percent < 0) return Color.FromArgb(150, 156, 160);
            if (percent >= 50) return Color.FromArgb(46, 178, 106);
            if (percent >= 20) return Color.FromArgb(214, 150, 20);
            return Color.FromArgb(214, 74, 62);
        }

        /// <summary>True when the taskbar is light, i.e. the tray glyph needs a dark outline.</summary>
        public static bool LightTaskbar()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (key != null)
                    {
                        object value = key.GetValue("SystemUsesLightTheme");
                        if (value is int)
                            return (int)value == 1;
                    }
                }
            }
            catch (Exception)
            {
            }
            return false;
        }
    }
}
