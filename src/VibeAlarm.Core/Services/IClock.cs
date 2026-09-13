using System;

namespace VibeAlarm.Services
{
    /// <summary>
    /// Abstraction over the system clock so that scheduling logic can be tested
    /// deterministically with a fake time source. The production implementation is
    /// <see cref="Clock"/> (returns DateTime.Now). All business logic should read time
    /// through this interface rather than calling DateTime.Now directly.
    /// </summary>
    public interface IClock
    {
        DateTime Now { get; }

        DateTime Today => Now.Date;
    }
}