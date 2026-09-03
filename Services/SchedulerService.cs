using System;
using System.Collections.Generic;
using System.Linq;
using VibeAlarm.Models;

namespace VibeAlarm.Services
{
    /// <summary>
    /// Event-driven scheduler and state machine for tasks. It decides WHEN a task
    /// changes state (driven by <see cref="TimeService"/> minute/date/resume events) and
    /// raises notifications for the UI to react to; it does not render anything itself.
    ///
    /// Lifecycle (persisted via TaskItem.State):
    ///   Scheduled → Due → Triggered → Completed   (fires the alarm/reminder exactly once)
    ///   Scheduled ────────────────────→ Expired   (date passed without a trigger; kept as history)
    ///
    /// State is derived deterministically from (persisted state + scheduled time + current
    /// local time), so restarting cannot wrongly re-fire an old schedule and past schedules
    /// are never deleted — they are only retired from the "upcoming" view.
    /// </summary>
    public sealed class SchedulerService
    {
        public const string TaskTypeAlarm = "Alarm";
        public const string TaskTypeImportant = "Important";
        public const string TaskTypeNotification = "Notification";

        private readonly IClock clock;
        private readonly List<TaskItem> tasks;

        // Guard against duplicate in-session triggers regardless of minute alignment.
        private readonly HashSet<string> sessionFiredKeys = new(StringComparer.OrdinalIgnoreCase);

        private TaskItem? lastNextUp;
        private DateTime? nextUpTime;

        /// <summary>Fired when a non-alarm task reaches its scheduled time.</summary>
        public event Action<TaskItem>? ReminderFired;

        /// <summary>Fired when an alarm-typed task reaches its scheduled time.</summary>
        public event Action<TaskItem>? AlarmFired;

        /// <summary>Fired when one or more task states changed and persisted state was saved.</summary>
        public event Action? StateChanged;

        /// <summary>Fired when the "next up" schedule (or its remaining time) changed.</summary>
        public event Action<TaskItem?, TimeSpan>? NextUpChanged;

        public SchedulerService(IClock clock, List<TaskItem> taskList)
        {
            this.clock = clock;
            tasks = taskList;
        }

        public TimeSpan Countdown { get; private set; }

        /// <summary>Normalizes persisted task states and recomputes Next Up after loading.</summary>
        public void ReplaceTasks()
        {
            NormalizeStates(clock.Now);
            RecomputeNextUp(clock.Now);
        }

        /// <summary>
        /// Adds a newly created schedule and immediately reconciles its state against the
        /// current time so a past schedule can never enter the running set as "upcoming".
        /// This is defensive: creation-time UI validation normally rejects past datetimes,
        /// but the scheduler must not rely on the UI alone. Fires StateChanged if the task
        /// was retired to Expired.
        /// </summary>
        public void AddTask(TaskItem task)
        {
            tasks.Add(task);
            // CheckTransitions already reconciles the new task's state AND recomputes Next Up,
            // so a past schedule can never enter the running set as "upcoming". No redundant
            // second scan here (single authoritative transition + Next Up recompute).
            bool anyChanged = CheckTransitions(clock.Now);
            if (anyChanged)
            {
                StateChanged?.Invoke();
            }
        }

        /// <summary>
        /// True when the task's complete scheduled date+time is strictly in the future.
        /// Uses the full local DateTime (never just the date or hour), so "today at a
        /// past time" and "any past date" are rejected. This is the single authoritative
        /// predicate used by both the creation UI and the scheduler.
        /// </summary>
        public bool IsScheduledTimeValid(TaskItem task, DateTime now)
        {
            DateTime? scheduled = AlarmEngine.GetScheduledDateTime(task);
            return scheduled != null && scheduled.Value > now;
        }

        public IReadOnlyList<TaskItem> Tasks => tasks;

        public TaskItem? NextUp { get; private set; }

        /// <summary>
        /// Snoozes the given task: postpones its schedule by <paramref name="span"/>, returns it
        /// to a re-fireable Scheduled state, and recomputes Next Up. Used by the global status-bar
        /// Snooze action. Raising StateChanged lets the UI persist + refresh counters.
        /// </summary>
        public void Snooze(TaskItem task, TimeSpan span, DateTime now)
        {
            DateTime? scheduled = GetScheduledTime(task);
            if (scheduled == null)
            {
                return;
            }

            DateTime pushed = scheduled.Value.Add(span);
            task.ScheduledDate = pushed.ToString("yyyy-MM-dd");
            task.RemindTime = pushed.ToString("HH:mm");
            task.SetState(TaskState.Scheduled);

            // Allow the postponed occurrence to fire again.
            string oldKey = OccurrenceKey(task, scheduled.Value);
            sessionFiredKeys.Remove(oldKey);

            StateChanged?.Invoke();
            RecomputeNextUp(now);
        }

        private DateTime? GetScheduledTime(TaskItem task)
        {
            if (!DateTime.TryParse(task.ScheduledDate, out DateTime date))
            {
                date = clock.Now.Date;
            }
            if (!DateTime.TryParse(task.RemindTime, out DateTime time))
            {
                return null;
            }
            return new DateTime(date.Year, date.Month, date.Day, time.Hour, time.Minute, 0);
        }

        private static string OccurrenceKey(TaskItem task, DateTime scheduledTime)
            => $"{task.Id}|{scheduledTime:yyyy-MM-dd HH:mm}";

        private static DateTime TruncateToMinute(DateTime value)
            => new(value.Year, value.Month, value.Day, value.Hour, value.Minute, 0);

