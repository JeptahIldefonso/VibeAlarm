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
        private const string PlaceholderTaskName = "Habit track name...";
        private const string TaskTypeNotification = "Notification";
        private const string TaskTypeAlarm = "Alarm";

        private static readonly string[] WeekDays =
        {
            "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"
        };

        private static readonly string[] ShortWeekDays =
        {
            "SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT"
        };

        [DllImport("Gdi32.dll", EntryPoint = "CreateRoundRectRgn")]
        private static extern IntPtr CreateRoundRectRgn(
            int nLeftRect,
            int nTopRect,
            int nRightRect,
            int nBottomRect,
            int nWidthEllipse,
            int nHeightEllipse);

        private readonly List<TaskItem> masterTaskList = new();

        private string activeCalendarDay = "Monday";
        private ThemePreset activeTheme = null!;
        private IReadOnlyList<ThemePreset> appThemes = Array.Empty<ThemePreset>();
        private System.Windows.Forms.Timer backgroundAlarmTicker = null!;
        private System.Media.SoundPlayer? activeAlarmPlayer;

        private TabControl mobileNavBar = null!;
        private TabPage pageDashboard = null!;
        private TabPage pageMomentum = null!;
        private TabPage pageCalendar = null!;
        private TabPage pageSettings = null!;

        private Label lblDashGreeting = null!;
        private Label lblDashCount = null!;
        private Label lblFocusTitle = null!;
        private Panel cardDashFocus = null!;
        private ListBox lstDashFocus = null!;

        private TextBox txtTaskInput = null!;
        private ComboBox cmbTaskType = null!;
        private ComboBox cmbTaskDay = null!;
        private ComboBox cmbHour = null!;
        private ComboBox cmbMinute = null!;
        private ComboBox cmbAmPm = null!;
        private ListBox lstMomentumMaster = null!;
        private Button btnAddTask = null!;

        private FlowLayoutPanel layoutCalendarStrip = null!;
        private Label lblCalDayHeader = null!;
        private Label lblCalProgress = null!;
        private ListBox lstCalendarAgenda = null!;

        private FlowLayoutPanel layoutThemeGrid = null!;
        private Button btnClearData = null!;

        public Form1()
        {
            InitializeComponent();
            InitializeThemes();
            BuildMobileInterface();
            LoadSampleMockData();
            InitializeAlarmEngine();
            RefreshAllScreens();
        }

        private void InitializeThemes()
        {
            appThemes = new List<ThemePreset>
            {
                new() { Name = "Midnight Black", BgColor = Color.FromArgb(10, 10, 10), AccentColor = Color.White, CardBgColor = Color.FromArgb(20, 24, 33), IsLight = false },
                new() { Name = "Apple Dark", BgColor = Color.Black, AccentColor = Color.FromArgb(10, 132, 255), CardBgColor = Color.FromArgb(28, 28, 30), IsLight = false },
                new() { Name = "GitHub Dark", BgColor = Color.FromArgb(13, 17, 23), AccentColor = Color.FromArgb(88, 166, 255), CardBgColor = Color.FromArgb(22, 27, 34), IsLight = false },
                new() { Name = "Slate Professional", BgColor = Color.FromArgb(15, 23, 42), AccentColor = Color.FromArgb(56, 189, 248), CardBgColor = Color.FromArgb(30, 41, 59), IsLight = false },
                new() { Name = "Spotify Dark", BgColor = Color.FromArgb(18, 18, 18), AccentColor = Color.FromArgb(29, 185, 84), CardBgColor = Color.FromArgb(40, 40, 40), IsLight = false }
            };

            activeTheme = appThemes[3];
        }

        private void BuildMobileInterface()
        {
            Size = new Size(430, 750);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;

            mobileNavBar = new TabControl
            {
                Location = new Point(-4, -2),
                Size = new Size(440, 725),
                Font = new Font("Segoe UI Semibold", 10F),
                Alignment = TabAlignment.Top
            };
            mobileNavBar.SelectedIndexChanged += (s, e) => RefreshAllScreens();

            pageDashboard = new TabPage { Text = "Dashboard" };
            pageMomentum = new TabPage { Text = "Momentum" };
            pageCalendar = new TabPage { Text = "Calendar" };
            pageSettings = new TabPage { Text = "Settings" };

            mobileNavBar.TabPages.AddRange(new[] { pageDashboard, pageMomentum, pageCalendar, pageSettings });
            Controls.Add(mobileNavBar);

            AssembleDashboardScreen();
            AssembleMomentumScreen();
            AssembleCalendarScreen();
            AssembleSettingsScreen();
        }

        private void AssembleDashboardScreen()
        {
            lblDashGreeting = CreateLabel("Good Evening", new Point(20, 30), new Size(350, 45), 24F, FontStyle.Bold);
            lblDashCount = CreateLabel("You have 0 active tasks.", new Point(24, 75), new Size(350, 25), 11F, FontStyle.Bold, "Segoe UI Semibold");

            cardDashFocus = CreateRoundedPanel(new Point(20, 130), new Size(375, 220), 16);
            lblFocusTitle = CreateLabel("Today's Focus", new Point(18, 16), new Size(200, 25), 12F, FontStyle.Bold);
            lstDashFocus = CreateTaskListBox(new Point(18, 50), new Size(340, 150), 32);

            cardDashFocus.Controls.AddRange(new Control[] { lblFocusTitle, lstDashFocus });
            pageDashboard.Controls.AddRange(new Control[] { lblDashGreeting, lblDashCount, cardDashFocus });
        }

        private void AssembleMomentumScreen()
        {
            Label title = CreateLabel("Track Momentum", new Point(15, 20), new Size(300, 40), 20F, FontStyle.Italic | FontStyle.Bold);
            Panel formCard = CreateRoundedPanel(new Point(15, 70), new Size(385, 235), 12);

            txtTaskInput = new TextBox { Location = new Point(15, 15), Size = new Size(355, 30), Font = new Font("Segoe UI", 11F), Text = PlaceholderTaskName };
            txtTaskInput.GotFocus += (s, e) => { if (txtTaskInput.Text == PlaceholderTaskName) txtTaskInput.Text = string.Empty; };

            cmbTaskType = CreateComboBox(new Point(15, 65), new Size(170, 28), new[] { TaskTypeNotification, TaskTypeAlarm }, 0);
            cmbTaskDay = CreateComboBox(new Point(200, 65), new Size(170, 28), WeekDays, 1);
            cmbHour = CreateComboBox(new Point(15, 115), new Size(110, 28), Enumerable.Range(1, 12).Select(h => h.ToString("D2")).ToArray(), 7);
            cmbMinute = CreateComboBox(new Point(135, 115), new Size(110, 28), Enumerable.Range(0, 60).Select(m => m.ToString("D2")).ToArray(), 30);
            cmbAmPm = CreateComboBox(new Point(255, 115), new Size(115, 28), new[] { "AM", "PM" }, 1);

            btnAddTask = new Button { Text = "Activate Tracking Target", Location = new Point(15, 168), Size = new Size(355, 45), Font = new Font("Segoe UI", 11F, FontStyle.Bold), FlatStyle = FlatStyle.Flat };
            btnAddTask.Click += BtnAddTask_Click;

            formCard.Controls.AddRange(new Control[] { txtTaskInput, cmbTaskType, cmbTaskDay, cmbHour, cmbMinute, cmbAmPm, btnAddTask });

            Label listTitle = CreateLabel("Active Setup Routines", new Point(15, 325), new Size(300, 25), 12F, FontStyle.Bold);
            lstMomentumMaster = CreateTaskListBox(new Point(15, 355), new Size(385, 280), 35);
            lstMomentumMaster.DoubleClick += ToggleTaskState_DoubleClick;

            pageMomentum.Controls.AddRange(new Control[] { title, formCard, listTitle, lstMomentumMaster });
        }

        private void AssembleCalendarScreen()
        {
            Label title = CreateLabel("Calendar View", new Point(15, 19), new Size(300, 40), 20F, FontStyle.Italic | FontStyle.Bold);

            layoutCalendarStrip = new FlowLayoutPanel { Location = new Point(15, 70), Size = new Size(385, 65), FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            GenerateCalendarCells();

            lblCalDayHeader = CreateLabel("Monday", new Point(15, 150), new Size(180, 35), 16F, FontStyle.Bold);
            lblCalProgress = CreateLabel("0% Done", new Point(200, 155), new Size(200, 25), 11F, FontStyle.Bold, "Segoe UI Semibold");
            lblCalProgress.TextAlign = ContentAlignment.TopRight;

            lstCalendarAgenda = CreateTaskListBox(new Point(15, 195), new Size(385, 430), 35);
            lstCalendarAgenda.DoubleClick += ToggleTaskState_DoubleClick;

            pageCalendar.Controls.AddRange(new Control[] { title, layoutCalendarStrip, lblCalDayHeader, lblCalProgress, lstCalendarAgenda });
        }

        private void GenerateCalendarCells()
        {
            layoutCalendarStrip.Controls.Clear();

            for (int i = 0; i < WeekDays.Length; i++)
            {
                Button dayButton = new()
                {
                    Text = ShortWeekDays[i],
                    Tag = WeekDays[i],
                    Size = new Size(48, 55),
                    Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                    FlatStyle = FlatStyle.Flat,
                    Margin = new Padding(3)
                };

                dayButton.Click += (s, e) =>
                {
                    if (s is Button { Tag: string selectedDay })
                    {
                        activeCalendarDay = selectedDay;
                        RefreshAllScreens();
                    }
                };

                layoutCalendarStrip.Controls.Add(dayButton);
            }
        }

        private void AssembleSettingsScreen()
        {
            Label title = CreateLabel("Settings", new Point(15, 20), new Size(200, 35), 20F, FontStyle.Italic | FontStyle.Bold);
            Label themesTitle = CreateLabel("1. Active Theme Palettes", new Point(15, 75), new Size(300, 20), 11F, FontStyle.Bold);
            layoutThemeGrid = new FlowLayoutPanel { Location = new Point(15, 105), Size = new Size(385, 250), FlowDirection = FlowDirection.TopDown };

            foreach (ThemePreset theme in appThemes)
            {
                Button themeButton = new()
                {
                    Text = $"   {theme.Name}",
                    Tag = theme,
                    Size = new Size(375, 44),
                    Font = new Font("Segoe UI Semibold", 10F),
                    TextAlign = ContentAlignment.MiddleLeft,
                    FlatStyle = FlatStyle.Flat
                };
                themeButton.Click += (s, e) =>
                {
                    if (s is Button { Tag: ThemePreset preset })
                    {
                        activeTheme = preset;
                        ApplyDynamicThemeStyles();
                    }
                };
                layoutThemeGrid.Controls.Add(themeButton);
            }

            Label operationsTitle = CreateLabel("2. Operations Registry", new Point(15, 380), new Size(300, 20), 11F, FontStyle.Bold);
            btnClearData = new Button { Text = "Purge Cache Storage Structures", Location = new Point(15, 410), Size = new Size(375, 48), Font = new Font("Segoe UI Semibold", 10F), ForeColor = Color.LightCoral, FlatStyle = FlatStyle.Flat };
            btnClearData.Click += (s, e) =>
            {
                masterTaskList.Clear();
                RefreshAllScreens();
                MessageBox.Show("Database collections cleared successfully.");
            };

            Label buildLabel = CreateLabel("Engine Context: v1.4.2-build (WinForms Native)", new Point(15, 630), new Size(350, 20), 8.5F, FontStyle.Regular, "Consolas");

            pageSettings.Controls.AddRange(new Control[] { title, themesTitle, layoutThemeGrid, operationsTitle, btnClearData, buildLabel });
        }

        private Label CreateLabel(string text, Point location, Size size, float fontSize, FontStyle style, string family = "Segoe UI")
        {
            return new Label
            {
                Text = text,
                Font = new Font(family, fontSize, style),
                Location = location,
                Size = size
            };
        }

        private ComboBox CreateComboBox(Point location, Size size, string[] items, int selectedIndex)
        {
            ComboBox comboBox = new()
            {
                Location = location,
                Size = size,
                DropDownStyle = ComboBoxStyle.DropDownList
            };

            comboBox.Items.AddRange(items);
            comboBox.SelectedIndex = selectedIndex;
            return comboBox;
        }

        private Panel CreateRoundedPanel(Point location, Size size, int radius)
        {
            Panel panel = new() { Location = location, Size = size };
            panel.Paint += (s, e) => ApplySmoothRoundedCorners(panel, radius);
            return panel;
        }

        private ListBox CreateTaskListBox(Point location, Size size, int itemHeight)
        {
            ListBox listBox = new()
            {
                Location = location,
                Size = size,
                Font = new Font("Segoe UI", 11F),
                BorderStyle = BorderStyle.None,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = itemHeight
            };

            listBox.DrawItem += DrawCustomMobileListCards;
            return listBox;
        }

        private void DrawCustomMobileListCards(object? sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || sender is not ListBox currentBox)
            {
                return;
            }

            string itemData = currentBox.Items[e.Index]?.ToString() ?? string.Empty;

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using SolidBrush backgroundBrush = new(activeTheme.BgColor);
            using SolidBrush cardBrush = new(activeTheme.CardBgColor);
            using SolidBrush indicatorBrush = new(itemData.StartsWith("[Done]") ? Color.FromArgb(46, 204, 113) : activeTheme.AccentColor);

            e.Graphics.FillRectangle(backgroundBrush, e.Bounds);

            Rectangle cardRect = new(e.Bounds.Left + 4, e.Bounds.Top + 3, e.Bounds.Width - 8, e.Bounds.Height - 6);
            e.Graphics.FillRectangle(cardBrush, cardRect);
            e.Graphics.FillRectangle(indicatorBrush, new Rectangle(cardRect.Left, cardRect.Top, 5, cardRect.Height));

            TextRenderer.DrawText(e.Graphics, itemData, currentBox.Font ?? Font, new Point(cardRect.Left + 15, cardRect.Top + 6), Color.White);
        }

        private void ApplySmoothRoundedCorners(Control target, int radius)
        {
            target.Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, target.Width, target.Height, radius, radius));
        }

        private void ApplyDynamicThemeStyles()
        {
            Color primaryBg = activeTheme.BgColor;
            Color accent = activeTheme.AccentColor;
            Color textColor = Color.White;

            BackColor = primaryBg;
            mobileNavBar.BackColor = primaryBg;

            foreach (TabPage page in mobileNavBar.TabPages)
            {
                page.BackColor = primaryBg;
                page.ForeColor = textColor;

                foreach (Control item in page.Controls)
                {
                    if (item is Label label)
                    {
                        label.ForeColor = label == lblCalDayHeader || label == lblDashGreeting ? accent : textColor;
                    }

                    if (item is ListBox listBox)
                    {
                        listBox.BackColor = primaryBg;
                        listBox.Font = new Font("Segoe UI Semibold", 10F);
                    }
                }
            }

            txtTaskInput.BackColor = activeTheme.CardBgColor;
            txtTaskInput.ForeColor = textColor;
            btnAddTask.BackColor = accent;
            btnAddTask.ForeColor = primaryBg;
            btnAddTask.FlatAppearance.BorderSize = 0;

            foreach (Control control in layoutCalendarStrip.Controls)
            {
                if (control is Button button && button.Tag != null)
                {
                    bool activeSelection = button.Tag.ToString() == activeCalendarDay;
                    button.BackColor = activeSelection ? accent : activeTheme.CardBgColor;
                    button.ForeColor = activeSelection ? primaryBg : textColor;
                    button.FlatAppearance.BorderColor = accent;
                    button.FlatAppearance.BorderSize = activeSelection ? 1 : 0;
                }
            }

            foreach (Control control in layoutThemeGrid.Controls)
            {
                if (control is Button button && button.Tag is ThemePreset theme)
                {
                    button.BackColor = theme.BgColor;
                    button.ForeColor = textColor;
                    button.FlatAppearance.BorderColor = theme.Name == activeTheme.Name ? accent : Color.DarkGray;
                    button.FlatAppearance.BorderSize = theme.Name == activeTheme.Name ? 2 : 1;
                }
            }

            cardDashFocus.BackColor = activeTheme.CardBgColor;
            lstDashFocus.BackColor = activeTheme.CardBgColor;
            lblFocusTitle.ForeColor = accent;
            lblCalProgress.ForeColor = accent;
            btnClearData.FlatAppearance.BorderColor = Color.Red;

            ApplyComboBoxTheme(cmbHour);
            ApplyComboBoxTheme(cmbMinute);
            ApplyComboBoxTheme(cmbAmPm);
        }

        private void ApplyComboBoxTheme(ComboBox comboBox)
        {
            comboBox.BackColor = activeTheme.CardBgColor;
            comboBox.ForeColor = Color.White;
        }

        private void InitializeAlarmEngine()
        {
            backgroundAlarmTicker = new System.Windows.Forms.Timer
            {
                Interval = 1000
            };
            backgroundAlarmTicker.Tick += BackgroundAlarmTicker_Tick;
            backgroundAlarmTicker.Start();
        }

        private void BackgroundAlarmTicker_Tick(object? sender, EventArgs e)
        {
            string currentSystemTime = DateTime.Now.ToString("hh:mm tt");
            string todayString = DateTime.Now.DayOfWeek.ToString();

            var matchingAlerts = masterTaskList
                .Where(task => task.Day == todayString
                    && task.RemindTime.Equals(currentSystemTime, StringComparison.OrdinalIgnoreCase)
                    && !task.Completed)
                .ToList();

            foreach (TaskItem alert in matchingAlerts)
            {
                alert.Completed = true;
                RefreshAllScreens();

                if (alert.Type == TaskTypeAlarm)
                {
                    try
                    {
                        PlayAlarmSound();
                        MessageBox.Show($"CRITICAL ALARM:\n\nTime to execute your routine:\n\"{alert.Title}\"!", "VibeAlarm System Trigger", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                    }
                    finally
                    {
                        StopAlarmSound();
                    }
                }
                else
                {
                    System.Media.SystemSounds.Asterisk.Play();
                    MessageBox.Show($"REMINDER:\n\n\"{alert.Title}\" is scheduled for this time slot.", "Workspace Notification", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        private void PlayAlarmSound()
        {
            string alarmPath = Path.Combine(AppContext.BaseDirectory, "Assets", "alarm.wav");

            if (!File.Exists(alarmPath))
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }

            StopAlarmSound();
            activeAlarmPlayer = new System.Media.SoundPlayer(alarmPath);
            activeAlarmPlayer.Load();
            activeAlarmPlayer.PlayLooping();
        }

        private void StopAlarmSound()
        {
            activeAlarmPlayer?.Stop();
            activeAlarmPlayer?.Dispose();
            activeAlarmPlayer = null;
        }

        private void BtnAddTask_Click(object? sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtTaskInput.Text) || txtTaskInput.Text == PlaceholderTaskName)
            {
                return;
            }

            string selectedType = cmbTaskType.SelectedItem?.ToString() ?? TaskTypeNotification;
            string selectedDay = cmbTaskDay.SelectedItem?.ToString() ?? "Monday";
            string hourToken = cmbHour.SelectedItem?.ToString() ?? "08";
            string minuteToken = cmbMinute.SelectedItem?.ToString() ?? "30";
            string amPmToken = cmbAmPm.SelectedItem?.ToString() ?? "PM";

            masterTaskList.Add(new TaskItem
            {
                Id = Guid.NewGuid().ToString(),
                Title = txtTaskInput.Text.Trim(),
                Type = selectedType,
                Day = selectedDay,
                RemindTime = $"{hourToken}:{minuteToken} {amPmToken}",
                Completed = false
            });

            txtTaskInput.Text = PlaceholderTaskName;
            RefreshAllScreens();
        }

        private void ToggleTaskState_DoubleClick(object? sender, EventArgs e)
        {
            if (sender is not ListBox targetBox || targetBox.SelectedIndex == -1)
            {
                return;
            }

            string lineItem = targetBox.SelectedItem?.ToString() ?? string.Empty;
            TaskItem? targetTask = masterTaskList.FirstOrDefault(task => lineItem.Contains(task.Title));

            if (targetTask != null)
            {
                targetTask.Completed = !targetTask.Completed;
                RefreshAllScreens();
            }
        }

        private void RefreshAllScreens()
        {
            string systemToday = DateTime.Now.DayOfWeek.ToString();
            var remainingToday = masterTaskList.Where(task => task.Day == systemToday && !task.Completed).ToList();

            lblDashCount.Text = $"You have {masterTaskList.Count(task => !task.Completed)} outstanding active targets.";

            lstDashFocus.Items.Clear();
            foreach (TaskItem task in remainingToday)
            {
                lstDashFocus.Items.Add($"[Open] {task.Title} - {task.RemindTime}");
            }
            if (!remainingToday.Any())
            {
                lstDashFocus.Items.Add("All clear. No pending items today.");
            }

            lstMomentumMaster.Items.Clear();
            foreach (TaskItem task in masterTaskList)
            {
                string stateIcon = task.Completed ? "[Done]" : "[Open]";
                string shortenedDay = task.Day.Length >= 3 ? task.Day[..3].ToUpper() : task.Day.ToUpper();
                lstMomentumMaster.Items.Add($"{stateIcon}  [{shortenedDay}] {task.Title} ({task.RemindTime})");
            }

            lblCalDayHeader.Text = activeCalendarDay;
            var explicitDayTasks = masterTaskList.Where(task => task.Day == activeCalendarDay).ToList();
            int totalCount = explicitDayTasks.Count;
            int finishedCount = explicitDayTasks.Count(task => task.Completed);
            int percentageRatio = totalCount > 0 ? finishedCount * 100 / totalCount : 0;

            lblCalProgress.Text = $"{finishedCount}/{totalCount} Completed ({percentageRatio}%)";

            lstCalendarAgenda.Items.Clear();
            foreach (TaskItem task in explicitDayTasks)
            {
                string stateIcon = task.Completed ? "[Done]" : "[Open]";
                lstCalendarAgenda.Items.Add($"{stateIcon}  {task.Title} - {task.RemindTime} [{task.Type}]");
            }
            if (!explicitDayTasks.Any())
            {
                lstCalendarAgenda.Items.Add("No agenda items for this day.");
            }

            ApplyDynamicThemeStyles();
        }

        private void LoadSampleMockData()
        {
            masterTaskList.AddRange(new[]
            {
                new TaskItem { Id = "1", Title = "Drink water 3L", Day = "Monday", RemindTime = "07:00 AM", Type = TaskTypeNotification, Completed = true },
                new TaskItem { Id = "2", Title = "Exercise 30 mins", Day = "Monday", RemindTime = "08:00 AM", Type = TaskTypeNotification, Completed = false },
                new TaskItem { Id = "3", Title = "Read a technical journal", Day = "Monday", RemindTime = "09:00 PM", Type = TaskTypeAlarm, Completed = false },
                new TaskItem { Id = "4", Title = "Database backups replication", Day = "Wednesday", RemindTime = "12:00 PM", Type = TaskTypeAlarm, Completed = false }
            });
        }

        private void Form1_Load(object sender, EventArgs e)
        {
        }
    }
}
