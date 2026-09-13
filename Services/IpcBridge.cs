using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using VibeAlarm.Bridge;
using VibeAlarm.Models;
using VibeAlarm.UI.Theming;

namespace VibeAlarm.Services
{
    /// <summary>
    /// The host half of the React ↔ C# IPC channel. Transport: WebView2's
    /// PostWebMessageAsJson / WebMessageReceived pair, speaking the envelope contract
    /// in <see cref="Bridge.IpcMessages"/>. React is presentation-only — every handler
    /// below delegates to the Core services (scheduler, storage, settings, audio) and
    /// pushes the resulting state back, so scheduling/persistence/timing never leave
    /// the C# process.
    ///
    /// All handlers run on the WinForms UI thread (WebMessageReceived is raised there),
    /// so they may touch services and controls freely.
    /// </summary>
    public sealed class IpcBridge
    {
        private readonly IClock clock;
        private readonly SchedulerService scheduler;
        private readonly List<TaskItem> tasks;
        private readonly AudioService audio;
        private readonly ThemeService theme;

        private CoreWebView2? core;
        private readonly Dictionary<string, Func<JsonElement?, object?>> handlers =
            new(StringComparer.Ordinal);

        /// <summary>True once React has completed its appReady handshake — the signal that
        /// the in-app alarm overlay (not the legacy MessageBox) is the answer surface.</summary>
        public bool ReactReady { get; private set; }

        /// <summary>Raised when the alarm overlay is answered (dismiss or snooze) so the
        /// host stops the looping alarm audio.</summary>
        public event Action? AlarmAnswered;

        /// <summary>Raised after any mutation persists — the host re-reconciles Windows'
        /// scheduled toasts with the new task list.</summary>
        public event Action? Persisted;

        public IpcBridge(IClock clock, SchedulerService scheduler, List<TaskItem> tasks, AudioService audio, ThemeService theme)
        {
            this.clock = clock;
            this.scheduler = scheduler;
            this.tasks = tasks;
            this.audio = audio;
            this.theme = theme;
            RegisterHandlers();
        }

        /// <summary>Connects the bridge to an initialized CoreWebView2. Call exactly once,
        /// from CoreWebView2InitializationCompleted, before the first Navigate.</summary>
        public void Attach(CoreWebView2 coreWebView)
        {
            core = coreWebView;
            core.WebMessageReceived += OnWebMessageReceived;
        }

        /// <summary>Posts an unsolicited event to React: { kind: "push", type, payload }.</summary>
        public void Push(string type, object? payload)
        {
            if (core == null)
            {
                return;
            }
            core.PostWebMessageAsJson(JsonSerializer.Serialize(new IpcPush { Type = type, Payload = payload }, IpcJson.Options));
        }

        // ---- Transport ----

        private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            IpcRequest? request;
            try
            {
                request = JsonSerializer.Deserialize<IpcRequest>(e.WebMessageAsJson, IpcJson.Options);
            }
            catch (JsonException)
            {
                return; // not one of ours — ignore rather than kill the page
            }

            if (request == null || !string.Equals(request.Kind, "req", StringComparison.Ordinal) ||
                string.IsNullOrEmpty(request.Type))
            {
                return;
            }

            if (string.Equals(request.Type, "appReady", StringComparison.Ordinal))
            {
                ReactReady = true;
            }

            object? result;
            try
            {
                result = handlers.TryGetValue(request.Type, out var handler)
                    ? handler(request.Payload)
                    : throw new InvalidOperationException($"Unknown message type '{request.Type}'.");
            }
            catch (Exception ex)
            {
                Post(IpcResponse.Failure(request.Id, ex.Message));
                return;
            }
            Post(IpcResponse.Success(request.Id, result));
        }

        private void Post(IpcResponse response)
        {
            core?.PostWebMessageAsJson(JsonSerializer.Serialize(response, IpcJson.Options));
        }

        // ---- Handlers ----

        private void RegisterHandlers()
        {
            handlers["appReady"] = _ => InitialState();
            handlers["getState"] = _ => StateSnapshot();

            handlers["getTasks"] = _ => tasks;

            handlers["createTask"] = payload =>
            {
                string? title = GetString(payload, "title");
                if (string.IsNullOrWhiteSpace(title))
                {
                    throw new InvalidOperationException("A task needs a title.");
                }
                var task = new TaskItem
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Title = title,
                    ScheduledDate = GetString(payload, "scheduledDate") ?? clock.Now.ToString("yyyy-MM-dd"),
                    RemindTime = GetString(payload, "remindTime") ?? clock.Now.ToString("hh:mm tt"),
                    Day = GetString(payload, "day") ?? string.Empty,
                    Type = GetString(payload, "type") ?? "Alarm",
                };
                if (!scheduler.IsScheduledTimeValid(task, clock.Now))
                {
                    throw new InvalidOperationException("Scheduled time must be in the future.");
                }
                scheduler.AddTask(task); // AddTask reconciles state + Next Up and raises StateChanged
                Persist();
                return task;
            };

