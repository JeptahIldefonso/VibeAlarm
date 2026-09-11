using System.Drawing;
using VibeAlarm.Services;
using VibeAlarm.UI.Theming;
using Xunit;

namespace VibeAlarm.Tests
{
    /// <summary>
    /// §23–24 transparency semantics: 0% = completely opaque, 100% = maximum transparency
    /// (clamped to a per-surface readability floor), RGB channels are never altered, and the
    /// effective token getters flow through the same single math.
    /// </summary>
    public class GlassSurfaceTests
    {
        [Theory]
        [InlineData(0, 255)]
        [InlineData(50, 128)]
        [InlineData(100, 0)]
        public void AlphaFromPercent_maps_zero_to_opaque(int pct, int expectedRawAlpha)
        {
            // With no floor (minAlpha 0) the raw mapping is exactly 255 - 255*pct/100.
            Assert.Equal(expectedRawAlpha, GlassSurface.AlphaFromPercent(pct, 0));
        }

        [Fact]
        public void AlphaFromPercent_never_drops_below_the_surface_minimum()
        {
            // Even at 100% transparency the visual alpha stays at the readability floor.
            Assert.Equal(GlassSurface.CardMinAlpha, GlassSurface.AlphaFromPercent(100, GlassSurface.CardMinAlpha));
            Assert.Equal(GlassSurface.PanelMinAlpha, GlassSurface.AlphaFromPercent(100, GlassSurface.PanelMinAlpha));
            Assert.Equal(GlassSurface.BorderMinAlpha, GlassSurface.AlphaFromPercent(100, GlassSurface.BorderMinAlpha));
            Assert.Equal(GlassSurface.SidebarMinAlpha, GlassSurface.AlphaFromPercent(100, GlassSurface.SidebarMinAlpha));
        }

        [Fact]
        public void AlphaFromPercent_is_monotonically_decreasing_and_clamped()
        {
            int previous = 255;
            for (int pct = 0; pct <= 100; pct += 5)
            {
                int alpha = GlassSurface.AlphaFromPercent(pct, 64);
                Assert.True(alpha <= previous, "More transparency must never raise the alpha.");
                Assert.InRange(alpha, 64, 255);
                previous = alpha;
            }
        }

        [Theory]
        [InlineData(-20)]
        [InlineData(150)]
        public void AlphaFromPercent_forgives_out_of_range_input(int pct)
        {
            Assert.InRange(GlassSurface.AlphaFromPercent(pct, 64), 64, 255);
        }

        [Fact]
        public void Apply_preserves_rgb_and_only_changes_alpha()
        {
            Color baseColor = Color.FromArgb(0x20, 0x20, 0x20);
            Color applied = GlassSurface.Apply(baseColor, 40, 51);
            Assert.Equal(baseColor.R, applied.R);
            Assert.Equal(baseColor.G, applied.G);
            Assert.Equal(baseColor.B, applied.B);
            Assert.Equal(GlassSurface.AlphaFromPercent(40, 51), applied.A);
        }

        [Fact]
        public void Effective_tokens_use_the_locked_design_percentages()
        {
            // The former Settings sliders are gone — these alphas are now design constants.
            // Cards sit opaque on the background, borders stay quiet, panels are barely
            // glassy, and the sidebar is the one true glass surface.
            var preset = ThemeService.Shared.FindByName("Dark")!;

            Assert.Equal(255, GlassSurface.CardFill(preset).A);
            Assert.Equal(GlassSurface.AlphaFromPercent(27, GlassSurface.BorderMinAlpha), GlassSurface.CardBorder(preset).A);
            Assert.Equal(GlassSurface.AlphaFromPercent(8, GlassSurface.PanelMinAlpha), GlassSurface.PanelFill(preset).A);
            Assert.Equal(GlassSurface.SidebarMinAlpha, GlassSurface.SidebarFill(preset).A);

            // RGB channels always come from the preset untouched.
            Assert.Equal(Color.FromArgb(255, preset.CardBgColor), GlassSurface.CardFill(preset));
            Assert.Equal(Color.FromArgb(GlassSurface.SidebarMinAlpha, preset.SidebarBg), GlassSurface.SidebarFill(preset));
        }

        [Fact]
        public void Hover_fills_stay_opaque_at_any_transparency()
        {
            var preset = ThemeService.Shared.FindByName("Dark")!;
            Assert.Equal(255, GlassSurface.CardHover(preset).A);
        }

        [Fact]
        public void Task_card_glass_is_a_separate_surface_from_the_shared_card_token()
        {
            var preset = ThemeService.Shared.FindByName("Dark")!;

            // The SHARED card token stays fully opaque — Dashboard and Settings section cards
            // depend on that and must not regress when task rows go glassy.
            Assert.Equal(255, GlassSurface.CardFill(preset).A);

            // Task rows get their own tinted-glass surface: translucent (not opaque), floored
            // for text readability, RGB from the preset untouched.
            Color task = GlassSurface.TaskCardFill(preset);
            Assert.Equal(GlassSurface.AlphaFromPercent(35, GlassSurface.TaskCardMinAlpha), task.A);
            Assert.InRange(task.A, GlassSurface.TaskCardMinAlpha, 255);
            Assert.Equal((Color.FromArgb(255, preset.CardBgColor)).R, task.R);
        }
    }
}
