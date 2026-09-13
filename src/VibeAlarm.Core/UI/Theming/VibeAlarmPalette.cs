using System.Drawing;

namespace VibeAlarm.UI.Theming
{
    /// <summary>
    /// THE color system, in two layers (the locked single source of truth):
    ///
    /// 1. The FIXED BASE PALETTE — static, never user-selectable, matching the reference
    ///    screenshots exactly: a #121212 content surface over a #000000 chrome family
    ///    (sidebar, top header band, bottom "Tasks Remaining" strip), #FFFFFF primary
    ///    ink, #B3B3B3 muted ink. There is no theme-preset system anymore; these values
    ///    are constants and nothing tints them.
    ///
    /// 2. THE ACCENT — the one runtime-variable part, set from <see cref="AccentCatalog"/>
    ///    via <see cref="VibeAlarm.Services.ThemeService.ApplyAccent"/>: the base fill,
    ///    its hover fill, the on-accent ink, and the 10% selection wash derived from it.
    ///
    /// Semantic status colors (destructive red, the amber/violet metric badges) are fixed
    /// here too — they represent meaning, never the accent. Screen styling reads from
    /// these tokens — never ad-hoc colors.
    /// </summary>
    public static class VibeAlarmPalette
    {
        // ---- Fixed base palette (never user-selectable) ----

        /// <summary>Main content area background — #121212, exactly.</summary>
        public static readonly Color Surface = Color.FromArgb(0x12, 0x12, 0x12);

        /// <summary>Black chrome family — the sidebar nav and the bottom "Tasks Remaining"
        /// strip read this one token so the two never diverge. The top header band is NOT
        /// chrome: it sits on <see cref="Surface"/> so the window top flows into the
        /// content (matching the Spotify reference). #000000, exactly.</summary>
        public static readonly Color Chrome = Color.FromArgb(0x00, 0x00, 0x00);

        /// <summary>Raised input surface (text fields, dropdowns, list boxes) — #202020.</summary>
        public static readonly Color InputFill = Color.FromArgb(0x20, 0x20, 0x20);

        /// <summary>Hover/unchecked fill for input-family controls — #2A2A2A.</summary>
        public static readonly Color InputHoverFill = Color.FromArgb(0x2A, 0x2A, 0x2A);

        /// <summary>Primary text — #F3F3F3, the reference screenshots' soft white (a hair
        /// off pure white, easier on #121212).</summary>
        public static readonly Color Text = Color.FromArgb(0xF3, 0xF3, 0xF3);

        /// <summary>Secondary/muted text — #B3B3B3.</summary>
        public static readonly Color Muted = Color.FromArgb(0xB3, 0xB3, 0xB3);

        /// <summary>Hairline border — #2F2F2F.</summary>
        public static readonly Color Border = Color.FromArgb(0x2F, 0x2F, 0x2F);

        /// <summary>Elevated surface (dialogs, context menus) — #242424.</summary>
        public static readonly Color SurfaceElevated = Color.FromArgb(0x24, 0x24, 0x24);

        // ---- Semantic status colors (fixed, never accent-driven) ----

        /// <summary>Destructive actions ("Delete all tasks", delete menu items) — fixed
        /// #EF4444, never the accent.</summary>
        public static readonly Color Error = Color.FromArgb(0xEF, 0x44, 0x44);

        /// <summary>"Active Tasks" stat-tile bell badge — fixed amber. The tile is a
        /// distinct metric, not the app's accent.</summary>
        public static readonly Color Amber = Color.FromArgb(0xF5, 0x9E, 0x0B);

        /// <summary>"Done Today" stat-tile checkmark badge — fixed violet. Same rule as
        /// <see cref="Amber"/>: a metric, not the accent.</summary>
        public static readonly Color Violet = Color.FromArgb(0x8B, 0x5C, 0xF6);

        // ---- Accent state (the one runtime-variable layer) ----

        /// <summary>The selected accent's base fill (primary buttons, active nav, task
        /// tags, the calendar today circle, progress fills). Set ONLY through
        /// <see cref="VibeAlarm.Services.ThemeService.ApplyAccent"/>; the field default
        /// is the shipped Matrix Green so anything constructed before the first
        /// ApplyAccent already carries the reference look.</summary>
        public static Color Accent { get; set; } = Color.FromArgb(0x22, 0xC5, 0x5E);

        /// <summary>The selected accent's hover fill — one fixed tonal step per preset
        /// (see <see cref="AccentCatalog"/>), never computed at a call site.</summary>
        public static Color AccentHover { get; set; } = Color.FromArgb(0x34, 0xD3, 0x74);

        /// <summary>Ink drawn ON the accent fill (primary buttons, the FAB glyph, the
        /// calendar "today" circle, selected dropdown rows, toggle thumbs) — the preset's
        /// fixed on-accent ink, set together with <see cref="Accent"/>.</summary>
        public static Color OnAccent { get; set; } = Color.FromArgb(0x00, 0x00, 0x00);

        /// <summary>A 10%-alpha wash of the accent — selection tint (calendar day
        /// selection). Derived, so it always tracks <see cref="Accent"/>.</summary>
        public static Color AccentTint => Color.FromArgb(0x1A, Accent);

        // ---- Spacing tokens (4/8/16/24/40/64) ----
        public const int Xs = 4;
        public const int Sm = 8;
        public const int Md = 16;
        public const int Lg = 24;
        public const int Xl = 40;
        public const int Xxl = 64;

        // ---- Geometry ----
        public const int Hairline = 1;
        public const int RadiusMax = 12;
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
    }
}
