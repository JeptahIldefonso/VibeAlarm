using VibeAlarm.Models;
using VibeAlarm.Services;
using Xunit;

namespace VibeAlarm.Tests
{
    /// <summary>
    /// "System" theme mode: a MODE stored in settings that resolves to the Light or Dark
    /// preset via the OS preference — the catalog itself stays at exactly two presets.
    /// </summary>
    public class SystemThemeModeTests
    {
        [Theory]
        [InlineData(true, "Dark")]
        [InlineData(false, "Light")]
        public void System_mode_resolves_via_the_os_preference(bool osDark, string expectedPreset)
        {
            SystemThemeProvider.ForceDark(osDark);
            try
            {
                ThemePreset resolved = ThemeService.Shared.ResolveEffective(SystemThemeProvider.SystemModeName);
                Assert.Equal(expectedPreset, resolved.Name);
            }
            finally
            {
                SystemThemeProvider.ForceDark(null);
            }
        }

        [Fact]
        public void System_mode_is_a_mode_not_a_preset()
        {
            Assert.Equal(2, ThemeService.Shared.Presets.Count);
            Assert.Null(ThemeService.Shared.FindByName(SystemThemeProvider.SystemModeName));
            Assert.True(SystemThemeProvider.IsSystemMode("system"));
            Assert.True(SystemThemeProvider.IsSystemMode("System"));
            Assert.False(SystemThemeProvider.IsSystemMode("Dark"));
            Assert.False(SystemThemeProvider.IsSystemMode(null));
        }

        [Fact]
        public void Non_system_names_still_resolve_directly()
        {
            SystemThemeProvider.ForceDark(true);
            try
            {
                Assert.Equal("Light", ThemeService.Shared.ResolveEffective("Light").Name);
                Assert.Equal("Dark", ThemeService.Shared.ResolveEffective("Dark").Name);
                // Unknown names keep the legacy bucketing (default Light).
                Assert.Equal("Light", ThemeService.Shared.ResolveEffective("Midnight-But-New").Name);
            }
            finally
            {
                SystemThemeProvider.ForceDark(null);
            }
        }
    }
}
