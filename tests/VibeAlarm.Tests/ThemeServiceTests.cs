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
            Assert.Equal(Color(0xFF, 0xFF, 0xFF), light.PrimaryBg);
            Assert.Equal(Color(0xF7, 0xF7, 0xF5), light.CardBgColor);
            Assert.Equal(Color(0xEF, 0xEF, 0xED), light.CardHoverBg);
            Assert.Equal(Color(0x37, 0x35, 0x2F), light.TextColor);
            Assert.Equal(Color(0x78, 0x77, 0x74), light.MutedTextColor);
            Assert.Equal(Color(0xE9, 0xE9, 0xE7), light.BorderColor);
            Assert.Equal(Color(0x0F, 0x6C, 0xBD), light.AccentColor);
            Assert.Equal(System.Drawing.Color.FromArgb(0x1A, 0x0F, 0x6C, 0xBD), light.AccentTintColor);
        }

        [Fact]
        public void Light_cards_lift_off_the_page_background()
        {
            // Frontend plan §10.7: cards must read as "raised" against the page — a deliberate lift,
            // sealed with a hairline border. §14 light uses a subtle Notion-gray card over white.
            var light = ThemeService.Shared.FindByName("Light")!;
            Assert.NotEqual(light.PrimaryBg, light.CardBgColor);
            Assert.Equal(light.SecondaryBg, light.CardBgColor);
        }

        [Fact]
        public void Dark_preset_uses_the_specified_palette()
        {
            var dark = ThemeService.Shared.FindByName("Dark")!;
            Assert.False(dark.IsLight);
            Assert.Equal(Color(0x19, 0x19, 0x19), dark.PrimaryBg);
            Assert.Equal(Color(0x20, 0x20, 0x20), dark.CardBgColor);
            Assert.Equal(Color(0x2A, 0x2A, 0x2A), dark.CardHoverBg);
            Assert.Equal(Color(0xE9, 0xE9, 0xE7), dark.TextColor);
            Assert.Equal(Color(0x9B, 0x9B, 0x99), dark.MutedTextColor);
            Assert.Equal(Color(0x2F, 0x2F, 0x2F), dark.BorderColor);
            Assert.Equal(Color(0x47, 0x9E, 0xF5), dark.AccentColor);
            Assert.Equal(System.Drawing.Color.FromArgb(0x1A, 0x47, 0x9E, 0xF5), dark.AccentTintColor);
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