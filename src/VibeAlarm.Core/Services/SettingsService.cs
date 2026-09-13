using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using VibeAlarm.Models;

namespace VibeAlarm.Services
{
    /// <summary>
    /// Persists user settings to a versioned, strongly-typed settings.json, and manages the
    /// "launch on Windows startup" registry entry. Backward compatible: keys removed from
    /// the schema over time (Theme, EnableAnimations, BackgroundImagePath, …) are simply
    /// ignored on deserialize, so an older settings.json loads cleanly with the current
    /// shape.
    /// </summary>
    public static class SettingsService
    {
        private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "VibeAlarm";

        public static string SettingsPath => AppPaths.SettingsPath;

        private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

        /// <summary>Reads the full settings object, migrating any legacy flat-file format.</summary>
        public static AppSettings Load()
        {
            try
            {
                string filePath = SettingsPath;
                if (File.Exists(filePath))
                {
                    string json = File.ReadAllText(filePath);
                    AppSettings? settings = null;
                    try
                    {
                        settings = JsonSerializer.Deserialize<AppSettings>(json, ReadOptions);
                    }
                    catch (JsonException)
                    {
                        // Malformed JSON falls through to the defaults below.
                    }

                    if (settings != null)
                    {
                        // Extra JSON keys from older settings.json files (Theme,
                        // EnableAnimations, BackgroundOpacity, ApplyMonochromeFilterToBackground,
                        // BackgroundImagePath, the former appearance ints) are simply
                        // ignored on deserialize — removals stay backward compatible with
                        // no migration step.
                        return settings;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load settings: {ex.Message}");
            }

            return new AppSettings();
        }

        /// <summary>Persists the full settings object.</summary>
        public static void Save(AppSettings settings)
        {
            try
            {
                string json = JsonSerializer.Serialize(settings);
                File.WriteAllText(SettingsPath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to save settings: {ex.Message}");
            }
        }

        /// <summary>Returns true when VibeAlarm is registered to launch on Windows startup.</summary>
        public static bool IsRunOnStartupEnabled()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey, false);
                return key?.GetValue(AppName) != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Adds or removes the Windows startup registration for the application.</summary>
        public static void SetRunOnStartup(bool enable)
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey, true);
                if (key == null)
                {
                    return;
                }

                if (enable)
                {
                    // Environment.ProcessPath works for every UI head (and stays correct
                    // under dotnet run); BaseDirectory is the fallback for exotic hosts.
                    string exePath = Environment.ProcessPath ?? AppContext.BaseDirectory;
                    key.SetValue(AppName, $"\"{exePath}\"");
                }
                else
                {
                    key.DeleteValue(AppName, false);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to set startup settings: {ex.Message}");
            }
        }
    }
}