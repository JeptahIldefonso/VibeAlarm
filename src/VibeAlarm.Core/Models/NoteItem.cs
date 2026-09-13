using System;

namespace VibeAlarm.Models
{
    /// <summary>
    /// One note. Unlike <see cref="TaskItem"/> (a record in the shared tasks.json),
    /// each note is its own plain .txt file in <see cref="Services.AppPaths.NotesDir"/>
    /// — first line is the title, the rest is the content — so a note behaves like a
    /// normal text file on disk, openable in any editor.
    /// </summary>
    public sealed class NoteItem
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;

        /// <summary>When the note was last written. On load this is the file's
        /// last-write time — the disk is the source of truth.</summary>
        public DateTime UpdatedAt { get; set; }
    }
}
