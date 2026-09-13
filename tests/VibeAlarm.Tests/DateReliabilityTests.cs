using System;
using System.Collections.Generic;
using System.Linq;
using VibeAlarm.Models;
using VibeAlarm.Services;
using Xunit;

namespace VibeAlarm.Tests
{
    /// <summary>
    /// Date/time reliability: the single exact-format parse (AlarmEngine), year/leap
    /// rollovers, DST gap + ambiguous-hour behavior (with an injectable DST zone), and
    /// multi-day gaps (sleep/closed-app catch-up). Complements SchedulerServiceTests,
    /// which covers the core transition matrix.
    /// </summary>
    public class DateReliabilityTests
    {
        private const string Reminder = SchedulerService.TaskTypeNotification;

        /// <summary>US Eastern — a real DST zone, injected so these tests behave the same
        /// on any machine (the dev machine's locale has no DST).</summary>
        private static readonly TimeZoneInfo Eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

        private static TaskItem Task(string title, DateTime scheduled, string type = Reminder)
        {
            return new TaskItem
            {
                Id = Guid.NewGuid().ToString(),
                Title = title,
                ScheduledDate = scheduled.ToString("yyyy-MM-dd"),
                RemindTime = scheduled.ToString("HH:mm"), // post-snooze storage format
                Day = scheduled.DayOfWeek.ToString(),
                Type = type,
            };
        }

        private static (FakeClock Clock, SchedulerService Scheduler, List<TaskItem> List) Make(
            DateTime now, TimeZoneInfo? timeZone = null, params TaskItem[] tasks)
        {
            var clock = new FakeClock(now);
            var list = new List<TaskItem>(tasks);
            var scheduler = new SchedulerService(clock, list, timeZone);
            scheduler.ReplaceTasks();
            return (clock, scheduler, list);
        }

        // ---- Single parse authority: exact formats, invariant culture ----

        [Theory]
        [InlineData("14:30", 14, 30)]        // "HH:mm" — snooze re-writes
        [InlineData("02:30", 2, 30)]
        [InlineData("2:30 PM", 14, 30)]      // "h:mm tt" — React modal writer
        [InlineData("02:30 PM", 14, 30)]     // "hh:mm tt" — legacy dialog / IpcBridge default
        [InlineData("2:30 AM", 2, 30)]
        [InlineData("12:15 AM", 0, 15)]      // midnight edge
        [InlineData("12:15 PM", 12, 15)]     // noon edge
        public void Every_stored_time_format_parses_exactly(string stored, int hour, int minute)
        {
            TaskItem task = new() { Id = "t", ScheduledDate = "2026-09-13", RemindTime = stored };

            DateTime? scheduled = AlarmEngine.GetScheduledDateTime(task);

            Assert.NotNull(scheduled);
            Assert.Equal(new DateTime(2026, 9, 13, hour, minute, 0), scheduled.Value);
        }

        [Theory]
        [InlineData("o'clock")]     // garbage
        [InlineData("25:99")]       // out-of-range
        [InlineData("14:30 PM")]    // 24h hour WITH meridiem — ambiguous, never written
        [InlineData("")]
        public void Unparseable_time_yields_null_not_a_guessed_datetime(string stored)
        {
            TaskItem task = new() { Id = "t", ScheduledDate = "2026-09-13", RemindTime = stored };

            Assert.Null(AlarmEngine.GetScheduledDateTime(task));
        }

        [Fact]
        public void Date_is_parsed_with_the_exact_stored_format()
        {
            // "yyyy-MM-dd" is the only format any writer persists.
            TaskItem leap = new() { Id = "t", ScheduledDate = "2028-02-29", RemindTime = "08:00" };
            Assert.Equal(new DateTime(2028, 2, 29, 8, 0, 0), AlarmEngine.GetScheduledDateTime(leap));

            // A locale-shaped date ("13/09/2026") is NOT accepted — exact format only, so
            // the parse can never succeed on one machine and fail on another.
            Assert.False(DateTime.TryParseExact(
                "13/09/2026", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out _));
        }

        // ---- Year rollover ----

        [Fact]
        public void Task_fires_across_december_31_into_january_of_the_next_year()
        {
            var now = new DateTime(2026, 12, 31, 23, 58, 0);
            var newYear = Task("New year", new DateTime(2027, 1, 1, 0, 1, 0));
            var (clock, scheduler, _) = Make(now, null, newYear);

            TaskItem? fired = null;
            scheduler.ReminderFired += t => fired = t;

            // Next Up correctly crosses the year boundary before firing.
            Assert.Equal("New year", scheduler.NextUp!.Title);
            Assert.Equal(TimeSpan.FromMinutes(3), scheduler.Countdown);

            clock.Set(new DateTime(2027, 1, 1, 0, 1, 30));
            scheduler.CheckTransitions(clock.Now);

            Assert.NotNull(fired);
            Assert.Equal(2027, clock.Now.Year);
            Assert.Equal(TaskState.Completed, newYear.GetState());
        }

