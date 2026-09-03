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
    public sealed class ThemeService
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
            // Exactly two modes, per product decision (Part 3). Light is grounded in the reference
            // portfolio site; Dark is a neutral/near-grayscale adaptation (Facebook/Google-style
            // dark values, kept strictly monochrome). The palette stays monochrome and editorial;
            // these are tonal/contrast modes of the same design system, not colored themes.

            // 1. Light (Default)
            presets.Add(new ThemePreset
            {
                Name = "Light",
                PrimaryBg = Color.FromArgb(0xE5, 0xE5, 0xE5),      // #E5E5E5 (Tailwind neutral-200) lighter ground
                SidebarBg = Color.FromArgb(0xE5, 0xE5, 0xE5),      // same ground tone
                SecondaryBg = Color.FromArgb(0xE5, 0xE5, 0xE5),    // card surface = ground, no fill differentiation
                CardBgColor = Color.FromArgb(0xE5, 0xE5, 0xE5),    // == PrimaryBg: cards separated by hairline, not fill
                CardHoverBg = Color.FromArgb(0xDA, 0xDA, 0xDA),    // #DADADA one step darker hover, never a white swap
                AccentColor = Color.FromArgb(0, 0, 0),             // #000000 pure black accent
                TextColor = Color.FromArgb(0x17, 0x17, 0x17),      // #171717 (Tailwind neutral-900) near-black ink
                MutedTextColor = Color.FromArgb(0x73, 0x73, 0x73), // #737373 (Tailwind neutral-500) secondary/labels
                BorderColor = Color.FromArgb(0xA3, 0xA3, 0xA3),    // #A3A3A3 (Tailwind neutral-400) reads on lighter bg
                IsLight = true,
                SurfaceElevated = Color.FromArgb(0xE0, 0xE0, 0xE0),
                PressedColor = Color.FromArgb(0xCF, 0xCF, 0xCF),
                SelectedColor = Color.FromArgb(0, 0, 0),
                SelectedTextColor = Color.FromArgb(0xE5, 0xE5, 0xE5),
                SuccessColor = VibeAlarmPalette.Success,
                WarningColor = VibeAlarmPalette.Warning,
                ErrorColor = VibeAlarmPalette.Error
            });

            // 2. Dark (Facebook/Google-style neutral dark)
            presets.Add(new ThemePreset
            {
                Name = "Dark",
                PrimaryBg = Color.FromArgb(0x18, 0x19, 0x1A),      // #18191A
                SidebarBg = Color.FromArgb(0x18, 0x19, 0x1A),      // same ground tone
                SecondaryBg = Color.FromArgb(0x24, 0x25, 0x26),    // #242526 card surface
                CardBgColor = Color.FromArgb(0x24, 0x25, 0x26),    // #242526
                CardHoverBg = Color.FromArgb(0x3A, 0x3B, 0x3C),    // #3A3B3C hover
                AccentColor = Color.FromArgb(0xFF, 0xFF, 0xFF),    // #FFFFFF neutral accent (no blue)
                TextColor = Color.FromArgb(0xE4, 0xE6, 0xEB),      // #E4E6EB primary text
                MutedTextColor = Color.FromArgb(0xB0, 0xB3, 0xB8), // #B0B3B8 secondary text
                BorderColor = Color.FromArgb(0x3A, 0x3B, 0x3C),    // #3A3B3C border (matches hover)
                IsLight = false,
                SurfaceElevated = Color.FromArgb(0x32, 0x33, 0x35),
                PressedColor = Color.FromArgb(0x4B, 0x4D, 0x4F),
                SelectedColor = Color.FromArgb(0xFF, 0xFF, 0xFF),
                SelectedTextColor = Color.FromArgb(0x18, 0x19, 0x1A),
                SuccessColor = VibeAlarmPalette.Success,
                WarningColor = VibeAlarmPalette.Warning,
                ErrorColor = VibeAlarmPalette.Error
            });
        }
    }
}
