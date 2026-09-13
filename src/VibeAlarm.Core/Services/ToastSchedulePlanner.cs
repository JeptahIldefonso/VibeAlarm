using System;
using System.Collections.Generic;
using System.Linq;
using VibeAlarm.Models;

namespace VibeAlarm.Services
{
    /// <summary>Identity of one scheduled toast: <see cref="Group"/> is the owning task's
    /// Id, <see cref="Tag"/> identifies the specific occurrence (its fire time), so a
    /// snoozed task's old toast and new toast are different keys.</summary>
    public sealed record ToastKey(string Group, string Tag);

    /// <summary>One toast the host should schedule. Title/body are pre-rendered so the
    /// WinRT layer does zero policy.</summary>
    public sealed record ToastAdd(ToastKey Key, string TaskId, string Title, string Body, DateTime FireTime);

    /// <summary>The diff between the toasts that SHOULD exist and the ones that DO.</summary>
    public sealed record ScheduledToastPlan(IReadOnlyList<ToastAdd> ToAdd, IReadOnlyList<ToastKey> ToRemove);

    /// <summary>
    /// Pure planning logic for OS-scheduled alarm toasts: given the task list, the
    /// current time, and the set of toasts Windows currently holds, compute the
    /// adds/removes that bring Windows in line. No WinRT, no I/O — fully unit-testable.
    ///
    /// Desired set: every task in Scheduled/Due state whose trigger time is in the
    /// future — no horizon cap, because re-arming only ever happens at a reconcile
    /// (which requires the app to be running). A capped horizon would mean a task
    /// created beyond it and never followed by another app launch never gets its OS
    /// alarm. Completed, Triggered, and Expired tasks never keep a toast.
    /// </summary>
    public static class ToastSchedulePlanner
    {

        /// <param name="timeZone">Wall-clock rules used to resolve DST-gap schedule times
        /// (see <see cref="AlarmEngine.ResolveDstGap"/>); defaults to the local zone.</param>
        public static ScheduledToastPlan Plan(IEnumerable<TaskItem> tasks, DateTime now, IEnumerable<ToastKey> currentlyScheduled, TimeZoneInfo? timeZone = null)
        {
            TimeZoneInfo zone = timeZone ?? TimeZoneInfo.Local;
            var desired = new Dictionary<ToastKey, ToastAdd>();
            foreach (TaskItem task in tasks)
            {
                TaskState state = task.GetState();
                if (state is not (TaskState.Scheduled or TaskState.Due))
                {
                    continue;
                }

                DateTime? trigger = AlarmEngine.GetScheduledDateTime(task);
                if (trigger == null)
                {
                    continue;
                }

                // Same DST-gap resolution as the in-app scheduler, so both surfaces fire at
                // the same (valid) instant.
                DateTime fire = AlarmEngine.ResolveDstGap(trigger.Value, zone);
                if (fire <= now)
                {
                    // Due (in the fire window) or past — the running app owns firing it;
                    // a toast in the past can never be scheduled.
                    continue;
                }

                ToastKey key = new(task.Id, OccurrenceTag(fire));
                desired[key] = new ToastAdd(key, task.Id, task.Title, BuildBody(task, fire), fire);
            }

            IReadOnlyList<ToastKey> current = currentlyScheduled as IReadOnlyList<ToastKey> ?? currentlyScheduled.ToList();
            return new ScheduledToastPlan(
                ToAdd: desired.Where(entry => !current.Contains(entry.Key)).Select(entry => entry.Value).ToList(),
                ToRemove: current.Where(key => !desired.ContainsKey(key)).ToList());
        }

        /// <summary>Occurrence identity: the fire minute. A snooze moves the fire time,
        /// which changes the tag — the diff then removes the old toast and adds the new.</summary>
        public static string OccurrenceTag(DateTime fireTime) => fireTime.ToString("yyyyMMddHHmm");

        /// <summary>The toast body text — shared by scheduled toasts and the fire-time
        /// toast the host posts when an alarm fires, so both surfaces read identically.</summary>
        public static string BuildBody(TaskItem task, DateTime fire) =>
            $"{task.Type} · {fire:ddd, MMM d} · {fire:hh:mm tt}";
    }
}
