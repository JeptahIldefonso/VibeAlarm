using System;
using System.Drawing;

namespace VibeAlarm.UI.Theming
{
    /// <summary>
    /// Shared blend math: the one home for the lighten/darken/blend channel math used
    /// across the accent system — the accent-card preview backgrounds in the Settings
    /// picker. Plain math, no external package. (The WCAG helpers remain for future
    /// accessibility work; the shipped on-accent inks are fixed per-preset values, not
    /// computed.)
    /// </summary>
    public static class ColorMath
    {
        public static Color Lighten(Color c, double amount) => Blend(c, Color.White, amount);

        public static Color Darken(Color c, double amount) => Blend(c, Color.Black, amount);

        public static Color Blend(Color baseColor, Color overlay, double amount) =>
            Color.FromArgb(
                (byte)(baseColor.R + (overlay.R - baseColor.R) * amount),
                (byte)(baseColor.G + (overlay.G - baseColor.G) * amount),
                (byte)(baseColor.B + (overlay.B - baseColor.B) * amount));

        /// <summary>WCAG 2.x relative luminance (0 = black, 1 = white): each sRGB channel is
        /// linearized, then weighted 0.2126/0.7152/0.0722 for R/G/B. The basis for
        /// <see cref="ContrastRatio"/> and the on-accent ink decision.</summary>
        public static double RelativeLuminance(Color c)
        {
            static double Linear(byte channel)
            {
                double s = channel / 255.0;
                return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);
        }

        /// <summary>WCAG contrast ratio (1 = identical, 21 = black on white). AA for normal
        /// text requires ≥ 4.5.</summary>
        public static double ContrastRatio(Color a, Color b)
        {
            double la = RelativeLuminance(a);
            double lb = RelativeLuminance(b);
            return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
        }
    }
}
