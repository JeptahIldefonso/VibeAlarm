using System;
using System.Collections.Generic;
using System.Linq;
using VibeAlarm.Models;
using VibeAlarm.Services;
using Xunit;

namespace VibeAlarm.Tests
{
    /// <summary>
    /// ToastSchedulePlanner: the pure diff that keeps Windows' scheduled toasts in line
    /// with the task list — future Scheduled/Due tasks get toasts,
    /// past/completed/expired tasks never do, snoozes re-key the occurrence, and an
    /// in-sync current set produces a no-op.
    /// </summary>
    public class ToastSchedulePlannerTests
    {
        private static readonly DateTime Now = new(2026, 9, 13, 12, 0, 0);

        private static TaskItem Task(string id, string date, string time, TaskState state = TaskState.Scheduled)
        {
            TaskItem task = new()
            {
                Id = id,
                Title = $"Task {id}",
                ScheduledDate = date,
                RemindTime = time,
                Type = "Alarm",
            };
            task.SetState(state);
            return task;
        }

        private static string Tag(int day, int hour, int minute)
            => new DateTime(2026, 9, day, hour, minute, 0).ToString("yyyyMMddHHmm");

        [Fact]
        public void A_future_task_gets_a_toast_keyed_by_task_and_occurrence()
        {
            TaskItem task = Task("t1", "2026-09-13", "2:30 PM"); // today 14:30

            ScheduledToastPlan plan = ToastSchedulePlanner.Plan(new[] { task }, Now, Enumerable.Empty<ToastKey>());

            ToastAdd add = Assert.Single(plan.ToAdd);
            Assert.Equal(new ToastKey("t1", Tag(13, 14, 30)), add.Key);
            Assert.Equal(new DateTime(2026, 9, 13, 14, 30, 0), add.FireTime);
            Assert.Equal("Task t1", add.Title);
            Assert.Empty(plan.ToRemove);
        }

        [Theory]
        [InlineData(TaskState.Completed)]
        [InlineData(TaskState.Triggered)]
        [InlineData(TaskState.Expired)]
        public void Non_re_fireable_states_never_keep_or_get_a_toast(TaskState state)
        {
            TaskItem task = Task("t1", "2026-09-13", "2:30 PM", state);

            ScheduledToastPlan plan = ToastSchedulePlanner.Plan(new[] { task }, Now, Enumerable.Empty<ToastKey>());

            Assert.Empty(plan.ToAdd);
        }

        [Fact]
        public void A_past_task_is_not_scheduled_and_its_existing_toast_is_removed()
        {
            TaskItem task = Task("t1", "2026-09-13", "10:00 AM"); // already 11:59→ past
            var current = new[] { new ToastKey("t1", Tag(13, 10, 0)) };

            ScheduledToastPlan plan = ToastSchedulePlanner.Plan(new[] { task }, Now, current);

            Assert.Empty(plan.ToAdd);
            Assert.Equal(current, plan.ToRemove);
        }

        [Fact]
        public void Distant_future_tasks_are_scheduled_too_no_horizon_cap()
        {
            // No horizon cap: re-arming only happens at a reconcile (app running), so a
            // distant task must be armed the moment it's created — it can't wait for a
            // later re-arm that may never come.
            TaskItem near = Task("near", "2026-09-19", "11:00 AM");
            TaskItem far = Task("far", "2027-03-21", "11:00 AM");

            ScheduledToastPlan plan = ToastSchedulePlanner.Plan(new[] { near, far }, Now, Enumerable.Empty<ToastKey>());

            Assert.Equal(2, plan.ToAdd.Count);
            Assert.Contains(plan.ToAdd, add => add.Key.Group == "far");
        }

        [Fact]
        public void A_snoozed_task_re_keys_its_occurrence_old_toast_removed_new_one_added()
        {
            // The task was at 13:00 (toast already scheduled for that occurrence); the
            // snooze pushed it to 13:09.
            TaskItem task = Task("t1", "2026-09-13", "13:09");
            var current = new[] { new ToastKey("t1", Tag(13, 13, 0)) };

            ScheduledToastPlan plan = ToastSchedulePlanner.Plan(new[] { task }, Now, current);

            ToastAdd add = Assert.Single(plan.ToAdd);
            Assert.Equal(Tag(13, 13, 9), add.Key.Tag);
            ToastKey remove = Assert.Single(plan.ToRemove);
            Assert.Equal(new ToastKey("t1", Tag(13, 13, 0)), remove);
        }

        [Fact]
        public void A_deleted_task_loses_its_toast()
        {
            var current = new[] { new ToastKey("gone", Tag(14, 9, 0)), new ToastKey("kept", Tag(14, 9, 30)) };
            TaskItem kept = Task("kept", "2026-09-14", "9:30 AM");

            ScheduledToastPlan plan = ToastSchedulePlanner.Plan(new[] { kept }, Now, current);

            Assert.Empty(plan.ToAdd);
            ToastKey remove = Assert.Single(plan.ToRemove);
            Assert.Equal("gone", remove.Group);
        }

        [Fact]
        public void An_in_sync_set_is_a_no_op()
        {
            TaskItem task = Task("t1", "2026-09-13", "2:30 PM");
            var current = new[] { new ToastKey("t1", Tag(13, 14, 30)) };

            ScheduledToastPlan plan = ToastSchedulePlanner.Plan(new[] { task }, Now, current);

            Assert.Empty(plan.ToAdd);
            Assert.Empty(plan.ToRemove);
        }

        [Fact]
        public void Due_state_still_plans_because_the_occurrence_is_in_the_future()
        {
            // Due is assigned inside the fire window (current or previous minute); a Due
            // task a minute ahead of `now` still deserves its toast.
            TaskItem task = Task("t1", "2026-09-13", "12:01 PM", TaskState.Due);

            ScheduledToastPlan plan = ToastSchedulePlanner.Plan(new[] { task }, Now, Enumerable.Empty<ToastKey>());

            ToastAdd add = Assert.Single(plan.ToAdd);
            Assert.Equal(Tag(13, 12, 1), add.Key.Tag);
        }

        [Fact]
        public void RemindTime_accepts_both_dialog_and_snooze_formats()
        {
            // "hh:mm tt" (dialog) and "HH:mm" (post-snooze) must both parse — the two
            // writers tasks.json has always had.
            TaskItem twelveHour = Task("t12", "2026-09-14", "9:05 AM");
            TaskItem twentyFour = Task("t24", "2026-09-14", "09:05");

            ScheduledToastPlan plan = ToastSchedulePlanner.Plan(
                new[] { twelveHour, twentyFour }, Now, Enumerable.Empty<ToastKey>());

            Assert.Equal(2, plan.ToAdd.Count);
            Assert.All(plan.ToAdd, add => Assert.Equal(Tag(14, 9, 5), add.Key.Tag));
        }
    }
}
