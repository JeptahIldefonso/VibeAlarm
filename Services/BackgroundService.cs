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
        /// Loads the stored background, optionally desaturated, with the user's opacity applied
        /// (0 = fully transparent image, 1 = fully visible), so foreground text stays legible.
        /// Returns null when no image is present.
        /// </summary>
        public static Bitmap? LoadBackground(bool applyMonochromeFilter, double opacity)
        {
            try
            {
                string path = StoredPath;
                if (!File.Exists(path))
                {
                    return null;
                }

                // One combined color matrix: optional desaturation + scrim, then the user's
                // opacity as a global alpha (row 4). At opacity 1 the output is identical to
                // the previous fixed-full-opacity render.
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

                using var original = new Bitmap(path);
                var result = new Bitmap(original.Width, original.Height, PixelFormat.Format32bppArgb);
                using (var attr = new ImageAttributes())
                {
                    attr.SetColorMatrix(new ColorMatrix(matrix));
                    using var g = Graphics.FromImage(result);
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.HighQuality;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(original, new Rectangle(0, 0, result.Width, result.Height),
                        0, 0, result.Width, result.Height, GraphicsUnit.Pixel, attr);
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