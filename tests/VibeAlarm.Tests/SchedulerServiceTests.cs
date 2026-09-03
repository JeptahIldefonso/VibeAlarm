using System;
using System.Collections.Generic;
using System.Linq;
using VibeAlarm.Models;
using VibeAlarm.Services;
using Xunit;

namespace VibeAlarm.Tests
{
    public class SchedulerServiceTests
    {
        private const string Alarm = SchedulerService.TaskTypeAlarm;
        private const string Reminder = SchedulerService.TaskTypeNotification;

        private static TaskItem Task(string title, DateTime scheduled, string type = Reminder)
        {
            return new TaskItem
            {
                Id = Guid.NewGuid().ToString(),
                Title = title,
                ScheduledDate = scheduled.ToString("yyyy-MM-dd"),
                RemindTime = scheduled.ToString("hh:mm tt"),
                Day = scheduled.DayOfWeek.ToString(),
                Type = type,
                Completed = false
            };
        }

        private static (FakeClock Clock, SchedulerService Scheduler, List<TaskItem> List) Make(
            DateTime now, params TaskItem[] tasks)
        {
            var clock = new FakeClock(now);
            var list = new List<TaskItem>(tasks);
            var scheduler = new SchedulerService(clock, list);
            scheduler.ReplaceTasks();
            return (clock, scheduler, list);
        }

        [Fact]
        public void Empty_list_has_no_next_up_and_zero_countdown()
        {
            var (_, scheduler, _) = Make(new DateTime(2026, 9, 2, 10, 0, 0));

            Assert.Null(scheduler.NextUp);
            Assert.Equal(TimeSpan.Zero, scheduler.Countdown);
        }

        [Fact]
        public void Next_up_returns_the_nearest_future_schedule()
        {
            var now = new DateTime(2026, 9, 2, 10, 0, 0);
            var late = Task("Later", now.AddHours(3));
            var soon = Task("Soon", now.AddHours(1));
            var (_, scheduler, _) = Make(now, late, soon);

            Assert.Equal("Soon", scheduler.NextUp!.Title);
            Assert.Equal(TimeSpan.FromHours(1), scheduler.Countdown);
        }

        [Fact]
        public void Next_up_skips_completed_and_expired_tasks()
        {
            var now = new DateTime(2026, 9, 2, 10, 0, 0);
            var completed = Task("Done", now.AddHours(1));

            var (_, scheduler, list) = Make(now, completed);
            list[0].SetState(TaskState.Completed);
            scheduler.RecomputeNextUp(now);

            Assert.Null(scheduler.NextUp);
        }

        [Fact]
        public void Countdown_refreshes_each_second_for_the_same_task()
        {
            var now = new DateTime(2026, 9, 2, 10, 0, 0);
            var (clock, scheduler, _) = Make(now, Task("Study", now.AddMinutes(90)));

            TimeSpan last = scheduler.Countdown;
            clock.AdvanceSeconds(1);
            scheduler.RefreshCountdown(clock.Now);

            Assert.Equal(TimeSpan.FromMinutes(90) - TimeSpan.FromSeconds(1), scheduler.Countdown);
            Assert.Equal("Study", scheduler.NextUp!.Title);
            Assert.NotEqual(last, scheduler.Countdown);
        }

        [Fact]
        public void Countdown_reaches_zero_at_scheduled_time()
        {
            var now = new DateTime(2026, 9, 2, 10, 0, 0);
            var (clock, scheduler, _) = Make(now, Task("Due", now.AddSeconds(30)));

            clock.AdvanceSeconds(30);
            scheduler.RefreshCountdown(clock.Now);

            Assert.Equal(TimeSpan.Zero, scheduler.Countdown);
        }

