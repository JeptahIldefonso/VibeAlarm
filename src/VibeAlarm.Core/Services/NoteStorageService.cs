using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using VibeAlarm.Models;

namespace VibeAlarm.Services
{
    /// <summary>
    /// Persists notes as individual plain .txt files in <see cref="AppPaths.NotesDir"/>
    /// — one file per note, named {Id}.txt. The first line of the file is the note's
    /// title, everything after the first line is its content, so a note is a real
    /// text file that opens and reads sensibly in any editor (unlike tasks, which are
    /// records in one shared JSON). The file's last-write time is the note's
    /// UpdatedAt — the disk is the source of truth.
    ///
    /// Directory-injected overloads keep the class unit-testable against temp
    /// directories (same pattern as AppPaths.MigrateLegacyData).
    /// </summary>
    public static class NoteStorageService
    {
        /// <summary>
        /// Reads every note, most recently updated first. Returns an empty list if
        /// the folder is missing or unreadable.
        /// </summary>
        public static List<NoteItem> Load(string? dir = null)
        {
            string notesDir = dir ?? AppPaths.NotesDir;
            var notes = new List<NoteItem>();
            try
            {
                if (!Directory.Exists(notesDir))
                {
                    return notes;
                }

                foreach (string filePath in Directory.EnumerateFiles(notesDir, "*.txt"))
                {
                    NoteItem? note = ReadFile(filePath);
                    if (note != null)
                    {
                        notes.Add(note);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load notes: {ex.Message}");
            }

            return notes.OrderByDescending(n => n.UpdatedAt).ToList();
        }

        /// <summary>Writes one note to {Id}.txt — first line title, then content.
        /// Creates or overwrites.</summary>
        public static void Save(NoteItem note, string? dir = null)
        {
            string notesDir = dir ?? AppPaths.NotesDir;
            try
            {
                Directory.CreateDirectory(notesDir);
                // The title owns the first line — a title that somehow contains a
                // newline would silently split the file format, so it is flattened.
                string title = note.Title.Replace("\r\n", " ").Replace("\r", " ").Replace("\n", " ");
                File.WriteAllText(Path.Combine(notesDir, $"{note.Id}.txt"), $"{title}\n{note.Content}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to save note {note.Id}: {ex.Message}");
            }
        }

        /// <summary>Deletes one note's file. Missing file is not an error.</summary>
        public static void Delete(string id, string? dir = null)
        {
            try
            {
                string filePath = Path.Combine(dir ?? AppPaths.NotesDir, $"{id}.txt");
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to delete note {id}: {ex.Message}");
            }
        }

        /// <summary>Parses one note file: filename stem is the Id, first line the
        /// Title, the remainder the Content, last-write time the UpdatedAt. Null when
        /// the file cannot be read.</summary>
        private static NoteItem? ReadFile(string filePath)
        {
            try
            {
                string text = File.ReadAllText(filePath);
                int split = text.IndexOf('\n');
                return new NoteItem
                {
                    Id = Path.GetFileNameWithoutExtension(filePath),
                    Title = split < 0 ? text : text[..split],
                    Content = split < 0 ? string.Empty : text[(split + 1)..],
                    UpdatedAt = File.GetLastWriteTime(filePath),
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to read note file {filePath}: {ex.Message}");
                return null;
            }
        }
    }
}
