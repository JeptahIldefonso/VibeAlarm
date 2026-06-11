using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace VibeAlarm
{
    public partial class Form1 : Form
    {
        private const string PlaceholderTaskName = "What needs to be done?";
        private const string PlaceholderSearch = "🔍 Search tasks...";
        private const string TaskTypeNotification = "Notification";
        private const string TaskTypeAlarm = "Alarm";
        private const string TaskTypeImportant = "Important";

        private static readonly string[] WeekDays =
        {
            "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"
        };

        private static readonly string[] ShortWeekDays =
        {
            "SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT"
        };

        private readonly List<TaskItem> masterTaskList = new();
        private readonly HashSet<string> triggeredAlertKeys = new(StringComparer.OrdinalIgnoreCase);
        private string activeCalendarDay = DateTime.Now.DayOfWeek.ToString();
        private string activeView = "Tasks";
        private string searchFilterQuery = string.Empty;

        // Spotify Premium Inspired Theme Palette
        private static readonly Color PrimaryBg = Color.FromArgb(18, 18, 18);
        private static readonly Color SecondaryBg = Color.FromArgb(19, 22, 26);
        private static readonly Color SidebarBg = Color.FromArgb(0, 0, 0);
        private static readonly Color CardBgColor = Color.FromArgb(22, 25, 29);
        private static readonly Color CardHoverBg = Color.FromArgb(27, 31, 36);
        private static readonly Color AccentColor = Color.FromArgb(30, 215, 96); // Authentic Spotify Green
        private static readonly Color MutedTextColor = Color.FromArgb(179, 179, 179);
        private static readonly Color BorderColor = Color.FromArgb(45, 49, 55);
        private static readonly Color WarningColor = Color.FromArgb(245, 181, 38);
        private static readonly Color AlarmColor = Color.FromArgb(255, 89, 89);
        private static readonly Color PurpleColor = Color.FromArgb(168, 105, 255);

        private System.Windows.Forms.Timer backgroundAlarmTicker = null!;
        private System.Media.SoundPlayer? activeAlarmPlayer;

        // Layout Shell Containers
        private Panel sidebarPanel = null!;
        private Panel mainContainer = null!;
        private Panel headerPanel = null!;
        private Panel contentPanel = null!;
        private Panel activeNavIndicator = null!;

        // Header Input Items
        private TextBox txtSearch = null!;
        private Button btnNewTask = null!;

        // Navigation Group Links
        private Button btnDashboard = null!;
        private Button btnTasks = null!;
        private Button btnCalendar = null!;
        private Button btnAmbient = null!;
        private Button btnSettings = null!;

        // Interactive Labels
        private Label lblGreeting = null!;
        private Label lblHeaderSubtitle = null!;
        private Label lblStatActiveCount = null!;
        private Label lblStatDoneCount = null!;
        private Label lblSidebarRemaining = null!;
        private Label lblSidebarPercent = null!;
        private Panel sidebarProgressTrack = null!;
        private Panel sidebarProgressFill = null!;

        // Dynamic Panels
        private FlowLayoutPanel taskListPanel = null!;
        private FlowLayoutPanel dashboardFocusPanel = null!;
        private TableLayoutPanel calendarStripMatrix = null!;
        private FlowLayoutPanel calendarListPanel = null!;
        private Label lblCalendarProgress = null!;
        private Label lblTaskCount = null!;
        private Button btnClearData = null!;
        private Label lblAmbientNowPlaying = null!;
        private TrackBar ambientVolumeSlider = null!;
        private string? activeAmbientAlias;
        private int ambientVolume = 70;

        public Form1()
        {
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            this.UpdateStyles();

            InitializeComponent();
            BuildDesktopInterface();
            LoadSampleMockData();
            InitializeAlarmEngine();
            RenderActiveView();
        }

        private void BuildDesktopInterface()
        {
            Text = "TaskFlow Premium Desktop";
            ClientSize = new Size(1280, 760);
            MinimumSize = new Size(1180, 700);
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 10F);
            BackColor = PrimaryBg;

            // Base Layout Splits
            sidebarPanel = new Panel { Dock = DockStyle.Left, Width = 250, BackColor = SidebarBg, Padding = new Padding(0, 24, 0, 0) };
            mainContainer = new Panel { Dock = DockStyle.Fill, BackColor = PrimaryBg, Padding = new Padding(34, 28, 36, 28) };

            Controls.Add(mainContainer);
            Controls.Add(sidebarPanel);

            BuildSidebarNavigation();
            BuildMainWorkspaceLayout();
        }

        private void BuildSidebarNavigation()
        {
            Label lblAppLogo = CreateLabel("TaskFlow", new Point(32, 34), new Size(180, 34), 20F, FontStyle.Bold, Color.White);
            sidebarPanel.Controls.Add(lblAppLogo);

            activeNavIndicator = new Panel { Width = 5, Height = 52, BackColor = AccentColor, Location = new Point(0, 0) };
            sidebarPanel.Controls.Add(activeNavIndicator);

            btnDashboard = CreateSidebarButton("⌂   Home", "Dashboard", 114);
            btnTasks = CreateSidebarButton("✓   Tasks", "Tasks", 166);
            btnCalendar = CreateSidebarButton("□   Calendar", "Calendar", 218);
            btnAmbient = CreateSidebarButton("◉   Ambient", "Ambient", 270);
            btnSettings = CreateSidebarButton("⚙   Settings", "Settings", 322);

            sidebarPanel.Controls.AddRange(new Control[] { btnDashboard, btnTasks, btnCalendar, btnAmbient, btnSettings });
            BuildSidebarProgressCard();
        }

        private Button CreateSidebarButton(string text, string viewKey, int topPosition)
        {
            Button btn = new Button
            {
                Text = text,
                Tag = viewKey,
                Location = new Point(0, topPosition),
                Size = new Size(250, 52),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI Semibold", 11F),
                ForeColor = MutedTextColor,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(34, 0, 0, 0),
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseDownBackColor = Color.Transparent;
            btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(15, 15, 15);

            btn.Click += (s, e) =>
            {
                activeView = viewKey;
                RenderActiveView();
            };

            return btn;
        }

        private void BuildSidebarProgressCard()
        {
            Panel progressCard = CreateCard(new Point(18, 580), new Size(214, 144), 8, Color.FromArgb(8, 9, 11), BorderColor);
            progressCard.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;

            Label icon = CreateLabel("▣", new Point(22, 24), new Size(28, 28), 17F, FontStyle.Bold, AccentColor);
            lblSidebarRemaining = CreateLabel("0 Tasks Remaining", new Point(54, 27), new Size(140, 24), 10.5F, FontStyle.Bold, Color.White);
            Label encouragement = CreateLabel("Keep going, Jeptah!", new Point(22, 66), new Size(170, 20), 9F, FontStyle.Regular, MutedTextColor);

            sidebarProgressTrack = new Panel { Location = new Point(22, 106), Size = new Size(132, 10), BackColor = Color.FromArgb(25, 29, 33) };
            sidebarProgressFill = new Panel { Location = new Point(0, 0), Size = new Size(0, 10), BackColor = AccentColor };
            lblSidebarPercent = CreateLabel("0%", new Point(172, 100), new Size(34, 22), 10F, FontStyle.Bold, Color.White);

            sidebarProgressTrack.Controls.Add(sidebarProgressFill);
            progressCard.Controls.AddRange(new Control[] { icon, lblSidebarRemaining, encouragement, sidebarProgressTrack, lblSidebarPercent });
            RoundControl(sidebarProgressTrack, 5);
            RoundControl(sidebarProgressFill, 5);
            sidebarPanel.Controls.Add(progressCard);
        }

        private void BuildMainWorkspaceLayout()
        {
            headerPanel = new Panel { Dock = DockStyle.Top, Height = 112, BackColor = Color.Transparent };
            contentPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 10, 0, 0), BackColor = Color.Transparent };

            mainContainer.Controls.Add(contentPanel);
            mainContainer.Controls.Add(headerPanel);

            // Responsive Greeting Setup
            lblGreeting = CreateLabel("Good Evening, Jeptah", new Point(0, 18), new Size(520, 40), 23F, FontStyle.Bold, Color.White);
            lblHeaderSubtitle = CreateLabel("You have 0 tasks remaining today.", new Point(2, 62), new Size(520, 24), 12F, FontStyle.Regular, MutedTextColor);
            headerPanel.Controls.AddRange(new Control[] { lblGreeting, lblHeaderSubtitle });

            // Optimized Search Bar Placement
            txtSearch = new TextBox
            {
                Location = new Point(520, 28),
                Size = new Size(310, 42),
                Font = new Font("Segoe UI", 12F),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(16, 18, 22),
                ForeColor = MutedTextColor,
                Text = "  Search tasks...",
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };

            txtSearch.GotFocus += (s, e) => {
                if (txtSearch.Text == PlaceholderSearch || txtSearch.Text.Trim() == "Search tasks...")
                {
                    txtSearch.Text = string.Empty;
                    txtSearch.ForeColor = Color.White;
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
            btnNewTask = CreatePrimaryButton("+  New Task", new Point(854, 28), new Size(142, 42));
            btnNewTask.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnNewTask.Click += (s, e) => { ExecuteModalTaskCreationDialogue(); };
            headerPanel.Controls.Add(btnNewTask);
            headerPanel.Resize += (s, e) => ArrangeHeaderActions();
            ArrangeHeaderActions();
        }

        private void ArrangeHeaderActions()
        {
            if (txtSearch == null || btnNewTask == null || headerPanel == null)
            {
                return;
            }

            const int gap = 22;
            int buttonWidth = 142;
            int rightEdge = headerPanel.ClientSize.Width;
            int desiredSearchWidth = 310;
            int availableSearchWidth = rightEdge - buttonWidth - gap - 520;
            int searchWidth = Math.Max(220, Math.Min(desiredSearchWidth, availableSearchWidth));

            btnNewTask.Location = new Point(Math.Max(0, rightEdge - buttonWidth), 28);
            btnNewTask.Size = new Size(buttonWidth, 42);
            txtSearch.Location = new Point(Math.Max(0, btnNewTask.Left - gap - searchWidth), 28);
            txtSearch.Size = new Size(searchWidth, 42);
        }

        private void ExecuteModalTaskCreationDialogue()
        {
            using (TaskCreateDialog dialog = new TaskCreateDialog())
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    masterTaskList.Add(dialog.GeneratedTask);
                    activeCalendarDay = dialog.GeneratedTask.Day;
                    RefreshDataCounters();
                }
            }
        }

        private void RenderActiveView()
        {
            contentPanel.SuspendLayout();
            contentPanel.Controls.Clear();

            Button targetBtn = activeView switch
            {
                "Dashboard" => btnDashboard,
                "Calendar" => btnCalendar,
                "Ambient" => btnAmbient,
                "Settings" => btnSettings,
                _ => btnTasks
            };

            activeNavIndicator.Location = new Point(0, targetBtn.Top);

            foreach (Button btn in new[] { btnDashboard, btnTasks, btnCalendar, btnAmbient, btnSettings })
            {
                bool isCurrent = btn == targetBtn;
                btn.ForeColor = isCurrent ? AccentColor : MutedTextColor;
                btn.Font = new Font("Segoe UI", 10F, isCurrent ? FontStyle.Bold : FontStyle.Regular);
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
        }

        private void RenderTaskView()
        {
            UpdateGreetingContext();

            // Refactored to dual clean structural column row layout
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

            lblTaskCount = CreateLabel("0 tasks outstanding", new Point(0, 132), new Size(400, 26), 13F, FontStyle.Bold, Color.White);

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

            calendarStripMatrix = new TableLayoutPanel
            {
                Location = new Point(0, 4),
                Size = new Size(940, 76),
                ColumnCount = 7,
                RowCount = 1,
                BackColor = Color.Transparent,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            for (int i = 0; i < 7; i++)
                calendarStripMatrix.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 14.28F));

            RebuildCalendarWeeklyStrip();

            Panel actionRow = CreateCard(new Point(0, 96), new Size(940, 56), 6, SecondaryBg, BorderColor);
            actionRow.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            lblCalendarProgress = CreateLabel("0 objectives resolved today", new Point(20, 18), new Size(400, 22), 11F, FontStyle.Bold, Color.White);

            Button btnToday = CreateGhostButton("Jump to Today", new Point(800, 11), new Size(120, 34));
            btnToday.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnToday.Click += (s, e) =>
            {
                activeCalendarDay = DateTime.Now.DayOfWeek.ToString();
                RenderActiveView();
            };
            actionRow.Controls.AddRange(new Control[] { lblCalendarProgress, btnToday });

            calendarListPanel = new FlowLayoutPanel
            {
                Location = new Point(0, 168),
                Size = new Size(940, 440),
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                Margin = new Padding(0),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };

            contentPanel.Controls.AddRange(new Control[] { calendarStripMatrix, actionRow, calendarListPanel });
        }

        private void RebuildCalendarWeeklyStrip()
        {
            calendarStripMatrix.Controls.Clear();
            string actualToday = DateTime.Now.DayOfWeek.ToString();

            for (int i = 0; i < WeekDays.Length; i++)
            {
                string analyticalDay = WeekDays[i];
                bool activeFocusTarget = analyticalDay == activeCalendarDay;
                bool structuralMatch = analyticalDay == actualToday;

                Panel wrapper = new Panel { Dock = DockStyle.Fill, Margin = new Padding(2) };

                Button itemBtn = new Button
                {
                    Text = $"{ShortWeekDays[i]}\n{i + 1:D2}",
                    Tag = analyticalDay,
                    Dock = DockStyle.Fill,
                    FlatStyle = FlatStyle.Flat,
                    Font = new Font("Segoe UI Semibold", 9.5F),
                    TextAlign = ContentAlignment.MiddleCenter,
                    BackColor = activeFocusTarget ? AccentColor : SecondaryBg,
                    ForeColor = activeFocusTarget ? Color.Black : Color.White,
                    Cursor = Cursors.Hand
                };
                itemBtn.FlatAppearance.BorderSize = structuralMatch ? 1 : 0;
                itemBtn.FlatAppearance.BorderColor = AccentColor;

                itemBtn.Click += (s, e) =>
                {
                    if (s is Button target)
                    {
                        activeCalendarDay = target.Tag?.ToString() ?? actualToday;
                        RenderActiveView();
                    }
                };

                wrapper.Controls.Add(itemBtn);
                RoundControl(itemBtn, 4);
                calendarStripMatrix.Controls.Add(wrapper, i, 0);
            }
        }

        private void RenderDashboardView()
        {
            UpdateGreetingContext();

            Panel metricsPanel = CreateCard(new Point(0, 4), new Size(940, 96), 8, SecondaryBg, BorderColor);
            metricsPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Label lblFocusHeading = CreateLabel("Application Runtime Infrastructure", new Point(24, 18), new Size(400, 22), 12F, FontStyle.Bold, AccentColor);
            Label lblFocusPara = CreateLabel("Background monitors stay running to coordinate custom alert queues. Active triggers execute recurring loops directly via local multimedia setups when conditions are met.", new Point(24, 46), new Size(890, 40), 10F, FontStyle.Regular, MutedTextColor);
            metricsPanel.Controls.AddRange(new Control[] { lblFocusHeading, lblFocusPara });

            Label lblUpcomingTitle = CreateLabel("UPCOMING WORKSPACE SCHEDULE", new Point(2, 122), new Size(400, 20), 9.5F, FontStyle.Bold, MutedTextColor);

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

        private void RenderSettingsView()
        {
            UpdateGreetingContext();

            Label lblSection = CreateLabel("DATA ALLOCATION MANAGEMENT", new Point(2, 12), new Size(400, 20), 9.5F, FontStyle.Bold, MutedTextColor);

            btnClearData = CreateGhostButton("Purge System Task Collection", new Point(0, 44), new Size(940, 46));
            btnClearData.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            btnClearData.ForeColor = Color.FromArgb(242, 92, 92);
            btnClearData.Click += (s, e) =>
            {
                if (MessageBox.Show("Delete all active task records permanently?", "Confirm Reset", MessageBoxButtons.YesNo, MessageBoxIcon.Stop) == DialogResult.Yes)
                {
                    masterTaskList.Clear();
                    RefreshDataCounters();
                }
            };

            Label lblSpecs = CreateLabel("App Release v1.4.2  •  Minimalist Core Design", new Point(2, 570), new Size(500, 22), 9F, FontStyle.Regular, MutedTextColor);
            lblSpecs.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;

            contentPanel.Controls.AddRange(new Control[] { lblSection, btnClearData, lblSpecs });
        }

        private void RenderAmbientView()
        {
            UpdateGreetingContext();

            Panel heroPanel = CreateCard(new Point(0, 4), new Size(970, 106), 8, Color.FromArgb(18, 21, 25), BorderColor);
            heroPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Label title = CreateLabel("Ambient Soundscapes", new Point(24, 20), new Size(420, 28), 15F, FontStyle.Bold, Color.White);
            Label subtitle = CreateLabel("Play your own local focus audio offline while you study or work.", new Point(24, 52), new Size(700, 22), 10.5F, FontStyle.Regular, MutedTextColor);
            lblAmbientNowPlaying = CreateLabel(activeAmbientAlias == null ? "Nothing playing" : "Ambient audio playing", new Point(24, 76), new Size(500, 20), 9.5F, FontStyle.Bold, AccentColor);

            Button stopButton = CreateGhostButton("Stop Sound", new Point(820, 34), new Size(126, 36));
            stopButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            stopButton.Click += (s, e) => StopAmbientAudio();
            heroPanel.Controls.AddRange(new Control[] { title, subtitle, lblAmbientNowPlaying, stopButton });

            Button importButton = CreatePrimaryButton("+  Add Local Sound", new Point(0, 132), new Size(170, 40));
            importButton.Click += (s, e) => ImportAndPlayAmbientFile();

            Panel volumePanel = CreateCard(new Point(0, 202), new Size(970, 92), 8, Color.FromArgb(18, 21, 25), BorderColor);
            volumePanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Label volumeTitle = CreateLabel("Sound Volume", new Point(24, 18), new Size(220, 24), 12F, FontStyle.Bold, Color.White);
            Label volumeHint = CreateLabel("Adjust the ambient sound level without changing your task alarms.", new Point(24, 46), new Size(440, 20), 9.5F, FontStyle.Regular, MutedTextColor);
            ambientVolumeSlider = new TrackBar
            {
                Location = new Point(500, 24),
                Size = new Size(330, 44),
                Minimum = 0,
                Maximum = 100,
                TickFrequency = 10,
                Value = ambientVolume,
                BackColor = Color.FromArgb(18, 21, 25),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            Label volumeValue = CreateLabel($"{ambientVolume}%", new Point(850, 31), new Size(70, 24), 11F, FontStyle.Bold, AccentColor);
            volumeValue.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            ambientVolumeSlider.Scroll += (s, e) =>
            {
                ambientVolume = ambientVolumeSlider.Value;
                volumeValue.Text = $"{ambientVolume}%";
                ApplyAmbientVolume();
            };
            volumePanel.Controls.AddRange(new Control[] { volumeTitle, volumeHint, ambientVolumeSlider, volumeValue });

            Panel emptyPanel = CreateCard(new Point(0, 318), new Size(970, 110), 8, Color.FromArgb(18, 21, 25), BorderColor);
            emptyPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Label emptyTitle = CreateLabel("No built-in sounds", new Point(24, 24), new Size(320, 24), 12F, FontStyle.Bold, Color.White);
            Label emptyText = CreateLabel("Use Add Local Sound to choose an mp3, mp4, wav, or other audio file from your computer.", new Point(24, 54), new Size(760, 22), 10F, FontStyle.Regular, MutedTextColor);
            emptyPanel.Controls.AddRange(new Control[] { emptyTitle, emptyText });

            contentPanel.Controls.AddRange(new Control[] { heroPanel, importButton, volumePanel, emptyPanel });
        }

        private Panel CreateCompactStatCard(string header, Color iconColor, out Label lblValue)
        {
            Panel card = CreateCard(new Point(0, 0), new Size(264, 86), 8, Color.FromArgb(18, 21, 25), BorderColor);
            card.Margin = new Padding(0, 0, 12, 0);

            Panel iconBox = new Panel { Location = new Point(20, 20), Size = new Size(52, 52), BackColor = Color.FromArgb(18 + iconColor.R / 6, 22 + iconColor.G / 6, 26 + iconColor.B / 6) };
            Label icon = CreateLabel(header.StartsWith("Active") ? "▣" : "✓", new Point(0, 9), new Size(52, 30), 17F, FontStyle.Bold, iconColor);
            icon.TextAlign = ContentAlignment.MiddleCenter;
            iconBox.Controls.Add(icon);
            RoundControl(iconBox, 8);

            Label lblTitle = CreateLabel(header, new Point(96, 22), new Size(140, 22), 11F, FontStyle.Regular, Color.FromArgb(220, 220, 220));
            lblValue = CreateLabel("0", new Point(96, 48), new Size(120, 32), 18F, FontStyle.Bold, Color.White);

            card.Controls.AddRange(new Control[] { iconBox, lblTitle, lblValue });
            return card;
        }

        private void RefreshDataCounters()
        {
            int pending = masterTaskList.Count(t => !t.Completed);
            int resolvedToday = masterTaskList.Count(t => t.Completed && t.Day == DateTime.Now.DayOfWeek.ToString());
            int total = masterTaskList.Count;
            int completed = masterTaskList.Count(t => t.Completed);
            int completePercent = total == 0 ? 0 : (int)Math.Round(completed * 100d / total);

            if (lblStatActiveCount != null) lblStatActiveCount.Text = pending.ToString();
            if (lblStatDoneCount != null) lblStatDoneCount.Text = resolvedToday.ToString();
            if (lblHeaderSubtitle != null) lblHeaderSubtitle.Text = $"You have {pending} tasks remaining today.";
            if (lblSidebarRemaining != null) lblSidebarRemaining.Text = $"{pending} Tasks Remaining";
            if (lblSidebarPercent != null) lblSidebarPercent.Text = $"{completePercent}%";
            if (sidebarProgressFill != null && sidebarProgressTrack != null)
            {
                sidebarProgressFill.Width = Math.Max(0, Math.Min(sidebarProgressTrack.Width, sidebarProgressTrack.Width * completePercent / 100));
                RoundControl(sidebarProgressFill, 5);
            }

            if (taskListPanel != null && !taskListPanel.IsDisposed) BindTaskListView();
            if (calendarListPanel != null && !calendarListPanel.IsDisposed) BindCalendarView();
            if (dashboardFocusPanel != null && !dashboardFocusPanel.IsDisposed) BindDashboardFocusView();
        }

        private IEnumerable<TaskItem> GetFilteredTasks(IEnumerable<TaskItem> SourceList)
        {
            if (string.IsNullOrWhiteSpace(searchFilterQuery)) return SourceList;
            return SourceList.Where(t => t.Title.Contains(searchFilterQuery, StringComparison.OrdinalIgnoreCase));
        }

        private void BindTaskListView()
        {
            taskListPanel.Controls.Clear();
            var filtered = GetFilteredTasks(masterTaskList).ToList();
            lblTaskCount.Text = $"{filtered.Count(t => !t.Completed)} tasks outstanding";

            if (!filtered.Any())
            {
                taskListPanel.Controls.Add(CreateEmptyStateRow("No matching parameters resolved.", "Refine search syntax parameters or add new tasks via header."));
                return;
            }

            foreach (TaskItem task in filtered.OrderBy(t => Array.IndexOf(WeekDays, t.Day)).ThenBy(t => t.RemindTime))
            {
                taskListPanel.Controls.Add(BuildTaskRowCard(task));
            }
        }

        private void BindCalendarView()
        {
            calendarListPanel.Controls.Clear();
            var items = GetFilteredTasks(masterTaskList.Where(t => t.Day == activeCalendarDay)).ToList();
            lblCalendarProgress.Text = $"{items.Count(t => t.Completed)} of {items.Count} parameters processed";

            if (!items.Any())
            {
                calendarListPanel.Controls.Add(CreateEmptyStateRow("Clear timeline.", "No specific objectives mapped onto this grid node."));
                return;
            }

            foreach (TaskItem task in items)
            {
                calendarListPanel.Controls.Add(BuildTaskRowCard(task));
            }
        }

        private void BindDashboardFocusView()
        {
            dashboardFocusPanel.Controls.Clear();
            string todayStr = DateTime.Now.DayOfWeek.ToString();
            var remainingItems = GetFilteredTasks(masterTaskList.Where(t => t.Day == todayStr && !t.Completed)).ToList();

            if (!remainingItems.Any())
            {
                dashboardFocusPanel.Controls.Add(CreateEmptyStateRow("Timeline caught up.", "All objectives for today have been sorted."));
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
            Panel card = CreateCard(new Point(0, 0), new Size(rowWidth, 84), 8, Color.FromArgb(18, 21, 25), BorderColor);
            card.Margin = new Padding(0, 0, 0, 10);

            card.MouseEnter += (s, e) => { card.BackColor = CardHoverBg; card.Invalidate(); };
            card.MouseLeave += (s, e) => { card.BackColor = SecondaryBg; card.Invalidate(); };

            Button btnToggle = new Button
            {
                Text = item.Completed ? "✓" : string.Empty,
                Location = new Point(22, 24),
                Size = new Size(38, 38),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 14F, FontStyle.Bold),
                BackColor = item.Completed ? AccentColor : Color.Transparent,
                ForeColor = Color.Black,
                Cursor = Cursors.Hand
            };
            btnToggle.FlatAppearance.BorderSize = item.Completed ? 0 : 2;
            btnToggle.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 80);

            btnToggle.Click += (s, e) =>
            {
                item.Completed = !item.Completed;
                RefreshDataCounters();
            };
            RoundControl(btnToggle, 19);

            int badgeLeft = Math.Max(520, rowWidth - 200);
            int optionsLeft = Math.Max(570, rowWidth - 50);
            int textWidth = Math.Max(260, badgeLeft - 110);

            Label lblTitle = CreateLabel(item.Title, new Point(86, 20), new Size(textWidth, 24), 12F, FontStyle.Bold, item.Completed ? MutedTextColor : Color.White);
            Label lblMeta = CreateLabel($"□  {item.Day}    •    ◷  {item.RemindTime}", new Point(86, 49), new Size(textWidth, 20), 9.5F, FontStyle.Regular, MutedTextColor);

            lblTitle.MouseEnter += (s, e) => { card.BackColor = CardHoverBg; card.Invalidate(); };
            lblMeta.MouseEnter += (s, e) => { card.BackColor = CardHoverBg; card.Invalidate(); };

            Color badgeColor = GetTaskTypeColor(item.Type);
            Panel badgePanel = new Panel { Size = new Size(122, 30), Location = new Point(badgeLeft, 27), BackColor = Color.FromArgb(22 + badgeColor.R / 7, 22 + badgeColor.G / 7, 24 + badgeColor.B / 7) };
            Label lblBadgeText = CreateLabel(item.Type, new Point(0, 5), new Size(122, 20), 9F, FontStyle.Bold, badgeColor);
            lblBadgeText.TextAlign = ContentAlignment.MiddleCenter;
            badgePanel.Controls.Add(lblBadgeText);
            RoundControl(badgePanel, 5);

            Button btnOptions = new Button
            {
                Text = "⋮",
                Location = new Point(optionsLeft, 24),
                Size = new Size(36, 36),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 14F, FontStyle.Bold),
                ForeColor = MutedTextColor,
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand
            };
            btnOptions.FlatAppearance.BorderSize = 0;

            btnOptions.Click += (s, e) =>
            {
                ContextMenuStrip contextMenu = new ContextMenuStrip();
                contextMenu.Items.Add("Complete Task", null, (src, ev) => { item.Completed = true; RefreshDataCounters(); });
                contextMenu.Items.Add("Duplicate Entry", null, (src, ev) => {
                    masterTaskList.Add(new TaskItem { Id = Guid.NewGuid().ToString(), Title = item.Title + " (Copy)", Day = item.Day, RemindTime = item.RemindTime, Type = item.Type, Completed = false });
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

        private static Color GetTaskTypeColor(string taskType)
        {
            return taskType switch
            {
                TaskTypeAlarm => AlarmColor,
                TaskTypeImportant => WarningColor,
                _ => AccentColor
            };
        }

        [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
        private static extern int mciSendString(string command, string? returnValue, int returnLength, IntPtr winHandle);

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
            StopAmbientAudio();

            activeAmbientAlias = "ambient" + Guid.NewGuid().ToString("N");
            string escapedPath = filePath.Replace("\"", string.Empty);
            int openResult = mciSendString($"open \"{escapedPath}\" alias {activeAmbientAlias}", null, 0, IntPtr.Zero);
            if (openResult != 0)
            {
                activeAmbientAlias = null;
                MessageBox.Show("This sound file could not be opened. Try another mp3, mp4, wav, or audio file.", "Soundscape Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            ApplyAmbientVolume();
            mciSendString($"play {activeAmbientAlias} repeat", null, 0, IntPtr.Zero);
            if (lblAmbientNowPlaying != null)
            {
                lblAmbientNowPlaying.Text = $"Playing: {displayName}";
            }
        }

        private void ApplyAmbientVolume()
        {
            if (activeAmbientAlias == null)
            {
                return;
            }

            int mciVolume = Math.Max(0, Math.Min(1000, ambientVolume * 10));
            mciSendString($"setaudio {activeAmbientAlias} volume to {mciVolume}", null, 0, IntPtr.Zero);
        }

        private void StopAmbientAudio()
        {
            if (activeAmbientAlias == null)
            {
                if (lblAmbientNowPlaying != null) lblAmbientNowPlaying.Text = "Nothing playing";
                return;
            }

            mciSendString($"stop {activeAmbientAlias}", null, 0, IntPtr.Zero);
            mciSendString($"close {activeAmbientAlias}", null, 0, IntPtr.Zero);
            activeAmbientAlias = null;
            if (lblAmbientNowPlaying != null) lblAmbientNowPlaying.Text = "Nothing playing";
        }

        private Control CreateEmptyStateRow(string head, string sub)
        {
            Panel emptyPanel = new Panel { Size = new Size(920, 140), Margin = new Padding(0, 16, 0, 0) };
            Label mainLabel = CreateLabel(head, new Point(0, 40), new Size(920, 24), 13F, FontStyle.Bold, Color.FromArgb(100, 100, 100));
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

        private Button CreatePrimaryButton(string text, Point loc, Size size)
        {
            Button btn = new Button
            {
                Text = text,
                Location = loc,
                Size = size,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI Semibold", 10F),
                BackColor = AccentColor,
                ForeColor = Color.Black,
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 0;
            RoundControl(btn, 18);
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
                Font = new Font("Segoe UI Semibold", 9.5F),
                BackColor = Color.Transparent,
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 1;
            btn.FlatAppearance.BorderColor = BorderColor;
            RoundControl(btn, 4);
            return btn;
        }

        private Panel CreateCard(Point loc, Size size, int radius, Color bg, Color border)
        {
            Panel cardPanel = new Panel { Location = loc, Size = size, BackColor = bg };
            cardPanel.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle rect = new Rectangle(0, 0, cardPanel.Width - 1, cardPanel.Height - 1);

                using (GraphicsPath path = GetRoundRectPath(rect, radius))
                {
                    using (Pen pen = new Pen(border, 1))
                        e.Graphics.DrawPath(pen, path);
                }
            };
            return cardPanel;
        }

        private void UpdateGreetingContext()
        {
            int hour = DateTime.Now.Hour;
            string structuralPrefix = hour < 12 ? "Good Morning" : hour < 18 ? "Good Afternoon" : "Good Evening";

            lblGreeting.Text = $"{structuralPrefix}, Jeptah 👋";
            lblHeaderSubtitle.Text = activeView switch
            {
                "Dashboard" => "A quick look at what needs your attention.",
                "Calendar" => "Plan your week and check what is due next.",
                "Settings" => "Tune the app and manage your task data.",
                _ => $"You have {masterTaskList.Count(t => !t.Completed)} tasks remaining today."
            };
        }

        private static GraphicsPath GetRoundRectPath(Rectangle r, int radius)
        {
            int d = radius * 2;
            GraphicsPath path = new GraphicsPath();
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

        private void InitializeAlarmEngine()
        {
            backgroundAlarmTicker = new System.Windows.Forms.Timer { Interval = 1000 };
            backgroundAlarmTicker.Tick += OnClockEngineTick;
            backgroundAlarmTicker.Start();
        }

        private void OnClockEngineTick(object? sender, EventArgs e)
        {
            DateTime now = DateTime.Now;
            string currentDay = now.DayOfWeek.ToString();

            var alerts = masterTaskList
                .Where(t => IsTaskDueNow(t, currentDay, now))
                .ToList();

            foreach (TaskItem task in alerts)
            {
                triggeredAlertKeys.Add(BuildAlertKey(task, now));
                task.Completed = true;
                RefreshDataCounters();

                if (task.Type == TaskTypeAlarm)
                {
                    try
                    {
                        PlayAlarmMediaAudioLoop();
                        MessageBox.Show($"Alarm Triggered:\n\n\"{task.Title}\"", "Alarm Alert", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    finally
                    {
                        StopAlarmMediaAudioLoop();
                    }
                }
                else
                {
                    System.Media.SystemSounds.Asterisk.Play();
                    MessageBox.Show($"Task Reminder:\n\n\"{task.Title}\"", "Task Notification", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        private bool IsTaskDueNow(TaskItem task, string currentDay, DateTime now)
        {
            if (task.Completed || !task.Day.Equals(currentDay, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!DateTime.TryParse(task.RemindTime, out DateTime scheduledTime))
            {
                return false;
            }

            bool sameMinute = scheduledTime.Hour == now.Hour && scheduledTime.Minute == now.Minute;
            return sameMinute && !triggeredAlertKeys.Contains(BuildAlertKey(task, now));
        }

        private static string BuildAlertKey(TaskItem task, DateTime now)
        {
            return $"{task.Id}|{now:yyyy-MM-dd HH:mm}";
        }

        private void PlayAlarmMediaAudioLoop()
        {
            string audioPath = Path.Combine(AppContext.BaseDirectory, "Assets", "alarm.wav");
            if (!File.Exists(audioPath))
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }
            StopAlarmMediaAudioLoop();
            activeAlarmPlayer = new System.Media.SoundPlayer(audioPath);
            activeAlarmPlayer.Load();
            activeAlarmPlayer.PlayLooping();
        }

        private void StopAlarmMediaAudioLoop()
        {
            activeAlarmPlayer?.Stop();
            activeAlarmPlayer?.Dispose();
            activeAlarmPlayer = null;
        }

        private void LoadSampleMockData()
        {
            masterTaskList.AddRange(new[]
            {
                new TaskItem { Id = "1", Title = "Review design system guidelines and assets", Day = "Monday", RemindTime = "08:00 AM", Type = TaskTypeImportant, Completed = true },
                new TaskItem { Id = "2", Title = "Sync development branch with production matrix", Day = DateTime.Now.DayOfWeek.ToString(), RemindTime = "07:30 AM", Type = TaskTypeAlarm, Completed = false },
                new TaskItem { Id = "3", Title = "Refactor background layout memory allocation loops", Day = DateTime.Now.DayOfWeek.ToString(), RemindTime = "11:00 PM", Type = TaskTypeNotification, Completed = false }
            });
        }

        private void Form1_Load(object sender, EventArgs e)
        {
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            StopAmbientAudio();
            base.OnFormClosing(e);
        }
    }

    public sealed class TaskCreateDialog : Form
    {
        private const string PlaceholderTaskName = "What needs to be done?";

        public TaskItem GeneratedTask { get; private set; } = null!;

        private TextBox txtInput = null!;
        private ComboBox cmbDay = null!;
        private ComboBox cmbType = null!;
        private ComboBox cmbHr = null!;
        private ComboBox cmbMin = null!;
        private ComboBox cmbAmPm = null!;
        private Button btnSave = null!;
        private Button btnCancel = null!;

        public TaskCreateDialog()
        {
            InitializeDialogCanvas();
        }

        private void InitializeDialogCanvas()
        {
            Text = "Create Task";
            ClientSize = new Size(560, 360);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(13, 15, 18);
            Font = new Font("Segoe UI", 10F);

            // Title Label
            Label lblHead = new Label { Text = "Create Task", Location = new Point(28, 24), Size = new Size(220, 28), Font = new Font("Segoe UI Semibold", 12F, FontStyle.Bold), ForeColor = Color.White, BackColor = Color.Transparent };
            Label lblSub = new Label { Text = "Add a reminder, alarm, or important task.", Location = new Point(28, 54), Size = new Size(360, 20), Font = new Font("Segoe UI", 9F), ForeColor = Color.FromArgb(170, 174, 180), BackColor = Color.Transparent };

            // Task Text Input Field
            Label lblTask = new Label { Text = "Task Name", Location = new Point(28, 94), Size = new Size(160, 18), Font = new Font("Segoe UI Semibold", 8.5F), ForeColor = Color.FromArgb(205, 207, 211), BackColor = Color.Transparent };
            txtInput = new TextBox { Location = new Point(28, 118), Size = new Size(504, 32), Font = new Font("Segoe UI", 10.5F), BackColor = Color.FromArgb(18, 21, 25), ForeColor = Color.FromArgb(170, 174, 180), BorderStyle = BorderStyle.FixedSingle, Text = PlaceholderTaskName };
            txtInput.GotFocus += (s, e) =>
            {
                if (txtInput.Text == PlaceholderTaskName)
                {
                    txtInput.Text = string.Empty;
                    txtInput.ForeColor = Color.White;
                }
            };
            txtInput.LostFocus += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(txtInput.Text))
                {
                    txtInput.Text = PlaceholderTaskName;
                    txtInput.ForeColor = Color.FromArgb(170, 174, 180);
                }
            };

            // Day Config Picker
            Label lblSchedule = new Label { Text = "Schedule", Location = new Point(28, 174), Size = new Size(160, 18), Font = new Font("Segoe UI Semibold", 8.5F), ForeColor = Color.FromArgb(205, 207, 211), BackColor = Color.Transparent };
            Label lblDay = new Label { Text = "Day", Location = new Point(28, 198), Size = new Size(80, 18), Font = new Font("Segoe UI", 8.5F), ForeColor = Color.FromArgb(170, 174, 180), BackColor = Color.Transparent };
            cmbDay = CreateCombo(new Point(28, 220), new Size(154, 32), new[] { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" }, 1);

            Label lblHour = new Label { Text = "Hour", Location = new Point(206, 198), Size = new Size(60, 18), Font = new Font("Segoe UI", 8.5F), ForeColor = Color.FromArgb(170, 174, 180), BackColor = Color.Transparent };
            cmbHr = CreateCombo(new Point(206, 220), new Size(70, 32), Enumerable.Range(1, 12).Select(h => h.ToString("D2")).ToArray(), 7);
            Label lblMinute = new Label { Text = "Minute", Location = new Point(292, 198), Size = new Size(70, 18), Font = new Font("Segoe UI", 8.5F), ForeColor = Color.FromArgb(170, 174, 180), BackColor = Color.Transparent };
            cmbMin = CreateCombo(new Point(292, 220), new Size(78, 32), Enumerable.Range(0, 60).Select(m => m.ToString("D2")).ToArray(), 0);
            Label lblAmPm = new Label { Text = "AM/PM", Location = new Point(386, 198), Size = new Size(80, 18), Font = new Font("Segoe UI", 8.5F), ForeColor = Color.FromArgb(170, 174, 180), BackColor = Color.Transparent };
            cmbAmPm = CreateCombo(new Point(386, 220), new Size(78, 32), new[] { "AM", "PM" }, 0);

            // Notification / Alarm Type Config Picker
            Label lblType = new Label { Text = "Type", Location = new Point(28, 272), Size = new Size(130, 18), Font = new Font("Segoe UI Semibold", 8.5F), ForeColor = Color.FromArgb(205, 207, 211), BackColor = Color.Transparent };
            cmbType = CreateCombo(new Point(28, 294), new Size(210, 32), new[] { "Notification", "Alarm", "Important" }, 0);

            // Action Execution Buttons
            btnSave = new Button { Text = "Create Task", Location = new Point(392, 292), Size = new Size(140, 36), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(30, 215, 96), ForeColor = Color.Black, Font = new Font("Segoe UI Semibold", 9F), Cursor = Cursors.Hand };
            btnSave.FlatAppearance.BorderSize = 0;
            btnSave.Click += OnSaveSubmitted;

            btnCancel = new Button { Text = "Cancel", Location = new Point(254, 292), Size = new Size(118, 36), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(16, 18, 22), ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 9F), Cursor = Cursors.Hand };
            btnCancel.FlatAppearance.BorderSize = 1;
            btnCancel.FlatAppearance.BorderColor = Color.FromArgb(45, 45, 45);
            btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

            Controls.AddRange(new Control[] { lblHead, lblSub, lblTask, txtInput, lblSchedule, lblDay, cmbDay, lblHour, cmbHr, lblMinute, cmbMin, lblAmPm, cmbAmPm, lblType, cmbType, btnSave, btnCancel });
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
                BackColor = Color.FromArgb(18, 21, 25),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9.5F)
            };
            b.Items.AddRange(items);
            if (items.Length > 0) b.SelectedIndex = idx;
            b.DrawItem += DrawDarkComboItem;
            return b;
        }

        private void DrawDarkComboItem(object? sender, DrawItemEventArgs e)
        {
            if (sender is not ComboBox combo || e.Index < 0)
            {
                return;
            }

            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            using SolidBrush background = new SolidBrush(selected ? Color.FromArgb(42, 48, 54) : Color.FromArgb(18, 21, 25));
            using SolidBrush textBrush = new SolidBrush(Color.White);
            e.Graphics.FillRectangle(background, e.Bounds);
            e.Graphics.DrawString(combo.Items[e.Index]?.ToString() ?? string.Empty, combo.Font, textBrush, e.Bounds.X + 4, e.Bounds.Y + 3);
            e.DrawFocusRectangle();
        }

        private void OnSaveSubmitted(object? sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtInput.Text) || txtInput.Text == PlaceholderTaskName)
            {
                txtInput.Focus();
                return;
            }

            GeneratedTask = new TaskItem
            {
                Id = Guid.NewGuid().ToString(),
                Title = txtInput.Text.Trim(),
                Day = cmbDay.SelectedItem?.ToString() ?? "Monday",
                Type = cmbType.SelectedItem?.ToString() ?? "Notification",
                RemindTime = $"{cmbHr.SelectedItem ?? "08"}:{cmbMin.SelectedItem ?? "00"} {cmbAmPm.SelectedItem ?? "PM"}",
                Completed = false
            };

            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
