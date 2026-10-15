using System;
using System.IO;
using System.Text.Json;
using VibeAlarm.Models;
using VibeAlarm.Services;
using Xunit;

namespace VibeAlarm.Tests
{
    public class AlarmSettingsAndAudioTests : IDisposable
    {
        private readonly string tempDir;

        public AlarmSettingsAndAudioTests()
        {
            tempDir = Path.Combine(Path.GetTempPath(), "VibeAlarm_AlarmTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
            catch { }
        }

        // ---- 1. Tone Generator Tests ----

        [Theory]
        [InlineData("Classic Beep")]
        [InlineData("Soft Chime")]
        [InlineData("Digital Pulse")]
        [InlineData("Rising Tone")]
        public void ToneGenerator_creates_valid_wav_file_for_each_tone(string toneName)
        {
            string outputPath = Path.Combine(tempDir, ToneGenerator.GetToneFileName(toneName));
            ToneGenerator.GenerateToneFile(outputPath, toneName);

            Assert.True(File.Exists(outputPath));
            var fi = new FileInfo(outputPath);
            Assert.True(fi.Length > 1000); // Has realistic audio data

            // Verify RIFF WAV header
            using var fs = File.OpenRead(outputPath);
            byte[] header = new byte[12];
            fs.ReadExactly(header, 0, 12);
            string riff = System.Text.Encoding.ASCII.GetString(header, 0, 4);
            string wave = System.Text.Encoding.ASCII.GetString(header, 8, 4);
            Assert.Equal("RIFF", riff);
            Assert.Equal("WAVE", wave);
        }

        [Fact]
        public void EnsureBuiltInTones_creates_all_four_tones_idempotently()
        {
            string tonesDir = Path.Combine(tempDir, "AlarmSounds");
            ToneGenerator.EnsureBuiltInTones(tonesDir);

            foreach (var tone in ToneGenerator.BuiltInTones)
            {
                string path = ToneGenerator.GetToneFilePath(tonesDir, tone);
                Assert.True(File.Exists(path));
                Assert.True(new FileInfo(path).Length > 0);
            }

            // Running again should be safe and idempotent
            ToneGenerator.EnsureBuiltInTones(tonesDir);
            foreach (var tone in ToneGenerator.BuiltInTones)
            {
                string path = ToneGenerator.GetToneFilePath(tonesDir, tone);
                Assert.True(File.Exists(path));
            }
        }

        // ---- 2. Custom Alarm Sound Validator Tests ----

        [Fact]
        public void ValidateAndImport_rejects_missing_file()
        {
            var res = CustomAlarmSoundService.ValidateAndImport(Path.Combine(tempDir, "ghost.mp3"), tempDir);
            Assert.False(res.Success);
            Assert.Contains("does not exist", res.ErrorMessage);
        }

        [Fact]
        public void ValidateAndImport_rejects_unsupported_extension()
        {
            string txtFile = Path.Combine(tempDir, "sound.txt");
            File.WriteAllText(txtFile, "hello");
            var res = CustomAlarmSoundService.ValidateAndImport(txtFile, tempDir);
            Assert.False(res.Success);
            Assert.Contains("Unsupported audio format", res.ErrorMessage);
        }

        [Fact]
        public void ValidateAndImport_rejects_empty_corrupt_file()
        {
            string corruptWav = Path.Combine(tempDir, "corrupt.wav");
            File.WriteAllBytes(corruptWav, new byte[20]); // invalid header
            var res = CustomAlarmSoundService.ValidateAndImport(corruptWav, tempDir);
            Assert.False(res.Success);
            Assert.Contains("Cannot decode audio file", res.ErrorMessage);
        }

        [Fact]
        public void ValidateAndImport_imports_valid_wav_file_with_hash_name()
        {
            string sourceWav = Path.Combine(tempDir, "my_custom_alarm.wav");
            ToneGenerator.GenerateToneFile(sourceWav, "Soft Chime");

            string targetDir = Path.Combine(tempDir, "Imported");
            var res = CustomAlarmSoundService.ValidateAndImport(sourceWav, targetDir);

            Assert.True(res.Success);
            Assert.NotNull(res.SoundPath);
            Assert.Contains("my_custom_alarm.wav", res.SoundPath);
            Assert.Equal("my_custom_alarm.wav", res.SoundName);
            Assert.True(File.Exists(Path.Combine(targetDir, res.SoundPath!)));
        }

        // ---- 3. Fallback Chain Tests ----

        [Fact]
        public void Fallback_chain_falls_back_to_default_when_custom_file_is_missing()
        {
            using var player = new AlarmAudioPlayer();
            var settings = new AppSettings
            {
                AlarmSound = "Custom",
                CustomAlarmSoundPath = "non_existent_custom_alarm.mp3",
                AlarmVolume = 80
            };

            // Assets dir contains default alarm.wav
            string assetsDir = Path.Combine(tempDir, "Assets");
            Directory.CreateDirectory(assetsDir);
            string defaultWav = Path.Combine(assetsDir, "alarm.wav");
            ToneGenerator.GenerateToneFile(defaultWav, "Classic Beep");

            var level = player.PlayAlarm(settings, assetsDir);

            // Should safely fall back to Level 2 (DefaultSound)
            Assert.Equal(AlarmFallbackLevel.DefaultSound, level);
            Assert.Equal("Default", player.ActiveSoundName);
            player.Stop();
        }

        [Fact]
        public void Fallback_chain_falls_back_to_system_beep_when_default_file_is_also_missing()
        {
            using var player = new AlarmAudioPlayer();
            var settings = new AppSettings
            {
                AlarmSound = "Custom",
                CustomAlarmSoundPath = "missing.mp3",
                AlarmVolume = 80
            };

            // Assets dir has no alarm.wav
            string emptyAssetsDir = Path.Combine(tempDir, "EmptyAssets");
            Directory.CreateDirectory(emptyAssetsDir);

            var level = player.PlayAlarm(settings, emptyAssetsDir);

            // Should safely fall back to Level 3 (SystemBeep) without crashing
            Assert.Equal(AlarmFallbackLevel.SystemBeep, level);
            Assert.Equal("System Beep", player.ActiveSoundName);
            player.Stop();
        }

        [Fact]
        public void Fallback_chain_plays_chosen_sound_when_file_exists()
        {
            using var player = new AlarmAudioPlayer();
            string soundsDir = AppPaths.AlarmSoundsDir;
            ToneGenerator.EnsureBuiltInTones(soundsDir);

            var settings = new AppSettings
            {
                AlarmSound = "Soft Chime",
                AlarmVolume = 75
            };

            string assetsDir = Path.Combine(tempDir, "Assets");
            Directory.CreateDirectory(assetsDir);

            var level = player.PlayAlarm(settings, assetsDir);

            Assert.Equal(AlarmFallbackLevel.ChosenSound, level);
            Assert.Equal("Soft Chime", player.ActiveSoundName);
            player.Stop();
        }

        // ---- 4. Settings Migration & Persistence Tests ----

        [Fact]
        public void Legacy_settings_json_without_alarm_fields_hydrates_with_safe_defaults()
        {
            string legacyJson = """
            {
              "SchemaVersion": 1,
              "AccentColor": "Neo Blue",
              "MinimizeToTray": true,
              "EnableAlarmSound": true,
              "EnableNotifications": true
            }
            """;

            var hydrated = JsonSerializer.Deserialize<AppSettings>(legacyJson);

            Assert.NotNull(hydrated);
            Assert.Equal("Default", hydrated!.AlarmSound);
            Assert.Null(hydrated.CustomAlarmSoundPath);
            Assert.Null(hydrated.CustomAlarmSoundName);
            Assert.Equal(80, hydrated.AlarmVolume);
            Assert.Equal(5, hydrated.AlarmDurationMinutes);
            Assert.Equal(9, hydrated.SnoozeDurationMinutes);
            Assert.False(hydrated.GradualVolumeIncrease);
        }

        [Fact]
        public void AppSettings_round_trip_preserves_all_alarm_fields()
        {
            var original = new AppSettings
            {
                AlarmSound = "Rising Tone",
                CustomAlarmSoundPath = "custom_test.wav",
                CustomAlarmSoundName = "Custom Test",
                AlarmVolume = 95,
                AlarmDurationMinutes = 10,
                SnoozeDurationMinutes = 15,
                GradualVolumeIncrease = true
            };

            string json = JsonSerializer.Serialize(original);
            var deserialized = JsonSerializer.Deserialize<AppSettings>(json);

            Assert.NotNull(deserialized);
            Assert.Equal("Rising Tone", deserialized!.AlarmSound);
            Assert.Equal("custom_test.wav", deserialized.CustomAlarmSoundPath);
            Assert.Equal("Custom Test", deserialized.CustomAlarmSoundName);
            Assert.Equal(95, deserialized.AlarmVolume);
            Assert.Equal(10, deserialized.AlarmDurationMinutes);
            Assert.Equal(15, deserialized.SnoozeDurationMinutes);
            Assert.True(deserialized.GradualVolumeIncrease);
        }
    }
}
