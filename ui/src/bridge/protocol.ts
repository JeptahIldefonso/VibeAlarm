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

/** A note — one plain .txt file on disk (first line title, rest content).
 *  `updatedAt` is the file's last-write time (ISO string). */
export interface NoteItem {
  id: string;
  title: string;
  content: string;
  updatedAt: string;
}

/** settings.json record (AppSettings + the Appearance partial, flattened). */
export interface AppSettings {
  schemaVersion: number;
  accentColor: string;
  minimizeToTray: boolean;
  startWithWindowsHidden?: boolean;
  missedAlarmGracePeriodMinutes?: number;
  lastActiveView: string;
  lastViewedCalendarMonth: string;
  enableNotifications: boolean;
  enableAlarmSound: boolean;
  confirmBeforeDelete: boolean;
  musicFolders?: string[];
  alarmSound?: string;
  customAlarmSoundPath?: string | null;
  customAlarmSoundName?: string | null;
  alarmVolume?: number;
  alarmDurationMinutes?: number;
  snoozeDurationMinutes?: number;
  gradualVolumeIncrease?: boolean;
}

/** A single track in the music library with TagLibSharp-extracted metadata. */
export interface MusicTrack {
  id: string;
  path: string;
  folderPath: string;
  title: string;
  artist: string;
  album: string;
  year?: number | null;
  genre: string;
  trackNumber?: number | null;
  durationMs: number;
  fileSize: number;
  modifiedAtUtc: string;
  addedAt: string;
  available: boolean;
  artKey?: string | null;
  artChecked?: boolean;
}

/** A registered music folder in the library. */
export interface LibraryFolder {
  path: string;
  addedAt: string;
  lastScannedAt: string;
  trackCount: number;
  reachable: boolean;
}

/** Background scan progress event payload. */
export interface MusicScanProgress {
  folder: string;
  songsFoundSoFar: number;
  done: boolean;
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

/** Input to createNote. The host assigns id; title defaults to "Untitled". */
export interface NoteInput {
  title?: string;
  content?: string;
}

/** Partial update for updateNote — only present fields are applied. */
export interface NoteUpdate extends Partial<NoteInput> {
  id: string;
}

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

export interface NowPlayingState {
  kind: 'track' | 'ambient';
  id?: string;
  title: string;
  artist?: string;
  album?: string;
  artKey?: string | null;
  durationMs: number;
  positionMs: number;
  state: 'playing' | 'paused' | 'stopped';
  volume: number;
  muted: boolean;
}

export interface MusicQueueState {
  shuffle: boolean;
  repeatMode: 'off' | 'all' | 'one';
  queueCount: number;
  currentIndex: number;
  currentTrackId?: string | null;
}

export type MusicTransportCommand =
  | { action: 'pause' }
  | { action: 'resume' }
  | { action: 'stop' }
  | { action: 'seek'; positionMs: number }
  | { action: 'next' }
  | { action: 'previous' }
  | { action: 'shuffle'; enabled?: boolean }
  | { action: 'repeat'; mode?: 'off' | 'all' | 'one' };

export interface InitialState {
  now: string;
  tasks: TaskItem[];
  nextUp: TaskItem | null;
  settings: AppSettings;
  accents: AccentOption[];
  startupEnabled: boolean;
  ambient: { playing: boolean; volume: number; name: string | null };
  notes: NoteItem[];
  music?: {
    folders: LibraryFolder[];
    tracks: MusicTrack[];
    currentTrack?: MusicTrack | null;
    state?: 'playing' | 'paused' | 'stopped';
    positionMs?: number;
    durationMs?: number;
    volume?: number;
    queue?: MusicQueueState;
  };
}

export interface StateSnapshot {
  now: string;
  tasks: TaskItem[];
  nextUp: TaskItem | null;
  notes: NoteItem[];
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
  | 'settingsChanged'
  | 'notesChanged'
  | 'musicScanProgress'
  | 'musicTracksBatch'
  | 'musicLibraryChanged'
  | 'musicTrackChanged'
  | 'musicState'
  | 'musicProgress'
  | 'musicQueueChanged'
  | 'musicArtBatch'
  | 'musicError';

export interface MusicArtBatchItem {
  trackId: string;
  artKey: string | null;
}

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
  getNotes: { payload: void; result: NoteItem[] };
  createNote: { payload: NoteInput; result: NoteItem };
  updateNote: { payload: NoteUpdate; result: NoteItem };
  deleteNote: { payload: { id: string }; result: null };
  /** Opens the host's Save-As dialog and writes the note as plain .txt.
   *  Resolves to null when the user cancels. */
  exportNote: { payload: { title: string; content: string }; result: { path: string } | null };
  /** Opens the note in a standalone lightweight notepad window. */
  detachNote: { payload: { id: string }; result: { ok: boolean } };
  // ---- Music Library (Phase 1) ----
  browseAmbientFolder: {
    payload: void;
    result: { folder: LibraryFolder; tracks: MusicTrack[] } | null;
  };
  removeMusicFolder: {
    payload: { path: string };
    result: { ok: boolean };
  };
  rescanMusicFolder: {
    payload: { path?: string };
    result: { ok: boolean };
  };
  getMusicLibrary: {
    payload: void;
    result: { folders: LibraryFolder[]; tracks: MusicTrack[] };
  };
  // ---- Music Playback (Phase 2 & 4) ----
  playTrack: {
    payload: { id: string; queue?: string[]; shuffle?: boolean };
    result: { ok: boolean; track?: MusicTrack };
  };
  musicTransport: {
    payload: MusicTransportCommand;
    result: {
      ok: boolean;
      state?: string;
      positionMs?: number;
      track?: MusicTrack;
      shuffle?: boolean;
      repeatMode?: 'off' | 'all' | 'one';
    };
  };
  // ---- Alarm Settings (Phase D) ----
  previewAlarmSound: {
    payload: { sound: string; customPath?: string | null; volume: number };
    result: { success: boolean };
  };
  stopAlarmPreview: {
    payload: void;
    result: { ok: boolean };
  };
  selectCustomAlarmSound: {
    payload: void;
    result: { success: boolean; soundPath?: string; soundName?: string; error?: string; canceled?: boolean };
  };
  resetAlarmSettings: {
    payload: void;
    result: AppSettings;
  };
  testAlarm: {
    payload: void;
    result: { ok: boolean };
  };
}

/** A playable ambient track served by the host. */
export interface AmbientSound {
  name: string;
  path: string;
}
