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
        private readonly MusicLibraryService musicLibrary;
        private readonly IMusicPlayer musicPlayer;
        private readonly MusicPlaybackService musicPlayback;

        private readonly List<CoreWebView2> activeCores = new();
        private CoreWebView2? mainCore;
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

        public IpcBridge(
            IClock clock,
            SchedulerService scheduler,
            List<TaskItem> tasks,
            AudioService audio,
            ThemeService theme,
            MusicLibraryService musicLibrary,
            IMusicPlayer musicPlayer,
            MusicPlaybackService musicPlayback)
        {
            this.clock = clock;
            this.scheduler = scheduler;
            this.tasks = tasks;
            this.audio = audio;
            this.theme = theme;
            this.musicLibrary = musicLibrary;
            this.musicPlayer = musicPlayer;
            this.musicPlayback = musicPlayback;
            RegisterHandlers();
        }

        /// <summary>Connects the bridge to an initialized CoreWebView2. Supports multiple
        /// concurrent windows (e.g. main app + detached notes).</summary>
        public void Attach(CoreWebView2 coreWebView)
        {
            if (!activeCores.Contains(coreWebView))
            {
                activeCores.Add(coreWebView);
                if (mainCore == null)
                {
                    mainCore = coreWebView;
                }
                coreWebView.WebMessageReceived += OnWebMessageReceived;
            }
        }

        /// <summary>Detaches a closed window's CoreWebView2 from the bridge.</summary>
        public void Detach(CoreWebView2 coreWebView)
        {
            if (activeCores.Remove(coreWebView))
            {
                try
                {
                    coreWebView.WebMessageReceived -= OnWebMessageReceived;
                }
                catch
                {
                    // Ignore if already disposed
                }
                if (mainCore == coreWebView)
                {
                    mainCore = activeCores.Count > 0 ? activeCores[0] : null;
                }
                if (activeCores.Count == 0)
                {
                    ReactReady = false;
                }
            }
        }

        /// <summary>Posts an unsolicited event to all attached React windows: { kind: "push", type, payload }.</summary>
        public void Push(string type, object? payload)
        {
            if (activeCores.Count == 0)
            {
                return;
            }
            string json = JsonSerializer.Serialize(new IpcPush { Type = type, Payload = payload }, IpcJson.Options);
            for (int i = activeCores.Count - 1; i >= 0; i--)
            {
                var c = activeCores[i];
                try
                {
                    c.PostWebMessageAsJson(json);
                }
                catch
                {
                    activeCores.RemoveAt(i);
                }
            }
        }

        // ---- Transport ----

        private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            var senderCore = sender as CoreWebView2 ?? mainCore;
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
                Post(senderCore, IpcResponse.Failure(request.Id, ex.Message));
                return;
            }
            Post(senderCore, IpcResponse.Success(request.Id, result));
        }

        private static void Post(CoreWebView2? target, IpcResponse response)
        {
            try
            {
                target?.PostWebMessageAsJson(JsonSerializer.Serialize(response, IpcJson.Options));
            }
            catch
            {
                // Core might have closed
            }
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
                // Re-scheduling a Missed (Expired) task revives it: without this,
                // editing a missed task from History to a new future time would
                // leave it Expired — dead in the archive, never to fire again.
                if (rescheduled && task.GetState() == TaskState.Expired)
                {
                    task.SetState(TaskState.Scheduled);
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

            // ---- Notes (one .txt per note on disk; storage is the source of truth) ----

            handlers["getNotes"] = _ => NoteStorageService.Load();

            handlers["createNote"] = payload =>
            {
                var note = new NoteItem
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Title = GetString(payload, "title") ?? "Untitled",
                    Content = GetString(payload, "content") ?? string.Empty,
                    UpdatedAt = clock.Now,
                };
                NoteStorageService.Save(note);
                Push("notesChanged", NoteStorageService.Load());
                return note;
            };

            handlers["updateNote"] = payload =>
            {
                NoteItem note = FindNote(payload);
                if (TryGetString(payload, "title", out string? title) && title != null)
                {
                    note.Title = title;
                }
                if (TryGetString(payload, "content", out string? content) && content != null)
                {
                    note.Content = content;
                }
                note.UpdatedAt = clock.Now;
                NoteStorageService.Save(note);
                Push("notesChanged", NoteStorageService.Load());
                return note;
            };

            handlers["deleteNote"] = payload =>
            {
                NoteItem note = FindNote(payload);
                NoteStorageService.Delete(note.Id);
                Push("notesChanged", NoteStorageService.Load());
                return null;
            };

            // Notepad-style "Save as…". The editor's buffer is already plain text
            // carrying Markdown emphasis (**bold**, *italic*, "- " bullets), so the
            // export is the buffer verbatim — it opens correctly in Notepad and can
            // be pasted back in without losing its structure. The payload carries the
            // live buffer rather than an id so unsaved edits are included.
            handlers["exportNote"] = payload =>
            {
                string title = GetString(payload, "title") ?? "Untitled";
                string content = GetString(payload, "content") ?? string.Empty;
                using var dialog = new SaveFileDialog
                {
                    Title = "Save note as",
                    Filter = "Text documents (*.txt)|*.txt|All files (*.*)",
                    DefaultExt = "txt",
                    AddExtension = true,
                    FileName = $"{SafeFileName(title)}.txt",
                };
                IWin32Window? owner = Form.ActiveForm;
                DialogResult result = owner != null ? dialog.ShowDialog(owner) : dialog.ShowDialog();
                if (result != DialogResult.OK)
                {
                    return null;
                }
                File.WriteAllText(dialog.FileName, ToWindowsNewlines(content));
                return new { path = dialog.FileName };
            };

            handlers["detachNote"] = payload =>
            {
                string? id = GetString(payload, "id");
                if (string.IsNullOrEmpty(id))
                {
                    throw new InvalidOperationException("Note id is required.");
                }
                var note = NoteStorageService.Load().FirstOrDefault(n => n.Id == id);
                string title = note?.Title ?? "Note";
                var form = new DetachedNoteForm(this, id, title);
                form.Show();
                return new { ok = true };
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
                if (SettingsService.IsRunOnStartupEnabled())
                {
                    SettingsService.SetRunOnStartup(true, settings.StartWithWindowsHidden);
                }
                theme.ApplyAccent(settings.AccentColor); // keep the host palette state in sync
                Push("settingsChanged", settings);
                return settings;
            };

            handlers["getAccentLibrary"] = _ =>
                AccentCatalog.Options.Select(AccentDto.From).ToArray();

            handlers["getStartupEnabled"] = _ => SettingsService.IsRunOnStartupEnabled();

            handlers["setStartupEnabled"] = payload =>
            {
                var cur = SettingsService.Load();
                SettingsService.SetRunOnStartup(GetBool(payload, "enabled"), cur.StartWithWindowsHidden);
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
                        // Rule 3: Single audio source - starting ambient stops music
                        if (musicPlayback.IsPlaying || musicPlayback.IsPaused)
                        {
                            musicPlayback.Stop();
                            Push("musicTrackChanged", null);
                        }
                        string displayName = GetString(payload, "displayName") ?? Path.GetFileNameWithoutExtension(path);
                        bool started = audio.PlayAmbientFile(path, displayName);
                        Push("ambientChanged", new { playing = audio.IsAmbientPlaying, volume = audio.AmbientVolume, name = audio.ActiveAmbientName });
                        return new { ok = started };
                    case "stop":
                        audio.StopAmbientAudio();
                        Push("ambientChanged", new { playing = false, volume = audio.AmbientVolume, name = (string?)null });
                        return new { ok = true };
                    case "volume":
                        int vol = Math.Clamp(GetInt(payload, "volume") ?? audio.AmbientVolume, 0, 100);
                        audio.AmbientVolume = vol;
                        audio.ApplyAmbientVolume();
                        musicPlayback.Volume = vol;
                        AppSettings settings = SettingsService.Load();
                        if (settings.MasterVolume != vol)
                        {
                            settings.MasterVolume = vol;
                            SettingsService.Save(settings);
                        }
                        Push("ambientChanged", new { playing = audio.IsAmbientPlaying, volume = audio.AmbientVolume, name = audio.ActiveAmbientName });
                        return new { volume = audio.AmbientVolume };
                    default:
                        throw new InvalidOperationException("Unknown ambient op.");
                }
            };

            // ---- Music Library & Playback (Phase 1, 2 & 4) ----

            handlers["playTrack"] = payload =>
            {
                string? id = GetString(payload, "id");
                if (string.IsNullOrWhiteSpace(id))
                {
                    throw new InvalidOperationException("Track ID is required.");
                }

                // Rule 3: Single audio source across both engines - starting music stops ambient
                audio.StopAmbientAudio();
                Push("ambientChanged", new { playing = false, volume = audio.AmbientVolume, name = (string?)null });

                List<string>? queue = null;
                if (payload.HasValue && payload.Value.TryGetProperty("queue", out JsonElement queueProp) && queueProp.ValueKind == JsonValueKind.Array)
                {
                    queue = new List<string>();
                    foreach (var item in queueProp.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String)
                        {
                            var qId = item.GetString();
                            if (!string.IsNullOrEmpty(qId)) queue.Add(qId);
                        }
                    }
                }

                bool? shuffleParam = null;
                if (TryGetBool(payload, "shuffle", out bool shuf))
                {
                    shuffleParam = shuf;
                }

                bool ok = musicPlayback.PlayTrack(id, queue, shuffleParam);
                return new { ok, track = musicPlayback.CurrentTrack };
            };

            handlers["musicTransport"] = payload =>
            {
                string? action = GetString(payload, "action") ?? GetString(payload, "op");
                switch (action?.ToLowerInvariant())
                {
                    case "pause":
                        musicPlayback.Pause();
                        return new { ok = true, state = "paused" };

                    case "resume":
                        // Rule 3: Single audio source - resuming music stops ambient
                        audio.StopAmbientAudio();
                        Push("ambientChanged", new { playing = false, volume = audio.AmbientVolume, name = (string?)null });
                        musicPlayback.Resume();
                        return new { ok = true, state = "playing" };

                    case "stop":
                        musicPlayback.Stop();
                        Push("musicTrackChanged", null);
                        return new { ok = true, state = "stopped" };

                    case "seek":
                        int pos = GetInt(payload, "positionMs") ?? GetInt(payload, "position") ?? 0;
                        musicPlayback.Seek(pos);
                        int currentPos = musicPlayback.GetPositionMs();
                        int duration = musicPlayback.GetDurationMs();
                        Push("musicProgress", new { positionMs = currentPos, durationMs = duration });
                        return new { ok = true, positionMs = currentPos };

                    case "next":
                        bool nextOk = musicPlayback.Next(autoAdvance: false);
                        return new { ok = nextOk, track = musicPlayback.CurrentTrack };

                    case "previous":
                        bool prevOk = musicPlayback.Previous();
                        return new { ok = prevOk, track = musicPlayback.CurrentTrack };

                    case "shuffle":
                        if (TryGetBool(payload, "enabled", out bool enableShuffle))
                        {
                            musicPlayback.SetShuffle(enableShuffle);
                        }
                        else
                        {
                            musicPlayback.SetShuffle(!musicPlayback.Shuffle);
                        }
                        return new { ok = true, shuffle = musicPlayback.Shuffle };

                    case "repeat":
                        string? mode = GetString(payload, "mode");
                        if (string.IsNullOrEmpty(mode))
                        {
                            mode = musicPlayback.RepeatMode switch
                            {
                                "off" => "all",
                                "all" => "one",
                                _ => "off"
                            };
                        }
                        musicPlayback.SetRepeatMode(mode);
                        return new { ok = true, repeatMode = musicPlayback.RepeatMode };

                    default:
                        throw new InvalidOperationException($"Unknown music transport action '{action}'.");
                }
            };

            handlers["browseAmbientFolder"] = _ =>
            {
                using var dialog = new FolderBrowserDialog
                {
                    Description = "Select a music folder to add to your library",
                    UseDescriptionForTitle = true,
                };
                IWin32Window? owner = Form.ActiveForm;
                DialogResult result = owner != null ? dialog.ShowDialog(owner) : dialog.ShowDialog();
                if (result != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
                {
                    return null;
                }

                string folderPath = dialog.SelectedPath;
                var folder = musicLibrary.AddFolderAsync(folderPath).GetAwaiter().GetResult();

                AppSettings settings = SettingsService.Load();
                if (!settings.MusicFolders.Any(f => string.Equals(f, folder.Path, StringComparison.OrdinalIgnoreCase)))
                {
                    settings.MusicFolders.Add(folder.Path);
                    SettingsService.Save(settings);
                }

                return new
                {
                    folder,
                    tracks = musicLibrary.Tracks
                };
            };

            handlers["removeMusicFolder"] = payload =>
            {
                string? path = GetString(payload, "path");
                if (string.IsNullOrWhiteSpace(path))
                {
                    throw new InvalidOperationException("Folder path is required.");
                }

                musicLibrary.RemoveFolder(path);

                AppSettings settings = SettingsService.Load();
                settings.MusicFolders.RemoveAll(f => string.Equals(f, path, StringComparison.OrdinalIgnoreCase));
                SettingsService.Save(settings);

                return new { ok = true };
            };

            handlers["rescanMusicFolder"] = payload =>
            {
                string? path = GetString(payload, "path");
                _ = musicLibrary.RescanAsync(path);
                return new { ok = true };
            };

            handlers["getMusicLibrary"] = _ =>
            {
                musicLibrary.StartArtPassIfNeeded();
                return new
                {
                    folders = musicLibrary.Folders,
                    tracks = musicLibrary.Tracks
                };
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
            notes = NoteStorageService.Load(),
            music = new
            {
                folders = musicLibrary.Folders,
                tracks = musicLibrary.Tracks,
                currentTrack = musicPlayback.CurrentTrack,
                state = musicPlayback.IsPlaying ? "playing" : (musicPlayback.IsPaused ? "paused" : "stopped"),
                positionMs = musicPlayback.GetPositionMs(),
                durationMs = musicPlayback.GetDurationMs(),
                volume = musicPlayback.Volume,
                queue = musicPlayback.GetQueueState()
            },
        };

        /// <summary>A leaner snapshot for on-demand refreshes.</summary>
        private object StateSnapshot() => new
        {
            now = clock.Now,
            tasks,
            nextUp = scheduler.NextUp,
            notes = NoteStorageService.Load(),
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

        private static NoteItem FindNote(JsonElement? payload)
        {
            string? id = GetString(payload, "id");
            NoteItem? note = NoteStorageService.Load().FirstOrDefault(n => n.Id == id);
            return note ?? throw new InvalidOperationException("Note not found.");
        }

        /// <summary>Turns a note title into a usable default file name for the Save-As
        /// dialog — invalid characters collapse to "_", and a title that reduces to
        /// nothing falls back to "Note".</summary>
        private static string SafeFileName(string title)
        {
            string name = string.Join(
                "_",
                title.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries))
                .Trim()
                .TrimEnd('.'); // Windows rejects a trailing dot
            if (name.Length == 0)
            {
                name = "Note";
            }
            return name.Length > 120 ? name[..120] : name;
        }

        /// <summary>Normalizes the browser's "\n" buffer to CRLF so the exported file
        /// is a well-formed Windows text file for every editor, not just Notepad.</summary>
        private static string ToWindowsNewlines(string text) =>
            text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "\r\n");

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