            handlers["updateTask"] = payload =>
            {
                TaskItem task = FindTask(payload);
                bool rescheduled = false;
                if (TryGetString(payload, "title", out string? title) && !string.IsNullOrWhiteSpace(title))
                {
                    task.Title = title;
                }
                if (TryGetString(payload, "scheduledDate", out string? date) && !string.IsNullOrEmpty(date))
                {
                    task.ScheduledDate = date;
                    rescheduled = true;
                }
                if (TryGetString(payload, "remindTime", out string? time) && !string.IsNullOrEmpty(time))
                {
                    task.RemindTime = time;
                    rescheduled = true;
                }
                if (TryGetString(payload, "day", out string? day))
                {
                    task.Day = day ?? string.Empty;
                }
                if (TryGetString(payload, "type", out string? type) && !string.IsNullOrEmpty(type))
                {
                    task.Type = type;
                }
                if (TryGetBool(payload, "completed", out bool completed))
                {
                    task.SetState(completed ? TaskState.Completed : TaskState.Scheduled);
                }
                if (rescheduled && !scheduler.IsScheduledTimeValid(task, clock.Now))
                {
                    throw new InvalidOperationException("Scheduled time must be in the future.");
                }
                scheduler.CheckTransitions(clock.Now);
                scheduler.RecomputeNextUp(clock.Now);
                Persist();
                return task;
            };

            handlers["deleteTask"] = payload =>
            {
                TaskItem task = FindTask(payload);
                tasks.Remove(task);
                scheduler.RecomputeNextUp(clock.Now);
                Persist();
                return null;
            };

            handlers["deleteAllTasks"] = _ =>
            {
                tasks.Clear();
                scheduler.RecomputeNextUp(clock.Now);
                Persist();
                return null;
            };

            handlers["toggleComplete"] = payload =>
            {
                TaskItem task = FindTask(payload);
                task.SetState(task.GetState() == TaskState.Completed ? TaskState.Scheduled : TaskState.Completed);
                Persist();
                return task;
            };

            handlers["snoozeTask"] = payload =>
            {
                TaskItem task = FindTask(payload);
                int minutes = GetInt(payload, "minutes") ?? 9;
                scheduler.Snooze(task, TimeSpan.FromMinutes(minutes), clock.Now); // raises StateChanged → persist + push
                AlarmAnswered?.Invoke();
                return task;
            };

            handlers["dismissAlarm"] = _ =>
            {
                AlarmAnswered?.Invoke();
                return null;
            };

            handlers["getSettings"] = _ => SettingsForClient();

            handlers["updateSettings"] = payload =>
            {
                AppSettings settings;
                try
                {
                    var updated = payload.HasValue ? payload.Value.Deserialize<AppSettings>(IpcJson.Options) : null;
                    if (updated == null)
                    {
                        throw new InvalidOperationException("Missing settings payload.");
                    }
                    settings = updated;
                }
                catch (JsonException)
                {
                    throw new InvalidOperationException("Malformed settings payload.");
                }
                settings.SchemaVersion = SettingsService.Load().SchemaVersion; // host owns schema versioning
                settings.AccentColor = AccentCatalog.Resolve(settings.AccentColor).Key; // normalize legacy keys
                SettingsService.Save(settings);
                theme.ApplyAccent(settings.AccentColor); // keep the host palette state in sync
                Push("settingsChanged", settings);
                return settings;
            };

            handlers["getAccentLibrary"] = _ =>
                AccentCatalog.Options.Select(AccentDto.From).ToArray();

            handlers["getStartupEnabled"] = _ => SettingsService.IsRunOnStartupEnabled();

            handlers["setStartupEnabled"] = payload =>
            {
                SettingsService.SetRunOnStartup(GetBool(payload, "enabled"));
                return SettingsService.IsRunOnStartupEnabled();
            };

            handlers["getAmbientSounds"] = _ => new[]
            {
                new { name = "Brown Noise", path = Path.Combine(AppPaths.AssetsCacheDir, "brown_noise.wav") },
                new { name = "Rain", path = Path.Combine(AppPaths.AssetsCacheDir, "rain.wav") },
            };