        [Fact]
        public void Snooze_across_midnight_of_december_31_lands_in_january()
        {
            var now = new DateTime(2026, 12, 31, 23, 30, 0);
            var (clock, scheduler, _) = Make(now, null, Task("NYE", now.AddMinutes(15)));

            scheduler.Snooze(scheduler.NextUp!, TimeSpan.FromMinutes(45), clock.Now);

            Assert.Equal(new DateTime(2027, 1, 1, 0, 30, 0), AlarmEngine.GetScheduledDateTime(scheduler.NextUp!));
        }

        // ---- Leap years ----

        [Fact]
        public void Leap_day_schedule_fires_and_rolls_into_march()
        {
            var now = new DateTime(2028, 2, 28, 22, 0, 0); // 2028 is a leap year
            var leapDay = Task("Leap day", new DateTime(2028, 2, 29, 23, 59, 0));
            var (clock, scheduler, _) = Make(now, null, leapDay);

            TaskItem? fired = null;
            scheduler.ReminderFired += t => fired = t;

            clock.Set(new DateTime(2028, 3, 1, 0, 0, 30)); // one minute past 23:59 on Feb 29
            scheduler.CheckTransitions(clock.Now);

            Assert.NotNull(fired);
            Assert.Equal(TaskState.Completed, leapDay.GetState());
        }

        [Fact]
        public void Non_leap_february_rolls_straight_from_feb_28_to_march_1()
        {
            var now = new DateTime(2027, 2, 28, 23, 58, 0); // 2027 is NOT a leap year
            var endOfMonth = Task("Feb end", new DateTime(2027, 2, 28, 23, 59, 0));
            var (clock, scheduler, _) = Make(now, null, endOfMonth);

            clock.Set(new DateTime(2027, 3, 1, 0, 0, 30));
            scheduler.CheckTransitions(clock.Now);

            Assert.Equal(TaskState.Completed, endOfMonth.GetState());
            Assert.Equal(3, clock.Now.Month);
        }

        // ---- DST spring-forward gap: treat as the next valid instant ----

        [Fact]
        public void Gap_time_resolves_to_the_next_valid_instant()
        {
            // 2027-03-14 02:30 does not exist in US Eastern (02:00 jumps to 03:00).
            DateTime gap = new(2027, 3, 14, 2, 30, 0);
            Assert.True(Eastern.IsInvalidTime(gap)); // test precondition

            Assert.Equal(new DateTime(2027, 3, 14, 3, 0, 0), AlarmEngine.ResolveDstGap(gap, Eastern));
        }

        [Fact]
        public void Valid_times_pass_through_unchanged()
        {
            Assert.Equal(
                new DateTime(2027, 3, 14, 1, 30, 0), // before the gap
                AlarmEngine.ResolveDstGap(new DateTime(2027, 3, 14, 1, 30, 0), Eastern));
            Assert.Equal(
                new DateTime(2027, 3, 14, 3, 30, 0), // after the gap
                AlarmEngine.ResolveDstGap(new DateTime(2027, 3, 14, 3, 30, 0), Eastern));
            // No-DST zone (UTC): pure no-op for any time.
            Assert.Equal(
                new DateTime(2027, 3, 14, 2, 30, 0),
                AlarmEngine.ResolveDstGap(new DateTime(2027, 3, 14, 2, 30, 0), TimeZoneInfo.Utc));
        }

        [Fact]
        public void Spring_forward_gap_alarm_fires_at_three_oclock_not_expired()
        {
            var task = Task("Gap alarm", new DateTime(2027, 3, 14, 2, 30, 0), SchedulerService.TaskTypeAlarm);
            var (clock, scheduler, _) = Make(new DateTime(2027, 3, 14, 1, 59, 0), Eastern, task);

            bool fired = false;
            scheduler.AlarmFired += _ => fired = true;

            // The wall clock jumps 01:59:59 -> 03:00:00 (no 02:xx minute ever exists).
            clock.Set(new DateTime(2027, 3, 14, 3, 0, 5));
            scheduler.CheckTransitions(clock.Now);

            Assert.True(fired);
            Assert.Equal(TaskState.Completed, task.GetState()); // fired, not Expired
        }

