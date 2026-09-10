using System.Drawing;

namespace VibeAlarm.UI.Theming
{
    /// <summary>
    /// Preset-independent design tokens: spacing scale, border radius policy, and typography.
    /// These are constants shared by EVERY control across all 5 ThemePresets. Per-preset color
    /// decisions live on <see cref="VibeAlarm.Models.ThemePreset"/>; this type intentionally holds
    /// no color values so no theme can be bypassed through it.
    /// </summary>
    public static class DesignTokens
    {
        /// <summary>Spacing scale. Layout spacing derived from the 4px rhythm: Xs=4, Sm=8, Md=16, Lg=24, Xl=32, Xxl=48.</summary>
        public static class Spacing
        {
            public const int Xs = 4;
            public const int Sm = 8;
            public const int Md = 16;
            public const int Lg = 24;
            public const int Xl = 32;
            public const int Xxl = 48;
        }

        /// <summary>
        /// Border-radius policy (§14.2 Notion/Calendar hybrid). Rounded, soft elevation: controls
        /// use an 8px radius, cards 12px. Zero remains only for elements that must be sharp (e.g.
        /// hairline dividers), never as the default for interactive surfaces.
        /// </summary>
        public static class Radius
        {
            /// <summary>Sharp — hairline rules, dividers, and other non-interactive edges.</summary>
            public const int None = 0;
            /// <summary>Controls — buttons, inputs, toggles, cells (§14.2 RadiusControl).</summary>
            public const int Small = 8;
            /// <summary>Cards and large surfaces (§14.2 RadiusCard).</summary>
            public const int Medium = 12;
        }

        /// <summary>
        /// Typography tokens. Font family + style decisions are centralized here and reused by
        /// the factories; they define the editorial (Georgia display / Segoe UI body / code-mono
        /// numeric) voice without duplicating font names per form.
        /// </summary>
        public static class Typography
        {
            public const int BodySize = 10;
            public const int FieldSize = 12;
            public const int ButtonSize = 10;
            public const int CaptionSize = 9;
            public const int NumericSize = 15;
            public const int DisplayTitleSize = 23;

            public static Font Body(float size = BodySize, FontStyle style = FontStyle.Regular)
                => VibeAlarmPalette.Body(size, style);

            public static Font Display(float size = DisplayTitleSize, FontStyle style = FontStyle.Bold)
                => VibeAlarmPalette.Display(size, style);

            public static Font Mono(float size = CaptionSize, FontStyle style = FontStyle.Regular)
                => VibeAlarmPalette.Mono(size, style);
        }
    }
}