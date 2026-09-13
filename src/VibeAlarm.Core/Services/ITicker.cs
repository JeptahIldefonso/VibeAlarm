using System;

namespace VibeAlarm.Services
{
    /// <summary>
    /// A UI-thread interval pulse, injected into <see cref="TimeService"/> so Core stays
    /// UI-framework-free: each app head supplies its own implementation (WinUI: a
    /// DispatcherQueueTimer; the WinForms head drives <see cref="TimeService.Pulse"/>
    /// from its own ticker and passes no ticker at all).
    /// </summary>
    public interface ITicker : IDisposable
    {
        /// <summary>Raised on the ticker's interval.</summary>
        event Action? Tick;

        /// <summary>Interval in milliseconds. Set before <see cref="Start"/>.</summary>
        int IntervalMs { set; }

        /// <summary>Starts the ticker. Idempotent.</summary>
        void Start();
    }
}
