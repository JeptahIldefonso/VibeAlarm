using System;
using System.IO;
using VibeAlarm.Services;
using Xunit;

namespace VibeAlarm.Tests
{
    /// <summary>
    /// The one-time exe-adjacent → per-user-data-root migration: legacy settings/tasks
    /// (and the generated ambient WAVs) are copied when the data root is missing them,
    /// existing data-root files always win, and the migration is idempotent.
    /// Driven against temp directories via the path-injected overload — the real static
    /// initializer runs the same code against %LOCALAPPDATA%\VibeAlarm.
    /// </summary>
    public class AppPathsMigrationTests : IDisposable
    {
        private readonly string tempRoot;

        public AppPathsMigrationTests()
        {
            tempRoot = Path.Combine(Path.GetTempPath(), "vibealarm-mig-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(tempRoot, true);
            }
            catch
            {
                // Best-effort cleanup; temp dirs are disposable.
            }
        }

        [Fact]
        public void Legacy_files_are_copied_when_the_data_root_is_missing_them()
        {
            string legacy = Path.Combine(tempRoot, "legacy");
            string data = Path.Combine(tempRoot, "data");
            Directory.CreateDirectory(Path.Combine(legacy, "Assets"));
            File.WriteAllText(Path.Combine(legacy, "settings.json"), "{\"accent\":\"x\"}");
            File.WriteAllText(Path.Combine(legacy, "tasks.json"), "[]");
            File.WriteAllText(Path.Combine(legacy, "Assets", "brown_noise.wav"), "wav-bytes");

            AppPaths.MigrateLegacyData(legacy, data);

            Assert.Equal("{\"accent\":\"x\"}", File.ReadAllText(Path.Combine(data, "settings.json")));
            Assert.Equal("[]", File.ReadAllText(Path.Combine(data, "tasks.json")));
            Assert.Equal("wav-bytes", File.ReadAllText(Path.Combine(data, "Assets", "brown_noise.wav")));
        }

        [Fact]
        public void A_newer_legacy_file_replaces_an_older_data_root_file()
        {
            // The real-world case: an ancient %LOCALAPPDATA% data root (an earlier era of
            // the app lived there) must lose to the actively-used exe-adjacent files.
            string legacy = Path.Combine(tempRoot, "legacy");
            string data = Path.Combine(tempRoot, "data");
            Directory.CreateDirectory(legacy);
            Directory.CreateDirectory(data);
            string legacyFile = Path.Combine(legacy, "settings.json");
            string targetFile = Path.Combine(data, "settings.json");
            File.WriteAllText(targetFile, "ancient-content");
            File.WriteAllText(legacyFile, "current-content");
            File.SetLastWriteTimeUtc(targetFile, DateTime.UtcNow.AddDays(-30));
            File.SetLastWriteTimeUtc(legacyFile, DateTime.UtcNow);

            AppPaths.MigrateLegacyData(legacy, data);

            Assert.Equal("current-content", File.ReadAllText(targetFile));
        }

        [Fact]
        public void An_older_legacy_file_does_not_touch_a_newer_data_root_file()
        {
            // Once the app writes to the data root, the frozen legacy copies stay frozen.
            string legacy = Path.Combine(tempRoot, "legacy");
            string data = Path.Combine(tempRoot, "data");
            Directory.CreateDirectory(legacy);
            Directory.CreateDirectory(data);
            string legacyFile = Path.Combine(legacy, "tasks.json");
            string targetFile = Path.Combine(data, "tasks.json");
            File.WriteAllText(legacyFile, "old-content");
            File.WriteAllText(targetFile, "live-content");
            File.SetLastWriteTimeUtc(legacyFile, DateTime.UtcNow.AddDays(-30));
            File.SetLastWriteTimeUtc(targetFile, DateTime.UtcNow);

            AppPaths.MigrateLegacyData(legacy, data);

            Assert.Equal("live-content", File.ReadAllText(targetFile));
        }

        [Fact]
        public void Migration_is_idempotent()
        {
            string legacy = Path.Combine(tempRoot, "legacy");
            string data = Path.Combine(tempRoot, "data");
            Directory.CreateDirectory(legacy);
            File.WriteAllText(Path.Combine(legacy, "tasks.json"), "[]");

            AppPaths.MigrateLegacyData(legacy, data);
            AppPaths.MigrateLegacyData(legacy, data);

            Assert.True(File.Exists(Path.Combine(data, "tasks.json")));
        }

        [Fact]
        public void A_missing_legacy_layout_is_skipped_without_throwing()
        {
            string legacy = Path.Combine(tempRoot, "does-not-exist");
            string data = Path.Combine(tempRoot, "data");

            AppPaths.MigrateLegacyData(legacy, data);

            Assert.False(Directory.Exists(Path.Combine(data, "Assets")) || File.Exists(Path.Combine(data, "settings.json")));
        }
    }
}