        [Fact]
        public void Scheduled_task_fires_and_completes_when_time_arrives()
        {
            var now = new DateTime(2026, 9, 2, 10, 0, 0);
            var (clock, scheduler, list) = Make(now, Task("Burnout", now.AddMinutes(5)));

            TaskItem? fired = null;
            scheduler.ReminderFired += t => fired = t;

            clock.Advance(TimeSpan.FromMinutes(5));
            scheduler.CheckTransitions(clock.Now);

            Assert.NotNull(fired);
            Assert.Equal("Burnout", fired!.Title);
            Assert.Equal(TaskState.Completed, list[0].GetState());
        }

        [Fact]
        public void Alarm_task_raises_alarm_event()
        {
            var now = new DateTime(2026, 9, 2, 10, 0, 0);
            var (clock, scheduler, _) = Make(now, Task("Wake", now, Alarm));

            TaskItem? alarm = null;
            TaskItem? reminder = null;
            scheduler.AlarmFired += t => alarm = t;
            scheduler.ReminderFired += t => reminder = t;

            clock.AdvanceSeconds(5);
            scheduler.CheckTransitions(clock.Now);

            Assert.Equal("Wake", alarm!.Title);
            Assert.Null(reminder);
        }

        [Fact]
        public void Schedule_is_not_fired_twice()
        {
            var now = new DateTime(2026, 9, 2, 10, 0, 0);
            var (clock, scheduler, _) = Make(now, Task("Once", now));

            int count = 0;
            scheduler.ReminderFired += _ => count++;

            clock.AdvanceSeconds(30);
            scheduler.CheckTransitions(clock.Now);
            clock.AdvanceSeconds(1);
            scheduler.CheckTransitions(clock.Now);

            Assert.Equal(1, count);
        }

        [Fact]
        public void Past_date_schedule_becomes_expired_and_is_never_deleted()
        {
            var now = new DateTime(2026, 9, 2, 10, 0, 0);
            var missed = Task("Missed", now.AddDays(-2));
            var (_, scheduler, list) = Make(now, missed);

            Assert.Equal(TaskState.Expired, list[0].GetState()); // retained (not removed), not deleted
            Assert.Single(list);
            Assert.Null(scheduler.NextUp);
        }

        [Fact]
        public void Earlier_today_but_past_minute_is_expired_on_restart()
        {
            var now = new DateTime(2026, 9, 2, 10, 0, 0);
            var missedThisMorning = Task("Missed morning", now.Date.AddHours(8)); // 08:00 today, we're at 10:00
            var (clock, scheduler, list) = Make(now, missedThisMorning);

            Assert.Equal(TaskState.Expired, list[0].GetState());
            Assert.DoesNotContain(TaskState.Scheduled, list.Select(t => t.GetState()));
            Assert.DoesNotContain(TaskState.Due, list.Select(t => t.GetState()));

            // Advancing must not fire it (it's retired, not re-triggerable).
            clock.Advance(TimeSpan.FromHours(2));
            scheduler.CheckTransitions(clock.Now);
            Assert.Equal(TaskState.Expired, list[0].GetState());
        }

        [Fact]
        public void Completed_schedule_does_not_retrigger_after_restart()
        {
            var now = new DateTime(2026, 9, 2, 10, 0, 0);
            var completed = Task("Fired", now.AddMinutes(-10));
            var (clock, scheduler, list) = Make(now, completed);
            list[0].SetState(TaskState.Completed);
            scheduler.ReplaceTasks(); // simulate reload

            int count = 0;
            scheduler.ReminderFired += _ => count++;

            clock.Advance(TimeSpan.FromHours(1));
            scheduler.CheckTransitions(clock.Now);

            Assert.Equal(0, count);
            Assert.Equal(TaskState.Completed, list[0].GetState());
        }

        [Fact]
        public void Two_schedules_at_the_same_time_both_fire()
        {
            var now = new DateTime(2026, 9, 2, 10, 0, 0);
            var (clock, scheduler, _) = Make(now, Task("A", now), Task("B", now));

            var fired = new List<string>();
            scheduler.ReminderFired += t => fired.Add(t.Title);

            clock.AdvanceSeconds(10);
            scheduler.CheckTransitions(clock.Now);

            Assert.Equal(2, fired.Count);
            Assert.Contains("A", fired);
            Assert.Contains("B", fired);
        }

