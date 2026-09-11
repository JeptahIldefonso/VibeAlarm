using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using VibeAlarm.Services;
using Xunit;

namespace VibeAlarm.Tests
{
    /// <summary>
    /// Locks the background layer's render contract: the persisted image is re-rendered as an
    /// ATMOSPHERIC layer — capped resolution (a 4K source serving a blur is wasted memory),
    /// blurred, and carrying the user's opacity as a global alpha so it blends over the
    /// theme-colored form background. All tests pass an explicit path so they never touch the
    /// user's real persisted background.png.
    /// </summary>
    public class BackgroundServiceTests : IDisposable
    {
        private readonly string tempImage;

        public BackgroundServiceTests()
        {
            // 3200x1800 source: larger than the 1600px cap on the long edge, with a
            // high-contrast checker pattern so any blur pass has real detail to soften.
            tempImage = Path.Combine(Path.GetTempPath(), $"vibealarm-test-bg-{Guid.NewGuid():N}.png");
            using var source = new Bitmap(3200, 1800);
            using (var g = Graphics.FromImage(source))
            {
                using var black = new SolidBrush(Color.Black);
                using var white = new SolidBrush(Color.White);
                for (int y = 0; y < 1800; y += 100)
                {
                    for (int x = 0; x < 3200; x += 100)
                    {
                        g.FillRectangle((x + y) % 200 == 0 ? white : black, x, y, 100, 100);
                    }
                }
            }
            source.Save(tempImage, ImageFormat.Png);
        }

        public void Dispose()
        {
            try { File.Delete(tempImage); } catch { /* temp cleanup is best-effort */ }
        }

        [Fact]
        public void LoadBackground_caps_rendered_resolution()
        {
            using Bitmap? bg = BackgroundService.LoadBackground(applyMonochromeFilter: false, opacity: 1.0, path: tempImage);

            Assert.NotNull(bg);
            // Long edge capped at 1600, aspect ratio preserved (3200:1800 -> 1600:900).
            Assert.True(Math.Max(bg!.Width, bg.Height) <= 1600,
                $"Expected long edge <= 1600, got {bg.Width}x{bg.Height}.");
            Assert.InRange((double)bg.Width / bg.Height, 3200.0 / 1801.0, 3200.0 / 1799.0);
        }

        [Fact]
        public void LoadBackground_applies_user_opacity_as_global_alpha()
        {
            // 0.5 opacity must land near 128 alpha (allow rounding slack), sampled at the
            // center where interpolation can't blend in an edge pixel.
            using Bitmap? bg = BackgroundService.LoadBackground(applyMonochromeFilter: false, opacity: 0.5, path: tempImage);

            Assert.NotNull(bg);
            Color center = bg!.GetPixel(bg.Width / 2, bg.Height / 2);
            Assert.InRange(center.A, 125, 131);
        }

        [Fact]
        public void LoadBackground_full_opacity_is_opaque()
        {
            using Bitmap? bg = BackgroundService.LoadBackground(applyMonochromeFilter: false, opacity: 1.0, path: tempImage);

            Assert.NotNull(bg);
            Color center = bg!.GetPixel(bg.Width / 2, bg.Height / 2);
            Assert.Equal(255, center.A);
        }

        [Fact]
        public void LoadBackground_returns_null_for_a_missing_file()
        {
            string missing = Path.Combine(Path.GetTempPath(), $"vibealarm-missing-{Guid.NewGuid():N}.png");

            Assert.Null(BackgroundService.LoadBackground(applyMonochromeFilter: false, opacity: 1.0, path: missing));
        }
    }
}
