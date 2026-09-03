using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using VibeAlarm.Models;

namespace VibeAlarm.Services
{
    /// <summary>
    /// Persists and restores the task list to/from tasks.json in the application directory.
    /// </summary>
    public static class TaskStorageService
    {
        private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

        /// <summary>
        /// Reads the persisted task list. Returns an empty list if no file exists or it cannot be parsed.
        /// </summary>
        public static List<TaskItem> Load()
        {
            try
            {
                string filePath = Path.Combine(AppContext.BaseDirectory, "tasks.json");
                if (File.Exists(filePath))
                {
                    string json = File.ReadAllText(filePath);
                    var list = JsonSerializer.Deserialize<List<TaskItem>>(json);
                    if (list != null)
                    {
                        return list;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load tasks: {ex.Message}");
            }

            return new List<TaskItem>();
        }

        /// <summary>
        /// Writes the task list to tasks.json in the application directory.
        /// </summary>
        public static void Save(IEnumerable<TaskItem> tasks)
        {
            try
            {
                string filePath = Path.Combine(AppContext.BaseDirectory, "tasks.json");
                string json = JsonSerializer.Serialize(tasks, WriteOptions);
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to save tasks: {ex.Message}");
            }
        }
    }
}
