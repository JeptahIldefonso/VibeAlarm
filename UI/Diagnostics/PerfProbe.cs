using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;

namespace VibeAlarm.UI.Diagnostics
{
    /// <summary>
    /// Opt-in performance instrumentation for the two paths the user reported as laggy:
    /// the sidebar tab switch and the scroll repaint. Entirely inert unless the
    /// VIBEALARM_PERF environment variable is set to 1, so the shipping app pays nothing
    /// (one env read at type-init, then a bool test at each probe site).
    ///
    /// This exists to answer "which fix actually mattered" with numbers instead of
    /// impressions: each run is tagged with a label (VIBEALARM_PERF_LABEL) and appended to
    /// a log, so BEFORE/AFTER rows for an individual fix sit next to each other.
    /// </summary>
    public static class PerfProbe
    {
        public static readonly bool Enabled =
            Environment.GetEnvironmentVariable("VIBEALARM_PERF") == "1";

        /// <summary>Identifies which build/fix combination produced a run's numbers.</summary>
        public static readonly string RunLabel =
            Environment.GetEnvironmentVariable("VIBEALARM_PERF_LABEL") ?? "unlabelled";

        private static readonly List<string> Lines = new();

        public static void Line(string text)
        {
            Lines.Add(text);
            Debug.WriteLine("[perf] " + text);
        }

        /// <summary>Records one sample set as min / median / mean / max, which is what makes a
        /// regression legible — a mean alone hides a single catastrophic first paint.</summary>
        public static void Report(string metric, IReadOnlyList<double> samplesMs)
        {
            if (samplesMs.Count == 0)
            {
                Line($"{RunLabel,-24} {metric,-34} (no samples)");
                return;
            }
            double[] sorted = samplesMs.OrderBy(v => v).ToArray();
            double median = sorted.Length % 2 == 1
                ? sorted[sorted.Length / 2]
                : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2d;
            Line(string.Format(CultureInfo.InvariantCulture,
                "{0,-24} {1,-34} n={2,-4} min={3,8:F2} med={4,8:F2} mean={5,8:F2} max={6,8:F2}",
                RunLabel, metric, samplesMs.Count, sorted[0], median, samplesMs.Average(), sorted[^1]));
        }

        // ---- Phase timing: which PART of a tab switch spends the time ----

        private static readonly Dictionary<string, List<double>> Phases = new();

        /// <summary>Accumulates elapsed time under <paramref name="name"/>. A readonly struct
        /// scope, so `using (PerfProbe.Phase("x"))` allocates nothing and costs one bool test
        /// when the harness is off.</summary>
        public static PhaseScope Phase(string name) => new(name);

        public readonly struct PhaseScope : IDisposable
        {
            private readonly string name;
            private readonly long start;

            internal PhaseScope(string name)
            {
                this.name = name;
                start = Enabled ? Stopwatch.GetTimestamp() : 0L;
            }

            public void Dispose()
            {
                if (!Enabled)
                {
                    return;
                }
                double ms = (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;
                if (!Phases.TryGetValue(name, out List<double>? bucket))
                {
                    Phases[name] = bucket = new List<double>();
                }
                bucket.Add(ms);
            }
        }

        /// <summary>Reports phase buckets worst-TOTAL first. The ranking is the deliverable:
        /// it names the one phase worth optimizing instead of spreading effort evenly across
        /// five plausible-looking suspects.</summary>
        public static void ReportPhases()
        {
            foreach (KeyValuePair<string, List<double>> kv in Phases.OrderByDescending(k => k.Value.Sum()))
            {
                double[] sorted = kv.Value.OrderBy(v => v).ToArray();
                Line(string.Format(CultureInfo.InvariantCulture,
                    "{0,-24} {1,-34} n={2,-4} total={3,9:F1} med={4,8:F2} mean={5,8:F2} max={6,8:F2}",
                    RunLabel, "phase " + kv.Key, sorted.Length, kv.Value.Sum(),
                    sorted[sorted.Length / 2], kv.Value.Average(), sorted[^1]));
            }
        }

        /// <summary>Drops warm-up samples so only measured rounds appear in the ranking.</summary>
        public static void ResetPhases() => Phases.Clear();

        /// <summary>Appends this run to the shared log so successive labelled runs accumulate
        /// into a single comparable table.</summary>
        public static void Flush()
        {
            if (!Enabled)
            {
                return;
            }
            try
            {
                string path = Environment.GetEnvironmentVariable("VIBEALARM_PERF_LOG")
                    ?? Path.Combine(AppContext.BaseDirectory, "perf.log");
                File.AppendAllLines(path, Lines);
                Lines.Clear();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Perf log write failed: " + ex.Message);
            }
        }
    }
}
