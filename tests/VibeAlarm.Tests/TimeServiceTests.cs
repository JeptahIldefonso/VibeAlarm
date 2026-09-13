using System;
using VibeAlarm.Services;
using Xunit;

namespace VibeAlarm.Tests
{
    public class TimeServiceTests
    {
        [Fact]
        public void First_pulse_raises_second_but_not_minute_or_date()
        {
            var clock = new FakeClock(new DateTime(2026, 9, 2, 10, 0, 0));
            using var service = new TimeService(clock, 1000);

            int seconds = 0, minutes = 0, dates = 0;
            service.SecondChanged += _ => seconds++;
            service.MinuteChanged += _ => minutes++;
            service.DateChanged += _ => dates++;

            service.Pulse();

            Assert.Equal(1, seconds);
            Assert.Equal(0, minutes);
            Assert.Equal(0, dates);
        }

        [Fact]
        public void Minute_boundary_raises_minute_changed()
        {
            var clock = new FakeClock(new DateTime(2026, 9, 2, 10, 0, 0));
            using var service = new TimeService(clock, 1000);
            service.Pulse();

            int minutes = 0;
            service.MinuteChanged += _ => minutes++;

            clock.Advance(TimeSpan.FromMinutes(1));
            service.Pulse();

            Assert.Equal(1, minutes);
        }

        [Fact]
        public void Midnight_rollover_raises_date_changed()
        {
            var clock = new FakeClock(new DateTime(2026, 9, 2, 23, 59, 59));
            using var service = new TimeService(clock, 1000);
            service.Pulse();

            int dates = 0;
            service.DateChanged += _ => dates++;

            clock.AdvanceSeconds(2); // -> 2026-09-03 00:00:01
            service.Pulse();

            Assert.Equal(1, dates);
            Assert.Equal(3, clock.Now.Day);
        }

        [Fact]
        public void Resume_after_suspension_detects_elapsed_minutes()
        {
            var clock = new FakeClock(new DateTime(2026, 9, 2, 10, 0, 0));
            using var service = new TimeService(clock, 1000);
            service.Pulse();

            int resumed = 0;
            service.Resumed += _ => resumed++;

            clock.Advance(TimeSpan.FromMinutes(5));
            service.OnResumed();

            Assert.Equal(1, resumed);
        }

        [Fact]
        public void Resume_with_no_elapsed_time_does_not_raise()
        {
            var clock = new FakeClock(new DateTime(2026, 9, 2, 10, 0, 0));
            using var service = new TimeService(clock, 1000);
            service.Pulse();

            int resumed = 0;
            service.Resumed += _ => resumed++;

            service.OnResumed();

            Assert.Equal(0, resumed);
        }

        [Fact]
        public void Pulse_after_a_multi_day_gap_raises_minute_and_date_once_each()
        {
            // Simulates the machine sleeping (or the app being suspended) for 3 days:
            // the ticker freezes, then the next Pulse sees a wall-clock that jumped.
            // The stateless before/after diff must report the new time exactly once —
            // no replay of every skipped minute, no missed date change.
            var clock = new FakeClock(new DateTime(2026, 9, 13, 10, 0, 0));
            using var service = new TimeService(clock, 1000);
            service.Pulse();

            int minutes = 0, dates = 0;
            DateTime? minuteAt = null, dateAt = null;
            service.MinuteChanged += now => { minutes++; minuteAt = now; };
            service.DateChanged += now => { dates++; dateAt = now; };

            clock.Set(new DateTime(2026, 9, 16, 10, 7, 23));
            service.Pulse();

            Assert.Equal(1, minutes);
            Assert.Equal(1, dates);
            Assert.Equal(new DateTime(2026, 9, 16, 10, 7, 23), minuteAt);
            Assert.Equal(new DateTime(2026, 9, 16, 10, 7, 23), dateAt);
        }

        [Fact]
        public void Pulse_after_a_gap_landing_on_the_same_minute_still_raises_date_changed()
        {
            // A sleep that ends at the same wall-clock minute+hour as it began changes
            // only the date — DateChanged alone must carry the catch-up (the host wires
            // it to CheckTransitions just like MinuteChanged).
            var clock = new FakeClock(new DateTime(2026, 9, 13, 10, 0, 0));
            using var service = new TimeService(clock, 1000);
            service.Pulse();

            int minutes = 0, dates = 0;
            service.MinuteChanged += _ => minutes++;
            service.DateChanged += _ => dates++;

            clock.Set(new DateTime(2026, 9, 16, 10, 0, 5));
            service.Pulse();

            Assert.Equal(0, minutes);
            Assert.Equal(1, dates);
        }

        [Fact]
        public void Pulse_across_new_year_raises_minute_and_date_into_january()
        {
            var clock = new FakeClock(new DateTime(2026, 12, 31, 23, 59, 59));
            using var service = new TimeService(clock, 1000);
            service.Pulse();

            int dates = 0;
            DateTime? dateAt = null;
            service.DateChanged += now => { dates++; dateAt = now; };

            clock.AdvanceSeconds(2); // -> 2027-01-01 00:00:01
            service.Pulse();

            Assert.Equal(1, dates);
            Assert.Equal(new DateTime(2027, 1, 1, 0, 0, 1), dateAt);
            Assert.Equal(2027, dateAt!.Value.Year);
        }
    }
}