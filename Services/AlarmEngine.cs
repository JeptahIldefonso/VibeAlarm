using System;
using VibeAlarm.Models;

namespace VibeAlarm.Services
{
    /// <summary>
    /// Static scheduling/time helpers for resolving a task's concrete date+time. The
    /// authoritative due-fire state machine lives in <see cref="SchedulerService"/> — this
    /// type no longer harbors a parallel due-check. Type constants are single-sourced from
    /// <see cref="SchedulerService"/> to avoid drift.
    /// </summary>
    public static class AlarmEngine
    {
        public const string TaskTypeNotification = SchedulerService.TaskTypeNotification;
        public const string TaskTypeAlarm = SchedulerService.TaskTypeAlarm;
        public const string TaskTypeImportant = SchedulerService.TaskTypeImportant;

        /// <summary>Builds a stable key identifying one task firing during one specific minute.</summary>
        public static string BuildAlertKey(TaskItem task, DateTime now)
        {
            return $"{task.Id}|{now:yyyy-MM-dd HH:mm}";
        }

        /// <summary>Resolves the concrete date a task is scheduled for.</summary>
        public static DateTime GetTaskDate(TaskItem task)
        {
            if (!string.IsNullOrWhiteSpace(task.ScheduledDate) &&
                DateTime.TryParse(task.ScheduledDate, out DateTime parsedDate))
            {
                return parsedDate.Date;
            }

            return NextDateForDay(task.Day, DateTime.Today);
        }

        /// <summary>Resolves the full scheduled date+time for a task, or null when the time is unparseable.</summary>
        public static DateTime? GetScheduledDateTime(TaskItem task)
        {
            DateTime date = GetTaskDate(task);
            if (!DateTime.TryParse(task.RemindTime, out DateTime time))
            {
                return null;
            }
            return new DateTime(date.Year, date.Month, date.Day, time.Hour, time.Minute, 0);
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
