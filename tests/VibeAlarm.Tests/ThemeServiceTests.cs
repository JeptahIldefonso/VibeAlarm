using System;
using System.Linq;
using VibeAlarm.Models;
using VibeAlarm.Services;
using Xunit;

namespace VibeAlarm.Tests
{
    /// <summary>
    /// Part 3 consolidation: exactly two theme modes (Light/Dark), correct token values, and
    /// safe migration of legacy (removed 5-preset) theme names persisted in old settings.json.
    /// </summary>
    public class ThemeServiceTests
    {
        [Fact]
        public void Presets_contains_exactly_two_entries()
        {
            var service = ThemeService.Shared;
            Assert.Equal(2, service.Presets.Count);
            Assert.Equal(new[] { "Light", "Dark" }, service.Presets.Select(t => t.Name).ToArray());
        }

        [Fact]
        public void Light_preset_uses_the_specified_palette()
        {
            var light = ThemeService.Shared.FindByName("Light")!;
            Assert.True(light.IsLight);
            Assert.Equal(Color(0xE5, 0xE5, 0xE5), light.PrimaryBg);
            Assert.Equal(Color(0xE5, 0xE5, 0xE5), light.CardBgColor);
            Assert.Equal(Color(0xDA, 0xDA, 0xDA), light.CardHoverBg);
            Assert.Equal(Color(0x17, 0x17, 0x17), light.TextColor);
            Assert.Equal(Color(0x73, 0x73, 0x73), light.MutedTextColor);
            Assert.Equal(Color(0xA3, 0xA3, 0xA3), light.BorderColor);
            Assert.Equal(Color(0x00, 0x00, 0x00), light.AccentColor);
        }

        [Fact]
        public void Light_card_background_matches_primary_background()
        {
            // Part 4: cards are separated by a hairline border, not a fill difference.
            // CardBgColor must equal PrimaryBg and hover must be one step darker — never a white swap.
            var light = ThemeService.Shared.FindByName("Light")!;
            Assert.Equal(light.PrimaryBg, light.CardBgColor);
            Assert.Equal(light.SecondaryBg, light.CardBgColor);
        }

        [Fact]
        public void Dark_preset_uses_the_specified_palette()
        {
            var dark = ThemeService.Shared.FindByName("Dark")!;
            Assert.False(dark.IsLight);
            Assert.Equal(Color(0x18, 0x19, 0x1A), dark.PrimaryBg);
            Assert.Equal(Color(0x24, 0x25, 0x26), dark.CardBgColor);
            Assert.Equal(Color(0x3A, 0x3B, 0x3C), dark.CardHoverBg);
            Assert.Equal(Color(0xE4, 0xE6, 0xEB), dark.TextColor);
            Assert.Equal(Color(0xB0, 0xB3, 0xB8), dark.MutedTextColor);
            Assert.Equal(Color(0x3A, 0x3B, 0x3C), dark.BorderColor);
            Assert.Equal(Color(0xFF, 0xFF, 0xFF), dark.AccentColor);
        }

        [Theory]
        [InlineData("Midnight", "Dark")]      // legacy dark
        [InlineData("Graphite", "Dark")]      // legacy dark
        [InlineData("Warm Paper", "Light")]   // legacy light
        [InlineData("Editorial Light", "Light")] // legacy light
        [InlineData("Stone", "Light")]        // legacy light
        public void Legacy_theme_names_bucket_to_the_sensible_mode(string legacyName, string expected)
        {
            var service = ThemeService.Shared;
            ThemePreset resolved = service.ResolveThemeName(legacyName) ?? service.Default;
            Assert.Equal(expected, resolved.Name);
        }

        [Fact]
        public void Unknown_or_blank_theme_name_defaults_to_light()
        {
            var service = ThemeService.Shared;
            Assert.Equal("Light", (service.ResolveThemeName("SomeFutureTheme") ?? service.Default).Name);
            Assert.Equal("Light", (service.ResolveThemeName(null) ?? service.Default).Name);
            Assert.Equal("Light", (service.ResolveThemeName("") ?? service.Default).Name);
        }

        [Fact]
        public void Current_names_resolve_directly()
        {
            var service = ThemeService.Shared;
            Assert.Equal("Light", (service.ResolveThemeName("light") ?? service.Default).Name);
            Assert.Equal("Dark", (service.ResolveThemeName("Dark") ?? service.Default).Name);
        }

        private static System.Drawing.Color Color(int r, int g, int b)
            => System.Drawing.Color.FromArgb(r, g, b);
    }
}