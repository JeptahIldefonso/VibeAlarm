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
        private static readonly Color PurpleColor = VibeAlarmPalette.NotificationMark;

        private System.Windows.Forms.Timer backgroundAlarmTicker = null!;
        private TrayService tray = null!;
        private bool userInitiatedExit;

        // Layout Shell Containers
        private Panel sidebarPanel = null!;
        private Panel mainContainer = null!;
        private Panel headerPanel = null!;
        private Panel contentPanel = null!;

        // Header Input Items
        private TextBox txtSearch = null!;
        private Button btnNewTask = null!;

        // Navigation Group Links
        private Guna2Button btnDashboard = null!;
        private Guna2Button btnTasks = null!;
        private Guna2Button btnCalendar = null!;
        private Guna2Button btnAmbient = null!;
        private Guna2Button btnSettings = null!;
        private readonly Dictionary<string, string> navGlyphCodes = new();

        // Interactive Labels
        private Label lblGreeting = null!;
        private Label lblHeaderSubtitle = null!;
        private Label lblStatActiveCount = null!;
        private Label lblStatDoneCount = null!;
        private Label lblSidebarRemaining = null!;
        private Label lblSidebarPercent = null!;
        private Panel sidebarProgressTrack = null!;
        private Panel sidebarProgressFill = null!;

        // Persistent controls stored for live theme application
        private Label lblAppLogo = null!;
        private Panel sidebarProgressCard = null!;
        private Label lblSidebarIcon = null!;
        private Label lblSidebarEncouragement = null!;

        // Dynamic Panels
        private FlowLayoutPanel taskListPanel = null!;
        private FlowLayoutPanel dashboardFocusPanel = null!;
        private TableLayoutPanel calendarStripMatrix = null!;
        private FlowLayoutPanel calendarListPanel = null!;
        private Label lblCalendarProgress = null!;
        private Label lblCalendarMonth = null!;
        private Label lblLiveClock = null!;
        private Label lblNextUpTime = null!;
        private Label lblNextUpName = null!;
        private Label lblNextUpCountdown = null!;
        private Label lblTaskCount = null!;

        // Global "Next Up" status bar (Spotify-style pinned strip, visible on every view)
        private Panel statusBarPanel = null!;
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

            currentTheme = themeService.ActivateFromSettings();
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
            MinimumSize = new Size(1180, 700);
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterScreen;
            Font = VibeAlarmPalette.Body(10F);
            BackColor = PrimaryBg;

            // Base Layout Splits
            sidebarPanel = new Panel { Dock = DockStyle.Left, Width = VibeAlarmPalette.SidebarWidth, BackColor = SidebarBg, Padding = new Padding(0, 24, 0, 0) };
            sidebarPanel.Paint += (s, e) =>
            {
                using Pen hairline = new Pen(BorderColor, 1);
                e.Graphics.DrawLine(hairline, sidebarPanel.Width - 1, 0, sidebarPanel.Width - 1, sidebarPanel.Height);
            };
            mainContainer = new Panel { Dock = DockStyle.Fill, BackColor = PrimaryBg, Padding = new Padding(34, 28, 36, 28) };

            Controls.Add(mainContainer);
            Controls.Add(sidebarPanel);

            BuildSidebarNavigation();
            BuildMainWorkspaceLayout();
        }

        private void BuildSidebarNavigation()
        {
            lblAppLogo = new Label
            {
                Text = "VibeAlarm",
                Location = new Point(24, 30),
                Size = new Size(180, 34),
                Font = VibeAlarmPalette.Display(20F, FontStyle.Bold),
                ForeColor = TextColor,
                BackColor = Color.Transparent
            };
            sidebarPanel.Controls.Add(lblAppLogo);

            btnDashboard = CreateSidebarButton("E80F", "Home", "Dashboard", 96);
            btnTasks = CreateSidebarButton("E9D5", "Tasks", "Tasks", 140);
            btnCalendar = CreateSidebarButton("E787", "Calendar", "Calendar", 184);
            btnAmbient = CreateSidebarButton("E767", "Ambient", "Ambient", 228);

            // Hairline rule above SETTINGS: the bottom-pinned group is intentional, not a gap (§10.5).
            Panel settingsDivider = new Panel
            {
                Location = new Point(24, 312),
                Size = new Size(VibeAlarmPalette.SidebarWidth - 48, 8),
                BackColor = Color.Transparent,
                Anchor = AnchorStyles.Left | AnchorStyles.Top
            };
            settingsDivider.Paint += (s, e) =>
            {
                using Pen hairline = new Pen(BorderColor, 1);
                e.Graphics.DrawLine(hairline, 0, 4, settingsDivider.Width, 4);
            };
            sidebarPanel.Controls.Add(settingsDivider);

            btnSettings = CreateSidebarButton("E713", "Settings", "Settings", 340);

            sidebarPanel.Controls.AddRange(new Control[] { btnDashboard, btnTasks, btnCalendar, btnAmbient, btnSettings });
            BuildSidebarProgressCard();
        }

        private Guna2Button CreateSidebarButton(string glyph, string label, string viewKey, int topPosition)
        {
            // §14.3 Notion sidebar: icon + label, full-height "pill" for the active row, no
            // numbered prefix. The glyph and caption live on the button itself (Image + Text)
            // rather than as child controls: a child label over a Guna2Button swallows the
            // mouse input, killing Click entirely (§15.1).
            Guna2Button btn = new Guna2Button
            {
                Tag = viewKey,
                Location = new Point(0, topPosition),
                Size = new Size(VibeAlarmPalette.SidebarWidth, 44),
                FillColor = Color.Transparent,
                ForeColor = MutedTextColor,
                BorderThickness = 0,
                BorderRadius = 22, // <44 height / 2> = pill
                Cursor = Cursors.Hand,
                Animated = false,
                Font = VibeAlarmPalette.Body(10F),
                Text = label,
                TextAlign = HorizontalAlignment.Left,
                TextOffset = new Point(46, 0), // clear of the 22px glyph slot + gap
                ImageAlign = HorizontalAlignment.Left,
                ImageOffset = new Point(16, 0),
                ImageSize = new Size(22, 22)
            };
            btn.HoverState.FillColor = currentTheme.CardHoverBg;
            btn.HoverState.ForeColor = currentTheme.TextColor;
            navGlyphCodes[viewKey] = glyph;
            btn.Image = RenderNavGlyph(glyph, MutedTextColor);

            btn.Click += (s, e) =>
            {
                activeView = viewKey;
                RenderActiveView();
            };

            return btn;
        }

        /// <summary>Renders a sidebar glyph to a small bitmap for Guna2Button.Image. The drawn
        /// color is baked in, so RenderActiveView re-renders it when the active row changes.</summary>
        private Bitmap RenderNavGlyph(string glyph, Color color)
        {
            string glyphText = FontRegistry.HasSymbolFonts ? ((char)Convert.ToInt32(glyph, 16)).ToString() : "●";
            Bitmap bmp = new Bitmap(22, 22);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using Font f = FontRegistry.HasSymbolFonts ? FontRegistry.Symbol(12F) : VibeAlarmPalette.Mono(12F, FontStyle.Bold);
                using SolidBrush b = new SolidBrush(color);
                using StringFormat sf = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                };
                g.DrawString(glyphText, f, b, new RectangleF(0, 0, 22, 22), sf);
            }
            return bmp;
        }

        private void BuildSidebarProgressCard()
        {
            sidebarProgressCard = CreateCard(new Point(16, 560), new Size(188, 148), DesignTokens.Radius.Medium, CardBgColor, BorderColor);
            sidebarProgressCard.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;

            lblSidebarIcon = CreateLabel("●", new Point(16, 16), new Size(24, 24), 15F, FontStyle.Bold, TextColor);
            lblSidebarIcon.Font = new Font("Segoe UI Symbol", 15F, FontStyle.Regular);
            lblSidebarRemaining = CreateLabel("0 Tasks Remaining", new Point(16, 46), new Size(156, 22), 9.5F, FontStyle.Bold, TextColor);
            lblSidebarEncouragement = CreateLabel("Keep going!", new Point(16, 68), new Size(156, 18), 8.5F, FontStyle.Regular, MutedTextColor);

            sidebarProgressTrack = new Panel { Location = new Point(16, 104), Size = new Size(116, 6), BackColor = currentTheme.BorderColor };
            sidebarProgressFill = new Panel { Location = new Point(0, 0), Size = new Size(0, 6), BackColor = TextColor };
            lblSidebarPercent = CreateLabel("0%", new Point(146, 96), new Size(32, 20), 9F, FontStyle.Bold, MutedTextColor);

            sidebarProgressTrack.Controls.Add(sidebarProgressFill);
            sidebarProgressCard.Controls.AddRange(new Control[] { lblSidebarIcon, lblSidebarRemaining, lblSidebarEncouragement, sidebarProgressTrack, lblSidebarPercent });
            sidebarPanel.Controls.Add(sidebarProgressCard);
        }

        private void BuildMainWorkspaceLayout()
        {
            headerPanel = new Panel { Dock = DockStyle.Top, Height = 112, BackColor = Color.Transparent };
            contentPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 10, 0, 0), BackColor = Color.Transparent, AutoScroll = true, AutoScrollMargin = new Size(0, 24) };
            BuildGlobalStatusBar();

            mainContainer.Controls.Add(contentPanel);
            mainContainer.Controls.Add(statusBarPanel);
            mainContainer.Controls.Add(headerPanel);

            // Responsive Greeting Setup
            lblGreeting = new Label
            {
                Text = "Good Evening, Jeptah",
                Location = new Point(0, 16),
                Size = new Size(600, 44),
                Font = VibeAlarmPalette.Display(25F, FontStyle.Bold),
                ForeColor = TextColor,
                BackColor = Color.Transparent
            };
            lblHeaderSubtitle = CreateLabel("You have 0 tasks remaining today.", new Point(2, 62), new Size(520, 24), 12F, FontStyle.Regular, MutedTextColor);
            headerPanel.Controls.AddRange(new Control[] { lblGreeting, lblHeaderSubtitle });

            // Optimized Search Bar Placement
            txtSearch = new TextBox
            {
                Location = new Point(520, 28),
                Size = new Size(310, 40),
                Font = VibeAlarmPalette.Body(12F),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = CardBgColor,
                ForeColor = MutedTextColor,
                Text = "  Search tasks...",
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };

            // Never leave the caret scrolled to the end on a fresh box — that renders the
            // placeholder's tail ("ks...") instead of its start (§10.1).
            txtSearch.SelectionStart = 0;
            txtSearch.SelectionLength = 0;

            txtSearch.GotFocus += (s, e) => {
                if (txtSearch.Text == PlaceholderSearch || txtSearch.Text.Trim() == "Search tasks...")
                {
                    txtSearch.Text = string.Empty;
                    txtSearch.ForeColor = TextColor;
                }
            };

            txtSearch.LostFocus += (s, e) => {
                if (string.IsNullOrWhiteSpace(txtSearch.Text))
                {
                    txtSearch.Text = "  Search tasks...";
                    txtSearch.ForeColor = MutedTextColor;
                }
            };

            txtSearch.TextChanged += (s, e) =>
            {
                searchFilterQuery = txtSearch.Text == PlaceholderSearch || txtSearch.Text.Trim() == "Search tasks..." ? string.Empty : txtSearch.Text.Trim();
                RefreshDataCounters();
            };
            headerPanel.Controls.Add(txtSearch);

            // Optimized New Task Action Layout Position
            btnNewTask = CreatePrimaryButton("+  New Task", new Point(854, 30), new Size(142, 38));
            btnNewTask.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnNewTask.Click += (s, e) => { ExecuteModalTaskCreationDialogue(); };
            headerPanel.Controls.Add(btnNewTask);
            headerPanel.Resize += (s, e) => ArrangeHeaderActions();
            ArrangeHeaderActions();
        }

        /// <summary>Persistent, app-wide "Next Up" strip pinned to the bottom of the window and
        /// visible on every view (Home/Tasks/Calendar/Ambient/Settings), not just the Calendar.
        /// Fed by the scheduler's cached Next Up, so the per-second tick keeps its countdown live.</summary>
        private void BuildGlobalStatusBar()
        {
            statusBarPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 58,
                BackColor = CardBgColor,
                Padding = new Padding(24, 0, 24, 0)
            };
            statusBarPanel.Paint += (s, e) =>
            {
                using Pen hairline = new Pen(BorderColor, 1);
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

        private void ArrangeHeaderActions()
        {
            if (txtSearch == null || btnNewTask == null || headerPanel == null)
            {
                return;
            }

            const int gap = 24;
            int buttonWidth = 142;
            const int btnHeight = 38;
            const int searchHeight = 40;
            int rightEdge = headerPanel.ClientSize.Width;
            int desiredSearchWidth = 310;
            int availableSearchWidth = rightEdge - buttonWidth - gap - 520;
            int searchWidth = Math.Max(240, Math.Min(desiredSearchWidth, availableSearchWidth));
            int btnTop = 28 + (searchHeight - btnHeight) / 2; // center the compact button against the search box

            btnNewTask.Location = new Point(Math.Max(0, rightEdge - buttonWidth), btnTop);
            btnNewTask.Size = new Size(buttonWidth, btnHeight);
            txtSearch.Location = new Point(Math.Max(0, btnNewTask.Left - gap - searchWidth), 28);
            txtSearch.Size = new Size(searchWidth, searchHeight);
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
                if (navGlyphCodes.TryGetValue(key, out string? code))
                {
                    Image prev = btn.Image;
                    btn.Image = RenderNavGlyph(code, isCurrent ? currentTheme.AccentColor : MutedTextColor);
                    prev?.Dispose();
                }
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
            UpdateGreetingContext();

            TableLayoutPanel statsRowPanel = new TableLayoutPanel
            {
                Location = new Point(0, 4),
                Size = new Size(560, 98),
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.Transparent,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            statsRowPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 274F));
            statsRowPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 274F));

            statsRowPanel.Controls.Add(CreateCompactStatCard("Active Tasks", AccentColor, out lblStatActiveCount), 0, 0);
            statsRowPanel.Controls.Add(CreateCompactStatCard("Done Today", PurpleColor, out lblStatDoneCount), 1, 0);
            contentPanel.Controls.Add(statsRowPanel);

            lblTaskCount = CreateLabel("0 tasks outstanding", new Point(0, 132), new Size(400, 26), 13F, FontStyle.Bold, TextColor);

            taskListPanel = new FlowLayoutPanel
            {
                Location = new Point(0, 172),
                Size = new Size(970, 430),
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                Margin = new Padding(0),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };

            contentPanel.Controls.AddRange(new Control[] { lblTaskCount, taskListPanel });
        }

        private void RenderCalendarView()
        {
            UpdateGreetingContext();

            Guna2Panel monthHeader = UIControlFactory.CreatePanel(
                CardBgColor,
                border: true,
                radius: DesignTokens.Radius.Small,
                preset: currentTheme);
            monthHeader.Location = new Point(0, 4);
            monthHeader.Size = new Size(970, 54);
            monthHeader.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            lblCalendarMonth = new Label
            {
                Text = displayedCalendarMonth.ToString("MMMM yyyy"),
                Location = new Point(24, 12),
                Size = new Size(320, 30),
                Font = VibeAlarmPalette.Display(16F, FontStyle.Bold),
                ForeColor = TextColor,
                BackColor = Color.Transparent
            };

            Guna2Button btnPrev = UIControlFactory.CreateSecondaryButton("<", preset: currentTheme);
            btnPrev.Location = new Point(768, 10);
            btnPrev.Size = new Size(42, 34);
            btnPrev.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnPrev.AccessibleName = "Previous month";
            btnPrev.Click += (s, e) =>
            {
                displayedCalendarMonth = displayedCalendarMonth.AddMonths(-1);
                RenderActiveView();
            };

            Guna2Button btnToday = UIControlFactory.CreateSecondaryButton("Today", preset: currentTheme);
            btnToday.Location = new Point(822, 10);
            btnToday.Size = new Size(78, 34);
            btnToday.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnToday.AccessibleName = "Jump to today";
            btnToday.Click += (s, e) =>
            {
                activeCalendarDate = DateTime.Today;
                activeCalendarDay = activeCalendarDate.DayOfWeek.ToString();
                displayedCalendarMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
                RenderActiveView();
            };

            Guna2Button btnNext = UIControlFactory.CreateSecondaryButton(">", preset: currentTheme);
            btnNext.Location = new Point(912, 10);
            btnNext.Size = new Size(42, 34);
            btnNext.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnNext.AccessibleName = "Next month";
            btnNext.Click += (s, e) =>
            {
                displayedCalendarMonth = displayedCalendarMonth.AddMonths(1);
                RenderActiveView();
            };
            monthHeader.Controls.AddRange(new Control[] { lblCalendarMonth, btnPrev, btnToday, btnNext });

            // NEXT UP block — prominent nearest upcoming schedule with live countdown.
            Guna2Panel nextUpCard = UIControlFactory.CreatePanel(
                CardBgColor,
                border: true,
                radius: DesignTokens.Radius.Medium,
                preset: currentTheme);
            nextUpCard.Location = new Point(0, 66);
            nextUpCard.Size = new Size(970, 116);
            nextUpCard.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            nextUpCard.Padding = new Padding(24, 0, 24, 0);
            Label lblNextUpTitle = CreateLabel("NEXT UP", new Point(24, 16), new Size(160, 18), 9.5F, FontStyle.Bold, MutedTextColor);
            lblNextUpTitle.Font = VibeAlarmPalette.Body(9.5F, FontStyle.Bold);
            lblNextUpTime = CreateLabel("--:-- --", new Point(24, 34), new Size(420, 38), 26F, FontStyle.Bold, TextColor);
            lblNextUpTime.Font = VibeAlarmPalette.Display(24F, FontStyle.Bold);
            lblNextUpName = CreateLabel("No upcoming schedules.", new Point(24, 78), new Size(460, 22), 12F, FontStyle.Regular, MutedTextColor);
            lblNextUpCountdown = CreateLabel("—", new Point(700, 42), new Size(246, 36), 17F, FontStyle.Bold, TextColor);
            lblNextUpCountdown.Font = VibeAlarmPalette.Mono(17F, FontStyle.Bold);
            lblNextUpCountdown.TextAlign = ContentAlignment.MiddleRight;
            lblNextUpCountdown.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            nextUpCard.Controls.AddRange(new Control[] { lblNextUpTitle, lblNextUpTime, lblNextUpName, lblNextUpCountdown });

            calendarStripMatrix = new TableLayoutPanel
            {
                Location = new Point(0, 190),
                Size = new Size(970, 262),
                ColumnCount = 7,
                RowCount = 7,
                BackColor = Color.Transparent,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            for (int i = 0; i < 7; i++)
                calendarStripMatrix.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 14.28F));
            calendarStripMatrix.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
            for (int i = 0; i < 6; i++)
                calendarStripMatrix.RowStyles.Add(new RowStyle(SizeType.Percent, 15.67F));

            RebuildCalendarMonthGrid();

            Guna2Panel actionRow = UIControlFactory.CreatePanel(
                CardBgColor,
                border: true,
                radius: DesignTokens.Radius.Medium,
                preset: currentTheme);
            actionRow.Location = new Point(0, 460);
            actionRow.Size = new Size(970, 44);
            actionRow.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            lblCalendarProgress = CreateLabel("0 tasks completed today", new Point(24, 13), new Size(460, 22), 10.5F, FontStyle.Bold, TextColor);
            actionRow.Controls.Add(lblCalendarProgress);

            // LIVE clock chip — updates every second (via TimeService), independent of the calendar grid.
            lblLiveClock = CreateLabel(DateTime.Now.ToString("hh:mm:ss tt"), new Point(560, 10), new Size(386, 24), 11F, FontStyle.Bold, TextColor);
            lblLiveClock.Font = VibeAlarmPalette.Mono(11F, FontStyle.Bold);
            lblLiveClock.TextAlign = ContentAlignment.MiddleCenter;
            lblLiveClock.Text = $"● LIVE  {DateTime.Now:hh:mm:ss tt}";
            lblLiveClock.Padding = new Padding(0, 0, 24, 0);
            lblLiveClock.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            actionRow.Controls.Add(lblLiveClock);

            calendarListPanel = new FlowLayoutPanel
            {
                Location = new Point(0, 512),
                Size = new Size(970, 70),
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                Margin = new Padding(0),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };

            contentPanel.Controls.AddRange(new Control[] { monthHeader, nextUpCard, calendarStripMatrix, actionRow, calendarListPanel });

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
            UpdateGreetingContext();

            Guna2Panel metricsPanel = UIControlFactory.CreatePanel(
                CardBgColor,
                border: true,
                radius: DesignTokens.Radius.Medium,
                preset: currentTheme);
            metricsPanel.Location = new Point(0, 4);
            metricsPanel.Size = new Size(940, 96);
            metricsPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Label lblFocusHeading = CreateLabel("Today at a Glance", new Point(24, 18), new Size(400, 22), 13F, FontStyle.Bold, TextColor);
            lblFocusHeading.Font = VibeAlarmPalette.Display(15F, FontStyle.Bold);
            Label lblFocusPara = CreateLabel("Your reminders are running in the background. When a task reaches its scheduled date and time, VibeAlarm will let you know.", new Point(24, 46), new Size(890, 40), 10F, FontStyle.Regular, MutedTextColor);
            metricsPanel.Controls.AddRange(new Control[] { lblFocusHeading, lblFocusPara });

            Label lblUpcomingTitle = CreateLabel("TODAY'S TASKS", new Point(2, 124), new Size(400, 18), 9.5F, FontStyle.Bold, MutedTextColor);
            lblUpcomingTitle.Font = VibeAlarmPalette.Body(9.5F, FontStyle.Bold);

            dashboardFocusPanel = new FlowLayoutPanel
            {
                Location = new Point(0, 154),
                Size = new Size(940, 450),
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                Margin = new Padding(0),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };

            contentPanel.Controls.AddRange(new Control[] { metricsPanel, lblUpcomingTitle, dashboardFocusPanel });
        }

        /// <summary>Settings action row: icon + title on a bordered card, left-aligned, chrome so it
        /// reads as a clickable row rather than a bare hyperlink (§15.2). Icon and title live on the
        /// button itself — never as child controls.</summary>
        private Guna2Button CreateSettingsRowButton(string glyph, string title, Color ink, ThemePreset p)
        {
            Guna2Button btn = new Guna2Button
            {
                Text = title,
                Font = VibeAlarmPalette.Body(10F),
                FillColor = p.CardBgColor,
                ForeColor = ink,
                BorderThickness = 1,
                BorderColor = p.BorderColor,
                BorderRadius = DesignTokens.Radius.Medium,
                Cursor = Cursors.Hand,
                Animated = false,
                TextAlign = HorizontalAlignment.Left,
                TextOffset = new Point(52, 0), // clear of the 22px glyph slot + gap
                ImageAlign = HorizontalAlignment.Left,
                ImageOffset = new Point(16, 0),
                ImageSize = new Size(22, 22)
            };
            btn.HoverState.FillColor = p.CardHoverBg;
            btn.HoverState.ForeColor = ink;
            btn.HoverState.BorderColor = ink;
            btn.Image = RenderNavGlyph(glyph, ink);
            return btn;
        }

        private void RenderSettingsView()
        {
            UpdateGreetingContext();

            Label lblSection = CreateLabel("TASK DATA", new Point(2, 12), new Size(400, 20), 9.5F, FontStyle.Bold, MutedTextColor);
            lblSection.Font = VibeAlarmPalette.Body(9.5F, FontStyle.Bold);

            Guna2Button btnClearData = CreateSettingsRowButton("E74D", "Delete all tasks", currentTheme.ErrorColor, currentTheme);
            btnClearData.Location = new Point(0, 44);
            btnClearData.Size = new Size(940, 40);
            btnClearData.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            btnClearData.BorderColor = currentTheme.ErrorColor;
            btnClearData.Click += (s, e) =>
            {
                if (MessageBox.Show("Delete all active task records permanently?", "Confirm Reset", MessageBoxButtons.YesNo, MessageBoxIcon.Stop) == DialogResult.Yes)
                {
                    masterTaskList.Clear();
                    RefreshDataCounters();
                    TaskStorageService.Save(masterTaskList);
                }
            };

            Guna2Button btnResetCompletion = CreateSettingsRowButton("E777", "Reset Task Completion Status", AccentColor, currentTheme);
            btnResetCompletion.Location = new Point(0, 100);
            btnResetCompletion.Size = new Size(940, 40);
            btnResetCompletion.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            btnResetCompletion.Click += (s, e) =>
            {
                foreach (var t in masterTaskList)
                {
                    t.Completed = false;
                }
                RefreshDataCounters();
                TaskStorageService.Save(masterTaskList);
                MessageBox.Show("All task completion statuses have been reset!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };

            Guna2Button btnOpenDataFolder = CreateSettingsRowButton("E8B7", "Open data folder", MutedTextColor, currentTheme);
            btnOpenDataFolder.Location = new Point(0, 156);
            btnOpenDataFolder.Size = new Size(940, 40);
            btnOpenDataFolder.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            btnOpenDataFolder.Click += (s, e) =>
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"\"{AppContext.BaseDirectory}\"",
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not open the data folder:\n{ex.Message}", "Data Folder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };

            Label lblThemeSection = CreateLabel("THEME", new Point(2, 224), new Size(400, 20), 9.5F, FontStyle.Bold, MutedTextColor);
            lblThemeSection.Font = VibeAlarmPalette.Body(9.5F, FontStyle.Bold);

            // 2-state picker: a toggle is the honest control for a binary Light/Dark choice (not a 2-item dropdown).
            bool darkEnabled = !currentTheme.IsLight;
            Label lblThemeDark = CreateLabel("DARK", new Point(0, 258), new Size(60, 20), 9F, FontStyle.Bold, MutedTextColor);
            lblThemeDark.Font = VibeAlarmPalette.Body(9F, FontStyle.Bold);
            lblThemeDark.TextAlign = ContentAlignment.MiddleLeft;
            Guna2ToggleSwitch themeToggle = UIControlFactory.CreateToggle(darkEnabled, preset: currentTheme);
            themeToggle.Location = new Point(66, 256);
            themeToggle.Size = new Size(56, 30);
            themeToggle.CheckedChanged += (s, e) =>
            {
                var target = themeToggle.Checked ? themeService.FindByName("Dark") : themeService.FindByName("Light");
                if (target != null)
                {
                    ApplyTheme(target);
                }
            };
            Label lblThemeLight = CreateLabel("LIGHT", new Point(128, 258), new Size(60, 20), 9F, FontStyle.Bold, MutedTextColor);
            lblThemeLight.Font = VibeAlarmPalette.Body(9F, FontStyle.Bold);
            lblThemeLight.TextAlign = ContentAlignment.MiddleLeft;

            Label lblBehaviorSection = CreateLabel("STARTUP", new Point(2, 314), new Size(400, 20), 9.5F, FontStyle.Bold, MutedTextColor);
            lblBehaviorSection.Font = VibeAlarmPalette.Body(9.5F, FontStyle.Bold);

            bool startupEnabled = SettingsService.IsRunOnStartupEnabled();
            Guna2Button btnStartup = CreateSettingsRowButton("E7E8", startupEnabled ? "Disable Launch on Windows Startup" : "Enable Launch on Windows Startup", startupEnabled ? AccentColor : TextColor, currentTheme);
            btnStartup.Location = new Point(0, 346);
            btnStartup.Size = new Size(940, 40);
            btnStartup.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            btnStartup.Click += (s, e) =>
            {
                bool nowEnabled = SettingsService.IsRunOnStartupEnabled();
                SettingsService.SetRunOnStartup(!nowEnabled);
                btnStartup.Text = !nowEnabled ? "Disable Launch on Windows Startup" : "Enable Launch on Windows Startup";
                btnStartup.ForeColor = !nowEnabled ? AccentColor : TextColor;
                btnStartup.HoverState.ForeColor = !nowEnabled ? AccentColor : TextColor;
                MessageBox.Show(!nowEnabled ? "Launch on startup enabled!" : "Launch on startup disabled!", "Startup Settings", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };

            // ---- System tray / hide-not-close ----
            Label lblTraySection = CreateLabel("SYSTEM", new Point(2, 412), new Size(400, 20), 9.5F, FontStyle.Bold, MutedTextColor);
            lblTraySection.Font = VibeAlarmPalette.Body(9.5F, FontStyle.Bold);

            bool trayEnabled = SettingsService.Load().MinimizeToTray;
            Guna2Button btnTray = CreateSettingsRowButton("E7F4", trayEnabled ? "Disable Minimize to Tray" : "Enable Minimize to Tray", trayEnabled ? AccentColor : TextColor, currentTheme);
            btnTray.Location = new Point(0, 444);
            btnTray.Size = new Size(940, 40);
            btnTray.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            btnTray.Click += (_, _) =>
            {
                AppSettings settings = SettingsService.Load();
                settings.MinimizeToTray = !settings.MinimizeToTray;
                SettingsService.Save(settings);
                btnTray.Text = settings.MinimizeToTray ? "Disable Minimize to Tray" : "Enable Minimize to Tray";
                btnTray.ForeColor = settings.MinimizeToTray ? AccentColor : TextColor;
                btnTray.HoverState.ForeColor = settings.MinimizeToTray ? AccentColor : TextColor;
                MessageBox.Show(settings.MinimizeToTray
                    ? "Closing the window will hide VibeAlarm to the tray. Alarms keep firing while hidden."
                    : "Minimize to tray disabled. Closing the window will exit the app.",
                    "Tray Settings", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };

            // ---- Custom background ----
            Label lblBgSection = CreateLabel("CUSTOM BACKGROUND", new Point(2, 510), new Size(400, 20), 9.5F, FontStyle.Bold, MutedTextColor);
            lblBgSection.Font = VibeAlarmPalette.Body(9.5F, FontStyle.Bold);

            Label lblBgStatus = CreateLabel(string.IsNullOrEmpty(SettingsService.Load().BackgroundImagePath) ? "No custom background set." : "Custom background set.", new Point(0, 646), new Size(600, 20), 9F, FontStyle.Regular, MutedTextColor);
            lblBgStatus.Font = VibeAlarmPalette.Body(9F);

            Guna2Button btnChooseBg = UIControlFactory.CreatePrimaryButton("Choose Image...", preset: currentTheme);
            btnChooseBg.Location = new Point(0, 542);
            btnChooseBg.Size = new Size(170, 36);
            btnChooseBg.Click += (_, _) =>
            {
                using OpenFileDialog picker = new OpenFileDialog
                {
                    Title = "Choose a background image",
                    Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif",
                    CheckFileExists = true,
                    Multiselect = false
                };
                if (picker.ShowDialog(this) == DialogResult.OK)
                {
                    string? stored = BackgroundService.SetBackground(picker.FileName);
                    if (stored == null)
                    {
                        MessageBox.Show("The selected image could not be used.", "Background", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    AppSettings settings = SettingsService.Load();
                    settings.BackgroundImagePath = stored;
                    SettingsService.Save(settings);
                    ApplyStoredBackground();
                    lblBgStatus.Text = "Background applied.";
                }
            };

            Guna2Button btnRemoveBg = UIControlFactory.CreateSecondaryButton("Remove Background", preset: currentTheme);
            btnRemoveBg.Location = new Point(186, 542);
            btnRemoveBg.Size = new Size(170, 36);
            btnRemoveBg.BorderThickness = 1;
            btnRemoveBg.BorderColor = BorderColor;
            btnRemoveBg.UseTransparentBackground = false;
            btnRemoveBg.Click += (_, _) =>
            {
                BackgroundService.RemoveBackground();
                AppSettings settings = SettingsService.Load();
                settings.BackgroundImagePath = string.Empty;
                SettingsService.Save(settings);
                this.BackgroundImage = null;
                lblBgStatus.Text = "Background removed.";
            };

            bool monoFilter = SettingsService.Load().ApplyMonochromeFilterToBackground;
            Label lblMonoState = CreateLabel(monoFilter ? "Monochrome Filter: On" : "Monochrome Filter: Off", new Point(88, 596), new Size(200, 30), 9F, FontStyle.Regular, MutedTextColor);
            lblMonoState.TextAlign = ContentAlignment.MiddleLeft;
            Guna2ToggleSwitch monoToggle = UIControlFactory.CreateToggle(monoFilter, preset: currentTheme);
            monoToggle.Location = new Point(14, 596);
            monoToggle.Size = new Size(56, 30);
            monoToggle.CheckedChanged += (_, _) =>
            {
                AppSettings settings = SettingsService.Load();
                settings.ApplyMonochromeFilterToBackground = monoToggle.Checked;
                SettingsService.Save(settings);
                lblMonoState.Text = monoToggle.Checked ? "Monochrome Filter: On" : "Monochrome Filter: Off";
            };
            lblBgStatus.Font = VibeAlarmPalette.Body(9F);

            Label lblSpecs = CreateLabel("VibeAlarm v1.5.0", new Point(2, 754), new Size(500, 22), 9F, FontStyle.Regular, MutedTextColor);
            lblSpecs.Font = VibeAlarmPalette.Body(9F);
            lblSpecs.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;

            contentPanel.Controls.AddRange(new Control[] { lblSection, btnClearData, btnResetCompletion, btnOpenDataFolder, lblThemeSection, lblThemeDark, themeToggle, lblThemeLight, lblBehaviorSection, btnStartup, lblTraySection, btnTray, lblBgSection, btnChooseBg, btnRemoveBg, monoToggle, lblMonoState, lblBgStatus, lblSpecs });
        }

        private void RenderAmbientView()
        {
            UpdateGreetingContext();

            Panel heroPanel = CreateCard(new Point(0, 4), new Size(970, 106), DesignTokens.Radius.Medium, CardBgColor, BorderColor);
            heroPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Label title = CreateLabel("Ambient Soundscapes", new Point(24, 20), new Size(420, 28), 15F, FontStyle.Bold, TextColor);
            Label subtitle = CreateLabel("Play your own local focus audio offline while you study or work.", new Point(24, 52), new Size(700, 22), 10.5F, FontStyle.Regular, MutedTextColor);
            lblAmbientNowPlaying = CreateLabel(audioService.IsAmbientPlaying ? "Ambient audio playing" : "Nothing playing", new Point(24, 76), new Size(500, 20), 9.5F, FontStyle.Bold, TextColor);
            lblAmbientNowPlaying.Font = VibeAlarmPalette.Body(9.5F, FontStyle.Bold);

            Button stopButton = CreateGhostButton("Stop Sound", new Point(820, 34), new Size(126, 36));
            stopButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            stopButton.Click += (s, e) => StopAmbientAudio();
            heroPanel.Controls.AddRange(new Control[] { title, subtitle, lblAmbientNowPlaying, stopButton });

            Button importButton = CreatePrimaryButton("+  Add Local Sound", new Point(0, 132), new Size(170, 36));
            importButton.Click += (s, e) => ImportAndPlayAmbientFile();

            Panel volumePanel = CreateCard(new Point(0, 202), new Size(970, 92), DesignTokens.Radius.Medium, CardBgColor, BorderColor);
            volumePanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Label volumeTitle = CreateLabel("Sound Volume", new Point(24, 18), new Size(220, 24), 12F, FontStyle.Bold, TextColor);
            Label volumeHint = CreateLabel("Adjust the ambient sound level without changing your task alarms.", new Point(24, 46), new Size(440, 20), 9.5F, FontStyle.Regular, MutedTextColor);
            ambientVolumeSlider = new TrackBar
            {
                Location = new Point(500, 24),
                Size = new Size(330, 44),
                Minimum = 0,
                Maximum = 100,
                TickFrequency = 10,
                Value = ambientVolume,
                BackColor = CardBgColor,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            Label volumeValue = CreateLabel($"{ambientVolume}%", new Point(850, 31), new Size(70, 24), 11F, FontStyle.Bold, TextColor);
            volumeValue.Font = VibeAlarmPalette.Mono(11F, FontStyle.Bold);
            volumeValue.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            ambientVolumeSlider.Scroll += (s, e) =>
            {
                ambientVolume = ambientVolumeSlider.Value;
                volumeValue.Text = $"{ambientVolume}%";
                audioService.AmbientVolume = ambientVolume;
                audioService.ApplyAmbientVolume();
            };
            volumePanel.Controls.AddRange(new Control[] { volumeTitle, volumeHint, ambientVolumeSlider, volumeValue });

            Panel builtinPanel = CreateCard(new Point(0, 318), new Size(970, 150), DesignTokens.Radius.Medium, CardBgColor, BorderColor);
            builtinPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Label builtinTitle = CreateLabel("Offline Focus Sounds", new Point(24, 20), new Size(320, 24), 12F, FontStyle.Bold, TextColor);
            Label builtinText = CreateLabel("Synthesized offline loops to drown out background noise and boost productivity.", new Point(24, 48), new Size(760, 20), 9.5F, FontStyle.Regular, MutedTextColor);

            Button btnPlayBrown = CreateGhostButton("Play Brownian Focus", new Point(24, 86), new Size(180, 38));
            btnPlayBrown.Click += (s, e) =>
            {
                string path = Path.Combine(AppContext.BaseDirectory, "Assets", "brown_noise.wav");
                PlayAmbientFile(path, "Brownian Focus");
            };

            Button btnPlayRain = CreateGhostButton("Play Rain Soundscape", new Point(220, 86), new Size(180, 38));
            btnPlayRain.Click += (s, e) =>
            {
                string path = Path.Combine(AppContext.BaseDirectory, "Assets", "rain.wav");
                PlayAmbientFile(path, "Rain Soundscape");
            };

            builtinPanel.Controls.AddRange(new Control[] { builtinTitle, builtinText, btnPlayBrown, btnPlayRain });

            contentPanel.Controls.AddRange(new Control[] { heroPanel, importButton, volumePanel, builtinPanel });
        }

        private Panel CreateCompactStatCard(string header, Color iconColor, out Label lblValue)
        {
            Panel card = CreateCard(new Point(0, 0), new Size(264, 86), DesignTokens.Radius.Medium, CardBgColor, BorderColor);
            card.Margin = new Padding(0, 0, 12, 0);

            Panel iconBox = new Panel { Location = new Point(20, 20), Size = new Size(46, 46), BackColor = Color.Transparent };
            // Reliable Unicode glyphs (drawn in the provided theme color) instead of ASCII
            // "[]"/"OK", which render as empty tofu on some systems. Active = bulleted task,
            // Done = checkmark — both are widely supported glyphs in the default UI font.
            Label icon = CreateLabel(header.StartsWith("Active") ? "\u25CF" : "\u2713", new Point(0, 10), new Size(46, 26), 15F, FontStyle.Bold, iconColor);
            icon.TextAlign = ContentAlignment.MiddleCenter;
            icon.Font = new Font("Segoe UI Symbol", 15F, FontStyle.Regular);
            iconBox.Controls.Add(icon);
            iconBox.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using Pen hairline = new Pen(BorderColor, VibeAlarmPalette.Hairline);
                e.Graphics.DrawRectangle(hairline, new Rectangle(0, 0, iconBox.Width - 1, iconBox.Height - 1));
            };
            RoundControl(iconBox, DesignTokens.Radius.Small);

            Label lblTitle = CreateLabel(header, new Point(84, 22), new Size(150, 22), 9F, FontStyle.Bold, MutedTextColor);
            lblTitle.Font = VibeAlarmPalette.Body(9F, FontStyle.Bold);
            lblValue = CreateLabel("0", new Point(84, 46), new Size(120, 32), 20F, FontStyle.Bold, TextColor);

            card.Controls.AddRange(new Control[] { iconBox, lblTitle, lblValue });
            return card;
        }

        private void RefreshDataCounters()
        {
            int pending = masterTaskList.Count(t => IsActiveTask(t));
            int pendingToday = masterTaskList.Count(t => IsActiveTask(t) && GetTaskDate(t).Date == DateTime.Today);
            int resolvedToday = masterTaskList.Count(t => t.Completed && GetTaskDate(t).Date == DateTime.Today);
            int total = masterTaskList.Count;
            int completed = masterTaskList.Count(t => t.Completed);
            int completePercent = total == 0 ? 0 : (int)Math.Round(completed * 100d / total);

            if (lblStatActiveCount != null) lblStatActiveCount.Text = pending.ToString();
            if (lblStatDoneCount != null) lblStatDoneCount.Text = resolvedToday.ToString();
            if (lblHeaderSubtitle != null) lblHeaderSubtitle.Text = $"You have {pendingToday} tasks remaining today.";
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

        private static string GetTaskDateLabel(TaskItem task)
        {
            return GetTaskDate(task).ToString("ddd, MMM d, yyyy");
        }

        private void BindTaskListView()
        {
            taskListPanel.Controls.Clear();
            var filtered = GetFilteredTasks(masterTaskList).ToList();
            lblTaskCount.Text = $"{filtered.Count(t => IsActiveTask(t))} tasks outstanding";

            if (!filtered.Any())
            {
                taskListPanel.Controls.Add(CreateEmptyStateRow("No tasks found.", "Try a different search or add a new task."));
                return;
            }

            foreach (TaskItem task in filtered.OrderBy(GetTaskDate).ThenBy(t => t.RemindTime))
            {
                taskListPanel.Controls.Add(BuildTaskRowCard(task));
            }
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

        private Control BuildTaskRowCard(TaskItem item)
        {
            int rowWidth = GetListRowWidth();
            Panel card = CreateCard(new Point(0, 0), new Size(rowWidth, 76), DesignTokens.Radius.Medium, CardBgColor, BorderColor);
            card.Margin = new Padding(0, 0, 0, 10);

            card.MouseEnter += (s, e) => { card.BackColor = CardHoverBg; card.Invalidate(); };
            card.MouseLeave += (s, e) => { card.BackColor = CardBgColor; card.Invalidate(); };

            Guna2Button btnToggle = UIControlFactory.CreateSecondaryButton(item.Completed ? "OK" : string.Empty, preset: currentTheme);
            btnToggle.Location = new Point(16, 18);
            btnToggle.Size = new Size(36, 36);
            btnToggle.Font = VibeAlarmPalette.Mono(12F, FontStyle.Bold);
            btnToggle.BorderRadius = DesignTokens.Radius.Small;
            btnToggle.FillColor = item.Completed ? TextColor : Color.Transparent;
            btnToggle.ForeColor = currentTheme.IsLight ? VibeAlarmPalette.Surface : Color.Black;
            btnToggle.BorderThickness = 1;
            btnToggle.BorderColor = BorderColor;
            btnToggle.HoverState.BorderColor = TextColor;

            btnToggle.Click += (s, e) =>
            {
                item.Completed = !item.Completed;
                RefreshDataCounters();
            };

            int badgeLeft = Math.Max(520, rowWidth - 200);
            int optionsLeft = Math.Max(570, rowWidth - 50);
            int textWidth = Math.Max(260, badgeLeft - 110);

            Label lblTitle = CreateLabel(item.Title, new Point(72, 14), new Size(textWidth, 24), 12F, FontStyle.Bold, item.Completed ? MutedTextColor : TextColor);
            Label lblMeta = CreateLabel($"Date: {GetTaskDateLabel(item)}    Time: {item.RemindTime}", new Point(72, 42), new Size(textWidth, 20), 9F, FontStyle.Regular, MutedTextColor);
            lblMeta.Font = VibeAlarmPalette.Mono(8.5F);

            lblTitle.MouseEnter += (s, e) => { card.BackColor = CardHoverBg; card.Invalidate(); };
            lblMeta.MouseEnter += (s, e) => { card.BackColor = CardHoverBg; card.Invalidate(); };

            // Type badge: outline treatment (no fill, hairline border, monochrome text) so it never
            // reads as a solid block competing with the primary action. Toast-style, not a pill fill.
            Panel badgePanel = new Panel { Size = new Size(132, 26), Location = new Point(badgeLeft, 25), BackColor = Color.Transparent };
            Label lblBadgeText = new Label
            {
                Text = item.Type.ToUpperInvariant(),
                Location = new Point(0, 4),
                Size = new Size(132, 18),
                Font = VibeAlarmPalette.Body(8F, FontStyle.Bold),
                ForeColor = TextColor,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleCenter
            };
            badgePanel.Controls.Add(lblBadgeText);
            badgePanel.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using Pen hairline = new Pen(BorderColor, VibeAlarmPalette.Hairline);
                Rectangle r = new Rectangle(0, 0, badgePanel.Width - 1, badgePanel.Height - 1);
                e.Graphics.DrawRectangle(hairline, r);
            };
            RoundControl(badgePanel, DesignTokens.Radius.Small);

            Guna2Button btnOptions = UIControlFactory.CreateIconButton("...", currentTheme);
            btnOptions.Location = new Point(optionsLeft, 20);
            btnOptions.Size = new Size(34, 34);
            btnOptions.Font = VibeAlarmPalette.Mono(14F, FontStyle.Bold);

            btnOptions.Click += (s, e) =>
            {
                ContextMenuStrip contextMenu = new ContextMenuStrip();
                contextMenu.Items.Add("Complete Task", null, (src, ev) => { item.Completed = true; RefreshDataCounters(); });
                contextMenu.Items.Add("Duplicate Entry", null, (src, ev) => {
                    masterTaskList.Add(new TaskItem { Id = Guid.NewGuid().ToString(), Title = item.Title + " (Copy)", ScheduledDate = item.ScheduledDate, Day = item.Day, RemindTime = item.RemindTime, Type = item.Type, Completed = false });
                    RefreshDataCounters();
                });
                contextMenu.Items.Add("Delete Permanently", null, (src, ev) => { masterTaskList.Remove(item); RefreshDataCounters(); });
                contextMenu.Show(btnOptions, new Point(0, btnOptions.Height));
            };

            card.Controls.AddRange(new Control[] { btnToggle, lblTitle, lblMeta, badgePanel, btnOptions });
            return card;
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

        private Button CreatePrimaryButton(string text, Point loc, Size size)
        {
            Button btn = new Button
            {
                Text = text,
                Location = loc,
                Size = size,
                FlatStyle = FlatStyle.Flat,
                Font = VibeAlarmPalette.Body(10F, FontStyle.Bold),
                BackColor = AccentColor,
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 0;
            // Accent hover/press: light theme lightens, dark theme darkens toward the page.
            btn.FlatAppearance.MouseDownBackColor = Shade(AccentColor, 0.82F);
            btn.FlatAppearance.MouseOverBackColor = HoverAccent();
            RoundControl(btn, DesignTokens.Radius.Small);
            return btn;
        }

        private Button CreateGhostButton(string text, Point loc, Size size)
        {
            Button btn = new Button
            {
                Text = text,
                Location = loc,
                Size = size,
                FlatStyle = FlatStyle.Flat,
                Font = VibeAlarmPalette.Body(10F),
                BackColor = Color.Transparent,
                ForeColor = TextColor,
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 0;
            // §14.5 ghost: no border at rest, quiet fill on hover (Notion secondary button).
            btn.FlatAppearance.MouseOverBackColor = CardHoverBg;
            btn.FlatAppearance.MouseDownBackColor = Shade(CardHoverBg, 0.9F);
            RoundControl(btn, DesignTokens.Radius.Small);
            return btn;
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

        private Panel CreateCard(Point loc, Size size, int radius, Color bg, Color border)
        {
            Panel cardPanel = new Panel { Location = loc, Size = size, BackColor = bg };
            int r = Math.Min(radius, VibeAlarmPalette.RadiusMax);
            cardPanel.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle rect = new Rectangle(0, 0, cardPanel.Width - 1, cardPanel.Height - 1);

                using (GraphicsPath path = GetRoundRectPath(rect, r))
                {
                    using (Pen pen = new Pen(border, VibeAlarmPalette.Hairline))
                        e.Graphics.DrawPath(pen, path);
                }
            };
            return cardPanel;
        }

        private void UpdateGreetingContext()
        {
            int hour = DateTime.Now.Hour;
            string structuralPrefix = hour < 12 ? "Good Morning" : hour < 18 ? "Good Afternoon" : "Good Evening";

            lblGreeting.Text = $"{structuralPrefix}, Jeptah";
            lblHeaderSubtitle.Text = activeView switch
            {
                "Dashboard" => "Here is what is on your plate today.",
                "Calendar" => "Pick a date to see what is scheduled.",
                "Settings" => "Manage your tasks, theme, and startup options.",
                _ => $"You have {masterTaskList.Count(t => IsActiveTask(t) && AlarmEngine.GetTaskDate(t).Date == DateTime.Today)} tasks remaining today."
            };
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
            try
            {
                audioService.PlayAlarmLoop(Path.Combine(AppContext.BaseDirectory, "Assets"));
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
            finally
            {
                audioService.StopAlarmLoop();
            }
        }

        private void OnReminderFired(TaskItem task)
        {
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

            // Update persistent container backgrounds
            BackColor = currentTheme.PrimaryBg;
            if (sidebarPanel != null) sidebarPanel.BackColor = currentTheme.SidebarBg;
            if (mainContainer != null) mainContainer.BackColor = currentTheme.PrimaryBg;

            // Update persistent header controls
            if (lblGreeting != null) lblGreeting.ForeColor = currentTheme.TextColor;
            if (lblHeaderSubtitle != null) lblHeaderSubtitle.ForeColor = currentTheme.MutedTextColor;

            if (txtSearch != null)
            {
                txtSearch.BackColor = currentTheme.CardBgColor;
                txtSearch.ForeColor = currentTheme.MutedTextColor;
            }

            if (btnNewTask != null)
            {
                btnNewTask.BackColor = currentTheme.AccentColor;
                btnNewTask.ForeColor = Color.White;
                btnNewTask.FlatAppearance.BorderColor = currentTheme.AccentColor;
                btnNewTask.FlatAppearance.MouseOverBackColor = HoverAccent();
                btnNewTask.FlatAppearance.MouseDownBackColor = Shade(currentTheme.AccentColor, 0.82F);
            }

            // Update persistent sidebar controls
            if (lblAppLogo != null) lblAppLogo.ForeColor = currentTheme.TextColor;

            // Refresh progress card
            if (sidebarProgressCard != null)
            {
                sidebarProgressCard.BackColor = CardBgColor;
                sidebarProgressCard.Invalidate(); // Force redraw of borders
            }

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

            SettingsService.SaveThemeName(newTheme.Name);

            // Re-render the active view to paint its controls with the new theme colors
            RenderActiveView();
        }

        private ComboBox CreateCombo(Point p, Size s, string[] items, int idx)
        {
            ComboBox b = new ComboBox
            {
                Location = p,
                Size = s,
                DropDownStyle = ComboBoxStyle.DropDownList,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 24,
                MaxDropDownItems = 8,
                DropDownHeight = 196,
                IntegralHeight = false,
                FlatStyle = FlatStyle.Flat,
                BackColor = CardBgColor,
                ForeColor = TextColor,
                Font = VibeAlarmPalette.Body(9.5F)
            };
            b.Items.AddRange(items);
            if (items.Length > 0) b.SelectedIndex = idx;
            b.DrawItem += (sender, e) =>
            {
                if (sender is not ComboBox combo || e.Index < 0) return;
                bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
                using SolidBrush background = new SolidBrush(selected ? CardHoverBg : CardBgColor);
                using SolidBrush textBrush = new SolidBrush(TextColor);
                e.Graphics.FillRectangle(background, e.Bounds);
                e.Graphics.DrawString(combo.Items[e.Index]?.ToString() ?? string.Empty, combo.Font, textBrush, e.Bounds.X + 4, e.Bounds.Y + 3);
                e.DrawFocusRectangle();
            };
            return b;
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

            tray?.HideTray();
            tray?.Dispose();
            audioService.Dispose();
            base.OnFormClosing(e);
        }
    }
}
