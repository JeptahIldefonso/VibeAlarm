using System;
using System.IO;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace VibeAlarm.Services
{
    /// <summary>
    /// NAudio-based music playback engine implementing <see cref="IMusicPlayer"/>.
    /// Uses Windows Media Foundation (<see cref="MediaFoundationReader"/>) to natively decode
    /// MP3, M4A, FLAC, and WAV files with sample-accurate seeking and software volume.
    /// </summary>
    public sealed class NAudioMusicPlayer : IMusicPlayer
    {
        private readonly object syncLock = new();

        private WaveOutEvent? outputDevice;
        private MediaFoundationReader? reader;
        private VolumeSampleProvider? volumeProvider;

        private int volume = 70;
        private bool isExplicitlyStopping;
        private bool disposed;

        public event Action? PlaybackEnded;
        public event Action<string>? PlaybackError;

        public string? CurrentFilePath { get; private set; }
        public string? CurrentDisplayName { get; private set; }

        public bool IsPlaying { get; private set; }
        public bool IsPaused { get; private set; }

        public int Volume
        {
            get => volume;
            set
            {
                lock (syncLock)
                {
                    volume = Math.Clamp(value, 0, 100);
                    if (volumeProvider != null)
                    {
                        volumeProvider.Volume = volume / 100f;
                    }
                }
            }
        }

        public bool Play(string filePath, string displayName)
        {
            lock (syncLock)
            {
                if (disposed) return false;

                StopInternal();
                isExplicitlyStopping = false;

                string extension = Path.GetExtension(filePath);

                if (!File.Exists(filePath))
                {
                    PlaybackError?.Invoke($"File not found ({extension}): {Path.GetFileName(filePath)}");
                    return false;
                }

                try
                {
                    reader = new MediaFoundationReader(filePath);
                    var sampleProvider = reader.ToSampleProvider();
                    volumeProvider = new VolumeSampleProvider(sampleProvider)
                    {
                        Volume = volume / 100f
                    };

                    outputDevice = new WaveOutEvent();
                    outputDevice.PlaybackStopped += OnPlaybackStopped;
                    outputDevice.Init(volumeProvider);
                    outputDevice.Play();

                    CurrentFilePath = filePath;
                    CurrentDisplayName = displayName;
                    IsPlaying = true;
                    IsPaused = false;
                    return true;
                }
                catch (Exception ex)
                {
                    Cleanup();
                    CurrentFilePath = null;
                    CurrentDisplayName = null;
                    IsPlaying = false;
                    IsPaused = false;
                    PlaybackError?.Invoke($"Cannot open audio file ({extension}): {ex.Message}");
                    return false;
                }
            }
        }

        public void Pause()
        {
            lock (syncLock)
            {
                if (disposed || !IsPlaying || outputDevice == null) return;

                try
                {
                    outputDevice.Pause();
                    IsPlaying = false;
                    IsPaused = true;
                }
                catch (Exception ex)
                {
                    PlaybackError?.Invoke($"Pause failed: {ex.Message}");
                }
            }
        }

        public void Resume()
        {
            lock (syncLock)
            {
                if (disposed || !IsPaused || outputDevice == null) return;

                try
                {
                    outputDevice.Play();
                    IsPlaying = true;
                    IsPaused = false;
                }
                catch (Exception ex)
                {
                    PlaybackError?.Invoke($"Resume failed: {ex.Message}");
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
            isExplicitlyStopping = true;
            if (outputDevice != null)
            {
                try
                {
                    outputDevice.Stop();
                }
                catch
                {
                    // Ignore device errors on stop
                }
            }
            Cleanup();
            IsPlaying = false;
            IsPaused = false;
            CurrentFilePath = null;
            CurrentDisplayName = null;
        }

        public void Seek(int positionMs)
        {
            lock (syncLock)
            {
                if (disposed || reader == null) return;

                try
                {
                    int totalMs = GetDurationMs();
                    int targetMs = Math.Clamp(positionMs, 0, Math.Max(0, totalMs));
                    reader.CurrentTime = TimeSpan.FromMilliseconds(targetMs);
                }
                catch (Exception ex)
                {
                    PlaybackError?.Invoke($"Seek failed: {ex.Message}");
                }
            }
        }

        public int GetPositionMs()
        {
            lock (syncLock)
            {
                if (disposed || reader == null) return 0;
                try
                {
                    return (int)reader.CurrentTime.TotalMilliseconds;
                }
                catch
                {
                    return 0;
                }
            }
        }

        public int GetDurationMs()
        {
            lock (syncLock)
            {
                if (disposed || reader == null) return 0;
                try
                {
                    return (int)reader.TotalTime.TotalMilliseconds;
                }
                catch
                {
                    return 0;
                }
            }
        }

        private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
        {
            lock (syncLock)
            {
                if (disposed) return;

                if (e.Exception != null)
                {
                    IsPlaying = false;
                    IsPaused = false;
                    PlaybackError?.Invoke($"Playback error: {e.Exception.Message}");
                    return;
                }

                if (!isExplicitlyStopping && reader != null)
                {
                    TimeSpan remaining = reader.TotalTime - reader.CurrentTime;
                    if (remaining.TotalMilliseconds <= 500 || reader.Position >= reader.Length)
                    {
                        IsPlaying = false;
                        IsPaused = false;
                        PlaybackEnded?.Invoke();
                    }
                }
            }
        }

        private void Cleanup()
        {
            if (outputDevice != null)
            {
                outputDevice.PlaybackStopped -= OnPlaybackStopped;
                try
                {
                    outputDevice.Dispose();
                }
                catch
                {
                    // Ignore
                }
                outputDevice = null;
            }

            if (reader != null)
            {
                try
                {
                    reader.Dispose();
                }
                catch
                {
                    // Ignore
                }
                reader = null;
            }

            volumeProvider = null;
        }

        public void Dispose()
        {
            lock (syncLock)
            {
                if (disposed) return;
                disposed = true;
                StopInternal();
            }
        }
    }
}
