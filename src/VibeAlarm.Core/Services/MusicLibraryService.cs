using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using VibeAlarm.Models;

namespace VibeAlarm.Services
{
    /// <summary>
    /// Scan progress notification payload.
    /// </summary>
    public sealed class MusicScanProgress
    {
        public string Folder { get; set; } = string.Empty;
        public int SongsFoundSoFar { get; set; }
        public bool Done { get; set; }
    }

    /// <summary>
    /// Manages music library indexing, tag extraction with TagLibSharp, incremental rescanning,
    /// and folder management off the UI thread.
    /// </summary>
    public sealed class MusicLibraryService
    {
        /// <summary>
        /// Single source of truth for all supported audio formats.
        /// </summary>
        public static readonly string[] SupportedExtensions =
            { ".mp3", ".wav", ".m4a", ".flac", ".ogg", ".opus" };

        private readonly object syncLock = new();
        private readonly List<LibraryFolder> folders = new();
        private readonly List<MusicTrack> tracks = new();

        /// <summary>Fired on background scan progress: folder name, count so far, done flag.</summary>
        public event Action<MusicScanProgress>? ScanProgress;

        /// <summary>Fired in batches of ~200 tracks during background scanning.</summary>
        public event Action<List<MusicTrack>>? TracksBatch;

        /// <summary>Fired when full scan completes or folders are modified.</summary>
        public event Action<List<MusicTrack>>? LibraryChanged;

        /// <summary>Fired when a batch of tracks has artwork resolved.</summary>
        public event Action<List<(string TrackId, string? ArtKey)>>? ArtBatchReady;

        /// <summary>Album artwork extraction and cache service.</summary>
        public AlbumArtService ArtService { get; } = new();

        public IReadOnlyList<LibraryFolder> Folders
        {
            get { lock (syncLock) { return folders.ToList(); } }
        }

        public IReadOnlyList<MusicTrack> Tracks
        {
            get { lock (syncLock) { return tracks.ToList(); } }
        }

        public MusicLibraryService()
        {
            // On startup: load persisted index first so the library shows instantly without rescanning
            MusicLibraryData data = MusicLibraryStorageService.Load();
            lock (syncLock)
            {
                folders.AddRange(data.Folders);
                tracks.AddRange(data.Tracks);
            }

            ArtService.ArtBatchReady += batch => ArtBatchReady?.Invoke(batch);
            ArtService.PersistIndexRequested += () => PersistLibrary();
        }

        private bool isArtPassStarted;

        /// <summary>
        /// Starts the background album art pass if not already started.
        /// Called lazily when the Ambient/Music view is opened or playback starts,
        /// avoiding background CPU/disk work when sitting silently in the tray.
        /// </summary>
        public void StartArtPassIfNeeded()
        {
            lock (syncLock)
            {
                if (isArtPassStarted) return;
                isArtPassStarted = true;
            }
            ArtService.StartBackgroundPass(Tracks.ToList());
        }

