using System.Drawing;

namespace VibeAlarm.UI.Theming
{
    /// <summary>
    /// Central design tokens for the monochrome editorial system, translated from
    /// the Music Oasis design authority. All screen styling must read from these
    /// tokens rather than inventing its own colors, fonts, or spacing.
    /// </summary>
    public static class VibeAlarmPalette
    {
        // ---- Color tokens (monochrome, no accents) ----
        // Light side: neutral/technical rather than warm. "Warm Paper" is the preset that owns
        // warmth; the shared tokens stay neutral so the editorial voice reads as technical.
        public static readonly Color Background = Color.FromArgb(244, 244, 245);  // #F4F4F5
        public static readonly Color Surface = Color.FromArgb(255, 255, 255);    // #FFFFFF
        public static readonly Color TextPrimary = Color.FromArgb(17, 17, 17);   // #111111 crisp near-black
        public static readonly Color TextSecondary = Color.FromArgb(113, 113, 122); // #71717A technical gray
        public static readonly Color Divider = Color.FromArgb(220, 220, 223);    // #DCDCDF thin neutral

        // Dark surfaces (Now Playing analog / dark editorial variant). Never pure #000000 —
        // every dark surface stays a hair above true black to reduce eye strain. These back the
        // "Graphite" preset and stay one tonal step ABOVE "Midnight" so the two remain distinct.
        public static readonly Color DarkBg = Color.FromArgb(18, 18, 18);        // #121212
        public static readonly Color DarkSurface = Color.FromArgb(28, 28, 31);   // #1C1C1F

        // Semantic status colors — the only sanctioned non-grayscale accents. Desaturated so
        // they read as editorial rather than "traffic-light"; used only when status is
        // genuinely meaningful (validation errors, destructive actions, affirmations).
        public static readonly Color Error = Color.FromArgb(168, 75, 63);        // #A84B3F brick
        public static readonly Color Warning = Color.FromArgb(168, 102, 60);     // #A8663C muted amber
        public static readonly Color Success = Color.FromArgb(84, 110, 84);      // #546E54 muted green

        // ---- Semantic task-type colors (desaturated, used sparingly for badges only) ----
        public static readonly Color AlarmMark = Color.FromArgb(168, 75, 63);   // error brick
        public static readonly Color ImportantMark = Color.FromArgb(102, 102, 102);
        public static readonly Color NotificationMark = Color.FromArgb(17, 17, 17);

        // ---- Spacing tokens (4/8/16/24/40/64) ----
        public const int Xs = 4;
        public const int Sm = 8;
        public const int Md = 16;
        public const int Lg = 24;
        public const int Xl = 40;
        public const int Xxl = 64;

        // ---- Geometry ----
        public const int Hairline = 1;
        public const int RadiusMax = 8;
        public const int SidebarWidth = 220;

        // ---- Typography ----
        // The editorial/technical contrast: geometric grotesk for headers + nav + greetings,
        // monospace for every digit-heavy or status label (times, dates, counts, statuses).
        // Faces are embedded OFL fonts registered by FontRegistry; the names below are the
        // preferred families, and FontRegistry falls back to a Windows-native equivalent per
        // role when a face is unavailable. Never construct `new Font("Inter", ...)` directly —
        // an unregistered family silently substitutes Microsoft Sans Serif.
        public const string DisplayFont = "Inter";
        public const string BodyFont = "Inter";
        public const string MonoFont = "JetBrains Mono";

        public static Font Display(float size, FontStyle style = FontStyle.Bold)
            => FontRegistry.Display(size, style);

        public static Font Body(float size, FontStyle style = FontStyle.Regular)
            => FontRegistry.Body(size, style);

        public static Font Mono(float size, FontStyle style = FontStyle.Regular)
            => FontRegistry.Mono(size, style);

        /// <summary>Positive/negative contrast for a light surface (textPrimary on background).</summary>
        public static Color OnLightPrimary => TextPrimary;
        public static Color OnLightSecondary => TextSecondary;
    }
}
