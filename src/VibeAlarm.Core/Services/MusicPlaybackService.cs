using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using VibeAlarm.Models;

namespace VibeAlarm.Services
{
    /// <summary>
    /// Snapshot of the current play queue, modes, and indices.
    /// </summary>
    public sealed class MusicQueueState
    {
        public bool Shuffle { get; set; }
        public string RepeatMode { get; set; } = "off"; // "off", "all", "one"
        public int QueueCount { get; set; }
        public int CurrentIndex { get; set; }
        public string? CurrentTrackId { get; set; }
    }

    /// <summary>
    /// Host-level playback coordinator that manages the active queue, shuffle, repeat,
    /// auto-advance on track completion, history tracking, and error resilience.
    /// </summary>
    public sealed class MusicPlaybackService : IDisposable
    {
        private readonly object syncLock = new();
        private readonly IMusicPlayer player;
        private readonly MusicLibraryService library;

        private readonly List<string> originalQueue = new();
        private readonly List<string> activeQueue = new();
        private readonly List<string> history = new();

        private int currentIndex = -1;
        private int consecutiveFailureCount = 0;
        private bool shuffle = false;
        private string repeatMode = "off"; // "off", "all", "one"
        private bool disposed = false;

        public event Action<MusicTrack?>? TrackChanged;
        public event Action<string>? StateChanged; // "playing", "paused", "stopped"
        public event Action<MusicQueueState>? QueueStateChanged;
        public event Action<string, string?>? Error; // message, trackId

        public bool Shuffle
        {
            get { lock (syncLock) { return shuffle; } }
            set { SetShuffle(value); }
        }

        public string RepeatMode
        {
            get { lock (syncLock) { return repeatMode; } }
            set { SetRepeatMode(value); }
        }

        public MusicTrack? CurrentTrack { get; private set; }

        public bool IsPlaying => player.IsPlaying;
        public bool IsPaused => player.IsPaused;

        public int Volume
        {
            get => player.Volume;
            set => player.Volume = value;
        }

        public MusicPlaybackService(IMusicPlayer player, MusicLibraryService library)
        {
            this.player = player;
            this.library = library;

            this.player.PlaybackEnded += OnPlayerPlaybackEnded;
            this.player.PlaybackError += OnPlayerPlaybackError;
        }

        /// <summary>
        /// Restores saved playback preferences (shuffle, repeat, last track) from settings.
        /// Does not auto-play on launch; leaves the track paused/ready.
        /// </summary>
        public void HydrateFromSettings(AppSettings settings)
        {
            lock (syncLock)
            {
                shuffle = settings.MusicShuffle;
                repeatMode = string.IsNullOrEmpty(settings.MusicRepeatMode) ? "off" : settings.MusicRepeatMode;

                if (!string.IsNullOrEmpty(settings.LastPlayedTrackId))
                {
                    var track = library.Tracks.FirstOrDefault(t => t.Id == settings.LastPlayedTrackId);
                    if (track != null)
                    {
                        CurrentTrack = track;
                        originalQueue.Clear();
                        originalQueue.Add(track.Id);
                        activeQueue.Clear();
                        activeQueue.Add(track.Id);
                        currentIndex = 0;
                    }
                }
            }
        }

        /// <summary>
        /// Plays a track by ID. If a queue of track IDs is provided, replaces the queue and sets the current index.
        /// </summary>
        public bool PlayTrack(string trackId, IEnumerable<string>? queueTrackIds = null, bool? forceShuffle = null)
        {
            lock (syncLock)
            {
                if (disposed) return false;

                if (forceShuffle.HasValue)
                {
                    shuffle = forceShuffle.Value;
                }

                if (queueTrackIds != null)
                {
                    originalQueue.Clear();
                    originalQueue.AddRange(queueTrackIds);

                    RebuildActiveQueue(trackId);
                }
                else if (!activeQueue.Contains(trackId))
                {
                    originalQueue.Clear();
                    originalQueue.Add(trackId);
                    activeQueue.Clear();
                    activeQueue.Add(trackId);
                    currentIndex = 0;
                }
                else
                {
                    currentIndex = activeQueue.IndexOf(trackId);
                }

                consecutiveFailureCount = 0;
                return StartTrackPlayback(trackId);
            }
        }

