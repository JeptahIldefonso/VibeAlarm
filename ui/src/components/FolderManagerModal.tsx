import { useState } from 'react';
import type { LibraryFolder } from '../bridge/protocol';
import { Icon } from './Icon';

interface FolderManagerModalProps {
  folders: LibraryFolder[];
  onClose: () => void;
  onAddFolder: () => Promise<void>;
  onRescanFolder: (path?: string) => Promise<void>;
  onRemoveFolder: (path: string) => Promise<void>;
}

export function FolderManagerModal({
  folders,
  onClose,
  onAddFolder,
  onRescanFolder,
  onRemoveFolder,
}: FolderManagerModalProps) {
  const [busy, setBusy] = useState<string | null>(null);
  const [confirmRemove, setConfirmRemove] = useState<string | null>(null);

  const handleAdd = async () => {
    setBusy('add');
    try {
      await onAddFolder();
    } finally {
      setBusy(null);
    }
  };

  const handleRescan = async (path: string) => {
    setBusy(`rescan-${path}`);
    try {
      await onRescanFolder(path);
    } finally {
      setBusy(null);
    }
  };

  const handleRemove = async (path: string) => {
    setBusy(`remove-${path}`);
    try {
      await onRemoveFolder(path);
      setConfirmRemove(null);
    } finally {
      setBusy(null);
    }
  };

  return (
    <div className="modal-backdrop" onClick={onClose} role="dialog" aria-modal="true" aria-label="Music folders">
      <div className="modal-card folder-manager-modal" onClick={(e) => e.stopPropagation()}>
        <div className="modal-header">
          <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            <span style={{ color: 'var(--text-secondary)' }}>
              <Icon name="folder" size={20} />
            </span>
            <h3 className="modal-title">Music folders</h3>
          </div>
          <button type="button" className="btn-icon" onClick={onClose} aria-label="Close">
            <Icon name="x" size={18} />
          </button>
        </div>

        <p className="muted" style={{ fontSize: 13, margin: '4px 0 16px' }}>
          Folders currently indexed in your local library. Removing a folder removes its tracks from the list, but never deletes or touches your actual files.
        </p>

        <div className="folder-list">
          {folders.length === 0 ? (
            <div className="folder-empty">
              <p className="muted">No music folders added yet.</p>
            </div>
          ) : (
            folders.map((f) => {
              const isRemoving = confirmRemove === f.path;
              const isBusy = busy === `rescan-${f.path}` || busy === `remove-${f.path}`;

              return (
                <div key={f.path} className={`folder-row${!f.reachable ? ' unreachable' : ''}`}>
                  <div className="folder-info">
                    <span className="folder-path" title={f.path}>
                      {f.path}
                    </span>
                    <div className="folder-meta">
                      <span>{f.trackCount} {f.trackCount === 1 ? 'song' : 'songs'}</span>
                      <span>•</span>
                      <span>
                        Last scanned {f.lastScannedAt ? new Date(f.lastScannedAt).toLocaleDateString() : 'recently'}
                      </span>
                      {!f.reachable && (
                        <span className="folder-warning">
                          (Folder not found or unreachable)
                        </span>
                      )}
                    </div>
                  </div>

                  <div className="folder-actions">
                    {isRemoving ? (
                      <div style={{ display: 'flex', gap: 6, alignItems: 'center' }}>
                        <span style={{ fontSize: 12, color: 'var(--danger)' }}>Remove?</span>
                        <button
                          type="button"
                          className="btn-ghost"
                          style={{ color: 'var(--danger)', padding: '4px 8px', fontSize: 12 }}
                          disabled={isBusy}
                          onClick={() => handleRemove(f.path)}
                        >
                          Yes
                        </button>
                        <button
                          type="button"
                          className="btn-ghost"
                          style={{ padding: '4px 8px', fontSize: 12 }}
                          onClick={() => setConfirmRemove(null)}
                        >
                          Cancel
                        </button>
                      </div>
                    ) : (
                      <>
                        <button
                          type="button"
                          className="btn-ghost"
                          style={{ padding: '4px 10px', fontSize: 12 }}
                          disabled={isBusy}
                          onClick={() => handleRescan(f.path)}
                          title="Rescan for new or modified songs"
                        >
                          <Icon name="refresh-cw" size={13} />
                          <span>Rescan</span>
                        </button>
                        <button
                          type="button"
                          className="btn-ghost"
                          style={{ padding: '4px 10px', fontSize: 12, color: 'var(--text-secondary)' }}
                          disabled={isBusy}
                          onClick={() => setConfirmRemove(f.path)}
                          title="Remove folder from library"
                        >
                          Remove
                        </button>
                      </>
                    )}
                  </div>
                </div>
              );
            })
          )}
        </div>

        <div className="modal-footer" style={{ marginTop: 20, display: 'flex', justifyContent: 'space-between' }}>
          <button
            type="button"
            className="btn-primary"
            disabled={busy === 'add'}
            onClick={handleAdd}
          >
            <Icon name="folder-plus" size={16} />
            <span>+ Add folder</span>
          </button>
          <button type="button" className="btn-ghost" onClick={onClose}>
            Done
          </button>
        </div>
      </div>
    </div>
  );
}
