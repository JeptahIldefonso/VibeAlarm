import { useEffect, useRef, useState, type CSSProperties } from 'react';
import { request } from '../bridge/client';
import type { MusicQueueState, NowPlayingState } from '../bridge/protocol';
import { Icon } from './Icon';

interface NowPlayingBarProps {
  nowPlaying: NowPlayingState;
  queueState?: MusicQueueState | null;
  onVolumeChange: (volume: number) => void;
}

/**
 * Global Now-Playing Bar: pinned across all views when music or ambient sound is loaded/playing.
 * Monochrome Raw Luxury styling adhering strictly to VibeAlarm's tokens and typography.
 */
export function NowPlayingBar({ nowPlaying, queueState, onVolumeChange }: NowPlayingBarProps) {
  const [seeking, setSeeking] = useState(false);
  const [seekPos, setSeekPos] = useState(0);
  const [muted, setMuted] = useState(false);
  const [artError, setArtError] = useState(false);
  const prevVolumeRef = useRef(nowPlaying.volume);

  useEffect(() => {
    setArtError(false);
  }, [nowPlaying.id, nowPlaying.artKey]);

  const isTrack = nowPlaying.kind === 'track';
  const isPlaying = nowPlaying.state === 'playing';
  const isShuffleActive = queueState?.shuffle ?? false;
  const repeatMode = queueState?.repeatMode ?? 'off';

  // Format milliseconds into M:SS or H:MM:SS
  const formatTime = (ms: number): string => {
    if (isNaN(ms) || ms < 0) return '0:00';
    const totalSeconds = Math.floor(ms / 1000);
    const hours = Math.floor(totalSeconds / 3600);
    const minutes = Math.floor((totalSeconds % 3600) / 60);
    const seconds = totalSeconds % 60;
    const secStr = seconds.toString().padStart(2, '0');
    if (hours > 0) {
      return `${hours}:${minutes.toString().padStart(2, '0')}:${secStr}`;
    }
    return `${minutes}:${secStr}`;
  };

  const handleTogglePlay = async () => {
    if (isTrack) {
      if (isPlaying) {
        await request('musicTransport', { action: 'pause' }).catch(() => {});
      } else {
        await request('musicTransport', { action: 'resume' }).catch(() => {});
      }
    } else {
      if (isPlaying) {
        await request('setAmbient', { op: 'stop' }).catch(() => {});
      }
    }
  };

  const handleToggleShuffle = async () => {
    if (!isTrack) return;
    await request('musicTransport', {
      action: 'shuffle',
      enabled: !isShuffleActive,
    }).catch(() => {});
  };

  const handlePrevious = async () => {
    if (!isTrack) return;
    await request('musicTransport', { action: 'previous' }).catch(() => {});
  };

  const handleNext = async () => {
    if (!isTrack) return;
    await request('musicTransport', { action: 'next' }).catch(() => {});
  };

  const handleCycleRepeat = async () => {
    if (!isTrack) return;
    const nextMode: 'off' | 'all' | 'one' =
      repeatMode === 'off' ? 'all' : repeatMode === 'all' ? 'one' : 'off';
    await request('musicTransport', {
      action: 'repeat',
      mode: nextMode,
    }).catch(() => {});
  };

  const handleSeekChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    setSeeking(true);
    setSeekPos(Number(e.target.value));
  };

  const handleSeekCommit = async (
    e: React.MouseEvent<HTMLInputElement> | React.TouchEvent<HTMLInputElement> | React.KeyboardEvent<HTMLInputElement>,
  ) => {
    setSeeking(false);
    const pos = Number((e.target as HTMLInputElement).value);
    await request('musicTransport', { action: 'seek', positionMs: pos }).catch(() => {});
  };

  const toggleMute = () => {
    if (muted) {
      setMuted(false);
      onVolumeChange(prevVolumeRef.current || 70);
    } else {
      prevVolumeRef.current = nowPlaying.volume;
      setMuted(true);
      onVolumeChange(0);
    }
  };

  const currentPos = seeking ? seekPos : nowPlaying.positionMs;
  const duration = nowPlaying.durationMs;
  const progressPercent = duration > 0 ? Math.min(100, Math.max(0, (currentPos / duration) * 100)) : 0;
  const effectiveVolume = muted ? 0 : nowPlaying.volume;

  return (
    <footer className="now-playing-bar" aria-label="Now playing bar">
      {/* Top progress slider */}
      <div className="np-progress-row">
        <span className="np-time">{isTrack ? formatTime(currentPos) : '0:00'}</span>
        <input
          type="range"
          className="np-progress-slider"
          min={0}
          max={isTrack && duration > 0 ? duration : 100}
          value={isTrack ? currentPos : 0}
          disabled={!isTrack || duration <= 0}
          onChange={handleSeekChange}
          onMouseUp={handleSeekCommit}
          onTouchEnd={handleSeekCommit}
          onKeyUp={handleSeekCommit}
          style={{ '--fill': `${progressPercent}%` } as CSSProperties}
          title={isTrack && duration > 0 ? `${formatTime(currentPos)} / ${formatTime(duration)}` : undefined}
          aria-label="Seek progress"
        />
        <span className="np-time">
          {isTrack ? (duration > 0 ? formatTime(duration) : '--:--') : 'LOOPING'}
        </span>
      </div>

      {/* Main bar content */}
      <div className="np-main-row">
        {/* Left: Artwork + Title + Artist */}
        <div className="np-track-info">
          <div className="np-artwork" aria-hidden="true">
            {isTrack && nowPlaying.artKey && !artError ? (
              <img
                src={`https://art.vibealarm.app/${nowPlaying.artKey}.jpg`}
                alt=""
                className="np-artwork-img"
                onError={() => setArtError(true)}
              />
            ) : (
              <Icon name={isTrack ? 'music' : 'audio-lines'} size={20} />
            )}
          </div>
          <div className="np-meta">
            <span className="np-title" title={nowPlaying.title}>
              {nowPlaying.title}
            </span>
            <span
              className="np-artist-album"
              title={
                isTrack
                  ? `${nowPlaying.artist || 'Unknown artist'} • ${nowPlaying.album || 'Unknown album'}`
                  : 'Built-in sound • Looping'
              }
            >
              {isTrack
                ? `${nowPlaying.artist || 'Unknown artist'} • ${nowPlaying.album || 'Unknown album'}`
                : 'Built-in sound • Looping'}
            </span>
          </div>
        </div>

        {/* Center: Controls */}
        <div className="np-controls">
          <button
            type="button"
            className={`np-btn ${isShuffleActive ? 'active' : ''}`}
            disabled={!isTrack}
            onClick={handleToggleShuffle}
            title={isShuffleActive ? 'Shuffle: On' : 'Shuffle: Off'}
            aria-label="Shuffle"
          >
            <Icon name="shuffle" size={16} />
          </button>
          <button
            type="button"
            className="np-btn"
            disabled={!isTrack}
            onClick={handlePrevious}
            title="Previous track"
            aria-label="Previous"
          >
            <Icon name="skip-back" size={18} />
          </button>
          <button
            type="button"
            className="np-play-btn"
            onClick={handleTogglePlay}
            aria-label={isPlaying ? 'Pause' : 'Play'}
          >
            <Icon name={isPlaying ? 'pause' : 'play'} size={18} />
          </button>
          <button
            type="button"
            className="np-btn"
            disabled={!isTrack}
            onClick={handleNext}
            title="Next track"
            aria-label="Next"
          >
            <Icon name="skip-forward" size={18} />
          </button>
          <button
            type="button"
            className={`np-btn ${repeatMode !== 'off' ? 'active' : ''}`}
            disabled={!isTrack}
            onClick={handleCycleRepeat}
            title={`Repeat: ${repeatMode.charAt(0).toUpperCase() + repeatMode.slice(1)} (click to cycle)`}
            aria-label={`Repeat mode: ${repeatMode}`}
          >
            <Icon name={repeatMode === 'one' ? 'repeat-1' : 'repeat'} size={16} />
          </button>
        </div>

        {/* Right: Volume */}
        <div
          className="np-volume-section"
          onWheel={(e) => {
            e.preventDefault();
            const step = e.deltaY < 0 ? 5 : -5;
            const nextVol = Math.max(0, Math.min(100, effectiveVolume + step));
            if (muted) setMuted(false);
            onVolumeChange(nextVol);
          }}
          title="Scroll mouse wheel to adjust volume"
        >
          <button
            type="button"
            className="np-btn"
            onClick={toggleMute}
            aria-label={muted || effectiveVolume === 0 ? 'Unmute' : 'Mute'}
          >
            <Icon name={muted || effectiveVolume === 0 ? 'volume-x' : 'volume-2'} size={18} />
          </button>
          <input
            type="range"
            className="np-volume-slider"
            min={0}
            max={100}
            value={effectiveVolume}
            onChange={(e) => {
              if (muted) setMuted(false);
              onVolumeChange(Number(e.target.value));
            }}
            style={{ '--fill': `${effectiveVolume}%` } as CSSProperties}
            aria-label="Master volume"
          />
          <span className="np-volume-percent">{effectiveVolume}%</span>
        </div>
      </div>
    </footer>
  );
}
