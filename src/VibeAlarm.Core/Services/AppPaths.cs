using System;
using System.Diagnostics;
using System.IO;

namespace VibeAlarm.Services
{
    /// <summary>
    /// The single owner of writable-data locations. Data lives under
    /// %LOCALAPPDATA%\VibeAlarm (per-user, writable regardless of where the exe is
    /// installed), NOT next to the exe — the legacy exe-adjacent layout breaks under a
    /// Program Files or MSIX install, and it also kept the two UI heads from sharing one
    /// task list during the WinForms→WinUI transition. Legacy files found beside the exe
    /// are migrated once, non-destructively: anything already in the data root always
    /// wins, and the migration is idempotent.
    /// </summary>
    public static class AppPaths
    {
        /// <summary>Per-user writable data root (%LOCALAPPDATA%\VibeAlarm).</summary>
        public static string DataRoot { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VibeAlarm");

        /// <summary>settings.json location.</summary>
        public static string SettingsPath { get; } = Path.Combine(DataRoot, "settings.json");

        /// <summary>tasks.json location.</summary>
        public static string TasksPath { get; } = Path.Combine(DataRoot, "tasks.json");

        /// <summary>Runtime-generated audio (the synthesized brown-noise/rain WAVs).
        /// Regenerated on demand, so this is a cache, not precious data.</summary>
        public static string AssetsCacheDir { get; } = Path.Combine(DataRoot, "Assets");

        static AppPaths()
        {
            try
            {
                Directory.CreateDirectory(DataRoot);
                Directory.CreateDirectory(AssetsCacheDir);
                MigrateLegacyData(AppContext.BaseDirectory, DataRoot);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"AppPaths initialization failed: {ex.Message}");
            }
        }

        /// <summary>
        /// One-time migration from the legacy exe-adjacent layout: copies settings.json,
        /// tasks.json, and the generated ambient WAVs across when the legacy copy is the
        /// most recently written one (a fresh data root, or an old data root that predates
        /// the actively-used exe-adjacent files — this app briefly lived in
        /// %LOCALAPPDATA% in an earlier era, so that case is real). Last writer wins;
        /// never deletes anything; safe to call repeatedly. Public and path-injected so
        /// tests can drive it against temp directories.
        /// </summary>
        public static void MigrateLegacyData(string legacyDir, string dataRoot)
        {
            MigrateFile(Path.Combine(legacyDir, "settings.json"), Path.Combine(dataRoot, "settings.json"));
            MigrateFile(Path.Combine(legacyDir, "tasks.json"), Path.Combine(dataRoot, "tasks.json"));

            string legacyAssets = Path.Combine(legacyDir, "Assets");
            string targetAssets = Path.Combine(dataRoot, "Assets");
            MigrateFile(Path.Combine(legacyAssets, "brown_noise.wav"), Path.Combine(targetAssets, "brown_noise.wav"));
            MigrateFile(Path.Combine(legacyAssets, "rain.wav"), Path.Combine(targetAssets, "rain.wav"));
        }

        private static void MigrateFile(string legacyPath, string targetPath)
        {
            try
            {
                if (!File.Exists(legacyPath))
                {
                    return;
                }

                if (!File.Exists(targetPath) ||
                    File.GetLastWriteTimeUtc(legacyPath) > File.GetLastWriteTimeUtc(targetPath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                    File.Copy(legacyPath, targetPath, overwrite: true);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Legacy data migration skipped for {legacyPath}: {ex.Message}");
            }
        }
    }
}
