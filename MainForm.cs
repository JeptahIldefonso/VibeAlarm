using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
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
        private const string ArtVirtualHost = "art.vibealarm.app";
        private const int UiReleaseDelaySeconds = 60;

        private readonly IClock clock = new Clock();
        private readonly List<TaskItem> masterTaskList = new();
        private readonly TimeService timeService;
        private readonly SchedulerService scheduler;
        private readonly AudioService audioService = new();
        private readonly IMusicPlayer musicPlayer = new NAudioMusicPlayer();
        private readonly ThemeService themeService = ThemeService.Shared;
        private readonly MusicLibraryService musicLibrary = new();
        private readonly MusicPlaybackService musicPlayback;
        private readonly ToastSchedulerService toastScheduler;

        private readonly bool startInTray;
        private bool allowVisible;
        private TrayService? tray;
        private WebView2? webView;
        private IpcBridge bridge = null!;
        private System.Threading.Timer? nextAlarmTimer;
        private System.Windows.Forms.Timer? uiClockTimer;
        private System.Threading.Timer? uiReleaseTimer;
        private System.Windows.Forms.Timer musicProgressTimer = null!;
        private string? lastPushedNextUpId;
        private bool isInitialized;
        private bool userInitiatedExit;
        private bool resumeMusicAfterAlarm;

        public MainForm(bool startInTray = false)
        {
            this.startInTray = startInTray;
            this.allowVisible = !startInTray;
            // Load persisted state once, up front — same startup sequence as the WinForms
            // UI: settings → accent → default ambient assets → task list → scheduler.
            AppSettings restored = SettingsService.Load();
            themeService.ApplyAccent(restored.AccentColor);
            AudioService.EnsureDefaultAmbientSounds(AppPaths.AssetsCacheDir);
            audioService.AmbientVolume = restored.MasterVolume;
            musicPlayer.Volume = restored.MasterVolume;
            musicPlayback = new MusicPlaybackService(musicPlayer, musicLibrary);
            musicPlayback.HydrateFromSettings(restored);

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
            // The SPA's ground is dark (#141414); matching it on the host
            // surface avoids a white/light flash before WebView2 has painted anything.
            BackColor = System.Drawing.Color.FromArgb(0x14, 0x14, 0x14);

            bridge = new IpcBridge(clock, scheduler, masterTaskList, audioService, themeService, musicLibrary, musicPlayer, musicPlayback);
            bridge.AlarmAnswered += () =>
            {
                audioService.StopAlarmLoop();
                tray?.ClearAlarmRinging();
                if (resumeMusicAfterAlarm)
                {
                    resumeMusicAfterAlarm = false;
                    musicPlayback.Resume();
                }
            };
            bridge.Persisted += () =>
            {
                toastScheduler.Reconcile();
                ArmNextAlarmTimer();
            };

            if (!startInTray)
            {
                CreateAndInitializeWebView();
            }

            InitializeServices();
            audioService.AmbientPlayingChanged += _ => bridge.Push(
                "ambientChanged",
                new { playing = audioService.IsAmbientPlaying, volume = audioService.AmbientVolume, name = audioService.ActiveAmbientName });

            // Music library events (marshaled to UI thread for WebView2)
            musicLibrary.ScanProgress += progress =>
            {
                if (InvokeRequired) BeginInvoke(() => bridge.Push("musicScanProgress", progress));
                else bridge.Push("musicScanProgress", progress);
            };

            musicLibrary.TracksBatch += batch =>
            {
                if (InvokeRequired) BeginInvoke(() => bridge.Push("musicTracksBatch", batch));
                else bridge.Push("musicTracksBatch", batch);
            };

            musicLibrary.LibraryChanged += tracks =>
            {
                if (InvokeRequired) BeginInvoke(() => bridge.Push("musicLibraryChanged", new { folders = musicLibrary.Folders, tracks }));
                else bridge.Push("musicLibraryChanged", new { folders = musicLibrary.Folders, tracks });
            };

            musicLibrary.ArtBatchReady += batch =>
            {
                var payload = batch.Select(b => new { trackId = b.TrackId, artKey = b.ArtKey }).ToArray();
                if (InvokeRequired) BeginInvoke(() => bridge.Push("musicArtBatch", payload));
                else bridge.Push("musicArtBatch", payload);
            };

            // Music playback progress ticker (approx 4 times per second = 250ms)
            musicProgressTimer = new System.Windows.Forms.Timer { Interval = 250 };
            musicProgressTimer.Tick += (s, e) =>
            {
                if (musicPlayback.IsPlaying)
                {
                    bridge.Push("musicProgress", new
                    {
                        positionMs = musicPlayback.GetPositionMs(),
                        durationMs = musicPlayback.GetDurationMs()
                    });
                }
                else
                {
                    musicProgressTimer.Stop();
                }
            };

            musicPlayback.TrackChanged += track =>
            {
                if (InvokeRequired) BeginInvoke(() =>
                {
                    bridge.Push("musicTrackChanged", track);
                    if (track != null)
                    {
                        bridge.Push("musicProgress", new
                        {
                            positionMs = 0,
                            durationMs = track.DurationMs > 0 ? track.DurationMs : musicPlayback.GetDurationMs()
                        });
                    }
                });
                else
                {
                    bridge.Push("musicTrackChanged", track);
                    if (track != null)
                    {
                        bridge.Push("musicProgress", new
                        {
                            positionMs = 0,
                            durationMs = track.DurationMs > 0 ? track.DurationMs : musicPlayback.GetDurationMs()
                        });
                    }
                }
            };

            musicPlayback.StateChanged += state =>
            {
                if (InvokeRequired) BeginInvoke(() =>
                {
                    if (state == "playing") musicProgressTimer.Start();
                    else musicProgressTimer.Stop();
                    bridge.Push("musicState", new { state });
                });
                else
                {
                    if (state == "playing") musicProgressTimer.Start();
                    else musicProgressTimer.Stop();
                    bridge.Push("musicState", new { state });
                }
            };

            musicPlayback.QueueStateChanged += qState =>
            {
                if (InvokeRequired) BeginInvoke(() => bridge.Push("musicQueueChanged", qState));
                else bridge.Push("musicQueueChanged", qState);
            };

            musicPlayback.Error += (msg, trackId) =>
            {
                if (InvokeRequired) BeginInvoke(() =>
                {
                    bridge.Push("musicError", new { message = msg, trackId });
                    bridge.Push("musicLibraryChanged", new { folders = musicLibrary.Folders, tracks = musicLibrary.Tracks });
                });
                else
                {
                    bridge.Push("musicError", new { message = msg, trackId });
                    bridge.Push("musicLibraryChanged", new { folders = musicLibrary.Folders, tracks = musicLibrary.Tracks });
                }
            };

            InitializeTray();

            // Startup reconciliation: whatever the task list says NOW is what Windows
            // should hold (adds missed schedules, removes stale ones).
            toastScheduler.Reconcile();
            isInitialized = true;
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
            ArmNextAlarmTimer();
        }

        // ---- WebView2 hosting ----

        private void CreateAndInitializeWebView()
        {
            if (webView != null) return;
            webView = new WebView2 { Dock = DockStyle.Fill };
            Controls.Add(webView);
            webView.CoreWebView2InitializationCompleted += OnWebViewInitialized;
            _ = webView.EnsureCoreWebView2Async();
        }

        private void OnWebViewInitialized(object? sender, CoreWebView2InitializationCompletedEventArgs e)
        {
            if (webView == null) return;
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

            core.SetVirtualHostNameToFolderMapping(
                ArtVirtualHost,
                AppPaths.ArtCacheDir,
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
        public static bool CanReach(string url)
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
            scheduler.MissedAlarm += OnMissedAlarm;

            // Any task transition: persist deterministically, hand React the new list,
            // and bring Windows' scheduled toasts in line with it.
            scheduler.StateChanged += () =>
            {
                TaskStorageService.Save(masterTaskList);
                bridge.Push("tasksChanged", masterTaskList);
                toastScheduler.Reconcile();
                ArmNextAlarmTimer();
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

            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.TimeChanged += OnTimeChanged;

            // Arm the single next-alarm timer and set initial tray tooltip
            ArmNextAlarmTimer();

            if (!startInTray)
            {
                StartUiClock();
            }

            this.Activated += (s, e) =>
            {
                timeService.OnResumed();
                if (allowVisible && WindowState != FormWindowState.Minimized)
                {
                    StartUiClock();
                }
            };
        }

        private void OnAlarmFired(TaskItem task)
        {
            AppSettings settings = SettingsService.Load();
            if (!settings.EnableAlarmSound && !settings.EnableNotifications)
            {
                return;
            }

            // Pause music during alarm; will resume once alarm is answered
            if (musicPlayback.IsPlaying)
            {
                resumeMusicAfterAlarm = true;
                musicPlayback.Pause();
            }
            else
            {
                resumeMusicAfterAlarm = false;
            }

            // The OS toast is no longer suppressed at fire time: the occurrence's
            // still-scheduled toast is removed (so it can't double-announce) and an
            // identical one is posted immediately.
            toastScheduler.RemoveGroup(task.Id);
            if (settings.EnableNotifications)
            {
                toastScheduler.ShowNow(task.Id, task.Title, ToastSchedulePlanner.BuildBody(task, clock.Now));
            }

            // Add Dismiss and Snooze to tray menu while alarm rings so alarm can ALWAYS be stopped
            // even if WebView2 is missing, released, or slow to load.
            tray?.SetAlarmRinging(
                task.Title,
                onDismiss: () =>
                {
                    audioService.StopAlarmLoop();
                    tray?.ClearAlarmRinging();
                    if (resumeMusicAfterAlarm)
                    {
                        resumeMusicAfterAlarm = false;
                        musicPlayback.Resume();
                    }
                },
                onSnooze: () =>
                {
                    audioService.StopAlarmLoop();
                    tray?.ClearAlarmRinging();
                    if (resumeMusicAfterAlarm)
                    {
                        resumeMusicAfterAlarm = false;
                        musicPlayback.Resume();
                    }
                    scheduler.Snooze(task, TimeSpan.FromMinutes(9), clock.Now);
                });

            // Host starts alarm audio immediately without waiting for UI
            if (settings.EnableAlarmSound)
            {
                audioService.PlayAlarmLoop(Path.Combine(AppContext.BaseDirectory, "Assets"));
            }

            // Restore the window and lazy WebView2 if released
            RestoreFromTray();

            if (bridge.ReactReady)
            {
                bridge.Push("alarmFired", task);
            }
            else
            {
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
                    tray?.ClearAlarmRinging();
                    if (resumeMusicAfterAlarm)
                    {
                        resumeMusicAfterAlarm = false;
                        musicPlayback.Resume();
                    }
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

            toastScheduler.RemoveGroup(task.Id);
            toastScheduler.ShowNow(task.Id, task.Title, ToastSchedulePlanner.BuildBody(task, clock.Now));

            RestoreFromTray();
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

        private void OnMissedAlarm(TaskItem task, DateTime scheduled)
        {
            toastScheduler.ShowNow(task.Id, "Missed Alarm", $"\"{task.Title}\" was scheduled for {scheduled:t} while the computer was asleep or off.");
            if (tray is { IsHidden: true })
            {
                tray.ShowToast("Missed Alarm", $"\"{task.Title}\" was scheduled for {scheduled:t}.", ToolTipIcon.Warning);
            }
        }

        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume)
            {
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        var settings = SettingsService.Load();
                        scheduler.HandleResumeOrTimeChange(clock.Now, settings.MissedAlarmGracePeriodMinutes);
                        ArmNextAlarmTimer();
                        if (allowVisible && WindowState != FormWindowState.Minimized)
                        {
                            StartUiClock();
                        }
                    }));
                }
                catch { }
            }
        }

        private void OnTimeChanged(object? sender, EventArgs e)
        {
            try
            {
                BeginInvoke(new Action(() =>
                {
                    var settings = SettingsService.Load();
                    scheduler.HandleResumeOrTimeChange(clock.Now, settings.MissedAlarmGracePeriodMinutes);
                    ArmNextAlarmTimer();
                    if (allowVisible && WindowState != FormWindowState.Minimized)
                    {
                        StartUiClock();
                    }
                }));
            }
            catch { }
        }

        private void ArmNextAlarmTimer()
        {
            DateTime now = clock.Now;
            DateTime? nextDue = scheduler.GetNextDueTime(now);
            if (nextDue == null)
            {
                nextAlarmTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                tray?.UpdateTooltip(null);
                return;
            }

            TimeSpan delay = nextDue.Value - now;
            long delayMs = (long)Math.Max(0, delay.TotalMilliseconds);
            if (delayMs > 86_400_000) delayMs = 86_400_000;

            if (nextAlarmTimer == null)
            {
                nextAlarmTimer = new System.Threading.Timer(_ =>
                {
                    try
                    {
                        if (!IsDisposed && IsHandleCreated)
                        {
                            BeginInvoke(new Action(OnAlarmTimerTick));
                        }
                    }
                    catch { }
                }, null, delayMs, Timeout.Infinite);
            }
            else
            {
                nextAlarmTimer.Change(delayMs, Timeout.Infinite);
            }

            var nextTask = scheduler.NextUp;
            if (nextTask != null)
            {
                tray?.UpdateTooltip($"{nextTask.Title} at {nextDue.Value:t}");
            }
        }

        private void OnAlarmTimerTick()
        {
            scheduler.CheckTransitions(clock.Now);
            ArmNextAlarmTimer();
        }

        private void StartUiClock()
        {
            StopUiClock();
            if (!isInitialized || bridge == null)
            {
                return;
            }
            if (tray is { IsHidden: true } || WindowState == FormWindowState.Minimized)
            {
                return;
            }

            bridge.Push("time", new { now = clock.Now });
            scheduler.RefreshCountdown(clock.Now);

            DateTime now = clock.Now;
            int msToNextMinute = (60 - now.Second) * 1000 - now.Millisecond;
            if (msToNextMinute <= 50) msToNextMinute = 60000;

            uiClockTimer = new System.Windows.Forms.Timer { Interval = msToNextMinute };
            uiClockTimer.Tick += OnUiClockTick;
            uiClockTimer.Start();
        }

        private void OnUiClockTick(object? sender, EventArgs e)
        {
            if (uiClockTimer == null) return;
            uiClockTimer.Stop();

            if (!isInitialized || bridge == null || tray is { IsHidden: true } || WindowState == FormWindowState.Minimized)
            {
                return;
            }

            bridge.Push("time", new { now = clock.Now });
            scheduler.RefreshCountdown(clock.Now);

            DateTime now = clock.Now;
            int msToNextMinute = (60 - now.Second) * 1000 - now.Millisecond;
            if (msToNextMinute <= 50) msToNextMinute = 60000;
            uiClockTimer.Interval = msToNextMinute;
            uiClockTimer.Start();
        }

        private void StopUiClock()
        {
            if (uiClockTimer != null)
            {
                uiClockTimer.Stop();
                uiClockTimer.Dispose();
                uiClockTimer = null;
            }
        }

        public void HideToTray()
        {
            Hide();
            tray?.MarkHidden();
            StopUiClock();
            if (musicProgressTimer.Enabled)
            {
                musicProgressTimer.Stop();
            }
            UpdateProcessEfficiency(isHidden: true, isAudioPlaying: musicPlayback.IsPlaying || audioService.IsAmbientPlaying);

            uiReleaseTimer?.Dispose();
            uiReleaseTimer = new System.Threading.Timer(_ =>
            {
                try
                {
                    if (!IsDisposed && IsHandleCreated)
                    {
                        BeginInvoke(new Action(ReleaseWebView));
                    }
                }
                catch { }
            }, null, UiReleaseDelaySeconds * 1000, Timeout.Infinite);
        }

        private void ReleaseWebView()
        {
            if (webView == null) return;

            uint browserPid = 0;
            try
            {
                browserPid = webView.CoreWebView2?.BrowserProcessId ?? 0;
            }
            catch { }

            if (webView.CoreWebView2 != null)
            {
                bridge.Detach(webView.CoreWebView2);
            }

            Controls.Remove(webView);
            webView.Dispose();
            webView = null;

            if (browserPid != 0)
            {
                try
                {
                    using var browserProcess = Process.GetProcessById((int)browserPid);
                    browserProcess.Kill(entireProcessTree: true);
                    browserProcess.WaitForExit(3000);
                }
                catch { }
            }

            GC.Collect(2, GCCollectionMode.Forced, true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, true);
            try
            {
                EmptyWorkingSet(Process.GetCurrentProcess().Handle);
            }
            catch { }
        }

        public void RestoreFromTray()
        {
            allowVisible = true;
            uiReleaseTimer?.Dispose();
            uiReleaseTimer = null;

            UpdateProcessEfficiency(isHidden: false, isAudioPlaying: true);

            if (webView == null)
            {
                CreateAndInitializeWebView();
            }

            Show();
            tray?.MarkVisible();
            WindowState = FormWindowState.Normal;
            ForceForeground();
            StartUiClock();

            if (musicPlayback.IsPlaying)
            {
                musicProgressTimer.Start();
            }
        }

        protected override void SetVisibleCore(bool value)
        {
            if (startInTray && !allowVisible)
            {
                value = false;
                if (!IsHandleCreated) CreateHandle();
            }
            base.SetVisibleCore(value);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (!isInitialized) return;
            if (WindowState == FormWindowState.Minimized)
            {
                if (SettingsService.Load().MinimizeToTray)
                {
                    HideToTray();
                }
                else
                {
                    StopUiClock();
                    if (musicProgressTimer.Enabled) musicProgressTimer.Stop();
                    UpdateProcessEfficiency(isHidden: true, isAudioPlaying: musicPlayback.IsPlaying || audioService.IsAmbientPlaying);
                }
            }
            else if (WindowState == FormWindowState.Normal)
            {
                StartUiClock();
                if (musicPlayback.IsPlaying) musicProgressTimer.Start();
                UpdateProcessEfficiency(isHidden: false, isAudioPlaying: true);
            }
        }

        private void UpdateProcessEfficiency(bool isHidden, bool isAudioPlaying)
        {
            try
            {
                using var process = Process.GetCurrentProcess();
                if (isHidden && !isAudioPlaying)
                {
                    if (process.PriorityClass != ProcessPriorityClass.BelowNormal)
                    {
                        process.PriorityClass = ProcessPriorityClass.BelowNormal;
                    }
                }
                else
                {
                    if (process.PriorityClass != ProcessPriorityClass.Normal)
                    {
                        process.PriorityClass = ProcessPriorityClass.Normal;
                    }
                }
            }
            catch { }
        }

        private bool HasActiveUpcomingAlarms()
        {
            return masterTaskList.Any(t => t.Type == SchedulerService.TaskTypeAlarm &&
                                           (t.GetState() is TaskState.Scheduled or TaskState.Due) &&
                                           scheduler.IsScheduledTimeValid(t, clock.Now));
        }

        [DllImport("psapi.dll")]
        private static extern bool EmptyWorkingSet(IntPtr hProcess);

        // ---- Force-foreground (the "queue popped" jump) ----

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
        /// is hidden to the tray, minimized, or merely open behind other apps.
        /// </summary>
        private void ForceForeground()
        {
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
            tray.ShowTray();
            tray.OpenRequested += () => RestoreFromTray();
            tray.ExitRequested += () =>
            {
                userInitiatedExit = true;
                Close();
            };
            if (startInTray)
            {
                tray.MarkHidden();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Hide-to-tray instead of exiting when the user clicks the close (X), if enabled.
            // Only the tray menu's Exit triggers a true close (alarms must keep firing while hidden).
            if (!userInitiatedExit && SettingsService.Load().MinimizeToTray)
            {
                e.Cancel = true;
                HideToTray();
                base.OnFormClosing(e);
                return;
            }

            if (HasActiveUpcomingAlarms())
            {
                DialogResult answer = MessageBox.Show(
                    "Are you sure you want to quit VibeAlarm?\n\n" +
                    "In-app alarms and looping sounds will not ring while the application is closed.\n" +
                    "(Windows will show a scheduled notification chime if registered, but VibeAlarm will not open automatically.)",
                    "Quit VibeAlarm?",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (answer != DialogResult.Yes)
                {
                    e.Cancel = true;
                    userInitiatedExit = false;
                    return;
                }
            }

            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.TimeChanged -= OnTimeChanged;

            nextAlarmTimer?.Dispose();
            nextAlarmTimer = null;
            StopUiClock();
            uiReleaseTimer?.Dispose();
            uiReleaseTimer = null;
            musicProgressTimer?.Stop();
            musicProgressTimer?.Dispose();
            timeService.Dispose();
            tray?.HideTray();
            tray?.Dispose();
            audioService.Dispose();
            musicLibrary.ArtService.CancelCurrentPass();
            musicLibrary.ArtService.Dispose();
            musicPlayback?.Dispose();
            base.OnFormClosing(e);
        }
    }
}
