# VibeAlarm

A Windows desktop alarm & task manager — a **C# / .NET 10 WinForms host** (window,
tray, scheduling, persistence) with the entire UI rendered as a **React SPA inside
WebView2**. Create tasks, reminders, and alarms; track them across a dashboard, list,
and calendar; play ambient focus sounds offline; and personalize the look with eight
accent presets. Alarms are additionally armed as **OS-scheduled toast notifications**,
so they fire on time even when the app isn't running.

> **Architecture note:** This is a hybrid app. The WinForms process is a lean host
> shell (window + tray + WebView2 control + all business logic); `ui/` holds the
> Vite + React + TypeScript presentation layer that replaces the old GDI/WinForms
> controls. React is presentation-only — scheduling, persistence, and timing never
> leave the C# process.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/) (Windows)
- [Node.js](https://nodejs.org/) 18+ (to build the SPA; not needed to run a published build)
- Windows 10/11 with the [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/)
  (preinstalled on Windows 11 and current Windows 10)

## Getting Started

```powershell
# Build the SPA (output lands in ui/dist, which the host copies to ui\ in its output)
cd ui
npm install
npm run build
cd ..

# Run (Debug)
dotnet run --project VibeAlarm.csproj

# Build (Release)
dotnet build VibeAlarm.csproj -c Release

# Run tests (Core + planner + IPC contract, deterministic fake clock)
dotnet test tests\VibeAlarm.Tests\VibeAlarm.Tests.csproj
```

### Development with hot reload

In `#if DEBUG` builds the host can navigate the Vite dev server instead of the
packaged build — set `VIBEALARM_DEV_SERVER` (default `http://localhost:5173`)
and run `npm run dev` in `ui/`. Production builds never use `file://`; the SPA is
served from the virtual host `https://vibealarm.app`, mapped to the `ui\` folder
next to the executable via `SetVirtualHostNameToFolderMapping`.

## How the hybrid works

- **Host shell** (`MainForm.cs`, `Services\TrayService.cs`): owns the window,
  NotifyIcon tray, close-to-tray, run-at-Windows-startup, and the WebView2 control.
- **IPC bridge** (`Services\IpcBridge.cs` + `src/VibeAlarm.Core/Bridge/IpcMessages.cs`
  ↔ `ui/src/bridge/`): typed request/response + push protocol over WebView2's
  `postMessage` / `PostWebMessageAsJson` channel. React sends intents
  (`createTask`, `updateSettings`, `setAmbient`, …); the host executes them against
  the Core services and pushes state changes back (`tasksChanged`, `time`,
  `alarmFired`, …).
- **Alarm accuracy**: `SchedulerService` drives in-app alarms (overlay + looping
  sound + 9-minute snooze) while the app runs, and `ToastSchedulerService` keeps
  Windows' own scheduled toasts in sync with the task list (`ToastSchedulePlanner`
  computes the add/remove diff). With the app closed, the OS fires the toast on
  schedule; its Snooze button re-arms the task via background activation.

## Project Structure

```text
VibeAlarm/
├── VibeAlarm.csproj              # WinForms host (net10.0-windows10.0.19041.0, WebView2)
├── Program.cs                    # Entry point + toast activation router
├── MainForm.cs                   # Lean host shell: WebView2 + tray + service wiring
│
├── Services/                     # Host-side services
│   ├── IpcBridge.cs              # The C# half of the React IPC channel
│   ├── ToastSchedulerService.cs  # WinRT scheduled toasts + background snooze
│   └── TrayService.cs            # NotifyIcon tray
│
├── src/VibeAlarm.Core/           # Backend (no UI dependencies)
│   ├── Bridge/IpcMessages.cs     # IPC wire contract (mirrored by protocol.ts)
│   ├── Models/                   # TaskItem, TaskState, AppSettings
│   ├── Services/                 # Clock, TimeService, SchedulerService, storage,
│   │                             #   settings, AlarmEngine, AudioService (MCI),
│   │                             #   ToastSchedulePlanner (pure, unit-tested)
│   └── UI/Theming/AccentCatalog.cs  # The eight accent presets (single source of truth)
│
├── ui/                           # React SPA (Vite + TypeScript)
│   └── src/
│       ├── bridge/               # protocol.ts (typed contract) + client.ts
│       ├── app/useBridgeState.ts # The single React state holder (host-pushed)
│       ├── views/                # Dashboard, Tasks, Calendar, Ambient, Settings
│       ├── components/           # TaskModal, AlarmLayer, CommandPalette
│       ├── lib/tasks.ts          # Stored-format date/time helpers
│       └── styles/tokens.css     # Design system tokens
│
├── tests/VibeAlarm.Tests/        # xUnit (fake clock; planner; IPC contract round-trips)
├── Assets/                       # alarm.wav, icons (copied to output)
└── scripts/Build-Installer.ps1   # Installer build script
```

## Runtime Data

At runtime the app stores user data under `%LOCALAPPDATA%\VibeAlarm`:

- `tasks.json` – persisted task/alarm list
- `settings.json` – saved settings (accent, toggles, last view)
- `Assets/` – generated offline ambient sounds (`brown_noise.wav`, `rain.wav`)
- `toast-diagnostics.log` – scheduled-toast errors (if any)

## Features

- Dashboard (live clock, active/done-today stats, NEXT UP countdown), task list,
  month calendar, ambient soundscape, and settings views — all React
- Task types: Alarm, Important, Notification
- Live clock pushed from the host each second (re-read from the system clock, never
  a locally incremented counter)
- Event-driven `SchedulerService` (Scheduled → Due → Triggered → Completed / Expired)
  with deterministic restart/rollover handling
- OS-scheduled toast alarms that fire on time even when the app isn't running;
  Snooze on the toast re-arms via background activation
- In-app full-screen alarm overlay with looping sound and 9-minute snooze while
  the app runs
- Ctrl+K command palette (actions + task search), Ctrl+N new task
- Custom local audio playback (MCI) with volume control; offline synthesized
  brown noise and rain
- Eight named accent presets (Matrix Green default) with live app-wide switching
- Optional launch on Windows startup

## Design System

Fixed dark surfaces — `#121212` content over a `#000000` chrome family, white
primary ink, `#B3B3B3` muted ink — locked in `ui/src/styles/tokens.css`. The one
runtime-variable part is the accent (`AccentCatalog.cs`, mirrored into CSS
variables on `:root`): eight named presets, each a fixed Base/Hover/OnAccent
triple, wired to primary buttons, active nav, task tags, the calendar today
circle, and selected accent rows. Destructive red and the amber/violet stat
badges are fixed constants, never accent-driven.
