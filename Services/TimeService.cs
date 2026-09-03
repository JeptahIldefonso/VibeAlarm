using System;
using System.Windows.Forms;

namespace VibeAlarm.Services
{
    /// <summary>
    /// The single authoritative source of "what time is it" for the application.
    /// A UI-facing 1-second timer periodically calls <see cref="Pulse"/>, which reads
    /// the injected <see cref="IClock"/> and raises fine-grained events:
    ///
    ///   • <see cref="SecondChanged"/>  – every second (drive only the live clock + countdown, cheap)
    ///   • <see cref="MinuteChanged"/>  – when the wall-clock minute changes (recompute transitions + Next Up)
    ///   • <see cref="DateChanged"/>    – when the calendar day rolls over (today/month changes, midnight)
    ///   • <see cref="Resumed"/>        – when the app regains focus (catch up on skipped time)
    ///
    /// This avoids updating the whole UI every second: only the clock/countdown depend on the
    /// per-second tick, while scheduling decisions happen on the far rarer minute/date edges.
    /// </summary>
    public sealed class TimeService : IDisposable
    {
        private readonly IClock clock;
        private readonly System.Windows.Forms.Timer uiTimer;
        private DateTime lastPulse;
        private bool hasPulsed;

        /// <summary>Fired once every second while the app is running.</summary>
        public event Action<DateTime>? SecondChanged;

        /// <summary>Fired when the wall-clock minute changes.</summary>
        public event Action<DateTime>? MinuteChanged;

        /// <summary>Fired when the calendar date rolls over (e.g. midnight / new month).</summary>
        public event Action<DateTime>? DateChanged;

        /// <summary>Fired when the application regains focus after the timer may have been suspended.</summary>
        public event Action<DateTime>? Resumed;

        public TimeService(IClock clock, int intervalMs = 1000)
        {
            this.clock = clock;
            uiTimer = new System.Windows.Forms.Timer { Interval = intervalMs };
            uiTimer.Tick += (s, e) => Pulse();
        }

        /// <summary>The current wall-clock time from the injected clock.</summary>
        public DateTime Now => clock.Now;

        /// <summary>The current date.</summary>
        public DateTime Today => clock.Now.Date;

        /// <summary>Starts the live polling timer. The first pulse happens immediately.</summary>
        public void Start()
        {
            if (!uiTimer.Enabled)
            {
                uiTimer.Start();
            }
            Pulse();
        }

        /// <summary>
        /// Samples the clock once. Marks the last sample, and raises DateChanged /
        /// MinuteChanged when the previous sample crossed those boundaries.
        /// </summary>
        public void Pulse()
        {
            DateTime now = clock.Now;

            if (hasPulsed)
            {
                if (lastPulse.Date != now.Date)
                {
                    DateChanged?.Invoke(now);
                }
                if (lastPulse.Minute != now.Minute || lastPulse.Hour != now.Hour)
                {
                    MinuteChanged?.Invoke(now);
                }
            }
            else
            {
                hasPulsed = true;
            }

            lastPulse = now;
            SecondChanged?.Invoke(now);
        }

        /// <summary>
        /// Re-synchronizes after the app regains focus. Detects any minute/day that elapsed
        /// while the timer was suspended and reports it so the scheduler can catch up.
        /// </summary>
        public void OnResumed()
        {
            if (!hasPulsed)
            {
                return;
            }

            DateTime now = clock.Now;
            if (now.Date != lastPulse.Date || now.Minute != lastPulse.Minute || now.Hour != lastPulse.Hour)
            {
                Resumed?.Invoke(now);
                Pulse();
            }
        }

        public void Dispose() => uiTimer.Dispose();
    }
}