import { useEffect, useState } from 'react';
import { request, subscribe } from '../bridge/client';
import type { TaskItem } from '../bridge/protocol';
import { Icon } from '../components/Icon';
import { whenLabel } from '../lib/tasks';

/**
 * The in-app alarm surface (the hybrid's "app is running" half): a full-screen
 * overlay with looping host-side sound and 9-minute Snooze. The host removes the
 * OS toast the instant this appears, so exactly one surface asks the question.
 * Also renders the transient reminder toasts.
 */
export function AlarmLayer({ now }: { now: Date }) {
  const [alarm, setAlarm] = useState<TaskItem | null>(null);
  const [reminders, setReminders] = useState<{ task: TaskItem; id: number }[]>([]);

  useEffect(() =>
    subscribe('alarmFired', (task: TaskItem) => {
      setAlarm(task);
    }), []);

  useEffect(() => {
    let nextId = 1;
    return subscribe('reminderFired', (task: TaskItem) => {
      const id = nextId++;
      setReminders((prev) => [...prev, { task, id }]);
      window.setTimeout(() => {
        setReminders((prev) => prev.filter((r) => r.id !== id));
      }, 7000);
    });
  }, []);

  if (alarm == null && reminders.length === 0) return null;

  return (
    <>
      {alarm != null && (
        <div className="alarm-overlay" role="alertdialog" aria-label={`Alarm: ${alarm.title}`}>
          <div className="alarm-card">
            <span className="alarm-icon" aria-hidden>
              <Icon name="siren" size={28} />
            </span>
            <p className="alarm-kicker">ALARM</p>
            <h1 className="alarm-title">{alarm.title}</h1>
            <p className="alarm-when">{whenLabel(alarm, now)}</p>
            <div className="alarm-actions">
              <button
                type="button"
                className="btn-primary"
                onClick={() => {
                  request('snoozeTask', { id: alarm.id, minutes: 9 }).catch(() => {});
                  setAlarm(null);
                }}
              >
                Snooze 9 min
              </button>
              <button
                type="button"
                className="btn-ghost"
                onClick={() => {
                  request('dismissAlarm').catch(() => {});
                  setAlarm(null);
                }}
              >
                Dismiss
              </button>
            </div>
          </div>
        </div>
      )}

      {reminders.map(({ task, id }) => (
        <div key={id} className="reminder-toast" role="status">
          <span className="reminder-icon" aria-hidden>
            <Icon name="bell-ring" size={15} />
          </span>
          <div>
            <p className="reminder-kicker">REMINDER</p>
            <p className="reminder-title">{task.title}</p>
          </div>
          <button type="button" className="reminder-close" aria-label="Dismiss reminder"
            onClick={() => setReminders((prev) => prev.filter((r) => r.id !== id))}>
            ×
          </button>
        </div>
      ))}
    </>
  );
}
