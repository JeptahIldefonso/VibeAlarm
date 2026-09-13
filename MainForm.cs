using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using VibeAlarm.Models;
using VibeAlarm.Services;

namespace VibeAlarm
{
    /// <summary>
    /// The WebView2 host shell. The WinForms process owns everything except presentation:
    /// the main window, the NotifyIcon tray, minimize-to-tray-on-close, and ALL business
    /// logic (scheduler, persistence, timing, audio) — unchanged from the WinForms UI era.
    /// The window's only child is a single WebView2 control hosting the React SPA, which
    /// talks to this host exclusively through <see cref="IpcBridge"/>.
    ///
    /// Production serves the built SPA from a virtual host ("vibealarm.app" → ui\ next to
    /// the exe) — never file://. DEBUG builds navigate the Vite dev server instead
    /// (VIBEALARM_DEV_SERVER, default http://localhost:5173) for hot reload.
    /// </summary>
    public sealed class MainForm : Form
    {
        private const string VirtualHost = "vibealarm.app";

        private readonly IClock clock = new Clock();
        private readonly List<TaskItem> masterTaskList = new();
        private readonly TimeService timeService;
        private readonly SchedulerService scheduler;
        private readonly AudioService audioService = new();
        private readonly ThemeService themeService = ThemeService.Shared;
        private readonly ToastSchedulerService toastScheduler;

        private TrayService? tray;
        private WebView2 webView = null!;
        private IpcBridge bridge = null!;
        private System.Windows.Forms.Timer backgroundAlarmTicker = null!;
        private string? lastPushedNextUpId;
        private bool userInitiatedExit;

        public MainForm()
        {
            // Load persisted state once, up front — same startup sequence as the WinForms
            // UI: settings → accent → default ambient assets → task list → scheduler.
            AppSettings restored = SettingsService.Load();
            themeService.ApplyAccent(restored.AccentColor);
            AudioService.EnsureDefaultAmbientSounds(AppPaths.AssetsCacheDir);
            // Under the activation lock: a toast Snooze clicked while the app was closed
            // may have rewritten tasks.json during this process's startup.
            lock (ToastSchedulerService.TaskFileLock)
            {
                masterTaskList.AddRange(TaskStorageService.Load());
            }

            // One authoritative time source and one event-driven scheduler, both backed by
            // the same clock. The scheduler references the live masterTaskList so runtime
            // add/remove is picked up without resync.
            timeService = new TimeService(clock);
            scheduler = new SchedulerService(clock, masterTaskList);
            scheduler.ReplaceTasks(); // deterministic state reconstruction after restart

            // Windows owns the exact-time trigger via OS-scheduled toasts (7-day horizon,
            // reconciled as state changes) so alarms fire even when the process isn't
            // running.
            toastScheduler = new ToastSchedulerService(() => masterTaskList, () => clock.Now);

            Text = "VibeAlarm";
            StartPosition = FormStartPosition.CenterScreen;
            // The SPA renders at 125% display scale (html zoom in index.css), so
            // the window is grown by the same factor to keep the amount of visible
            // content identical to the old 1280x800.
            ClientSize = new Size(1600, 1000);
            MinimumSize = new Size(1200, 820);
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            // The SPA is dark; a black host surface avoids a white flash before WebView2
            // has painted anything.
            BackColor = System.Drawing.Color.Black;

            // The entire presentation layer: one WebView2 filling the window.
            webView = new WebView2 { Dock = DockStyle.Fill };
            Controls.Add(webView);
            webView.CoreWebView2InitializationCompleted += OnWebViewInitialized;
            _ = webView.EnsureCoreWebView2Async();

            bridge = new IpcBridge(clock, scheduler, masterTaskList, audioService, themeService);
            bridge.AlarmAnswered += audioService.StopAlarmLoop;
            bridge.Persisted += toastScheduler.Reconcile;

            InitializeServices();
            audioService.AmbientPlayingChanged += _ => bridge.Push(
                "ambientChanged",
                new { playing = audioService.IsAmbientPlaying, volume = audioService.AmbientVolume, name = audioService.ActiveAmbientName });

            InitializeTray();

            // Startup reconciliation: whatever the task list says NOW is what Windows
            // should hold (adds missed schedules, removes stale ones).
            toastScheduler.Reconcile();
        }