            handlers["browseAmbientFile"] = _ =>
            {
                using var dialog = new OpenFileDialog
                {
                    Title = "Choose an ambient sound",
                    Filter = "Audio files (*.wav;*.mp3)|*.wav;*.mp3|All files (*.*)|*.*",
                };
                return dialog.ShowDialog() == DialogResult.OK
                    ? new { path = dialog.FileName, name = Path.GetFileNameWithoutExtension(dialog.FileName) }
                    : null;
            };

            handlers["setAmbient"] = payload =>
            {
                switch (GetString(payload, "op"))
                {
                    case "play":
                        string? path = GetString(payload, "path");
                        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                        {
                            throw new InvalidOperationException("Ambient sound file not found.");
                        }
                        bool started = audio.PlayAmbientFile(path, GetString(payload, "displayName") ?? Path.GetFileNameWithoutExtension(path));
                        return new { ok = started };
                    case "stop":
                        audio.StopAmbientAudio();
                        return new { ok = true };
                    case "volume":
                        audio.AmbientVolume = Math.Clamp(GetInt(payload, "volume") ?? audio.AmbientVolume, 0, 100);
                        audio.ApplyAmbientVolume();
                        Push("ambientChanged", new { playing = audio.IsAmbientPlaying, volume = audio.AmbientVolume, name = audio.ActiveAmbientName });
                        return new { volume = audio.AmbientVolume };
                    default:
                        throw new InvalidOperationException("Unknown ambient op.");
                }
            };
        }

        /// <summary>The full hydration payload React gets once, on appReady.</summary>
        private object InitialState() => new
        {
            now = clock.Now,
            tasks,
            nextUp = scheduler.NextUp,
            settings = SettingsForClient(),
            accents = AccentCatalog.Options.Select(AccentDto.From).ToArray(),
            startupEnabled = SettingsService.IsRunOnStartupEnabled(),
            ambient = new { playing = audio.IsAmbientPlaying, volume = audio.AmbientVolume, name = audio.ActiveAmbientName },
        };

        /// <summary>A leaner snapshot for on-demand refreshes.</summary>
        private object StateSnapshot() => new
        {
            now = clock.Now,
            tasks,
            nextUp = scheduler.NextUp,
        };

        /// <summary>Settings served to React with the accent key resolved to a live
        /// catalog entry — a persisted legacy key ("Spotify Green") maps onto its
        /// preset so the accent library lookup always succeeds.</summary>
        private AppSettings SettingsForClient()
        {
            AppSettings settings = SettingsService.Load();
            settings.AccentColor = AccentCatalog.Resolve(settings.AccentColor).Key;
            return settings;
        }

        /// <summary>Persist the live task list and tell React it changed — the single
        /// "data moved" path after any mutation.</summary>
        private void Persist()
        {
            TaskStorageService.Save(tasks);
            Push("tasksChanged", tasks);
            Persisted?.Invoke();
        }

        private TaskItem FindTask(JsonElement? payload)
        {
            string? id = GetString(payload, "id");
            TaskItem? task = tasks.FirstOrDefault(t => t.Id == id);
            return task ?? throw new InvalidOperationException("Task not found.");
        }

        // ---- JsonElement payload helpers ----

        private static string? GetString(JsonElement? payload, string name)
        {
            if (payload.HasValue && payload.Value.TryGetProperty(name, out JsonElement property) &&
                property.ValueKind == JsonValueKind.String)
            {
                return property.GetString();
            }
            return null;
        }

        private static bool TryGetString(JsonElement? payload, string name, out string? value)
        {
            value = GetString(payload, name);
            return value != null;
        }

        private static bool GetBool(JsonElement? payload, string name)
        {
            if (payload.HasValue && payload.Value.TryGetProperty(name, out JsonElement property) &&
                (property.ValueKind == JsonValueKind.True || property.ValueKind == JsonValueKind.False))
            {
                return property.GetBoolean();
            }
            return false;
        }

        private static bool TryGetBool(JsonElement? payload, string name, out bool value)
        {
            if (payload.HasValue && payload.Value.TryGetProperty(name, out JsonElement property) &&
                (property.ValueKind == JsonValueKind.True || property.ValueKind == JsonValueKind.False))
            {
                value = property.GetBoolean();
                return true;
            }
            value = false;
            return false;
        }

        private static int? GetInt(JsonElement? payload, string name)
        {
            if (payload.HasValue && payload.Value.TryGetProperty(name, out JsonElement property) &&
                property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out int value))
            {
                return value;
            }
            return null;
        }
    }
}
