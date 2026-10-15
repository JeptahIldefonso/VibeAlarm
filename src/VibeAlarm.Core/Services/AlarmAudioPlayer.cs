using System;
using System.IO;
using System.Media;
using System.Threading;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using VibeAlarm.Models;

namespace VibeAlarm.Services
{
    /// <summary>
    /// Tracks which stage of the reliability fallback chain succeeded for alarm playback.
    /// </summary>
    public enum AlarmFallbackLevel
    {
        None = 0,
        ChosenSound = 1,
        DefaultSound = 2,
        SystemBeep = 3
    }

    /// <summary>
    /// NAudio-backed alarm playback engine providing:
    /// 1. Reliable fallback chain: Chosen Sound -> Default Sound -> System Beep.
    /// 2. Safe volume controls (never silent) and optional gradual volume ramp.
    /// 3. Ring duration auto-stop timer.
    /// 4. Non-blocking preview playback.
    /// 5. Offline audio hardware teardown when idle.
    /// </summary>
    public sealed class AlarmAudioPlayer : IDisposable
    {
        private readonly object syncLock = new();

        private WaveOutEvent? outputDevice;
        private WaveStream? readerStream;
        private VolumeSampleProvider? volumeProvider;
        private System.Threading.Timer? rampTimer;
        private System.Threading.Timer? durationTimer;
        private System.Threading.Timer? previewTimer;

        private float targetVolume = 0.8f;
        private float currentVolume = 0.8f;
        private bool isRamping;
        private bool disposed;

        public bool IsPlaying { get; private set; }
        public bool IsPreviewing { get; private set; }
        public AlarmFallbackLevel ActiveFallbackLevel { get; private set; } = AlarmFallbackLevel.None;
        public string? ActiveSoundName { get; private set; }

        public event Action? PlaybackStopped;

        public AlarmFallbackLevel PlayAlarm(AppSettings settings, string defaultAssetsDir)
        {
            lock (syncLock)
            {
                StopInternal();

                int volume = Math.Clamp(settings.AlarmVolume, 10, 100);
                targetVolume = volume / 100f;
                currentVolume = settings.GradualVolumeIncrease ? Math.Min(0.15f, targetVolume) : targetVolume;
                isRamping = settings.GradualVolumeIncrease;

                // Level 1: Chosen Sound
                string? chosenPath = ResolveSoundPath(settings.AlarmSound, settings.CustomAlarmSoundPath, defaultAssetsDir);
                if (!string.IsNullOrEmpty(chosenPath) && File.Exists(chosenPath))
                {
                    if (TryPlayAudioFile(chosenPath, isLooping: true))
                    {
                        ActiveFallbackLevel = AlarmFallbackLevel.ChosenSound;
                        ActiveSoundName = settings.AlarmSound;
                        ArmDurationTimer(settings.AlarmDurationMinutes);
                        if (isRamping) StartVolumeRamp();
                        return ActiveFallbackLevel;
                    }
                    LogWarning($"Alarm chosen sound '{settings.AlarmSound}' at '{chosenPath}' failed to play. Falling back to default sound.");
                }
                else
                {
                    LogWarning($"Alarm chosen sound '{settings.AlarmSound}' file not found ('{chosenPath}'). Falling back to default sound.");
                }

                // Level 2: Default Sound (alarm.wav)
                string defaultPath = Path.Combine(defaultAssetsDir, "alarm.wav");
                if (File.Exists(defaultPath))
                {
                    if (TryPlayAudioFile(defaultPath, isLooping: true))
                    {
                        ActiveFallbackLevel = AlarmFallbackLevel.DefaultSound;
                        ActiveSoundName = "Default";
                        ArmDurationTimer(settings.AlarmDurationMinutes);
                        if (isRamping) StartVolumeRamp();
                        return ActiveFallbackLevel;
                    }
                    LogWarning($"Default alarm sound at '{defaultPath}' failed to play. Falling back to system beep.");
                }
                else
                {
                    LogWarning($"Default alarm sound file '{defaultPath}' missing. Falling back to system beep.");
                }

                // Level 3: System Beep
                try
                {
                    SystemSounds.Beep.Play();
                }
                catch (Exception ex)
                {
                    LogWarning($"System beep failed: {ex.Message}");
                }

                ActiveFallbackLevel = AlarmFallbackLevel.SystemBeep;
                ActiveSoundName = "System Beep";
                IsPlaying = true;
                return ActiveFallbackLevel;
            }
        }

        public bool PlayPreview(string soundType, string? customSoundPath, int volumePercent, string defaultAssetsDir)
        {
            lock (syncLock)
            {
                StopInternal();

                int volume = Math.Clamp(volumePercent, 10, 100);
                targetVolume = volume / 100f;
                currentVolume = targetVolume;
                isRamping = false;

                string? soundPath = ResolveSoundPath(soundType, customSoundPath, defaultAssetsDir);
                if (string.IsNullOrEmpty(soundPath) || !File.Exists(soundPath))
                {
                    LogWarning($"Preview sound file not found for '{soundType}' ('{soundPath}').");
                    return false;
                }

                if (!TryPlayAudioFile(soundPath, isLooping: true))
                {
                    LogWarning($"Preview failed to play audio file '{soundPath}'.");
                    return false;
                }

                IsPreviewing = true;
                ActiveSoundName = soundType;

                // Stop preview automatically after 5 seconds
                previewTimer?.Dispose();
                previewTimer = new System.Threading.Timer(_ =>
                {
                    StopPreview();
                }, null, 5000, Timeout.Infinite);

                return true;
            }
        }

        public void StopPreview()
        {
            lock (syncLock)
            {
                if (IsPreviewing)
                {
                    StopInternal();
                }
            }
        }

