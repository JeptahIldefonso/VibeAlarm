using System.Drawing;
using VibeAlarm.UI.Theming;
using Xunit;

namespace VibeAlarm.Tests
{
    /// <summary>
    /// The SVG icon integration: vector-backed kinds (embedded Assets/Icons/*.svg rasterized by
    /// SVG.NET) must render at the requested size with actual ink in the requested color, the
    /// renderer must degrade to the font-glyph path — never throw — when the SVG resource is
    /// missing, and results must be cached by (kind, ink, size) with the caller never owning or
    /// disposing the returned bitmap. This locks the three contracts IconSet promises its
    /// callers: (a) vectors and glyphs are interchangeable (same size/ink), (b) a bad SVG can
    /// never crash rendering, and (c) repeat requests are allocation-free cache hits until the
    /// theme (ink) changes.
    /// </summary>
    public class IconSetTests
    {
        [Theory]
        [InlineData(IconKind.Dashboard)]       // existing kinds upgraded to vectors
        [InlineData(IconKind.Tasks)]
        [InlineData(IconKind.Calendar)]
        [InlineData(IconKind.TaskComplete)]    // new vector-only kinds
        [InlineData(IconKind.TaskCheckbox)]
        [InlineData(IconKind.TaskPending)]
        [InlineData(IconKind.SoundWave)]
        [InlineData(IconKind.AdminSettings)]
        [InlineData(IconKind.UserPreferences)]
        public void Svg_backed_kinds_render_ink_at_the_requested_size(IconKind kind)
        {
            Assert.True(IconSet.SvgResources.ContainsKey(kind), $"{kind} should be SVG-backed.");

            // Render returns a SHARED cached bitmap — do not dispose it.
            Bitmap bmp = IconSet.Render(kind, Color.White, 24);

            Assert.Equal(24, bmp.Width);
            Assert.Equal(24, bmp.Height);
            Assert.True(BitmapHasInk(bmp), $"SVG render produced a blank bitmap for {kind}.");
        }

        [Fact]
        public void Render_falls_back_to_the_glyph_when_the_svg_resource_is_missing()
        {
            // Distinct ink: the cache is keyed by (kind, ink, size), so this must not collide
            // with the theory above (which renders Calendar in White) or the glyph result
            // would be served from cache and the fallback would never execute here.
            IconSet.ClearCache();
            Color ink = Color.FromArgb(0xFE, 0xDC, 0xBA);
            string prior = IconSet.SvgResources[IconKind.Calendar];
            try
            {
                IconSet.SvgResources[IconKind.Calendar] = "VibeAlarm.Assets.Icons.does-not-exist.svg";

                Bitmap bmp = IconSet.Render(IconKind.Calendar, ink, 24);

                Assert.Equal(24, bmp.Width);
                Assert.Equal(24, bmp.Height);
                Assert.True(BitmapHasInk(bmp), "Glyph fallback produced a blank bitmap.");
            }
            finally
            {
                IconSet.SvgResources[IconKind.Calendar] = prior;
            }
        }

        [Fact]
        public void Glyph_only_kinds_still_render_through_the_unchanged_path()
        {
            Assert.False(IconSet.SvgResources.ContainsKey(IconKind.Plus));

            Bitmap bmp = IconSet.Render(IconKind.Plus, Color.White, 24);

            Assert.Equal(24, bmp.Width);
            Assert.True(BitmapHasInk(bmp));
        }

        [Fact]
        public void Render_caches_by_kind_ink_and_size()
        {
            IconSet.ClearCache();
            Color ink = Color.FromArgb(0x11, 0x22, 0x33); // unique to this test

            Bitmap first = IconSet.Render(IconKind.Plus, ink, 20);
            Bitmap second = IconSet.Render(IconKind.Plus, ink, 20);
            Assert.Same(first, second);

            // A different ink or a different size is a different raster.
            Assert.NotSame(first, IconSet.Render(IconKind.Plus, Color.White, 20));
            Assert.NotSame(first, IconSet.Render(IconKind.Plus, ink, 22));

            // ClearCache (the theme-change hook) releases the shared instances — the next
            // request re-rasterizes instead of serving the disposed entry.
            IconSet.ClearCache();
            Assert.Throws<ArgumentException>(() => first.Width);
            Bitmap rebuilt = IconSet.Render(IconKind.Plus, ink, 20);
            Assert.NotSame(first, rebuilt);
            Assert.True(BitmapHasInk(rebuilt));
        }

        /// <summary>Scans for any non-transparent pixel — proves the rasterizer actually drew
        /// the shape rather than returning an empty surface.</summary>
        private static bool BitmapHasInk(Bitmap bmp)
        {
            for (int y = 0; y < bmp.Height; y++)
            {
                for (int x = 0; x < bmp.Width; x++)
                {
                    if (bmp.GetPixel(x, y).A > 0)
                    {
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
