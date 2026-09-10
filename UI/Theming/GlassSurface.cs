using System.Drawing;
using VibeAlarm.Models;

namespace VibeAlarm.UI.Theming
{
    /// <summary>
    /// The ONLY place transparency math lives (§23–24 of the orientation redesign). User-facing
    /// transparency is a friendly 0–100% where 0% = completely opaque and 100% = maximum
    /// transparency; internally each percentage maps to an alpha channel, clamped to a
    /// per-surface safe minimum so the UI can never become unreadable — the slider still shows
    /// the user's chosen percentage even when the visual alpha is floored.
    ///
    /// Effective colors are computed from the active <see cref="ThemePreset"/> plus the
    /// persisted appearance settings; no form or factory may compute its own ARGB surface.
    /// </summary>
    public static class GlassSurface
    {
        // Per-surface alpha floors: the lowest alpha (most transparent) each surface may ever
        // reach, keeping text on it readable even at the 100% slider extreme.
        public const int CardMinAlpha = 64;      // ~25%
        public const int PanelMinAlpha = 77;     // ~30%
        public const int BorderMinAlpha = 51;    // ~20%
        public const int SidebarMinAlpha = 128;  // ~50%

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
        /// only the alpha channel changes, so a theme's hue survives any slider position.</summary>
        public static Color Apply(Color baseColor, int transparencyPct, int minAlpha)
            => Color.FromArgb(AlphaFromPercent(transparencyPct, minAlpha), baseColor);

        // ---- Effective surface tokens ----
        // Card surfaces (task rows, month header, info blocks).
        public static Color CardFill(ThemePreset p, AppSettings s) => Apply(p.CardBgColor, s.CardTransparency, CardMinAlpha);
        public static Color CardBorder(ThemePreset p, AppSettings s) => Apply(p.BorderColor, s.BorderTransparency, BorderMinAlpha);

        /// <summary>Hover fills stay fully opaque — a hover wash must always read clearly over
        /// whatever the (possibly very transparent) resting surface was.</summary>
        public static Color CardHover(ThemePreset p) => p.CardHoverBg;

        // Raised panels (status bar, calendar toolbar, settings sections).
        public static Color PanelFill(ThemePreset p, AppSettings s) => Apply(p.SecondaryBg, s.PanelTransparency, PanelMinAlpha);
        public static Color PanelBorder(ThemePreset p, AppSettings s) => CardBorder(p, s);

        // The navigation sidebar — the most "glass" surface in the app, so it gets a higher floor.
        public static Color SidebarFill(ThemePreset p, AppSettings s) => Apply(p.SidebarBg, s.GlassTransparency, SidebarMinAlpha);

        // Settings section cards use the panel transparency (slightly more present than task cards).
        public static Color SectionFill(ThemePreset p, AppSettings s) => PanelFill(p, s);
    }
}
