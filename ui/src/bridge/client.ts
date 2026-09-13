import type {
  AmbientChangedPush,
  AppSettings,
  IpcPush,
  IpcRequest,
  IpcResponse,
  NextUpChangedPush,
  NoteItem,
  PushType,
  RequestMap,
  TaskItem,
  TimePush,
} from './protocol';

/**
 * The React half of the IPC channel. All host communication flows through here:
 * `request()` for the req/res calls, `subscribe()` for the host's pushes. React
 * never owns scheduling, persistence, or timing — it renders state and sends
 * intents.
 *
 * Guarded with isHostAvailable() so the SPA still renders (empty) in a plain
 * browser during development.
 */

interface WebViewHost {
  postMessage(message: unknown): void;
  addEventListener(type: 'message', listener: (event: MessageEvent) => void): void;
  removeEventListener(type: 'message', listener: (event: MessageEvent) => void): void;
}

declare global {
  interface Window {
    chrome?: { webview?: WebViewHost };
  }
}

const pending = new Map<number, { resolve: (value: unknown) => void; reject: (error: Error) => void }>();
const pushHandlers = new Map<string, Set<(payload: unknown) => void>>();
let nextRequestId = 1;
let listening = false;

export function isHostAvailable(): boolean {
  return typeof window !== 'undefined' && window.chrome?.webview != null;
}

function ensureListening(): void {
  const host = window.chrome?.webview;
  if (!host || listening) return;
  listening = true;
  host.addEventListener('message', (event: MessageEvent) => {
    const message = event.data as IpcResponse | IpcPush | undefined;
    if (!message || typeof message !== 'object' || !('kind' in message)) return;

    if (message.kind === 'res') {
      const waiter = pending.get(message.id);
      if (!waiter) return;
      pending.delete(message.id);
      if (message.ok) {
        waiter.resolve(message.payload);
      } else {
        waiter.reject(new Error(message.error ?? 'Request failed.'));
      }
    } else if (message.kind === 'push') {
      const handlers = pushHandlers.get(message.type);
      handlers?.forEach((handler) => handler(message.payload));
    }
  });
}

/** Sends a typed request to the host and resolves with its payload. */
export function request<TType extends keyof RequestMap & string>(
  type: TType,
  ...payload: RequestMap[TType]['payload'] extends void ? [] : [RequestMap[TType]['payload']]
): Promise<RequestMap[TType]['result']> {
  const host = window.chrome?.webview;
  if (!host) {
    return Promise.reject(new Error('Host bridge is not available.'));
  }
  ensureListening();

  const id = nextRequestId++;
  const message: IpcRequest = { kind: 'req', id, type };
  if (payload.length > 0) {
    message.payload = payload[0];
  }

  return new Promise((resolve, reject) => {
    pending.set(id, { resolve: resolve as (value: unknown) => void, reject });
    host.postMessage(message);
  });
}

/** Subscribes to a host push. Returns an unsubscribe function. */
export function subscribe<TType extends PushType>(
  type: TType,
  handler: (payload: PushPayload<TType>) => void,
): () => void {
  ensureListening();
  let handlers = pushHandlers.get(type);
  if (!handlers) {
    handlers = new Set();
    pushHandlers.set(type, handlers);
  }
  const wrapped = handler as (payload: unknown) => void;
  handlers.add(wrapped);
  return () => {
    handlers.delete(wrapped);
  };
}

type PushPayload<TType extends PushType> =
  TType extends 'time' ? TimePush
  : TType extends 'tasksChanged' ? TaskItem[]
  : TType extends 'nextUpChanged' ? NextUpChangedPush
  : TType extends 'alarmFired' ? TaskItem
  : TType extends 'reminderFired' ? TaskItem
  : TType extends 'ambientChanged' ? AmbientChangedPush
  : TType extends 'settingsChanged' ? AppSettings
  : TType extends 'notesChanged' ? NoteItem[]
  : never;
