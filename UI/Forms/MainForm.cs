using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Guna.UI2.WinForms;
using VibeAlarm.Models;
using VibeAlarm.Services;
using VibeAlarm.UI.Controls;
using VibeAlarm.UI.Theming;
// 'Appearance' also names a WinForms ButtonBase enum — bind the bare name to the
// theming accessor (settings-derived radii, density, task-row height).
using Appearance = VibeAlarm.UI.Theming.Appearance;

namespace VibeAlarm.UI.Forms
{
    public partial class MainForm : Form
    {
        private const string PlaceholderTaskName = "What needs to be done?";
        private const string PlaceholderSearch = "Search tasks...";

        private static readonly string[] WeekDays =
        {
            "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"
        };

        private static readonly string[] ShortWeekDays =
        {
            "SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT"
        };

        private readonly List<TaskItem> masterTaskList = new();
        private readonly ThemeService themeService = ThemeService.Shared;
        private readonly AudioService audioService = new();
        private readonly Clock clock = new();
        private readonly TimeService timeService;
        private readonly SchedulerService scheduler;
        private string activeCalendarDay = DateTime.Now.DayOfWeek.ToString();
        private DateTime activeCalendarDate = DateTime.Today;
        private DateTime displayedCalendarMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        private string activeView = "Tasks";
        private string searchFilterQuery = string.Empty;

        // Dynamic Theme Preset properties
        private ThemePreset currentTheme = null!;

        private Color PrimaryBg => currentTheme.PrimaryBg;
        private Color SecondaryBg => currentTheme.SecondaryBg;
        private Color SidebarBg => currentTheme.SidebarBg;
        private Color CardBgColor => currentTheme.CardBgColor;
        private Color CardHoverBg => currentTheme.CardHoverBg;
        private Color AccentColor => currentTheme.AccentColor;
        private Color AccentTintColor => currentTheme.AccentTintColor;
        private Color TextColor => currentTheme.TextColor;
        private Color MutedTextColor => currentTheme.MutedTextColor;
        private Color BorderColor => currentTheme.BorderColor;

        /// <summary>Secondary ink — one tonal step brighter than muted, still clearly
        /// subordinate to the title: metadata must read at a glance, not require squinting
        /// (§12 task-row feedback: 8.5pt full-muted read as an afterthought).</summary>
        private Color SecondaryTextColor =>
            Color.FromArgb(
                (MutedTextColor.R + TextColor.R) / 2,
                (MutedTextColor.G + TextColor.G) / 2,
                (MutedTextColor.B + TextColor.B) / 2);

        private System.Windows.Forms.Timer backgroundAlarmTicker = null!;
        private TrayService tray = null!;
        private bool userInitiatedExit;

        // Layout Shell Containers
        private Guna2Panel sidebarPanel = null!;
        private Panel mainContainer = null!;
        private TableLayoutPanel headerPanel = null!;
        private Panel contentPanel = null!;

        // Header Input Items
        private Guna2TextBox txtSearch = null!;
        private Guna2Button btnNewTask = null!;
        // Circular floating "New Task" action on the Tasks view (replaces the header button
        // there). Parented to mainContainer — NOT the AutoScroll content host — so it never
        // scrolls with the list and never falls behind rebuilt list content.
        private FloatingActionButton? taskFab;

        // Navigation Group Links
        private Guna2Button btnDashboard = null!;
        private Guna2Button btnTasks = null!;
        private Guna2Button btnCalendar = null!;
        private Guna2Button btnAmbient = null!;
        private Guna2Button btnSettings = null!;
        private static readonly Dictionary<string, IconKind> NavIcons = new()
        {
            ["Dashboard"] = IconKind.Dashboard,
            ["Tasks"] = IconKind.Tasks,
            ["Calendar"] = IconKind.Calendar,
            // Ambient renders the SoundWave vector (waveform-path.svg) — the view is about
            // background SOUND, and the speaker glyph (Volume) is its degraded fallback.
            ["Ambient"] = IconKind.SoundWave,
            // Settings renders the UserPreferences vector (user-skill-gear.svg) — the standard
            // gear glyph (IconKind.Settings) is its degraded fallback.
            ["Settings"] = IconKind.UserPreferences
        };

        // Interactive Labels — lblGreeting doubles as the per-view page title (§4 hierarchy).
        private Label lblGreeting = null!;
        private Label lblHeaderSubtitle = null!;
        private Label lblSidebarRemaining = null!;
        private Label lblSidebarPercent = null!;
        private Panel sidebarProgressTrack = null!;
        private Panel sidebarProgressFill = null!;

        // Persistent controls stored for live theme application
        private Label lblAppLogo = null!;
        private Guna2Panel sidebarProgressCard = null!;
        private Label lblSidebarIcon = null!;
        private Label lblSidebarEncouragement = null!;
        private FlowLayoutPanel navFlow = null!;

        // Dynamic Panels
        private FlowLayoutPanel taskListPanel = null!;
        private FlowLayoutPanel dashboardFocusPanel = null!;
        private FlowLayoutPanel dashboardUpcomingPanel = null!;
        private TableLayoutPanel calendarStripMatrix = null!;
        private FlowLayoutPanel calendarListPanel = null!;
        private Label lblCalendarProgress = null!;
        private Label lblCalendarMonth = null!;
        private Label lblLiveClock = null!;
        private Label lblNextUpTime = null!;
        private Label lblNextUpName = null!;
        private Label lblNextUpCountdown = null!;
        private Label lblAmbientNowPlaying = null!;
        private TrackBar ambientVolumeSlider = null!;
        private int ambientVolume = 70;

        public MainForm()
        {
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            this.UpdateStyles();

            // Snapshot the persisted appearance settings (radius/density/transparency) before
            // any control is constructed — the factory reads them for every surface it builds.
            Appearance.Apply(SettingsService.Load());
            currentTheme = themeService.ActivateEffectiveFromSettings();
            AudioService.EnsureDefaultAmbientSounds(Path.Combine(AppContext.BaseDirectory, "Assets"));
            masterTaskList.AddRange(TaskStorageService.Load());

            // Restore navigation state (last view + calendar month) so returning to the app
            // doesn't reset where the user left off.
            AppSettings restored = SettingsService.Load();
            if (!string.IsNullOrWhiteSpace(restored.LastActiveView))
            {
                activeView = restored.LastActiveView;
            }
            if (DateTime.TryParseExact(
                    restored.LastViewedCalendarMonth, "yyyy-MM", null,
                    System.Globalization.DateTimeStyles.None, out DateTime lastMonth))
            {
                displayedCalendarMonth = new DateTime(lastMonth.Year, lastMonth.Month, 1);
            }

            // One authoritative time source and one event-driven scheduler, both backed by the
            // same clock. The scheduler references the live masterTaskList so runtime add/remove
            // is picked up without resync.
            timeService = new TimeService(clock);
            scheduler = new SchedulerService(clock, masterTaskList);
            scheduler.ReplaceTasks(); // deterministic state reconstruction after restart

            InitializeComponent();
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            BuildDesktopInterface();
            ApplyTheme(currentTheme);

            InitializeServices();
            audioService.AmbientPlayingChanged += OnAmbientPlayingChanged;

            InitializeTray();
            ApplyStoredBackground();

            // Global quick-switch command palette (Ctrl+K).
            this.KeyPreview = true;
            this.KeyDown += OnGlobalKeyDown;

            // Follow the Windows theme while the user is in "System" mode: re-resolve the
            // effective preset (and re-render) when the OS app theme preference flips.
            Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnOsPreferenceChanged;
        }

        /// <summary>OS theme (or other personalization) changed — re-resolve when in System mode.</summary>
        private void OnOsPreferenceChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
        {
            if (e.Category != Microsoft.Win32.UserPreferenceCategory.General &&
                e.Category != Microsoft.Win32.UserPreferenceCategory.VisualStyle)
            {
                return;
            }

            SystemThemeProvider.Refresh();
            if (!SystemThemeProvider.IsSystemMode(SettingsService.LoadThemeName()))
            {
                return;
            }

            ThemePreset effective = themeService.ResolveEffective(SettingsService.LoadThemeName());
            if (effective.Name != currentTheme.Name)
            {
                BeginInvoke(() => ApplyTheme(effective));
            }
        }

        private void OnGlobalKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.K && e.Control)
            {
                OpenCommandPalette();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.N && e.Control)
            {
                ExecuteModalTaskCreationDialogue();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        private void OpenCommandPalette()
        {
            using (CommandPaletteDialog palette = new CommandPaletteDialog(masterTaskList))
            {
                if (palette.ShowDialog(this) == DialogResult.OK && palette.Result != null)
                {
                    var r = palette.Result;
                    switch (r.Action)
                    {
                        case CommandPaletteDialog.PaletteAction.NewTask:
                            ExecuteModalTaskCreationDialogue();
                            break;
                        case CommandPaletteDialog.PaletteAction.Search:
                            txtSearch.Focus();
                            txtSearch.SelectAll();
                            break;
                        case CommandPaletteDialog.PaletteAction.Navigate:
                            if (r.Payload is TaskItem task)
                            {
                                // Jump the calendar to the task's scheduled day.
                                activeCalendarDate = GetTaskDate(task);
                                activeCalendarDay = activeCalendarDate.DayOfWeek.ToString();
                                displayedCalendarMonth = new DateTime(activeCalendarDate.Year, activeCalendarDate.Month, 1);
                                activeView = "Calendar";
                                RenderActiveView();
                            }
                            else if (r.Payload is string view)
                            {
                                activeView = view;
                                RenderActiveView();
                            }
                            break;
                    }
                }
            }
        }

        /// <summary>Tray icon + hide-not-close behavior. Restore re-runs the resume catch-up.</summary>
        private void InitializeTray()
        {
            tray = new TrayService(Icon, "VibeAlarm");
            tray.OpenRequested += () =>
            {
                if (!Visible)
                {
                    Show();
                }
                if (WindowState == FormWindowState.Minimized)
                {
                    WindowState = FormWindowState.Normal;
                }
                Activate();
                tray.MarkVisible();
                timeService.OnResumed(); // catch up any time elapppped while hidden
            };
            tray.ExitRequested += () => { userInitiatedExit = true; Close(); };
            tray.ShowTray();
        }

        /// <summary>Applies the persisted custom background (if any) behind the content surfaces.</summary>
        private void ApplyStoredBackground()
        {
            AppSettings settings = SettingsService.Load();
            if (string.IsNullOrWhiteSpace(settings.BackgroundImagePath) ||
                !File.Exists(settings.BackgroundImagePath))
            {
                // No stored image: clear any previously applied layer (e.g. after "Remove").
                Image? stale = this.BackgroundImage;
                this.BackgroundImage = null;
                stale?.Dispose();
                return;
            }

            Bitmap? bg = BackgroundService.LoadBackground(settings.ApplyMonochromeFilterToBackground, settings.BackgroundOpacity);
            if (bg == null)
            {
                return;
            }

            // The form now owns the new bitmap for its lifetime: never `using` it here, or the
            // pending repaint draws against a disposed image and throws GDI+ "Parameter is not
            // valid." Dispose the PREVIOUS background instead, then swap.
            // Stretch, not Zoom: Zoom letterboxes a non-matching aspect ratio, leaving hard
            // edges where the image stops and the scrim color takes over. On a BLURRED image,
            // Stretch's aspect distortion is invisible — this is the WinForms equivalent of
            // CSS background-size: cover.
            Image? previous = this.BackgroundImage;
            this.BackgroundImageLayout = ImageLayout.Stretch;
            this.BackgroundImage = bg;
            previous?.Dispose();
        }

        private void OnAmbientPlayingChanged(string? displayName)
        {
            if (lblAmbientNowPlaying == null || lblAmbientNowPlaying.IsDisposed)
            {
                return;
            }

            lblAmbientNowPlaying.Text = displayName == null ? "Nothing playing" : $"Playing: {displayName}";
        }

        private void BuildDesktopInterface()
        {
            Text = "VibeAlarm";
            // Build provenance: the exe's own write time in the title bar. A stale binary is
            // then instantly distinguishable from a fresh build — editing source files does NOT
            // change an already-built exe, and "why don't I see the update" becomes checkable.
            try
            {
                string? exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(exePath))
                {
                    Text = $"VibeAlarm — built {File.GetLastWriteTime(exePath):MMM d, HH:mm}";
                }
            }
            catch
            {
                // Module info unavailable (edge hosting cases) — keep the plain title.
            }
            ClientSize = new Size(1280, 760);
            MinimumSize = new Size(1000, 650);
            FormBorderStyle = FormBorderStyle.Sizable;
            // Explicit: the window is freely resizable AND maximizable (no MaximumSize lock).
            MaximizeBox = true;
            StartPosition = FormStartPosition.CenterScreen;
            Font = VibeAlarmPalette.Body(10F);
            BackColor = PrimaryBg;

            // Base Layout Splits — glass sidebar (Guna renders ARGB fills; a plain Panel cannot
            // alpha-blend) + main workspace.
            sidebarPanel = new Guna2Panel
            {
                Dock = DockStyle.Left,
                Width = VibeAlarmPalette.SidebarWidth,
                FillColor = GlassSurface.SidebarFill(currentTheme),
                BorderThickness = 0,
                BorderRadius = 0,
                Padding = new Padding(0, DesignTokens.Spacing.Lg, 0, DesignTokens.Spacing.Md)
            };
            sidebarPanel.ShadowDecoration.Enabled = false;
            sidebarPanel.Paint += (s, e) =>
            {
                using Pen hairline = new Pen(GlassSurface.CardBorder(currentTheme), 1);
                e.Graphics.DrawLine(hairline, sidebarPanel.Width - 1, 0, sidebarPanel.Width - 1, sidebarPanel.Height);
            };
            // Transparent, NOT PrimaryBg: the form's BackgroundImage (the global background
            // layer) must show through the whole workspace — an opaque container here is what
            // used to hide it outside the glass cards. The form's own BackColor (set by
            // ApplyTheme) is the scrim the image blends over.
            mainContainer = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Padding = new Padding(DesignTokens.Spacing.Xl, DesignTokens.Spacing.Md, DesignTokens.Spacing.Xl, DesignTokens.Spacing.Md) };

