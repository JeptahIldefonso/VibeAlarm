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
    }
}