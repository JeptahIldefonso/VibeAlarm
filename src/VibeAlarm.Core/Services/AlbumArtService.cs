using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using VibeAlarm.Models;

namespace VibeAlarm.Services
{
    /// <summary>
    /// Background and on-demand album artwork extraction, thumbnailing, deduplication,
    /// and disk cache management.
    /// </summary>
    public sealed class AlbumArtService : IDisposable
    {
        private static readonly string[] FolderArtNames = { "cover", "folder", "front", "album" };
        private static readonly string[] FolderArtExts = { ".jpg", ".jpeg", ".png" };

        private readonly object passLock = new();
        private CancellationTokenSource? currentPassCts;
        private bool disposed = false;

        /// <summary>Raised when a batch of tracks has had artwork resolved (trackId and artKey pairs).</summary>
        public event Action<List<(string TrackId, string? ArtKey)>>? ArtBatchReady;

        /// <summary>Raised when the library index should be persisted to disk after a batch of art extractions.</summary>
        public event Action? PersistIndexRequested;

        /// <summary>Diagnostics: tracks processed in the last background pass.</summary>
        public int LastPassTrackCount { get; private set; }

        /// <summary>Diagnostics: total elapsed milliseconds for the last background pass.</summary>
        public long LastPassDurationMs { get; private set; }

        public AlbumArtService()
        {
            try
            {
                Directory.CreateDirectory(AppPaths.ArtCacheDir);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to create ArtCacheDir: {ex.Message}");
            }
        }