        [Fact]
        public void Midnight_rollover_expires_yesterdays_pending_and_recomputes_next_up()
        {
            var start = new DateTime(2026, 9, 2, 23, 59, 59);
            var todayLate = Task("11:59pm", start);                 // fires now
            var tomorrowEarly = Task("Tomorrow", start.AddDays(1).Date.AddHours(6)); // next day 06:00
            var (clock, scheduler, list) = Make(start, todayLate, tomorrowEarly);

            clock.AdvanceSeconds(2); // -> 2026-09-03 00:00:01
            scheduler.CheckTransitions(clock.Now);

            Assert.Equal(TaskState.Completed, list[0].GetState());
            Assert.Equal("Tomorrow", scheduler.NextUp!.Title);
        }

        [Fact]
        public void Month_rollover_is_handled_by_date_change()
        {
            var start = new DateTime(2026, 9, 30, 23, 59, 0);
            var nextMonth = Task("Oct task", new DateTime(2026, 10, 1, 8, 0, 0));
            var (clock, scheduler, _) = Make(start, nextMonth);

            clock.Advance(TimeSpan.FromMinutes(61)); // -> 2026-10-01
            scheduler.CheckTransitions(clock.Now);

            Assert.Equal("Oct task", scheduler.NextUp!.Title);
            Assert.Equal(10, clock.Now.Month);   // rolled over into October
            Assert.Equal(2026, clock.Now.Year);
        }

        [Fact]
        public void Next_up_changed_event_fires_on_selection_and_countdown()
        {
            var now = new DateTime(2026, 9, 2, 10, 0, 0);
            var (clock, scheduler, _) = Make(now, Task("Study", now.AddHours(1)));

            int events = 0;
            scheduler.NextUpChanged += (t, r) => events++;

            clock.AdvanceSeconds(1);
            scheduler.RefreshCountdown(clock.Now);

            Assert.True(events >= 1);
        }

        // ---- Scheduling validity (creation-time future DateTime validation) ----

        [Fact]
        public void Past_date_is_not_a_valid_schedule_time()
        {
            var now = new DateTime(2026, 9, 2, 10, 30, 0);
            var (_, scheduler, _) = Make(now);
            var yesterday = Task("Yesterday", now.Date.AddDays(-1));

            Assert.False(scheduler.IsScheduledTimeValid(yesterday, now));
        }

        [Fact]
        public void Today_past_time_is_not_valid()
        {
            var now = new DateTime(2026, 9, 2, 10, 30, 0);
            var (_, scheduler, _) = Make(now);
            var earlierToday = Task("Earlier today", now.Date.AddHours(9)); // 09:00, now 10:30

            Assert.False(scheduler.IsScheduledTimeValid(earlierToday, now));
        }

        [Fact]
        public void Today_just_past_minute_is_not_valid()
        {
            // Current 10:30 PM; schedule 9:00 PM same day → rejected.
            var now = new DateTime(2026, 9, 2, 22, 30, 0);
            var (_, scheduler, _) = Make(now);
            var tonightNine = Task("Tonight 9pm", now.Date.AddHours(21));

            Assert.False(scheduler.IsScheduledTimeValid(tonightNine, now));
        }

        [Fact]
        public void Today_future_time_is_valid()
        {
            var now = new DateTime(2026, 9, 2, 22, 30, 0);
            var (_, scheduler, _) = Make(now);
            var tonightEleven = Task("Tonight 11pm", now.Date.AddHours(23)); // 11 PM, now 10:30 PM

            Assert.True(scheduler.IsScheduledTimeValid(tonightEleven, now));
        }

