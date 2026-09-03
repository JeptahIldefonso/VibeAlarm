using System;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace VibeAlarm.Services
{
    /// <summary>
    /// Centralises all audio playback: MCI-based ambient soundscapes (with volume control)
    /// and the looping alarm.wav used when an alarm fires.
    /// </summary>
    public sealed class AudioService : IDisposable
    {
        [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
        private static extern int mciSendString(string command, string? returnValue, int returnLength, IntPtr winHandle);

        private string? activeAmbientAlias;
        private SoundPlayer? activeAlarmPlayer;

        /// <summary>Fired whenever the currently-playing ambient track changes (null when stopped).</summary>
        public event Action<string?>? AmbientPlayingChanged;

        /// <summary>The ambient sound volume (0-100).</summary>
        public int AmbientVolume { get; set; } = 70;

        /// <summary>Whether an ambient track is currently playing.</summary>
        public bool IsAmbientPlaying => activeAmbientAlias != null;

        /// <summary>
        /// Opens and plays an ambient sound file in a loop. Returns false when the file cannot be opened.
        /// </summary>
        public bool PlayAmbientFile(string filePath, string displayName)
        {
            StopAmbientAudio();

            string alias = "ambient" + Guid.NewGuid().ToString("N");
            string escapedPath = filePath.Replace("\"", string.Empty);
            int openResult = mciSendString($"open \"{escapedPath}\" alias {alias}", null, 0, IntPtr.Zero);
            if (openResult != 0)
            {
                activeAmbientAlias = null;
                AmbientPlayingChanged?.Invoke(null);
                return false;
            }

            ApplyAmbientVolume();
            mciSendString($"play {alias} repeat", null, 0, IntPtr.Zero);
            activeAmbientAlias = alias;
            AmbientPlayingChanged?.Invoke(displayName);
            return true;
        }

        /// <summary>Applies the current volume to the active ambient track.</summary>
        public void ApplyAmbientVolume()
        {
            if (activeAmbientAlias == null)
            {
                return;
            }

            int mciVolume = Math.Max(0, Math.Min(1000, AmbientVolume * 10));
            mciSendString($"setaudio {activeAmbientAlias} volume to {mciVolume}", null, 0, IntPtr.Zero);
        }

        /// <summary>Stops any playing ambient track.</summary>
        public void StopAmbientAudio()
        {
            if (activeAmbientAlias != null)
            {
                mciSendString($"stop {activeAmbientAlias}", null, 0, IntPtr.Zero);
                mciSendString($"close {activeAmbientAlias}", null, 0, IntPtr.Zero);
                activeAmbientAlias = null;
            }

            AmbientPlayingChanged?.Invoke(null);
        }

        /// <summary>
        /// Plays the built-in alarm.wav in a loop. Falls back to the system beep when the file is missing.
        /// </summary>
        public void PlayAlarmLoop(string assetsDirectory)
        {
            string audioPath = Path.Combine(assetsDirectory, "alarm.wav");
            if (!File.Exists(audioPath))
            {
                SystemSounds.Beep.Play();
                return;
            }

            StopAlarmLoop();
            activeAlarmPlayer = new SoundPlayer(audioPath);
            activeAlarmPlayer.Load();
            activeAlarmPlayer.PlayLooping();
        }

        /// <summary>Stops the looping alarm audio.</summary>
        public void StopAlarmLoop()
        {
            activeAlarmPlayer?.Stop();
            activeAlarmPlayer?.Dispose();
            activeAlarmPlayer = null;
        }

        /// <summary>
        /// Ensures the offline brown-noise and rain ambient loops exist inside the assets directory,
        /// generating them on the fly when missing.
        /// </summary>
        public static void EnsureDefaultAmbientSounds(string assetsDirectory)
        {
            if (!Directory.Exists(assetsDirectory))
            {
                Directory.CreateDirectory(assetsDirectory);
            }

            string brownPath = Path.Combine(assetsDirectory, "brown_noise.wav");
            string rainPath = Path.Combine(assetsDirectory, "rain.wav");

            if (!File.Exists(brownPath))
            {
                GenerateWavFile(brownPath, "brown", 5, 22050);
            }
            if (!File.Exists(rainPath))
            {
                GenerateWavFile(rainPath, "rain", 5, 22050);
            }
        }

        private static void GenerateWavFile(string filePath, string type, int duration, int sampleRate)
        {
            int numSamples = duration * sampleRate;
            int dataSize = numSamples * 2;
            int fileSize = 36 + dataSize;

            using FileStream fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
            using BinaryWriter writer = new BinaryWriter(fs);

            writer.Write("RIFF".ToCharArray());
            writer.Write(fileSize);
            writer.Write("WAVE".ToCharArray());

            writer.Write("fmt ".ToCharArray());
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(sampleRate);
            writer.Write(sampleRate * 2);
            writer.Write((short)2);
            writer.Write((short)16);

            writer.Write("data".ToCharArray());
            writer.Write(dataSize);

            Random random = new Random();
            double lastOut = 0.0;
            double rainDropFilter = 0.0;

            for (int i = 0; i < numSamples; i++)
            {
                short sampleValue = 0;
                if (type == "brown")
                {
                    double white = random.NextDouble() * 2.0 - 1.0;
                    lastOut = (lastOut + (0.02 * white)) / 1.02;
                    double val = lastOut * 3.5;
                    if (val > 1.0) val = 1.0;
                    if (val < -1.0) val = -1.0;
                    sampleValue = (short)(val * 32767);
                }
                else if (type == "rain")
                {
                    double white = random.NextDouble() * 2.0 - 1.0;
                    lastOut = (lastOut + (0.05 * white)) / 1.05;

                    if (random.NextDouble() < 0.0006)
                    {
                        rainDropFilter = 0.8;
                    }
                    rainDropFilter *= 0.99;
                    double patter = rainDropFilter * (random.NextDouble() - 0.5) * 1.5;

                    double val = lastOut * 0.6 + patter * 0.4;
                    if (val > 1.0) val = 1.0;
                    if (val < -1.0) val = -1.0;
                    sampleValue = (short)(val * 32767);
                }

                writer.Write(sampleValue);
            }
        }

        public void Dispose() => StopAmbientAudio();
    }
}