        public void Stop()
        {
            lock (syncLock)
            {
                StopInternal();
            }
        }

        private void StopInternal()
        {
            rampTimer?.Dispose();
            rampTimer = null;

            durationTimer?.Dispose();
            durationTimer = null;

            previewTimer?.Dispose();
            previewTimer = null;

            if (outputDevice != null)
            {
                try
                {
                    outputDevice.PlaybackStopped -= OnOutputPlaybackStopped;
                    outputDevice.Stop();
                }
                catch { }
                outputDevice.Dispose();
                outputDevice = null;
            }

            if (readerStream != null)
            {
                try { readerStream.Dispose(); } catch { }
                readerStream = null;
            }

            volumeProvider = null;
            IsPlaying = false;
            IsPreviewing = false;
            ActiveFallbackLevel = AlarmFallbackLevel.None;
            ActiveSoundName = null;

            PlaybackStopped?.Invoke();
        }

        private bool TryPlayAudioFile(string filePath, bool isLooping)
        {
            try
            {
                var baseReader = new MediaFoundationReader(filePath);
                readerStream = isLooping ? new LoopStream(baseReader) : baseReader;

                var sampleProvider = readerStream.ToSampleProvider();
                volumeProvider = new VolumeSampleProvider(sampleProvider)
                {
                    Volume = currentVolume
                };

                outputDevice = new WaveOutEvent();
                outputDevice.PlaybackStopped += OnOutputPlaybackStopped;
                outputDevice.Init(volumeProvider);
                outputDevice.Play();

                IsPlaying = true;
                return true;
            }
            catch (Exception ex)
            {
                LogWarning($"TryPlayAudioFile exception for '{filePath}': {ex.Message}");
                if (outputDevice != null)
                {
                    try { outputDevice.Dispose(); } catch { }
                    outputDevice = null;
                }
                if (readerStream != null)
                {
                    try { readerStream.Dispose(); } catch { }
                    readerStream = null;
                }
                volumeProvider = null;
                IsPlaying = false;
                return false;
            }
        }

        private void OnOutputPlaybackStopped(object? sender, StoppedEventArgs e)
        {
            if (e.Exception != null)
            {
                LogWarning($"Output device playback stopped with error: {e.Exception.Message}");
            }
        }

        private string? ResolveSoundPath(string soundType, string? customSoundPath, string defaultAssetsDir)
        {
            if (string.Equals(soundType, "Default", StringComparison.OrdinalIgnoreCase))
            {
                return Path.Combine(defaultAssetsDir, "alarm.wav");
            }

            if (string.Equals(soundType, "Custom", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(customSoundPath)) return null;

                if (Path.IsPathRooted(customSoundPath) && File.Exists(customSoundPath))
                {
                    return customSoundPath;
                }
                return Path.Combine(AppPaths.AlarmSoundsDir, customSoundPath);
            }

            // Built-in procedural tones
            ToneGenerator.EnsureBuiltInTones(AppPaths.AlarmSoundsDir);
            return ToneGenerator.GetToneFilePath(AppPaths.AlarmSoundsDir, soundType);
        }

        private void ArmDurationTimer(int durationMinutes)
        {
            durationTimer?.Dispose();
            durationTimer = null;

            if (durationMinutes > 0)
            {
                durationTimer = new System.Threading.Timer(_ =>
                {
                    Stop();
                }, null, durationMinutes * 60 * 1000, Timeout.Infinite);
            }
        }

        private void StartVolumeRamp()
        {
            rampTimer?.Dispose();
            // Ramp up from current (15%) to targetVolume in 30 seconds (steps of 1 second = 30 steps)
            float step = (targetVolume - currentVolume) / 30f;
            int stepCount = 0;

            rampTimer = new System.Threading.Timer(_ =>
            {
                lock (syncLock)
                {
                    if (!IsPlaying || volumeProvider == null)
                    {
                        rampTimer?.Dispose();
                        rampTimer = null;
                        return;
                    }

                    stepCount++;
                    currentVolume = Math.Min(targetVolume, currentVolume + step);
                    volumeProvider.Volume = currentVolume;

                    if (stepCount >= 30 || currentVolume >= targetVolume)
                    {
                        currentVolume = targetVolume;
                        volumeProvider.Volume = targetVolume;
                        rampTimer?.Dispose();
                        rampTimer = null;
                    }
                }
            }, null, 1000, 1000);
        }

        private static void LogWarning(string message)
        {
            try
            {
                string logPath = AppPaths.LogPath;
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [AlarmAudio] {message}{Environment.NewLine}");
            }
            catch { }
        }

        public void Dispose()
        {
            if (!disposed)
            {
                disposed = true;
                Stop();
            }
        }

        /// <summary>
        /// A WaveStream that continuously loops a source stream from start to finish.
        /// </summary>
        private sealed class LoopStream : WaveStream
        {
            private readonly WaveStream sourceStream;

            public LoopStream(WaveStream sourceStream)
            {
                this.sourceStream = sourceStream;
            }

            public override WaveFormat WaveFormat => sourceStream.WaveFormat;
            public override long Length => sourceStream.Length;
            public override long Position
            {
                get => sourceStream.Position;
                set => sourceStream.Position = value;
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                int totalBytesRead = 0;
                while (totalBytesRead < count)
                {
                    int bytesRead = sourceStream.Read(buffer, offset + totalBytesRead, count - totalBytesRead);
                    if (bytesRead == 0)
                    {
                        if (sourceStream.Position == 0)
                        {
                            break;
                        }
                        sourceStream.Position = 0;
                    }
                    totalBytesRead += bytesRead;
                }
                return totalBytesRead;
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    sourceStream.Dispose();
                }
                base.Dispose(disposing);
            }
        }
    }
}
