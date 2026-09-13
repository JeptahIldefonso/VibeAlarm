import { useEffect, useRef, useState, type CSSProperties } from 'react';
import { request } from '../bridge/client';
import type { AmbientSound } from '../bridge/protocol';

/**
 * The Ambient view: a hero card for whatever is playing, the volume slider
 * (applied live through the host's MCI audio), the built-in offline loops,
 * and a custom-file picker (native dialog served by the host).
 */
export function AmbientView({
  playing,
  volume,
  name,
  onLocalVolume,
}: {
  playing: boolean;
  volume: number;
  name: string | null;
  onLocalVolume: (volume: number) => void;
}) {
  const [sounds, setSounds] = useState<AmbientSound[]>([]);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    request('getAmbientSounds').then(setSounds).catch(() => {});
  }, []);

  const play = async (sound: AmbientSound) => {
    setError(null);
    try {
      await request('setAmbient', { op: 'play', path: sound.path, displayName: sound.name });
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    }
  };

  const stop = async () => {
    setError(null);
    try {
      await request('setAmbient', { op: 'stop' });
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    }
  };

  const browse = async () => {
    setError(null);
    try {
      const picked = await request('browseAmbientFile');
      if (picked != null) {
        await play(picked);
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    }
  };

  return (
    <div className="view view-center">
      <section className="ambient-hero" aria-label="Now playing">
        <span className={`ambient-hero-icon${playing ? ' playing' : ''}`} aria-hidden>
          {playing ? '🎵' : '🌙'}
        </span>
        <div className="ambient-hero-main">
          <p className="ambient-hero-kicker">{playing ? 'NOW PLAYING' : 'AMBIENT SOUND'}</p>
          <h2 className="ambient-hero-title">{playing ? (name ?? 'Unknown track') : 'Nothing playing'}</h2>
        </div>
        {playing ? (
          <button type="button" className="btn-ghost" onClick={stop}>
            ■ Stop
          </button>
        ) : (
          sounds.length > 0 && (
            <button type="button" className="btn-primary" onClick={() => play(sounds[0])}>
              ▶ Play {sounds[0].name}
            </button>
          )
        )}
      </section>

      <section className="ambient-volume" aria-label="Ambient volume">
        <span className="field-label">Volume</span>
        <input
          className="ambient-slider"
          type="range"
          min={0}
          max={100}
          value={volume}
          aria-label="Ambient volume"
          style={{ '--fill': `${volume}%` } as CSSProperties}
          onChange={(e) => onLocalVolume(Number(e.target.value))}
        />
        <span className="ambient-volume-value">{volume}%</span>
      </section>

      <section aria-label="Built-in sounds">
        <h3 className="section-label">BUILT-IN SOUNDS</h3>
        <div className="ambient-list">
          {sounds.map((sound) => (
            <button
              key={sound.path}
              type="button"
              className={`ambient-tile${playing && name === sound.name ? ' active' : ''}`}
              onClick={() => (playing && name === sound.name ? stop() : play(sound))}
            >
              <span className="ambient-tile-icon" aria-hidden>
                {playing && name === sound.name ? '❚❚' : '▶'}
              </span>
              <span className="ambient-tile-name">{sound.name}</span>
            </button>
          ))}
        </div>
      </section>

      <section aria-label="Custom sound">
        <h3 className="section-label">CUSTOM</h3>
        <button type="button" className="btn-ghost" onClick={browse}>
          Choose an audio file…
        </button>
        <p className="muted ambient-custom-hint">WAV or MP3 from your machine, played on loop.</p>
      </section>

      {error != null && <p className="field-error" role="alert">{error}</p>}
    </div>
  );
}

/**
 * The volume slider's live-drag behavior: optimistic local value for smooth
 * tracking, with the host's setAmbient volume op debounced behind it.
 */
export function useAmbientVolume(volume: number): [number, (v: number) => void] {
  const [local, setLocal] = useState(volume);
  const pushedRef = useRef(volume);
  const timerRef = useRef<number | null>(null);

  useEffect(() => {
    if (volume !== pushedRef.current) {
      // A push from the host (e.g. initial hydration) — adopt it.
      pushedRef.current = volume;
      setLocal(volume);
    }
  }, [volume]);

  useEffect(() => () => {
    if (timerRef.current != null) window.clearTimeout(timerRef.current);
  }, []);

  const set = (value: number) => {
    setLocal(value);
    if (timerRef.current != null) window.clearTimeout(timerRef.current);
    timerRef.current = window.setTimeout(() => {
      timerRef.current = null;
      pushedRef.current = value;
      request('setAmbient', { op: 'volume', volume: value }).catch(() => {});
    }, 120);
  };
  return [local, set];
}
