using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
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
        private string activeCalendarDay = DateTime.Now.DayOfWeek.ToString();
        private string activeView = "Tasks";
        private string searchFilterQuery = string.Empty;

        // Spotify Premium Inspired Theme Palette
        private static readonly Color PrimaryBg = Color.FromArgb(18, 18, 18);
        private static readonly Color SecondaryBg = Color.FromArgb(24, 24, 24);
        private static readonly Color SidebarBg = Color.FromArgb(0, 0, 0);
        private static readonly Color CardBgColor = Color.FromArgb(32, 32, 32);
        private static readonly Color CardHoverBg = Color.FromArgb(38, 38, 38);
        private static readonly Color AccentColor = Color.FromArgb(30, 215, 96); // Authentic Spotify Green
        private static readonly Color MutedTextColor = Color.FromArgb(179, 179, 179);
        private static readonly Color BorderColor = Color.FromArgb(45, 45, 45);

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
        private Button btnSettings = null!;

        // Interactive Labels
        private Label lblGreeting = null!;
        private Label lblHeaderSubtitle = null!;
        private Label lblStatActiveCount = null!;
        private Label lblStatDoneCount = null!;

        // Dynamic Panels
        private FlowLayoutPanel taskListPanel = null!;
        private FlowLayoutPanel dashboardFocusPanel = null!;
        private TableLayoutPanel calendarStripMatrix = null!;
        private FlowLayoutPanel calendarListPanel = null!;
        private Label lblCalendarProgress = null!;
        private Label lblTaskCount = null!;
        private Button btnClearData = null!;

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
            MinimumSize = new Size(1200, 720);
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 10F);
            BackColor = PrimaryBg;

            // Base Layout Splits
            sidebarPanel = new Panel { Dock = DockStyle.Left, Width = 240, BackColor = SidebarBg, Padding = new Padding(0, 24, 0, 0) };
            mainContainer = new Panel { Dock = DockStyle.Fill, BackColor = PrimaryBg, Padding = new Padding(40, 24, 40, 24) };

            Controls.Add(mainContainer);
            Controls.Add(sidebarPanel);

            BuildSidebarNavigation();
            BuildMainWorkspaceLayout();
        }

        private void BuildSidebarNavigation()
        {
            Label lblAppLogo = CreateLabel("TaskFlow", new Point(24, 16), new Size(180, 32), 18F, FontStyle.Bold, Color.White);
            sidebarPanel.Controls.Add(lblAppLogo);

            activeNavIndicator = new Panel { Width = 4, Height = 44, BackColor = AccentColor, Location = new Point(0, 0) };
            sidebarPanel.Controls.Add(activeNavIndicator);

            btnDashboard = CreateSidebarButton("🏠  Dashboard", "Dashboard", 86);
            btnTasks = CreateSidebarButton("✓  Tasks", "Tasks", 136);
            btnCalendar = CreateSidebarButton("📅  Calendar", "Calendar", 186);
            btnSettings = CreateSidebarButton("⚙  Settings", "Settings", 236);

            sidebarPanel.Controls.AddRange(new Control[] { btnDashboard, btnTasks, btnCalendar, btnSettings });
        }

        private Button CreateSidebarButton(string text, string viewKey, int topPosition)
        {
            Button btn = new Button
            {
                Text = text,
                Tag = viewKey,
                Location = new Point(0, topPosition),
                Size = new Size(240, 44),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI Semibold", 10F),
                ForeColor = MutedTextColor,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(24, 0, 0, 0),
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

        private void BuildMainWorkspaceLayout()
        {
            headerPanel = new Panel { Dock = DockStyle.Top, Height = 90, BackColor = Color.Transparent };
            contentPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 16, 0, 0), BackColor = Color.Transparent };

            mainContainer.Controls.Add(contentPanel);
            mainContainer.Controls.Add(headerPanel);

            // Responsive Greeting Setup
            lblGreeting = CreateLabel("Stay Productive 🎯", new Point(0, 8), new Size(420, 36), 22F, FontStyle.Bold, Color.White);
            lblHeaderSubtitle = CreateLabel("Ready for your next milestone.", new Point(2, 48), new Size(420, 20), 10.5F, FontStyle.Regular, MutedTextColor);
            headerPanel.Controls.AddRange(new Control[] { lblGreeting, lblHeaderSubtitle });

            // Optimized Search Bar Placement
            txtSearch = new TextBox
            {
                Location = new Point(570, 20),
                Size = new Size(220, 36),
                Font = new Font("Segoe UI", 11F),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = CardBgColor,
                ForeColor = MutedTextColor,
                Text = PlaceholderSearch,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };

            txtSearch.GotFocus += (s, e) => {
                if (txtSearch.Text == PlaceholderSearch)
                {
                    txtSearch.Text = string.Empty;
                    txtSearch.ForeColor = Color.White;
                }
            };

            txtSearch.LostFocus += (s, e) => {
                if (string.IsNullOrWhiteSpace(txtSearch.Text))
                {
                    txtSearch.Text = PlaceholderSearch;
                    txtSearch.ForeColor = MutedTextColor;
                }
            };

            txtSearch.TextChanged += (s, e) =>
            {
                searchFilterQuery = txtSearch.Text == PlaceholderSearch ? string.Empty : txtSearch.Text.Trim();
                RefreshDataCounters();
            };
            headerPanel.Controls.Add(txtSearch);

            // Optimized New Task Action Layout Position
            btnNewTask = CreatePrimaryButton("+ New Task", new Point(810, 20), new Size(130, 36));
            btnNewTask.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnNewTask.Click += (s, e) => { ExecuteModalTaskCreationDialogue(); };
            headerPanel.Controls.Add(btnNewTask);
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
                "Settings" => btnSettings,
                _ => btnTasks
            };

            activeNavIndicator.Location = new Point(0, targetBtn.Top);

            foreach (Button btn in new[] { btnDashboard, btnTasks, btnCalendar, btnSettings })
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
                Size = new Size(940, 96),
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.Transparent,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            statsRowPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160F));
            statsRowPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160F));

            statsRowPanel.Controls.Add(CreateCompactStatCard("📋 Active Tasks", out lblStatActiveCount), 0, 0);
            statsRowPanel.Controls.Add(CreateCompactStatCard("✓ Done Today", out lblStatDoneCount), 1, 0);
            contentPanel.Controls.Add(statsRowPanel);

            lblTaskCount = CreateLabel("Active Tasks Scheduled", new Point(2, 120), new Size(400, 22), 11F, FontStyle.Bold, Color.White);

            taskListPanel = new FlowLayoutPanel
            {
                Location = new Point(0, 154),
                Size = new Size(940, 450),
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

        private Panel CreateCompactStatCard(string header, out Label lblValue)
        {
            Panel card = CreateCard(new Point(0, 0), new Size(144, 80), 6, SecondaryBg, BorderColor);
            Label lblTitle = CreateLabel(header, new Point(16, 14), new Size(120, 18), 9F, FontStyle.Bold, MutedTextColor);
            lblValue = CreateLabel("0", new Point(16, 38), new Size(120, 32), 16F, FontStyle.Bold, Color.White);

            card.Controls.AddRange(new Control[] { lblTitle, lblValue });
            return card;
        }

        private void RefreshDataCounters()
        {
            int pending = masterTaskList.Count(t => !t.Completed);
            int resolvedToday = masterTaskList.Count(t => t.Completed && t.Day == DateTime.Now.DayOfWeek.ToString());

            if (lblStatActiveCount != null) lblStatActiveCount.Text = pending.ToString();
            if (lblStatDoneCount != null) lblStatDoneCount.Text = resolvedToday.ToString();

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
            Panel card = CreateCard(new Point(0, 0), new Size(920, 88), 6, SecondaryBg, BorderColor);
            card.Margin = new Padding(0, 0, 0, 10);

            card.MouseEnter += (s, e) => { card.BackColor = CardHoverBg; card.Invalidate(); };
            card.MouseLeave += (s, e) => { card.BackColor = SecondaryBg; card.Invalidate(); };

            Button btnToggle = new Button
            {
                Text = item.Completed ? "✓" : string.Empty,
                Location = new Point(24, 28),
                Size = new Size(32, 32),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
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
            RoundControl(btnToggle, 16);

            Label lblTitle = CreateLabel(item.Title, new Point(76, 22), new Size(540, 24), 11.5F, FontStyle.Bold, item.Completed ? MutedTextColor : Color.White);
            Label lblMeta = CreateLabel($"{item.Day}  •  {item.RemindTime}", new Point(76, 48), new Size(400, 18), 9F, FontStyle.Regular, MutedTextColor);

            lblTitle.MouseEnter += (s, e) => { card.BackColor = CardHoverBg; card.Invalidate(); };
            lblMeta.MouseEnter += (s, e) => { card.BackColor = CardHoverBg; card.Invalidate(); };

            Panel badgePanel = new Panel { Size = new Size(110, 26), Location = new Point(660, 31), BackColor = CardBgColor };
            Label lblBadgeText = CreateLabel(item.Type, new Point(0, 4), new Size(110, 18), 8F, FontStyle.Bold, AccentColor);
            lblBadgeText.TextAlign = ContentAlignment.MiddleCenter;
            badgePanel.Controls.Add(lblBadgeText);
            RoundControl(badgePanel, 4);

            Button btnOptions = new Button
            {
                Text = "⋮",
                Location = new Point(850, 26),
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
                "Dashboard" => "Here is your global system agenda overview.",
                "Calendar" => "Filter configurations and check structural weekly alignments.",
                "Settings" => "Configure framework parameters and core runtime variables.",
                _ => "Structure upcoming milestones and project logs cleanly."
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
            string systemTime = DateTime.Now.ToString("hh:mm TT");
            string currentDay = DateTime.Now.DayOfWeek.ToString();

            var alerts = masterTaskList
                .Where(t => t.Day == currentDay && t.RemindTime.Equals(systemTime, StringComparison.OrdinalIgnoreCase) && !t.Completed)
                .ToList();

            foreach (TaskItem task in alerts)
            {
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
            Text = "Create New Objective Task";
            ClientSize = new Size(540, 320);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(24, 24, 24);
            Font = new Font("Segoe UI", 10F);

            // Title Label
            Label lblHead = new Label { Text = "NEW DESKTOP TASK PARAMETERS", Location = new Point(24, 24), Size = new Size(400, 20), Font = new Font("Segoe UI", 9F, FontStyle.Bold), ForeColor = Color.FromArgb(179, 179, 179) };

            // Task Text Input Field
            txtInput = new TextBox { Location = new Point(24, 60), Size = new Size(490, 30), Font = new Font("Segoe UI", 11F), BackColor = Color.FromArgb(32, 32, 32), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Text = PlaceholderTaskName };
            txtInput.GotFocus += (s, e) => { if (txtInput.Text == PlaceholderTaskName) txtInput.Text = string.Empty; };

            // Day Config Picker
            Label lblDay = new Label { Text = "Execution Day", Location = new Point(24, 114), Size = new Size(140, 18), Font = new Font("Segoe UI", 8.5F), ForeColor = Color.FromArgb(179, 179, 179) };
            cmbDay = CreateCombo(new Point(24, 136), new Size(140, 30), new[] { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" }, 1);

            // Notification / Alarm Type Config Picker
            Label lblType = new Label { Text = "Alert Configuration", Location = new Point(184, 114), Size = new Size(130, 18), Font = new Font("Segoe UI", 8.5F), ForeColor = Color.FromArgb(179, 179, 179) };
            cmbType = CreateCombo(new Point(184, 136), new Size(130, 30), new[] { "Notification", "Alarm", "Important" }, 0);

            // Hour, Minute, and AM/PM Selector Dropdowns
            Label lblTime = new Label { Text = "Target Time Configuration", Location = new Point(334, 114), Size = new Size(180, 18), Font = new Font("Segoe UI", 8.5F), ForeColor = Color.FromArgb(179, 179, 179) };
            cmbHr = CreateCombo(new Point(334, 136), new Size(54, 30), Enumerable.Range(1, 12).Select(h => h.ToString("D2")).ToArray(), 7);
            cmbMin = CreateCombo(new Point(394, 136), new Size(54, 30), Enumerable.Range(0, 60).Select(m => m.ToString("D2")).ToArray(), 0);
            cmbAmPm = CreateCombo(new Point(454, 136), new Size(60, 30), new[] { "AM", "PM" }, 0);

            // Action Execution Buttons
            btnSave = new Button { Text = "Confirm Task", Location = new Point(256, 240), Size = new Size(124, 38), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(30, 215, 96), ForeColor = Color.Black, Font = new Font("Segoe UI Semibold", 9.5F), Cursor = Cursors.Hand };
            btnSave.FlatAppearance.BorderSize = 0;
            btnSave.Click += OnSaveSubmitted;

            btnCancel = new Button { Text = "Cancel", Location = new Point(390, 240), Size = new Size(124, 38), FlatStyle = FlatStyle.Flat, BackColor = Color.Transparent, ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 9.5F), Cursor = Cursors.Hand };
            btnCancel.FlatAppearance.BorderColor = Color.FromArgb(45, 45, 45);
            btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

            Controls.AddRange(new Control[] { lblHead, txtInput, lblDay, cmbDay, lblType, cmbType, lblTime, cmbHr, cmbMin, cmbAmPm, btnSave, btnCancel });
        }

        private ComboBox CreateCombo(Point p, Size s, string[] items, int idx)
        {
            ComboBox b = new ComboBox { Location = p, Size = s, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(32, 32, 32), ForeColor = Color.White };
            b.Items.AddRange(items);
            if (items.Length > 0) b.SelectedIndex = idx;
            return b;
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