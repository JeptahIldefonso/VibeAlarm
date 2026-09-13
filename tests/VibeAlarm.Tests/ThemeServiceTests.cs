using VibeAlarm.Services;
using VibeAlarm.UI.Theming;
using Xunit;

namespace VibeAlarm.Tests
{
    /// <summary>
    /// The accent-only theme service: the base palette is a fixed constant (exact
    /// #121212/#000000 values, never retinted), and ApplyAccent publishes the selected
    /// accent's Base/Hover/OnAccent onto the shared palette state — with unknown-key
    /// tolerance and idempotent re-application.
    ///
    /// Collection: this class and <see cref="UIControlFactoryTests"/> both touch the
    /// ThemeService.Shared singleton (palette state via ApplyAccent), so they must not
    /// run in parallel with each other.
    /// </summary>
    [Collection("ThemeService")]
    public class ThemeServiceTests
    {
        [Fact]
        public void A_fresh_service_starts_on_the_shipped_default_accent()
        {
            var service = new ThemeService();
            Assert.Equal(VibeAlarmPalette.Accent, AccentCatalog.Default.Base);
            Assert.Equal(VibeAlarmPalette.AccentHover, AccentCatalog.Default.Hover);
            Assert.Equal(VibeAlarmPalette.OnAccent, AccentCatalog.Default.OnAccent);
        }

        [Fact]
        public void The_base_palette_is_the_fixed_spec_exactly()
        {
            // The locked color system: near-black content area, pure-black chrome, soft-white
            // primary ink, #B3B3B3 muted ink, fixed destructive red. These NEVER move with
            // the accent.
            Assert.Equal(System.Drawing.Color.FromArgb(0x12, 0x12, 0x12), VibeAlarmPalette.Surface);
            Assert.Equal(System.Drawing.Color.FromArgb(0x00, 0x00, 0x00), VibeAlarmPalette.Chrome);
            Assert.Equal(System.Drawing.Color.FromArgb(0xF3, 0xF3, 0xF3), VibeAlarmPalette.Text);
            Assert.Equal(System.Drawing.Color.FromArgb(0xB3, 0xB3, 0xB3), VibeAlarmPalette.Muted);
            Assert.Equal(System.Drawing.Color.FromArgb(0xEF, 0x44, 0x44), VibeAlarmPalette.Error);
        }

        [Fact]
        public void ApplyAccent_publishes_the_accent_triple_onto_the_shared_palette()
        {
            var service = ThemeService.Shared;
            try
            {
                AccentOption applied = service.ApplyAccent("Violet System");
                Assert.Equal("Violet System", applied.Key);

                Assert.Equal(applied.Base, VibeAlarmPalette.Accent);
                Assert.Equal(applied.Hover, VibeAlarmPalette.AccentHover);
                Assert.Equal(applied.OnAccent, VibeAlarmPalette.OnAccent);
                // The tint is a fixed-alpha wash of whatever accent is live (10%).
                Assert.Equal(System.Drawing.Color.FromArgb(0x1A, applied.Base), VibeAlarmPalette.AccentTint);
            }
            finally
            {
                // Restore the shipped default for every other test in this collection.
                service.ApplyAccent(AccentCatalog.DefaultKey);
            }
        }

        [Fact]
        public void ApplyAccent_never_touches_the_fixed_base_palette()
        {
            var service = ThemeService.Shared;
            try
            {
                service.ApplyAccent("Pearl White");
                // Assignments only — the base palette is a constant, not a blend target.
                Assert.Equal(System.Drawing.Color.FromArgb(0x12, 0x12, 0x12), VibeAlarmPalette.Surface);
                Assert.Equal(System.Drawing.Color.FromArgb(0x00, 0x00, 0x00), VibeAlarmPalette.Chrome);
                Assert.Equal(System.Drawing.Color.FromArgb(0xF3, 0xF3, 0xF3), VibeAlarmPalette.Text);
                Assert.Equal(System.Drawing.Color.FromArgb(0xB3, 0xB3, 0xB3), VibeAlarmPalette.Muted);
            }
            finally
            {
                service.ApplyAccent(AccentCatalog.DefaultKey);
            }
        }

        [Fact]
        public void ApplyAccent_is_idempotent_across_repeated_and_alternating_applications()
        {
            var service = ThemeService.Shared;
            try
            {
                service.ApplyAccent("Sunset Red");
                service.ApplyAccent("Neo Blue");
                service.ApplyAccent("Neo Blue");
                Assert.Equal(AccentCatalog.Resolve("Neo Blue").Base, VibeAlarmPalette.Accent);

                service.ApplyAccent("Solar Amber");
                service.ApplyAccent("Neo Blue");
                Assert.Equal(AccentCatalog.Resolve("Neo Blue").Base, VibeAlarmPalette.Accent);
            }
            finally
            {
                service.ApplyAccent(AccentCatalog.DefaultKey);
            }
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not-a-real-accent")]
        public void ApplyAccent_unknown_keys_fall_back_to_the_default(string? key)
        {
            var service = ThemeService.Shared;
            try
            {
                AccentOption applied = service.ApplyAccent(key);
                Assert.Equal(AccentCatalog.DefaultKey, applied.Key);
                Assert.Equal(System.Drawing.Color.FromArgb(0x22, 0xC5, 0x5E), VibeAlarmPalette.Accent); // Matrix Green
            }
            finally
            {
                service.ApplyAccent(AccentCatalog.DefaultKey);
            }
        }
    }
}
