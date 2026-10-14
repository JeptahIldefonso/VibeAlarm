import { useCallback, useEffect, useRef, useState } from 'react';
import { request } from '../bridge/client';
import type { NoteItem } from '../bridge/protocol';
import { Icon } from '../components/Icon';
import {
  htmlToMarkdown,
  markdownToHtml,
  sanitizePastedHtml,
} from '../lib/noteFormat';

/**
 * Windows 11 Notepad-style tabbed editor for VibeAlarm notes.
 *
 * Replaces bulky side cards with compact horizontal tabs across the top,
 * a clean formatting & menu bar, full-screen canvas, and an authentic bottom
 * status bar (line/col/char counts, UTF-8, Windows CRLF, and live save status).
 */
export function NotesView({
  notes,
  confirmBeforeDelete,
  initialSelectedId = null,
  isDetached = false,
}: {
  notes: NoteItem[];
  confirmBeforeDelete: boolean;
  initialSelectedId?: string | null;
  isDetached?: boolean;
}) {
  const [selectedId, setSelectedId] = useState<string | null>(initialSelectedId);
  const [title, setTitle] = useState('');
  const [status, setStatus] = useState<'saved' | 'dirty' | 'saving'>('saved');
  const [activeFormats, setActiveFormats] = useState({ bold: false, italic: false, bullet: false });
  const [canUndo, setCanUndo] = useState(false);
  const [canRedo, setCanRedo] = useState(false);
  const [editingTabId, setEditingTabId] = useState<string | null>(null);
  const [renameValue, setRenameValue] = useState('');
  const [cursorPos, setCursorPos] = useState({ line: 1, col: 1, chars: 0 });

  const notesRef = useRef(notes);
  useEffect(() => {
    notesRef.current = notes;
  }, [notes]);

  const contentRef = useRef<HTMLDivElement | null>(null);
  const tabsContainerRef = useRef<HTMLDivElement | null>(null);
  const currentContentRef = useRef<string>('');
  const lastLoadedId = useRef<string | null>(null);
  const pending = useRef<{ id: string; title: string; content: string } | null>(null);
  const skipLoad = useRef(false);

  // Update cursor position and character count for the Notepad status bar
  const updateCursorPos = useCallback(() => {
    const el = contentRef.current;
    if (!el) return;
    const text = el.textContent || '';
    const chars = text.length;
    const sel = window.getSelection();
    if (!sel || sel.rangeCount === 0 || !el.contains(sel.anchorNode)) {
      setCursorPos((prev) => ({ ...prev, chars }));
      return;
    }
    try {
      const range = sel.getRangeAt(0);
      const preRange = range.cloneRange();
      preRange.selectNodeContents(el);
      preRange.setEnd(range.startContainer, range.startOffset);
      const preText = preRange.toString();
      const lines = preText.split('\n');
      const line = lines.length;
      const col = (lines[lines.length - 1]?.length ?? 0) + 1;
      setCursorPos({ line, col, chars });
    } catch {
      setCursorPos((prev) => ({ ...prev, chars }));
    }
  }, []);

  // Update formatting indicators (bold, italic, list) and undo/redo capabilities
  const updateFormatState = useCallback(() => {
    try {
      const bold = document.queryCommandState('bold');
      const italic = document.queryCommandState('italic');
      const bullet = document.queryCommandState('insertUnorderedList');
      setActiveFormats({ bold, italic, bullet });

      const undo = document.queryCommandEnabled('undo');
      const redo = document.queryCommandEnabled('redo');
      setCanUndo(undo);
      setCanRedo(redo);
    } catch {
      /* outside document selection */
    }
    updateCursorPos();
  }, [updateCursorPos]);

  useEffect(() => {
    const onSelectionChange = () => {
      const el = contentRef.current;
      if (el != null && (document.activeElement === el || el.contains(document.activeElement))) {
        updateFormatState();
      }
    };
    document.addEventListener('selectionchange', onSelectionChange);
    return () => document.removeEventListener('selectionchange', onSelectionChange);
  }, [updateFormatState]);

  // Keep a valid selection: initial/selected note, first note when loaded, null when empty
  useEffect(() => {
    setSelectedId((current) => {
      if (notes.length === 0) return null;
      if (initialSelectedId && notes.some((n) => n.id === initialSelectedId)) {
        return initialSelectedId;
      }
      return current != null && notes.some((n) => n.id === current) ? current : notes[0].id;
    });
  }, [notes, initialSelectedId]);

  // Scroll active tab into view when selection changes
  useEffect(() => {
    if (selectedId == null || !tabsContainerRef.current) return;
    const tabEl = tabsContainerRef.current.querySelector<HTMLElement>(`[data-tab-id="${selectedId}"]`);
    if (tabEl) {
      tabEl.scrollIntoView({ behavior: 'smooth', block: 'nearest', inline: 'nearest' });
    }
  }, [selectedId]);

  // Autosave flush logic
  const saveNow = () => {
    const p = pending.current;
    pending.current = null;
    if (p == null) return;
    setStatus('saving');
    request('updateNote', p)
      .then(() => setStatus('saved'))
      .catch(() => setStatus('dirty'));
  };
  const saveNowRef = useRef(saveNow);
  useEffect(() => {
    saveNowRef.current = saveNow;
  });

  // Debounced autosave (~500ms after the last edit)
  useEffect(() => {
    if (status !== 'dirty' || selectedId == null) return;
    const timer = setTimeout(saveNowRef.current, 500);
    return () => clearTimeout(timer);
  }, [status, selectedId, title]);

  // Flush unsaved edits on unmount
  useEffect(() => () => saveNowRef.current(), []);

  // Sync editor content when selected note changes
  useEffect(() => {
    if (skipLoad.current) {
      skipLoad.current = false;
      return;
    }
    if (selectedId == null) {
      setTitle('');
      currentContentRef.current = '';
      lastLoadedId.current = null;
      if (contentRef.current) contentRef.current.innerHTML = '';
      setCursorPos({ line: 1, col: 1, chars: 0 });
      return;
    }

    const note = notesRef.current.find((n) => n.id === selectedId);
    if (!note) return;

    if (lastLoadedId.current !== selectedId) {
      lastLoadedId.current = selectedId;
      setTitle(note.title);
      currentContentRef.current = note.content;
      if (contentRef.current) {
        contentRef.current.innerHTML = markdownToHtml(note.content);
      }
      setStatus('saved');
      setCanUndo(false);
      setCanRedo(false);
      setEditingTabId(null);
      setTimeout(updateCursorPos, 50);
    } else if (status === 'saved' && note.content !== currentContentRef.current) {
      currentContentRef.current = note.content;
      if (contentRef.current) {
        contentRef.current.innerHTML = markdownToHtml(note.content);
      }
      setTimeout(updateCursorPos, 50);
    }
  }, [selectedId, notes, status, updateCursorPos]);

  const handleContentInput = () => {
    if (!contentRef.current || selectedId == null) return;
    const md = htmlToMarkdown(contentRef.current.innerHTML);
    currentContentRef.current = md;
    pending.current = { id: selectedId, title, content: md };
    setStatus('dirty');
    updateFormatState();
  };

  const handleTitleChange = (nextTitle: string) => {
    setTitle(nextTitle);
    if (selectedId != null) {
      pending.current = { id: selectedId, title: nextTitle, content: currentContentRef.current };
      setStatus('dirty');
    }
  };

  const startRenameTab = (note: NoteItem, e: React.MouseEvent) => {
    e.stopPropagation();
    setEditingTabId(note.id);
    setRenameValue(note.title || 'Untitled');
  };

  const finishRenameTab = () => {
    if (editingTabId == null) return;
    const cleanTitle = renameValue.trim() || 'Untitled';
    if (editingTabId === selectedId) {
      handleTitleChange(cleanTitle);
    } else {
      const target = notes.find((n) => n.id === editingTabId);
      if (target) {
        request('updateNote', { id: editingTabId, title: cleanTitle, content: target.content }).catch(() => {});
      }
    }
    setEditingTabId(null);
  };

  const runCommand = (command: string, value: string | undefined = undefined) => {
    const el = contentRef.current;
    if (el == null) return;
    el.focus();
    document.execCommand(command, false, value);
    handleContentInput();
  };

  const handleKeyDown = (event: React.KeyboardEvent<HTMLDivElement>) => {
    if (!(event.ctrlKey || event.metaKey) || event.altKey) return;
    const key = event.key.toLowerCase();
    if (key === 'b') {
      event.preventDefault();
      runCommand('bold');
    } else if (key === 'i') {
      event.preventDefault();
      runCommand('italic');
    } else if (key === 'z') {
      event.preventDefault();
      if (event.shiftKey) runCommand('redo');
      else runCommand('undo');
    } else if (key === 'y') {
      event.preventDefault();
      runCommand('redo');
    } else if (key === 's') {
      event.preventDefault();
      exportTxt();
    } else if (key === 'n') {
      event.preventDefault();
      create();
    } else if (key === 'w') {
      event.preventDefault();
      remove();
    }
  };

  const handlePaste = (event: React.ClipboardEvent<HTMLDivElement>) => {
    event.preventDefault();
    const html = event.clipboardData.getData('text/html');
    if (html) {
      const cleanHtml = sanitizePastedHtml(html);
      if (cleanHtml) {
        document.execCommand('insertHTML', false, cleanHtml);
        handleContentInput();
        return;
      }
    }
    const plain = event.clipboardData.getData('text/plain');
    if (plain) {
      document.execCommand('insertText', false, plain);
      handleContentInput();
    }
  };

  const select = (id: string) => {
    if (id === selectedId) return;
    saveNow();
    setSelectedId(id);
  };

  const create = async () => {
    saveNow();
    try {
      const note = await request('createNote', { title: 'Untitled' });
      skipLoad.current = true;
      setSelectedId(note.id);
      lastLoadedId.current = note.id;
      setTitle(note.title);
      currentContentRef.current = note.content;
      if (contentRef.current) {
        contentRef.current.innerHTML = markdownToHtml(note.content);
        contentRef.current.focus();
      }
      setStatus('saved');
      setCanUndo(false);
      setCanRedo(false);
      setCursorPos({ line: 1, col: 1, chars: 0 });
    } catch {
      /* host syncs on push */
    }
  };

  const removeNoteById = async (id: string, e?: React.MouseEvent) => {
    if (e) e.stopPropagation();
    const note = notesRef.current.find((n) => n.id === id);
    const confirmed =
      !confirmBeforeDelete || window.confirm(`Close "${note?.title || 'this note'}" and delete file?`);
    if (!confirmed) return;
    if (id === selectedId) {
      pending.current = null;
      setStatus('saved');
    }
    try {
      await request('deleteNote', { id });
    } catch {
      /* host syncs on push */
    }
  };

  const remove = async () => {
    if (selectedId != null) {
      await removeNoteById(selectedId);
    }
  };

  const exportTxt = async () => {
    saveNow();
    const md = currentContentRef.current;
    try {
      await request('exportNote', { title, content: md });
    } catch {
      if (typeof window !== 'undefined' && !window.chrome?.webview) {
        exportInBrowser(title, md);
      }
    }
  };

  const detach = async () => {
    if (selectedId == null) return;
    saveNow();
    try {
      await request('detachNote', { id: selectedId });
    } catch {
      if (typeof window !== 'undefined') {
        window.open(
          `${window.location.origin}${window.location.pathname}?detached=true&id=${encodeURIComponent(selectedId)}`,
          '_blank',
          'width=740,height=580',
        );
      }
    }
  };

  const scrollTabs = (direction: 'left' | 'right') => {
    if (!tabsContainerRef.current) return;
    tabsContainerRef.current.scrollBy({
      left: direction === 'left' ? -180 : 180,
      behavior: 'smooth',
    });
  };

  const selected = notes.find((n) => n.id === selectedId);

  return (
    <div className={`notepad-shell${isDetached ? ' detached-notepad' : ''}`}>
      {/* 1. Windows Notepad Tab Bar */}
      <div className="notepad-tabbar" role="tablist" aria-label="Open notes">
        <div className="notepad-tabs-scroll-wrapper">
          <button
            type="button"
            className="notepad-tab-scroll-btn"
            title="Scroll tabs left"
            aria-label="Scroll tabs left"
            onClick={() => scrollTabs('left')}
          >
            <Icon name="chevron-left" size={13} />
          </button>

          <div ref={tabsContainerRef} className="notepad-tabs-list">
            {notes.map((note) => {
              const isActive = note.id === selectedId;
              const isDirty = isActive && status === 'dirty';
              const isEditing = editingTabId === note.id;

              return (
                <div
                  key={note.id}
                  data-tab-id={note.id}
                  role="tab"
                  aria-selected={isActive}
                  className={`notepad-tab${isActive ? ' active' : ''}`}
                  onClick={() => select(note.id)}
                  onDoubleClick={(e) => startRenameTab(note, e)}
                  title={`${note.title || 'Untitled'} (Double-click to rename)`}
                >
                  {isEditing ? (
                    <input
                      // eslint-disable-next-line jsx-a11y/no-autofocus
                      autoFocus
                      className="notepad-tab-rename-input"
                      value={renameValue}
                      onChange={(e) => setRenameValue(e.target.value)}
                      onBlur={finishRenameTab}
                      onKeyDown={(e) => {
                        if (e.key === 'Enter') finishRenameTab();
                        if (e.key === 'Escape') setEditingTabId(null);
                      }}
                      onClick={(e) => e.stopPropagation()}
                    />
                  ) : (
                    <span className="notepad-tab-title">{note.title || 'Untitled'}</span>
                  )}

                  {isDirty ? (
                    <span className="notepad-tab-dot" title="Unsaved changes" aria-label="Unsaved" />
                  ) : (
                    <button
                      type="button"
                      className="notepad-tab-close"
                      title="Close note"
                      aria-label="Close note"
                      onClick={(e) => removeNoteById(note.id, e)}
                    >
                      <Icon name="x" size={11} />
                    </button>
                  )}
                </div>
              );
            })}
          </div>

          <button
            type="button"
            className="notepad-tab-scroll-btn"
            title="Scroll tabs right"
            aria-label="Scroll tabs right"
            onClick={() => scrollTabs('right')}
          >
            <Icon name="chevron-right" size={13} />
          </button>
        </div>

        <button
          type="button"
          className="notepad-add-tab-btn"
          title="New note (Ctrl+N)"
          aria-label="New note"
          onClick={create}
        >
          <Icon name="plus" size={14} />
        </button>
      </div>

      {/* 2. Notepad Menu & Formatting Toolbar */}
      <div className="notepad-menubar">
        <div className="notepad-menu-actions">
          <button
            type="button"
            className="notepad-menu-btn"
            title="Save note as .txt (Ctrl+S)"
            onClick={exportTxt}
            disabled={selected == null}
          >
            <Icon name="download" size={14} />
            <span>Save .txt</span>
          </button>

          {!isDetached && (
            <button
              type="button"
              className="notepad-menu-btn"
              title="Detach into separate window"
              onClick={detach}
              disabled={selected == null}
            >
              <Icon name="external-link" size={14} />
              <span>Pop out</span>
            </button>
          )}

          <button
            type="button"
            className="notepad-menu-btn text-danger"
            title="Delete note (Ctrl+W)"
            onClick={remove}
            disabled={selected == null}
          >
            <Icon name="trash-2" size={14} />
            <span>Delete</span>
          </button>
        </div>

        {selected != null && (
          <div className="notepad-format-group">
            <button
              type="button"
              className={`notepad-format-btn${activeFormats.bold ? ' active' : ''}`}
              title="Bold (Ctrl+B)"
              aria-label="Bold"
              onMouseDown={(e) => e.preventDefault()}
              onClick={() => runCommand('bold')}
            >
              <Icon name="bold" size={13} />
            </button>
            <button
              type="button"
              className={`notepad-format-btn${activeFormats.italic ? ' active' : ''}`}
              title="Italic (Ctrl+I)"
              aria-label="Italic"
              onMouseDown={(e) => e.preventDefault()}
              onClick={() => runCommand('italic')}
            >
              <Icon name="italic" size={13} />
            </button>
            <button
              type="button"
              className={`notepad-format-btn${activeFormats.bullet ? ' active' : ''}`}
              title="Bullet list"
              aria-label="Bullet list"
              onMouseDown={(e) => e.preventDefault()}
              onClick={() => runCommand('insertUnorderedList')}
            >
              <Icon name="list" size={13} />
            </button>

            <span className="notepad-vdivider" aria-hidden="true" />

            <button
              type="button"
              className="notepad-format-btn"
              title="Undo (Ctrl+Z)"
              aria-label="Undo"
              disabled={!canUndo}
              onMouseDown={(e) => e.preventDefault()}
              onClick={() => runCommand('undo')}
            >
              <Icon name="undo-2" size={13} />
            </button>
            <button
              type="button"
              className="notepad-format-btn"
              title="Redo (Ctrl+Y)"
              aria-label="Redo"
              disabled={!canRedo}
              onMouseDown={(e) => e.preventDefault()}
              onClick={() => runCommand('redo')}
            >
              <Icon name="redo-2" size={13} />
            </button>
          </div>
        )}

        <div className="notepad-meta-actions">
          {selected != null && (
            <input
              className="notepad-quick-title"
              type="text"
              value={title}
              placeholder="Untitled note"
              title="Note title"
              onChange={(e) => handleTitleChange(e.target.value)}
            />
          )}
          <span className="notepad-status-tag" data-status={status}>
            {status === 'dirty' ? 'Editing…' : status === 'saving' ? 'Saving…' : 'Saved'}
          </span>
        </div>
      </div>

      {/* 3. Canvas Writing Surface */}
      {notes.length === 0 ? (
        <div className="notepad-empty-state">
          <p className="notepad-empty-title">No notes open</p>
          <button type="button" className="btn-primary" onClick={create}>
            <Icon name="plus" size={15} /> Create note
          </button>
        </div>
      ) : (
        <div
          ref={contentRef}
          className="notepad-editor-canvas"
          contentEditable
          role="textbox"
          aria-multiline="true"
          data-placeholder="Start writing…"
          aria-label="Note content"
          spellCheck={false}
          onInput={handleContentInput}
          onKeyDown={handleKeyDown}
          onPaste={handlePaste}
          onKeyUp={updateFormatState}
          onMouseUp={updateFormatState}
        />
      )}

      {/* 4. Windows Notepad Status Bar */}
      <footer className="notepad-statusbar">
        <div className="notepad-status-left">
          <span>{`Ln ${cursorPos.line}, Col ${cursorPos.col}`}</span>
          <span className="notepad-status-separator">|</span>
          <span>{`${cursorPos.chars} characters`}</span>
        </div>
        <div className="notepad-status-center">
          <span>UTF-8</span>
          <span className="notepad-status-separator">|</span>
          <span>Plain text</span>
        </div>
        <div className="notepad-status-right">
          <span>100%</span>
          <span className="notepad-status-separator">|</span>
          <span>Windows (CRLF)</span>
          <span className="notepad-status-separator">|</span>
          <span className="notepad-status-indicator" data-status={status}>
            {status === 'dirty' ? '● Unsaved' : status === 'saving' ? 'Saving…' : 'Saved'}
          </span>
        </div>
      </footer>
    </div>
  );
}

async function exportInBrowser(noteTitle: string, noteContent: string): Promise<void> {
  const safeName = (noteTitle.trim() || 'Untitled').replace(/[\\/:*?"<>|]/g, '_') + '.txt';
  const textWithCrlf = noteContent.replace(/\r?\n/g, '\r\n');
  if ('showSaveFilePicker' in window) {
    try {
      const picker = (
        window as unknown as {
          showSaveFilePicker: (opts: unknown) => Promise<{
            createWritable: () => Promise<{ write: (data: string) => Promise<void>; close: () => Promise<void> }>;
          }>;
        }
      ).showSaveFilePicker;
      const handle = await picker({
        suggestedName: safeName,
        types: [{ description: 'Text documents (*.txt)', accept: { 'text/plain': ['.txt'] } }],
      });
      const writable = await handle.createWritable();
      await writable.write(textWithCrlf);
      await writable.close();
      return;
    } catch {
      return;
    }
  }
  const blob = new Blob([textWithCrlf], { type: 'text/plain;charset=utf-8' });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = safeName;
  a.click();
  URL.revokeObjectURL(url);
}
