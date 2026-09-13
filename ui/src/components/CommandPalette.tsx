import { useEffect, useMemo, useRef, useState } from 'react';
import type { TaskItem } from '../bridge/protocol';
import { whenLabel } from '../lib/tasks';

export interface Command {
  id: string;
  label: string;
  hint?: string;
  run: () => void;
}

/**
 * The Ctrl+K command palette: quick actions plus task search-and-jump.
 * Purely presentational — commands are closures supplied by the app shell.
 */
export function CommandPalette({
  open,
  onClose,
  commands,
  tasks,
  now,
  onOpenTask,
}: {
  open: boolean;
  onClose: () => void;
  commands: Command[];
  tasks: TaskItem[];
  now: Date;
  onOpenTask: (task: TaskItem) => void;
}) {
  const [query, setQuery] = useState('');
  const [cursor, setCursor] = useState(0);
  const inputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    if (open) {
      setQuery('');
      setCursor(0);
      inputRef.current?.focus();
    }
  }, [open]);

  const matches = useMemo(() => {
    const needle = query.trim().toLowerCase();
    const commandHits = (needle
      ? commands.filter((c) => c.label.toLowerCase().includes(needle))
      : commands
    ).map((c) => ({ kind: 'command' as const, command: c }));
    const taskHits = (needle
      ? tasks.filter((t) => t.title.toLowerCase().includes(needle))
      : []
    )
      .slice(0, 6)
      .map((t) => ({ kind: 'task' as const, task: t }));
    return [...commandHits, ...taskHits];
  }, [commands, tasks, query]);

  useEffect(() => setCursor(0), [query]);

  if (!open) return null;

  const activate = (index: number) => {
    const match = matches[index];
    if (match == null) return;
    onClose();
    if (match.kind === 'command') match.command.run();
    else onOpenTask(match.task);
  };

  return (
    <div
      className="palette-backdrop"
      onMouseDown={onClose}
      role="dialog"
      aria-label="Command palette"
    >
      <div className="palette" onMouseDown={(e) => e.stopPropagation()}>
        <input
          ref={inputRef}
          className="palette-input"
          type="text"
          placeholder="Type a command or search tasks…"
          value={query}
          aria-label="Command palette search"
          onChange={(e) => setQuery(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === 'ArrowDown') {
              e.preventDefault();
              setCursor((c) => Math.min(c + 1, matches.length - 1));
            } else if (e.key === 'ArrowUp') {
              e.preventDefault();
              setCursor((c) => Math.max(c - 1, 0));
            } else if (e.key === 'Enter') {
              e.preventDefault();
              activate(cursor);
            } else if (e.key === 'Escape') {
              e.preventDefault();
              onClose();
            }
          }}
        />
        {matches.length === 0 ? (
          <p className="muted palette-empty">No matches.</p>
        ) : (
          <div className="palette-list" role="listbox">
            {matches.map((match, i) => (
              <button
                key={match.kind === 'command' ? `c-${match.command.id}` : `t-${match.task.id}`}
                type="button"
                role="option"
                aria-selected={i === cursor}
                className={`palette-item${i === cursor ? ' cursor' : ''}`}
                onMouseEnter={() => setCursor(i)}
                onClick={() => activate(i)}
              >
                {match.kind === 'command' ? (
                  <>
                    <span className="palette-item-icon" aria-hidden>⌘</span>
                    <span className="palette-item-label">{match.command.label}</span>
                    {match.command.hint != null && (
                      <span className="palette-item-hint">{match.command.hint}</span>
                    )}
                  </>
                ) : (
                  <>
                    <span className="palette-item-icon" aria-hidden>⏰</span>
                    <span className="palette-item-label">{match.task.title}</span>
                    <span className="palette-item-hint">{whenLabel(match.task, now)}</span>
                  </>
                )}
              </button>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}
