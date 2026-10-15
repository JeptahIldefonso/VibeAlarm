using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using VibeAlarm.Models;

namespace VibeAlarm.Services
{
    /// <summary>
    /// Wrapper for the persisted music library data stored in music-library.json.
    /// </summary>
    public sealed class MusicLibraryData
    {
        public int SchemaVersion { get; set; } = 1;
        public List<LibraryFolder> Folders { get; set; } = new();
        public List<MusicTrack> Tracks { get; set; } = new();
    }

    /// <summary>
    /// Persists and restores the music library index (folders and scanned tracks)
    /// to/from music-library.json in AppPaths.DataRoot, using safe atomic writes.
    /// </summary>
    public static class MusicLibraryStorageService
    {
        private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };
        private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

        /// <summary>
        /// Reads the persisted library index. Returns a new empty object if the file doesn't exist
        /// or cannot be parsed.
        /// </summary>
        public static MusicLibraryData Load()
        {
            try
            {
                string filePath = AppPaths.MusicLibraryPath;
                if (File.Exists(filePath))
                {
                    string json = File.ReadAllText(filePath);
                    var data = JsonSerializer.Deserialize<MusicLibraryData>(json, ReadOptions);
                    if (data != null)
                    {
                        return data;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load music library index: {ex.Message}");
            }

            return new MusicLibraryData();
        }

        /// <summary>
        /// Atomically writes the music library data to music-library.json.
        /// </summary>
        public static void Save(MusicLibraryData data)
        {
            try
            {
                string filePath = AppPaths.MusicLibraryPath;
                string tempPath = filePath + ".tmp";
                string json = JsonSerializer.Serialize(data, WriteOptions);

                // Write to temp file first, then atomically replace
                File.WriteAllText(tempPath, json);
                if (File.Exists(filePath))
                {
                    File.Replace(tempPath, filePath, null);
                }
                else
                {
                    File.Move(tempPath, filePath);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to save music library index: {ex.Message}");
            }
        }
    }
}