            Controls.Add(mainContainer);
            Controls.Add(sidebarPanel);

            BuildSidebarNavigation();
            BuildMainWorkspaceLayout();
        }

        private void BuildSidebarNavigation()
        {
            // §6: spacing (not borders) separates nav groups. A top-down flow positions the
            // items; the progress card is pinned to the sidebar's bottom edge.
            navFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };

            // Fill docks into the remaining space first; the progress card host takes the
            // bottom edge after (same dock order the main container uses).
            sidebarPanel.Controls.Add(navFlow);
            BuildSidebarProgressCard();

            lblAppLogo = new Label
            {
                Text = "VibeAlarm",
                AutoSize = false,
                Size = new Size(VibeAlarmPalette.SidebarWidth - 48, 34),
                Margin = new Padding(24, 2, 24, DesignTokens.Spacing.Md),
                Font = VibeAlarmPalette.Display(17F, FontStyle.Bold),
                ForeColor = TextColor,
                BackColor = Color.Transparent
            };
            navFlow.Controls.Add(lblAppLogo);

            btnDashboard = CreateSidebarButton("Dashboard");
            btnTasks = CreateSidebarButton("Tasks");
            btnCalendar = CreateSidebarButton("Calendar");
            navFlow.Controls.AddRange(new Control[] { btnDashboard, btnTasks, btnCalendar });

            navFlow.Controls.Add(CreateSidebarDivider());

            btnAmbient = CreateSidebarButton("Ambient");
            btnSettings = CreateSidebarButton("Settings");
            navFlow.Controls.AddRange(new Control[] { btnAmbient, btnSettings });
        }

        /// <summary>Hairline rule between the main nav group and the bottom group — the gap is
        /// intentional structure, not decoration (§6).</summary>
        private Control CreateSidebarDivider()
        {
            Panel divider = new Panel
            {
                Size = new Size(VibeAlarmPalette.SidebarWidth - 48, 9),
                Margin = new Padding(24, DesignTokens.Spacing.Sm, 24, DesignTokens.Spacing.Md),
                BackColor = Color.Transparent
            };
            divider.Paint += (s, e) =>
            {
                using Pen hairline = new Pen(GlassSurface.CardBorder(currentTheme), 1);
                e.Graphics.DrawLine(hairline, 0, 4, divider.Width, 4);
            };
            return divider;
        }

        private Guna2Button CreateSidebarButton(string viewKey)
        {
            // §6/§14.3: icon + label, full-height rounded pill for the active row. The glyph and
            // caption live on the button itself (Image + Text) rather than as child controls: a
            // child label over a Guna2Button swallows the mouse input, killing Click entirely.
            Guna2Button btn = new Guna2Button
            {
                Tag = viewKey,
                Size = new Size(VibeAlarmPalette.SidebarWidth - 32, 40),
                Margin = new Padding(16, 1, 16, 1),
                FillColor = Color.Transparent,
                ForeColor = MutedTextColor,
                BorderThickness = 0,
                BorderRadius = 20, // <40 height / 2> = pill
                Animated = Appearance.Animations,
                Cursor = Cursors.Hand,
                Font = VibeAlarmPalette.Body(10F),
                Text = viewKey,
                TextAlign = HorizontalAlignment.Left,
                TextOffset = new Point(46, 0), // clear of the 20px glyph slot + gap
                ImageAlign = HorizontalAlignment.Left,
                ImageOffset = new Point(14, 0),
                ImageSize = new Size(20, 20),
                AccessibleName = $"{viewKey} view"
            };
            btn.HoverState.FillColor = currentTheme.CardHoverBg;
            btn.HoverState.ForeColor = currentTheme.TextColor;
            btn.Image = IconSet.Render(NavIcons[viewKey], MutedTextColor, 20);

            btn.Click += (s, e) =>
            {
                activeView = viewKey;
                RenderActiveView();
            };

            return btn;
        }

        private void BuildSidebarProgressCard()
        {
            // Glass card pinned to the sidebar bottom: the factory computes the translucent
            // fill/border; a transparent host provides the 16px side insets (docking ignores
            // Margin, so the inset comes from the host's Padding).
            sidebarProgressCard = UIControlFactory.CreateCardPanel(preset: currentTheme);
            sidebarProgressCard.Dock = DockStyle.Fill;

            lblSidebarIcon = CreateLabel("●", new Point(16, 16), new Size(24, 24), 15F, FontStyle.Bold, TextColor);
            lblSidebarIcon.Font = new Font("Segoe UI Symbol", 15F, FontStyle.Regular);
            lblSidebarRemaining = CreateLabel("0 Tasks Remaining", new Point(16, 46), new Size(156, 22), 9.5F, FontStyle.Bold, TextColor);
            lblSidebarEncouragement = CreateLabel("Keep going!", new Point(16, 68), new Size(156, 18), 8.5F, FontStyle.Regular, MutedTextColor);

            sidebarProgressTrack = new Panel { Location = new Point(16, 104), Size = new Size(116, 6), BackColor = currentTheme.BorderColor };
            sidebarProgressFill = new Panel { Location = new Point(0, 0), Size = new Size(0, 6), BackColor = TextColor };
            lblSidebarPercent = CreateLabel("0%", new Point(146, 96), new Size(32, 20), 9F, FontStyle.Bold, MutedTextColor);

            sidebarProgressTrack.Controls.Add(sidebarProgressFill);
            sidebarProgressCard.Controls.AddRange(new Control[] { lblSidebarIcon, lblSidebarRemaining, lblSidebarEncouragement, sidebarProgressTrack, lblSidebarPercent });

            Panel bottomHost = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 156,
                Padding = new Padding(16, DesignTokens.Spacing.Sm, 16, DesignTokens.Spacing.Xs),
                BackColor = Color.Transparent
            };
            bottomHost.Controls.Add(sidebarProgressCard);
            sidebarPanel.Controls.Add(bottomHost);
        }

        private void BuildMainWorkspaceLayout()
        {
            // §8: header laid out with containers — page title + description on the left,
            // search + primary action on the right. No coordinates.
            headerPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 92,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            headerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            headerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            contentPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, DesignTokens.Spacing.Sm, 0, 0), BackColor = Color.Transparent, AutoScroll = true, AutoScrollMargin = new Size(0, DesignTokens.Spacing.Lg) };
            // Native scrollbar follows the active theme (dark mode = Windows' dark scrollbar,
            // light = default) — the persistent scroll host; view panels re-track on render.
            NativeScrollbarTheme.Track(contentPanel);

            // The former global status bar is gone (persistent overlap issues; snooze moved to
            // the alarm popup, complete stays on the task row). Header Top + content Fill only.
            mainContainer.Controls.Add(contentPanel);
            mainContainer.Controls.Add(headerPanel);

            // Floating "New Task" action for the Tasks view — a circular FAB pinned over the
            // content area's bottom-right corner. Lives on mainContainer (the NON-scrolling
            // host), never on contentPanel: an anchored child of an AutoScroll panel scrolls
            // with the content and gets clipped off-screen; this way it stays fixed on screen
            // at any scroll position. Visibility is driven per-view by RenderActiveView.
            taskFab = new FloatingActionButton(currentTheme.AccentColor)
            {
                Visible = false
            };
            taskFab.Click += (s, e) => { ExecuteModalTaskCreationDialogue(); };
            mainContainer.Controls.Add(taskFab);
            taskFab.BringToFront(); // always paints above the scrollable list
            // Re-pin on every resize AND re-assert z-order: cheap, and it guarantees the FAB
            // can never end up behind list content after a resize (spec re-test case).
            mainContainer.Resize += (s, e) =>
            {
                if (taskFab is { Visible: true })
                {
                    PositionTaskFab();
                    taskFab.BringToFront();
                }
            };

            // Left: page title (Level 1) over supporting description (Level 2). The title
            // ellipsizes gracefully when the header narrows.
            TableLayoutPanel titleBlock = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            titleBlock.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            titleBlock.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            lblGreeting = new Label
            {
                Text = "Good Evening, Jeptah",
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                Font = VibeAlarmPalette.Display(15F, FontStyle.Bold),
                ForeColor = TextColor,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 8, 0, 2),
                Padding = new Padding(0)
            };
            lblHeaderSubtitle = new Label
            {
                Text = "You have 0 tasks remaining today.",
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                Font = VibeAlarmPalette.Body(9.5F),
                ForeColor = MutedTextColor,
                BackColor = Color.Transparent,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            titleBlock.Controls.Add(lblGreeting, 0, 0);
            titleBlock.Controls.Add(lblHeaderSubtitle, 0, 1);

            // Right: search field + primary action, vertically centered as a group.
            FlowLayoutPanel headerActions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent,
                Margin = new Padding(DesignTokens.Spacing.Md, 0, 0, 0)
            };

            txtSearch = UIControlFactory.CreateSearchBox(PlaceholderSearch, preset: currentTheme);
            txtSearch.Size = new Size(300, 38);
            txtSearch.Margin = new Padding(0, 16, DesignTokens.Spacing.Sm, 0);
            txtSearch.TextChanged += (s, e) =>
            {
                searchFilterQuery = txtSearch.Text.Trim();
                RefreshDataCounters();
            };

            btnNewTask = UIControlFactory.CreatePrimaryButton("New Task", IconKind.Plus, preset: currentTheme);
            btnNewTask.AutoSize = true;
            btnNewTask.MinimumSize = new Size(132, 38);
            btnNewTask.Margin = new Padding(0, 16, 0, 0);
            btnNewTask.AccessibleName = "Create a new task";
            btnNewTask.Click += (s, e) => { ExecuteModalTaskCreationDialogue(); };

            headerActions.Controls.Add(txtSearch);
            headerActions.Controls.Add(btnNewTask);

            headerPanel.Controls.Add(titleBlock, 0, 0);
            headerPanel.Controls.Add(headerActions, 1, 0);

            // §9: when the window narrows the title ellipsizes and the search field shrinks
            // within safe bounds — the primary action never moves or clips.
            headerPanel.Resize += (s, e) => ResizeHeaderSearch();
        }

        /// <summary>Clamps the header search width so the header stays usable at the minimum
        /// window size (1000×650) while giving the field as much room as exists.</summary>
        private void ResizeHeaderSearch()
        {
            if (txtSearch == null || btnNewTask == null || headerPanel == null)
            {
                return;
            }

            const int titleMinimum = 300;
            int chrome = btnNewTask.Width + txtSearch.Margin.Horizontal + btnNewTask.Margin.Horizontal + headerPanel.Margin.Horizontal + 32;
            int available = headerPanel.ClientSize.Width - chrome - titleMinimum;
            txtSearch.Width = Math.Clamp(available, 210, 320);
        }

        private void ExecuteModalTaskCreationDialogue(DateTime? presetDate = null)
        {
            using (TaskCreateDialog dialog = new TaskCreateDialog(clock, presetDate))
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    // Add through the scheduler so a newly created schedule is reconciled
                    // against the current time IMMEDIATELY (defensive: a past schedule can
                    // never enter the running set as "upcoming", even if UI validation were
                    // bypassed). This also persists via StateChanged.
                    scheduler.AddTask(dialog.GeneratedTask);
                    activeCalendarDay = dialog.GeneratedTask.Day;
                    activeCalendarDate = GetTaskDate(dialog.GeneratedTask);
                    displayedCalendarMonth = new DateTime(activeCalendarDate.Year, activeCalendarDate.Month, 1);
                    RefreshDataCounters();
                    TaskStorageService.Save(masterTaskList);
                }
            }
        }

        private void RenderActiveView()
        {
            contentPanel.SuspendLayout();
            contentPanel.Controls.Clear();

            Guna2Button targetBtn = activeView switch
            {
                "Dashboard" => btnDashboard,
                "Calendar" => btnCalendar,
                "Ambient" => btnAmbient,
                "Settings" => btnSettings,
                _ => btnTasks
            };

            foreach (Guna2Button btn in new[] { btnDashboard, btnTasks, btnCalendar, btnAmbient, btnSettings })
            {
                bool isCurrent = btn == targetBtn;
                if (btn.Tag is not string key)
                {
                    continue;
                }
                // §14.3 pill: active = accent-tinted fill + accent glyph/ink; idle = transparent.
                btn.FillColor = isCurrent ? currentTheme.AccentTintColor : Color.Transparent;
                btn.ForeColor = isCurrent ? currentTheme.TextColor : MutedTextColor;
                // NOTE: the previous Image is NOT disposed — IconSet.Render now returns
                // SHARED cached bitmaps (see IconSet.Render's ownership contract).
                btn.Image = IconSet.Render(NavIcons[key], isCurrent ? currentTheme.AccentColor : MutedTextColor, 20);
            }

            switch (activeView)
            {
                case "Calendar":
                    RenderCalendarView();
                    break;
                case "Dashboard":
                    RenderDashboardView();
                    break;
                case "Ambient":
                    RenderAmbientView();
                    break;
                case "Settings":
                    RenderSettingsView();
                    break;
                default:
                    RenderTaskView();
                    break;
            }

            // The Tasks view replaces the header's "New Task" button with the floating
            // action button; every other view keeps the header button (their only creation
            // affordance — Calendar also has per-day "Add task").
            bool tasksViewActive = activeView == "Tasks";
            if (taskFab != null)
            {
                taskFab.Visible = tasksViewActive;
                if (tasksViewActive)
                {
                    PositionTaskFab();
                    taskFab.BringToFront(); // list content was (re)added above
                }
            }
            if (btnNewTask != null)
            {
                btnNewTask.Visible = !tasksViewActive;
            }

            contentPanel.ResumeLayout();
            RefreshDataCounters();
            PersistNavigationState();
        }

        private void PersistNavigationState()
        {
            try
            {
                AppSettings s = SettingsService.Load();
                s.LastActiveView = activeView;
                s.LastViewedCalendarMonth = displayedCalendarMonth.ToString("yyyy-MM");
                SettingsService.Save(s);
            }
            catch
            {
                // Persistence is best-effort here; never block navigation on a write failure.
            }
        }

        private void RenderTaskView()
        {
            UpdateHeaderContext();

            // §11: the user primarily sees their tasks — no dashboard statistics here. Section
            // headings and cards are appended by BindTaskListView into one scrollable flow.
            taskListPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Margin = new Padding(0)
            };
            contentPanel.Controls.Add(taskListPanel);
            BindRowWidthToHost(taskListPanel);
            // Row hosts paint on scroll/hover — flicker there reads as lag or overlap.
            EnableFlickerFreePaint(taskListPanel);
        }

        /// <summary>Pins the FAB to the content area's bottom-right corner. Recomputed on
        /// every resize and every view switch because the FAB is positioned by code, not by
        /// Anchor — its parent is the non-scrolling mainContainer, and an Anchor inside the
        /// AutoScroll content host would scroll it away with the list.</summary>
        private void PositionTaskFab()
        {
            if (taskFab == null || contentPanel == null)
            {
                return;
            }
            taskFab.Location = new Point(
                Math.Max(contentPanel.Left, contentPanel.Right - taskFab.Width - 28),
                Math.Max(contentPanel.Top, contentPanel.Bottom - taskFab.Height - 28));
        }

        /// <summary>Turns on the flicker-free paint styles (OptimizedDoubleBuffer +
        /// AllPaintingInWmPaint; UserPaint is already set on every container this is used on)
        /// for row/list containers, so repeated paints during scroll and hover never flash.
        /// DoubleBuffered is protected on Control, hence the reflection set.</summary>
        private static void EnableFlickerFreePaint(Control control)
        {
            typeof(Control).GetProperty("DoubleBuffered",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(control, true);
        }

        /// <summary>Keeps full-width rows (section headings, task cards) expanding with the
        /// window: FlowLayoutPanel children keep their own width, so each resize re-clamps every
        /// child to the host's client width. Cheap — no rebuilds.</summary>
        private static void BindRowWidthToHost(FlowLayoutPanel host)
        {
            host.Resize += (s, e) =>
            {
                int width = host.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 2;
                foreach (Control row in host.Controls)
                {
                    // AutoSize inner flows (WrapContents off) manage their own width — leave them alone.
                    if (row is FlowLayoutPanel inner && !inner.WrapContents && inner.AutoSize)
                    {
                        continue;
                    }
                    if (row.Width != width)
                    {
                        row.Width = Math.Max(320, width - row.Margin.Horizontal);
                    }
                }
            };
        }

        /// <summary>Small uppercase section heading with a task count, e.g. "TODAY · 3".</summary>
        private Label CreateSectionHeading(string title, int count)
        {
            Label heading = new Label
            {
                Text = count > 0 ? $"{title} · {count}" : title,
                AutoSize = false,
                Height = 24,
                Font = VibeAlarmPalette.Body(9.5F, FontStyle.Bold),
                ForeColor = MutedTextColor,
                BackColor = Color.Transparent,
                Margin = new Padding(2, DesignTokens.Spacing.Sm, 0, DesignTokens.Spacing.Sm),
                Padding = new Padding(0)
            };
            // Tracked caps — the quiet grouped-list header treatment (Things/Reminders style).
            ApplyLetterSpacing(heading);
            return heading;
        }

        private void RenderCalendarView()
        {
            UpdateHeaderContext();

            // §17–18: one responsive table — month header, Next Up, the 7-column grid, the
            // selected-day summary row, and the day's task list. No fixed widths; the grid and
            // list expand with the window.
            TableLayoutPanel calendarLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            calendarLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            // Fixed-height strips (header / Next Up / summary) use ABSOLUTE rows: a Dock.Fill
            // child inside an AutoSize row does not grow the row — the child paints over the
            // neighboring rows (the "Add task" button clipping into the grid and day list).
            // Absolute row = strip height + the strip's own vertical margins.
            calendarLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));  // month header
            calendarLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 88F + DesignTokens.Spacing.Sm)); // next up (+ gap below)
            calendarLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 65F));  // month grid — the dominant element
            calendarLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50F + 2 * DesignTokens.Spacing.Sm)); // summary (+ gaps)
            calendarLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 35F));  // day list

            // ---- Month header: title left, [<] [Today] [>] right ----
            Guna2Panel monthHeader = UIControlFactory.CreateCardPanel(preset: currentTheme);
            monthHeader.Dock = DockStyle.Fill;
            monthHeader.Margin = new Padding(0); // height comes from the Absolute row
            monthHeader.Padding = new Padding(DesignTokens.Spacing.Md, 0, DesignTokens.Spacing.Md, 0);

            TableLayoutPanel headerRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            headerRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            headerRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            lblCalendarMonth = new Label
            {
                Text = displayedCalendarMonth.ToString("MMMM yyyy"),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Font = VibeAlarmPalette.Display(13F, FontStyle.Bold),
                ForeColor = TextColor,
                BackColor = Color.Transparent,
                Margin = new Padding(DesignTokens.Spacing.Sm, 0, 0, 0)
            };
            headerRow.Controls.Add(lblCalendarMonth, 0, 0);

            FlowLayoutPanel monthNav = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 12, 0, 12)
            };

            Guna2Button btnPrev = UIControlFactory.CreateIconButton(IconKind.ChevronLeft, preset: currentTheme);
            btnPrev.AccessibleName = "Previous month";
            btnPrev.Click += (s, e) =>
            {
                displayedCalendarMonth = displayedCalendarMonth.AddMonths(-1);
                RenderActiveView();
            };

            Guna2Button btnToday = UIControlFactory.CreateSecondaryButton("Today", preset: currentTheme);
            btnToday.Size = new Size(78, 34);
            btnToday.Margin = new Padding(DesignTokens.Spacing.Sm, 1, DesignTokens.Spacing.Sm, 1);
            btnToday.AccessibleName = "Jump to today";
            btnToday.Click += (s, e) =>
            {
                activeCalendarDate = DateTime.Today;
                activeCalendarDay = activeCalendarDate.DayOfWeek.ToString();
                displayedCalendarMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
                RenderActiveView();
            };

            Guna2Button btnNext = UIControlFactory.CreateIconButton(IconKind.ChevronRight, preset: currentTheme);
            btnNext.AccessibleName = "Next month";
            btnNext.Click += (s, e) =>
            {
                displayedCalendarMonth = displayedCalendarMonth.AddMonths(1);
                RenderActiveView();
            };

            monthNav.Controls.AddRange(new Control[] { btnPrev, btnToday, btnNext });
            headerRow.Controls.Add(monthNav, 1, 0);
            monthHeader.Controls.Add(headerRow);
            calendarLayout.Controls.Add(monthHeader, 0, 0);

            // ---- NEXT UP block — nearest upcoming schedule with live countdown. Container
            // layout only (no absolute coordinates); idle state shows real guidance, not dashes.
            Guna2Panel nextUpCard = UIControlFactory.CreateCardPanel(preset: currentTheme);
            nextUpCard.Dock = DockStyle.Fill;
            nextUpCard.Margin = new Padding(0, 0, 0, DesignTokens.Spacing.Sm); // gap to the grid below
            nextUpCard.Padding = new Padding(DesignTokens.Spacing.Lg, DesignTokens.Spacing.Sm, DesignTokens.Spacing.Lg, DesignTokens.Spacing.Sm);

            TableLayoutPanel nextUpLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 3,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            nextUpLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            nextUpLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            nextUpLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // caption
            nextUpLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 62F)); // big time
            nextUpLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 38F)); // task name

            Label lblNextUpTitle = CreateLabel("NEXT UP", Point.Empty, new Size(0, 14), 8.5F, FontStyle.Bold, MutedTextColor);
            lblNextUpTitle.Dock = DockStyle.Fill;
            lblNextUpTitle.TextAlign = ContentAlignment.MiddleLeft;
            lblNextUpTitle.Margin = new Padding(0);

            lblNextUpTime = CreateLabel("--:-- --", Point.Empty, new Size(0, 34), 19F, FontStyle.Bold, TextColor);
            lblNextUpTime.Font = VibeAlarmPalette.Display(19F, FontStyle.Bold);
            lblNextUpTime.Dock = DockStyle.Fill;
            lblNextUpTime.TextAlign = ContentAlignment.MiddleLeft;
            lblNextUpTime.AutoEllipsis = true;
            lblNextUpTime.Margin = new Padding(0);

            lblNextUpName = CreateLabel("No upcoming schedules.", Point.Empty, new Size(0, 18), 9.5F, FontStyle.Regular, MutedTextColor);
            lblNextUpName.Dock = DockStyle.Fill;
            lblNextUpName.TextAlign = ContentAlignment.MiddleLeft;
            lblNextUpName.AutoEllipsis = true;
            lblNextUpName.Margin = new Padding(0);

            lblNextUpCountdown = CreateLabel("—", Point.Empty, new Size(130, 0), 13F, FontStyle.Bold, TextColor);
            lblNextUpCountdown.Font = VibeAlarmPalette.Mono(13F, FontStyle.Bold);
            lblNextUpCountdown.Dock = DockStyle.Fill;
            lblNextUpCountdown.TextAlign = ContentAlignment.MiddleRight;
            lblNextUpCountdown.Margin = new Padding(DesignTokens.Spacing.Md, 0, 0, 0);

            nextUpLayout.Controls.Add(lblNextUpTitle, 0, 0);
            nextUpLayout.Controls.Add(lblNextUpCountdown, 1, 0);
            nextUpLayout.SetRowSpan(lblNextUpCountdown, 3);
            nextUpLayout.Controls.Add(lblNextUpTime, 0, 1);
            nextUpLayout.Controls.Add(lblNextUpName, 0, 2);
            nextUpCard.Controls.Add(nextUpLayout);
            calendarLayout.Controls.Add(nextUpCard, 0, 1);

            // ---- Month grid ----
            calendarStripMatrix = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 7,
                RowCount = 7,
                BackColor = Color.Transparent,
                Margin = new Padding(0, DesignTokens.Spacing.Md, 0, DesignTokens.Spacing.Sm)
            };

            for (int i = 0; i < 7; i++)
                calendarStripMatrix.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 14.28F));
            calendarStripMatrix.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
            for (int i = 0; i < 6; i++)
                calendarStripMatrix.RowStyles.Add(new RowStyle(SizeType.Percent, 15.67F));

            RebuildCalendarMonthGrid();
            calendarLayout.Controls.Add(calendarStripMatrix, 0, 2);

            // ---- Selected-day summary row: day title + progress left, Add-task action and
            // live clock right. Container layout — the clock can never collide with the title.
            Guna2Panel actionRow = UIControlFactory.CreatePanelSurface(preset: currentTheme);
            actionRow.Dock = DockStyle.Fill;
            actionRow.Margin = new Padding(0, DesignTokens.Spacing.Sm, 0, DesignTokens.Spacing.Sm); // breathing room vs grid/list
            actionRow.Padding = new Padding(DesignTokens.Spacing.Md, 0, DesignTokens.Spacing.Md, 0);

            TableLayoutPanel summaryLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            summaryLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            summaryLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            summaryLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            lblCalendarProgress = CreateLabel("0 tasks completed today", Point.Empty, new Size(0, 22), 10.5F, FontStyle.Bold, TextColor);
            lblCalendarProgress.Dock = DockStyle.Fill;
            lblCalendarProgress.TextAlign = ContentAlignment.MiddleLeft;
            lblCalendarProgress.AutoEllipsis = true;
            lblCalendarProgress.Margin = new Padding(0);
            summaryLayout.Controls.Add(lblCalendarProgress, 0, 0);

            // Same New Task flow as the header button, pre-scheduled to the selected day.
            Guna2Button btnAddForDay = UIControlFactory.CreatePrimaryButton("Add task", IconKind.Plus, preset: currentTheme);
            btnAddForDay.Size = new Size(114, 32);
            btnAddForDay.Margin = new Padding(DesignTokens.Spacing.Md, 0, DesignTokens.Spacing.Md, 0);
            btnAddForDay.AccessibleName = "Add a task on the selected day";
            btnAddForDay.Click += (s, e) => ExecuteModalTaskCreationDialogue(activeCalendarDate);
            summaryLayout.Controls.Add(btnAddForDay, 1, 0);

            lblLiveClock = CreateLabel($"● LIVE  {DateTime.Now:hh:mm:ss tt}", Point.Empty, new Size(0, 22), 10.5F, FontStyle.Bold, TextColor);
            lblLiveClock.Font = VibeAlarmPalette.Mono(10.5F, FontStyle.Bold);
            lblLiveClock.Dock = DockStyle.Fill;
            lblLiveClock.TextAlign = ContentAlignment.MiddleRight;
            lblLiveClock.Margin = new Padding(0);
            summaryLayout.Controls.Add(lblLiveClock, 2, 0);

            actionRow.Controls.Add(summaryLayout);
            calendarLayout.Controls.Add(actionRow, 0, 3);

            // ---- Selected day's task list ----
            calendarListPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                Margin = new Padding(0)
            };
            NativeScrollbarTheme.Track(calendarListPanel);
            calendarLayout.Controls.Add(calendarListPanel, 0, 4);
            BindRowWidthToHost(calendarListPanel);

            contentPanel.Controls.Add(calendarLayout);

            UpdateNextUpDisplay();
        }

        private void RebuildCalendarMonthGrid()
        {
            calendarStripMatrix.Controls.Clear();

            for (int i = 0; i < WeekDays.Length; i++)
            {
                Label dayHeader = CreateLabel(ShortWeekDays[i], new Point(0, 0), new Size(80, 24), 9F, FontStyle.Bold, MutedTextColor);
                dayHeader.TextAlign = ContentAlignment.MiddleCenter;
                dayHeader.Dock = DockStyle.Fill;
                calendarStripMatrix.Controls.Add(dayHeader, i, 0);
            }

            DateTime firstDay = displayedCalendarMonth;
            int leadingDays = (int)firstDay.DayOfWeek;
            DateTime gridDate = firstDay.AddDays(-leadingDays);

            for (int row = 1; row <= 6; row++)
            {
                for (int col = 0; col < 7; col++)
                {
                    DateTime currentDate = gridDate;
                    calendarStripMatrix.Controls.Add(BuildCalendarDayCell(currentDate), col, row);
                    gridDate = gridDate.AddDays(1);
                }
            }
        }

        private Control BuildCalendarDayCell(DateTime date)
        {
            bool isCurrentMonth = date.Month == displayedCalendarMonth.Month && date.Year == displayedCalendarMonth.Year;
            bool isSelected = date.Date == activeCalendarDate.Date;
            bool isToday = date.Date == DateTime.Today;
            bool isPast = date.Date < DateTime.Today;

            // The day's tasks drive the in-cell indicators: up to three type-colored dots
            // (same mapping as the task cards), then "+N" when the day holds more. Cells are
            // a fixed grid size — indicators paint inside, so content never shifts layout.
            List<TaskItem> dayTasks = masterTaskList
                .Where(t => GetTaskDate(t).Date == date.Date)
                .OrderBy(t => AlarmEngine.GetScheduledDateTime(t))
                .ToList();

            Color numberColor = isToday
                ? Color.White
                : !isCurrentMonth
                    ? Color.FromArgb(140, MutedTextColor)   // leading/trailing days: dimmed, not hidden
                    : isPast
                        ? Color.FromArgb(150, MutedTextColor)
                        : TextColor;

            Panel cell = new Panel
            {
                Tag = date,
                Dock = DockStyle.Fill,
                Margin = new Padding(1),
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand,
                AccessibleName = date.ToString("dddd, MMMM d, yyyy")
            };
            bool hovering = false;
            float dpr = DeviceDpi / 96F;

            // Date number sits in the top-left corner (standard calendar). TODAY = the number
            // inside a filled accent circle; SELECTED = accent-tinted wash + accent border —
            // the two states stay visually distinct even when they coincide.
            void DrawNumber(Graphics g)
            {
                using Font numberFont = VibeAlarmPalette.Mono(9F, isToday ? FontStyle.Bold : FontStyle.Regular);
                if (isToday)
                {
                    int d = (int)(22 * dpr);
                    using SolidBrush circleBrush = new SolidBrush(AccentColor);
                    g.FillEllipse(circleBrush, 5 * dpr, 4 * dpr, d, d);
                    using StringFormat centered = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    using SolidBrush ink = new SolidBrush(Color.White);
                    g.DrawString(date.Day.ToString(), numberFont, ink, new RectangleF(5 * dpr, 4 * dpr, d, d), centered);
                }
                else
                {
                    using StringFormat near = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Near };
                    using SolidBrush ink = new SolidBrush(numberColor);
                    g.DrawString(date.Day.ToString(), numberFont, ink, new RectangleF(7 * dpr, 5 * dpr, cell.Width - 8 * dpr, 16 * dpr), near);
                }
            }

            void DrawIndicators(Graphics g)
            {
                if (dayTasks.Count == 0)
                {
                    return;
                }
                float dot = 5 * dpr;
                float gap = 3 * dpr;
                float x = 7 * dpr;
                float y = cell.Height - 9 * dpr;
                foreach (TaskItem task in dayTasks.Take(3))
                {
                    using SolidBrush dotBrush = new SolidBrush(TypeDotColor(task));
                    g.FillEllipse(dotBrush, x, y, dot, dot);
                    x += dot + gap;
                }
                if (dayTasks.Count > 3)
                {
                    using Font moreFont = VibeAlarmPalette.Mono(6.5F);
                    using SolidBrush ink = new SolidBrush(MutedTextColor);
                    g.DrawString($"+{dayTasks.Count - 3}", moreFont, ink, x, y - 3 * dpr);
                }
            }

            // Every cell is a real, bordered cell: normal = card fill + hairline border,
            // hover = card-hover fill, selected = accent wash + accent border.
            cell.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle rect = new Rectangle(0, 0, cell.Width - 1, cell.Height - 1);
                using GraphicsPath path = GetRoundRectPath(rect, DesignTokens.Radius.Small);
                Color fill = isSelected
                    ? currentTheme.AccentTintColor
                    : hovering
                        ? CardHoverBg
                        : GlassSurface.CardFill(currentTheme);
                using (SolidBrush fillBrush = new SolidBrush(fill))
                {
                    e.Graphics.FillPath(fillBrush, path);
                }
                using Pen borderPen = new Pen(
                    isSelected ? AccentColor : GlassSurface.CardBorder(currentTheme),
                    isSelected ? 1.6F : 1F);
                e.Graphics.DrawPath(borderPen, path);
                DrawNumber(e.Graphics);
                DrawIndicators(e.Graphics);
            };

            cell.MouseEnter += (s, e) => { hovering = true; cell.Invalidate(); };
            cell.MouseLeave += (s, e) => { hovering = false; cell.Invalidate(); };

            // Single click = select the day (the detail panel below lists its tasks, and the
            // Add-task button targets it). Skipping the re-render when the selection is
            // unchanged keeps this control alive so DoubleClick below can still fire.
            cell.Click += (s, e) =>
            {
                if (cell.Tag is not DateTime clickedDate || clickedDate.Date == activeCalendarDate.Date)
                {
                    return;
                }
                activeCalendarDate = clickedDate;
                activeCalendarDay = activeCalendarDate.DayOfWeek.ToString();
                displayedCalendarMonth = new DateTime(activeCalendarDate.Year, activeCalendarDate.Month, 1);
                RenderActiveView();
            };

            // Double click = schedule: the SAME New Task flow as the header button, with the
            // clicked day pre-filled — never a second, parallel creation form.
            cell.DoubleClick += (s, e) =>
            {
                if (cell.Tag is DateTime clickedDate)
                {
                    ExecuteModalTaskCreationDialogue(clickedDate);
                }
            };

            return cell;
        }

        /// <summary>Semantic type color shared by task cards and calendar day cells:
        /// alarm = error, important = warning, notification = muted.</summary>
        private Color TypeDotColor(TaskItem task) => task.Type switch
        {
            SchedulerService.TaskTypeAlarm => currentTheme.ErrorColor,
            SchedulerService.TaskTypeImportant => currentTheme.WarningColor,
            _ => MutedTextColor
        };

        private void RenderDashboardView()
        {
            UpdateHeaderContext();

            // §16: dashboard = practical overview, not metric cards. One compact info row,
            // then TODAY and UPCOMING lists — all in a single scrollable flow.
            FlowLayoutPanel dashboardFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                Margin = new Padding(0)
            };
            NativeScrollbarTheme.Track(dashboardFlow);

            // ---- TODAY info row: small chips on one card (no giant stat cards) ----
            Guna2Panel infoCard = UIControlFactory.CreateCardPanel(preset: currentTheme);
            infoCard.Height = 52;
            infoCard.Padding = new Padding(DesignTokens.Spacing.Md, 0, DesignTokens.Spacing.Md, 0);

            FlowLayoutPanel chips = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };

            int activeToday = masterTaskList.Count(t => IsActiveTask(t) && GetTaskDate(t).Date == DateTime.Today);
            int alarmsToday = masterTaskList.Count(t => IsActiveTask(t) && GetTaskDate(t).Date == DateTime.Today && t.Type == SchedulerService.TaskTypeAlarm);
            int doneToday = masterTaskList.Count(t => t.Completed && GetTaskDate(t).Date == DateTime.Today);
            chips.Controls.Add(CreateInfoChip($"{activeToday} active today", currentTheme.AccentColor));
            chips.Controls.Add(CreateInfoChip($"{alarmsToday} alarms", currentTheme.ErrorColor));
            chips.Controls.Add(CreateInfoChip($"{doneToday} completed", currentTheme.SuccessColor));
            infoCard.Controls.Add(chips);
            dashboardFlow.Controls.Add(infoCard);

            // ---- TODAY list ----
            dashboardFlow.Controls.Add(CreateSectionHeading("TODAY", activeToday));
            dashboardFocusPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0)
            };
            BindRowWidthToHost(dashboardFocusPanel);
            dashboardFlow.Controls.Add(dashboardFocusPanel);

            // ---- UPCOMING list (next few beyond today) ----
            int upcomingCount = masterTaskList.Count(t => IsActiveTask(t) && GetTaskDate(t).Date > DateTime.Today);
            dashboardFlow.Controls.Add(CreateSectionHeading("UPCOMING", upcomingCount));
            dashboardUpcomingPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0)
            };
            BindRowWidthToHost(dashboardUpcomingPanel);
            dashboardFlow.Controls.Add(dashboardUpcomingPanel);

            contentPanel.Controls.Add(dashboardFlow);
            BindRowWidthToHost(dashboardFlow);
        }

        /// <summary>Small "● label" info chip for the dashboard's TODAY row — a colored dot +
        /// muted text, not a filled badge.</summary>
        private Control CreateInfoChip(string text, Color dotColor)
        {
            FlowLayoutPanel chip = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 14, DesignTokens.Spacing.Lg, 0)
            };
            chip.Controls.Add(new Label
            {
                Text = "●",
                AutoSize = true,
                Font = new Font("Segoe UI", 8F),
                ForeColor = dotColor,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 5, 6, 0)
            });
            chip.Controls.Add(new Label
            {
                Text = text,
                AutoSize = true,
                Font = VibeAlarmPalette.Body(9.5F, FontStyle.Bold),
                ForeColor = MutedTextColor,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            });
            return chip;
        }

        private void RenderSettingsView()
        {
            UpdateHeaderContext();

            // §19–24: the whole settings page (scrolling section cards, two-column rows)
            // lives in SettingsPageView. MainForm only hosts it and wires the callbacks —
            // the page owns no business logic.
            SettingsPageView settingsPage = new SettingsPageView(
                getTasks: () => masterTaskList,
                onThemeModeChanged: ApplyThemeMode,
                onAppearanceCommitted: ApplyAppearanceSettings,
                onDataChanged: RefreshDataCounters)
            {
                Dock = DockStyle.Fill
            };
            contentPanel.Controls.Add(settingsPage);
        }

        private void RenderAmbientView()
        {
            UpdateHeaderContext();

            // Same content, rebuilt on layout containers (§5): hero, import action, volume card,
            // built-in sounds card — one scrollable flow, no Point(x,y) positioning.
            FlowLayoutPanel ambientFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                Margin = new Padding(0)
            };
            NativeScrollbarTheme.Track(ambientFlow);

            // ---- Hero: text left, Stop right ----
            Guna2Panel heroPanel = UIControlFactory.CreateCardPanel(preset: currentTheme);
            heroPanel.Height = 116;
            heroPanel.Padding = new Padding(DesignTokens.Spacing.Lg, DesignTokens.Spacing.Md, DesignTokens.Spacing.Lg, DesignTokens.Spacing.Md);

            TableLayoutPanel heroRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            heroRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            heroRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            TableLayoutPanel heroText = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            heroText.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            heroText.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            heroText.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            heroText.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            Label title = new Label
            {
                Text = "Ambient Soundscapes",
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                Font = VibeAlarmPalette.Display(13F, FontStyle.Bold),
                ForeColor = TextColor,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            Label subtitle = new Label
            {
                Text = "Play your own local focus audio offline while you study or work.",
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                Font = VibeAlarmPalette.Body(9.5F),
                ForeColor = MutedTextColor,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 3, 0, 0)
            };
            lblAmbientNowPlaying = new Label
            {
                Text = audioService.IsAmbientPlaying ? "Ambient audio playing" : "Nothing playing",
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                Font = VibeAlarmPalette.Body(9.5F, FontStyle.Bold),
                ForeColor = TextColor,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 8, 0, 0)
            };
            heroText.Controls.Add(title, 0, 0);
            heroText.Controls.Add(subtitle, 0, 1);
            heroText.Controls.Add(lblAmbientNowPlaying, 0, 2);
            heroRow.Controls.Add(heroText, 0, 0);

            Guna2Button stopButton = UIControlFactory.CreateSecondaryButton("Stop Sound", preset: currentTheme);
            stopButton.Size = new Size(110, 34);
            stopButton.Margin = new Padding(DesignTokens.Spacing.Md, 0, 0, 0);
            stopButton.Anchor = AnchorStyles.Right;
            stopButton.Click += (s, e) => StopAmbientAudio();
            heroRow.Controls.Add(stopButton, 1, 0);
            heroPanel.Controls.Add(heroRow);
            ambientFlow.Controls.Add(heroPanel);

            // ---- Import action ----
            Guna2Button importButton = UIControlFactory.CreatePrimaryButton("Add Local Sound", IconKind.Plus, preset: currentTheme);
            importButton.Size = new Size(170, 36);
            importButton.Margin = new Padding(0, DesignTokens.Spacing.Md, 0, DesignTokens.Spacing.Md);
            importButton.Click += (s, e) => ImportAndPlayAmbientFile();
            ambientFlow.Controls.Add(importButton);

            // ---- Volume card: name/hint left, slider + live % right ----
            Guna2Panel volumePanel = UIControlFactory.CreateCardPanel(preset: currentTheme);
            volumePanel.Height = 96;
            volumePanel.Padding = new Padding(DesignTokens.Spacing.Lg, DesignTokens.Spacing.Md, DesignTokens.Spacing.Lg, DesignTokens.Spacing.Md);

            TableLayoutPanel volumeRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            volumeRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55F));
            volumeRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45F));

            TableLayoutPanel volumeText = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            volumeText.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            volumeText.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            volumeText.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            Label volumeTitle = new Label
            {
                Text = "Sound Volume",
                Dock = DockStyle.Fill,
                Font = VibeAlarmPalette.Body(10.5F, FontStyle.Bold),
                ForeColor = TextColor,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            Label volumeHint = new Label
            {
                Text = "Adjust the ambient sound level without changing your task alarms.",
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                Font = VibeAlarmPalette.Body(8.5F),
                ForeColor = MutedTextColor,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 4, 0, 0)
            };
            volumeText.Controls.Add(volumeTitle, 0, 0);
            volumeText.Controls.Add(volumeHint, 0, 1);
            volumeRow.Controls.Add(volumeText, 0, 0);

            Label volumeValue = new Label
            {
                Text = $"{ambientVolume}%",
                AutoSize = true,
                Font = VibeAlarmPalette.Mono(11F, FontStyle.Bold),
                ForeColor = TextColor,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 10, DesignTokens.Spacing.Sm, 0)
            };
            ambientVolumeSlider = new TrackBar
            {
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Width = 240,
                Minimum = 0,
                Maximum = 100,
                TickFrequency = 10,
                Value = ambientVolume
            };
            ambientVolumeSlider.Scroll += (s, e) =>
            {
                ambientVolume = ambientVolumeSlider.Value;
                volumeValue.Text = $"{ambientVolume}%";
                audioService.AmbientVolume = ambientVolume;
                audioService.ApplyAmbientVolume();
            };

            FlowLayoutPanel volumeControls = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent,
                Margin = new Padding(DesignTokens.Spacing.Lg, 0, 0, 0)
            };
            volumeControls.Controls.Add(volumeValue);
            volumeControls.Controls.Add(ambientVolumeSlider);
            volumeRow.Controls.Add(volumeControls, 1, 0);
            volumePanel.Controls.Add(volumeRow);
            ambientFlow.Controls.Add(volumePanel);

            // ---- Built-in offline sounds ----
            Guna2Panel builtinPanel = UIControlFactory.CreateCardPanel(preset: currentTheme);
            builtinPanel.Height = 140;
            builtinPanel.Padding = new Padding(DesignTokens.Spacing.Lg, DesignTokens.Spacing.Md, DesignTokens.Spacing.Lg, DesignTokens.Spacing.Md);

            TableLayoutPanel builtinLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            builtinLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            builtinLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            builtinLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            TableLayoutPanel builtinText = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            builtinText.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            builtinText.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            builtinText.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            Label builtinTitle = new Label
            {
                Text = "Offline Focus Sounds",
                Dock = DockStyle.Fill,
                Font = VibeAlarmPalette.Body(10.5F, FontStyle.Bold),
                ForeColor = TextColor,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            Label builtinText2 = new Label
            {
                Text = "Synthesized offline loops to drown out background noise and boost productivity.",
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                Font = VibeAlarmPalette.Body(8.5F),
                ForeColor = MutedTextColor,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 4, 0, 0)
            };
            builtinText.Controls.Add(builtinTitle, 0, 0);
            builtinText.Controls.Add(builtinText2, 0, 1);
            builtinLayout.Controls.Add(builtinText, 0, 0);

            FlowLayoutPanel builtinButtons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            Guna2Button btnPlayBrown = UIControlFactory.CreateSecondaryButton("Play Brownian Focus", preset: currentTheme);
            btnPlayBrown.Size = new Size(180, 36);
            btnPlayBrown.Margin = new Padding(0, 10, DesignTokens.Spacing.Sm, 0);
            btnPlayBrown.Click += (s, e) =>
            {
                string path = Path.Combine(AppContext.BaseDirectory, "Assets", "brown_noise.wav");
                PlayAmbientFile(path, "Brownian Focus");
            };
            Guna2Button btnPlayRain = UIControlFactory.CreateSecondaryButton("Play Rain Soundscape", preset: currentTheme);
            btnPlayRain.Size = new Size(180, 36);
            btnPlayRain.Margin = new Padding(DesignTokens.Spacing.Sm, 10, 0, 0);
            btnPlayRain.Click += (s, e) =>
            {
                string path = Path.Combine(AppContext.BaseDirectory, "Assets", "rain.wav");
                PlayAmbientFile(path, "Rain Soundscape");
            };
            builtinButtons.Controls.Add(btnPlayBrown);
            builtinButtons.Controls.Add(btnPlayRain);
            builtinLayout.Controls.Add(builtinButtons, 0, 1);
            builtinPanel.Controls.Add(builtinLayout);
            ambientFlow.Controls.Add(builtinPanel);

            contentPanel.Controls.Add(ambientFlow);
            BindRowWidthToHost(ambientFlow);
        }

        private void RefreshDataCounters()
        {
            int pending = masterTaskList.Count(t => IsActiveTask(t));
            int total = masterTaskList.Count;
            int completed = masterTaskList.Count(t => t.Completed);
            int completePercent = total == 0 ? 0 : (int)Math.Round(completed * 100d / total);

            // lblHeaderSubtitle is owned by UpdateHeaderContext — never rewritten here.
            if (lblSidebarRemaining != null) lblSidebarRemaining.Text = $"{pending} Tasks Remaining";
            if (lblSidebarPercent != null) lblSidebarPercent.Text = $"{completePercent}%";
            if (sidebarProgressFill != null && sidebarProgressTrack != null)
            {
                sidebarProgressFill.Width = Math.Max(0, Math.Min(sidebarProgressTrack.Width, sidebarProgressTrack.Width * completePercent / 100));
                RoundControl(sidebarProgressFill, 5);
            }

            if (taskListPanel != null && !taskListPanel.IsDisposed) BindTaskListView();
            if (calendarListPanel != null && !calendarListPanel.IsDisposed)
            {
                BindCalendarView();
                // Live-sync the month-grid day badges on the same data edges (task added/fired/
                // expired). Gated to the active Calendar tab and grid existence so this runs only
                // on real state changes, not on per-second/minte ticks.
                if (activeView == "Calendar" && calendarStripMatrix != null && !calendarStripMatrix.IsDisposed)
                {
                    RebuildCalendarMonthGrid();
                }
            }
            if (dashboardFocusPanel != null && !dashboardFocusPanel.IsDisposed) BindDashboardFocusView();
            if (dashboardUpcomingPanel != null && !dashboardUpcomingPanel.IsDisposed) BindDashboardUpcomingView();
            TaskStorageService.Save(masterTaskList);
        }

        private IEnumerable<TaskItem> GetFilteredTasks(IEnumerable<TaskItem> SourceList)
        {
            if (string.IsNullOrWhiteSpace(searchFilterQuery)) return SourceList;
            return SourceList.Where(t => t.Title.Contains(searchFilterQuery, StringComparison.OrdinalIgnoreCase));
        }

        private static DateTime GetTaskDate(TaskItem task)
        {
            return AlarmEngine.GetTaskDate(task);
        }

        /// <summary>True for a schedule that still counts as an outstanding/upcoming item
        /// (not completed and not retired to history as expired).</summary>
        private static bool IsActiveTask(TaskItem task)
        {
            return !task.Completed && task.GetState() is TaskState.Scheduled or TaskState.Due;
        }

        /// <summary>§11: tasks grouped into TODAY / UPCOMING sections, each with a heading +
        /// count. Completed tasks stay in place inside their section (§12: don't hide history).</summary>
        private void BindTaskListView()
        {
            taskListPanel.SuspendLayout();
            taskListPanel.Controls.Clear();
            var filtered = GetFilteredTasks(masterTaskList).ToList();

            if (!filtered.Any())
            {
                taskListPanel.Controls.Add(CreateEmptyStateRow("No tasks found.", "Try a different search or add a new task."));
                taskListPanel.ResumeLayout();
                return;
            }

            var ordered = filtered.OrderBy(GetTaskDate).ThenBy(t => t.RemindTime).ToList();
            var today = ordered.Where(t => GetTaskDate(t).Date == DateTime.Today).ToList();
            var upcoming = ordered.Where(t => GetTaskDate(t).Date != DateTime.Today).ToList();

            if (today.Count > 0)
            {
                taskListPanel.Controls.Add(CreateSectionHeading("TODAY", today.Count));
                foreach (TaskItem task in today)
                {
                    taskListPanel.Controls.Add(BuildTaskRowCard(task));
                }
            }

            if (upcoming.Count > 0)
            {
                taskListPanel.Controls.Add(CreateSectionHeading("UPCOMING", upcoming.Count));
                foreach (TaskItem task in upcoming)
                {
                    taskListPanel.Controls.Add(BuildTaskRowCard(task));
                }
            }

            if (today.Count == 0 && upcoming.Count == 0)
            {
                taskListPanel.Controls.Add(CreateEmptyStateRow("Nothing scheduled yet.", "Create your first task to get started."));
            }

            taskListPanel.ResumeLayout();
        }

        private void BindCalendarView()
        {
            calendarListPanel.Controls.Clear();
            activeCalendarDay = activeCalendarDate.DayOfWeek.ToString();
            var items = GetFilteredTasks(masterTaskList.Where(t => GetTaskDate(t).Date == activeCalendarDate.Date)).ToList();
            int upcoming = items.Count(t => t.GetState() is TaskState.Scheduled or TaskState.Due);
            int completed = items.Count(t => t.GetState() == TaskState.Completed);
            int expired = items.Count(t => t.GetState() == TaskState.Expired);
            lblCalendarProgress.Text = $"{activeCalendarDate:dddd, MMMM d}" + (expired > 0
                ? $" - {upcoming} upcoming, {completed} done, {expired} past"
                : $" - {completed} of {items.Count} tasks completed");

            if (!items.Any())
            {
                calendarListPanel.Controls.Add(CreateEmptyStateRow("No tasks scheduled.", $"You do not have anything planned for {activeCalendarDate:dddd, MMMM d}."));
                return;
            }

            // Upcoming/due first, then completed, then expired history retained at the end.
            foreach (TaskItem task in items
                .OrderByDescending(t => TaskStateRank(t.GetState()))
                .ThenBy(t => AlarmEngine.GetScheduledDateTime(t)))
            {
                calendarListPanel.Controls.Add(BuildTaskRowCard(task));
            }
        }

        private static int TaskStateRank(TaskState state) => state switch
        {
            TaskState.Scheduled or TaskState.Due => 0,
            TaskState.Completed => 1,
            TaskState.Triggered => 1,
            _ => 2
        };

        private void BindDashboardFocusView()
        {
            dashboardFocusPanel.Controls.Clear();
            var remainingItems = GetFilteredTasks(masterTaskList.Where(t => GetTaskDate(t).Date == DateTime.Today && IsActiveTask(t))).ToList();

            if (!remainingItems.Any())
            {
                dashboardFocusPanel.Controls.Add(CreateEmptyStateRow("You're all caught up.", "No tasks are scheduled for today."));
                return;
            }

            foreach (TaskItem task in remainingItems.Take(4))
            {
                dashboardFocusPanel.Controls.Add(BuildTaskRowCard(task));
            }
        }

        /// <summary>Dashboard UPCOMING: the next few active tasks beyond today, soonest first.</summary>
        private void BindDashboardUpcomingView()
        {
            dashboardUpcomingPanel.Controls.Clear();
            var upcoming = GetFilteredTasks(masterTaskList.Where(t => GetTaskDate(t).Date > DateTime.Today && IsActiveTask(t)))
                .OrderBy(GetTaskDate).ThenBy(t => t.RemindTime)
                .ToList();

            if (!upcoming.Any())
            {
                dashboardUpcomingPanel.Controls.Add(CreateEmptyStateRow("Nothing on the horizon.", "Tasks scheduled for future days will appear here."));
                return;
            }

            foreach (TaskItem task in upcoming.Take(4))
            {
                dashboardUpcomingPanel.Controls.Add(BuildTaskRowCard(task));
            }
        }

        /// <summary>§12: a calm 80px (Comfortable) / 68px (Compact) card on layout containers —
        /// [checkbox | title + one-line "Today · 8:00 PM" metadata | type dot + more]. No badges,
        /// no hard-coded X coordinates inside the card.</summary>
        private Control BuildTaskRowCard(TaskItem item)
        {
            int rowWidth = GetListRowWidth();
            int cardHeight = Appearance.TaskRowHeight;

            // Flat list (§12, Google Tasks / MS To Do style): the row IS the page at rest —
            // no per-item box, border, or radius; a 1px hairline divider separates neighbors,
            // and the ONLY visual distinction is the hover highlight. A Guna2Panel (not a
            // plain Panel) still hosts the row so Guna routes child-mouse messages to it,
            // keeping the hover reliable with label children. The resting fill is the task
            // card's own TINT-ONLY glass token (GlassSurface.TaskCardFill) — a translucent
            // wash the atmospheric background shows through; blur-behind is deliberately NOT
            // simulated per-card (the lag risk on the most-rebuilt list in the app).
            Color restFill = GlassSurface.TaskCardFill(currentTheme);
            Guna2Panel card = new Guna2Panel
            {
                Size = new Size(rowWidth, cardHeight),
                // Dividers (not gaps) separate rows — zero margin keeps one continuous list.
                Margin = new Padding(0),
                // Extra padding on the right (Lg vs Md): the trailing pill + "…" button
                // must never touch the row edge — text shrinks with AutoEllipsis, they don't.
                Padding = new Padding(DesignTokens.Spacing.Md, 0, DesignTokens.Spacing.Lg, 0),
                FillColor = restFill,
                BorderThickness = 0,
                BorderRadius = 0,
                BackColor = Color.Transparent
            };
            // Rows repaint on every hover and scroll — the buffered styles stop the flicker
            // that reads as lag or overlap.
            EnableFlickerFreePaint(card);

            // Hairline divider on the row's bottom edge (drawn in Paint so it lands on top
            // of both the transparent rest state and the hover fill).
            card.Paint += (s, e) =>
            {
                using Pen divider = new Pen(Color.FromArgb(120, currentTheme.BorderColor));
                e.Graphics.DrawLine(divider, 0, card.Height - 1, card.Width, card.Height - 1);
            };

            // Hover: one quiet opaque step (GlassSurface.CardHover is intentionally opaque) —
            // the only time a row looks distinct from its neighbors, besides the divider.
            Color hoverFill = GlassSurface.CardHover(currentTheme);
            TypeBadge? badge = null; // created below; declared early so hover can refresh it
            card.MouseEnter += (s, e) =>
            {
                card.FillColor = hoverFill;
                badge?.SetBackdrop(hoverFill); // pill corners follow the row's current fill
            };
            card.MouseLeave += (s, e) =>
            {
                card.FillColor = restFill;
                badge?.SetBackdrop(restFill);
            };

            TableLayoutPanel row = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            // One Percent(100) row: all three columns measure against the SAME vertical
            // midpoint, so checkbox / text / trailing read as one centered unit.
            row.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            EnableFlickerFreePaint(row);
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44));   // checkbox column
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F)); // text column
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));      // type pill + more

            // ---- Checkbox: quiet circle — pending clock while open, filled clipboard-check
            // when completed (the two SVG state icons; glyph path is their fallback). ----
            Guna2Button btnToggle = new Guna2Button
            {
                Size = new Size(28, 28),
                Anchor = AnchorStyles.None,
                FillColor = item.Completed ? MutedTextColor : Color.Transparent,
                BorderThickness = 1,
                BorderColor = item.Completed ? MutedTextColor : BorderColor,
                BorderRadius = 14, // circle
                Animated = false,
                Cursor = Cursors.Hand,
                AccessibleName = item.Completed ? "Mark as not done" : "Complete task"
            };
            if (item.Completed)
            {
                btnToggle.Image = IconSet.Render(IconKind.TaskComplete, currentTheme.IsLight ? Color.White : Color.FromArgb(0x19, 0x19, 0x19), 16);
                btnToggle.ImageSize = new Size(16, 16);
            }
            else
            {
                btnToggle.Image = IconSet.Render(IconKind.TaskPending, MutedTextColor, 16);
                btnToggle.ImageSize = new Size(16, 16);
            }
            btnToggle.HoverState.BorderColor = TextColor;
            // Hover = border shift AND a quiet fill — an obvious-but-subtle cue matching the
            // row/card hover treatment (Guna hovers the full circle, so the hit area is clear).
            btnToggle.HoverState.FillColor = GlassSurface.CardHover(currentTheme);
            btnToggle.Click += (s, e) =>
            {
                item.Completed = !item.Completed;
                RefreshDataCounters();
            };
            row.Controls.Add(btnToggle, 0, 0);

            // ---- Text: title over one-line metadata ("Today · 8:00 PM") ----
            // Rows are FONT-DRIVEN (AutoSize), not a fixed 55/45 split of the card height:
            // each row is exactly its label's line height, so descenders can never clip at any
            // DPI or font-fallback. Flexible spacer rows above and below center the pair.
            TableLayoutPanel textBlock = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            textBlock.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            textBlock.RowStyles.Add(new RowStyle(SizeType.Percent, 50F)); // spacer
            textBlock.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // title
            textBlock.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // meta
            textBlock.RowStyles.Add(new RowStyle(SizeType.Percent, 50F)); // spacer

            Label lblTitle = new Label
            {
                Text = item.Title,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.BottomLeft,
                AutoEllipsis = true,
                // §12: completed = muted + strikeout (Inter degrades to muted-only if it
                // rejects strikeout — FontRegistry.Resolve handles that).
                Font = VibeAlarmPalette.Body(10.5F, item.Completed ? FontStyle.Strikeout | FontStyle.Bold : FontStyle.Bold),
                ForeColor = item.Completed ? MutedTextColor : TextColor,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            Label lblMeta = new Label
            {
                Text = GetTaskWhenLabel(item),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.TopLeft,
                AutoEllipsis = true,
                // 9.5pt (up from 8.5) + secondary ink (up from full-muted): the metadata
                // line reads at a glance while the bold title stays the anchor.
                Font = VibeAlarmPalette.Body(9.5F),
                ForeColor = SecondaryTextColor,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 3, 0, 0) // consistent title→meta gap, never cramped
            };
            textBlock.Controls.Add(lblTitle, 0, 1);
            textBlock.Controls.Add(lblMeta, 0, 2);
            row.Controls.Add(textBlock, 1, 0);

            // ---- Type pill, then the more (…) button ----
            // A TableLayoutPanel, NOT a FlowLayoutPanel with hand-computed top margins (the
            // old "nudge" approach — the pill/… offsets drifted at other row heights and DPI
            // scales and read as elements competing for the same space). Children anchored
            // LEFT are vertically CENTERED by the panel itself at any row height — no math.
            TableLayoutPanel trailing = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.Transparent,
                Margin = new Padding(DesignTokens.Spacing.Md, 0, 0, 0) // 16px gap after the text block
            };
            trailing.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); // type pill
            trailing.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 30 + DesignTokens.Spacing.Md)); // … button + 16px gap

            // Status pill in the type's semantic color (Todoist/Linear tag pattern) — the
            // type color is folded into fill + ink. Backdrop = the row's resting glass fill
            // (the pill's corners must match what the row paints under it); hover swaps the
            // backdrop to the highlight.
            TypeBadge typeBadge = new TypeBadge(item.Type, TypeDotColor(item), restFill, VibeAlarmPalette.Mono(8.5F, FontStyle.Bold));
            badge = typeBadge;
            typeBadge.Anchor = AnchorStyles.Left;
            badge.Margin = new Padding(0, 0, DesignTokens.Spacing.Md, 0);
            trailing.Controls.Add(badge, 0, 0);

            Guna2Button btnOptions = UIControlFactory.CreateIconButton(IconKind.More, preset: currentTheme, iconSize: 16, buttonSize: 30);
            btnOptions.Anchor = AnchorStyles.Left;
            btnOptions.Margin = new Padding(0);
            btnOptions.Click += (s, e) => ShowTaskContextMenu(item, btnOptions);
            trailing.Controls.Add(btnOptions, 1, 0);

            row.Controls.Add(trailing, 2, 0);
            card.Controls.Add(row);
            return card;
        }

        /// <summary>Status pill for a task's type (ALARM / NOTIFICATION / IMPORTANT): rounded
        /// tinted capsule in the type's semantic color — the Todoist/Linear tag pattern. The
        /// capsule's tint is BLENDED with the page background into an opaque color, and the
        /// whole client rect is painted with the row's CURRENT backdrop first (rest = page,
        /// hover = highlight, swapped via <see cref="SetBackdrop"/>) so the corners outside
        /// the capsule always match the row exactly — no simulated transparency needed.</summary>
        private sealed class TypeBadge : Control
        {
            private readonly string label;
            private readonly Color ink;
            private readonly Color fill;
            private readonly Color border;
            private Color backdrop;

            public TypeBadge(string text, Color typeColor, Color backdrop, Font font)
            {
                label = text.ToUpperInvariant();
                ink = typeColor;
                fill = Blend(typeColor, backdrop, 0.14f);
                border = Blend(typeColor, backdrop, 0.38f);
                this.backdrop = backdrop;
                Font = font;
                TabStop = false;
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

                // Fixed, text-driven size: the pill can never clip its own label.
                Size preferred = TextRenderer.MeasureText(label, font);
                Size = new Size(preferred.Width + 18, preferred.Height + 7);
            }

            /// <summary>Swaps the corner/backdrop color — the row calls this when its hover
            /// fill turns on or off so the pill never floats on a stale background.</summary>
            public void SetBackdrop(Color value)
            {
                backdrop = value;
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                // Full rect in the row's current fill FIRST: the capsule's corners must be
                // indistinguishable from the row behind it in both rest and hover states.
                using (SolidBrush back = new SolidBrush(backdrop))
                {
                    e.Graphics.FillRectangle(back, ClientRectangle);
                }

                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using GraphicsPath path = GetRoundRectPath(new Rectangle(0, 0, Width - 1, Height - 1), (Height - 1) / 2);
                using (SolidBrush capsule = new SolidBrush(fill))
                {
                    e.Graphics.FillPath(capsule, path);
                }
                using (Pen edge = new Pen(border))
                {
                    e.Graphics.DrawPath(edge, path);
                }
                TextRenderer.DrawText(e.Graphics, label, Font, ClientRectangle, ink,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                base.OnPaint(e);
            }

            private static Color Blend(Color source, Color backdrop, float amount) =>
                Color.FromArgb(
                    (int)(source.R * amount + backdrop.R * (1 - amount)),
                    (int)(source.G * amount + backdrop.G * (1 - amount)),
                    (int)(source.B * amount + backdrop.B * (1 - amount)));
        }

        /// <summary>§12: three-dot menu — "Complete task" / "Duplicate task" / separator /
        /// "Delete task" (destructive styling + optional confirmation).</summary>
        private void ShowTaskContextMenu(TaskItem item, Control anchor)
        {
            ContextMenuStrip menu = new ContextMenuStrip
            {
                BackColor = currentTheme.SurfaceElevated,
                ForeColor = TextColor,
                ShowImageMargin = true,
                Font = VibeAlarmPalette.Body(9.5F)
            };

            ToolStripMenuItem completeItem = new ToolStripMenuItem(item.Completed ? "Mark as not done" : "Complete task")
            {
                // Same vector as the completed state on the row itself, so the action and its
                // result read as one icon.
                Image = IconSet.Render(IconKind.TaskComplete, TextColor, 16)
            };
            completeItem.Click += (src, ev) =>
            {
                item.Completed = !item.Completed;
                RefreshDataCounters();
            };

            ToolStripMenuItem duplicateItem = new ToolStripMenuItem("Duplicate task")
            {
                Image = IconSet.Render(IconKind.Copy, TextColor, 16)
            };
            duplicateItem.Click += (src, ev) =>
            {
                masterTaskList.Add(new TaskItem
                {
                    Id = Guid.NewGuid().ToString(),
                    Title = item.Title + " (Copy)",
                    ScheduledDate = item.ScheduledDate,
                    Day = item.Day,
                    RemindTime = item.RemindTime,
                    Type = item.Type,
                    Completed = false
                });
                RefreshDataCounters();
            };

            ToolStripMenuItem deleteItem = new ToolStripMenuItem("Delete task")
            {
                Image = IconSet.Render(IconKind.Trash, currentTheme.ErrorColor, 16),
                ForeColor = currentTheme.ErrorColor
            };
            deleteItem.Click += (src, ev) =>
            {
                bool confirm = SettingsService.Load().ConfirmBeforeDelete;
                if (!confirm || MessageBox.Show($"Delete \"{item.Title}\" permanently?", "Delete Task",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Stop) == DialogResult.Yes)
                {
                    masterTaskList.Remove(item);
                    RefreshDataCounters();
                }
            };

            menu.Items.Add(completeItem);
            menu.Items.Add(duplicateItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(deleteItem);
            menu.Show(anchor, new Point(0, anchor.Height));
        }

        /// <summary>Compact "Today · 8:00 PM" / "Tomorrow · 9:30 AM" / "Mon, Oct 5 · 8:00 PM" label.</summary>
        private static string GetTaskWhenLabel(TaskItem task)
        {
            DateTime date = GetTaskDate(task);
            string day = date.Date == DateTime.Today ? "Today"
                : date.Date == DateTime.Today.AddDays(1) ? "Tomorrow"
                : date.ToString("ddd, MMM d");
            return $"{day} · {task.RemindTime}";
        }

        private int GetListRowWidth()
        {
            FlowLayoutPanel? host = taskListPanel;
            if (calendarListPanel != null && !calendarListPanel.IsDisposed && calendarListPanel.Visible)
            {
                host = calendarListPanel;
            }
            if (dashboardFocusPanel != null && !dashboardFocusPanel.IsDisposed && dashboardFocusPanel.Visible)
            {
                host = dashboardFocusPanel;
            }
            if (dashboardUpcomingPanel != null && !dashboardUpcomingPanel.IsDisposed && dashboardUpcomingPanel.Visible)
            {
                host = dashboardUpcomingPanel;
            }

            int availableWidth = host?.ClientSize.Width > 0 ? host.ClientSize.Width : contentPanel.ClientSize.Width;
            return Math.Max(620, availableWidth - 24);
        }

        private void ImportAndPlayAmbientFile()
        {
            using OpenFileDialog picker = new OpenFileDialog
            {
                Title = "Choose a sound file",
                Filter = "Audio and video files|*.mp3;*.mp4;*.wav;*.wma;*.aac;*.m4a;*.flac;*.avi;*.wmv|All files|*.*",
                CheckFileExists = true,
                Multiselect = false
            };

            if (picker.ShowDialog(this) == DialogResult.OK)
            {
                PlayAmbientFile(picker.FileName, Path.GetFileNameWithoutExtension(picker.FileName));
            }
        }

        private void PlayAmbientFile(string filePath, string displayName)
        {
            if (!audioService.PlayAmbientFile(filePath, displayName))
            {
                MessageBox.Show("This sound file could not be opened. Try another mp3, mp4, wav, or audio file.", "Soundscape Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void StopAmbientAudio()
        {
            audioService.StopAmbientAudio();
        }

        private Control CreateEmptyStateRow(string head, string sub)
        {
            Panel emptyPanel = new Panel { Size = new Size(920, 140), Margin = new Padding(0, 16, 0, 0) };
            Label mainLabel = CreateLabel(head, new Point(0, 40), new Size(920, 24), 13F, FontStyle.Bold, MutedTextColor);
            mainLabel.TextAlign = ContentAlignment.MiddleCenter;
            Label subLabel = CreateLabel(sub, new Point(0, 68), new Size(920, 20), 10F, FontStyle.Regular, MutedTextColor);
            subLabel.TextAlign = ContentAlignment.MiddleCenter;

            emptyPanel.Controls.AddRange(new Control[] { mainLabel, subLabel });
            return emptyPanel;
        }

        private Label CreateLabel(string text, Point location, Size size, float fontSize, FontStyle style, Color color)
        {
            return new Label
            {
                Text = text,
                Location = location,
                Size = size,
                Font = new Font("Segoe UI", fontSize, style),
                ForeColor = color,
                BackColor = Color.Transparent
            };
        }

        /// <summary>Subtle editorial letter-spacing for short tracked-caps labels (e.g. "NEXT UP").
        /// WinForms has no native tracking, so we insert a narrow hair space between characters.
        /// Applied only to short uppercase metadata labels — long strings and body text keep normal
        /// tracking so legibility and layout widths are never compromised.</summary>
        private static void ApplyLetterSpacing(Control label)
        {
            if (label == null || string.IsNullOrEmpty(label.Text))
            {
                return;
            }
            const char hairSpace = '\u200A';
            const int maxTrackedLength = 24;
            if (label.Text.Length > maxTrackedLength)
            {
                return;
            }
            label.Text = string.Join(hairSpace.ToString(), label.Text.ToCharArray());
        }

        /// <summary>Lightens (&gt; 1) or darkens (&lt; 1) a color toward its gray value, theme-aware.</summary>
        private static Color Shade(Color color, float amount)
        {
            float factor = Math.Clamp(amount, 0.5F, 1.5F);
            // On dark surfaces, "more" of the same gray = lighter; on light, clamp channel-wise.
            int r = (int)Math.Clamp(color.R * factor, 0, 255);
            int g = (int)Math.Clamp(color.G * factor, 0, 255);
            int b = (int)Math.Clamp(color.B * factor, 0, 255);
            return Color.FromArgb(r, g, b);
        }

        /// <summary>Accent hover, one tonal step off the §14 accent fill (matches factory primary).</summary>
        private Color HoverAccent()
        {
            int step = currentTheme.IsLight ? 24 : 40;
            int r = Math.Clamp(AccentColor.R + step, 0, 255);
            int g = Math.Clamp(AccentColor.G + step, 0, 255);
            int b = Math.Clamp(AccentColor.B + step, 0, 255);
            return Color.FromArgb(r, g, b);
        }

        /// <summary>§4: the header carries the page context. Dashboard keeps the greeting;
        /// every other view shows its page title + a one-line description. The subtitle is
        /// owned here only — RefreshDataCounters no longer overwrites it.</summary>
        private void UpdateHeaderContext()
        {
            int hour = DateTime.Now.Hour;
            string greeting = hour < 12 ? "Good morning" : hour < 18 ? "Good afternoon" : "Good evening";

            int activeToday = masterTaskList.Count(t => IsActiveTask(t) && GetTaskDate(t).Date == DateTime.Today);

            switch (activeView)
            {
                case "Dashboard":
                    lblGreeting.Text = $"{greeting}, Jeptah";
                    lblHeaderSubtitle.Text = "Here's what's happening today.";
                    break;
                case "Calendar":
                    lblGreeting.Text = "Calendar";
                    lblHeaderSubtitle.Text = "Pick a date to see what's scheduled.";
                    break;
                case "Ambient":
                    lblGreeting.Text = "Ambient";
                    lblHeaderSubtitle.Text = "Play local focus audio offline while you study or work.";
                    break;
                case "Settings":
                    lblGreeting.Text = "Settings";
                    lblHeaderSubtitle.Text = "Manage appearance, notifications, data, and system options.";
                    break;
                default:
                    lblGreeting.Text = "Tasks";
                    lblHeaderSubtitle.Text = activeToday == 1
                        ? "1 task remaining today."
                        : $"{activeToday} tasks remaining today.";
                    break;
            }
        }

        /// <summary>Refreshes the LIVE clock label (called once per second).</summary>
        private void UpdateLiveClock(DateTime now)
        {
            if (lblLiveClock == null || lblLiveClock.IsDisposed)
            {
                return;
            }
            lblLiveClock.Text = $"● LIVE  {now:hh:mm:ss tt}";
        }

        /// <summary>Updates the NEXT UP block from the scheduler's cached Next Up (targeted, no scan).</summary>
        private void UpdateNextUpDisplay()
        {
            if (lblNextUpTime == null || lblNextUpTime.IsDisposed)
            {
                return;
            }

            if (scheduler.NextUp == null)
            {
                lblNextUpTime.Text = "Nothing scheduled";
                lblNextUpName.Text = "Double-click any day on the calendar to plan your next task.";
                lblNextUpCountdown.Text = string.Empty;
                return;
            }

            lblNextUpTime.Text = AlarmEngine.GetScheduledTimeLabel(scheduler.NextUp);
            lblNextUpName.Text = scheduler.NextUp.Title;
            lblNextUpCountdown.Text = FormatCountdown(scheduler.Countdown);
        }

        private static string FormatCountdown(TimeSpan remaining)
        {
            if (remaining <= TimeSpan.Zero)
            {
                return "NOW";
            }
            return $"IN {remaining:hh\\:mm\\:ss}";
        }

        private static GraphicsPath GetRoundRectPath(Rectangle r, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private void RoundControl(Control ctrl, int radius)
        {
            if (ctrl.Width <= 0 || ctrl.Height <= 0) return;
            Region? legacyRegion = ctrl.Region;
            ctrl.Region = new Region(GetRoundRectPath(new Rectangle(0, 0, ctrl.Width, ctrl.Height), radius));
            legacyRegion?.Dispose();
        }

        private void InitializeServices()
        {
            // Alarm/reminder notifications (same handlers as before, now driven by the scheduler).
            scheduler.AlarmFired += OnAlarmFired;
            scheduler.ReminderFired += OnReminderFired;

            // When any task transitions, persist deterministically and refresh counters/views.
            scheduler.StateChanged += () =>
            {
                RefreshDataCounters(); // also persists (RefreshDataCounters saves)
                UpdateNextUpDisplay();
            };

            // Next Up / countdown changes update the dedicated NEXT UP block (targeted, cheap
            // updates — no scanning).
            scheduler.NextUpChanged += (task, remaining) =>
            {
                UpdateNextUpDisplay();
            };

            // A single 1-second timer drives the live clock. The TimeService turns each second
            // into cheap per-second ticks plus rare minute/date/resume boundaries.
            backgroundAlarmTicker = new System.Windows.Forms.Timer { Interval = 1000 };
            backgroundAlarmTicker.Tick += OnClockEngineTick;
            backgroundAlarmTicker.Start();

            timeService.SecondChanged += OnSecondChanged;
            timeService.MinuteChanged += OnMinuteOrResume;
            timeService.Resumed += OnMinuteOrResume;
            timeService.DateChanged += OnDateRollover;

            // Re-sync immediately whenever the app regains focus (timer may have been suspended).
            this.Activated += (s, e) => timeService.OnResumed();
        }

        /// <summary>Per-second: refresh only the live clock + NEXT UP countdown (cheap, no scan).</summary>
        private void OnSecondChanged(DateTime now)
        {
            UpdateLiveClock(now);
            scheduler.RefreshCountdown(now);
        }

        /// <summary>Minute/resume boundary: evaluate task transitions + recompute Next Up.</summary>
        private void OnMinuteOrResume(DateTime now)
        {
            scheduler.CheckTransitions(now);
        }

        /// <summary>Date rollover (midnight / new month): align the scheduler and calendar to the new day.</summary>
        private void OnDateRollover(DateTime now)
        {
            // Retire any schedules whose date has now passed and recompute Next Up.
            scheduler.CheckTransitions(now);
            if (displayedCalendarMonth.Year == now.Year && displayedCalendarMonth.Month == now.Month)
            {
                RefreshDataCounters();
                UpdateNextUpDisplay();
            }
        }

        private void OnClockEngineTick(object? sender, EventArgs e)
        {
            timeService.Pulse();
        }

        private void OnAlarmFired(TaskItem task)
        {
            // §20 Notifications section: the toggles gate the alarm's *surfaces* (sound,
            // notification) — never the scheduler itself.
            AppSettings settings = SettingsService.Load();
            try
            {
                if (settings.EnableAlarmSound)
                {
                    audioService.PlayAlarmLoop(Path.Combine(AppContext.BaseDirectory, "Assets"));
                }
                if (settings.EnableNotifications)
                {
                    if (tray != null && tray.IsHidden)
                    {
                        // Window not visible — surface the alarm as a tray balloon so it isn't
                        // missed. (A balloon can't take a snooze answer; restoring the window
                        // keeps the task one click away to complete or reschedule.)
                        tray.ShowToast("Alarm", $"\"{task.Title}\" is alerting.", ToolTipIcon.Warning);
                    }
                    else
                    {
                        // Snooze lives HERE now (the former bottom status bar was removed): the
                        // prompt appears exactly when the alarm fires, where the decision is made.
                        DialogResult answer = MessageBox.Show(
                            $"Alarm Triggered:\n\n\"{task.Title}\"\n\nSnooze for 9 minutes?",
                            "Alarm Alert",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Warning);
                        if (answer == DialogResult.Yes)
                        {
                            scheduler.Snooze(task, TimeSpan.FromMinutes(9), clock.Now);
                            RefreshDataCounters(); // persists the postponed schedule
                        }
                    }
                }
            }
            finally
            {
                audioService.StopAlarmLoop();
            }
        }

        private void OnReminderFired(TaskItem task)
        {
            AppSettings settings = SettingsService.Load();
            if (!settings.EnableNotifications)
            {
                return;
            }
            System.Media.SystemSounds.Asterisk.Play();
            if (tray != null && tray.IsHidden)
            {
                tray.ShowToast("Reminder", $"\"{task.Title}\" is due.", ToolTipIcon.Info);
            }
            else
            {
                MessageBox.Show($"Task Reminder:\n\n\"{task.Title}\"", "Task Notification", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void ApplyTheme(ThemePreset newTheme)
        {
            currentTheme = newTheme;
            // Keep the SHARED singleton in sync — ApplyTheme is the one application point for
            // every theme change (startup, the Settings dropdown, settings commits, OS theme
            // flips), but ResolveEffective() is a pure function that never sets Current. Without
            // this line, every consumer that reads ThemeService.Shared.Current (Settings page,
            // dialogs, the control factory, scrollbar theming) silently fell back to the Light
            // preset while MainForm rendered from its own local field — the two stayed in sync
            // only at startup (ActivateEffectiveFromSettings).
            themeService.Current = newTheme;

            // Every icon ink shifts with the theme — release the shared raster cache so it
            // only ever holds one theme's worth of bitmaps (render results are cached by
            // (kind, ink, size); the old-theme entries would otherwise sit stale forever).
            IconSet.ClearCache();

            // The FAB persists across themes (it lives on mainContainer, not the rebuilt
            // view) — retint its disc; its cached shadow/face bitmaps rebuild once, on the
            // next paint, only because the ink actually changed.
            taskFab?.SetInk(currentTheme.AccentColor);

            // Persistent container backgrounds. The FORM's BackColor is the scrim the
            // background image blends over — it must follow the theme. mainContainer stays
            // transparent (see BuildDesktopInterface) so the image shows site-wide.
            BackColor = currentTheme.PrimaryBg;

            // The persistent scroll host's native scrollbar follows the theme; the view
            // panels below are rebuilt by RenderActiveView and re-track themselves.
            if (contentPanel != null) NativeScrollbarTheme.Reapply(contentPanel);

            // Persistent header controls
            if (lblGreeting != null) lblGreeting.ForeColor = currentTheme.TextColor;
            if (lblHeaderSubtitle != null) lblHeaderSubtitle.ForeColor = currentTheme.MutedTextColor;

            if (txtSearch != null)
            {
                txtSearch.FillColor = currentTheme.CardBgColor;
                txtSearch.ForeColor = currentTheme.TextColor;
                txtSearch.PlaceholderForeColor = currentTheme.MutedTextColor;
                txtSearch.BorderColor = currentTheme.BorderColor;
                txtSearch.FocusedState.BorderColor = currentTheme.TextColor;
                // The old IconLeft is NOT disposed — IconSet.Render returns shared cached
                // bitmaps now; the cache itself was cleared at the top of ApplyTheme.
                txtSearch.IconLeft = IconSet.Render(IconKind.Search, currentTheme.MutedTextColor, 16);
            }

            if (btnNewTask != null)
            {
                btnNewTask.FillColor = currentTheme.AccentColor;
                btnNewTask.ForeColor = Color.White;
                btnNewTask.HoverState.FillColor = HoverAccent();
                btnNewTask.Image = IconSet.Render(IconKind.Plus, Color.White, 16);
            }

            // Persistent sidebar controls
            if (lblAppLogo != null) lblAppLogo.ForeColor = currentTheme.TextColor;

            if (lblSidebarIcon != null) lblSidebarIcon.ForeColor = currentTheme.TextColor;
            if (lblSidebarRemaining != null) lblSidebarRemaining.ForeColor = currentTheme.TextColor;
            if (lblSidebarPercent != null) lblSidebarPercent.ForeColor = currentTheme.MutedTextColor;
            if (lblSidebarEncouragement != null) lblSidebarEncouragement.ForeColor = currentTheme.MutedTextColor;

            if (sidebarProgressTrack != null)
            {
                sidebarProgressTrack.BackColor = currentTheme.BorderColor;
            }
            if (sidebarProgressFill != null)
            {
                sidebarProgressFill.BackColor = currentTheme.TextColor;
            }

            if (btnDashboard != null) btnDashboard.HoverState.FillColor = currentTheme.CardHoverBg;
            if (btnTasks != null) btnTasks.HoverState.FillColor = currentTheme.CardHoverBg;
            if (btnCalendar != null) btnCalendar.HoverState.FillColor = currentTheme.CardHoverBg;
            if (btnAmbient != null) btnAmbient.HoverState.FillColor = currentTheme.CardHoverBg;
            if (btnSettings != null) btnSettings.HoverState.FillColor = currentTheme.CardHoverBg;

            RefreshGlassSurfaces();

            // NOTE: persistence is the caller's job — ApplyThemeMode saves the user's choice
            // (including "System"); OS-preference flips must NOT overwrite "System" with the
            // resolved preset name.

            // Re-render the active view to paint its controls with the new theme colors
            RenderActiveView();
        }

        /// <summary>Applies a theme MODE chosen in Settings ("Light"/"Dark"/"System"): persist
        /// the mode, resolve it to an effective preset, and apply.</summary>
        private void ApplyThemeMode(string mode)
        {
            SettingsService.SaveThemeName(mode);
            ApplyTheme(themeService.ResolveEffective(mode));
        }

        /// <summary>Re-applies live settings on a Settings commit: refreshes the Animations
        /// snapshot, the system theme, and the effective theme. Transparency/radius/density are
        /// locked constants now, so there is no per-slider preview path anymore — commits are
        /// the only trigger.</summary>
        private void ApplyAppearanceSettings()
        {
            AppSettings settings = SettingsService.Load();
            Appearance.Apply(settings);
            SystemThemeProvider.Refresh();
            ApplyTheme(themeService.ResolveEffective(settings.Theme));
            // Background image settings (opacity / monochrome) are baked into the bitmap —
            // reload the layer so the new values take effect.
            ApplyStoredBackground();
        }

        /// <summary>Recomputes the translucent ARGB surfaces of the persistent chrome (sidebar,
        /// status bar, progress card). All alpha math flows through GlassSurface — no ARGB here.
        /// The percentages are locked constants now, so this runs on theme changes rather than
        /// slider drags, but it stays the one hook for refreshing the chrome surfaces.</summary>
        private void RefreshGlassSurfaces()
        {
            if (sidebarPanel != null)
            {
                sidebarPanel.FillColor = GlassSurface.SidebarFill(currentTheme);
            }
            if (sidebarProgressCard != null)
            {
                sidebarProgressCard.FillColor = GlassSurface.CardFill(currentTheme);
                sidebarProgressCard.BorderColor = GlassSurface.CardBorder(currentTheme);
            }
            if (navFlow != null)
            {
                // Hairline divider between the nav groups repaints with the current glass border.
                navFlow.Invalidate(true);
            }
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Hide-to-tray instead of exiting when the user clicks the close (X), if enabled.
            // Only the tray menu's Exit triggers a true close (alarms must keep firing while hidden).
            if (!userInitiatedExit && SettingsService.Load().MinimizeToTray)
            {
                e.Cancel = true;
                Hide();
                tray.MarkHidden();
                base.OnFormClosing(e);
                return;
            }

            Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnOsPreferenceChanged;
            tray?.HideTray();
            tray?.Dispose();
            audioService.Dispose();
            base.OnFormClosing(e);
        }
    }
}