        [Fact]
        public void Os_toast_for_a_gap_time_uses_the_resolved_instant_too()
        {
            // The toast must fire at the same (valid) instant as the in-app alarm.
            var task = Task("Gap alarm", new DateTime(2027, 3, 14, 2, 30, 0));
            task.SetState(TaskState.Scheduled);

            ScheduledToastPlan plan = ToastSchedulePlanner.Plan(
                new[] { task }, new DateTime(2027, 3, 13, 12, 0, 0), Enumerable.Empty<ToastKey>(), Eastern);

            ToastAdd add = Assert.Single(plan.ToAdd);
            Assert.Equal(new DateTime(2027, 3, 14, 3, 0, 0), add.FireTime);
            Assert.Equal(new DateTime(2027, 3, 14, 3, 0, 0).ToString("yyyyMMddHHmm"), add.Key.Tag);
        }

        // ---- DST fall-back (ambiguous repeated hour): fire exactly once ----

        [Fact]
        public void Fall_back_repeated_hour_does_not_double_fire()
        {
            // 2027-11-07 01:30 occurs TWICE in US Eastern (02:00 falls back to 01:00).
            var task = Task("Ambiguous", new DateTime(2027, 11, 7, 1, 30, 0));
            var (clock, scheduler, _) = Make(new DateTime(2027, 11, 7, 1, 29, 0), Eastern, task);

            int fired = 0;
            scheduler.ReminderFired += _ => fired++;

            // First occurrence of 01:30 -> fires.
            clock.Set(new DateTime(2027, 11, 7, 1, 30, 30));
            scheduler.CheckTransitions(clock.Now);
            Assert.Equal(1, fired);

            // The hour repeats (clock shows 01:xx again) -> same occurrence key, no refire.
            clock.Set(new DateTime(2027, 11, 7, 2, 0, 0));
            scheduler.CheckTransitions(clock.Now);
            clock.Set(new DateTime(2027, 11, 7, 1, 30, 30));
            scheduler.CheckTransitions(clock.Now);

            Assert.Equal(1, fired);
            Assert.Equal(TaskState.Completed, task.GetState()); // not stuck, not refired
        }

        // ---- Multi-day gap (machine asleep / app closed for days) ----

        [Fact]
        public void Multi_day_gap_expires_missed_tasks_without_firing_retroactively()
        {
            var now = new DateTime(2026, 9, 13, 10, 0, 0);
            var duringGap = Task("During gap", new DateTime(2026, 9, 14, 8, 0, 0));
            var afterGap = Task("After gap", new DateTime(2026, 9, 20, 8, 0, 0));
            var (clock, scheduler, _) = Make(now, null, duringGap, afterGap);

            int fired = 0;
            scheduler.ReminderFired += _ => fired++;

            clock.Set(now.AddDays(3)); // three days later, mid-gap task long past
            scheduler.CheckTransitions(clock.Now);

            Assert.Equal(0, fired);                          // never fired retroactively
            Assert.Equal(TaskState.Expired, duringGap.GetState()); // resolved, not silently stuck
            Assert.Equal(TaskState.Scheduled, afterGap.GetState());
            Assert.Equal("After gap", scheduler.NextUp!.Title);
        }

        [Fact]
        public void Restart_normalization_after_a_multi_day_gap_matches_a_single_tick()
        {
            var now = new DateTime(2026, 9, 13, 10, 0, 0);
            // Two INDEPENDENT copies: the live pass mutates state, the restart pass must
            // reach the same states from pristine persisted records.
            var liveDuring = Task("During gap", new DateTime(2026, 9, 14, 8, 0, 0));
            var liveAfter = Task("After gap", new DateTime(2026, 9, 20, 8, 0, 0));
            var restartDuring = Task("During gap", new DateTime(2026, 9, 14, 8, 0, 0));
            var restartAfter = Task("After gap", new DateTime(2026, 9, 20, 8, 0, 0));

            // Single-tick pass (app stayed running across the gap).
            var (clock, live, _) = Make(now, null, liveDuring, liveAfter);
            clock.Set(now.AddDays(3));
            live.CheckTransitions(clock.Now);

            // Restart pass (app reopened after the gap) from the pristine records.
            var restarted = new SchedulerService(
                new FakeClock(now.AddDays(3)), new List<TaskItem> { restartDuring, restartAfter });
            restarted.ReplaceTasks();

            Assert.Equal(TaskState.Expired, liveDuring.GetState());
            Assert.Equal(liveDuring.GetState(), restartDuring.GetState()); // both Expired
            Assert.Equal(liveAfter.GetState(), restartAfter.GetState());   // both Scheduled
            Assert.Equal("After gap", restarted.NextUp!.Title);
        }
    }
}
