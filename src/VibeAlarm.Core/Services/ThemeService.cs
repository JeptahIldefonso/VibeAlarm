using System;
using VibeAlarm.UI.Theming;

namespace VibeAlarm.Services
{
    /// <summary>
    /// Applies the selected <see cref="AccentCatalog"/> preset to the shared palette
    /// state (<see cref="VibeAlarmPalette"/>). The theme-preset system is gone: the base
    /// palette (the #121212 surface, the #000000 chrome family, the ink tokens) is a
    /// fixed constant and nothing retints it — only the accent varies at runtime.
    /// Applying the theme to the UI remains a concern of the view layer.
    /// </summary>
    public sealed class ThemeService
    {
        /// <summary>Canonical shared service so the app applies the accent from one place.</summary>
        public static ThemeService Shared { get; } = new();

        /// <summary>A freshly constructed service — including every test that never calls
        /// <see cref="ApplyAccent"/> — starts on the shipped default accent (Spotify Green,
        /// the reference-screenshot look).</summary>
        public ThemeService()
        {
            ApplyAccent(AccentCatalog.DefaultKey);
        }

        /// <summary>Publishes a <see cref="AccentCatalog"/> preset to the shared palette:
        /// the accent fill, its hover fill, and the fixed on-accent ink
        /// (<see cref="VibeAlarmPalette.OnAccent"/>). Idempotent by construction — the
        /// values are assignments, not blends. Call it BEFORE first rendering so the
        /// persisted accent is live from the first paint. Unknown/blank keys resolve to
        /// the default, never throw.</summary>
        public AccentOption ApplyAccent(string? key)
        {
            AccentOption accent = AccentCatalog.Resolve(key);
            VibeAlarmPalette.Accent = accent.Base;
            VibeAlarmPalette.AccentHover = accent.Hover;
            VibeAlarmPalette.OnAccent = accent.OnAccent;
            return accent;
        }
    }
}
