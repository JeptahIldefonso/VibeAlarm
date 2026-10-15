using System;

namespace VibeAlarm.Services
{
    /// <summary>
    /// Playback engine contract. Decouples music playback from the underlying audio
    /// implementation (e.g. Win32 MCI, NAudio, or Media Foundation).
    /// </summary>
    public interface IMusicPlayer : IDisposable
    {
        /// <summary>Starts playing the track at the specified path.</summary>
        bool Play(string filePath, string displayName);

        /// <summary>Pauses playback.</summary>
        void Pause();

        /// <summary>Resumes playback if paused.</summary>
        void Resume();

        /// <summary>Stops playback.</summary>
        void Stop();

        /// <summary>Seeks to a position in milliseconds.</summary>
        void Seek(int positionMs);

        /// <summary>Gets the current playback position in milliseconds.</summary>
        int GetPositionMs();

        /// <summary>Gets the total track duration in milliseconds.</summary>
        int GetDurationMs();

        /// <summary>Gets or sets the playback volume (0 to 100).</summary>
        int Volume { get; set; }

        /// <summary>True if audio is currently playing.</summary>
        bool IsPlaying { get; }

        /// <summary>True if audio is paused.</summary>
        bool IsPaused { get; }

        /// <summary>Raised when a track naturally reaches playback completion.</summary>
        event Action? PlaybackEnded;

        /// <summary>Raised when an error occurs during decoding or playback.</summary>
        event Action<string>? PlaybackError;

        /// <summary>The file path of the currently loaded track, or null.</summary>
        string? CurrentFilePath { get; }

        /// <summary>The display name of the currently loaded track, or null.</summary>
        string? CurrentDisplayName { get; }
    }
}
