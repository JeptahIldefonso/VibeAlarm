using System;

namespace VibeAlarm.Models
{
    /// <summary>
    /// A single track in the music library with metadata parsed via TagLibSharp.
    /// Missing tags gracefully fall back to clean defaults (e.g. filename for title).
    /// </summary>
    public sealed class MusicTrack
    {
        /// <summary>Deterministic ID based on the file path.</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Absolute path to the audio file on disk.</summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>The root library folder this track belongs to.</summary>
        public string FolderPath { get; set; } = string.Empty;

        /// <summary>Song title (tag title, or filename without extension).</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>Artist name (tag artist/performers, or "Unknown artist").</summary>
        public string Artist { get; set; } = "Unknown artist";

        /// <summary>Album name (tag album, or "Unknown album").</summary>
        public string Album { get; set; } = "Unknown album";

        /// <summary>Release year, or null if missing.</summary>
        public uint? Year { get; set; }

        /// <summary>Genre (tag genre, or "Unknown genre").</summary>
        public string Genre { get; set; } = "Unknown genre";

        /// <summary>Track number in album, or null if missing.</summary>
        public uint? TrackNumber { get; set; }

        /// <summary>Duration in milliseconds, extracted from file headers/tags.</summary>
        public int DurationMs { get; set; }

        /// <summary>File size in bytes (for fast incremental rescan checks).</summary>
        public long FileSize { get; set; }

        /// <summary>Last modified timestamp in UTC (for fast incremental rescan checks).</summary>
        public DateTime ModifiedAtUtc { get; set; }

        /// <summary>Timestamp when this track was first indexed.</summary>
        public DateTime AddedAt { get; set; }

        /// <summary>True if the file exists on disk; false if missing/moved.</summary>
        public bool Available { get; set; } = true;

        /// <summary>Deduplicated hash key of the thumbnail image in %LOCALAPPDATA%\VibeAlarm\Art ({ArtKey}.jpg), or null if no art.</summary>
        public string? ArtKey { get; set; }

        /// <summary>True if the artwork extraction pass has checked this track (with or without art found).</summary>
        public bool ArtChecked { get; set; }
    }
}
