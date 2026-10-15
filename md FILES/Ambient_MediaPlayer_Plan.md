# VibeAlarm: Ambient → Media Player
### Planning and implementation spec (no code)

**Goal:** Turn the Ambient page into a simple local music player. The user adds a folder of music, sees their songs in a clean list (like Windows Media Player), and plays them. The look stays in the existing VibeAlarm theme. All UI changes are strictly isolated to the **Ambient tab only** (no global bars on other pages, no changes to Tasks, Notes, Calendar, or Settings).

**Status:** Plan only. Nothing here is implemented yet.
**Audience:** The project owner, and the coding agent (Claude Code / GLM) that will build it phase by phase.

---

## 0. How to use this document

- Sections 1–6 define **what** to build and how it should look.
- Sections 7–8 define **how** it works internally and what can go wrong.
- Section 9 is the **build order**. Work one phase at a time.
- Section 11 lists the **decisions already made** (defaults), so the builder does not have to wait for answers.
- Section 13 holds **rules for the coding agent**.

---

## 1. Success criteria (Definition of Done)

The feature is done when all of these are true:

1. The user can click **Add folder**, pick a folder, and see its songs listed (subfolders included).
2. Double-clicking a song plays it. Play/pause, next, previous, seek, shuffle, repeat, and volume all work.
3. **Shuffle and play** starts a random order from the songs currently shown.
4. Songs show title, artist, album, year, genre, and duration. Missing tags show "Unknown artist / album / genre", and the title falls back to the file name.
5. The list can be sorted, filtered by genre, and searched.
6. Playback keeps going in the background via the host when switching pages (Tasks, Notes, Calendar, etc.), while all player UI and the now-playing bar remain strictly inside the Ambient tab (no intrusive global bar across other pages; **touch the ambient section tab only**).
7. Closing and reopening the app restores the library, volume, and the last played song (paused).
8. The existing built-in sounds (Brown Noise, Rain) and the custom audio file option still work inside the Sounds tab.
9. The page looks like it belongs in VibeAlarm: same colors, glass panels, fonts, and label style.
10. A library of about 5,000 songs scrolls smoothly and the app stays responsive while scanning.

---

## 2. Scope

| Phase | Included |
|---|---|
| **MVP** | Add/remove folders, scan, song list, play queue, now-playing bar, shuffle/repeat, volume, sort/filter/search, persistence, built-in sounds kept |
| **Later** | Albums and Artists tabs, queue panel, Windows media keys, sleep timer, alarm integration, song as alarm tone, playlists |
| **Not planned** | Streaming services, tag editing, equalizer, video playback, lyrics, cloud sync |

---

## 3. Current state (from the current Ambient screenshot)

- A card at the top: label **AMBIENT SOUND**, status **"Nothing playing"**, and a button **"Play Brown Noise"**.
- A **Volume** slider (currently 70%).
- **Built-in sounds:** two tiles, Brown Noise and Rain.
- **Custom:** a button "Choose an audio file..." (WAV or MP3, played on loop).
- Theme: dark gray sidebar, light frosted-glass panels over a gray fabric background, bold sans-serif headings, small **monospace uppercase labels** with wide letter-spacing, rounded corners, thin borders, mid-gray buttons.

**What changes:** The page becomes a library plus a persistent player. The existing sounds move into their own tab instead of being removed.

---

## 4. Reference: what to borrow from Windows Media Player

**Borrow:**
- **Add folder** button at the top right.
- **Shuffle and play** as the main action button.
- Tabs for **Songs / Albums / Artists** (Songs first).
- **Sort by** and **Genre** dropdowns.
- A song list **grouped by first letter** (A, B, C... with letter headers), alternating row shading, and columns: title, artist, album, year, genre, duration.
- A **now-playing bar** fixed at the bottom with a progress slider, times, artwork, title/artist, shuffle, previous, play/pause, next, repeat, and volume.
- A search box.

**Do not borrow:**
- The orange accent color. VibeAlarm keeps its gray palette.
- The dark Windows 11 look. VibeAlarm keeps its light frosted panels.
- Large album-art grids in the MVP. Keep it simple.

---

## 5. Target UX

### 5.1 Layout

