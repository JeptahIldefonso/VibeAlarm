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
        /// Loads the stored background, optionally darkened and desaturated so foreground text
        /// stays legible and the design stays monochrome. Returns null when no image is present.
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

                using var original = new Bitmap(path);
                var result = new Bitmap(original.Width, original.Height, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(result))
                {
                    g.SmoothingMode = SmoothingMode.HighQuality;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(original, 0, 0, original.Width, original.Height);
                }

                if (applyMonochromeFilter)
                {
                    // Desaturate, then darken with a scrim so text remains legible behind it.
                    ColorMatrix m = new(new float[][]
                    {
                        new float[] {0.299f,0.299f,0.299f,0,0},
                        new float[] {0.587f,0.587f,0.587f,0,0},
                        new float[] {0.114f,0.114f,0.114f,0,0},
                        new float[] {0,0,0,1,0},
                        new float[] {0.18f,-0.02f,-0.08f,0,1}
                    });
                    using var attr = new ImageAttributes();
                    attr.SetColorMatrix(m);
                    using var dark = new Bitmap(result.Width, result.Height, PixelFormat.Format32bppArgb);
                    using (var dg = Graphics.FromImage(dark))
                    {
                        dg.DrawImage(result, new Rectangle(0, 0, result.Width, result.Height),
                            0, 0, result.Width, result.Height, GraphicsUnit.Pixel, attr);
                    }
                    result.Dispose();
                    return dark;
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