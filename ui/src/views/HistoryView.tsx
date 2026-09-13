import { useEffect, useMemo, useRef, useState } from 'react';
import { request } from '../bridge/client';
import type { TaskItem } from '../bridge/protocol';
import { Icon } from '../components/Icon';
import type { TaskModalState } from '../components/TaskModal';
import { formatTime, taskDateTime, taskState, whenDayLabel } from '../lib/tasks';

/**
 * The History view: every terminal task, grouped under day headers ("Today",
 * "Yesterday", "Fri, Sep 25") with the most recent day first. Entries are
 * distinguished by outcome — "Completed" (fired / checked off normally) vs
 * "Missed" (Expired: the app wasn't running at fire time). Nothing is ever
 * deleted from storage to appear here; the host retains terminal tasks in
 * tasks.json precisely for this view.
 */
export function HistoryView({
  tasks,
  now,
  confirmBeforeDelete,
  openModal,
}: {
  tasks: TaskItem[];
  now: Date;
  confirmBeforeDelete: boolean;
  openModal: (state: TaskModalState) => void;
}) {
  const [search, setSearch] = useState('');
  const [menuFor, setMenuFor] = useState<string | null>(null);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setMenuFor(null);
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, []);

  const entries = useMemo(() => {
    const needle = search.trim().toLowerCase();
    return tasks
      .filter((t) => taskState(t) !== 'Scheduled')
      .filter((t) => !needle || t.title.toLowerCase().includes(needle))
      .sort(
        (a, b) => (taskDateTime(b)?.getTime() ?? 0) - (taskDateTime(a)?.getTime() ?? 0),
      );
  }, [tasks, search]);

  // Descending sort keeps each day's tasks contiguous, so grouping is a simple
  // run over the labels — and the groups themselves come out most-recent-first.
  const groups = useMemo(() => {
    const result: { label: string; tasks: TaskItem[] }[] = [];
    for (const task of entries) {
      const label = whenDayLabel(task, now);
      const last = result[result.length - 1];
      if (last != null && last.label === label) last.tasks.push(task);
      else result.push({ label, tasks: [task] });
    }
    return result;
  }, [entries, now]);

  const missedCount = entries.filter((t) => taskState(t) === 'Expired').length;

  const call = (fn: () => Promise<unknown>) => {
    void fn().catch(() => {});
  };

  return (
    <div className="view view-flow">
      <div className="view-toolbar">
        <div className="search-field">
          <Icon name="search-check" size={15} />
          <input
            className="search-input"
            type="search"
            placeholder="Search history…"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            aria-label="Search history"
          />
        </div>
        <p className="history-summary">
          {entries.length} {entries.length === 1 ? 'entry' : 'entries'}
          {missedCount > 0 && ` · ${missedCount} missed`}
        </p>
      </div>

      {entries.length === 0 && (
        <div className="empty-state">
          <p className="empty-title">
            {search ? 'No history matches your search.' : 'No history yet.'}
          </p>
          {!search && (
            <p className="muted">Completed and missed tasks will appear here, grouped by day.</p>
          )}
        </div>
      )}

      {groups.map((group) => (
        <section className="task-section" key={group.label} aria-label={group.label}>
          <h3 className="section-label">
            {group.label} <span className="section-count">{group.tasks.length}</span>
          </h3>
          <div className="task-list">
            {group.tasks.map((task) => (
              <HistoryEntry
                key={task.id}
                task={task}
                missed={taskState(task) === 'Expired'}
                menuOpen={menuFor === task.id}
                setMenuFor={setMenuFor}
                confirmBeforeDelete={confirmBeforeDelete}
                openModal={openModal}
                call={call}
              />
            ))}
          </div>
        </section>
      ))}
    </div>
  );
}

function HistoryEntry({
  task,
  missed,
  menuOpen,
  setMenuFor,
  confirmBeforeDelete,
  openModal,
  call,
}: {
  task: TaskItem;
  missed: boolean;
  menuOpen: boolean;
  setMenuFor: (id: string | null) => void;
  confirmBeforeDelete: boolean;
  openModal: (state: TaskModalState) => void;
  call: (fn: () => Promise<unknown>) => void;
}) {
  const menuRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!menuOpen) return;
    const onDown = (e: MouseEvent) => {
      if (menuRef.current != null && !menuRef.current.contains(e.target as Node)) {
        setMenuFor(null);
      }
    };
    window.addEventListener('mousedown', onDown);
    return () => window.removeEventListener('mousedown', onDown);
  }, [menuOpen, setMenuFor]);

  const deleteTask = () => {
    setMenuFor(null);
    const confirmed =
      !confirmBeforeDelete || window.confirm(`Delete "${task.title}" permanently?`);
    if (confirmed) {
      call(() => request('deleteTask', { id: task.id }));
    }
  };

  return (
    <article
      className={`history-entry${missed ? ' missed' : ''}${menuOpen ? ' menu-open' : ''}`}
      onDoubleClick={() => openModal({ mode: 'edit', task })}
    >
      <span className="history-status" aria-hidden>
        <Icon name={missed ? 'bell-off' : 'circle-check-big'} size={20} />
      </span>

      <div className="history-entry-main">
        <h4 className="history-title">{task.title}</h4>
        <p className="task-meta">
          <span className="task-type">{task.type}</span>
          <span> · {formatTime(task.remindTime)}</span>
        </p>
      </div>

      <span className={`history-badge ${missed ? 'missed' : 'done'}`}>
        {missed ? 'Missed' : 'Completed'}
      </span>

      <div className="task-card-actions" ref={menuRef}>
        <button
          type="button"
          className="icon-btn"
          aria-label="Entry actions"
          onClick={() => setMenuFor(menuOpen ? null : task.id)}
        >
          <Icon name="circle-ellipsis" size={16} />
        </button>
        {menuOpen && (
          <div className="context-menu" role="menu">
            {missed ? (
              <button
                type="button"
                role="menuitem"
                onClick={() => {
                  setMenuFor(null);
                  openModal({ mode: 'edit', task });
                }}
              >
                Re-schedule task
              </button>
            ) : (
              <>
                <button
                  type="button"
                  role="menuitem"
                  onClick={() => {
                    setMenuFor(null);
                    call(() => request('toggleComplete', { id: task.id }));
                  }}
                >
                  Restore to active
                </button>
                <button
                  type="button"
                  role="menuitem"
                  onClick={() => {
                    setMenuFor(null);
                    openModal({ mode: 'edit', task });
                  }}
                >
                  Edit task
                </button>
              </>
            )}
            <button
              type="button"
              role="menuitem"
              onClick={() => {
                setMenuFor(null);
                call(() =>
                  request('createTask', {
                    title: `${task.title} (Copy)`,
                    scheduledDate: task.scheduledDate,
                    remindTime: task.remindTime,
                    day: task.day,
                    type: task.type,
                  }),
                );
              }}
            >
              Duplicate task
            </button>
            <div className="context-menu-separator" />
            <button type="button" role="menuitem" className="danger" onClick={deleteTask}>
              Delete task
            </button>
          </div>
        )}
      </div>
    </article>
  );
}
