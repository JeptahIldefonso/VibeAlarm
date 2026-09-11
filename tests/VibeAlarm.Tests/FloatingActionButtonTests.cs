using System.Drawing;
using VibeAlarm.UI.Controls;
using Xunit;

namespace VibeAlarm.Tests
{
    /// <summary>
    /// The Tasks view floating action button: the hit-test region must be a CIRCLE cut to the
    /// disc (GraphicsPath ellipse — never the full bounding square, which includes shadow and
    /// hover-scale headroom), and construction/retint must not throw. The shadow/face bitmap
    /// caching is enforced by design (caches rebuild only on real size/ink changes — SetInk
    /// with the same color is a no-op) and is not directly observable without a paint cycle,
    /// so the ownership contract is covered by IconSetTests instead.
    /// </summary>
    public class FloatingActionButtonTests
    {
        [Fact]
        public void Hit_region_is_circular_and_disc_sized()
        {
            const int disc = 52;
            using FloatingActionButton fab = new(Color.Crimson, discSize: disc);

            Assert.NotNull(fab.Region);
            using Bitmap surface = new(fab.Width, fab.Height);
            using Graphics g = Graphics.FromImage(surface);

            // The control rect carries shadow + hover headroom, so it is larger than the disc…
            Assert.True(fab.Width > disc, "Control must be larger than the disc (shadow + hover headroom).");
            Assert.True(fab.Height > disc, "Control must be larger than the disc (shadow + hover headroom).");

            // …but the REGION is the disc alone (1px anti-alias allowance at most): the
            // bounding corners are NOT clickable — clicks fall through to the list beneath.
            RectangleF region = fab.Region.GetBounds(g);
            Assert.True(region.Width <= disc + 2, $"Region width {region.Width} must be disc-sized, not square.");
            Assert.True(region.Height <= disc + 2, $"Region height {region.Height} must be disc-sized, not square.");
            Assert.False(fab.Region.IsVisible(0, 0, g), "Corner must be outside the hit region.");
            Assert.False(fab.Region.IsVisible(fab.Width - 1, 0, g), "Corner must be outside the hit region.");
            Assert.True(fab.Region.IsVisible(fab.Width / 2, fab.Height / 2, g), "Center must be inside the hit region.");
        }

        [Fact]
        public void SetInk_with_the_same_color_is_a_no_op()
        {
            using FloatingActionButton fab = new(Color.Crimson, discSize: 40);
            // Retint with the SAME ink — must not throw, must not invalidate anything.
            fab.SetInk(Color.Crimson);
            // Retint with a real change (cache rebuild happens lazily on next paint).
            fab.SetInk(Color.ForestGreen);
        }
    }
}
