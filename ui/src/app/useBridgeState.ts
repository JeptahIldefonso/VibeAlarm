import { useEffect, useState } from 'react';
import { isHostAvailable, request, subscribe } from '../bridge/client';
import type {
  AppSettings,
  AccentOption,
  InitialState,
  LibraryFolder,
  MusicQueueState,
  MusicScanProgress,
  MusicTrack,
  NoteItem,
  NowPlayingState,
  TaskItem,
} from '../bridge/protocol';

/**
 * The single React-side state holder: hydrated once from the host's appReady
 * snapshot, then kept current by the host's pushes. React only renders this —
 * every mutation goes back through request() and comes home as a push.
 *
 * The clock is the pushed system time (re-read by the host each tick) — never a
 * locally incremented counter.
 */
export interface BridgeState {
  ready: boolean;
  now: Date;
  tasks: TaskItem[];
  nextUp: TaskItem | null;
  notes: NoteItem[];
  settings: AppSettings | null;
  accents: AccentOption[];
  startupEnabled: boolean;
  ambientPlaying: boolean;
  ambientVolume: number;
  ambientName: string | null;
  musicFolders: LibraryFolder[];
  musicTracks: MusicTrack[];
  musicScanning: boolean;
  musicScanProgress: MusicScanProgress | null;
  nowPlaying: NowPlayingState | null;
  musicQueue: MusicQueueState | null;
  musicError: { message: string; trackId?: string } | null;
}

const initial: BridgeState = {
  ready: false,
  now: new Date(),
  tasks: [],
  nextUp: null,
  notes: [],
  settings: null,
  accents: [],
  startupEnabled: false,
  ambientPlaying: false,
  ambientVolume: 70,
  ambientName: null,
  musicFolders: [],
  musicTracks: [],
  musicScanning: false,
  musicScanProgress: null,
  nowPlaying: null,
  musicQueue: null,
  musicError: null,
};