        /// <summary>
        /// True when the schedule is due to fire now: its scheduled minute is the current
        /// minute, or the immediately-preceding minute (a one-minute grace so a schedule at
        /// a minute boundary — e.g. a task at 23:59 evaluated just after midnight — is honored
        /// rather than spuriously retired). Schedules older than that are simply missed.
        /// </summary>
        private static bool InFireWindow(DateTime scheduled, DateTime now)
        {
            DateTime s = TruncateToMinute(scheduled);
            DateTime n = TruncateToMinute(now);
            return s == n || s == n.AddMinutes(-1);
        }

        /// <summary>
        /// Assigns a deterministic initial state to every task based on its persisted state
        /// and the current local time. Handles restart / catch-up deterministically:
        ///
        ///   • already-fired tasks stay Completed / Triggered (never re-fired);
        ///   • the nearest past minute (within a 60s grace) is left Due so a schedule that was
        ///     just about to fire (e.g. crossed midnight mid-window) is still honored;
        ///   • anything older becomes Expired (kept as history, never deleted) so a restart
        ///     does not incorrectly fire an old schedule.
        ///
        /// This makes restart behavior a pure function of persisted state + scheduled time + now.
        /// </summary>
        private void NormalizeStates(DateTime now)
        {
            foreach (TaskItem task in tasks)
            {
                TaskState current = task.GetState();
                if (current is TaskState.Completed or TaskState.Triggered or TaskState.Expired)
                {
                    continue;
                }

                DateTime? scheduled = GetScheduledTime(task);
                if (scheduled == null)
                {
                    continue;
                }

                if (InFireWindow(scheduled.Value, now))
                {
                    // Just reached (or just passed within the grace minute): fire on the next pass.
                    task.SetState(TaskState.Due);
                }
                else if (scheduled.Value <= now)
                {
                    // Missed older time → retire to history, never delete.
                    task.SetState(TaskState.Expired);
                }
            }
        }

        /// <summary>Called on every minute/date/resume boundary. Fires any task that is due.
        /// Returns true when one or more task states changed (and were persisted).</summary>
        public bool CheckTransitions(DateTime now)
        {
            bool anyChanged = false;

            for (int i = 0; i < tasks.Count; i++)
            {
                TaskItem task = tasks[i];
                TaskState state = task.GetState();

                if (state == TaskState.Completed || state == TaskState.Triggered || state == TaskState.Expired)
                {
                    continue;
                }

                DateTime? scheduled = GetScheduledTime(task);
                if (scheduled == null)
                {
                    continue;
                }

                // Not yet reached its time.
                if (scheduled.Value > now)
                {
                    task.SetState(TaskState.Scheduled);
                    continue;
                }

                // Reached, but beyond the grace minute → missed, retire to history (never delete).
                if (!InFireWindow(scheduled.Value, now))
                {
                    task.SetState(TaskState.Expired);
                    anyChanged = true;
                    continue;
                }

                // Fire once, then complete.
                string key = OccurrenceKey(task, scheduled.Value);
                if (!sessionFiredKeys.Contains(key))
                {
                    sessionFiredKeys.Add(key);
                    // Persists as Completed (preserves the legacy auto-complete UI) while the
                    // sessionFiredKeys guard guarantees a single trigger per occurrence.
                    task.SetState(TaskState.Completed);
                    anyChanged = true;

                    if (task.Type == TaskTypeAlarm)
                    {
                        AlarmFired?.Invoke(task);
                    }
                    else
                    {
                        ReminderFired?.Invoke(task);
                    }
                }
            }

            if (anyChanged)
            {
                StateChanged?.Invoke();
            }

            RecomputeNextUp(now);
            return anyChanged;
        }

        /// <summary>Recomputes the nearest upcoming schedule and raises NextUpChanged when it
        /// (the task or its remaining time) changed. Called on minute/date/state edges.</summary>
        public void RecomputeNextUp(DateTime now)
        {
            var candidate = tasks
                .Where(t => t.GetState() is TaskState.Scheduled or TaskState.Due)
                .Select(t => new { Task = t, Time = GetScheduledTime(t) })
                .Where(x => x.Time is DateTime s && s.Date >= now.Date && s >= now)
                .OrderBy(x => x.Time)
                .FirstOrDefault();

            TaskItem? next = candidate?.Task;
            DateTime? target = candidate?.Time;

            TimeSpan remaining = target != null
                ? target.Value - now
                : TimeSpan.Zero;

            bool taskChanged = !ReferenceEquals(next, lastNextUp) || target != nextUpTime;
            bool timeChanged = remaining != Countdown;
            NextUp = next;
            nextUpTime = target;
            Countdown = remaining;
            lastNextUp = next;

            if (taskChanged || timeChanged)
            {
                NextUpChanged?.Invoke(next, remaining);
            }
        }

        /// <summary>
        /// Cheap per-second update of the countdown using the cached Next Up target, without
        /// scanning the task list. Raises NextUpChanged only when the remaining time changes.
        /// </summary>
        public void RefreshCountdown(DateTime now)
        {
            if (nextUpTime == null)
            {
                if (Countdown != TimeSpan.Zero)
                {
                    Countdown = TimeSpan.Zero;
                    NextUpChanged?.Invoke(null, TimeSpan.Zero);
                }
                return;
            }

            TimeSpan remaining = nextUpTime.Value - now;
            if (remaining < TimeSpan.Zero)
            {
                remaining = TimeSpan.Zero;
            }

            if (remaining != Countdown)
            {
                Countdown = remaining;
                NextUpChanged?.Invoke(NextUp, remaining);
            }
        }
    }
}