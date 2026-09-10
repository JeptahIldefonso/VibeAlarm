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
            ["Ambient"] = IconKind.Volume,
            ["Settings"] = IconKind.Settings
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

        // Global "Next Up" status bar (persistent strip, visible on every view)
        private Guna2Panel statusBarPanel = null!;
        private Label lblStatusNextUpName = null!;
        private Label lblStatusNextUpTime = null!;
        private Label lblStatusNextUpCountdown = null!;
        private Button btnStatusSnooze = null!;
        private Button btnStatusDismiss = null!;
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
            Image? previous = this.BackgroundImage;
            this.BackgroundImageLayout = ImageLayout.Zoom;
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
            ClientSize = new Size(1280, 760);
            MinimumSize = new Size(1000, 650);
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterScreen;
            Font = VibeAlarmPalette.Body(10F);
            BackColor = PrimaryBg;

            // Base Layout Splits — glass sidebar (Guna renders ARGB fills; a plain Panel cannot
            // alpha-blend) + main workspace.
            sidebarPanel = new Guna2Panel
            {
                Dock = DockStyle.Left,
                Width = VibeAlarmPalette.SidebarWidth,
                FillColor = GlassSurface.SidebarFill(currentTheme, Appearance.Current),
                BorderThickness = 0,
                BorderRadius = 0,
                Padding = new Padding(0, DesignTokens.Spacing.Lg, 0, DesignTokens.Spacing.Md)
            };
            sidebarPanel.ShadowDecoration.Enabled = false;
            sidebarPanel.Paint += (s, e) =>
            {
                using Pen hairline = new Pen(GlassSurface.CardBorder(currentTheme, Appearance.Current), 1);
                e.Graphics.DrawLine(hairline, sidebarPanel.Width - 1, 0, sidebarPanel.Width - 1, sidebarPanel.Height);
            };
            mainContainer = new Panel { Dock = DockStyle.Fill, BackColor = PrimaryBg, Padding = new Padding(DesignTokens.Spacing.Xl, DesignTokens.Spacing.Md, DesignTokens.Spacing.Xl, DesignTokens.Spacing.Md) };

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
                using Pen hairline = new Pen(GlassSurface.CardBorder(currentTheme, Appearance.Current), 1);
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
            sidebarProgressCard = UIControlFactory.CreateCardPanel(preset: currentTheme, settings: Appearance.Current);
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
            BuildGlobalStatusBar();

            mainContainer.Controls.Add(contentPanel);
            mainContainer.Controls.Add(statusBarPanel);
            mainContainer.Controls.Add(headerPanel);

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

        /// <summary>Persistent, app-wide "Next Up" strip pinned to the bottom of the window and
        /// visible on every view (Home/Tasks/Calendar/Ambient/Settings), not just the Calendar.
        /// Fed by the scheduler's cached Next Up, so the per-second tick keeps its countdown live.</summary>
        private void BuildGlobalStatusBar()
        {
            statusBarPanel = new Guna2Panel
            {
                Dock = DockStyle.Bottom,
                Height = 58,
                FillColor = GlassSurface.PanelFill(currentTheme, Appearance.Current),
                BorderThickness = 0,
                BorderRadius = 0,
                Padding = new Padding(DesignTokens.Spacing.Lg, 0, DesignTokens.Spacing.Lg, 0)
            };
            statusBarPanel.ShadowDecoration.Enabled = false;
            statusBarPanel.Paint += (s, e) =>
            {
                using Pen hairline = new Pen(GlassSurface.PanelBorder(currentTheme, Appearance.Current), 1);
                e.Graphics.DrawLine(hairline, 0, 0, statusBarPanel.Width, 0);
            };

            Label lblStatusCaption = CreateLabel("NEXT UP", Point.Empty, new Size(0, 16), 8.5F, FontStyle.Bold, MutedTextColor);
            lblStatusCaption.Font = VibeAlarmPalette.Body(8.5F, FontStyle.Bold);
            lblStatusCaption.Dock = DockStyle.Fill;
            lblStatusCaption.TextAlign = ContentAlignment.MiddleLeft;
            lblStatusCaption.Margin = new Padding(0, 0, 24, 0);

            // Left info cluster: title over time, stacked with automatic spacing.
            TableLayoutPanel info = new TableLayoutPanel
            {
                RowCount = 2,
                ColumnCount = 1,
                BackColor = Color.Transparent
            };
            info.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            info.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            lblStatusNextUpName = CreateLabel("No upcoming schedules.", Point.Empty, new Size(0, 18), 11.5F, FontStyle.Bold, TextColor);
            lblStatusNextUpName.Dock = DockStyle.Fill;
            lblStatusNextUpName.TextAlign = ContentAlignment.MiddleLeft;
            lblStatusNextUpName.Margin = new Padding(0);
            lblStatusNextUpTime = CreateLabel("--:-- --", Point.Empty, new Size(0, 20), 10F, FontStyle.Regular, MutedTextColor);
            lblStatusNextUpTime.Dock = DockStyle.Fill;
            lblStatusNextUpTime.TextAlign = ContentAlignment.MiddleLeft;
            lblStatusNextUpTime.Margin = new Padding(0);
            info.Controls.Add(lblStatusNextUpName);
            info.Controls.Add(lblStatusNextUpTime);

            // Countdown fills the middle and right-aligns itself regardless of label widths.
            lblStatusNextUpCountdown = CreateLabel("—", Point.Empty, new Size(0, 28), 15F, FontStyle.Bold, TextColor);
            lblStatusNextUpCountdown.Font = VibeAlarmPalette.Mono(15F, FontStyle.Bold);
            lblStatusNextUpCountdown.Dock = DockStyle.Fill;
            lblStatusNextUpCountdown.TextAlign = ContentAlignment.MiddleRight;
            lblStatusNextUpCountdown.Margin = new Padding(24, 0, 24, 0);

            // Matched action pair. Buttons size to content (MinWidth is a floor, not a ceiling)
            // so letter-distance never clips "SNOOZE +9M" vs "DISMISS" differently (§10.2).
            FlowLayoutPanel actions = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent,
                Margin = new Padding(24, 0, 0, 0)
            };

            btnStatusSnooze = CreateStatusActionButton("SNOOZE", AccentColor);
            btnStatusSnooze.AutoSize = true;
            btnStatusSnooze.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            btnStatusSnooze.MinimumSize = new Size(96, 34);
            btnStatusSnooze.Padding = new Padding(16, 0, 16, 0);
            btnStatusSnooze.Margin = new Padding(0, 12, 10, 12);
            btnStatusSnooze.AccessibleName = "Snooze next up for nine minutes";
            btnStatusSnooze.Click += (_, _) => SnoozeFromStatusBar();

            btnStatusDismiss = CreateStatusActionButton("DISMISS", TextColor);
            btnStatusDismiss.AutoSize = true;
            btnStatusDismiss.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            btnStatusDismiss.MinimumSize = new Size(96, 34);
            btnStatusDismiss.Padding = new Padding(16, 0, 16, 0);
            btnStatusDismiss.Margin = new Padding(10, 12, 0, 12);
            btnStatusDismiss.AccessibleName = "Dismiss next up";
            btnStatusDismiss.Click += (_, _) => DismissFromStatusBar();

            actions.Controls.Add(btnStatusSnooze);
            actions.Controls.Add(btnStatusDismiss);

            // Auto, Auto, *, Auto — guaranteed alignment regardless of text length (§10.4).
            TableLayoutPanel bar = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                RowCount = 1,
                ColumnCount = 4
            };
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bar.Controls.Add(lblStatusCaption, 0, 0);
            bar.Controls.Add(info, 1, 0);
            bar.Controls.Add(lblStatusNextUpCountdown, 2, 0);
            bar.Controls.Add(actions, 3, 0);
            statusBarPanel.Controls.Add(bar);
        }

        private void ExecuteModalTaskCreationDialogue()
        {
            using (TaskCreateDialog dialog = new TaskCreateDialog(clock))
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
                Image prev = btn.Image;
                btn.Image = IconSet.Render(NavIcons[key], isCurrent ? currentTheme.AccentColor : MutedTextColor, 20);
                prev?.Dispose();
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
            calendarLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // month header
            calendarLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // next up
            calendarLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 48F)); // month grid
            calendarLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // selected-day summary
            calendarLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 52F)); // day list

            // ---- Month header: title left, [<] [Today] [>] right ----
            Guna2Panel monthHeader = UIControlFactory.CreateCardPanel(preset: currentTheme, settings: Appearance.Current);
            monthHeader.Dock = DockStyle.Fill;
            monthHeader.Height = 58;
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

            // ---- NEXT UP block — prominent nearest upcoming schedule with live countdown ----
            Guna2Panel nextUpCard = UIControlFactory.CreateCardPanel(preset: currentTheme, settings: Appearance.Current);
            nextUpCard.Dock = DockStyle.Fill;
            nextUpCard.Height = 116;
            Label lblNextUpTitle = CreateLabel("NEXT UP", new Point(DesignTokens.Spacing.Lg, 16), new Size(160, 18), 9.5F, FontStyle.Bold, MutedTextColor);
            lblNextUpTitle.Font = VibeAlarmPalette.Body(9.5F, FontStyle.Bold);
            lblNextUpTime = CreateLabel("--:-- --", new Point(DesignTokens.Spacing.Lg, 34), new Size(420, 38), 26F, FontStyle.Bold, TextColor);
            lblNextUpTime.Font = VibeAlarmPalette.Display(20F, FontStyle.Bold);
            lblNextUpName = CreateLabel("No upcoming schedules.", new Point(DesignTokens.Spacing.Lg, 78), new Size(460, 22), 12F, FontStyle.Regular, MutedTextColor);
            lblNextUpName.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            lblNextUpCountdown = CreateLabel("—", new Point(700, 42), new Size(246, 36), 17F, FontStyle.Bold, TextColor);
            lblNextUpCountdown.Font = VibeAlarmPalette.Mono(15F, FontStyle.Bold);
            lblNextUpCountdown.TextAlign = ContentAlignment.MiddleRight;
            lblNextUpCountdown.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            nextUpCard.Controls.AddRange(new Control[] { lblNextUpTitle, lblNextUpTime, lblNextUpName, lblNextUpCountdown });
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

            // ---- Selected-day summary row ----
            Guna2Panel actionRow = UIControlFactory.CreatePanelSurface(preset: currentTheme, settings: Appearance.Current);
            actionRow.Dock = DockStyle.Fill;
            actionRow.Height = 44;
            lblCalendarProgress = CreateLabel("0 tasks completed today", new Point(DesignTokens.Spacing.Lg, 13), new Size(460, 22), 10.5F, FontStyle.Bold, TextColor);
            actionRow.Controls.Add(lblCalendarProgress);

            // LIVE clock chip — updates every second (via TimeService), independent of the calendar grid.
            lblLiveClock = CreateLabel(DateTime.Now.ToString("hh:mm:ss tt"), new Point(560, 10), new Size(386, 24), 11F, FontStyle.Bold, TextColor);
            lblLiveClock.Font = VibeAlarmPalette.Mono(11F, FontStyle.Bold);
            lblLiveClock.TextAlign = ContentAlignment.MiddleRight;
            lblLiveClock.Text = $"● LIVE  {DateTime.Now:hh:mm:ss tt}";
            lblLiveClock.Padding = new Padding(0, 0, DesignTokens.Spacing.Md, 0);
            lblLiveClock.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            actionRow.Controls.Add(lblLiveClock);
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

            // Day visual-state summary derived from the task lifecycle (no per-second work here).
            int scheduledCount = masterTaskList.Count(t => GetTaskDate(t).Date == date.Date);
            int completedCount = masterTaskList.Count(t =>
                t.GetState() == TaskState.Completed && GetTaskDate(t).Date == date.Date);
            int expiredCount = masterTaskList.Count(t =>
                t.GetState() == TaskState.Expired && GetTaskDate(t).Date == date.Date);
            int upcomingCount = masterTaskList.Count(t =>
                t.GetState() is TaskState.Scheduled or TaskState.Due && GetTaskDate(t).Date == date.Date);

            // Compact editorial status line beneath the day number.
            string status = string.Empty;
            if (upcomingCount > 0)
            {
                status = $"○ {scheduledCount}";
            }
            if (completedCount > 0)
            {
                status = $"✓ {completedCount} done";
            }
            if (expiredCount > 0 && status.Length == 0)
            {
                status = $"— {expiredCount} past";
            }

            // §14.4 Notion/Calendar: TODAY = number inside a filled accent circle; a small accent
            // dot under the number signals a day with tasks; SELECTED = accent-tinted wash.
            Color numberColor = isToday
                ? Color.White
                : isPast
                    ? Color.FromArgb(150, MutedTextColor)
                    : isCurrentMonth ? TextColor : MutedTextColor;
            Color statusColor = isPast ? Color.FromArgb(120, MutedTextColor) : MutedTextColor;

            Panel cell = new Panel
            {
                Tag = date,
                Dock = DockStyle.Fill,
                Margin = new Padding(3),
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand,
                AccessibleName = date.ToString("dddd, MMMM d, yyyy")
            };
            bool hovering = false;
            float dpr = DeviceDpi / 96F;
            int contentTop = (int)(6 * dpr);

            void DrawNumber(Graphics g)
            {
                Font numberFont = VibeAlarmPalette.Mono((float)(isToday ? 9.5 : 8.5), isToday ? FontStyle.Bold : FontStyle.Regular);
                using StringFormat fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                int d = (int)(20 * dpr);
                int cx = cell.Width / 2;
                RectangleF cellRect = new RectangleF(0, contentTop, cell.Width, d + 2);
                if (isToday)
                {
                    using SolidBrush circleBrush = new SolidBrush(AccentColor);
                    g.FillEllipse(circleBrush, cx - d / 2f, contentTop, d, d);
                }
                g.DrawString(date.Day.ToString(), numberFont, new SolidBrush(numberColor), cellRect, fmt);
            }

            void DrawStatus(Graphics g)
            {
                if (string.IsNullOrEmpty(status))
                {
                    return;
                }
                using Font statusFont = VibeAlarmPalette.Mono(6.5F);
                using StringFormat fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                RectangleF rect = new RectangleF(0, (int)(28 * dpr), cell.Width, 12);
                g.DrawString(status, statusFont, new SolidBrush(statusColor), rect, fmt);
            }

            void DrawDot(Graphics g)
            {
                if (scheduledCount <= 0)
                {
                    return;
                }
                using SolidBrush dotBrush = new SolidBrush(AccentColor);
                g.FillEllipse(dotBrush, cell.Width / 2f - 2.5f * dpr, cell.Height - (int)(8 * dpr), 5 * dpr, 5 * dpr);
            }

            cell.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle rect = new Rectangle(0, 0, cell.Width - 1, cell.Height - 1);
                using (GraphicsPath path = GetRoundRectPath(rect, DesignTokens.Radius.Small))
                {
                    using SolidBrush fillBrush = new SolidBrush(isSelected ? AccentTintColor : hovering ? CardHoverBg : Color.Transparent);
                    e.Graphics.FillPath(fillBrush, path);
                    if (isSelected)
                    {
                        using Pen ring = new Pen(AccentColor, 1.6F);
                        e.Graphics.DrawPath(ring, path);
                    }
                }
                DrawNumber(e.Graphics);
                DrawStatus(e.Graphics);
                DrawDot(e.Graphics);
            };

            cell.MouseEnter += (s, e) => { hovering = true; cell.Invalidate(); };
            cell.MouseLeave += (s, e) => { hovering = false; cell.Invalidate(); };
            cell.Click += (s, e) =>
            {
                if (cell.Tag is not DateTime clickedDate)
                {
                    return;
                }

                activeCalendarDate = clickedDate;
                activeCalendarDay = activeCalendarDate.DayOfWeek.ToString();
                displayedCalendarMonth = new DateTime(activeCalendarDate.Year, activeCalendarDate.Month, 1);
                RenderActiveView();
            };

            return cell;
        }

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

            // ---- TODAY info row: small chips on one card (no giant stat cards) ----
            Guna2Panel infoCard = UIControlFactory.CreateCardPanel(preset: currentTheme, settings: Appearance.Current);
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

            // §19–24: the whole settings page (scrolling section cards, two-column rows,
            // 0–100% transparency sliders) lives in SettingsPageView. MainForm only hosts it
            // and wires the five callbacks — the page owns no business logic.
            SettingsPageView settingsPage = new SettingsPageView(
                getTasks: () => masterTaskList,
                onThemeModeChanged: ApplyThemeMode,
                onAppearanceCommitted: ApplyAppearanceSettings,
                onAppearancePreview: RefreshGlassSurfaces,
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

            // ---- Hero: text left, Stop right ----
            Guna2Panel heroPanel = UIControlFactory.CreateCardPanel(preset: currentTheme, settings: Appearance.Current);
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
            Guna2Panel volumePanel = UIControlFactory.CreateCardPanel(preset: currentTheme, settings: Appearance.Current);
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
            Guna2Panel builtinPanel = UIControlFactory.CreateCardPanel(preset: currentTheme, settings: Appearance.Current);
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

            Guna2Panel card = UIControlFactory.CreateCardPanel(preset: currentTheme, settings: Appearance.Current);
            card.Size = new Size(rowWidth, cardHeight);
            card.Margin = new Padding(0, 0, 0, DesignTokens.Spacing.Sm);
            card.Padding = new Padding(DesignTokens.Spacing.Md, 0, DesignTokens.Spacing.Md, 0);

            // Hover: one quiet opaque step (GlassSurface.CardHover is intentionally opaque).
            Color restFill = card.FillColor;
            card.MouseEnter += (s, e) => card.FillColor = GlassSurface.CardHover(currentTheme);
            card.MouseLeave += (s, e) => card.FillColor = restFill;

            TableLayoutPanel row = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44));   // checkbox column
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F)); // text column
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));      // type dot + more

            // ---- Checkbox: quiet circle, filled check when completed ----
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
                btnToggle.Image = IconSet.Render(IconKind.Check, currentTheme.IsLight ? Color.White : Color.FromArgb(0x19, 0x19, 0x19), 14);
                btnToggle.ImageSize = new Size(14, 14);
            }
            btnToggle.HoverState.BorderColor = TextColor;
            btnToggle.Click += (s, e) =>
            {
                item.Completed = !item.Completed;
                RefreshDataCounters();
            };
            row.Controls.Add(btnToggle, 0, 0);

            // ---- Text: title over one-line metadata ("Today · 8:00 PM") ----
            TableLayoutPanel textBlock = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            textBlock.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            textBlock.RowStyles.Add(new RowStyle(SizeType.Percent, 55F));
            textBlock.RowStyles.Add(new RowStyle(SizeType.Percent, 45F));

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
                Font = VibeAlarmPalette.Body(8.5F),
                ForeColor = MutedTextColor,
                BackColor = Color.Transparent,
                Margin = new Padding(1, 2, 0, 0)
            };
            textBlock.Controls.Add(lblTitle, 0, 0);
            textBlock.Controls.Add(lblMeta, 0, 1);
            row.Controls.Add(textBlock, 1, 0);

            // ---- Type dot + label, then the more (…) button ----
            FlowLayoutPanel trailing = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent,
                Margin = new Padding(DesignTokens.Spacing.Md, 0, 0, 0)
            };

            Color typeColor = item.Type switch
            {
                SchedulerService.TaskTypeAlarm => currentTheme.ErrorColor,
                SchedulerService.TaskTypeImportant => currentTheme.WarningColor,
                _ => MutedTextColor
            };
            int dotVOffset = Math.Max(0, (cardHeight - 20) / 2);
            Label lblTypeDot = new Label
            {
                Text = "●",
                AutoSize = true,
                Font = new Font("Segoe UI", 8F),
                ForeColor = typeColor,
                BackColor = Color.Transparent,
                Margin = new Padding(2, dotVOffset + 4, 4, 0)
            };
            Label lblType = new Label
            {
                Text = item.Type,
                AutoSize = true,
                Font = VibeAlarmPalette.Body(8.5F),
                ForeColor = MutedTextColor,
                BackColor = Color.Transparent,
                Margin = new Padding(0, dotVOffset, 10, 0)
            };
            trailing.Controls.Add(lblTypeDot);
            trailing.Controls.Add(lblType);

            Guna2Button btnOptions = UIControlFactory.CreateIconButton(IconKind.More, preset: currentTheme, iconSize: 16, buttonSize: 30);
            btnOptions.Margin = new Padding(0, Math.Max(0, (cardHeight - 30) / 2), 0, 0);
            btnOptions.Click += (s, e) => ShowTaskContextMenu(item, btnOptions);
            trailing.Controls.Add(btnOptions);

            row.Controls.Add(trailing, 2, 0);
            card.Controls.Add(row);
            return card;
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
                Image = IconSet.Render(IconKind.Check, TextColor, 16)
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

        /// <summary>Status-bar action (SNOOZE / DISMISS). A bordered chip — quiet fill at rest,
        /// hairline border so it reads as a button rather than a bare hyperlink (§15.2).</summary>
        private Button CreateStatusActionButton(string text, Color ink)
        {
            Button btn = new Button
            {
                Text = text,
                FlatStyle = FlatStyle.Flat,
                Font = VibeAlarmPalette.Body(10F, FontStyle.Bold),
                BackColor = Color.Transparent,
                ForeColor = ink,
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 1;
            btn.FlatAppearance.BorderColor = BorderColor;
            btn.FlatAppearance.MouseOverBackColor = CardHoverBg;
            btn.FlatAppearance.MouseDownBackColor = Shade(CardHoverBg, 0.9F);
            RoundControl(btn, DesignTokens.Radius.Small);
            return btn;
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
                lblNextUpTime.Text = "--:-- --";
                lblNextUpName.Text = "No upcoming schedules.";
                lblNextUpCountdown.Text = "—";
                return;
            }

            lblNextUpTime.Text = AlarmEngine.GetScheduledTimeLabel(scheduler.NextUp);
            lblNextUpName.Text = scheduler.NextUp.Title;
            lblNextUpCountdown.Text = FormatCountdown(scheduler.Countdown);
        }

        /// <summary>Keeps the app-wide status bar in sync with the scheduler's Next Up / countdown.</summary>
        private void UpdateNextStatusBar()
        {
            if (statusBarPanel == null || statusBarPanel.IsDisposed)
            {
                return;
            }

            bool hasNext = scheduler.NextUp != null;
            btnStatusSnooze.Visible = hasNext;
            btnStatusDismiss.Visible = hasNext;

            if (!hasNext)
            {
                lblStatusNextUpName.Text = "No upcoming schedules.";
                lblStatusNextUpTime.Text = "";
                lblStatusNextUpCountdown.Text = "—";
                return;
            }

            lblStatusNextUpName.Text = scheduler.NextUp!.Title;
            lblStatusNextUpTime.Text = AlarmEngine.GetScheduledTimeLabel(scheduler.NextUp);
            lblStatusNextUpCountdown.Text = FormatCountdown(scheduler.Countdown);
        }

        private void SnoozeFromStatusBar()
        {
            if (scheduler.NextUp == null)
            {
                return;
            }
            scheduler.Snooze(scheduler.NextUp, TimeSpan.FromMinutes(9), clock.Now);
            RefreshDataCounters(); // persists the postponed schedule
        }

        private void DismissFromStatusBar()
        {
            if (scheduler.NextUp == null)
            {
                return;
            }
            var target = scheduler.NextUp;
            target.SetState(TaskState.Completed);
            TaskStorageService.Save(masterTaskList);
            scheduler.CheckTransitions(clock.Now); // recomputes Next Up (skips completed task)
            RefreshDataCounters();
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
                UpdateNextStatusBar();
            };

            // Next Up / countdown changes update both the dedicated NEXT UP block and the
            // app-wide status bar (targeted, cheap updates — no scanning).
            scheduler.NextUpChanged += (task, remaining) =>
            {
                UpdateNextUpDisplay();
                UpdateNextStatusBar();
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
            UpdateNextStatusBar();
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
                        // Window not visible — surface the alarm as a tray balloon so it isn't missed.
                        tray.ShowToast("Alarm", $"\"{task.Title}\" is alerting.", ToolTipIcon.Warning);
                    }
                    else
                    {
                        MessageBox.Show($"Alarm Triggered:\n\n\"{task.Title}\"", "Alarm Alert", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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

            // Persistent container backgrounds
            BackColor = currentTheme.PrimaryBg;
            if (mainContainer != null) mainContainer.BackColor = currentTheme.PrimaryBg;

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
                Image? prevIcon = txtSearch.IconLeft;
                txtSearch.IconLeft = IconSet.Render(IconKind.Search, currentTheme.MutedTextColor, 16);
                prevIcon?.Dispose();
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

        /// <summary>§24: re-applies appearance settings (transparency / radius / density) and
        /// the effective theme after any settings commit. Slider drags call the cheaper
        /// <see cref="RefreshGlassSurfaces"/> live and this on release.</summary>
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
        /// status bar, progress card). All alpha math flows through GlassSurface — no ARGB here.</summary>
        private void RefreshGlassSurfaces()
        {
            AppSettings settings = Appearance.Current;
            if (sidebarPanel != null)
            {
                sidebarPanel.FillColor = GlassSurface.SidebarFill(currentTheme, settings);
            }
            if (statusBarPanel != null)
            {
                statusBarPanel.FillColor = GlassSurface.PanelFill(currentTheme, settings);
                statusBarPanel.Invalidate(); // repaint the hairline top border
            }
            if (sidebarProgressCard != null)
            {
                sidebarProgressCard.FillColor = GlassSurface.CardFill(currentTheme, settings);
                sidebarProgressCard.BorderColor = GlassSurface.CardBorder(currentTheme, settings);
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
