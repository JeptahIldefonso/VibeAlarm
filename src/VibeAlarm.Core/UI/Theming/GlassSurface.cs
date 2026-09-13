using System.Drawing;

namespace VibeAlarm.UI.Theming
{
    /// <summary>
    /// The ONLY place transparency math lives. Every translucent surface in the app is
    /// computed here from locked design constants — the card-container spec: a soft,
    /// translucent rounded box per item, matching the mobile app's Tasks screen. Fills
    /// and borders are WHITE overlays over the dark page backdrop (never a per-call
    /// hue), the border is one step stronger than the fill, and hover bumps fill and
    /// border one quiet step each. No form or factory may compute its own ARGB surface.
    ///
    /// This is TINT-ONLY glass — a translucent fill over whatever the background shows
    /// through. True blur-behind (DWM composition / acrylic) is deliberately NOT simulated:
    /// per-card backdrop capture would be the primary lag risk on the most-rebuilt lists in
    /// the app. If real acrylic is wanted later, that is a DWM API change, not a
    /// transparency-value change.
    ///
    /// The app chrome (sidebar, bottom strip) is NOT glass: it is opaque black
    /// (<see cref="VibeAlarmPalette.Chrome"/>, Spotify-style); the top header band sits
    /// on the content surface. Only the card containers carry transparency.
    /// </summary>
    public static class GlassSurface
    {
        // ---- Card-container spec (locked alphas, white over the dark backdrop) ----
        // Fill: white at ~5.5% opacity (#0EFFFFFF) — the "transparent box" look.
        // Border: white at ~9% (#17FFFFFF) — subtle definition, not a hard line.
        // Hover fill: ~8% (#14FFFFFF); hover border: ~14% (#24FFFFFF) — a small lift.
        private const int CardFillAlpha = 14;      // ~5.5% white
        private const int CardBorderAlpha = 23;    // ~9% white
        private const int HoverFillAlpha = 20;     // ~8% white
        private const int HoverBorderAlpha = 36;   // ~14% white

        private static Color WhiteOverlay(int alpha) => Color.FromArgb(alpha, 255, 255, 255);

        // ---- Effective surface tokens ----
        // Card surfaces (task rows, stat tiles, month header, info blocks): the row-level
        // card container. Task rows deliberately share the SAME token as plain cards — the
        // spec is one card style across every list, not a stronger variant per list.
        public static Color CardFill() => WhiteOverlay(CardFillAlpha);
        public static Color CardBorder() => WhiteOverlay(CardBorderAlpha);

        /// <summary>Hover fill: the same white overlay one clearly stronger step (~8% vs the
        /// resting ~5.5%) — translucent like the resting fill so a hover never flashes a
        /// solid block over the surface behind the card, while still reading as an obvious
        /// state change.</summary>
        public static Color CardHover() => WhiteOverlay(HoverFillAlpha);

        /// <summary>Hover border (~14% white): pairs with <see cref="CardHover"/> — the lift
        /// is fill AND border, so the card's outline sharpens as it brightens.</summary>
        public static Color CardHoverBorder() => WhiteOverlay(HoverBorderAlpha);

        // Section panels (settings groups, ambient sections, calendar grid, raised strips):
        // the same white family, a hair more present (~6%).
        public static Color PanelFill() => WhiteOverlay(15);
        public static Color PanelBorder() => CardBorder();

        // Settings section cards use the panel surface (slightly more present than row cards).
        public static Color SectionFill() => PanelFill();
        public static Color SectionBorder() => PanelBorder();

        /// <summary>Task rows (Tasks / Calendar / Dashboard lists) share the card-container
        /// fill — one card style everywhere per the spec. Kept as its own accessor so a
        /// future row-specific tweak is a one-line change, not a hunt through call sites.</summary>
        public static Color TaskCardFill() => CardFill();
    }
}
