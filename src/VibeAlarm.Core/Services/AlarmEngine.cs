using System;
using System.Globalization;
using VibeAlarm.Models;

namespace VibeAlarm.Services
{
    /// <summary>
    /// Static scheduling/time helpers for resolving a task's concrete date+time. The
    /// authoritative due-fire state machine lives in <see cref="SchedulerService"/> — this
    /// type no longer harbors a parallel due-check. Type constants are single-sourced from
    /// <see cref="SchedulerService"/> to avoid drift.
    ///
    /// This is the ONE place that parses <see cref="TaskItem.ScheduledDate"/> /
    /// <see cref="TaskItem.RemindTime"/> strings into a DateTime. Every consumer
    /// (<see cref="SchedulerService"/>, <see cref="ToastSchedulePlanner"/>,
    /// ToastSchedulerService) must go through <see cref="GetScheduledDateTime"/> — never
    /// re-implement the parse, or a format change on one writer silently desyncs the rest.
    /// </summary>
    public static class AlarmEngine
    {
        public const string TaskTypeNotification = SchedulerService.TaskTypeNotification;
        public const string TaskTypeAlarm = SchedulerService.TaskTypeAlarm;
        public const string TaskTypeImportant = SchedulerService.TaskTypeImportant;

        /// <summary>The only date format any writer persists: "yyyy-MM-dd" (legacy WinForms
        /// dialog, React modal, Snooze, IpcBridge default — all write it explicitly).</summary>
        private const string DateFormat = "yyyy-MM-dd";

        /// <summary>Every RemindTime format a writer can persist, parsed exactly with the
        /// invariant culture (never a culture-sensitive general parse):
        /// "HH:mm" — Snooze re-writes (SchedulerService/ToastSchedulerService);
        /// "hh:mm tt" — legacy WinForms dialog + IpcBridge's default;
        /// "h:mm tt" — the React modal's toStoredTime (unpadded hour).</summary>
        private static readonly string[] TimeFormats = { "HH:mm", "hh:mm tt", "h:mm tt" };

        /// <summary>Builds a stable key identifying one task firing during one specific minute.</summary>
        public static string BuildAlertKey(TaskItem task, DateTime now)
        {
            return $"{task.Id}|{now:yyyy-MM-dd HH:mm}";
        }

        /// <summary>Resolves the concrete date a task is scheduled for. An absent/unparseable
        /// ScheduledDate (only possible in corrupt or hand-edited records — every creation
        /// path writes one) falls back to the next occurrence of the legacy Day-of-week field.</summary>
        public static DateTime GetTaskDate(TaskItem task)
        {
            if (!string.IsNullOrWhiteSpace(task.ScheduledDate) &&
                DateTime.TryParseExact(task.ScheduledDate, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedDate))
            {
                return parsedDate.Date;
            }

            return NextDateForDay(task.Day, DateTime.Today);
        }

        /// <summary>Resolves the full scheduled date+time for a task, or null when the time is unparseable.</summary>
        public static DateTime? GetScheduledDateTime(TaskItem task)
        {
            DateTime date = GetTaskDate(task);
            if (!DateTime.TryParseExact(task.RemindTime, TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime time))
            {
                return null;
            }
            return new DateTime(date.Year, date.Month, date.Day, time.Hour, time.Minute, 0);
        }

        /// <summary>
        /// Resolves a wall-clock schedule time that falls inside a DST spring-forward gap —
        /// a time that never exists locally (e.g. 02:30 where 02:00 jumps to 03:00). The
        /// intended behavior: treat it as the NEXT valid instant (02:30 → 03:00), the same
        /// convention phone alarms use — NOT "missed/Expired", which would silently kill an
        /// alarm the user set for a time they had no way to know was invalid. Times outside
        /// a gap pass through unchanged, so non-DST locales (and UTC) are pure no-ops.
        /// </summary>
        public static DateTime ResolveDstGap(DateTime scheduled, TimeZoneInfo timeZone)
        {
            if (scheduled.Kind == DateTimeKind.Utc || !timeZone.IsInvalidTime(scheduled))
            {
                return scheduled;
            }

            // Gaps are at most a few hours; walk forward to the first valid wall-clock minute.
            DateTime resolved = scheduled;
            for (int i = 0; i < 360 && timeZone.IsInvalidTime(resolved); i++)
            {
                resolved = resolved.AddMinutes(1);
            }
            return resolved;
        }

        /// <summary>Formats a task's scheduled time as a 12-hour label (e.g. "07:02 PM").</summary>
        public static string GetScheduledTimeLabel(TaskItem task)
        {
            DateTime? scheduled = GetScheduledDateTime(task);
            return scheduled?.ToString("hh:mm tt") ?? task.RemindTime;
        }

        private static DateTime NextDateForDay(string dayName, DateTime fromDate)
        {
            if (!Enum.TryParse(dayName, true, out DayOfWeek targetDay))
            {
                return fromDate.Date;
            }

            int daysUntilTarget = ((int)targetDay - (int)fromDate.DayOfWeek + 7) % 7;
            return fromDate.Date.AddDays(daysUntilTarget);
        }
    }
}
