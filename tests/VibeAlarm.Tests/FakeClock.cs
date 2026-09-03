using VibeAlarm.Services;

namespace VibeAlarm.Tests
{
    /// <summary>
    /// Deterministic, manually-advanceable clock so scheduling behavior can be tested
    /// without real waiting. Mirrors <see cref="Clock"/> semantics.
    /// </summary>
    public sealed class FakeClock : IClock
    {
        public DateTime Now { get; private set; }

        public FakeClock(DateTime now) => Now = now;

        /// <summary>Sets the clock to a specific instant (used for rollover/restart tests).</summary>
        public void Set(DateTime now) => Now = now;

        /// <summary>Advances the clock by a duration (used for transition/countdown tests).</summary>
        public void Advance(TimeSpan delta) => Now = Now.Add(delta);

        /// <summary>Advances by a whole number of seconds.</summary>
        public void AdvanceSeconds(int seconds) => Advance(TimeSpan.FromSeconds(seconds));
    }
}