        public void Pause()
        {
            lock (syncLock)
            {
                player.Pause();
                StateChanged?.Invoke("paused");
            }
        }

        public void Resume()
        {
            lock (syncLock)
            {
                if (player.IsPaused)
                {
                    player.Resume();
                    StateChanged?.Invoke("playing");
                    return;
                }

                if (CurrentTrack != null && !player.IsPlaying)
                {
                    StartTrackPlayback(CurrentTrack.Id);
                    return;
                }

                if (activeQueue.Count > 0 && currentIndex >= 0 && currentIndex < activeQueue.Count)
                {
                    StartTrackPlayback(activeQueue[currentIndex]);
                }
            }
        }

        public void Stop()
        {
            lock (syncLock)
            {
                player.Stop();
                StateChanged?.Invoke("stopped");
            }
        }

        public void Seek(int positionMs)
        {
            lock (syncLock)
            {
                player.Seek(positionMs);
            }
        }

        public int GetPositionMs() => player.GetPositionMs();
        public int GetDurationMs() => player.GetDurationMs();

        /// <summary>
        /// Advances to the next track in the queue, handling repeat all / repeat one / repeat off.
        /// </summary>
        public bool Next(bool autoAdvance = false)
        {
            lock (syncLock)
            {
                if (disposed) return false;

                if (autoAdvance && string.Equals(repeatMode, "one", StringComparison.OrdinalIgnoreCase))
                {
                    if (CurrentTrack != null)
                    {
                        player.Seek(0);
                        return StartTrackPlayback(CurrentTrack.Id);
                    }
                }

                int nextIndex = currentIndex + 1;

                if (nextIndex < activeQueue.Count)
                {
                    currentIndex = nextIndex;
                    return StartTrackPlayback(activeQueue[currentIndex]);
                }

                // Reached end of queue
                if (string.Equals(repeatMode, "all", StringComparison.OrdinalIgnoreCase) && activeQueue.Count > 0)
                {
                    currentIndex = 0;
                    return StartTrackPlayback(activeQueue[currentIndex]);
                }

                // Repeat off: stop playback, keep last track loaded at 0 position
                player.Stop();
                StateChanged?.Invoke("stopped");
                return false;
            }
        }

        /// <summary>
        /// Navigates to previous song, or restarts the current song if more than 3 seconds elapsed.
        /// </summary>
        public bool Previous()
        {
            lock (syncLock)
            {
                if (disposed) return false;

                // Rule: If > 3 seconds elapsed, restart current song
                if (player.GetPositionMs() > 3000)
                {
                    player.Seek(0);
                    if (!player.IsPlaying && !player.IsPaused)
                    {
                        player.Resume();
                        StateChanged?.Invoke("playing");
                    }
                    return true;
                }

                // Otherwise go to previous song
                if (history.Count > 1)
                {
                    // Remove current song from history top
                    history.RemoveAt(history.Count - 1);
                    string prevId = history[^1];
                    int idx = activeQueue.IndexOf(prevId);
                    if (idx >= 0)
                    {
                        currentIndex = idx;
                    }
                    else if (currentIndex > 0)
                    {
                        currentIndex--;
                    }
                    return StartTrackPlayback(activeQueue[currentIndex]);
                }

                if (currentIndex > 0)
                {
                    currentIndex--;
                    return StartTrackPlayback(activeQueue[currentIndex]);
                }

                // At the very start of queue: restart current song
                player.Seek(0);
                return true;
            }
        }

        public void SetShuffle(bool enable)
        {
            lock (syncLock)
            {
                if (shuffle == enable) return;
                shuffle = enable;

                string? currentId = CurrentTrack?.Id ?? activeQueue.FirstOrDefault() ?? originalQueue.FirstOrDefault();
                if (currentId != null)
                {
                    RebuildActiveQueue(currentId);
                }

                PersistSettings();
                QueueStateChanged?.Invoke(GetQueueState());
            }
        }

        public void SetRepeatMode(string mode)
        {
            lock (syncLock)
            {
                string normalized = mode.ToLowerInvariant();
                if (normalized != "off" && normalized != "all" && normalized != "one")
                {
                    normalized = "off";
                }

                repeatMode = normalized;
                PersistSettings();
                QueueStateChanged?.Invoke(GetQueueState());
            }
        }

