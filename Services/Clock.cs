using System;

namespace VibeAlarm.Services
{
    /// <summary>
    /// Production clock backed by the operating system's local time. Scheduling must
    /// use the OS local time unless the application explicitly supports another zone;
    /// no fixed offset or timezone is hard-coded.
    /// </summary>
    public sealed class Clock : IClock
    {
        public DateTime Now => DateTime.Now;
    }
}