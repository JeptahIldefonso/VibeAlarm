using System.IO;
using VibeAlarm.Models;
using VibeAlarm.Services;
using Xunit;

namespace VibeAlarm.Tests
{
    /// <summary>
    /// Settings load-path backward compatibility: keys removed from the schema
    /// (Theme, EnableAnimations, BackgroundImagePath, BackgroundOpacity,
    /// ApplyMonochromeFilterToBackground, and the older appearance ints) must be ignored on
    /// deserialize — an older settings.json loads cleanly with the current shape, never
    /// throws, and its still-valid fields survive.
    /// </summary>
    public class SettingsServiceBackwardCompatTests
    {
        [Fact]
        public void Removed_keys_in_an_old_settings_file_are_ignored_on_load()
        {
            string path = SettingsService.SettingsPath;
            string? backup = File.Exists(path) ? File.ReadAllText(path) : null;

            try
            {
                // A settings.json exactly as an older build would have written it, removed
                // keys included (one out of range, one flipped off, to catch any residual
                // clamping/consuming path).
                File.WriteAllText(path,
                    "{\"SchemaVersion\":2,\"Theme\":\"Dark\",\"MinimizeToTray\":true," +
                    "\"BackgroundImagePath\":\"\",\"BackgroundOpacity\":7.5," +
                    "\"ApplyMonochromeFilterToBackground\":false,\"EnableAnimations\":false," +
                    "\"LastActiveView\":\"Tasks\",\"LastViewedCalendarMonth\":\"\",\"EnableNotifications\":false," +
                    "\"EnableAlarmSound\":true,\"ConfirmBeforeDelete\":true}");

                AppSettings loaded = SettingsService.Load();
                Assert.True(loaded.MinimizeToTray);       // surviving field round-trips
                Assert.False(loaded.EnableNotifications); // still-live toggle round-trips
            }
            finally
            {
                if (backup != null)
                {
                    File.WriteAllText(path, backup);
                }
                else if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }
}
