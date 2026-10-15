using System;
using System.IO;

namespace VibeAlarm.Services
{
    /// <summary>
    /// Generates high-quality procedural alarm tones (PCM WAV) completely offline in code.
    /// Eliminates bundle size overhead while giving users distinct built-in alarm tone choices.
    /// </summary>
    public static class ToneGenerator
    {
        public const string ToneClassicBeep = "Classic Beep";
        public const string ToneSoftChime = "Soft Chime";
        public const string ToneDigitalPulse = "Digital Pulse";
        public const string ToneRisingTone = "Rising Tone";

        public static readonly string[] BuiltInTones =
        {
            ToneClassicBeep,
            ToneSoftChime,
            ToneDigitalPulse,
            ToneRisingTone
        };

        public static string GetToneFileName(string toneName) => toneName switch
        {
            ToneClassicBeep => "classic_beep.wav",
            ToneSoftChime => "soft_chime.wav",
            ToneDigitalPulse => "digital_pulse.wav",
            ToneRisingTone => "rising_tone.wav",
            _ => "classic_beep.wav"
        };

        public static string GetToneFilePath(string alarmSoundsDir, string toneName)
        {
            return Path.Combine(alarmSoundsDir, GetToneFileName(toneName));
        }

        public static void EnsureBuiltInTones(string alarmSoundsDir)
        {
            if (!Directory.Exists(alarmSoundsDir))
            {
                Directory.CreateDirectory(alarmSoundsDir);
            }

            foreach (var tone in BuiltInTones)
            {
                string path = GetToneFilePath(alarmSoundsDir, tone);
                if (!File.Exists(path) || new FileInfo(path).Length == 0)
                {
                    GenerateToneFile(path, tone);
                }
            }
        }

        public static void GenerateToneFile(string outputPath, string toneName)
        {
            const int sampleRate = 44100;
            short[] samples = toneName switch
            {
                ToneClassicBeep => GenerateClassicBeep(sampleRate),
                ToneSoftChime => GenerateSoftChime(sampleRate),
                ToneDigitalPulse => GenerateDigitalPulse(sampleRate),
                ToneRisingTone => GenerateRisingTone(sampleRate),
                _ => GenerateClassicBeep(sampleRate)
            };

            WriteWavFile(outputPath, samples, sampleRate);
        }

        private static short[] GenerateClassicBeep(int sampleRate)
        {
            // 2.0s loop: dual beep (880Hz + 1760Hz overtone)
            double duration = 2.0;
            int totalSamples = (int)(duration * sampleRate);
            short[] buffer = new short[totalSamples];

            void RenderBeep(double startSec, double lengthSec, double freq)
            {
                int start = (int)(startSec * sampleRate);
                int count = (int)(lengthSec * sampleRate);
                int fade = (int)(0.005 * sampleRate); // 5ms fade

                for (int i = 0; i < count && (start + i) < totalSamples; i++)
                {
                    double t = (double)i / sampleRate;
                    double env = 1.0;
                    if (i < fade) env = (double)i / fade;
                    else if (i > count - fade) env = (double)(count - i) / fade;

                    double wave = 0.7 * Math.Sin(2.0 * Math.PI * freq * t) +
                                  0.3 * Math.Sin(2.0 * Math.PI * (freq * 2.0) * t);

                    buffer[start + i] = (short)(wave * env * 24000.0);
                }
            }

            RenderBeep(0.00, 0.12, 880.0);
            RenderBeep(0.20, 0.12, 880.0);

            return buffer;
        }

        private static short[] GenerateSoftChime(int sampleRate)
        {
            // 3.0s loop: pleasant pentatonic chime (E5, G#5, B5, E6)
            double duration = 3.0;
            int totalSamples = (int)(duration * sampleRate);
            short[] buffer = new short[totalSamples];

            double[] freqs = { 659.25, 830.61, 987.77, 1318.51 };
            double[] weights = { 0.35, 0.25, 0.25, 0.15 };

            for (int i = 0; i < totalSamples; i++)
            {
                double t = (double)i / sampleRate;
                double decay = Math.Exp(-1.35 * t);
                double sample = 0;

                for (int n = 0; n < freqs.Length; n++)
                {
                    sample += weights[n] * Math.Sin(2.0 * Math.PI * freqs[n] * t);
                }

                // Soft bell strike envelope
                double strike = t < 0.004 ? t / 0.004 : 1.0;
                double val = sample * strike * decay;
                buffer[i] = (short)(Math.Clamp(val, -1.0, 1.0) * 26000.0);
            }

            return buffer;
        }

