/**
 * The wire contract between the React SPA and the WinForms host, mirroring
 * Bridge/IpcMessages.cs on the C# side (camelCase property names both ways).
 *
 * Every message is one JSON object posted through WebView2's
 * window.chrome.webview.postMessage / PostWebMessageAsJson channel,
 * discriminated by `kind`:
 *
 *   • request  — { kind: "req",  id, type, payload }        (React → host)
 *   • response — { kind: "res",  id, ok, payload | error }  (host → React)
 *   • push     — { kind: "push", type, payload }            (host → React, unsolicited)
 */

// ---- Domain models (serialized forms of the C# types) ----

export type TaskType = 'Alarm' | 'Important' | 'Notification';

/** tasks.json record. `scheduledDate` is "yyyy-MM-dd"; `remindTime` is "hh:mm tt"
 *  (dialog) or "HH:mm" (post-snooze) — both parse with the same rules as C#
 *  DateTime.TryParse. `state` is a TaskState name; `completed` is kept in sync
 *  by the host. */
export interface TaskItem {
  id: string;
  title: string;
  scheduledDate: string;
  remindTime: string;
  day: string;
  type: string;
  completed: boolean;
  state: string;
}

/** settings.json record (AppSettings + the Appearance partial, flattened). */
export interface AppSettings {
  schemaVersion: number;
  accentColor: string;
  minimizeToTray: boolean;
  lastActiveView: string;
  lastViewedCalendarMonth: string;
  enableNotifications: boolean;
  enableAlarmSound: boolean;
  confirmBeforeDelete: boolean;
}

/** One named accent preset — colors as #RRGGBB strings (AccentDto on the host). */
export interface AccentOption {
  key: string;
  base: string;
  hover: string;
  onAccent: string;
}

/** Input to createTask. The host assigns id/state and validates the time. */
export interface TaskInput {
  title: string;
  scheduledDate: string;
  remindTime: string;
  day?: string;
  type?: TaskType | string;
}

/** Partial update for updateTask — only present fields are applied. */
export interface TaskUpdate extends Partial<TaskInput> {
  id: string;
  completed?: boolean;
}

export type AmbientCommand =
  | { op: 'play'; path: string; displayName?: string }
  | { op: 'stop' }
  | { op: 'volume'; volume: number };

// ---- Push payloads ----

export interface TimePush {
  now: string; // ISO 8601 local time, re-read from the system clock each tick
}

export interface NextUpChangedPush {
  task: TaskItem | null;
  remainingMs: number;
}

export interface AmbientChangedPush {
  playing: boolean;
  volume: number;
  name: string | null;
}

export interface InitialState {
  now: string;
  tasks: TaskItem[];
  nextUp: TaskItem | null;
  settings: AppSettings;
  accents: AccentOption[];
  startupEnabled: boolean;
  ambient: { playing: boolean; volume: number; name: string | null };
}

export interface StateSnapshot {
  now: string;
  tasks: TaskItem[];
  nextUp: TaskItem | null;
}

// ---- Envelopes ----

export interface IpcRequest {
  kind: 'req';
  id: number;
  type: string;
  payload?: unknown;
}

export interface IpcResponse {
  kind: 'res';
  id: number;
  ok: boolean;
  payload?: unknown;
  error?: string;
}

export interface IpcPush {
  kind: 'push';
  type: string;
  payload?: unknown;
}

export type PushType =
  | 'time'
  | 'tasksChanged'
  | 'nextUpChanged'
  | 'alarmFired'
  | 'reminderFired'
  | 'ambientChanged'
  | 'settingsChanged';

/** Typed request catalogue: `request('createTask', input)` resolves to TaskItem. */
export interface RequestMap {
  appReady: { payload: void; result: InitialState };
  getState: { payload: void; result: StateSnapshot };
  getTasks: { payload: void; result: TaskItem[] };
  createTask: { payload: TaskInput; result: TaskItem };
  updateTask: { payload: TaskUpdate; result: TaskItem };
  deleteTask: { payload: { id: string }; result: null };
  deleteAllTasks: { payload: void; result: null };
  toggleComplete: { payload: { id: string }; result: TaskItem };
  snoozeTask: { payload: { id: string; minutes?: number }; result: TaskItem };
  dismissAlarm: { payload: void; result: null };
  getSettings: { payload: void; result: AppSettings };
  updateSettings: { payload: AppSettings; result: AppSettings };
  getAccentLibrary: { payload: void; result: AccentOption[] };
  getStartupEnabled: { payload: void; result: boolean };
  setStartupEnabled: { payload: { enabled: boolean }; result: boolean };
  setAmbient: { payload: AmbientCommand; result: { ok?: boolean; volume?: number } };
  getAmbientSounds: { payload: void; result: AmbientSound[] };
  browseAmbientFile: { payload: void; result: AmbientSound | null };
}

/** A playable ambient track served by the host. */
export interface AmbientSound {
  name: string;
  path: string;
}
