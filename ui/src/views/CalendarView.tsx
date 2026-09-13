import { useEffect, useMemo, useState } from 'react';
import { request } from '../bridge/client';
import type { AppSettings, TaskItem } from '../bridge/protocol';
import { Icon } from '../components/Icon';
import type { TaskModalState } from '../components/TaskModal';
import { formatTime, taskDateTime } from '../lib/tasks';

const WEEKDAYS = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
const MONTHS = [
  'January', 'February', 'March', 'April', 'May', 'June',
  'July', 'August', 'September', 'October', 'November', 'December',
];

/**
 * The Calendar: a 7×6 month grid with today in an accent circle, day select
 * with a detail list, create-prefilled for the selected day, and the viewed
 * month persisted (yyyy-MM) so the app reopens where the user left off.
 */
export function CalendarView({
  tasks,
  now,
  settings,
  openModal,
}: {
  tasks: TaskItem[];
  now: Date;
  settings: AppSettings | null;
  openModal: (state: TaskModalState) => void;
}) {
  const [month, setMonth] = useState(parseMonth(settings?.lastViewedCalendarMonth) ?? startOfMonth(now));
  const [selected, setSelected] = useState(toKey(now));

  // Persist the viewed month once settings hydrate, and on every change after.
  const [hydrated, setHydrated] = useState(false);
  useEffect(() => {
    if (settings == null) return;
    if (!hydrated) {
      const persisted = parseMonth(settings.lastViewedCalendarMonth);
      if (persisted != null) setMonth(persisted);
      setHydrated(true);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [settings != null]);

  useEffect(() => {
    if (!hydrated || settings == null) return;
    const key = formatMonth(month);
    if (settings.lastViewedCalendarMonth !== key) {
      request('updateSettings', { ...settings, lastViewedCalendarMonth: key }).catch(() => {});
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [month, hydrated]);

  const byDay = useMemo(() => {
    const map = new Map<string, TaskItem[]>();
    for (const task of tasks) {
      const key = toKey(taskDateTime(task));
      if (key == null) continue;
      const list = map.get(key);
      if (list != null) list.push(task);
      else map.set(key, [task]);
    }
    // Schedule order inside each day, matching the detail list below.
    for (const list of map.values()) {
      list.sort((a, b) => {
        const da = taskDateTime(a)?.getTime() ?? 0;
        const db = taskDateTime(b)?.getTime() ?? 0;
        return da - db;
      });
    }
    return map;
  }, [tasks]);

  const cells = useMemo(() => gridCells(month), [month]);
  const selectedTasks = (byDay.get(selected) ?? []).slice().sort((a, b) => {
    const da = taskDateTime(a)?.getTime() ?? 0;
    const db = taskDateTime(b)?.getTime() ?? 0;
    return da - db;
  });

  const shiftMonth = (delta: number) => {
    setMonth((m) => new Date(m.getFullYear(), m.getMonth() + delta, 1));
  };

  return (
    <div className="view view-wide view-center">
      <div className="cal-toolbar">
        <h2 className="cal-month-label">{MONTHS[month.getMonth()]} {month.getFullYear()}</h2>
        <div className="cal-nav">
          <button type="button" className="icon-btn cal-nav-btn" aria-label="Previous month" onClick={() => shiftMonth(-1)}>‹</button>
          <button type="button" className="btn-ghost cal-today-btn" onClick={() => { setMonth(startOfMonth(now)); setSelected(toKey(now)); }}>Today</button>
          <button type="button" className="icon-btn cal-nav-btn" aria-label="Next month" onClick={() => shiftMonth(1)}>›</button>
        </div>
      </div>

      <div className="cal-card">
        <div className="cal-grid cal-weekdays">
          {WEEKDAYS.map((d) => <span key={d} className="cal-weekday">{d}</span>)}
        </div>
        <div className="cal-grid">
          {cells.map((cell) => {
            const dayTasks = byDay.get(cell.key);
            return (
              <button
                key={cell.key}
                type="button"
                className={`cal-cell${cell.other ? ' other' : ''}${cell.key === selected ? ' selected' : ''}`}
                aria-label={cellDateLabel(cell.key)}
                onClick={() => setSelected(cell.key)}
              >
                <span
                  className={`cal-day-num${cell.key === toKey(now) ? ' today' : ''}`}
                  aria-hidden
                >
                  {cell.day}
                </span>
                {(dayTasks?.length ?? 0) > 0 && (
                  <span className="cal-items" aria-hidden>
                    {dayTasks!.slice(0, 3).map((task, i) => (
                      <span key={task.id} className={`cal-item ${task.type.toLowerCase()}`}>
                        <span className="cal-item-name">{task.title}</span>
                        {/* More than three: "…" on the end of the last shown name. */}
                        {i === 2 && dayTasks!.length > 3 && <span className="cal-item-more">…</span>}
                      </span>
                    ))}
                  </span>
                )}
              </button>
            );
          })}
        </div>
      </div>

      <section className="cal-detail" aria-label="Tasks on selected day">
        <div className="cal-detail-head">
          <h3 className="section-label">{detailLabel(selected, now)}</h3>
          <button
            type="button"
            className="btn-primary cal-add-btn"
            onClick={() => openModal({ mode: 'create', task: null, presetDate: selected })}
          >
            <Icon name="clock-plus" size={15} /> Add task
          </button>
        </div>
        {selectedTasks.length === 0 ? (
          <p className="muted cal-detail-empty">Nothing scheduled this day.</p>
        ) : (
          <div className="task-list">
            {selectedTasks.map((task) => (
              <article key={task.id} className={`task-card${task.completed ? ' completed' : ''}`}>
                <span className={`cal-detail-dot ${task.type.toLowerCase()}`} aria-hidden />
                <div className="task-card-main">
                  <h4 className={task.completed ? 'task-title done' : 'task-title'}>{task.title}</h4>
                  <p className="task-meta">
                    <span className="task-type">{task.type}</span>
                    <span> · {formatTime(task.remindTime)}</span>
                  </p>
                </div>
              </article>
            ))}
          </div>
        )}
      </section>
    </div>
  );
}

/** The 42 cells (6 weeks, Sunday-first) covering the viewed month. */
function gridCells(month: Date): { key: string; day: number; other: boolean }[] {
  const first = new Date(month.getFullYear(), month.getMonth(), 1);
  const start = new Date(first);
  start.setDate(1 - first.getDay());
  const cells = [];
  for (let i = 0; i < 42; i++) {
    const date = new Date(start.getFullYear(), start.getMonth(), start.getDate() + i);
    cells.push({ key: toKey(date), day: date.getDate(), other: date.getMonth() !== month.getMonth() });
  }
  return cells;
}

function detailLabel(key: string, now: Date): string {
  const date = fromKey(key);
  if (date == null) return 'SELECTED DAY';
  if (date.toDateString() === now.toDateString()) return 'TODAY';
  return date.toLocaleDateString(undefined, { weekday: 'long', month: 'long', day: 'numeric' }).toUpperCase();
}

/** Accessible name for a day cell — the date, since the cell's visual contents are aria-hidden. */
function cellDateLabel(key: string): string {
  const date = fromKey(key);
  return date == null ? key : date.toLocaleDateString(undefined, { weekday: 'long', month: 'long', day: 'numeric', year: 'numeric' });
}

function startOfMonth(date: Date): Date {
  return new Date(date.getFullYear(), date.getMonth(), 1);
}

/** yyyy-MM-dd local — matches the stored scheduledDate format. */
function toKey(date: Date | null | undefined): string {
  if (date == null) return '';
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
}

function fromKey(key: string): Date | null {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(key);
  return match ? new Date(Number(match[1]), Number(match[2]) - 1, Number(match[3])) : null;
}

/** yyyy-MM local, the LastViewedCalendarMonth persistence format. */
function formatMonth(month: Date): string {
  return `${month.getFullYear()}-${String(month.getMonth() + 1).padStart(2, '0')}`;
}

function parseMonth(value: string | undefined): Date | null {
  if (value == null) return null;
  const match = /^(\d{4})-(\d{2})$/.exec(value);
  return match ? new Date(Number(match[1]), Number(match[2]) - 1, 1) : null;
}
