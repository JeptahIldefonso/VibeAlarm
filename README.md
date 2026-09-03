# VibeAlarm

A Windows desktop alarm & task manager built with **C# / WinForms (.NET 10)**. Create tasks, reminders, and alarms; track them across a dashboard, list, and calendar; play ambient focus sounds offline; and personalize the look with built-in dark/light themes. Runs in the background so scheduled tasks fire even when you are focused elsewhere.

> **Note:** This is a C# WinForms application. It uses an SDK-style `.csproj` (not CMake) and auto-includes all `.cs` files, so there is no manual source list to maintain.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/) (Windows)
- Windows (uses WinForms, the MCI multimedia API, and the Registry)

## Getting Started

```powershell
# Run (Debug)
dotnet run --project VibeAlarm.csproj

# Build (Release)
dotnet build VibeAlarm.csproj -c Release

# Build a self-contained installer
powershell -ExecutionPolicy Bypass -File scripts\Build-Installer.ps1
# Run tests
dotnet test tests\VibeAlarm.Tests\VibeAlarm.Tests.csproj
```

The `Build-Installer.ps1` script publishes the app as a single-file, self-contained EXE, then wraps it into `VibeAlarmInstaller.exe` via a small generated .NET installer project in `build_staging/` (cleaned up automatically).

## Project Structure

```text
VibeAlarm/
├── VibeAlarm.csproj              # SDK-style project (auto-globs .cs files)
├── VibeAlarm.slnx                # Solution file
├── Program.cs                    # Application entry point
│
├── Models/                       # Plain data types
│   ├── TaskItem.cs               # A scheduled task / alarm (lifecycle state-aware)
│   ├── TaskState.cs              # Schedule lifecycle: Scheduled → Due → Triggered → Completed / Expired
│   └── ThemePreset.cs            # A color theme definition
│
├── Services/                     # Non-UI application logic
│   ├── IClock.cs / Clock.cs      # Time-source abstraction (system clock; fake in tests)
│   ├── TimeService.cs            # Single live clock: 1s tick + minute/date/rollover/resume events
│   ├── SchedulerService.cs       # Event-driven state machine + Next Up + countdown
│   ├── TaskStorageService.cs     # Save/load tasks.json
│   ├── SettingsService.cs        # Theme persistence + Windows startup registration
│   ├── ThemeService.cs           # Built-in theme catalog
│   ├── AlarmEngine.cs            # Scheduling & due-task detection (legacy helpers retained)
│   └── AudioService.cs           # Ambient MCI playback + alarm.wav loop + WAV synthesis
│
├── UI/
│   ├── Forms/
│   │   ├── MainForm.cs           # Main window (dashboard / tasks / calendar / ambient / settings)
│   │   ├── MainForm.Designer.cs
│   │   ├── MainForm.resx
│   │   └── TaskCreateDialog.cs   # Modal "Create Task" dialog
│   └── Theming/
│       └── VibeAlarmPalette.cs   # Monochrome editorial design tokens (colors, fonts, spacing)
│
├── tests/                        # xUnit unit tests (fake clock, deterministic)
│   └── VibeAlarm.Tests/
│       ├── VibeAlarm.Tests.csproj
│       ├── FakeClock.cs
│       ├── SchedulerServiceTests.cs
│       └── TimeServiceTests.cs
│
├── Assets/                       # Static resources (copied to output)
│   ├── alarm.wav
│   ├── app-icon.png
│   └── app.ico
│
├── Properties/
│   ├── launchSettings.json
│   └── PublishProfiles/
│       └── FolderProfile.pubxml
│
└── scripts/
    └── Build-Installer.ps1       # Installer build script
```

## Runtime Data

At runtime the app stores the following files alongside the executable (in the install/output directory):

- `tasks.json` – persisted task/alarm list
- `settings.json` – saved theme preference
- `Assets/` – generated offline ambient sounds (`brown_noise.wav`, `rain.wav`)

These are user data and are excluded from source control via `.gitignore`.

## Features

- Dashboard, task list, weekday calendar, ambient soundscape, and settings views
- Task types: Notification, Alarm, Important
- Centralized real-time clock (`TimeService`) with a single UI timer driving the calendar clock
- Event-driven `SchedulerService` that automatically transitions schedules (Scheduled → Due →
  Triggered → Completed / Expired) at their due minute, without per-second full refreshes
- Prominent **NEXT UP** block with a live countdown to the nearest upcoming schedule
- Deterministic restart/rollover handling: past schedules are retained as history (retired to
  "Expired") and never deleted; midnight / new-month / resume-from-focus are handled automatically
- Calendar day visual states (today / selected / past / future / has-events / completed)
- Background ticker that triggers due tasks at their scheduled time without duplicate notifications
- Custom local audio playback (MP3, WAV, FLAC, etc.) via the MCI API with volume control
- Offline synthesized focus sounds (brown noise, rain)
- Multiple built-in color themes with live switching
- Optional launch on Windows startup

## Design System

VibeAlarm applies a **monochrome editorial** design language (translated from
the Music Oasis design authority). UI colors, typography, and spacing are
centralized in `UI/Theming/VibeAlarmPalette.cs` rather than hard-coded per
control. The palette is strictly monochrome: serif display headings, monospace
metadata/labels, 1px hairline borders, minimal corner radius, and no accent
colors or shadows. The selectable "themes" are tonal/contrast variations of this
same monochrome system (light editorial variants plus dark `Graphite`/`Midnight`),
so every preset stays within the design language.
