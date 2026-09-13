using System.Drawing;
using VibeAlarm.UI.Theming;
using Xunit;

namespace VibeAlarm.Tests
{
    /// <summary>
    /// The card-container spec (§23–24 evolved): every card surface is a WHITE overlay over
    /// the dark backdrop at locked alphas — fill ~5.5%, border ~9%, hover ~8%/~14% — computed
    /// in ONE place. The app chrome (sidebar, header band, bottom strip) is opaque black
    /// (<see cref="VibeAlarmPalette.Chrome"/>, Spotify-style) and is covered by
    /// ThemeServiceTests, not here — only the translucent card containers carry alpha.
    /// </summary>
    public class GlassSurfaceTests
    {
        [Fact]
        public void Card_tokens_are_white_overlays_at_the_spec_alphas()
        {
            // The card container: fill ~5.5% white (#0EFFFFFF), border ~9% (#17FFFFFF) —
            // subtle definition, not a hard line. RGB is pure white (never a per-call hue)
            // so every card on the dark backdrop reads as one family.
            Assert.Equal(Color.FromArgb(14, 255, 255, 255), GlassSurface.CardFill());
            Assert.Equal(Color.FromArgb(23, 255, 255, 255), GlassSurface.CardBorder());

            // Section panels share the family, a hair more present (~6%).
            Assert.Equal(Color.FromArgb(15, 255, 255, 255), GlassSurface.PanelFill());
            Assert.Equal(GlassSurface.CardBorder(), GlassSurface.PanelBorder());
            Assert.Equal(GlassSurface.PanelFill(), GlassSurface.SectionFill());

            // Task rows share the SAME card token — one card style across every list.
            Assert.Equal(GlassSurface.CardFill(), GlassSurface.TaskCardFill());
        }

        [Fact]
        public void Hover_steps_fill_and_border_up_translucently()
        {
            // Hover must read clearly over the resting tint WITHOUT flashing a solid block
            // over the surface behind the card: fill ~8% vs the resting ~5.5%, border ~14%
            // vs the resting ~9% — both steps, both still translucent.
            Color hover = GlassSurface.CardHover();
            Color hoverBorder = GlassSurface.CardHoverBorder();

            Assert.Equal(Color.FromArgb(20, 255, 255, 255), hover);
            Assert.Equal(Color.FromArgb(36, 255, 255, 255), hoverBorder);
            Assert.True(hover.A > GlassSurface.CardFill().A,
                "Hover fill must be more present than the resting fill.");
            Assert.True(hoverBorder.A > GlassSurface.CardBorder().A,
                "Hover border must sharpen the resting border.");
            Assert.True(hover.A < 255 && hoverBorder.A < 255, "Hover must not be opaque.");
        }
    }
}