        /// <summary>
        /// Starts a low-priority background pass that processes all tracks requiring artwork extraction.
        /// Does not block scan or UI.
        /// </summary>
        public void StartBackgroundPass(IReadOnlyList<MusicTrack> allTracks)
        {
            lock (passLock)
            {
                if (disposed) return;

                currentPassCts?.Cancel();
                currentPassCts?.Dispose();
                currentPassCts = new CancellationTokenSource();
            }

            var token = currentPassCts.Token;
            Task.Run(async () =>
            {
                var sw = Stopwatch.StartNew();
                List<MusicTrack> toProcess;
                lock (passLock)
                {
                    toProcess = allTracks.Where(t => !t.ArtChecked && t.Available).ToList();
                }

                if (toProcess.Count == 0)
                {
                    sw.Stop();
                    LastPassTrackCount = 0;
                    LastPassDurationMs = sw.ElapsedMilliseconds;
                    return;
                }

                var batch = new List<(string TrackId, string? ArtKey)>(20);
                int processedSinceSave = 0;
                int totalProcessed = 0;

                foreach (var track in toProcess)
                {
                    if (token.IsCancellationRequested || disposed) break;

                    string? artKey = ExtractArtForTrack(track);
                    batch.Add((track.Id, artKey));
                    processedSinceSave++;
                    totalProcessed++;

                    if (batch.Count >= 20)
                    {
                        var copy = batch.ToList();
                        batch.Clear();
                        ArtBatchReady?.Invoke(copy);
                    }

                    if (processedSinceSave >= 50)
                    {
                        PersistIndexRequested?.Invoke();
                        processedSinceSave = 0;
                    }

                    try
                    {
                        await Task.Delay(5, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }

                if (batch.Count > 0)
                {
                    var copy = batch.ToList();
                    batch.Clear();
                    ArtBatchReady?.Invoke(copy);
                }

                if (processedSinceSave > 0)
                {
                    PersistIndexRequested?.Invoke();
                }

                sw.Stop();
                LastPassTrackCount = totalProcessed;
                LastPassDurationMs = sw.ElapsedMilliseconds;
                Debug.WriteLine($"Background artwork pass completed: {totalProcessed} tracks in {sw.ElapsedMilliseconds}ms (avg {(totalProcessed > 0 ? (double)sw.ElapsedMilliseconds / totalProcessed : 0):F1}ms/track).");
            }, token);
        }

        /// <summary>
        /// Ensures a specific track's artwork is extracted immediately on demand (e.g. when selected/played).
        /// </summary>
        public string? EnsureTrackArt(MusicTrack track)
        {
            if (track.ArtChecked)
            {
                return track.ArtKey;
            }

            string? artKey = ExtractArtForTrack(track);
            ArtBatchReady?.Invoke(new() { (track.Id, artKey) });
            PersistIndexRequested?.Invoke();
            return artKey;
        }

        /// <summary>
        /// Extracts artwork from embedded tags or folder image, hashes, and generates a 160x160 thumbnail.
        /// Always marks ArtChecked = true.
        /// </summary>
        public string? ExtractArtForTrack(MusicTrack track)
        {
            if (string.IsNullOrWhiteSpace(track.Path) || !File.Exists(track.Path))
            {
                track.ArtChecked = true;
                track.ArtKey = null;
                return null;
            }

            byte[]? rawImageBytes = null;

            // 1. TagLibSharp embedded picture (front cover first, else first picture)
            try
            {
                using var tagFile = TagLib.File.Create(track.Path);
                var pictures = tagFile?.Tag?.Pictures;
                if (pictures != null && pictures.Length > 0)
                {
                    var front = pictures.FirstOrDefault(p => p.Type == TagLib.PictureType.FrontCover) ?? pictures[0];
                    if (front?.Data?.Data != null && front.Data.Data.Length > 0)
                    {
                        rawImageBytes = front.Data.Data;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to read embedded picture for {track.Path}: {ex.Message}");
            }

            // 2. Folder image file (cover, folder, front, album with .jpg, .jpeg, .png)
            if (rawImageBytes == null)
            {
                try
                {
                    string? dir = Path.GetDirectoryName(track.Path);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    {
                        foreach (var name in FolderArtNames)
                        {
                            foreach (var ext in FolderArtExts)
                            {
                                string candidate = Path.Combine(dir, name + ext);
                                if (File.Exists(candidate))
                                {
                                    try
                                    {
                                        var bytes = File.ReadAllBytes(candidate);
                                        if (bytes.Length > 0)
                                        {
                                            rawImageBytes = bytes;
                                            break;
                                        }
                                    }
                                    catch
                                    {
                                        // Ignore locked or inaccessible candidate
                                    }
                                }
                            }
                            if (rawImageBytes != null) break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to check folder image for {track.Path}: {ex.Message}");
                }
            }

            // 3. No artwork found
            if (rawImageBytes == null || rawImageBytes.Length == 0)
            {
                track.ArtChecked = true;
                track.ArtKey = null;
                return null;
            }

            // 4. Hash and thumbnail creation (deduplicated by byte hash)
            try
            {
                string artKey = ComputeHash(rawImageBytes);
                string thumbPath = Path.Combine(AppPaths.ArtCacheDir, artKey + ".jpg");

                if (!File.Exists(thumbPath))
                {
                    SaveThumbnail(rawImageBytes, thumbPath);
                }

                track.ArtKey = artKey;
                track.ArtChecked = true;
                return artKey;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to process artwork thumbnail for {track.Path}: {ex.Message}");
                track.ArtChecked = true;
                track.ArtKey = null;
                return null;
            }
        }

        /// <summary>
        /// Resizes raw image bytes to a 160x160 square center-cropped JPEG (quality 80).
        /// </summary>
        private static void SaveThumbnail(byte[] imageBytes, string destPath)
        {
            using var ms = new MemoryStream(imageBytes);
            using var src = Image.FromStream(ms);

            int srcW = src.Width;
            int srcH = src.Height;
            if (srcW <= 0 || srcH <= 0) return;

            int cropSize = Math.Min(srcW, srcH);
            int srcX = (srcW - cropSize) / 2;
            int srcY = (srcH - cropSize) / 2;

            using var thumb = new Bitmap(160, 160, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(thumb))
            {
                g.Clear(Color.FromArgb(0x20, 0x20, 0x20));
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;

                g.DrawImage(src, new Rectangle(0, 0, 160, 160), new Rectangle(srcX, srcY, cropSize, cropSize), GraphicsUnit.Pixel);
            }

            var jpegEncoder = GetJpegEncoder();
            if (jpegEncoder != null)
            {
                using var encoderParams = new EncoderParameters(1);
                encoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 80L);

                string tempPath = destPath + ".tmp." + Guid.NewGuid().ToString("N");
                thumb.Save(tempPath, jpegEncoder, encoderParams);

                if (!File.Exists(destPath))
                {
                    try
                    {
                        File.Move(tempPath, destPath);
                    }
                    catch
                    {
                        if (File.Exists(tempPath)) File.Delete(tempPath);
                    }
                }
                else
                {
                    if (File.Exists(tempPath)) File.Delete(tempPath);
                }
            }
        }

        private static ImageCodecInfo? GetJpegEncoder()
        {
            return ImageCodecInfo.GetImageEncoders().FirstOrDefault(e => e.FormatID == ImageFormat.Jpeg.Guid);
        }

        private static string ComputeHash(byte[] bytes)
        {
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(bytes);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        /// <summary>
        /// Removes orphaned thumbnails on disk that are not referenced by any tracks in the library.
        /// </summary>
        public void CleanupOrphanedThumbnails(IEnumerable<MusicTrack> remainingTracks)
        {
            try
            {
                var referencedKeys = new HashSet<string>(
                    remainingTracks
                        .Where(t => !string.IsNullOrEmpty(t.ArtKey))
                        .Select(t => t.ArtKey!),
                    StringComparer.OrdinalIgnoreCase);

                if (Directory.Exists(AppPaths.ArtCacheDir))
                {
                    foreach (var file in Directory.EnumerateFiles(AppPaths.ArtCacheDir, "*.jpg"))
                    {
                        string key = Path.GetFileNameWithoutExtension(file);
                        if (!referencedKeys.Contains(key))
                        {
                            try { File.Delete(file); } catch { }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to clean up orphaned thumbnails: {ex.Message}");
            }
        }

        /// <summary>
        /// Returns the count and total size of thumbnail files in the art cache directory.
        /// </summary>
        public (int FileCount, long TotalSizeBytes) GetCacheStats()
        {
            try
            {
                if (!Directory.Exists(AppPaths.ArtCacheDir))
                {
                    return (0, 0);
                }

                var files = Directory.GetFiles(AppPaths.ArtCacheDir, "*.jpg");
                long size = 0;
                foreach (var f in files)
                {
                    try
                    {
                        size += new FileInfo(f).Length;
                    }
                    catch { }
                }
                return (files.Length, size);
            }
            catch
            {
                return (0, 0);
            }
        }

        public void CancelCurrentPass()
        {
            lock (passLock)
            {
                currentPassCts?.Cancel();
            }
        }

        public void Dispose()
        {
            lock (passLock)
            {
                if (disposed) return;
                disposed = true;

                currentPassCts?.Cancel();
                currentPassCts?.Dispose();
                currentPassCts = null;
            }
        }
    }
}