```
┌─────────┬────────────────────────────────────────────────────────────────┐
│ Sidebar │  AMBIENT                                        [ + Add folder ] │
│ (same   │  [Songs] [Albums] [Artists] [Sounds]                             │
│ as now) │  [▶ Shuffle and play]  [ Search songs... ]  Sort: A–Z ▾  Genre ▾ │
│         │                                                                  │
│         │  O                                                               │
│         │  ▶  Title                 Artist         Album      Year Genre  Time │
│         │  Origami                  The Rare Occ.  Big Whoop  2021 Unknown 2:54 │
│         │  Out of Time              The Weeknd     Dawn FM    2022 Unknown 3:53 │
│         │  P                                                               │
│         │  Pag-ibig ay Kanibalismo  fitterkarma    ...                      │
│         │  ...                                                             │
│         ├────────────────────────────────────────────────────────────────┤
│ 03:33PM │ ━━━━●━━━━━━━━━━━━━━━━━━━━━━━━━━━━  0:00 / 2:32                   │
│ (clock) │ [art] Title            ⇄  ⏮  ▶  ⏭  ↻                🔊 ━━●━ 70% │
│         │       Artist • Album                                             │
└─────────┴────────────────────────────────────────────────────────────────┘
```

The now-playing bar is pinned to the bottom of the **Ambient view** only (it does not cover the sidebar or show on other views), so other pages remain untouched.

### 5.2 Tabs

| Tab | Content | Phase |
|---|---|---|
| **Songs** | Full song list described below | MVP |
| **Sounds** | The current page content, unchanged: Brown Noise and Rain tiles, plus "Choose an audio file..." | MVP |
| **Albums** | Albums as a simple list or grid; click to see its songs | Later |
| **Artists** | Artists as a simple list; click to see their songs | Later |

Reasoning: the old top card (AMBIENT SOUND / Nothing playing / Play Brown Noise) and the old Volume slider are consolidated into the bottom now-playing bar inside the Ambient view.

### 5.3 Header controls

- **Add folder:** opens the native Windows folder picker.
- **Shuffle and play:** builds a random queue from the songs currently visible (respecting search and genre filter) and starts playing.
- **Search:** filters by title, artist, or album as the user types (short delay before filtering).
- **Sort by:** A–Z (default), Z–A, Artist, Album, Year (newest first), Date added, Duration.
- **Genre:** All genres (default), then each genre found.
- A small **Folders** control (icon or text button) opens folder management (5.6).

### 5.4 Song list

**Columns, in order of importance:** Title, Artist, Album, Year, Genre, Duration.

**Grouping:** When sorted by title or A–Z, show a letter header per group (A, B, C...). Use "#" for titles starting with a digit or symbol. For other sort modes, hide letter headers.

**Row states:**

| State | Look |
|---|---|
| Normal | Transparent row, alternate rows get a very subtle shade |
| Hover | Slightly lighter glass; a small play icon appears in the leading column |
| Selected (single click) | Clear highlight using the theme's existing active color |
| Playing | Bolder title and a small animated bars icon in the leading column |
| Unavailable (file missing) | Dimmed text and a small warning icon; not playable |

**Actions:**
- Double-click or Enter plays the song and builds the queue from the visible list, starting at that song.
- Right-click menu is a Later item (Play, Play next, Add to queue, Show in Explorer, Remove from library).

### 5.5 Now-playing bar (Ambient tab only)

