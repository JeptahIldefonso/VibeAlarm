import { useEffect, useState, type CSSProperties } from 'react';
import { request } from '../bridge/client';
import type { AccentOption, AppSettings } from '../bridge/protocol';
import { Icon } from '../components/Icon';

/**
 * The Settings view: the 8-preset accent library (a click re-publishes the CSS
 * variables app-wide instantly and persists), behavior toggles, run-at-startup,
 * and the danger zone. Every change goes through updateSettings — the host
 * normalizes, saves, and pushes settingsChanged back.
 */
export function SettingsView({
  settings,
  accents,
  startupEnabled,
}: {
  settings: AppSettings | null;
  accents: AccentOption[];
  startupEnabled: boolean;
}) {
  const [startup, setStartup] = useState(startupEnabled);
  const [saving, setSaving] = useState(false);

  useEffect(() => setStartup(startupEnabled), [startupEnabled]);

  if (settings == null) {
    return (
      <div className="view view-center">
        <div className="empty-state">
          <p className="empty-title">Loading settings…</p>
        </div>
      </div>
    );
  }

  const patch = async (changes: Partial<AppSettings>) => {
    setSaving(true);
    try {
      await request('updateSettings', { ...settings, ...changes });
    } finally {
      setSaving(false);
    }
  };

  const toggleStartup = async (enabled: boolean) => {
    setStartup(enabled); // optimistic — registry write is instant
    try {
      const result = await request('setStartupEnabled', { enabled });
      setStartup(result);
    } catch {
      setStartup(!enabled);
    }
  };

  const deleteAll = async () => {
    if (!window.confirm('Delete ALL tasks permanently? This cannot be undone.')) return;
    try {
      await request('deleteAllTasks');
    } catch {
      // The push never confirms; the tasks list disappearing is the feedback.
    }
  };

  return (
    <div className="view view-center">
      <section aria-label="Accent color">
        <h3 className="section-label">ACCENT COLOR</h3>
        <div className="accent-list">
          {accents.map((accent) => (
            <button
              key={accent.key}
              type="button"
              className={`accent-row${accent.key === settings.accentColor ? ' selected' : ''}`}
              style={{ '--row-accent': accent.base } as CSSProperties}
              onClick={() => patch({ accentColor: accent.key })}
              aria-pressed={accent.key === settings.accentColor}
            >
              <span className="accent-swatch" style={{ background: accent.base }} aria-hidden />
              <span className="accent-row-name">{accent.key}</span>
              {accent.key === settings.accentColor && (
                <span className="accent-check" aria-hidden>
                  <Icon name="check" size={14} />
                </span>
              )}
            </button>
          ))}
        </div>
      </section>

      <section aria-label="Behavior">
        <h3 className="section-label">BEHAVIOR</h3>
        <div className="settings-card">
          <ToggleRow
            label="Alarm sound"
            hint="Play the looping alarm tone when an alarm fires."
            checked={settings.enableAlarmSound}
            disabled={saving}
            onChange={(v) => patch({ enableAlarmSound: v })}
          />
          <ToggleRow
            label="Notifications"
            hint="Show in-app reminders and OS toasts."
            checked={settings.enableNotifications}
            disabled={saving}
            onChange={(v) => patch({ enableNotifications: v })}
          />
          <ToggleRow
            label="Confirm before delete"
            hint="Ask before deleting a task."
            checked={settings.confirmBeforeDelete}
            disabled={saving}
            onChange={(v) => patch({ confirmBeforeDelete: v })}
          />
          <ToggleRow
            label="Minimize to tray on close"
            hint="The close button hides to the tray instead of exiting."
            checked={settings.minimizeToTray}
            disabled={saving}
            onChange={(v) => patch({ minimizeToTray: v })}
          />
          <ToggleRow
            label="Run at Windows startup"
            hint="Start VibeAlarm automatically when you sign in."
            checked={startup}
            disabled={saving}
            onChange={toggleStartup}
          />
        </div>
      </section>

      <section aria-label="Danger zone">
        <h3 className="section-label">DANGER ZONE</h3>
        <div className="settings-card danger-zone">
          <div>
            <p className="danger-title">Delete all tasks</p>
            <p className="muted danger-hint">Permanently removes every task and its scheduled alarms.</p>
          </div>
          <button type="button" className="btn-danger danger-btn" onClick={deleteAll}>
            Delete all tasks
          </button>
        </div>
      </section>
    </div>
  );
}

function ToggleRow({
  label,
  hint,
  checked,
  disabled,
  onChange,
}: {
  label: string;
  hint: string;
  checked: boolean;
  disabled: boolean;
  onChange: (value: boolean) => void;
}) {
  return (
    <label className={`toggle-row${disabled ? ' disabled' : ''}`}>
      <span className="toggle-text">
        <span className="toggle-label">{label}</span>
        <span className="toggle-hint">{hint}</span>
      </span>
      <button
        type="button"
        role="switch"
        aria-checked={checked}
        aria-label={label}
        className={`toggle-switch${checked ? ' on' : ''}`}
        disabled={disabled}
        onClick={() => onChange(!checked)}
      >
        <span className="toggle-knob" aria-hidden />
      </button>
    </label>
  );
}