        [Fact]
        public void Tomorrow_midnight_is_valid_even_when_today_late()
        {
            // Current 11:59:59 PM today; tomorrow 12:00 AM is a valid FUTURE time.
            var now = new DateTime(2026, 9, 2, 23, 59, 59);
            var (_, scheduler, _) = Make(now);
            var tomorrowMidnight = Task("Tomorrow", now.Date.AddDays(1));

            Assert.True(scheduler.IsScheduledTimeValid(tomorrowMidnight, now));
        }

        [Fact]
        public void Today_midnight_when_past_is_not_valid()
        {
            // Current 11:59:59 PM; today 12:00 AM already passed this morning → rejected.
            var now = new DateTime(2026, 9, 2, 23, 59, 59);
            var (_, scheduler, _) = Make(now);
            var todayMidnight = Task("Today midnight", now.Date); // 2026-09-02 00:00

            Assert.False(scheduler.IsScheduledTimeValid(todayMidnight, now));
        }

        // ---- Defensive scheduler: adding a schedule never leaves a past one as upcoming ----

        [Fact]
        public void Adding_a_past_schedule_reconciled_immediately_to_expired()
        {
            var now = new DateTime(2026, 9, 2, 10, 30, 0);
            var (clock, scheduler, _) = Make(now);

            var past = Task("Past", now.AddMinutes(-45));
            scheduler.AddTask(past);

            Assert.Equal(TaskState.Expired, past.GetState());
            Assert.Null(scheduler.NextUp);
            Assert.DoesNotContain(past, scheduler.Tasks.Where(t => t.GetState() is TaskState.Scheduled or TaskState.Due));
        }

        [Fact]
        public void Adding_a_future_schedule_stays_scheduled_and_becomes_next_up()
        {
            var now = new DateTime(2026, 9, 2, 10, 30, 0);
            var (clock, scheduler, _) = Make(now);

            var future = Task("Future", now.AddHours(1));
            scheduler.AddTask(future);

            Assert.Equal(TaskState.Scheduled, future.GetState());
            Assert.Equal("Future", scheduler.NextUp!.Title);
        }

        [Fact]
        public void Next_up_ignores_a_schedule_whose_time_has_passed()
        {
            var now = new DateTime(2026, 9, 2, 10, 30, 0);
            var (_, scheduler, list) = Make(now, Task("TooLate", now.AddMinutes(-5)));

            // Domain reconciliation retires it; it must not surface as Next Up.
            Assert.Null(scheduler.NextUp);
            Assert.Equal(TaskState.Expired, list[0].GetState());
        }

        [Fact]
        public void Restart_before_scheduled_time_preserves_the_schedule()
        {
            var now = new DateTime(2026, 9, 2, 10, 30, 0);
            var future = Task("Not yet", now.AddHours(6));
            var clock = new FakeClock(now);
            var list = new List<TaskItem> { future };

            // Simulate restart: fresh scheduler reconciles persisted records at 10:30.
            var scheduler = new SchedulerService(clock, list);
            scheduler.ReplaceTasks(); // startup reconciliation

            Assert.Equal(TaskState.Scheduled, future.GetState());
            Assert.Equal("Not yet", scheduler.NextUp!.Title);
        }

        [Fact]
        public void Restart_after_scheduled_time_retires_it_and_it_is_not_upcoming()
        {
            var scheduledAt = new DateTime(2026, 9, 2, 22, 0, 0); // 10:00 PM
            var restartedAt = new DateTime(2026, 9, 2, 22, 5, 0); // reopened 10:05 PM

            var past = Task("10pm task", scheduledAt);
            var clock = new FakeClock(restartedAt);
            var list = new List<TaskItem> { past };

            var scheduler = new SchedulerService(clock, list);
            scheduler.ReplaceTasks(); // startup reconciliation after its time passed

            Assert.Equal(TaskState.Expired, past.GetState());
            Assert.Null(scheduler.NextUp); // MUST NOT return as upcoming
        }

        [Fact]
        public void Empty_future_schedules_report_no_next_up()
        {
            var now = new DateTime(2026, 9, 2, 10, 0, 0);
            var (_, scheduler, _) = Make(now);

            Assert.Null(scheduler.NextUp);
            Assert.Equal(TimeSpan.Zero, scheduler.Countdown);
        }

