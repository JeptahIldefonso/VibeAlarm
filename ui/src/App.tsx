import { useEffect, useMemo, useState } from 'react';
import { isHostAvailable, request } from './bridge/client';
import { useBridgeState } from './app/useBridgeState';
import { AlarmLayer } from './components/AlarmLayer';
import { CommandPalette, type Command } from './components/CommandPalette';
import { Icon, type IconName } from './components/Icon';
import { TaskModal, type TaskModalState } from './components/TaskModal';
import { AmbientView, useAmbientVolume } from './views/AmbientView';
import { CalendarView } from './views/CalendarView';
import { DashboardView } from './views/DashboardView';
import { HistoryView } from './views/HistoryView';
import { NotesView } from './views/NotesView';
import { SettingsView } from './views/SettingsView';
import { TasksView } from './views/TasksView';

const VIEWS = ['Dashboard', 'Tasks', 'Calendar', 'History', 'Notes', 'Ambient', 'Settings'] as const;
type ViewName = (typeof VIEWS)[number];

const VIEW_ICONS: Record<ViewName, IconName> = {
  Dashboard: 'siren',
  Tasks: 'clipboard-list',
  Calendar: 'calendar-days',
  History: 'history',
  Notes: 'sticky-note',
  Ambient: 'audio-lines',
  Settings: 'sliders-horizontal',
};

/**
 * The app shell: black chrome sidebar (nav + app identity) over the #121212
 * content area, the live-clock header, the active view, the app-level task
 * modal + Ctrl+K palette, and the always-mounted alarm layer. One process —
 * the host owns the window and all state, this owns the pixels.
 */
export default function App() {
  const state = useBridgeState();
  const [activeView, setActiveView] = useState<ViewName>('Tasks');
  const [taskModal, setTaskModal] = useState<TaskModalState | null>(null);
  const [paletteOpen, setPaletteOpen] = useState(false);
  const [ambientVolume, setAmbientVolume] = useAmbientVolume(state.ambientVolume);

  // Accent → CSS variables: the host's AccentCatalog is the single source of truth;
  // this just re-publishes the selected triple onto :root so every accented element
  // switches live.
  useEffect(() => {
    const key = state.settings?.accentColor;
    const accent = state.accents.find(a => a.key === key) ?? state.accents.find(a => a.key === 'Matrix Green');
    if (accent == null) return;
    document.documentElement.style.setProperty('--accent', accent.base);
    document.documentElement.style.setProperty('--accent-hover', accent.hover);
    document.documentElement.style.setProperty('--on-accent', accent.onAccent);
  }, [state.settings?.accentColor, state.accents]);

  // Restore the last active view once settings hydrate (and persist changes back).
  useEffect(() => {
    const persisted = state.settings?.lastActiveView as ViewName | undefined;
    if (persisted != null && (VIEWS as readonly string[]).includes(persisted)) {
      setActiveView(persisted);
    }
    // Only restore on hydration, not on every settings push.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [state.settings != null]);

  useEffect(() => {
    if (state.settings == null || state.settings.lastActiveView === activeView) return;
    request('updateSettings', { ...state.settings, lastActiveView: activeView }).catch(() => {});
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [activeView]);

  // Ctrl+K palette.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.ctrlKey && e.key.toLowerCase() === 'k') {
        e.preventDefault();
        setPaletteOpen((open) => !open);
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, []);

  const openModal = (modalState: TaskModalState) => setTaskModal(modalState);

  const commands: Command[] = useMemo(
    () => [
      ...VIEWS.map((view): Command => ({
        id: `go-${view}`,
        label: `Go to ${view}`,
        run: () => setActiveView(view),
      })),
      {
        id: 'new-task',
        label: 'New task',
        hint: 'Ctrl+N',
        run: () => openModal({ mode: 'create', task: null }),
      },
      {
        id: 'stop-ambient',
        label: 'Stop ambient sound',
        run: () => {
          request('setAmbient', { op: 'stop' }).catch(() => {});
        },
      },
    ],
    [],
  );

  if (!isHostAvailable()) {
    return (
      <main className="shell">
        <p className="muted">No host bridge — run inside the VibeAlarm app (DEBUG: Vite dev server, otherwise the packaged build).</p>
      </main>
    );
  }

  return (
    <div className="app">
      <nav className="sidebar">
        <div className="sidebar-brand">
          <span className="sidebar-logo" aria-hidden>
            <Icon name="bell-ring" size={18} />
          </span>
          <span className="sidebar-name">VibeAlarm</span>
        </div>
        <div className="sidebar-nav">
          {VIEWS.map((view) => (
            <button
              key={view}
              type="button"
              className={view === activeView ? 'nav-item active' : 'nav-item'}
              onClick={() => setActiveView(view)}
            >
              <Icon name={VIEW_ICONS[view]} size={16} />
              <span>{view}</span>
            </button>
          ))}
        </div>
        <div className="sidebar-footer">
          <p className="sidebar-clock">{state.now.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}</p>
        </div>
      </nav>

      <main className="content">
        {activeView === 'Dashboard' && (
          <DashboardView tasks={state.tasks} now={state.now} nextUp={state.nextUp} />
        )}
        {activeView === 'Tasks' && (
          <TasksView
            tasks={state.tasks}
            now={state.now}
            confirmBeforeDelete={state.settings?.confirmBeforeDelete ?? true}
            openModal={openModal}
          />
        )}
        {activeView === 'Calendar' && (
          <CalendarView tasks={state.tasks} now={state.now} settings={state.settings} openModal={openModal} />
        )}
        {activeView === 'History' && (
          <HistoryView
            tasks={state.tasks}
            now={state.now}
            confirmBeforeDelete={state.settings?.confirmBeforeDelete ?? true}
            openModal={openModal}
          />
        )}
        {activeView === 'Notes' && (
          <NotesView
            notes={state.notes}
            confirmBeforeDelete={state.settings?.confirmBeforeDelete ?? true}
          />
        )}
        {activeView === 'Ambient' && (
          <AmbientView
            playing={state.ambientPlaying}
            volume={ambientVolume}
            name={state.ambientName}
            onLocalVolume={setAmbientVolume}
          />
        )}
        {activeView === 'Settings' && (
          <SettingsView
            settings={state.settings}
            accents={state.accents}
            startupEnabled={state.startupEnabled}
          />
        )}
      </main>

      {taskModal != null && <TaskModal state={taskModal} onClose={() => setTaskModal(null)} />}

      <CommandPalette
        open={paletteOpen}
        onClose={() => setPaletteOpen(false)}
        commands={commands}
        tasks={state.tasks}
        now={state.now}
        onOpenTask={(task) => openModal({ mode: 'edit', task })}
      />

      <AlarmLayer now={state.now} />
    </div>
  );
}