        /// <summary>Reloads tasks.json into the live list — called after a toast Snooze
        /// answered while the app was closed rewrote the file behind the running
        /// process's back (marshaled to the UI thread by the activation router).</summary>
        public void ReloadTasksFromDisk()
        {
            lock (ToastSchedulerService.TaskFileLock)
            {
                List<TaskItem> reloaded = TaskStorageService.Load();
                masterTaskList.Clear();
                masterTaskList.AddRange(reloaded);
            }
            scheduler.ReplaceTasks();
            bridge.Push("tasksChanged", masterTaskList);
            toastScheduler.Reconcile();
        }

        // ---- WebView2 hosting ----

        private void OnWebViewInitialized(object? sender, CoreWebView2InitializationCompletedEventArgs e)
        {
            if (!e.IsSuccess || webView.CoreWebView2 == null)
            {
                // Surface the failure in-window rather than leaving a black void.
                Controls.Add(new Label
                {
                    Dock = DockStyle.Fill,
                    ForeColor = System.Drawing.Color.White,
                    TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
                    Text = $"WebView2 failed to initialize:\n\n{e.InitializationException?.Message ?? "Unknown error"}\n\nThe Microsoft Edge WebView2 Runtime must be installed.",
                });
                return;
            }

            CoreWebView2 core = webView.CoreWebView2;

            // Production: the built SPA is served from a virtual host mapped to ui\ next
            // to the exe — a real https origin (never file://), so modules/fetches behave.
            core.SetVirtualHostNameToFolderMapping(
                VirtualHost,
                Path.Combine(AppContext.BaseDirectory, "ui"),
                CoreWebView2HostResourceAccessKind.Allow);

            bridge.Attach(core);

#if DEBUG
            // Dev: the Vite dev server for hot reload (VIBEALARM_DEV_SERVER, default
            // http://localhost:5173). An empty value forces the packaged UI, and so
            // does an unreachable dev server — a Debug build should always show the
            // app, never a connection-refused page.
            string devServer = Environment.GetEnvironmentVariable("VIBEALARM_DEV_SERVER") ?? "http://localhost:5173";
            if (devServer.Length == 0 || !CanReach(devServer))
            {
                core.Navigate($"https://{VirtualHost}/index.html");
            }
            else
            {
                core.Navigate(devServer);
            }
#else
            core.Navigate($"https://{VirtualHost}/index.html");
#endif
        }

