using System;

namespace VibeAlarm.Models
{
    /// <summary>
    /// A music folder registered in the library.
    /// </summary>
    public sealed class LibraryFolder
    {
        /// <summary>Normalized absolute path to the directory.</summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>Timestamp when this folder was added to the library.</summary>
        public DateTime AddedAt { get; set; }

        /// <summary>Timestamp when this folder was last scanned.</summary>
        public DateTime LastScannedAt { get; set; }

        /// <summary>Number of tracks currently indexed under this folder.</summary>
        public int TrackCount { get; set; }

        /// <summary>True if the folder directory currently exists and is accessible on disk.</summary>
        public bool Reachable { get; set; } = true;
    }
}
