import { memo, useCallback, useEffect, useMemo, useRef, useState, type CSSProperties } from 'react';
import { request } from '../bridge/client';
import type {
  AmbientSound,
  LibraryFolder,
  MusicScanProgress,
  MusicTrack,
  NowPlayingState,
} from '../bridge/protocol';
import { FolderManagerModal } from '../components/FolderManagerModal';
import { Icon } from '../components/Icon';

type AmbientTab = 'songs' | 'sounds' | 'albums' | 'artists';
type SortMode = 'title-asc' | 'title-desc' | 'artist' | 'album' | 'year-desc' | 'duration-desc';

// Format milliseconds into M:SS
function formatDuration(ms: number): string {
  if (isNaN(ms) || ms <= 0) return '0:00';
  const totalSeconds = Math.floor(ms / 1000);
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;
  return `${minutes}:${seconds.toString().padStart(2, '0')}`;
}

interface SongRowProps {
  track: MusicTrack;
  index: number;
  isPlaying: boolean;
  isLoaded: boolean;
  isSelected: boolean;
  onSelect: (id: string) => void;
  onPlay: (track: MusicTrack) => void;
}

/**
 * Memoized single track row to prevent full table re-renders during playback ticks or progressive art batches.
 */
const SongRow = memo(function SongRow({
  track,
  index,
  isPlaying,
  isLoaded,
  isSelected,
  onSelect,
  onPlay,
}: SongRowProps) {
  const [imgError, setImgError] = useState(false);

  useEffect(() => {
    setImgError(false);
  }, [track.artKey]);

  return (
    <div
      className={`song-row${isPlaying ? ' is-playing' : ''}${isLoaded ? ' is-loaded' : ''}${isSelected ? ' is-selected' : ''}${!track.available ? ' is-unavailable' : ''}`}
      onClick={() => onSelect(track.id)}
      onDoubleClick={() => onPlay(track)}
      onKeyDown={(e) => {
        if (e.key === 'Enter') onPlay(track);
      }}
      tabIndex={0}
      role="row"
      aria-selected={isSelected}
    >
      <div className="song-col col-num">
        {isPlaying ? (
          <div className="sound-bars" aria-label="Playing">
            <div className="sound-bar" />
            <div className="sound-bar" />
            <div className="sound-bar" />
            <div className="sound-bar" />
          </div>
        ) : isLoaded ? (
          <div className="sound-bars paused" aria-label="Paused">
            <div className="sound-bar" />
            <div className="sound-bar" />
            <div className="sound-bar" />
            <div className="sound-bar" />
          </div>
        ) : (
          <>
            <span className="row-num">{index + 1}</span>
            <button
              type="button"
              className="row-play-btn"
              disabled={!track.available}
              onClick={(e) => {
                e.stopPropagation();
                onPlay(track);
              }}
              aria-label={`Play ${track.title}`}
            >
              <Icon name="play" size={12} />
            </button>
          </>
        )}
      </div>
      <div className="song-col col-title" title={track.title}>
        <div className="song-thumb-box" aria-hidden="true">
          {track.artKey && !imgError ? (
            <img
              src={`https://art.vibealarm.app/${track.artKey}.jpg`}
              alt=""
              loading="lazy"
              decoding="async"
              className="song-thumb-img"
              onError={() => setImgError(true)}
            />
          ) : (
            <Icon name="music" size={14} />
          )}
        </div>
        <span className="song-title-text">{track.title}</span>
      </div>
      <div className="song-col col-artist" title={track.artist}>
        {track.artist}
      </div>
      <div className="song-col col-album" title={track.album}>
        {track.album}
      </div>
      <div className="song-col col-year">
        {track.year ? track.year : '—'}
      </div>
      <div className="song-col col-genre" title={track.genre}>
        {track.genre || '—'}
      </div>
      <div className="song-col col-time">
        {formatDuration(track.durationMs)}
      </div>
    </div>
  );
});