        private static short[] GenerateDigitalPulse(int sampleRate)
        {
            // 2.0s loop: 4 modern digital pulses
            double duration = 2.0;
            int totalSamples = (int)(duration * sampleRate);
            short[] buffer = new short[totalSamples];

            void RenderPulse(double startSec, double lengthSec, double freq)
            {
                int start = (int)(startSec * sampleRate);
                int count = (int)(lengthSec * sampleRate);
                int fade = (int)(0.004 * sampleRate);

                for (int i = 0; i < count && (start + i) < totalSamples; i++)
                {
                    double t = (double)i / sampleRate;
                    double env = 1.0;
                    if (i < fade) env = (double)i / fade;
                    else if (i > count - fade) env = (double)(count - i) / fade;

                    double wave = 0.75 * Math.Sin(2.0 * Math.PI * freq * t) +
                                  0.25 * Math.Sin(2.0 * Math.PI * (freq * 1.5) * t);

                    buffer[start + i] = (short)(wave * env * 25000.0);
                }
            }

            RenderPulse(0.00, 0.07, 1046.50);
            RenderPulse(0.14, 0.07, 1174.66);
            RenderPulse(0.28, 0.07, 1318.51);
            RenderPulse(0.42, 0.10, 1567.98);

            return buffer;
        }

        private static short[] GenerateRisingTone(int sampleRate)
        {
            // 2.5s loop: rising arpeggio C5 -> E5 -> G5 -> C6
            double duration = 2.5;
            int totalSamples = (int)(duration * sampleRate);
            short[] buffer = new short[totalSamples];

            double[] notes = { 523.25, 659.25, 783.99, 1046.50 };
            double noteDuration = 0.22;

            for (int n = 0; n < notes.Length; n++)
            {
                double noteStart = n * noteDuration;
                int start = (int)(noteStart * sampleRate);
                int count = (int)((duration - noteStart) * sampleRate);
                double freq = notes[n];

                for (int i = 0; i < count && (start + i) < totalSamples; i++)
                {
                    double t = (double)i / sampleRate;
                    double decay = Math.Exp(-2.2 * t);
                    double attack = t < 0.005 ? t / 0.005 : 1.0;
                    double wave = 0.7 * Math.Sin(2.0 * Math.PI * freq * t) +
                                  0.3 * Math.Sin(2.0 * Math.PI * (freq * 2.0) * t);

                    double sample = wave * attack * decay * 0.45;
                    int idx = start + i;
                    int blended = buffer[idx] + (int)(sample * 32767.0);
                    buffer[idx] = (short)Math.Clamp(blended, short.MinValue, short.MaxValue);
                }
            }

            return buffer;
        }

        private static void WriteWavFile(string filePath, short[] samples, int sampleRate)
        {
            int numSamples = samples.Length;
            int dataSize = numSamples * 2;
            int fileSize = 36 + dataSize;

            using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read);
            using var writer = new BinaryWriter(fs);

            writer.Write("RIFF".ToCharArray());
            writer.Write(fileSize);
            writer.Write("WAVE".ToCharArray());

            writer.Write("fmt ".ToCharArray());
            writer.Write(16);
            writer.Write((short)1);                // AudioFormat (PCM = 1)
            writer.Write((short)1);                // Channels (Mono = 1)
            writer.Write(sampleRate);              // SampleRate
            writer.Write(sampleRate * 2);          // ByteRate
            writer.Write((short)2);                // BlockAlign
            writer.Write((short)16);               // BitsPerSample

            writer.Write("data".ToCharArray());
            writer.Write(dataSize);

            for (int i = 0; i < samples.Length; i++)
            {
                writer.Write(samples[i]);
            }
        }
    }
}