        /// <summary>Quick TCP probe so a Debug launch without a running dev server
        /// falls back to the packaged UI instead of showing an error page.</summary>
        private static bool CanReach(string url)
        {
            try
            {
                if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)) return false;
                using var probe = new System.Net.Sockets.TcpClient();
                return probe.ConnectAsync(uri.Host, uri.Port).Wait(500) && probe.Connected;
            }
            catch
            {
                return false;
            }
        }

        // ---- Service wiring (scheduler + time, translated to IPC pushes) ----

        private void InitializeServices()
        {
            // Hybrid alarm: while the app runs, the React overlay is the answer surface
            // (loop stops when it answers); the legacy MessageBox is the fallback for the
            // startup race before React has completed its handshake.
            scheduler.AlarmFired += OnAlarmFired;
            scheduler.ReminderFired += OnReminderFired;

            // Any task transition: persist deterministically, hand React the new list,
            // and bring Windows' scheduled toasts in line with it.
            scheduler.StateChanged += () =>
            {
                TaskStorageService.Save(masterTaskList);
                bridge.Push("tasksChanged", masterTaskList);
                toastScheduler.Reconcile();
            };

            // Next Up changes tell React which task leads and its remaining time; the
            // per-second countdown itself is derived client-side from the time pushes.
            scheduler.NextUpChanged += (task, remaining) =>
            {
                string? id = task?.Id;
                if (id != lastPushedNextUpId)
                {
                    lastPushedNextUpId = id;
                    bridge.Push("nextUpChanged", new { task, remainingMs = remaining.TotalMilliseconds });
                }
            };

            // A single 1-second timer drives the live clock. The TimeService turns each
            // second into cheap per-second ticks plus rare minute/date/resume boundaries.
            backgroundAlarmTicker = new System.Windows.Forms.Timer { Interval = 1000 };
            backgroundAlarmTicker.Tick += (s, e) => timeService.Pulse();
            backgroundAlarmTicker.Start();

            // Per second: push the system time (re-read from the clock each tick — React
            // never increments a local counter) and refresh the Next Up countdown.
            timeService.SecondChanged += now =>
            {
                bridge.Push("time", new { now });
                scheduler.RefreshCountdown(now);
            };

            // Minute/resume boundary: evaluate task transitions.
            timeService.MinuteChanged += now => scheduler.CheckTransitions(now);
            timeService.Resumed += now => scheduler.CheckTransitions(now);
            timeService.DateChanged += now => scheduler.CheckTransitions(now);

            // Re-sync immediately whenever the app regains focus (timer may have been
            // suspended while the window was hidden).
            this.Activated += (s, e) => timeService.OnResumed();
        }

        private void OnAlarmFired(TaskItem task)
        {
            // The toggles gate the alarm's *surfaces* (sound, notification) — never the
            // scheduler itself.
            AppSettings settings = SettingsService.Load();
            if (!settings.EnableAlarmSound && !settings.EnableNotifications)
            {
                return;
            }

            // The OS toast is no longer suppressed at fire time: the occurrence's
            // still-scheduled toast is removed (so it can't double-announce) and an
            // identical one is posted immediately — the alarm is announced on the
            // Windows notification surface at the actual fire moment, alongside the
            // in-app overlay + sound.
            toastScheduler.RemoveGroup(task.Id);
            if (settings.EnableNotifications)
            {
                toastScheduler.ShowNow(task.Id, task.Title, ToastSchedulePlanner.BuildBody(task, clock.Now));
            }

            ForceForeground();

            if (settings.EnableAlarmSound)
            {
                audioService.PlayAlarmLoop(Path.Combine(AppContext.BaseDirectory, "Assets"));
            }

            if (bridge.ReactReady)
            {
                // The React overlay (Snooze 9 min / Dismiss) is the answer surface; the
                // loop stops when it answers (dismissAlarm / snoozeTask → AlarmAnswered).
                bridge.Push("alarmFired", task);
            }
            else
            {
                // Startup race or placeholder UI — the legacy prompt, identical to the
                // WinForms-era behavior.
                try
                {
                    if (settings.EnableNotifications)
                    {
                        DialogResult answer = MessageBox.Show(
                            $"Alarm Triggered:\n\n\"{task.Title}\"\n\nSnooze for 9 minutes?",
                            "Alarm Alert",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Warning);
                        if (answer == DialogResult.Yes)
                        {
                            scheduler.Snooze(task, TimeSpan.FromMinutes(9), clock.Now);
                        }
                    }
                }
                finally
                {
                    audioService.StopAlarmLoop();
                }
            }
        }

        private void OnReminderFired(TaskItem task)
        {
            AppSettings settings = SettingsService.Load();
            if (!settings.EnableNotifications)
            {
                return;
            }

            // Same as the alarm path: replace the occurrence's scheduled toast with an
            // immediate one at the actual fire moment.
            toastScheduler.RemoveGroup(task.Id);
            toastScheduler.ShowNow(task.Id, task.Title, ToastSchedulePlanner.BuildBody(task, clock.Now));

            ForceForeground();
            System.Media.SystemSounds.Asterisk.Play();
            if (bridge.ReactReady)
            {
                bridge.Push("reminderFired", task);
            }
            else if (tray is { IsHidden: true })
            {
                tray.ShowToast("Reminder", $"\"{task.Title}\" is due.", ToolTipIcon.Info);
            }
            else
            {
                MessageBox.Show($"Task Reminder:\n\n\"{task.Title}\"", "Task Notification",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        // ---- Force-foreground (the "queue popped" jump) ----

        // Windows only lets a process call SetForegroundWindow successfully when it
        // already owns the foreground (or meets narrow criteria); from the background
        // the call silently fails and the taskbar button just flashes. The classic
        // workaround: attach our input queue to the current foreground thread's —
        // that makes this process part of the foreground input context for the
        // duration of the call, so the focus steal is permitted.
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        /// <summary>
        /// Brings the window to the true foreground on every fire — including when it
        /// is hidden to the tray, minimized, or merely open behind other apps (plain
        /// <see cref="Form.Activate"/> covers only the first two). Best-effort: no
        /// failure here may ever break the alarm itself.
        /// </summary>
        private void ForceForeground()
        {
            // Un-hide from the tray / restore from minimized so there is a visible,
            // normal-state window to bring forward. Capture the hidden state BEFORE
            // MarkVisible() clears it.
            bool wasHidden = tray is { IsHidden: true };
            if (tray is { IsHidden: true } hidden)
            {
                Show();
                hidden.MarkVisible();
            }
            if (wasHidden || WindowState == FormWindowState.Minimized)
            {
                WindowState = FormWindowState.Normal;
            }

            try
            {
                IntPtr foregroundWindow = GetForegroundWindow();
                uint foregroundThread = GetWindowThreadProcessId(foregroundWindow, out _);
                uint thisThread = GetCurrentThreadId();
                if (foregroundThread != 0 && foregroundThread != thisThread)
                {
                    AttachThreadInput(thisThread, foregroundThread, true);
                    try
                    {
                        SetForegroundWindow(Handle);
                    }
                    finally
                    {
                        AttachThreadInput(thisThread, foregroundThread, false);
                    }
                }
                else
                {
                    SetForegroundWindow(Handle);
                }

                // Belt and braces: if the input-queue trick was still denied (e.g. an
                // elevated foreground window), a momentary topmost toggle forces the
                // z-order change regardless.
                if (GetForegroundWindow() != Handle)
                {
                    TopMost = true;
                    TopMost = false;
                }
            }
            catch
            {
                // Never let a foreground trick break the alarm itself.
            }

            Activate();
        }

        // ---- Tray + close-to-tray ----

        private void InitializeTray()
        {
            tray = new TrayService(System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath));
            // The icon is present for the whole app lifetime (Discord-style), not
            // only while hidden — the user can always reach Open/Exit from the tray.
            tray.ShowTray();
            tray.OpenRequested += () =>
            {
                Show();
                tray.MarkVisible();
                WindowState = FormWindowState.Normal;
                Activate();
                timeService.OnResumed();
            };
            tray.ExitRequested += () =>
            {
                userInitiatedExit = true;
                Close();
            };
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Hide-to-tray instead of exiting when the user clicks the close (X), if enabled.
            // Only the tray menu's Exit triggers a true close (alarms must keep firing while hidden).
            if (!userInitiatedExit && SettingsService.Load().MinimizeToTray)
            {
                e.Cancel = true;
                Hide();
                tray?.MarkHidden();
                base.OnFormClosing(e);
                return;
            }

            backgroundAlarmTicker?.Stop();
            timeService.Dispose();
            tray?.HideTray();
            tray?.Dispose();
            audioService.Dispose();
            base.OnFormClosing(e);
        }
    }
}