interface AmbientViewProps {
  playing: boolean;
  volume: number;
  name: string | null;
  onLocalVolume: (volume: number) => void;
  musicFolders?: LibraryFolder[];
  musicTracks?: MusicTrack[];
  musicScanning?: boolean;
  musicScanProgress?: MusicScanProgress | null;
  nowPlaying?: NowPlayingState | null;
}

export function AmbientView({
  playing,
  volume,
  name,
  onLocalVolume,
  musicFolders = [],
  musicTracks = [],
  musicScanning = false,
  musicScanProgress = null,
  nowPlaying = null,
}: AmbientViewProps) {
  const [activeTab, setActiveTab] = useState<AmbientTab>('songs');
  const [searchQuery, setSearchQuery] = useState('');
  const [sortBy, setSortBy] = useState<SortMode>('title-asc');
  const [genreFilter, setGenreFilter] = useState('all');
  const [selectedTrackId, setSelectedTrackId] = useState<string | null>(null);
  const [folderModalOpen, setFolderModalOpen] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const searchInputRef = useRef<HTMLInputElement>(null);

  // Built-in ambient sounds
  const [sounds, setSounds] = useState<AmbientSound[]>([]);


  // Fetch ambient sounds on mount if empty
  useMemo(() => {
    request('getAmbientSounds')
      .then(setSounds)
      .catch(() => {});
  }, []);

  // Distinct genres from indexed tracks
  const availableGenres = useMemo(() => {
    const set = new Set<string>();
    musicTracks.forEach((t) => {
      if (t.genre && t.genre !== 'Unknown genre' && t.genre.trim()) {
        set.add(t.genre.trim());
      }
    });
    return Array.from(set).sort((a, b) => a.localeCompare(b));
  }, [musicTracks]);

  // Filtered tracks
  const filteredTracks = useMemo(() => {
    return musicTracks.filter((t) => {
      if (genreFilter !== 'all' && t.genre.toLowerCase() !== genreFilter.toLowerCase()) {
        return false;
      }
      if (searchQuery.trim()) {
        const q = searchQuery.toLowerCase().trim();
        const matchTitle = t.title.toLowerCase().includes(q);
        const matchArtist = t.artist.toLowerCase().includes(q);
        const matchAlbum = t.album.toLowerCase().includes(q);
        if (!matchTitle && !matchArtist && !matchAlbum) return false;
      }
      return true;
    });
  }, [musicTracks, genreFilter, searchQuery]);

  // Sorted tracks
  const sortedTracks = useMemo(() => {
    const list = [...filteredTracks];
    switch (sortBy) {
      case 'title-asc':
        return list.sort((a, b) => a.title.localeCompare(b.title));
      case 'title-desc':
        return list.sort((a, b) => b.title.localeCompare(a.title));
      case 'artist':
        return list.sort(
          (a, b) =>
            (a.artist || '').localeCompare(b.artist || '') || a.title.localeCompare(b.title),
        );
      case 'album':
        return list.sort(
          (a, b) => (a.album || '').localeCompare(b.album || '') || a.title.localeCompare(b.title),
        );
      case 'year-desc':
        return list.sort((a, b) => (b.year ?? 0) - (a.year ?? 0) || a.title.localeCompare(b.title));
      case 'duration-desc':
        return list.sort((a, b) => b.durationMs - a.durationMs);
      default:
        return list;
    }
  }, [filteredTracks, sortBy]);

  // Grouped by letter (only when sorted by title-asc)
  const groupedTracks = useMemo(() => {
    if (sortBy !== 'title-asc') return null;
    const groups: { letter: string; tracks: MusicTrack[] }[] = [];
    let currentLetter = '';
    let currentGroup: MusicTrack[] = [];

    for (const track of sortedTracks) {
      const firstChar = track.title.trim()[0]?.toUpperCase() || '#';
      const letter = /[A-Z]/.test(firstChar) ? firstChar : '#';
      if (letter !== currentLetter) {
        if (currentGroup.length > 0) {
          groups.push({ letter: currentLetter, tracks: currentGroup });
        }
        currentLetter = letter;
        currentGroup = [track];
      } else {
        currentGroup.push(track);
      }
    }
    if (currentGroup.length > 0) {
      groups.push({ letter: currentLetter, tracks: currentGroup });
    }
    return groups;
  }, [sortedTracks, sortBy]);

  const handlePlayTrack = useCallback(
    async (track: MusicTrack) => {
      if (!track.available) return;
      setError(null);
      try {
        const queueIds = sortedTracks.filter((t) => t.available).map((t) => t.id);
        const res = await request('playTrack', { id: track.id, queue: queueIds });
        if (!res.ok) {
          setError(`Cannot play "${track.title}". Track marked unavailable.`);
        }
      } catch (err) {
        setError(err instanceof Error ? err.message : String(err));
      }
    },
    [sortedTracks],
  );

  const handleSelectTrack = useCallback((id: string) => {
    setSelectedTrackId(id);
  }, []);

  // Auto-dismiss error banner after 4.5 seconds
  useEffect(() => {
    if (error) {
      const timer = setTimeout(() => setError(null), 4500);
      return () => clearTimeout(timer);
    }
  }, [error]);

  // Keyboard navigation & playback shortcuts (Section 5.8)
  useEffect(() => {
    const onKeyDown = (e: KeyboardEvent) => {
      const target = e.target as HTMLElement | null;
      const isInput =
        target &&
        (target.tagName === 'INPUT' ||
          target.tagName === 'TEXTAREA' ||
          target.tagName === 'SELECT' ||
          target.isContentEditable);

      // Ctrl+F / Cmd+F: focus search box
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'f') {
        if (activeTab === 'songs') {
          e.preventDefault();
          searchInputRef.current?.focus();
          searchInputRef.current?.select();
        }
        return;
      }

      if (isInput) return;

      // Space: toggle play/pause
      if (e.key === ' ' || e.code === 'Space') {
        e.preventDefault();
        if (nowPlaying?.kind === 'track') {
          if (nowPlaying.state === 'playing') {
            request('musicTransport', { action: 'pause' }).catch(() => {});
          } else {
            request('musicTransport', { action: 'resume' }).catch(() => {});
          }
        } else if (nowPlaying?.kind === 'ambient') {
          if (nowPlaying.state === 'playing') {
            request('setAmbient', { op: 'stop' }).catch(() => {});
          }
        } else if (sortedTracks.length > 0) {
          const trackToPlay = sortedTracks.find((t) => t.id === selectedTrackId) || sortedTracks[0];
          if (trackToPlay && trackToPlay.available) {
            handlePlayTrack(trackToPlay);
          }
        }
        return;
      }

      // ArrowLeft / ArrowRight: seek back/forward 5 seconds
      if (e.key === 'ArrowLeft' || e.key === 'ArrowRight') {
        if (nowPlaying?.kind === 'track' && nowPlaying.durationMs > 0) {
          e.preventDefault();
          const delta = e.key === 'ArrowRight' ? 5000 : -5000;
          const targetPos = Math.max(0, Math.min(nowPlaying.durationMs, nowPlaying.positionMs + delta));
          request('musicTransport', { action: 'seek', positionMs: targetPos }).catch(() => {});
        }
        return;
      }

      // ArrowUp / ArrowDown: list selection navigation
      if (activeTab === 'songs' && (e.key === 'ArrowDown' || e.key === 'ArrowUp')) {
        if (sortedTracks.length > 0) {
          e.preventDefault();
          const currentIndex = sortedTracks.findIndex((t) => t.id === selectedTrackId);
          if (e.key === 'ArrowDown') {
            const nextIdx = currentIndex < sortedTracks.length - 1 ? currentIndex + 1 : 0;
            setSelectedTrackId(sortedTracks[nextIdx].id);
          } else {
            const prevIdx = currentIndex > 0 ? currentIndex - 1 : sortedTracks.length - 1;
            setSelectedTrackId(sortedTracks[prevIdx].id);
          }
        }
        return;
      }

      // Enter: play selected song
      if (e.key === 'Enter') {
        if (activeTab === 'songs' && selectedTrackId) {
          const trackToPlay = sortedTracks.find((t) => t.id === selectedTrackId);
          if (trackToPlay && trackToPlay.available) {
            e.preventDefault();
            handlePlayTrack(trackToPlay);
          }
        }
      }
    };

    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, [activeTab, sortedTracks, selectedTrackId, nowPlaying, handlePlayTrack]);

  const handleShuffleAndPlay = async () => {
    if (sortedTracks.length === 0) return;
    const available = sortedTracks.filter((t) => t.available);
    if (available.length === 0) return;
    const randomTrack = available[Math.floor(Math.random() * available.length)];
    setError(null);
    try {
      await request('playTrack', {
        id: randomTrack.id,
        queue: available.map((t) => t.id),
        shuffle: true,
      });
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    }
  };

  const handleAddFolder = async () => {
    setError(null);
    try {
      await request('browseAmbientFolder');
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    }
  };

  const handleRescanFolder = async (path?: string) => {
    setError(null);
    try {
      await request('rescanMusicFolder', { path });
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    }
  };

  const handleRemoveFolder = async (path: string) => {
    setError(null);
    try {
      await request('removeMusicFolder', { path });
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    }
  };

  // Ambient sound handlers
  const playAmbient = async (sound: AmbientSound) => {
    setError(null);
    try {
      await request('setAmbient', { op: 'play', path: sound.path, displayName: sound.name });
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    }
  };

  const stopAmbient = async () => {
    setError(null);
    try {
      await request('setAmbient', { op: 'stop' });
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    }
  };

  const browseAmbientFile = async () => {
    setError(null);
    try {
      const picked = await request('browseAmbientFile');
      if (picked != null) {
        await playAmbient(picked);
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    }
  };

  return (
    <div className="music-view">
      {/* Header with Title, Tabs, and Folder Actions */}
      <header className="music-header">
        <div className="music-header-left">
          <h2 className="music-title">Music</h2>
          <nav className="music-tabs" aria-label="Music navigation">
            <button
              type="button"
              className={`music-tab-pill${activeTab === 'songs' ? ' active' : ''}`}
              onClick={() => setActiveTab('songs')}
            >
              Songs
              {musicTracks.length > 0 && (
                <span className="music-tab-badge">{musicTracks.length}</span>
              )}
            </button>
            <button
              type="button"
              className={`music-tab-pill${activeTab === 'sounds' ? ' active' : ''}`}
              onClick={() => setActiveTab('sounds')}
            >
              Sounds
            </button>
            <button
              type="button"
              className="music-tab-pill disabled"
              disabled
              title="Albums (Phase 6)"
            >
              Albums
            </button>
            <button
              type="button"
              className="music-tab-pill disabled"
              disabled
              title="Artists (Phase 6)"
            >
              Artists
            </button>
          </nav>
        </div>

        <div className="music-header-right">
          <button
            type="button"
            className="btn-ghost"
            onClick={() => setFolderModalOpen(true)}
            title="Manage music folders"
          >
            <Icon name="folder" size={15} />
            <span>Folders ({musicFolders.length})</span>
          </button>
          <button
            type="button"
            className="btn-primary"
            onClick={handleAddFolder}
            title="Add a music folder from your computer"
          >
            <Icon name="folder-plus" size={15} />
            <span>+ Add folder</span>
          </button>
        </div>
      </header>

      {/* Scanning status banner */}
      {musicScanning && (
        <div className="music-scan-banner" role="status">
          <span className="spin" style={{ display: 'inline-flex' }}>
            <Icon name="refresh-cw" size={13} />
          </span>
          <span>
            Scanning folders... {musicScanProgress?.songsFoundSoFar ?? 0} tracks discovered so far
          </span>
        </div>
      )}

      {error != null && (
        <div className="music-error-banner" role="alert">
          <span>{error}</span>
          <button type="button" className="btn-icon" onClick={() => setError(null)} aria-label="Dismiss error">
            <Icon name="x" size={14} />
          </button>
        </div>
      )}

      {/* ================= SONGS TAB ================= */}
      {activeTab === 'songs' && (
        <div className="music-tab-content">
          {musicFolders.length === 0 ? (
            /* Empty state: No folders added yet */
            <div className="music-empty-card">
              <div className="music-empty-icon" aria-hidden="true">
                <Icon name="music" size={40} />
              </div>
              <h3 className="music-empty-title">Add a music folder to get started</h3>
              <p className="music-empty-desc">
                Select a folder containing audio files (MP3, WAV, M4A, FLAC) from your PC. Your files remain untouched and will be indexed for instant playback.
              </p>
              <button type="button" className="btn-primary" onClick={handleAddFolder}>
                <Icon name="folder-plus" size={16} />
                <span>+ Add music folder</span>
              </button>
            </div>
          ) : (
            <>
              {/* Songs Toolbar */}
              <div className="music-toolbar">
                <div className="music-toolbar-left">
                  <button
                    type="button"
                    className="btn-primary"
                    disabled={sortedTracks.length === 0}
                    onClick={handleShuffleAndPlay}
                    title="Play visible songs in random order"
                  >
                    <Icon name="shuffle" size={15} />
                    <span>Shuffle and play</span>
                  </button>
                </div>

                <div className="music-toolbar-right">
                  {/* Search box */}
                  <div className="music-search-box">
                    <span className="music-search-icon" aria-hidden="true">
                      <Icon name="search" size={13} />
                    </span>
                    <input
                      ref={searchInputRef}
                      type="text"
                      className="music-search-input"
                      placeholder="Search title, artist, album..."
                      value={searchQuery}
                      onChange={(e) => setSearchQuery(e.target.value)}
                    />
                    {searchQuery.length > 0 && (
                      <button
                        type="button"
                        className="music-search-clear"
                        onClick={() => setSearchQuery('')}
                        aria-label="Clear search"
                      >
                        <Icon name="x" size={12} />
                      </button>
                    )}
                  </div>

                  {/* Sort by dropdown */}
                  <div className="music-dropdown-wrap">
                    <span className="music-dropdown-label">Sort by:</span>
                    <select
                      className="music-select"
                      value={sortBy}
                      onChange={(e) => setSortBy(e.target.value as SortMode)}
                      aria-label="Sort tracks by"
                    >
                      <option value="title-asc">A – Z</option>
                      <option value="title-desc">Z – A</option>
                      <option value="artist">Artist</option>
                      <option value="album">Album</option>
                      <option value="year-desc">Year (newest)</option>
                      <option value="duration-desc">Duration</option>
                    </select>
                  </div>

                  {/* Genre dropdown */}
                  <div className="music-dropdown-wrap">
                    <span className="music-dropdown-label">Genre:</span>
                    <select
                      className="music-select"
                      value={genreFilter}
                      onChange={(e) => setGenreFilter(e.target.value)}
                      aria-label="Filter by genre"
                    >
                      <option value="all">All genres</option>
                      {availableGenres.map((g) => (
                        <option key={g} value={g}>
                          {g}
                        </option>
                      ))}
                    </select>
                  </div>
                </div>
              </div>

              {/* Tracks Table */}
              {sortedTracks.length === 0 ? (
                <div className="music-empty-search">
                  <p className="muted">No songs match your search or genre filter.</p>
                  {(searchQuery.length > 0 || genreFilter !== 'all') && (
                    <button
                      type="button"
                      className="btn-ghost"
                      onClick={() => {
                        setSearchQuery('');
                        setGenreFilter('all');
                      }}
                      style={{ marginTop: 8 }}
                    >
                      Clear filters
                    </button>
                  )}
                </div>
              ) : (
                <div className="song-table-container">
                  <div className="song-table-header" role="row">
                    <div className="song-col col-num">#</div>
                    <div className="song-col col-title">TITLE</div>
                    <div className="song-col col-artist">ARTIST</div>
                    <div className="song-col col-album">ALBUM</div>
                    <div className="song-col col-year">YEAR</div>
                    <div className="song-col col-genre">GENRE</div>
                    <div className="song-col col-time">TIME</div>
                  </div>

                  <div className="song-table-body" role="rowgroup">
                    {groupedTracks != null ? (
                      /* Grouped by letter */
                      groupedTracks.map((group) => (
                        <div key={group.letter} className="song-group-block">
                          <div className="song-group-header" aria-hidden="true">
                            {group.letter}
                          </div>
                          {group.tracks.map((track, idx) => (
                            <SongRow
                              key={track.id}
                              track={track}
                              index={idx}
                              isPlaying={
                                nowPlaying?.kind === 'track' &&
                                nowPlaying.id === track.id &&
                                nowPlaying.state === 'playing'
                              }
                              isLoaded={nowPlaying?.kind === 'track' && nowPlaying.id === track.id}
                              isSelected={selectedTrackId === track.id}
                              onSelect={handleSelectTrack}
                              onPlay={handlePlayTrack}
                            />
                          ))}
                        </div>
                      ))
                    ) : (
                      /* Flat sorted list */
                      sortedTracks.map((track, idx) => (
                        <SongRow
                          key={track.id}
                          track={track}
                          index={idx}
                          isPlaying={
                            nowPlaying?.kind === 'track' &&
                            nowPlaying.id === track.id &&
                            nowPlaying.state === 'playing'
                          }
                          isLoaded={nowPlaying?.kind === 'track' && nowPlaying.id === track.id}
                          isSelected={selectedTrackId === track.id}
                          onSelect={handleSelectTrack}
                          onPlay={handlePlayTrack}
                        />
                      ))
                    )}
                  </div>
                </div>
              )}
            </>
          )}
        </div>
      )}

      {/* ================= SOUNDS TAB ================= */}
      {activeTab === 'sounds' && (
        <div className="sounds-tab-content">
          <section className="ambient-hero" aria-label="Now playing ambient">
            <span className={`ambient-hero-icon${playing ? ' playing' : ''}`} aria-hidden>
              <Icon name="audio-lines" size={26} />
            </span>
            <div className="ambient-hero-main">
              <p className="ambient-hero-kicker">{playing ? 'NOW PLAYING' : 'AMBIENT SOUND'}</p>
              <h2 className="ambient-hero-title">
                {playing ? name ?? 'Unknown sound' : 'Nothing playing'}
              </h2>
            </div>
            {playing ? (
              <button type="button" className="btn-ghost" onClick={stopAmbient}>
                ■ Stop
              </button>
            ) : (
              sounds.length > 0 && (
                <button
                  type="button"
                  className="btn-primary"
                  onClick={() => playAmbient(sounds[0])}
                >
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
                  onClick={() =>
                    playing && name === sound.name ? stopAmbient() : playAmbient(sound)
                  }
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
            <button type="button" className="btn-ghost" onClick={browseAmbientFile}>
              Choose an audio file…
            </button>
            <p className="muted ambient-custom-hint">
              WAV or MP3 from your machine, played on loop.
            </p>
          </section>
        </div>
      )}

      {/* Folder Manager Modal */}
      {folderModalOpen && (
        <FolderManagerModal
          folders={musicFolders}
          onClose={() => setFolderModalOpen(false)}
          onAddFolder={handleAddFolder}
          onRescanFolder={handleRescanFolder}
          onRemoveFolder={handleRemoveFolder}
        />
      )}
    </div>
  );
}

/**
 * Optimistic local volume helper (used by App.tsx)
 */
export function useAmbientVolume(volume: number): [number, (v: number) => void] {
  const [local, setLocal] = useState(volume);

  const set = (value: number) => {
    setLocal(value);
    request('setAmbient', { op: 'volume', volume: value }).catch(() => {});
  };
  return [local, set];
}
