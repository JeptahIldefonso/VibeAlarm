using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace VibeAlarm.UI.Theming
{
    /// <summary>One named accent preset: its base fill, hover fill, and the ink drawn on
    /// both. All three are fixed per-preset values from the locked color system — never
    /// computed at runtime.</summary>
    public sealed record AccentOption(string Key, Color Base, Color Hover, Color OnAccent);

    /// <summary>
    /// The Settings accent catalog: the eight named presets of the locked color system.
    /// "Matrix Green" (#22C55E) is the default, so a fresh install matches the reference
    /// screenshots out of the box. Unknown/blank persisted keys resolve back to the
    /// default, never throw; earlier eras' short keys ("Fluent", "Forest", …) and the
    /// retired "Spotify Green" default map onto their corresponding presets so an
    /// existing settings.json lands on the same look.
    /// </summary>
    public static class AccentCatalog
    {
        public const string DefaultKey = "Matrix Green";

        public static readonly IReadOnlyList<AccentOption> Options = new AccentOption[]
        {
            new("Obsidian Core",   Color.FromArgb(0x8E, 0x8E, 0x93), Color.FromArgb(0xA0, 0xA0, 0xA6), Color.FromArgb(0x00, 0x00, 0x00)),
            new("Neo Blue",        Color.FromArgb(0x4C, 0x8D, 0xFF), Color.FromArgb(0x6F, 0xA1, 0xFF), Color.FromArgb(0xFF, 0xFF, 0xFF)),
            new("Violet System",   Color.FromArgb(0x7C, 0x5C, 0xFC), Color.FromArgb(0x94, 0x78, 0xFD), Color.FromArgb(0xFF, 0xFF, 0xFF)),
            new("Matrix Green",    Color.FromArgb(0x22, 0xC5, 0x5E), Color.FromArgb(0x34, 0xD3, 0x74), Color.FromArgb(0x00, 0x00, 0x00)),
            new("Sunset Red",      Color.FromArgb(0xE5, 0x48, 0x4D), Color.FromArgb(0xF1, 0x60, 0x65), Color.FromArgb(0xFF, 0xFF, 0xFF)),
            new("Solar Amber",     Color.FromArgb(0xF0, 0xA0, 0x20), Color.FromArgb(0xF5, 0xB3, 0x47), Color.FromArgb(0x00, 0x00, 0x00)),
            new("Pearl White",     Color.FromArgb(0xE8, 0xE9, 0xEC), Color.FromArgb(0xF2, 0xF3, 0xF5), Color.FromArgb(0x00, 0x00, 0x00)),
            new("Platinum Silver", Color.FromArgb(0xC4, 0xC8, 0xCC), Color.FromArgb(0xD4, 0xD7, 0xDA), Color.FromArgb(0x00, 0x00, 0x00)),
        };

        /// <summary>Earlier eras' persisted keys — the first circular-swatch set's short
        /// keys plus the retired "Spotify Green" default — mapped onto the preset with
        /// the corresponding color so existing saves don't shift.</summary>
        private static readonly IReadOnlyDictionary<string, string> LegacyKeys =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Fluent"] = "Neo Blue",
                ["Coral"] = "Sunset Red",
                ["Forest"] = "Matrix Green",
                ["Indigo"] = "Violet System",
                ["Amber"] = "Solar Amber",
                ["Spotify Green"] = "Matrix Green",
            };

        public static AccentOption Default => Options.First(o => o.Key == DefaultKey);

        /// <summary>Case-insensitive lookup (legacy keys included), or null for a
        /// null/blank/unknown key.</summary>
        public static AccentOption? Find(string? key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            if (LegacyKeys.TryGetValue(key, out string? mapped))
            {
                key = mapped;
            }
            return Options.FirstOrDefault(o => o.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Lookup that always succeeds: unknown or blank falls back to
        /// <see cref="Default"/> so a hand-edited settings.json can never crash the UI.</summary>
        public static AccentOption Resolve(string? key) => Find(key) ?? Default;
    }
}
