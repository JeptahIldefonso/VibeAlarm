using System;
using VibeAlarm.UI.Theming;

namespace VibeAlarm.Services
{
    /// <summary>
    /// Applies the selected <see cref="AccentCatalog"/> preset.
    /// The accent is exposed to the UI bridge which updates CSS custom properties at runtime.
    /// </summary>
    public sealed class ThemeService
    {
        /// <summary>Canonical shared service so the app applies the accent from one place.</summary>
        public static ThemeService Shared { get; } = new();

        /// <summary>The currently active accent option.</summary>
        public AccentOption CurrentAccent { get; private set; }

        public ThemeService()
        {
            CurrentAccent = ApplyAccent(AccentCatalog.DefaultKey);
        }

        /// <summary>Publishes an <see cref="AccentCatalog"/> preset. Unknown/blank keys resolve to
        /// the default, never throw.</summary>
        public AccentOption ApplyAccent(string? key)
        {
            AccentOption accent = AccentCatalog.Resolve(key);
            CurrentAccent = accent;
            return accent;
        }
    }
}