export function useBridgeState(): BridgeState {
  const [state, setState] = useState<BridgeState>(initial);

  useEffect(() => {
    if (!isHostAvailable()) return;

    let cancelled = false;
    request('appReady').then((snapshot: InitialState) => {
      if (cancelled) return;
      const initialVolume = snapshot.music?.volume ?? snapshot.ambient.volume ?? 70;
      let initialNowPlaying: NowPlayingState | null = null;

      if (snapshot.music?.currentTrack != null) {
        initialNowPlaying = {
          kind: 'track',
          id: snapshot.music.currentTrack.id,
          title: snapshot.music.currentTrack.title,
          artist: snapshot.music.currentTrack.artist,
          album: snapshot.music.currentTrack.album,
          artKey: snapshot.music.currentTrack.artKey,
          durationMs: snapshot.music.currentTrack.durationMs,
          positionMs: snapshot.music.positionMs ?? 0,
          state: snapshot.music.state ?? 'stopped',
          volume: initialVolume,
          muted: false,
        };
      } else if (snapshot.ambient.playing) {
        initialNowPlaying = {
          kind: 'ambient',
          title: snapshot.ambient.name ?? 'Ambient Sound',
          artist: 'Built-in sound',
          album: 'Looping',
          durationMs: 0,
          positionMs: 0,
          state: 'playing',
          volume: initialVolume,
          muted: false,
        };
      }

      setState((prev) => ({
        ...prev,
        ready: true,
        now: new Date(snapshot.now),
        tasks: snapshot.tasks,
        nextUp: snapshot.nextUp,
        notes: snapshot.notes,
        settings: snapshot.settings,
        accents: snapshot.accents,
        startupEnabled: snapshot.startupEnabled,
        ambientPlaying: snapshot.ambient.playing,
        ambientVolume: initialVolume,
        ambientName: snapshot.ambient.name,
        musicFolders: snapshot.music?.folders ?? [],
        musicTracks: snapshot.music?.tracks ?? [],
        nowPlaying: initialNowPlaying,
        musicQueue: snapshot.music?.queue ?? null,
      }));
    }).catch(() => {
      // Host never answered (e.g. it restarted) — stay in the not-ready state.
    });

    const unsubscribers = [
      subscribe('time', ({ now }: { now: string }) => {
        setState((prev) => ({ ...prev, now: new Date(now) }));
      }),
      subscribe('tasksChanged', (tasks: TaskItem[]) => {
        setState((prev) => ({ ...prev, tasks }));
      }),
      subscribe('notesChanged', (notes: NoteItem[]) => {
        setState((prev) => ({ ...prev, notes }));
      }),
      subscribe('nextUpChanged', ({ task }: { task: TaskItem | null }) => {
        setState((prev) => ({ ...prev, nextUp: task }));
      }),
      subscribe('settingsChanged', (settings: AppSettings) => {
        setState((prev) => ({ ...prev, settings }));
      }),
      subscribe('ambientChanged', ({ playing, volume, name }: { playing: boolean; volume: number; name: string | null }) => {
        setState((prev) => {
          let updatedNowPlaying = prev.nowPlaying;
          if (playing) {
            updatedNowPlaying = {
              kind: 'ambient',
              title: name ?? 'Ambient Sound',
              artist: 'Built-in sound',
              album: 'Looping',
              durationMs: 0,
              positionMs: 0,
              state: 'playing',
              volume,
              muted: prev.nowPlaying?.muted ?? false,
            };
          } else if (prev.nowPlaying?.kind === 'ambient') {
            updatedNowPlaying = null;
          } else if (updatedNowPlaying != null) {
            updatedNowPlaying = { ...updatedNowPlaying, volume };
          }
          return {
            ...prev,
            ambientPlaying: playing,
            ambientVolume: volume,
            ambientName: name,
            nowPlaying: updatedNowPlaying,
          };
        });
      }),
      subscribe('musicScanProgress', (progress: MusicScanProgress) => {
        setState((prev) => ({
          ...prev,
          musicScanning: !progress.done,
          musicScanProgress: progress,
        }));
      }),
      subscribe('musicTracksBatch', (incoming: MusicTrack[]) => {
        setState((prev) => {
          const map = new Map(prev.musicTracks.map((t) => [t.id, t]));
          incoming.forEach((t) => map.set(t.id, t));
          return {
            ...prev,
            musicTracks: Array.from(map.values()),
          };
        });
      }),
      subscribe('musicLibraryChanged', ({ folders, tracks }: { folders: LibraryFolder[]; tracks: MusicTrack[] }) => {
        setState((prev) => ({
          ...prev,
          musicFolders: folders,
          musicTracks: tracks,
          musicScanning: false,
        }));
      }),
      subscribe('musicTrackChanged', (track: MusicTrack | null) => {
        setState((prev) => {
          if (track == null) {
            return {
              ...prev,
              nowPlaying: prev.nowPlaying?.kind === 'track' ? null : prev.nowPlaying,
            };
          }
          return {
            ...prev,
            ambientPlaying: false,
            ambientName: null,
            nowPlaying: {
              kind: 'track',
              id: track.id,
              title: track.title,
              artist: track.artist,
              album: track.album,
              artKey: track.artKey,
              durationMs: track.durationMs,
              positionMs: 0,
              state: 'playing',
              volume: prev.ambientVolume,
              muted: prev.nowPlaying?.muted ?? false,
            },
          };
        });
      }),
      subscribe('musicState', ({ state }: { state: 'playing' | 'paused' | 'stopped' }) => {
        setState((prev) => {
          if (prev.nowPlaying?.kind !== 'track') return prev;
          return {
            ...prev,
            nowPlaying: {
              ...prev.nowPlaying,
              state,
            },
          };
        });
      }),
      subscribe('musicProgress', ({ positionMs, durationMs }: { positionMs: number; durationMs: number }) => {
        setState((prev) => {
          if (prev.nowPlaying?.kind !== 'track') return prev;
          return {
            ...prev,
            nowPlaying: {
              ...prev.nowPlaying,
              positionMs,
              durationMs: durationMs > 0 ? durationMs : prev.nowPlaying.durationMs,
            },
          };
        });
      }),
      subscribe('musicQueueChanged', (queue: MusicQueueState) => {
        setState((prev) => ({ ...prev, musicQueue: queue }));
      }),
      subscribe('musicArtBatch', (batch: Array<{ trackId: string; artKey: string | null }>) => {
        setState((prev) => {
          const artMap = new Map(batch.map((b) => [b.trackId, b.artKey]));
          let anyChanged = false;
          const updatedTracks = prev.musicTracks.map((t) => {
            if (artMap.has(t.id)) {
              const newArtKey = artMap.get(t.id) ?? null;
              if (t.artKey !== newArtKey || !t.artChecked) {
                anyChanged = true;
                return { ...t, artKey: newArtKey, artChecked: true };
              }
            }
            return t;
          });

          let updatedNowPlaying = prev.nowPlaying;
          if (
            updatedNowPlaying?.kind === 'track' &&
            updatedNowPlaying.id &&
            artMap.has(updatedNowPlaying.id)
          ) {
            const currentTrackArt = artMap.get(updatedNowPlaying.id) ?? null;
            if (updatedNowPlaying.artKey !== currentTrackArt) {
              updatedNowPlaying = { ...updatedNowPlaying, artKey: currentTrackArt };
              anyChanged = true;
            }
          }

          if (!anyChanged) return prev;

          return {
            ...prev,
            musicTracks: updatedTracks,
            nowPlaying: updatedNowPlaying,
          };
        });
      }),
      subscribe('musicError', ({ message, trackId }: { message: string; trackId?: string }) => {
        setState((prev) => ({
          ...prev,
          musicError: { message, trackId },
          nowPlaying: prev.nowPlaying?.id === trackId ? null : prev.nowPlaying,
        }));
      }),
    ];

    return () => {
      cancelled = true;
      unsubscribers.forEach((unsubscribe) => unsubscribe());
    };
  }, []);

  return state;
}

