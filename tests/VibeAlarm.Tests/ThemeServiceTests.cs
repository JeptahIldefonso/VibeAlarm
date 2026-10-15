using VibeAlarm.Services;
using VibeAlarm.UI.Theming;
using Xunit;

namespace VibeAlarm.Tests
{
    /// <summary>
    /// The accent-only theme service: ApplyAccent resolves the selected
    /// accent's Base/Hover/OnAccent from AccentCatalog — with unknown-key
    /// tolerance and idempotent re-application.
    /// </summary>
    [Collection("ThemeService")]
    public class ThemeServiceTests
    {
        [Fact]
        public void A_fresh_service_starts_on_the_shipped_default_accent()
        {
            var service = new ThemeService();
            Assert.Equal(AccentCatalog.DefaultKey, service.CurrentAccent.Key);
            Assert.Equal(AccentCatalog.Default.Base, service.CurrentAccent.Base);
            Assert.Equal(AccentCatalog.Default.Hover, service.CurrentAccent.Hover);
            Assert.Equal(AccentCatalog.Default.OnAccent, service.CurrentAccent.OnAccent);
        }

        [Fact]
        public void ApplyAccent_publishes_the_accent_triple()
        {
            var service = ThemeService.Shared;
            try
            {
                AccentOption applied = service.ApplyAccent("Violet System");
                Assert.Equal("Violet System", applied.Key);
                Assert.Equal("Violet System", service.CurrentAccent.Key);
                Assert.Equal(applied.Base, service.CurrentAccent.Base);
                Assert.Equal(applied.Hover, service.CurrentAccent.Hover);
                Assert.Equal(applied.OnAccent, service.CurrentAccent.OnAccent);
            }
            finally
            {
                service.ApplyAccent(AccentCatalog.DefaultKey);
            }
        }

        [Fact]
        public void ApplyAccent_is_idempotent_across_repeated_and_alternating_applications()
        {
            var service = ThemeService.Shared;
            try
            {
                service.ApplyAccent("Sunset Red");
                service.ApplyAccent("Neo Blue");
                service.ApplyAccent("Neo Blue");
                Assert.Equal(AccentCatalog.Resolve("Neo Blue").Base, service.CurrentAccent.Base);

                service.ApplyAccent("Solar Amber");
                service.ApplyAccent("Neo Blue");
                Assert.Equal(AccentCatalog.Resolve("Neo Blue").Base, service.CurrentAccent.Base);
            }
            finally
            {
                service.ApplyAccent(AccentCatalog.DefaultKey);
            }
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not-a-real-accent")]
        public void ApplyAccent_unknown_keys_fall_back_to_the_default(string? key)
        {
            var service = ThemeService.Shared;
            try
            {
                AccentOption applied = service.ApplyAccent(key);
                Assert.Equal(AccentCatalog.DefaultKey, applied.Key);
                Assert.Equal(AccentCatalog.Default.Base, service.CurrentAccent.Base);
            }
            finally
            {
                service.ApplyAccent(AccentCatalog.DefaultKey);
            }
        }
    }
}
