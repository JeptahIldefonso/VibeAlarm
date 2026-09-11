using System.Drawing;
using VibeAlarm.Models;

namespace VibeAlarm.UI.Theming
{
    /// <summary>
    /// The ONLY place transparency math lives (§23–24 of the orientation redesign). Transparency
    /// is expressed as a friendly 0–100% where 0% = completely opaque and 100% = maximum
    /// transparency; internally each percentage maps to an alpha channel, clamped to a
    /// per-surface safe minimum so the UI can never become unreadable.
    ///
    /// The percentages are now HARDCODED design constants (tuned against the blurred background
    /// and locked when the Settings sliders were removed) — effective colors are computed from
    /// the active <see cref="ThemePreset"/> alone; no form or factory may compute its own ARGB
    /// surface.
    /// </summary>
    public static class GlassSurface
    {
        // Per-surface alpha floors: the lowest alpha (most transparent) each surface may ever
        // reach, keeping text on it readable even at the 100% slider extreme.
        public const int CardMinAlpha = 64;      // ~25%
        public const int PanelMinAlpha = 77;     // ~30%
        public const int BorderMinAlpha = 51;    // ~20%
        public const int SidebarMinAlpha = 128;  // ~50%
        public const int TaskCardMinAlpha = 150; // ~59% — task rows carry live text, so their
                                                 // glass floor is deliberately the highest of
                                                 // any content surface

        // Locked transparency percentages (formerly the Settings sliders). These match the
        // values the interface was tuned against before the sliders were removed, so the
        // removal was a no-visible-change cleanup.
        private const int CardTransparencyPct = 0;    // cards sit opaque on the background
        private const int BorderTransparencyPct = 27; // hairline borders stay quiet
        private const int PanelTransparencyPct = 8;   // raised panels are barely glassy
        private const int SidebarTransparencyPct = 50; // the nav rail is the one true glass
        private const int TaskCardTransparencyPct = 35; // task rows are tinted glass — see
                                                        // TaskCardFill below

        /// <summary>Converts a 0–100 transparency percentage into an alpha channel value.
        /// 0% → 255 (opaque); 100% → <paramref name="minAlpha"/> (the most transparent the
        /// surface may safely go). Values are clamped to the [minAlpha, 255] range.</summary>
        public static int AlphaFromPercent(int transparencyPct, int minAlpha)
        {
            int pct = Math.Clamp(transparencyPct, 0, 100);
            // 0% → 255, 100% → 0 before the safety floor is applied.
            int raw = 255 - (255 * pct / 100);
            return Math.Clamp(raw, minAlpha, 255);
        }

        /// <summary>Applies transparency to a base color. The RGB channels are never touched —
        /// only the alpha channel changes, so a theme's hue survives any percentage.</summary>
        public static Color Apply(Color baseColor, int transparencyPct, int minAlpha)
            => Color.FromArgb(AlphaFromPercent(transparencyPct, minAlpha), baseColor);

        // ---- Effective surface tokens ----
        // Card surfaces (task rows, month header, info blocks).
        public static Color CardFill(ThemePreset p) => Apply(p.CardBgColor, CardTransparencyPct, CardMinAlpha);
        public static Color CardBorder(ThemePreset p) => Apply(p.BorderColor, BorderTransparencyPct, BorderMinAlpha);

        /// <summary>Hover fills stay fully opaque — a hover wash must always read clearly over
        /// whatever the (possibly very transparent) resting surface was.</summary>
        public static Color CardHover(ThemePreset p) => p.CardHoverBg;

        // Raised panels (status bar, calendar toolbar, settings sections).
        public static Color PanelFill(ThemePreset p) => Apply(p.SecondaryBg, PanelTransparencyPct, PanelMinAlpha);
        public static Color PanelBorder(ThemePreset p) => CardBorder(p);

        // The navigation sidebar — the most "glass" surface in the app, so it gets a higher floor.
        public static Color SidebarFill(ThemePreset p) => Apply(p.SidebarBg, SidebarTransparencyPct, SidebarMinAlpha);

        // Settings section cards use the panel transparency (slightly more present than task cards).
        public static Color SectionFill(ThemePreset p) => PanelFill(p);

        /// <summary>Task rows (Tasks / Calendar / Dashboard lists) are a SEPARATE, more
        /// transparent surface than <see cref="CardFill"/>: the shared card token stays fully
        /// opaque because Dashboard and Settings section cards depend on it, while task rows
        /// sit directly on the atmospheric background and read as tinted glass. This is
        /// TINT-ONLY glass — a translucent fill over whatever the background shows through.
        /// True blur-behind (DWM composition / acrylic) is deliberately NOT simulated here:
        /// per-card backdrop capture would be the primary lag risk on the most-rebuilt list
        /// in the app. If real acrylic is wanted later, that is a DWM API change, not a
        /// transparency-percentage change.</summary>
        public static Color TaskCardFill(ThemePreset p) => Apply(p.CardBgColor, TaskCardTransparencyPct, TaskCardMinAlpha);
    }
}
