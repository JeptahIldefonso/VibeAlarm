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

  const [previewing, setPreviewing] = useState(false);
  const [customFileError, setCustomFileError] = useState<string | null>(null);

  const togglePreview = async () => {
    if (previewing) {
      await request('stopAlarmPreview');
      setPreviewing(false);
    } else {
      const res = await request('previewAlarmSound', {
        sound: settings.alarmSound ?? 'Default',
        customPath: settings.customAlarmSoundPath,
        volume: settings.alarmVolume ?? 80,
      });
      if (res.success) {
        setPreviewing(true);
        window.setTimeout(() => setPreviewing(false), 5100);
      }
    }
  };

  const triggerTestAlarm = async () => {
    await request('testAlarm');
  };

  const chooseCustomFile = async () => {
    setCustomFileError(null);
    try {
      const res = await request('selectCustomAlarmSound');
      if (res.error) {
        setCustomFileError(res.error);
      }
    } catch (err: unknown) {
      setCustomFileError(err instanceof Error ? err.message : 'Failed to choose file');
    }
  };

  const resetAlarmSettings = async () => {
    setSaving(true);
    setPreviewing(false);
    setCustomFileError(null);
    try {
      await request('resetAlarmSettings');
    } finally {
      setSaving(false);
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
            hint="The close button hides to the tray instead of exiting (alarms continue to ring)."
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
          {startup && (
            <ToggleRow
              label="Start with Windows, hidden"
              hint="Launch minimized in the system tray when Windows boots."
              checked={settings.startWithWindowsHidden ?? true}
              disabled={saving}
              onChange={(v) => patch({ startWithWindowsHidden: v })}
            />
          )}
        </div>
      </section>

      <section aria-label="Alarm settings">
        <div className="settings-header-row">
          <h3 className="section-label" style={{ margin: 0 }}>ALARM</h3>
          <button
            type="button"
            className="btn-ghost"
            style={{ fontSize: '12px', padding: '4px 10px' }}
            disabled={saving}
            onClick={resetAlarmSettings}
          >
            Reset to default
          </button>
        </div>
        <div className="settings-card">
          <div className="settings-row">
            <span className="toggle-text">
              <span className="toggle-label">Alarm sound</span>
              <span className="toggle-hint">
                {settings.alarmSound === 'Custom'
                  ? 'Custom sound file (copied safely to app data; Windows default chime if closed).'
                  : 'Choose a synthesized tone or the classic alarm sound.'}
              </span>
              {settings.alarmSound === 'Custom' && (
                <div className="alarm-sound-custom-box">
                  <span className="alarm-sound-file-tag">
                    {settings.customAlarmSoundName || 'No file chosen'}
                  </span>
                  <button
                    type="button"
                    className="btn-ghost"
                    style={{ fontSize: '11.5px', padding: '3px 8px' }}
                    disabled={saving}
                    onClick={chooseCustomFile}
                  >
                    Choose file…
                  </button>
                </div>
              )}
              {customFileError && (
                <span style={{ fontSize: '11.5px', color: 'var(--danger, #ff5555)', marginTop: '4px' }}>
                  {customFileError}
                </span>
              )}
            </span>
            <div className="alarm-btn-group">
              <select
                className="settings-select"
                value={settings.alarmSound ?? 'Default'}
                disabled={saving}
                onChange={(e) => {
                  const val = e.target.value;
                  if (val === 'Custom' && !settings.customAlarmSoundPath) {
                    patch({ alarmSound: 'Custom' });
                    chooseCustomFile();
                  } else {
                    patch({ alarmSound: val });
                  }
                }}
              >
                <option value="Default">Default (Classic)</option>
                <option value="Classic Beep">Classic Beep</option>
                <option value="Soft Chime">Soft Chime</option>
                <option value="Digital Pulse">Digital Pulse</option>
                <option value="Rising Tone">Rising Tone</option>
                <option value="Custom">Custom file…</option>
              </select>
            </div>
          </div>

          <div className="settings-row">
            <span className="toggle-text">
              <span className="toggle-label">Test & Preview</span>
              <span className="toggle-hint">Audition chosen sound or simulate an incoming alarm alert.</span>
            </span>
            <div className="alarm-btn-group">
              <button
                type="button"
                className="btn-ghost"
                style={{ fontSize: '12px', padding: '5px 10px' }}
                disabled={saving}
                onClick={togglePreview}
              >
                {previewing ? 'Stop preview' : 'Preview sound'}
              </button>
              <button
                type="button"
                className="btn-ghost"
                style={{ fontSize: '12px', padding: '5px 10px' }}
                disabled={saving}
                onClick={triggerTestAlarm}
              >
                Test alarm
              </button>
            </div>
          </div>

          <div className="settings-row">
            <span className="toggle-text">
              <span className="toggle-label">Alarm volume</span>
              <span className="toggle-hint">Dedicated alarm level (minimum 10% to prevent accidental silence).</span>
            </span>
            <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
              <input
                type="range"
                className="alarm-volume-slider"
                min={10}
                max={100}
                value={settings.alarmVolume ?? 80}
                disabled={saving}
                onChange={(e) => patch({ alarmVolume: Number(e.target.value) })}
              />
              <span className="alarm-volume-val">{settings.alarmVolume ?? 80}%</span>
            </div>
          </div>

          <ToggleRow
            label="Gradual volume increase"
            hint="Gently ramp up alarm volume over 30 seconds."
            checked={settings.gradualVolumeIncrease ?? false}
            disabled={saving}
            onChange={(v) => patch({ gradualVolumeIncrease: v })}
          />

          <div className="settings-row">
            <span className="toggle-text">
              <span className="toggle-label">Ring duration</span>
              <span className="toggle-hint">How long the alarm rings before stopping automatically.</span>
            </span>
            <select
              className="settings-select"
              value={settings.alarmDurationMinutes ?? 5}
              disabled={saving}
              onChange={(e) => patch({ alarmDurationMinutes: Number(e.target.value) })}
            >
              <option value={1}>1 minute</option>
              <option value={2}>2 minutes</option>
              <option value={5}>5 minutes (default)</option>
              <option value={10}>10 minutes</option>
              <option value={0}>Ring until dismissed</option>
            </select>
          </div>

          <div className="settings-row">
            <span className="toggle-text">
              <span className="toggle-label">Snooze length</span>
              <span className="toggle-hint">Minutes added when clicking Snooze on an alarm.</span>
            </span>
            <select
              className="settings-select"
              value={settings.snoozeDurationMinutes ?? 9}
              disabled={saving}
              onChange={(e) => patch({ snoozeDurationMinutes: Number(e.target.value) })}
            >
              <option value={5}>5 minutes</option>
              <option value={9}>9 minutes (default)</option>
              <option value={10}>10 minutes</option>
              <option value={15}>15 minutes</option>
            </select>
          </div>
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