- Visible inside the **Ambient tab only**, pinned to the bottom of the Ambient view whenever something is loaded (a song or a built-in sound). It does **NOT** show on Tasks, Notes, Calendar, History, Dashboard, or Settings.
- **Progress slider** across the top with elapsed and total time (e.g. 0:00 / 2:32). Dragging seeks; hovering shows the time under the cursor.
- **Left:** artwork thumbnail (or the app's waveform icon in a circle when there is no art), title, and "Artist • Album".
- **Center:** shuffle toggle, previous, play/pause (largest button), next, repeat (off / all / one).
- **Right:** volume icon (click toggles mute), volume slider, percentage.
- For endless built-in sounds (Brown Noise, Rain): show the sound name, hide the time and seek behavior (show an "∞" or "LOOPING" label), and keep play/pause and volume active.

### 5.6 Folder management

A small panel (popover or modal) opened from the **Folders** control:
- A list of added folders with song counts and "last scanned" time.
- Per folder: **Rescan** and **Remove**. Removing only removes it from the library, never deletes files. Show a short confirmation.
- **Add folder** button.
- If a folder is no longer reachable (renamed, drive missing), show a warning on that row and keep its songs marked unavailable instead of silently deleting them.

### 5.7 Empty, loading, and error states

| Situation | What the user sees |
|---|---|
| No folders yet | A large friendly card in the list area: icon, "Add a music folder to get started", and an **Add folder** button |
| Scanning | A thin progress line and text such as "Scanning... 1,240 songs found". Songs appear progressively and the list is usable while scanning |
| Search with no matches | "No songs match your search" with a clear-search link |
| Folder unreachable | Warning row in folder management, dimmed songs in the list |
| Song fails to play | A short toast "Couldn't play *title*", then skip to the next |
| Unsupported file in folder | Ignored during scan (not shown as an error) |

### 5.8 Keyboard and mouse

| Input | Action |
|---|---|
| Space | Play/pause (only when the user is not typing in a text field) |
| Enter on a row | Play that song |
| Left / Right arrow | Seek back / forward 5 seconds |
| Up / Down arrow | List navigation (volume stays on the slider) |
| Ctrl+F | Focus the search box |
| Double-click row | Play |
| Mouse wheel on volume slider | Adjust volume in steps of 5 |

Windows media keys (play/pause/next/previous) are a Later item.

### 5.9 Responsive behavior

When the window gets narrow, hide columns in this order: Genre, then Year, then Album, then Artist. Title and Duration always stay. The now-playing bar keeps play/pause, previous, next, and volume; shuffle and repeat collapse into a small menu only if there is truly no room.

---

## 6. Theme rules (keep the current look)

Reuse the app's existing CSS variables and class patterns. Do not introduce a new color palette.

| Element | Rule |
|---|---|
| Panels (list area, now-playing bar, folder panel) | Same frosted-glass surface, border, and corner radius as the current Ambient cards |
| Page and section labels (e.g. "AMBIENT", column headers, letter headers) | Same small monospace uppercase style with wide letter-spacing used by "BUILT-IN SOUNDS" and "VOLUME" |
| Primary button (Shuffle and play, Add folder) | Same mid-gray button style as "Play Brown Noise" and "New note" |
| Secondary controls (dropdowns, search) | Same input and button styling already used in Notes and Settings |
| Tabs | Same pill style as the sidebar's active item, scaled down |
| Slider (progress and volume) | Same look as the current Volume slider (gray track, gray round thumb) |
| Playing indicator | No new accent color. Use bolder text weight, the existing active gray, and a small animated bars icon |
| Fonts | Same font family and sizes as the rest of the app |
| Icons | Same icon set already used in the sidebar (outline style, consistent stroke) |
| Dark/light | Whatever the app already supports must keep working. If the app only has one look, match it |

---

## 7. Architecture

### 7.1 Overview

The app is a **C# WinForms host** running a **React single-page UI inside WebView2**. Audio playback is handled directly in the C# host via `AudioService` (using Win32 MCI), which keeps playing across views without needing complex virtual host mappings or browser audio elements. The UI is strictly isolated to the **Ambient tab only**.

```
┌───────────────────────── C# Host (WinForms) ─────────────────────────┐
│  AudioService (MCI playback: play, pause, resume, seek, volume)       │
│  MusicLibraryService                                                  │
│   • folder list      • recursive scan      • tag / filename reading   │
│   • playlist state   • shuffle/repeat      • progress/track pushes    │
│  IpcBridge  ←→  IPC messages / events                                 │
└───────────────────────────────┬──────────────────────────────────────┘
                                │ IPC JSON messages
┌───────────────────────────────┴──────────────────────────────────────┐
│  React UI (WebView2)                                                  │
│   AmbientView (strictly inside the Ambient section tab)               │
│     ├─ Tab switcher: [Songs] [Sounds]                                │
│     ├─ Songs Tab: Header controls + Song list table + Folders modal   │
│     ├─ Sounds Tab: Brown Noise, Rain, Custom audio file picker        │
│     └─ NowPlayingBar: Pinned to bottom of AmbientView ONLY            │
│   Other Views (Tasks, Notes, Calendar, Settings): UNTOUCHED           │
└───────────────────────────────────────────────────────────────────────┘
```

### 7.2 Responsibilities

| Host (C#) | UI (React — AmbientView only) |
|---|---|
| Folder picker dialog & disk scan | Rendering list, tabs, controls inside AmbientView |
| Audio playback, seek, volume via AudioService | Search, sort, filter, grouping (in memory) |
| Auto-advance to next track on track end | User interactions (double-click play, shuffle toggle) |
| Pushing playback progress (position/duration) | NowPlayingBar display (time, seek slider, volume) |
| Persisting folders & preferences in settings | Built-in sound triggers (Brown Noise, Rain) |

### 7.3 Key technical decisions

| Decision | Choice | Why | Alternative |
|---|---|---|---|
| Where audio plays | In C# host via `AudioService` (MCI) | Existing `AudioService` already handles MCI audio; seamlessly plays in background across pages without audio element routing | Web `<audio>` element (requires setting up virtual host mappings for local drive paths, fails with CORS/codecs) |
| UI Scope | Strictly inside `AmbientView` | **Touch the ambient section tab only**: keeps Tasks, Notes, Calendar, and Settings clean and untouched | Global bar across all views (rejected: intrusive across other pages) |
| Reading tags | TagLibSharp or clean filename fallback | Fast, works for all formats; falls back to clean filename without extension | Reading in UI (slow, blocked by browser security) |
| Library storage | `settings.json` / JSON index in AppData | Consistent with existing settings and notes storage pattern | SQLite (unnecessary overhead for local library) |
| Progress updates | Host timer pushes `musicProgress` (~500ms) | Accurate MCI position reporting with no browser event drift | Polling from React (adds redundant IPC round-trips) |

> Confirmed Phase 0 finding: `AudioService.cs` already owns MCI playback and audio volume. Ambient sounds and music share this layer.

### 7.4 Data model

**Track**
| Field | Notes |
|---|---|
| id | Stable id (e.g. hash of full path) |
| path | Full file path |
| folderId | Which library folder it belongs to |
| title | From tag, else file name without extension |
| artist | From tag, else "Unknown artist" |
| album | From tag, else "Unknown album" |
| year | Number or empty |
| genre | From tag, else "Unknown genre" |
| trackNumber | Optional, used for album ordering later |
| durationMs | From file |
| fileSize, modifiedAt | Used to detect changes on rescan |
| addedAt | When first seen |
| available | False if the file could not be found on last check |
| artKey | Reference to a cached thumbnail, if any |

**LibraryFolder:** id, path, lastScannedAt, trackCount, reachable.

**Player state (UI):** source (library song or built-in sound), queue (list of track ids), queueIndex, isPlaying, positionMs, shuffle, repeat (off/all/one), volume (0–100), muted.

**Saved preferences:** volume, shuffle, repeat, sort mode, genre filter, last tab, last track id and position, queue (ids).

### 7.5 IPC messages (names and purpose only)

| Message | Direction | Purpose |
|---|---|---|
| library.pickFolder | UI → Host | Open the native folder picker and add the result |
| library.removeFolder | UI → Host | Remove a folder from the library (files untouched) |
| library.rescan | UI → Host | Rescan one folder or all folders |
| library.getAll | UI → Host | Return the current folders and tracks (on startup) |
| library.scanProgress | Host → UI | Progress updates: folder, songs found so far, done flag |
| library.tracksAdded / tracksUpdated / tracksRemoved | Host → UI | Batched changes (for example 200 tracks per message) so the UI stays responsive |
| library.getArtwork | UI → Host | Return a thumbnail for a track or album |
| library.reveal | UI → Host | Show a file in Windows Explorer (Later) |

Follow the existing message style in the current protocol definitions (naming, request ids, error shape). If the existing "browse audio file" handler already opens a dialog, reuse its pattern for the folder picker.

### 7.6 Playback behavior spec

- **Building the queue:** Playing from a row uses the visible list (after search, filter, and sort) from that row onward. "Shuffle and play" uses the visible list in random order.
- **Shuffle on:** Create a random order once, with the current song placed first. Turning shuffle off returns to the sorted order at the current song.
- **Next:** Goes to the next item. At the end: repeat all wraps around; repeat off stops (bar stays on the last song, paused at the start); repeat one replays the same song.
- **Previous:** If more than 3 seconds have played, restart the current song. Otherwise go to the previous song (in shuffle, use play history).
- **Errors:** If a song fails to load, mark it unavailable, show a toast, and skip after about one second. If five songs in a row fail, stop and show a message.
- **One source at a time:** Starting a built-in sound stops any song, and starting a song stops any built-in sound.
- **Volume:** One shared value (0–100) for songs and built-in sounds. Default 70%, saved between sessions.
- **Seeking:** The slider updates the position on release (and shows a preview while dragging).
- **Resume on launch:** Restore the last song and position, **paused**. Never auto-play on launch.
- **Preload:** Optionally preload the next song to avoid a gap. Not required for MVP.

### 7.7 Scanning behavior

- Runs in the background; the UI never freezes.
- Recursive by default. Supported formats: **mp3, wav, flac, m4a, ogg, opus**. Ignore other files silently.
- **Incremental:** On rescan, compare file size and modified time. Re-read tags only for new or changed files, and mark vanished files as unavailable (do not delete instantly).
- Send results to the UI in batches; show the list as it fills.
- Skip hidden/system folders. Skip files that cannot be opened (locked) and retry on next scan.
- Rescan automatically on app start (quietly), plus a manual Rescan button. Watching folders live is Later.

### 7.8 Integration with the rest of VibeAlarm

| Area | Behavior |
|---|---|
| **Ambient Tab Scope** | **Touch the ambient section tab only**: all player UI, library controls, and now-playing bar are encapsulated within `AmbientView.tsx`. Other views (Tasks, Notes, Calendar, History, Settings) are untouched. |
| **Alarms** | When an alarm rings while music plays, the C# host automatically **pauses the music**, then resumes it when the alarm is dismissed. |
| **Navigation** | Music keeps playing in the background via the host when navigating between pages, but the player bar only renders when viewing the Ambient tab. |
| **Settings page** | Untouched. Folder management, rescanning, and playback controls are accessible directly inside the Ambient view header. |
| **App close / tray** | Minimize-to-tray keeps host running; closing the app disposes audio playback. |
| **Later** | Sleep timer (stop after N minutes with a fade out), use a song as the alarm tone, playlists. |

---

## 8. Edge cases checklist

- [ ] Folder renamed, moved, or deleted while the app is open
- [ ] External or network drive disconnected
- [ ] Same song in two folders (show both; do not auto-merge)
- [ ] Duplicate titles with different artists (e.g. the same song twice in the library)
- [ ] Missing or broken tags; non-English titles and artists (Filipino, Japanese, emoji)
- [ ] Very long titles (truncate with ellipsis, full text on hover)
- [ ] 0-byte, corrupt, or DRM-protected files
- [ ] Unsupported formats such as WMA (ignored; mention in settings help text)
- [ ] Very long files (podcasts, mixes) and seeking in them
- [ ] Huge libraries (10,000+ songs): scan time, memory, scrolling
- [ ] Removing a folder while one of its songs is playing (stop playback cleanly)
- [ ] Window minimized or app in background while a song ends
- [ ] PC sleep and resume; audio device changed or unplugged
- [ ] Alarm rings during a song, and during a built-in sound
- [ ] Switching between a song and a built-in sound repeatedly
- [ ] First launch with no folders; folder with no supported files
- [ ] Adding a folder that is a parent or child of an already-added folder

---

## 9. Phased implementation plan

Work in small phases. Each phase must leave the app building and the existing Ambient features working.

### Phase 0: Discovery & Reality Check (Verified)
**Findings from codebase inspection:**
- [x] **UI Structure:** React 18 + TypeScript SPA inside WebView2 (`ui/src/views/AmbientView.tsx`, `ui/src/App.tsx`).
- [x] **IPC Architecture:** Two-way JSON envelope over WebView2 `postMessage` (`ui/src/bridge/protocol.ts` and `Services/IpcBridge.cs`).
- [x] **Audio Engine Reality:** Audio is **host-played** via C# `AudioService.cs` using Win32 MCI (`mciSendString`). Built-in sounds (Brown Noise, Rain) are generated WAVs stored in `%LOCALAPPDATA%\VibeAlarm\Assets`. MCI already plays local file paths directly, so no complex virtual host mapping is required.
- [x] **Strict Scope:** **Touch the ambient section tab only**. All player UI, tabs, and the now-playing bar live inside `AmbientView.tsx`. The rest of the app (`TasksView`, `NotesView`, `CalendarView`, `HistoryView`, `SettingsView`, and app shell nav) remains completely untouched.

### Phase 1: Library & Playlist Service (Host Side)
**Tasks**
- [ ] Add `MusicLibraryService.cs`: folder list, recursive scan, file metadata/tag reading, play queue state, auto-advance on track end.
- [ ] Extend `AudioService.cs`: add `PlayAmbientFileOnce()` (without loop), seek, pause, resume, position/duration queries via MCI `StringBuilder`.
- [ ] Add IPC messages: `browseAmbientFolder`, `getMusicLibrary`, `playTrack`, `musicTransport` (pause/resume/seek/next/prev/shuffle).
- [ ] Wire events to IPC pushes: `musicProgress`, `musicTrackChanged`, `musicPlayingChanged`.

**Done when:** Adding a folder scans audio files, returns a track list, and double-clicking or requesting playback starts the track cleanly via the host.

### Phase 2: Player Core & Now-Playing Bar (Inside AmbientView)
**Tasks**
- [ ] Extend `protocol.ts`, `client.ts`, and `useBridgeState.ts` with music tracks, current index, progress, and transport state.
- [ ] Build `NowPlayingBar.tsx` mounted at the bottom of `AmbientView` (progress seek slider, track info, transport controls, volume).
- [ ] Move volume and playback controls into this bar.

**Done when:** Both playlist tracks and built-in sounds (Brown Noise, Rain) can be controlled, paused, and volume-adjusted from the bar inside the Ambient view.

### Phase 3: Library UI & Tabs (Inside AmbientView)
**Tasks**
- [ ] Add `[Songs]` and `[Sounds]` tabs inside `AmbientView.tsx`. Move existing Brown Noise, Rain, and custom file picker into `[Sounds]`.
- [ ] In `[Songs]`: Add header with **Add folder**, **Shuffle and play**, **Search**, **Sort by**, and **Genre**.
- [ ] Build the song list table with letter grouping (A, B, C...), alternating rows, hover/active states, and columns (Title, Artist, Album, Year, Genre, Time).
- [ ] Add empty state ("Add a music folder to get started") and folder management popover.

**Done when:** Music folders can be added and browsed with smooth scrolling, letter headers, search, and one-click play.

### Phase 4: Playback Features & Integration
**Tasks**
- [ ] Queue from visible list; double-click/Enter plays track.
- [ ] Next, previous, shuffle, repeat per behavior spec (7.6).
- [ ] Auto-advance to next song on completion.
- [ ] One-source-at-a-time rule: starting music stops ambient loop; starting ambient loop stops music.
- [ ] Alarm integration: C# host automatically pauses music when alarm rings, resumes upon dismiss.

**Done when:** All transport behaviors, auto-advance, and alarm pause/resume function reliably.

### Phase 5: Polish & Styling
**Tasks**
- [ ] Ensure all colors, fonts, borders, and glass effects strictly reuse existing `tokens.css` variables.
- [ ] Playing indicator: bold title + animated bars icon.
- [ ] Responsive column hiding and title truncation.
- [ ] Verify that navigating away to Tasks, Notes, etc., leaves those pages completely untouched while music plays smoothly in the background.

**Done when:** Every item in Section 1 is true and the Section 10 test matrix passes.

### Phase 6: Extras (optional, in this order)
1. Albums and Artists sub-tabs
2. Queue panel (view, reorder, remove)
3. Windows media keys and system media overlay
4. Sleep timer with fade out
5. Song as alarm tone
6. Playlists

---

## 10. Test plan

### 10.1 Test library to prepare
A folder of about 30 files, plus one large folder:
- Mixed formats: mp3, wav, flac, m4a, ogg
- Fully tagged, partly tagged, and untagged files
- Non-English titles and artists
- Duplicate songs (same file in two folders)
- One corrupt file and one 0-byte file
- One very long file (30+ minutes)
- Nested subfolders (3 levels)
- A large folder of 3,000+ songs for performance
- Optionally a USB drive folder to test disconnect

### 10.2 Manual test matrix

| Area | Check |
|---|---|
| Add folder | Songs appear; list is usable while scanning |
| Persistence | Close and reopen: folders, library, volume, and last song restored (paused) |
| Playback | Play, pause, seek by drag, next, previous (restart vs go back), end of queue |
| Shuffle/repeat | Order differs each time; shuffle off returns to sorted order; repeat all/one |
| Search/sort/filter | Combined correctly; queue follows what is visible |
| Built-ins | Brown Noise, Rain, custom file still work in Sounds tab; switching sources stops the other |
| Navigation | **Touch ambient section tab only**: music continues playing in background; now-playing bar only renders in Ambient tab; other pages remain clean |
| Alarm | Rings during a song → music pauses → resumes after dismiss |
| Errors | Corrupt file skips with a toast; consecutive failures stop cleanly |
| Folder changes | Remove playing folder; rename a folder; unplug a drive |
| Layout | Narrow window hides columns in the right order; theme strictly matches VibeAlarm |
| Performance | 3,000+ songs: smooth scroll, no UI freeze |

### 10.3 Debugging notes (Visual Studio 2026)
- Run the host project with F5 for C# breakpoints (scan, IPC handlers).
- Open DevTools in the running WebView2 window (right-click → Inspect, or F12) for the UI and console errors.
- Rebuild the UI (`npm run build` in `ui/`) before running the host so the host loads the latest front-end.

---

## 11. Decisions already made (defaults)

| # | Question | Default |
|---|---|---|
| 1 | Keep Brown Noise, Rain, and custom file? | Yes, in the **Sounds** tab inside AmbientView |
| 2 | Alarm rings while music plays? | Pause music, resume after dismiss |
| 3 | Expected library size? | Up to about 10,000 songs; JSON index is fine |
| 4 | Formats? | mp3, wav, flac, m4a, ogg; WMA ignored |
| 5 | Scan subfolders? | Yes |
| 6 | Playlists in MVP? | No (Later) |
| 7 | Where does the now-playing bar show? | **Inside the Ambient tab only** (pinned to bottom of AmbientView) |
| 8 | Auto-play on launch? | Never |
| 9 | Accent color for the playing song? | No new color; use the existing active gray plus bold text and an animated bars icon |
| 10 | Settings page touched? | No; all folder and library options reside inside AmbientView |

---

## 12. Proposed file map

**Host (C#)**
- `src/VibeAlarm.Core/Services/MusicLibraryService.cs` (New: scan, track model, queue, auto-advance)
- `src/VibeAlarm.Core/Services/AudioService.cs` (Extend: PlayOnce, Seek, Position, Pause, Resume, Mode queries)
- `src/VibeAlarm.Core/Models/AppSettings.cs` (Extend: `MusicFolders` list)
- `Services/IpcBridge.cs` (Extend: folder & music IPC handlers)
- `MainForm.cs` (Extend: register MusicLibraryService, wire push events, scan on start)

**UI (React/TypeScript — Ambient tab only)**
- `ui/src/bridge/protocol.ts` (Extend: music types, push types, request map)
- `ui/src/bridge/client.ts` (Extend: push payload mapping)
- `ui/src/app/useBridgeState.ts` (Extend: music state fields & subscriptions)
- `ui/src/views/AmbientView.tsx` (Rework: tabs [Songs]/[Sounds], header, song table, empty state)
- `ui/src/components/NowPlayingBar.tsx` (New: pinned to bottom of AmbientView only)
- `ui/src/components/Icon.tsx` (Extend: add play, pause, skip, shuffle, folder-plus icons)
- `ui/src/index.css` (Extend: `.content-ambient` and styles under `/* ---- Ambient ---- */`)
- **Untouched:** `App.tsx` shell router, `DashboardView.tsx`, `TasksView.tsx`, `CalendarView.tsx`, `HistoryView.tsx`, `NotesView.tsx`, `SettingsView.tsx`

---

## 13. Rules for the coding agent

1. **Read before writing.** Complete Phase 0 first and record findings. Do not assume file names from this document.
2. **One phase at a time.** Stop after each phase, list what changed, and confirm the build.
3. **Touch the ambient section tab only.** Do not place global bars on other views; do not modify Tasks, Notes, Calendar, or Settings views.
4. **Never break existing Ambient features** (Brown Noise, Rain, custom file) at any phase.
5. **Reuse the existing theme.** No new colors, fonts, or icon sets. Reuse existing classes and variables.
6. **Follow existing patterns** for IPC naming, error handling, settings storage, and file locations.
7. **Keep the UI responsive.** No scanning, tag reading, or large data transfers on the UI thread.
8. **Never delete or modify the user's music files.** Removing a folder only removes it from the library.
9. **Be honest about verification.** If code could not be built or run, say so clearly and list exactly what still needs to be tested.
10. **Ask only when blocked.** Use the defaults in Section 11 for everything else.
