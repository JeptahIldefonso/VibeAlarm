using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using VibeAlarm.Models;
using VibeAlarm.UI.Theming;

namespace VibeAlarm.Services
{
    /// <summary>
    /// Owns the built-in theme preset catalog and resolves the active preset.
    /// Applying a theme to the UI remains a concern of the view layer.
    /// </summary>
    public sealed partial class ThemeService
    {
        private readonly List<ThemePreset> presets = new();

        /// <summary>Canonical shared service so views and the control factory read the SAME active
        /// preset via <see cref="Current"/>, rather than each form keeping a divergent copy.</summary>
        public static ThemeService Shared { get; } = new();

        public ThemeService()
        {
            InitializePresets();
        }

        public IReadOnlyList<ThemePreset> Presets => presets;

        /// <summary>The currently active preset. Centralized so <see cref="UI.Controls.UIControlFactory"/>
        /// and the view layer render from a single source, fed from persisted settings.</summary>
        public ThemePreset Current { get; set; } = null!;

        /// <summary>The default theme, used when nothing is persisted or the saved name is unknown.</summary>
        public ThemePreset Default => presets[0];

        /// <summary>Resolves the persisted theme name (if any) and sets <see cref="Current"/> to it.
        /// Unknown/legacy names are bucketed by the old preset's light/dark intent (or default to Light).
        /// Falls back to the default preset. Returns the active preset.</summary>
        public ThemePreset ActivateFromSettings()
        {
            Current = ResolveThemeName(SettingsService.LoadThemeName()) ?? Default;
            return Current;
        }

        /// <summary>Looks up a preset by name (case-insensitive), or null when not found.</summary>
        public ThemePreset? FindByName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            return presets.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Resolves a persisted theme name to one of the two current presets without crashing.
        /// Current names ("Light"/"Dark", case-insensitive) map directly. Legacy names from the
        /// removed 5-preset system are bucketed by their old light/dark intent so a user who had
        /// "Midnight" lands on Dark and "Warm Paper" lands on Light. Unknown/blank defaults to Light.
        /// </summary>
        public ThemePreset? ResolveThemeName(string? persistedName)
        {
            if (!string.IsNullOrWhiteSpace(persistedName))
            {
                ThemePreset? direct = FindByName(persistedName);
                if (direct != null)
                {
                    return direct;
                }

                // Legacy 5-preset names → bucket by their old light/dark intent.
                bool legacyWasLight = persistedName switch
                {
                    "Editorial Light" => true,
                    "Warm Paper" => true,
                    "Graphite" => false,
                    "Stone" => true,
                    "Midnight" => false,
                    _ => true // unknown → conservative default: Light
                };
                return legacyWasLight ? Default : presets[1]; // presets[1] == Dark
            }
            return null;
        }

        private void InitializePresets()
        {
            // Exactly two modes, per product decision (Part 3). §14 supersedes the earlier
            // monochrome identity: this is now the "Notion / Windows Calendar hybrid" — a warm
            // near-black on dark, warm off-white on light, and one Fluent-style blue accent used
            // for the primary action, the selected nav pill, and the calendar "today" circle.

            // 1. Light (Default) — Notion hybrid: white page, subtle gray sidebar/cards, blue accent.
            presets.Add(new ThemePreset
            {
                Name = "Light",
                PrimaryBg = Color.FromArgb(0xFF, 0xFF, 0xFF),      // #FFFFFF page
                SidebarBg = Color.FromArgb(0xF7, 0xF7, 0xF5),      // #F7F7F5 Notion sidebar gray
                SecondaryBg = Color.FromArgb(0xF7, 0xF7, 0xF5),    // #F7F7F5 raised surface
                CardBgColor = Color.FromArgb(0xF7, 0xF7, 0xF5),    // #F7F7F5 cards
                CardHoverBg = Color.FromArgb(0xEF, 0xEF, 0xED),    // #EFEFED hover
                AccentColor = Color.FromArgb(0x0F, 0x6C, 0xBD),    // #0F6CBD Fluent blue
                AccentTintColor = Color.FromArgb(0x1A, 0x0F, 0x6C, 0xBD), // 10% blue wash
                TextColor = Color.FromArgb(0x37, 0x35, 0x2F),      // #37352F Notion ink
                MutedTextColor = Color.FromArgb(0x78, 0x77, 0x74), // #787774
                BorderColor = Color.FromArgb(0xE9, 0xE9, 0xE7),    // #E9E9E7 hairline
                IsLight = true,
                SurfaceElevated = Color.FromArgb(0xFF, 0xFF, 0xFF),
                PressedColor = Color.FromArgb(0xE2, 0xE0, 0xDC),
                SelectedColor = Color.FromArgb(0x0F, 0x6C, 0xBD),
                SelectedTextColor = Color.FromArgb(0xFF, 0xFF, 0xFF),
                SuccessColor = VibeAlarmPalette.Success,
                WarningColor = VibeAlarmPalette.Warning,
                ErrorColor = VibeAlarmPalette.Error
            });

            // 2. Dark — Notion hybrid: warm near-black page, raised surfaces one step lighter, blue accent.
            presets.Add(new ThemePreset
            {
                Name = "Dark",
                PrimaryBg = Color.FromArgb(0x19, 0x19, 0x19),      // #191919 Notion dark page
                SidebarBg = Color.FromArgb(0x20, 0x20, 0x20),      // #202020 raised sidebar
                SecondaryBg = Color.FromArgb(0x20, 0x20, 0x20),    // #202020 raised surface
                CardBgColor = Color.FromArgb(0x20, 0x20, 0x20),    // #202020 cards
                CardHoverBg = Color.FromArgb(0x2A, 0x2A, 0x2A),    // #2A2A2A hover
                AccentColor = Color.FromArgb(0x47, 0x9E, 0xF5),    // #479EF5 Fluent blue
                AccentTintColor = Color.FromArgb(0x1A, 0x47, 0x9E, 0xF5), // 10% blue wash
                TextColor = Color.FromArgb(0xE9, 0xE9, 0xE7),      // #E9E9E7
                MutedTextColor = Color.FromArgb(0x9B, 0x9B, 0x99), // #9B9B99
                BorderColor = Color.FromArgb(0x2F, 0x2F, 0x2F),    // #2F2F2F hairline
                IsLight = false,
                SurfaceElevated = Color.FromArgb(0x28, 0x28, 0x28),
                PressedColor = Color.FromArgb(0x14, 0x14, 0x14),
                SelectedColor = Color.FromArgb(0x47, 0x9E, 0xF5),
                SelectedTextColor = Color.FromArgb(0xFF, 0xFF, 0xFF),
                SuccessColor = VibeAlarmPalette.Success,
                WarningColor = VibeAlarmPalette.Warning,
                ErrorColor = VibeAlarmPalette.Error
            });
        }
    }
}
