using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using VibeAlarm.Models;

namespace VibeAlarm.Services
{
    /// <summary>
    /// Persists user settings to a versioned, strongly-typed settings.json, and manages the
    /// "launch on Windows startup" registry entry. Backward compatible: an older flat
    /// {"Theme": "..."} file is migrated into the new <see cref="AppSettings"/> shape on load.
    /// </summary>
    public static class SettingsService
    {
        private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "VibeAlarm";

        public static string SettingsPath => Path.Combine(AppContext.BaseDirectory, "settings.json");

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

                    // Legacy format: a flat {"Theme":"..."} map. Wrap it into the new object.
                    AppSettings? settings;
                    try
                    {
                        settings = JsonSerializer.Deserialize<AppSettings>(json, ReadOptions);
                    }
                    catch (JsonException)
                    {
                        settings = null;
                    }

                    if (settings == null && TryReadLegacyFlat(json, out AppSettings? migrated) && migrated != null)
                    {
                        settings = migrated;
                    }

                    if (settings != null)
                    {
                        // Forgive stored values out of the valid ranges.
                        settings.BackgroundOpacity = Math.Clamp(settings.BackgroundOpacity, 0.0, 1.0);
                        // Appearance ints (§24): transparency percentages 0–100, radius 0–20px.
                        settings.GlassTransparency = Math.Clamp(settings.GlassTransparency, 0, 100);
                        settings.PanelTransparency = Math.Clamp(settings.PanelTransparency, 0, 100);
                        settings.CardTransparency = Math.Clamp(settings.CardTransparency, 0, 100);
                        settings.BorderTransparency = Math.Clamp(settings.BorderTransparency, 0, 100);
                        settings.CornerRadius = Math.Clamp(settings.CornerRadius, 0, 20);
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

        /// <summary>Reads the persisted theme name, or null when absent/unreadable.</summary>
        public static string? LoadThemeName() => Load().Theme;

        /// <summary>Writes the active theme name, preserving other settings fields.</summary>
        public static void SaveThemeName(string themeName)
        {
            AppSettings settings = Load();
            settings.Theme = themeName;
            Save(settings);
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
                    key.SetValue(AppName, $"\"{Application.ExecutablePath}\"");
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

        private static bool TryReadLegacyFlat(string json, out AppSettings? settings)
        {
            settings = null;
            try
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (dict == null)
                {
                    return false;
                }

                settings = new AppSettings
                {
                    Theme = dict.TryGetValue("Theme", out string? theme) ? theme : string.Empty
                };
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }
}