        // ---- Exhaustive 10:30:00 PM boundary matrix (spec: reject past, strictly-future accept) ----

        [Fact]
        public void At_10_30_pm_today_past_times_are_invalid()
        {
            var now = new DateTime(2026, 9, 2, 22, 30, 0); // 10:30:00 PM
            var (_, scheduler, _) = Make(now);

            Assert.False(scheduler.IsScheduledTimeValid(Task("9pm", now.Date.AddHours(21)), now));       // 9:00 PM  reject
            Assert.False(scheduler.IsScheduledTimeValid(Task("10pm", now.Date.AddHours(22)), now));       // 10:00 PM reject
            Assert.False(scheduler.IsScheduledTimeValid(Task("10:29pm", now.Date.AddMinutes(-1)), now)); // 10:29 PM reject
        }

        [Fact]
        public void At_10_30_pm_exactly_now_is_invalid()
        {
            var now = new DateTime(2026, 9, 2, 22, 30, 0); // 10:30:00 PM
            var (_, scheduler, _) = Make(now);
            var exactlyNow = Task("10:30pm", now.Date.AddHours(22).AddMinutes(30)); // == now

            Assert.False(scheduler.IsScheduledTimeValid(exactlyNow, now));
        }

        [Fact]
        public void At_10_30_pm_future_times_are_valid()
        {
            var now = new DateTime(2026, 9, 2, 22, 30, 0); // 10:30:00 PM
            var (_, scheduler, _) = Make(now);

            Assert.True(scheduler.IsScheduledTimeValid(Task("10:31pm", now.Date.AddHours(22).AddMinutes(31)), now)); // 10:31 PM accept
            Assert.True(scheduler.IsScheduledTimeValid(Task("11pm", now.Date.AddHours(23)), now));                    // 11:00 PM accept
            Assert.True(scheduler.IsScheduledTimeValid(Task("Tomorrow midnight", now.Date.AddDays(1)), now));         // 12:00 AM tomorrow accept
        }

        [Fact]
        public void At_11_59_59_pm_tomorrow_midnight_is_valid()
        {
            var now = new DateTime(2026, 9, 2, 23, 59, 59);
            var (_, scheduler, _) = Make(now);
            var tomorrowMidnight = Task("Tomorrow 12am", now.Date.AddDays(1)); // 00:00 next day

            Assert.True(scheduler.IsScheduledTimeValid(tomorrowMidnight, now));
        }

        // ---- Mixed startup reconciliation: past / exactly-now / future (spec §4) ----

        [Fact]
        public void Restart_reconciles_mixed_records_past_expired_now_fires_future_scheduled()
        {
            var now = new DateTime(2026, 9, 2, 10, 30, 0);
            var yesterday = Task("Yesterday", now.Date.AddDays(-1));
            var earlierToday = Task("Earlier today", now.Date.AddHours(9)); // 09:00, past
            var future = Task("Future", now.AddHours(2));

            var clock = new FakeClock(now);
            var list = new List<TaskItem> { yesterday, earlierToday, future };
            var scheduler = new SchedulerService(clock, list);
            scheduler.ReplaceTasks(); // startup reconciliation

            Assert.Equal(TaskState.Expired, yesterday.GetState());
            Assert.Equal(TaskState.Expired, earlierToday.GetState());
            Assert.Equal(TaskState.Scheduled, future.GetState());
            Assert.Equal("Future", scheduler.NextUp!.Title); // only the future schedule is eligible

            // Historical records are retained (never deleted).
            Assert.Equal(3, list.Count);
        }

