using System.IO;
using VibeAlarm.Models;
using VibeAlarm.Services;
using Xunit;

namespace VibeAlarm.Tests
{
    /// <summary>
    /// Settings load-path forgiveness: out-of-range values stored in settings.json (hand-edited,
    /// older builds, corruption) are clamped into their valid ranges on load so the UI can never
    /// receive a nonsensical value. The former appearance ints (transparency/radius/density) are
    /// gone from the schema — extra JSON keys from older settings.json are simply ignored.
    /// </summary>
    public class SettingsServiceClampingTests
    {
        [Fact]
        public void Out_of_range_values_are_clamped_on_load()
        {
            string path = SettingsService.SettingsPath;
            string? backup = File.Exists(path) ? File.ReadAllText(path) : null;

            try
            {
                var hostile = new AppSettings
                {
                    Theme = "Light",
                    BackgroundOpacity = 7.5
                };
                SettingsService.Save(hostile);

                AppSettings loaded = SettingsService.Load();
                Assert.InRange(loaded.BackgroundOpacity, 0.0, 1.0);
                Assert.Equal(1.0, loaded.BackgroundOpacity); // oversized clamps to the maximum
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