        /// <summary>
        /// Registers a new folder and starts a background scan.
        /// Validates folder existence, duplicate registration, and nested folder rules.
        /// </summary>
        public async Task<LibraryFolder> AddFolderAsync(string folderPath)
        {
            string normalized = NormalizePath(folderPath);
            if (!Directory.Exists(normalized))
            {
                throw new DirectoryNotFoundException($"Directory does not exist: {folderPath}");
            }

            LibraryFolder folder;
            lock (syncLock)
            {
                // Rule 1: Reject identical duplicate
                if (folders.Any(f => string.Equals(f.Path, normalized, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidOperationException("Folder is already in your library.");
                }

                // Rule 2: Reject if child of an existing folder (already covered by recursive scan)
                var existingParent = folders.FirstOrDefault(f => IsSubfolder(normalized, f.Path));
                if (existingParent != null)
                {
                    throw new InvalidOperationException(
                        $"This folder is already included because its parent folder '{existingParent.Path}' is in your library.");
                }

                // Rule 3: If new folder contains one or more existing folders, subsume them
                var childFolders = folders.Where(f => IsSubfolder(f.Path, normalized)).ToList();
                foreach (var child in childFolders)
                {
                    folders.Remove(child);
                    // Reassign tracks to new parent
                    foreach (var track in tracks.Where(t => string.Equals(t.FolderPath, child.Path, StringComparison.OrdinalIgnoreCase)))
                    {
                        track.FolderPath = normalized;
                    }
                }

                folder = new LibraryFolder
                {
                    Path = normalized,
                    AddedAt = DateTime.UtcNow,
                    LastScannedAt = DateTime.UtcNow,
                    TrackCount = tracks.Count(t => string.Equals(t.FolderPath, normalized, StringComparison.OrdinalIgnoreCase)),
                    Reachable = true
                };

                folders.Add(folder);
                PersistLibrary();
            }

            // Run scan on background thread
            await Task.Run(() => ScanFolderInternal(folder));
            return folder;
        }

        /// <summary>
        /// Removes a folder from the library index and removes its tracks from the library.
        /// Never modifies or deletes files on disk.
        /// </summary>
        public void RemoveFolder(string folderPath)
        {
            string normalized = NormalizePath(folderPath);
            lock (syncLock)
            {
                folders.RemoveAll(f => string.Equals(f.Path, normalized, StringComparison.OrdinalIgnoreCase));
                tracks.RemoveAll(t => string.Equals(t.FolderPath, normalized, StringComparison.OrdinalIgnoreCase));
                PersistLibrary();
                ArtService.CleanupOrphanedThumbnails(tracks);
            }

            LibraryChanged?.Invoke(Tracks.ToList());
        }

        /// <summary>
        /// Triggers an incremental rescan of a specific folder or all folders on a background thread.
        /// </summary>
        public async Task RescanAsync(string? folderPath = null)
        {
            List<LibraryFolder> toScan;
            lock (syncLock)
            {
                if (!string.IsNullOrEmpty(folderPath))
                {
                    string normalized = NormalizePath(folderPath);
                    toScan = folders.Where(f => string.Equals(f.Path, normalized, StringComparison.OrdinalIgnoreCase)).ToList();
                }
                else
                {
                    toScan = folders.ToList();
                }
            }

            await Task.Run(() =>
            {
                foreach (var folder in toScan)
                {
                    ScanFolderInternal(folder);
                }
            });
        }

        private void ScanFolderInternal(LibraryFolder folder)
        {
            if (!Directory.Exists(folder.Path))
            {
                lock (syncLock)
                {
                    folder.Reachable = false;
                    foreach (var t in tracks.Where(t => string.Equals(t.FolderPath, folder.Path, StringComparison.OrdinalIgnoreCase)))
                    {
                        t.Available = false;
                    }
                    PersistLibrary();
                }
                ScanProgress?.Invoke(new MusicScanProgress { Folder = folder.Path, SongsFoundSoFar = 0, Done = true });
                return;
            }

            folder.Reachable = true;
            IEnumerable<string> filePaths;
            try
            {
                var searchPatternExts = new HashSet<string>(SupportedExtensions, StringComparer.OrdinalIgnoreCase);
                filePaths = Directory.EnumerateFiles(folder.Path, "*.*", SearchOption.AllDirectories)
                    .Where(p => searchPatternExts.Contains(Path.GetExtension(p)));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to enumerate directory {folder.Path}: {ex.Message}");
                return;
            }

            var scannedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var batch = new List<MusicTrack>(200);
            int count = 0;

            foreach (string file in filePaths)
            {
                scannedPaths.Add(file);
                FileInfo fileInfo;
                try
                {
                    fileInfo = new FileInfo(file);
                }
                catch
                {
                    continue;
                }

                MusicTrack? existingTrack;
                lock (syncLock)
                {
                    existingTrack = tracks.FirstOrDefault(t => string.Equals(t.Path, file, StringComparison.OrdinalIgnoreCase));
                }

                // Incremental rescan check: if size & modification timestamp match, skip tag reading!
                if (existingTrack != null &&
                    existingTrack.FileSize == fileInfo.Length &&
                    existingTrack.ModifiedAtUtc == fileInfo.LastWriteTimeUtc)
                {
                    existingTrack.Available = true;
                    count++;
                }
                else
                {
                    // New or modified file: parse tags with TagLibSharp
                    MusicTrack parsedTrack = ReadTrackMetadata(file, folder.Path, fileInfo);
                    lock (syncLock)
                    {
                        if (existingTrack != null)
                        {
                            // Update in place preserving AddedAt
                            parsedTrack.AddedAt = existingTrack.AddedAt;
                            int idx = tracks.IndexOf(existingTrack);
                            if (idx >= 0) tracks[idx] = parsedTrack;
                        }
                        else
                        {
                            tracks.Add(parsedTrack);
                        }
                    }

                    batch.Add(parsedTrack);
                    count++;

                    // Push in batches of ~200
                    if (batch.Count >= 200)
                    {
                        TracksBatch?.Invoke(batch.ToList());
                        batch.Clear();
                        ScanProgress?.Invoke(new MusicScanProgress
                        {
                            Folder = folder.Path,
                            SongsFoundSoFar = count,
                            Done = false
                        });
                    }
                }
            }

            // Flush remainder batch
            if (batch.Count > 0)
            {
                TracksBatch?.Invoke(batch.ToList());
                batch.Clear();
            }

            // Mark missing/vanished files as unavailable (never delete)
            lock (syncLock)
            {
                foreach (var track in tracks.Where(t => string.Equals(t.FolderPath, folder.Path, StringComparison.OrdinalIgnoreCase)))
                {
                    if (!scannedPaths.Contains(track.Path))
                    {
                        track.Available = false;
                    }
                }

                folder.TrackCount = tracks.Count(t => string.Equals(t.FolderPath, folder.Path, StringComparison.OrdinalIgnoreCase) && t.Available);
                folder.LastScannedAt = DateTime.UtcNow;
                PersistLibrary();
            }

            ScanProgress?.Invoke(new MusicScanProgress
            {
                Folder = folder.Path,
                SongsFoundSoFar = count,
                Done = true
            });

            LibraryChanged?.Invoke(Tracks.ToList());

            // Run low-priority background artwork extraction pass for newly scanned/modified tracks
            ArtService.StartBackgroundPass(Tracks.ToList());
        }

        private static MusicTrack ReadTrackMetadata(string filePath, string folderPath, FileInfo fileInfo)
        {
            string fallbackTitle = Path.GetFileNameWithoutExtension(filePath);
            string title = fallbackTitle;
            string artist = "Unknown artist";
            string album = "Unknown album";
            string genre = "Unknown genre";
            uint? year = null;
            uint? trackNumber = null;
            int durationMs = 0;

            try
            {
                using var tagFile = TagLib.File.Create(filePath);
                if (tagFile != null)
                {
                    if (!string.IsNullOrWhiteSpace(tagFile.Tag.Title))
                    {
                        title = tagFile.Tag.Title.Trim();
                    }

                    if (tagFile.Tag.Performers != null && tagFile.Tag.Performers.Length > 0 &&
                        !string.IsNullOrWhiteSpace(tagFile.Tag.Performers[0]))
                    {
                        artist = string.Join(", ", tagFile.Tag.Performers.Where(p => !string.IsNullOrWhiteSpace(p))).Trim();
                    }
                    else if (!string.IsNullOrWhiteSpace(tagFile.Tag.FirstAlbumArtist))
                    {
                        artist = tagFile.Tag.FirstAlbumArtist.Trim();
                    }

                    if (!string.IsNullOrWhiteSpace(tagFile.Tag.Album))
                    {
                        album = tagFile.Tag.Album.Trim();
                    }

                    if (tagFile.Tag.Genres != null && tagFile.Tag.Genres.Length > 0 &&
                        !string.IsNullOrWhiteSpace(tagFile.Tag.Genres[0]))
                    {
                        genre = string.Join(", ", tagFile.Tag.Genres.Where(g => !string.IsNullOrWhiteSpace(g))).Trim();
                    }

                    if (tagFile.Tag.Year > 0)
                    {
                        year = tagFile.Tag.Year;
                    }

                    if (tagFile.Tag.Track > 0)
                    {
                        trackNumber = tagFile.Tag.Track;
                    }

                    durationMs = (int)tagFile.Properties.Duration.TotalMilliseconds;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TagLibSharp failed to parse {filePath}: {ex.Message}");
            }

            return new MusicTrack
            {
                Id = GenerateTrackId(filePath),
                Path = filePath,
                FolderPath = folderPath,
                Title = title,
                Artist = artist,
                Album = album,
                Year = year,
                Genre = genre,
                TrackNumber = trackNumber,
                DurationMs = durationMs,
                FileSize = fileInfo.Length,
                ModifiedAtUtc = fileInfo.LastWriteTimeUtc,
                AddedAt = DateTime.UtcNow,
                Available = true
            };
        }

        /// <summary>
        /// Updates track availability (e.g. if a track fails to open or file is missing) and persists the change.
        /// </summary>
        public void SetTrackAvailability(string trackId, bool available)
        {
            lock (syncLock)
            {
                var track = tracks.FirstOrDefault(t => t.Id == trackId);
                if (track != null && track.Available != available)
                {
                    track.Available = available;
                    PersistLibrary();
                }
            }
        }

        private void PersistLibrary()
        {
            var data = new MusicLibraryData
            {
                SchemaVersion = 1,
                Folders = folders.ToList(),
                Tracks = tracks.ToList()
            };
            MusicLibraryStorageService.Save(data);
        }

        private static string NormalizePath(string path)
        {
            return Path.GetFullPath(path).TrimEnd('\\');
        }

        private static bool IsSubfolder(string candidateChild, string parent)
        {
            string childNorm = NormalizePath(candidateChild);
            string parentNorm = NormalizePath(parent);
            return childNorm.StartsWith(parentNorm + "\\", StringComparison.OrdinalIgnoreCase);
        }

        private static string GenerateTrackId(string path)
        {
            byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(path.ToLowerInvariant()));
            return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
        }
    }
}
