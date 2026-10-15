using System;
using System.IO;
using System.Security.Cryptography;
using NAudio.Wave;

namespace VibeAlarm.Services
{
    public sealed record CustomSoundImportResult(
        bool Success,
        string? SoundPath,
        string? SoundName,
        string? ErrorMessage);

    /// <summary>
    /// Validates and imports user-provided custom alarm sounds.
    /// Verifies playable audio decoding and copies to %LOCALAPPDATA%\VibeAlarm\Assets\AlarmSounds
    /// with a 10 MB file size limit so original file movement never silences an alarm.
    /// </summary>
    public static class CustomAlarmSoundService
    {
        public const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB

        public static readonly string[] SupportedExtensions = { ".mp3", ".wav", ".m4a", ".flac" };

        public static CustomSoundImportResult ValidateAndImport(string sourceFilePath, string targetDir)
        {
            if (string.IsNullOrWhiteSpace(sourceFilePath))
            {
                return new CustomSoundImportResult(false, null, null, "No audio file selected.");
            }

            if (!File.Exists(sourceFilePath))
            {
                return new CustomSoundImportResult(false, null, null, "The selected file does not exist.");
            }

            string ext = Path.GetExtension(sourceFilePath).ToLowerInvariant();
            if (Array.IndexOf(SupportedExtensions, ext) < 0)
            {
                return new CustomSoundImportResult(false, null, null,
                    $"Unsupported audio format '{ext}'. Supported formats: .mp3, .wav, .m4a, .flac");
            }

            var fileInfo = new FileInfo(sourceFilePath);
            if (fileInfo.Length > MaxFileSizeBytes)
            {
                return new CustomSoundImportResult(false, null, null,
                    $"File size ({fileInfo.Length / (1024 * 1024.0):F1} MB) exceeds the 10 MB limit.");
            }

            // Validate that MediaFoundationReader can actually decode the audio
            try
            {
                using var reader = new MediaFoundationReader(sourceFilePath);
                byte[] testBuffer = new byte[1024];
                int read = reader.Read(testBuffer, 0, testBuffer.Length);
                if (read == 0)
                {
                    return new CustomSoundImportResult(false, null, null, "The audio file contains no playable data.");
                }
            }
            catch (Exception ex)
            {
                return new CustomSoundImportResult(false, null, null, $"Cannot decode audio file: {ex.Message}");
            }

            if (!Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            // Compute hash for deduplication and conflict-free naming
            string hash;
            using (var md5 = MD5.Create())
            using (var stream = File.OpenRead(sourceFilePath))
            {
                byte[] hashBytes = md5.ComputeHash(stream);
                hash = Convert.ToHexString(hashBytes)[..8].ToLowerInvariant();
            }

            string originalName = Path.GetFileNameWithoutExtension(sourceFilePath);
            string sanitizedName = string.Join("_", originalName.Split(Path.GetInvalidFileNameChars()));
            string targetFileName = $"{hash}_{sanitizedName}{ext}";
            string targetFilePath = Path.Combine(targetDir, targetFileName);

            try
            {
                if (!File.Exists(targetFilePath))
                {
                    File.Copy(sourceFilePath, targetFilePath, overwrite: true);
                }

                string displayName = Path.GetFileName(sourceFilePath);
                return new CustomSoundImportResult(true, targetFileName, displayName, null);
            }
            catch (Exception ex)
            {
                return new CustomSoundImportResult(false, null, null, $"Failed to copy custom sound: {ex.Message}");
            }
        }
    }
}
