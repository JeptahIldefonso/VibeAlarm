using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace VibeAlarm.Services
{
    /// <summary>
    /// Manages the user's custom background image: copies the selected file into a dedicated
    /// app-data folder for durability across restarts, and can produce a darkened + optionally
    /// desaturated variant so text stays legible and the "strictly monochrome" design rule is
    /// honored without silently fighting the user (grayscale is a toggle).
    /// </summary>
    public static class BackgroundService
    {
        private const string BackdropDirName = "VibeAlarm\\backgrounds";

        /// <summary>Where the app keeps its copy of the active background image.</summary>
        public static string BackgroundsDirectory
        {
            get
            {
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string dir = Path.Combine(local, BackdropDirName);
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        /// <summary>Persisted copy of the background image ("" when none is set).</summary>
        public static string StoredPath => Path.Combine(BackgroundsDirectory, "background.png");

        /// <summary>
        /// Copies a user-selected image into the durable app-data folder as a normalized PNG.
        /// Returns the stored path, or null when the source cannot be opened/copied.
        /// </summary>
        public static string? SetBackground(string sourcePath)
        {
            try
            {
                if (!File.Exists(sourcePath))
                {
                    return null;
                }

                using var source = new Bitmap(sourcePath);
                // Rewrite to PNG so a moved/deleted original never breaks the persisted path.
                source.Save(StoredPath, ImageFormat.Png);
                return StoredPath;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to set background: {ex.Message}");
                return null;
            }
        }

        /// <summary>Removes the stored background image (falls back to the flat preset color).</summary>
        public static void RemoveBackground()
        {
            try
            {
                if (File.Exists(StoredPath))
                {
                    File.Delete(StoredPath);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to remove background: {ex.Message}");
            }
        }

        /// <summary>
        /// Loads the background as an ATMOSPHERIC layer: downscaled to a sane render size,
        /// blurred (downscale → high-quality upscale — the standard cheap gaussian
        /// approximation, so the image reads as texture instead of a competing sharp
        /// visual), optionally desaturated, with the user's opacity applied as a global
        /// alpha (0 = fully transparent image, 1 = fully visible). The opacity is the
        /// readability lever: the image blends over the form's theme-colored BackColor, so
        /// low values keep every text element comfortable while the background stays
        /// intentional. Returns null when no image is present.
        /// </summary>
        /// <param name="path">Overrides the stored image (used by tests so they never touch
        /// the user's real persisted background).</param>
        public static Bitmap? LoadBackground(bool applyMonochromeFilter, double opacity, string? path = null)
        {
            try
            {
                string image = path ?? StoredPath;
                if (!File.Exists(image))
                {
                    return null;
                }

                using var original = new Bitmap(image);

                // A blurred background never needs full source resolution — a 4K source
                // serving a blur is wasted memory and paint time. Render at most
                // MaxRenderedEdge px on the long edge.
                const int MaxRenderedEdge = 1600;
                double fit = Math.Min(1.0, (double)MaxRenderedEdge / Math.Max(original.Width, original.Height));
                int targetW = Math.Max(1, (int)Math.Round(original.Width * fit));
                int targetH = Math.Max(1, (int)Math.Round(original.Height * fit));

                // Blur: shrink to a 1/8 thumbnail, then draw it back up to the target size —
                // the upscale softens all detail. One extra DrawImage, no per-pixel loops.
                int thumbW = Math.Max(1, targetW / 8);
                int thumbH = Math.Max(1, targetH / 8);
                using var thumb = new Bitmap(thumbW, thumbH);
                using (var tg = Graphics.FromImage(thumb))
                {
                    tg.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    tg.DrawImage(original, 0, 0, thumbW, thumbH);
                }

                // One combined color matrix: optional desaturation + scrim, then the user's
                // opacity as a global alpha (row 4).
                float a = (float)Math.Clamp(opacity, 0.0, 1.0);
                float[][] matrix = applyMonochromeFilter
                    ? new float[][]
                    {
                        new float[] {0.299f*a,0.299f*a,0.299f*a,0,0},
                        new float[] {0.587f*a,0.587f*a,0.587f*a,0,0},
                        new float[] {0.114f*a,0.114f*a,0.114f*a,0,0},
                        new float[] {0,0,0,a,0},
                        new float[] {0.18f*a,-0.02f*a,-0.08f*a,0,1}
                    }
                    : new float[][]
                    {
                        new float[] {a,0,0,0,0},
                        new float[] {0,a,0,0,0},
                        new float[] {0,0,a,0,0},
                        new float[] {0,0,0,a,0},
                        new float[] {0,0,0,0,1}
                    };

                var result = new Bitmap(targetW, targetH, PixelFormat.Format32bppArgb);
                using (var attr = new ImageAttributes())
                {
                    attr.SetColorMatrix(new ColorMatrix(matrix));
                    using var g = Graphics.FromImage(result);
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.HighQuality;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(thumb, new Rectangle(0, 0, targetW, targetH),
                        0, 0, thumbW, thumbH, GraphicsUnit.Pixel, attr);
                }

                return result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load background: {ex.Message}");
                return null;
            }
        }
    }
}