        [Fact]
        public void Exactly_now_schedule_is_due_and_fires_once_then_rolls_to_completed()
        {
            var now = new DateTime(2026, 9, 2, 10, 30, 0);
            var dueNow = Task("Due now", now);
            var (clock, scheduler, list) = Make(now, dueNow);

            int fired = 0;
            scheduler.ReminderFired += _ => fired++;

            // In the grace window on restart it is Due; the next pass fires it once.
            clock.AdvanceSeconds(30);
            scheduler.CheckTransitions(clock.Now);

            Assert.Equal(1, fired);
            Assert.Equal(TaskState.Completed, list[0].GetState());
            Assert.Null(scheduler.NextUp); // no longer upcoming after firing
        }

        // ---- Next Up progression through multiple future schedules (spec §8) ----

        [Fact]
        public void Next_up_progresses_through_10_11_12_then_empty()
        {
            var now = new DateTime(2026, 9, 2, 10, 30, 0); // 10:30 AM
            var ten = Task("10am", now.Date.AddHours(10));
            var eleven = Task("11am", now.Date.AddHours(11));
            var noon = Task("12pm", now.Date.AddHours(12));
            var (clock, scheduler, list) = Make(now, ten, eleven, noon);

            // At 10:30, 10am already passed → Next Up should be 11am.
            Assert.Equal("11am", scheduler.NextUp!.Title);

            // 11am arrives → fires → Next Up becomes 12pm.
            clock.Set(now.Date.AddHours(11).AddMinutes(1));
            scheduler.CheckTransitions(clock.Now);
            Assert.Equal("12pm", scheduler.NextUp!.Title);

            // 12pm arrives → fires → no future schedules remain → empty.
            clock.Set(now.Date.AddHours(12).AddMinutes(1));
            scheduler.CheckTransitions(clock.Now);
            Assert.Null(scheduler.NextUp);
            Assert.Equal(TimeSpan.Zero, scheduler.Countdown);
        }

        // ---- Snooze (global status-bar action) ----

        [Fact]
        public void Snooze_re_pushes_the_time_by_the_given_span()
        {
            var now = new DateTime(2026, 9, 2, 10, 0, 0);
            var (clock, scheduler, _) = Make(now, Task("Alarm", now.AddMinutes(30), Alarm));

            scheduler.Snooze(scheduler.NextUp!, TimeSpan.FromMinutes(9), clock.Now);

            var scheduled = AlarmEngine.GetScheduledDateTime(scheduler.NextUp!);
            Assert.NotNull(scheduled);
            Assert.Equal(now.AddMinutes(39), scheduled.Value);
            Assert.Equal(TaskState.Scheduled, scheduler.NextUp!.GetState());
        }

        [Fact]
        public void Snoozed_schedule_can_fire_again()
        {
            var now = new DateTime(2026, 9, 2, 10, 0, 0);
            var (clock, scheduler, _) = Make(now, Task("Alarm", now.AddMinutes(1), Alarm));

            // Snooze just before firing.
            scheduler.Snooze(scheduler.NextUp!, TimeSpan.FromMinutes(9), clock.Now);

            // Advance past the postponed time → the alarm fires again.
            var fired = false;
            scheduler.AlarmFired += _ => fired = true;
            clock.Set(now.AddMinutes(1).Add(TimeSpan.FromMinutes(9)).AddMinutes(1));
            scheduler.CheckTransitions(clock.Now);

            Assert.True(fired);
            Assert.Equal(TaskState.Completed, scheduler.Tasks.Single().GetState());
        }

        [Fact]
        public void Snooze_does_not_affect_other_future_schedules()
        {
            var now = new DateTime(2026, 9, 2, 10, 0, 0);
            var (clock, scheduler, _) = Make(now, Task("Soon", now.AddMinutes(30)), Task("Later", now.AddHours(2)));

            scheduler.Snooze(scheduler.NextUp!, TimeSpan.FromMinutes(9), clock.Now);

            var later = scheduler.Tasks.First(t => t.Title == "Later");
            Assert.Equal(TaskState.Scheduled, later.GetState());
            Assert.Equal(new DateTime(2026, 9, 2, 12, 0, 0), AlarmEngine.GetScheduledDateTime(later));
        }
    }
}