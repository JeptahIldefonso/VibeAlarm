import type { TaskItem } from '../bridge/protocol';

/**
 * Task display helpers — mirror the WinForms-era formats exactly:
 * ScheduledDate "yyyy-MM-dd", RemindTime "hh:mm tt" (dialog) or "HH:mm"
 * (post-snooze), both parsing with the same tolerance as C#
 * DateTime.TryParse.
 */

/** Parses a task's stored date+time into a Date, or null when unparseable. */
export function taskDateTime(task: TaskItem): Date | null {
  if (!task.scheduledDate) return null;
  const date = new Date(`${task.scheduledDate}T00:00:00`);
  if (Number.isNaN(date.getTime())) return null;

  const time = parseTime(task.remindTime);
  if (time == null) return null;

  const result = new Date(date);
  result.setHours(time.hours, time.minutes, 0, 0);
  return result;
}

function parseTime(value: string): { hours: number; minutes: number } | null {
  if (!value) return null;
  const match = /^(\d{1,2}):(\d{2})\s*(am|pm)?$/i.exec(value.trim());
  if (!match) return null;
  let hours = Number(match[1]);
  const minutes = Number(match[2]);
  const meridiem = match[3]?.toLowerCase();
  if (Number.isNaN(hours) || Number.isNaN(minutes) || hours < 0 || hours > 23 || minutes > 59) return null;
  if (meridiem) {
    hours = hours % 12;
    if (meridiem === 'pm') hours += 12;
  }
  return { hours, minutes };
}

/** "Today", "Tomorrow", or "Fri, Sep 25" for the task's date. */
export function whenDayLabel(task: TaskItem, today: Date): string {
  const date = taskDateTime(task);
  if (date == null) return task.scheduledDate;
  const day = startOfDay(date);
  const todayStart = startOfDay(today);
  const dayDiff = Math.round((day.getTime() - todayStart.getTime()) / 86_400_000);
  if (dayDiff === 0) return 'Today';
  if (dayDiff === 1) return 'Tomorrow';
  if (dayDiff === -1) return 'Yesterday';
  return date.toLocaleDateString(undefined, { weekday: 'short', month: 'short', day: 'numeric' });
}

/** The full "Today · 8:00 PM" card label. */
export function whenLabel(task: TaskItem, today: Date): string {
  return `${whenDayLabel(task, today)} · ${formatTime(task.remindTime)}`;
}

/** Renders a stored RemindTime as "h:mm AM/PM" for display. */
export function formatTime(remindTime: string): string {
  const parsed = parseTime(remindTime);
  if (parsed == null) return remindTime;
  const meridiem = parsed.hours >= 12 ? 'PM' : 'AM';
  const twelve = parsed.hours % 12 || 12;
  return `${twelve}:${String(parsed.minutes).padStart(2, '0')} ${meridiem}`;
}

/** Formats a Date as the "hh:mm tt" the host stores (dialog format). */
export function toStoredTime(date: Date): string {
  const meridiem = date.getHours() >= 12 ? 'PM' : 'AM';
  const twelve = date.getHours() % 12 || 12;
  return `${twelve}:${String(date.getMinutes()).padStart(2, '0')} ${meridiem}`;
}

/** Formats a Date as "yyyy-MM-dd" (local). */
export function toStoredDate(date: Date): string {
  const y = date.getFullYear();
  const m = String(date.getMonth() + 1).padStart(2, '0');
  const d = String(date.getDate()).padStart(2, '0');
  return `${y}-${m}-${d}`;
}

function startOfDay(date: Date): Date {
  return new Date(date.getFullYear(), date.getMonth(), date.getDate());
}

export function isToday(task: TaskItem, today: Date): boolean {
  return whenDayLabel(task, today) === 'Today';
}

export function isUpcoming(task: TaskItem, today: Date): boolean {
  const date = taskDateTime(task);
  return date != null && startOfDay(date).getTime() > startOfDay(today).getTime();
}

export function isPast(task: TaskItem, today: Date): boolean {
  const date = taskDateTime(task);
  return date != null && startOfDay(date).getTime() < startOfDay(today).getTime();
}
