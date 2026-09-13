import { useEffect, useState } from 'react';
import { isHostAvailable, request, subscribe } from '../bridge/client';
import type { AppSettings, AccentOption, InitialState, TaskItem } from '../bridge/protocol';

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
  settings: AppSettings | null;
  accents: AccentOption[];
  startupEnabled: boolean;
  ambientPlaying: boolean;
  ambientVolume: number;
  ambientName: string | null;
}

const initial: BridgeState = {
  ready: false,
  now: new Date(),
  tasks: [],
  nextUp: null,
  settings: null,
  accents: [],
  startupEnabled: false,
  ambientPlaying: false,
  ambientVolume: 70,
  ambientName: null,
};

export function useBridgeState(): BridgeState {
  const [state, setState] = useState<BridgeState>(initial);

  useEffect(() => {
    if (!isHostAvailable()) return;

    let cancelled = false;
    request('appReady').then((snapshot: InitialState) => {
      if (cancelled) return;
      setState((prev) => ({
        ...prev,
        ready: true,
        now: new Date(snapshot.now),
        tasks: snapshot.tasks,
        nextUp: snapshot.nextUp,
        settings: snapshot.settings,
        accents: snapshot.accents,
        startupEnabled: snapshot.startupEnabled,
        ambientPlaying: snapshot.ambient.playing,
        ambientVolume: snapshot.ambient.volume,
        ambientName: snapshot.ambient.name,
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
      subscribe('nextUpChanged', ({ task }: { task: TaskItem | null }) => {
        setState((prev) => ({ ...prev, nextUp: task }));
      }),
      subscribe('settingsChanged', (settings: AppSettings) => {
        setState((prev) => ({ ...prev, settings }));
      }),
      subscribe('ambientChanged', ({ playing, volume, name }) => {
        setState((prev) => ({ ...prev, ambientPlaying: playing, ambientVolume: volume, ambientName: name }));
      }),
    ];

    return () => {
      cancelled = true;
      unsubscribers.forEach((unsubscribe) => unsubscribe());
    };
  }, []);

  return state;
}
