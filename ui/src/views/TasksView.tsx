import { useEffect, useMemo, useRef, useState } from 'react';
import { request } from '../bridge/client';
import type { TaskItem } from '../bridge/protocol';
import type { TaskModalState } from '../components/TaskModal';
import { isPast, isToday, isUpcoming, taskDateTime, whenLabel } from '../lib/tasks';

/**
 * The Tasks view: TODAY / UPCOMING / EARLIER lists with search, per-card actions
 * (complete, edit, duplicate, delete with confirmation), the create/edit modal
 * (app-level, opened via openModal), the accent FAB, and Ctrl+N. A port of the
 * WinForms Tasks screen — identical semantics, every mutation routed through
 * the IPC bridge.
 */
export function TasksView({
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
      if (e.ctrlKey && e.key.toLowerCase() === 'n') {
        e.preventDefault();
        openModal({ mode: 'create', task: null });
      }
      if (e.key === 'Escape') {
        setMenuFor(null);
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [openModal]);

  const filtered = useMemo(() => {
    const needle = search.trim().toLowerCase();
    const matches = needle
      ? tasks.filter((t) => t.title.toLowerCase().includes(needle))
      : tasks;
    return [...matches].sort((a, b) => {
      const da = taskDateTime(a)?.getTime() ?? Number.MAX_SAFE_INTEGER;
      const db = taskDateTime(b)?.getTime() ?? Number.MAX_SAFE_INTEGER;
      return da - db;
    });
  }, [tasks, search]);

  const today = filtered.filter((t) => isToday(t, now));
  const upcoming = filtered.filter((t) => isUpcoming(t, now));
  const earlier = filtered.filter((t) => isPast(t, now));

  const call = (fn: () => Promise<unknown>) => {
    void fn().catch(() => {});
  };

  const openCreate = () => openModal({ mode: 'create', task: null });

  return (
    <div className="view view-flow">
      <div className="view-toolbar">
        <input
          className="search-input"
          type="search"
          placeholder="Search tasks…"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          aria-label="Search tasks"
        />
        <button type="button" className="btn-primary" onClick={openCreate}>
          + New Task
        </button>
      </div>

      {filtered.length === 0 && (
        <div className="empty-state">
          <p className="empty-title">{search ? 'No tasks match your search.' : 'No tasks yet.'}</p>
          {!search && (
            <button type="button" className="btn-ghost" onClick={openCreate}>
              Create your first task
            </button>
          )}
        </div>
      )}

      <TaskSection
        label="TODAY"
        tasks={today}
        now={now}
        menuFor={menuFor}
        setMenuFor={setMenuFor}
        confirmBeforeDelete={confirmBeforeDelete}
        onEdit={(task) => openModal({ mode: 'edit', task })}
        call={call}
      />
      <TaskSection
        label="UPCOMING"
        tasks={upcoming}
        now={now}
        menuFor={menuFor}
        setMenuFor={setMenuFor}
        confirmBeforeDelete={confirmBeforeDelete}
        onEdit={(task) => openModal({ mode: 'edit', task })}
        call={call}
      />
      <TaskSection
        label="EARLIER"
        tasks={earlier}
        now={now}
        menuFor={menuFor}
        setMenuFor={setMenuFor}
        confirmBeforeDelete={confirmBeforeDelete}
        onEdit={(task) => openModal({ mode: 'edit', task })}
        call={call}
      />

      <button type="button" className="fab" aria-label="New task" onClick={openCreate}>
        +
      </button>
    </div>
  );
}

function TaskSection({
  label,
  tasks,
  now,
  menuFor,
  setMenuFor,
  confirmBeforeDelete,
  onEdit,
  call,
}: {
  label: string;
  tasks: TaskItem[];
  now: Date;
  menuFor: string | null;
  setMenuFor: (id: string | null) => void;
  confirmBeforeDelete: boolean;
  onEdit: (task: TaskItem) => void;

  call: (fn: () => Promise<unknown>) => void;
}) {
  if (tasks.length === 0) return null;
  return (
    <section className="task-section" aria-label={label}>
      <h3 className="section-label">
        {label} <span className="section-count">{tasks.length}</span>
      </h3>
      <div className="task-list">
        {tasks.map((task) => (
          <TaskCard
            key={task.id}
            task={task}
            now={now}
            menuOpen={menuFor === task.id}
            setMenuFor={setMenuFor}
            confirmBeforeDelete={confirmBeforeDelete}
            onEdit={onEdit}
            call={call}
          />
        ))}
      </div>
    </section>
  );
}

function TaskCard({
  task,
  now,
  menuOpen,
  setMenuFor,
  confirmBeforeDelete,
  onEdit,
  call,
}: {
  task: TaskItem;
  now: Date;
  menuOpen: boolean;
  setMenuFor: (id: string | null) => void;
  confirmBeforeDelete: boolean;
  onEdit: (task: TaskItem) => void;

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
      !confirmBeforeDelete ||
      window.confirm(`Delete "${task.title}" permanently?`);
    if (confirmed) {
      call(() => request('deleteTask', { id: task.id }));
    }
  };

  return (
    <article
      className={`task-card${task.completed ? ' completed' : ''}${menuOpen ? ' menu-open' : ''}`}
      onDoubleClick={() => onEdit(task)}
    >
      <button
        type="button"
        className={`task-check${task.completed ? ' checked' : ''}`}
        aria-label={task.completed ? 'Mark as not done' : 'Complete task'}
        aria-checked={task.completed}
        role="checkbox"
        onClick={() => call(() => request('toggleComplete', { id: task.id }))}
      >
        {task.completed && <span className="task-check-mark">✓</span>}
      </button>

      <div className="task-card-main">
        <h4 className={task.completed ? 'task-title done' : 'task-title'}>{task.title}</h4>
        <p className="task-meta">
          <span className="task-type">{task.type}</span>
          <span> · {whenLabel(task, now)}</span>
        </p>
      </div>

      <div className="task-card-actions" ref={menuRef}>
        <button
          type="button"
          className="icon-btn"
          aria-label="Task actions"
          onClick={() => setMenuFor(menuOpen ? null : task.id)}
        >
          ⋯
        </button>
        {menuOpen && (
          <div className="context-menu" role="menu">
            <button
              type="button"
              role="menuitem"
              onClick={() => {
                setMenuFor(null);
                call(() => request('toggleComplete', { id: task.id }));
              }}
            >
              {task.completed ? 'Mark as not done' : 'Complete task'}
            </button>
            <button
              type="button"
              role="menuitem"
              onClick={() => {
                setMenuFor(null);
                onEdit(task);
              }}
            >
              Edit task
            </button>
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
