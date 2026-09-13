import { useEffect, useRef, useState } from 'react';
import { request } from '../bridge/client';
import type { NoteItem } from '../bridge/protocol';
import { Icon } from '../components/Icon';

/**
 * The Notes view: a master-detail editor over the host's per-note .txt files.
 * The left rail lists note titles (most recently updated first — the host sorts);
 * the right side is a title field plus a freely scrolling content area. Edits
 * autosave (debounced) through the bridge — nothing is written from React.
 */
export function NotesView({
  notes,
  confirmBeforeDelete,
}: {
  notes: NoteItem[];
  confirmBeforeDelete: boolean;
}) {
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [title, setTitle] = useState('');
  const [content, setContent] = useState('');
  const [status, setStatus] = useState<'saved' | 'dirty' | 'saving'>('saved');

  const notesRef = useRef(notes);
  notesRef.current = notes;
  // Edits not yet flushed to the host (the debounced autosave's source of truth).
  const pending = useRef<{ id: string; title: string; content: string } | null>(null);
  // Set when the editor was just filled from a createNote response — skips the
  // selection-load effect for one pass so the push (which may still be in flight)
  // can't blank the freshly created note.
  const skipLoad = useRef(false);

  // Keep a valid selection: first note once the list hydrates, the next note when
  // the selected one disappears (deleted), null when the list empties.
  useEffect(() => {
    setSelectedId((current) => {
      if (notes.length === 0) return null;
      return current != null && notes.some((n) => n.id === current) ? current : notes[0].id;
    });
  }, [notes]);

  // Load the selected note into the editor ONLY when the selection itself changes
  // — notesChanged pushes must never clobber in-progress typing.
  useEffect(() => {
    if (skipLoad.current) {
      skipLoad.current = false;
      return;
    }
    const note = notesRef.current.find((n) => n.id === selectedId);
    setTitle(note?.title ?? '');
    setContent(note?.content ?? '');
    setStatus('saved');
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedId]);

  const saveNow = () => {
    const p = pending.current;
    pending.current = null;
    if (p == null) return;
    setStatus('saving');
    request('updateNote', p)
      .then(() => setStatus('saved'))
      .catch(() => setStatus('dirty')); // back to dirty so the debounce retries
  };
  const saveNowRef = useRef(saveNow);
  saveNowRef.current = saveNow;

  // Debounced autosave — ~600ms after the last keystroke.
  useEffect(() => {
    if (status !== 'dirty' || selectedId == null) return;
    const timer = setTimeout(saveNowRef.current, 600);
    return () => clearTimeout(timer);
  }, [status, selectedId, title, content]);

  // Flush unsaved edits when the view goes away (switching views / app exit).
  useEffect(() => () => saveNowRef.current(), []);

  const edit = (nextTitle: string, nextContent: string) => {
    setTitle(nextTitle);
    setContent(nextContent);
    if (selectedId != null) {
      pending.current = { id: selectedId, title: nextTitle, content: nextContent };
    }
    setStatus('dirty');
  };

  const select = (id: string) => {
    if (id === selectedId) return;
    saveNow(); // don't lose the edits of the note being left
    setSelectedId(id);
  };

  const create = async () => {
    saveNow();
    try {
      const note = await request('createNote', { title: 'Untitled' });
      skipLoad.current = true;
      setSelectedId(note.id);
      setTitle(note.title);
      setContent(note.content);
      setStatus('saved');
    } catch {
      /* the push (if any) will re-sync the list */
    }
  };

  const remove = async () => {
    if (selectedId == null) return;
    const note = notesRef.current.find((n) => n.id === selectedId);
    const confirmed =
      !confirmBeforeDelete || window.confirm(`Delete "${note?.title || 'this note'}" permanently?`);
    if (!confirmed) return;
    pending.current = null; // never flush edits for a note that's being deleted
    setStatus('saved');
    try {
      await request('deleteNote', { id: selectedId });
    } catch {
      /* the push (if any) will re-sync the list */
    }
  };

  const selected = notes.find((n) => n.id === selectedId);

  return (
    <div className="view view-wide notes-view">
      <div className="notes-toolbar">
        <h2 className="section-label">
          Notes <span className="section-count">{notes.length}</span>
        </h2>
        <button type="button" className="btn-primary" onClick={create}>
          <Icon name="sticky-note" size={15} /> New note
        </button>
      </div>

      {notes.length === 0 ? (
        <div className="empty-state">
          <p className="empty-title">No notes yet.</p>
          <button type="button" className="btn-ghost" onClick={create}>
            Create your first note
          </button>
        </div>
      ) : (
        <div className="notes-layout">
          <aside className="notes-list" aria-label="Notes">
            {notes.map((note) => (
              <button
                key={note.id}
                type="button"
                className={`notes-item${note.id === selectedId ? ' active' : ''}`}
                onClick={() => select(note.id)}
              >
                <span className="notes-item-title">{note.title || 'Untitled'}</span>
                <span className="notes-item-date">{formatUpdated(note.updatedAt)}</span>
              </button>
            ))}
          </aside>

          {selected != null && (
            <section className="notes-editor" aria-label="Note editor">
              <div className="notes-editor-head">
                <input
                  className="notes-title-input"
                  type="text"
                  value={title}
                  placeholder="Title"
                  aria-label="Note title"
                  onChange={(e) => edit(e.target.value, content)}
                />
                <span className="notes-status" data-status={status}>
                  {status === 'dirty' ? 'Editing…' : status === 'saving' ? 'Saving…' : 'Saved'}
                </span>
                <button
                  type="button"
                  className="icon-btn"
                  aria-label="Delete note"
                  onClick={remove}
                >
                  <Icon name="trash-2" size={16} />
                </button>
              </div>
              <textarea
                className="notes-content"
                value={content}
                placeholder="Start writing…"
                aria-label="Note content"
                spellCheck={false}
                onChange={(e) => edit(title, e.target.value)}
              />
            </section>
          )}
        </div>
      )}
    </div>
  );
}

function formatUpdated(updatedAt: string): string {
  const date = new Date(updatedAt);
  return Number.isNaN(date.getTime())
    ? ''
    : date.toLocaleString([], { month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' });
}
