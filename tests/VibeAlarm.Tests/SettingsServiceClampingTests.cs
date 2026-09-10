using System.IO;
using VibeAlarm.Models;
using VibeAlarm.Services;
using Xunit;

namespace VibeAlarm.Tests
{
    /// <summary>
    /// Settings load-path forgiveness: out-of-range appearance values stored in settings.json
    /// (hand-edited, older builds, corruption) are clamped into their valid ranges on load so
    /// the UI can never receive a nonsensical transparency/radius value.
    /// </summary>
    public class SettingsServiceClampingTests
    {
        [Fact]
        public void Out_of_range_appearance_values_are_clamped_on_load()
        {
            string path = SettingsService.SettingsPath;
            string? backup = File.Exists(path) ? File.ReadAllText(path) : null;

            try
            {
                var hostile = new AppSettings
                {
                    Theme = "Light",
                    GlassTransparency = 400,
                    PanelTransparency = -25,
                    CardTransparency = 999,
                    BorderTransparency = -1,
                    CornerRadius = 55,
                    BackgroundOpacity = 7.5
                };
                SettingsService.Save(hostile);

                AppSettings loaded = SettingsService.Load();
                Assert.InRange(loaded.GlassTransparency, 0, 100);
                Assert.InRange(loaded.PanelTransparency, 0, 100);
                Assert.InRange(loaded.CardTransparency, 0, 100);
                Assert.InRange(loaded.BorderTransparency, 0, 100);
                Assert.InRange(loaded.CornerRadius, 0, 20);
                Assert.InRange(loaded.BackgroundOpacity, 0.0, 1.0);

                // Direction of the clamp: negatives go to the minimum, oversized to the maximum.
                Assert.Equal(100, loaded.GlassTransparency);
                Assert.Equal(0, loaded.PanelTransparency);
                Assert.Equal(20, loaded.CornerRadius);
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
