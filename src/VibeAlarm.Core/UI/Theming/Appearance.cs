namespace VibeAlarm.UI.Theming
{
    /// <summary>
    /// Locked appearance constants: the card-container geometry. The whole app reads the
    /// "soft translucent rounded box" spec from here — list-row cards, section panels,
    /// padding, and stacking gap — so the look is defined ONCE and every screen references
    /// it (future retuning is a one-line change). Corner radius was formerly a persisted
    /// setting; the slider is gone and these are the tuned values.
    /// </summary>
    public static class Appearance
    {
        /// <summary>Card radius in px for LIST ROWS (task rows, stat tiles, small cards):
        /// the mobile app's rounded feel at 14px. Buttons do NOT follow this: text buttons
        /// keep their own <see cref="DesignTokens.Radius"/> and icon buttons floor at 4.</summary>
        public const int CardRadius = 14;

        /// <summary>Card radius in px for LARGER SECTION PANELS (settings groups, ambient
        /// sections, the calendar grid, NEXT UP): one step rounder than rows.</summary>
        public const int SectionRadius = 20;

        /// <summary>Control (icon button) radius — kept as its own constant so a future
        /// rounded-controls look is a one-line change rather than a re-coupling to cards.
        /// Icon buttons floor at 4px in the factory regardless.</summary>
        public const int ControlRadius = 0;

        /// <summary>Padding inside every card: 16px horizontal, 14px vertical (the
        /// card-container spec). Framework-neutral ints — each UI head wraps them into its
        /// own padding type (WinForms: WinFormsAppearance.CardPadding). Fixed-height list
        /// rows keep their own internal layout (their height already centers content) but
        /// match the horizontal value.</summary>
        public const int CardPaddingHorizontal = 16;
        public const int CardPaddingVertical = 14;

        /// <summary>Vertical gap between stacked cards (10–12px band, tuned to 12): each
        /// card in a stack carries half of this as top/bottom margin so neighbors never
        /// touch and the gap never doubles against a container's own padding.</summary>
        public const int CardGap = 12;

        /// <summary>Layout density: Compact is locked in (the tuned interface ran with the
        /// density dropdown on Compact).</summary>
        public static bool Compact => true;

        /// <summary>Task row height in px. Rows are cards now (rounded translucent boxes
        /// with a gap between them) rather than a flat list with dividers, so the height
        /// carries the card's own visual weight — title + meta at any DPI.</summary>
        public static int TaskRowHeight => Compact ? 56 : 64;

        /// <summary>Vertical padding inside settings rows.</summary>
        public static int SettingsRowPadding => Compact ? 8 : 14;
    }
}
