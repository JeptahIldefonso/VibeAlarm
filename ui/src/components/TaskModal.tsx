import { useEffect, useRef, useState } from 'react';
import { request } from '../bridge/client';
import type { TaskItem, TaskType } from '../bridge/protocol';
import { toStoredDate, toStoredTime } from '../lib/tasks';

const TYPES: TaskType[] = ['Alarm', 'Important', 'Notification'];

export interface TaskModalState {
  mode: 'create' | 'edit';
  task: TaskItem | null;
  /** Create-prefill: the calendar's selected day as yyyy-MM-dd. */
  presetDate?: string;
}

/**
 * The create/edit task dialog: title, date, time, type — the same fields the
 * WinForms TaskCreateDialog owned. The host validates the schedule is in the
 * future; its error is surfaced inline.
 */
export function TaskModal({ state, onClose }: { state: TaskModalState; onClose: () => void }) {
  const editing = state.task;
  const [title, setTitle] = useState(editing?.title ?? '');
  const [date, setDate] = useState(editing?.scheduledDate ?? state.presetDate ?? toStoredDate(new Date()));
  const [time, setTime] = useState(toTimeInput(editing?.remindTime ?? defaultTimeInput()));
  const [type, setType] = useState<TaskType>((editing?.type as TaskType) ?? 'Alarm');
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const titleRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    titleRef.current?.focus();
  }, []);

  const submit = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!title.trim() || saving) return;
    setSaving(true);
    setError(null);
    try {
      const parsed = parseTimeInput(time);
      const scheduledDate = date; // yyyy-MM-dd from the date input, the stored format
      const remindTime = toStoredTime(new Date(`${date}T${parsed}:00`));
      const dayName = new Date(`${date}T00:00:00`).toLocaleDateString(undefined, { weekday: 'long' });
      if (state.mode === 'create') {
        await request('createTask', { title: title.trim(), scheduledDate, remindTime, day: dayName, type });
      } else if (editing != null) {
        await request('updateTask', { id: editing.id, title: title.trim(), scheduledDate, remindTime, day: dayName, type });
      }
      onClose();
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
      setSaving(false);
    }
  };

  return (
    <div className="modal-backdrop" onMouseDown={onClose}>
      <form
        className="modal-card"
        role="dialog"
        aria-label={state.mode === 'create' ? 'New task' : 'Edit task'}
        onMouseDown={(e) => e.stopPropagation()}
        onSubmit={submit}
      >
        <h2 className="modal-title">{state.mode === 'create' ? 'New Task' : 'Edit Task'}</h2>

        <label className="field">
          <span className="field-label">Title</span>
          <input
            ref={titleRef}
            className="field-input"
            value={title}
            onChange={(e) => setTitle(e.target.value)}
            placeholder="What should remind you?"
            maxLength={120}
            required
          />
        </label>

        <div className="field-row">
          <label className="field">
            <span className="field-label">Date</span>
            <input
              className="field-input"
              type="date"
              value={date}
              onChange={(e) => setDate(e.target.value)}
              required
            />
          </label>
          <label className="field">
            <span className="field-label">Time</span>
            <input
              className="field-input"
              type="time"
              value={time}
              onChange={(e) => setTime(e.target.value)}
              required
            />
          </label>
        </div>

        <div className="field-row">
          <label className="field">
            <span className="field-label">Type</span>
            <select className="field-input" value={type} onChange={(e) => setType(e.target.value as TaskType)}>
              {TYPES.map((t) => (
                <option key={t} value={t}>
                  {t}
                </option>
              ))}
            </select>
          </label>
        </div>

        {error != null && <p className="field-error" role="alert">{error}</p>}

        <div className="modal-actions">
          <button type="button" className="btn-ghost" onClick={onClose}>
            Cancel
          </button>
          <button type="submit" className="btn-primary" disabled={!title.trim() || saving}>
            {state.mode === 'create' ? 'Add Task' : 'Save Changes'}
          </button>
        </div>
      </form>
    </div>
  );
}

function defaultTimeInput(): string {
  const soon = new Date(Date.now() + 60 * 60 * 1000);
  return toStoredTime(soon);
}

function toTimeInput(stored: string): string {
  const match = /^(\d{1,2}):(\d{2})\s*(am|pm)?$/i.exec((stored ?? '').trim());
  if (!match) return '';
  let hours = Number(match[1]);
  const meridiem = match[3]?.toLowerCase();
  if (meridiem) {
    hours = hours % 12;
    if (meridiem === 'pm') hours += 12;
  }
  return `${String(hours).padStart(2, '0')}:${match[2]}`;
}

function parseTimeInput(value: string): string {
  return /^\d{2}:\d{2}$/.test(value) ? value : '09:00';
}