        public MusicQueueState GetQueueState()
        {
            lock (syncLock)
            {
                return new MusicQueueState
                {
                    Shuffle = shuffle,
                    RepeatMode = repeatMode,
                    QueueCount = activeQueue.Count,
                    CurrentIndex = currentIndex,
                    CurrentTrackId = CurrentTrack?.Id
                };
            }
        }

        private bool StartTrackPlayback(string trackId)
        {
            var track = library.Tracks.FirstOrDefault(t => t.Id == trackId);
            if (track == null || !track.Available)
            {
                library.SetTrackAvailability(trackId, false);
                Error?.Invoke($"Track unavailable: {track?.Title ?? trackId}", trackId);
                return SkipFailedTrack(trackId);
            }

            bool ok = player.Play(track.Path, track.Title);
            if (!ok)
            {
                library.SetTrackAvailability(track.Id, false);
                Error?.Invoke($"Cannot play '{track.Title}' ({Path.GetExtension(track.Path)})", track.Id);
                return SkipFailedTrack(track.Id);
            }

            consecutiveFailureCount = 0;
            CurrentTrack = track;

            // Extract artwork on demand for the track about to play if not yet processed
            if (!track.ArtChecked)
            {
                library.ArtService.EnsureTrackArt(track);
            }

            if (history.Count == 0 || history[^1] != track.Id)
            {
                history.Add(track.Id);
                if (history.Count > 100) history.RemoveAt(0);
            }

            TrackChanged?.Invoke(track);
            StateChanged?.Invoke("playing");
            QueueStateChanged?.Invoke(GetQueueState());

            PersistLastPlayed(track.Id);
            return true;
        }

        private bool SkipFailedTrack(string failedTrackId)
        {
            consecutiveFailureCount++;
            if (consecutiveFailureCount >= 5)
            {
                player.Stop();
                CurrentTrack = null;
                TrackChanged?.Invoke(null);
                StateChanged?.Invoke("stopped");
                Error?.Invoke("Multiple consecutive tracks failed to play. Playback stopped.", failedTrackId);
                return false;
            }

            // Auto-advance to next track in queue
            return Next(autoAdvance: true);
        }

        private void RebuildActiveQueue(string startingTrackId)
        {
            if (originalQueue.Count == 0) return;

            if (shuffle)
            {
                var others = originalQueue.Where(id => id != startingTrackId).ToList();
                ShuffleList(others);

                activeQueue.Clear();
                activeQueue.Add(startingTrackId);
                activeQueue.AddRange(others);
                currentIndex = 0;
            }
            else
            {
                activeQueue.Clear();
                activeQueue.AddRange(originalQueue);
                currentIndex = activeQueue.IndexOf(startingTrackId);
                if (currentIndex < 0) currentIndex = 0;
            }
        }

        private static void ShuffleList<T>(IList<T> list)
        {
            int n = list.Count;
            while (n > 1)
            {
                n--;
                int k = RandomNumberGenerator.GetInt32(n + 1);
                (list[n], list[k]) = (list[k], list[n]);
            }
        }

        private void OnPlayerPlaybackEnded()
        {
            Next(autoAdvance: true);
        }

        private void OnPlayerPlaybackError(string errorMessage)
        {
            Error?.Invoke(errorMessage, CurrentTrack?.Id);
            SkipFailedTrack(CurrentTrack?.Id ?? string.Empty);
        }

        private void PersistSettings()
        {
            try
            {
                var settings = SettingsService.Load();
                settings.MusicShuffle = shuffle;
                settings.MusicRepeatMode = repeatMode;
                SettingsService.Save(settings);
            }
            catch
            {
                // Ignore transient write errors
            }
        }

        private void PersistLastPlayed(string trackId)
        {
            try
            {
                var settings = SettingsService.Load();
                settings.LastPlayedTrackId = trackId;
                SettingsService.Save(settings);
            }
            catch
            {
                // Ignore
            }
        }

        public void Dispose()
        {
            lock (syncLock)
            {
                if (disposed) return;
                disposed = true;

                player.PlaybackEnded -= OnPlayerPlaybackEnded;
                player.PlaybackError -= OnPlayerPlaybackError;
                player.Dispose();
            }
        }
    }
}
