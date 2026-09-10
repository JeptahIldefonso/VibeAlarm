using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Guna.UI2.WinForms;
using VibeAlarm.Models;
using VibeAlarm.Services;
using VibeAlarm.UI.Controls;
using VibeAlarm.UI.Theming;

namespace VibeAlarm.UI.Forms
{
    /// <summary>
    /// Modal dialog that collects the details for a new task and produces a generated TaskItem.
    /// A schedule represents a FUTURE event: the complete scheduled date+time must be in the
    /// future, otherwise the dialog stays open and shows a clear validation message.
    ///
    /// Visual primitives (text field, dropdowns, buttons) come from <see cref="UIControlFactory"/>:
    /// Guna renders them, the active <see cref="ThemePreset"/> decides every color/radius/font.
    /// Business/validation logic lives here, never inside those primitives.
    /// </summary>
    public sealed class TaskCreateDialog : Form
    {
        private const string PlaceholderTaskName = "What needs to be done?";

        private readonly IClock clock;

        public TaskItem GeneratedTask { get; private set; } = null!;

        private Guna2TextBox txtInput = null!;
        private DateTimePicker dtpScheduleDate = null!;
        private Guna2ComboBox cmbType = null!;
        private Guna2ComboBox cmbHr = null!;
        private Guna2ComboBox cmbMin = null!;
        private Guna2ComboBox cmbAmPm = null!;
        private Guna2Button btnSave = null!;
        private Guna2Button btnCancel = null!;
        private Label lblValidation = null!;

        public TaskCreateDialog(IClock? clock = null)
        {
            this.clock = clock ?? new Clock();
            InitializeDialogCanvas();
        }

        private void InitializeDialogCanvas()
        {
            Text = "Create Task";
            ClientSize = new Size(560, 400);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = VibeAlarmPalette.Background;
            Font = VibeAlarmPalette.Body(10F);

            // §34: layout containers, not coordinates — header, full-width name field, a
            // schedule flow (date | hour | minute | AM/PM), type, right-aligned actions.
            TableLayoutPanel root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 8,
                BackColor = Color.Transparent,
                Padding = new Padding(28, 20, 28, 20)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // header
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // name label
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // name field
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // schedule label
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // schedule flow
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // type label
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // type row
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F)); // actions

            // ---- Header ----
            TableLayoutPanel header = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 0, 0, DesignTokens.Spacing.Lg)
            };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            header.Controls.Add(new Label
            {
                Text = "Create Task",
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                Font = VibeAlarmPalette.Display(16F, FontStyle.Bold),
                ForeColor = VibeAlarmPalette.TextPrimary,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            }, 0, 0);
            header.Controls.Add(new Label
            {
                Text = "Add a reminder, alarm, or important task.",
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                Font = VibeAlarmPalette.Body(9F),
                ForeColor = VibeAlarmPalette.TextSecondary,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 3, 0, 0)
            }, 0, 1);
            root.Controls.Add(header, 0, 0);

            // ---- Task name ----
            root.Controls.Add(CreateFieldLabel("TASK NAME"), 0, 1);
            txtInput = UIControlFactory.CreateTextBox(placeholder: PlaceholderTaskName);
            txtInput.Dock = DockStyle.Fill;
            txtInput.Margin = new Padding(0, 4, 0, DesignTokens.Spacing.Lg);
            root.Controls.Add(txtInput, 0, 2);

            // ---- Schedule: date | hour | minute | AM/PM ----
            root.Controls.Add(CreateFieldLabel("SCHEDULE"), 0, 3);

            dtpScheduleDate = new DateTimePicker
            {
                Format = DateTimePickerFormat.Custom,
                CustomFormat = "MMM dd, yyyy",
                Value = DateTime.Today,
                // Past dates are not schedulable — a schedule is a FUTURE event.
                MinDate = DateTime.Today,
                MaxDate = DateTime.Today.AddYears(5),
                CalendarForeColor = VibeAlarmPalette.TextPrimary,
                CalendarMonthBackground = VibeAlarmPalette.Surface,
                CalendarTitleBackColor = VibeAlarmPalette.TextPrimary,
                CalendarTitleForeColor = VibeAlarmPalette.Surface,
                Font = VibeAlarmPalette.Body(9.5F),
                Width = 154,
                Margin = new Padding(0, 0, DesignTokens.Spacing.Md, 0)
            };

            cmbHr = CreateDropdown(Enumerable.Range(1, 12).Select(h => h.ToString("D2")).ToArray(), 7);
            cmbHr.Width = 70;
            cmbHr.Margin = new Padding(0, 0, 6, 0);
            cmbMin = CreateDropdown(Enumerable.Range(0, 60).Select(m => m.ToString("D2")).ToArray(), 0);
            cmbMin.Width = 78;
            cmbMin.Margin = new Padding(0, 0, 6, 0);
            cmbAmPm = CreateDropdown(new[] { "AM", "PM" }, 0);
            cmbAmPm.Width = 78;
            cmbAmPm.Margin = new Padding(0);

            FlowLayoutPanel scheduleFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 4, 0, DesignTokens.Spacing.Lg)
            };
            scheduleFlow.Controls.Add(dtpScheduleDate);
            scheduleFlow.Controls.Add(cmbHr);
            scheduleFlow.Controls.Add(cmbMin);
            scheduleFlow.Controls.Add(cmbAmPm);
            root.Controls.Add(scheduleFlow, 0, 4);

            // ---- Type + inline validation message ----
            root.Controls.Add(CreateFieldLabel("TYPE"), 0, 5);

            cmbType = CreateDropdown(new[] { "Notification", "Alarm", "Important" }, 0);
            cmbType.Width = 210;
            cmbType.Margin = new Padding(0, 4, DesignTokens.Spacing.Md, 0);

            // Inline, monochrome validation feedback (no animation, no colorful material UI).
            lblValidation = new Label
            {
                Text = string.Empty,
                AutoSize = true,
                MaximumSize = new Size(260, 0),
                Font = VibeAlarmPalette.Mono(8F),
                ForeColor = VibeAlarmPalette.Error,
                BackColor = Color.Transparent,
                Visible = false,
                Margin = new Padding(0, 12, 0, 0)
            };

            FlowLayoutPanel typeRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 0, 0, 0)
            };
            typeRow.Controls.Add(cmbType);
            typeRow.Controls.Add(lblValidation);
            root.Controls.Add(typeRow, 0, 6);

            // ---- Actions: right-aligned (Cancel ghost, Create Task primary) ----
            TableLayoutPanel actionsRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            actionsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            actionsRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            FlowLayoutPanel actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 4, 0, 0)
            };

            btnCancel = UIControlFactory.CreateSecondaryButton("Cancel");
            btnCancel.Size = new Size(110, 38);
            btnCancel.Margin = new Padding(0, 4, DesignTokens.Spacing.Sm, 0);
            btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

            btnSave = UIControlFactory.CreatePrimaryButton("Create Task");
            btnSave.Size = new Size(140, 38);
            btnSave.Margin = new Padding(0, 4, 0, 0);
            btnSave.Click += OnSaveSubmitted;

            actions.Controls.Add(btnCancel);
            actions.Controls.Add(btnSave);
            actionsRow.Controls.Add(new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent }, 0, 0);
            actionsRow.Controls.Add(actions, 1, 0);
            root.Controls.Add(actionsRow, 0, 7);

            // Keyboard accessibility: Escape cancels, Enter submits (unless focus is on Cancel).
            KeyPreview = true;
            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); e.Handled = true; }
                else if (e.KeyCode == Keys.Enter && btnCancel.Focused)
                {
                    btnCancel.PerformClick();
                    e.Handled = true;
                }
            };
            AcceptButton = btnSave;
            CancelButton = btnCancel;

            Controls.Add(root);
        }

        /// <summary>Small uppercase field caption (task name / schedule / type).</summary>
        private static Label CreateFieldLabel(string text) => new()
        {
            Text = text,
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            Font = VibeAlarmPalette.Mono(8.5F, FontStyle.Bold),
            ForeColor = VibeAlarmPalette.TextPrimary,
            BackColor = Color.Transparent,
            Margin = new Padding(0)
        };

        private Guna2ComboBox CreateDropdown(string[] items, int idx)
            => UIControlFactory.CreateDropdown(items, selectedIndex: idx);

        private void OnSaveSubmitted(object? sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtInput.Text))
            {
                txtInput.Focus();
                return;
            }

            DateTime? selected = BuildScheduledDateTime();
            if (selected == null || selected.Value <= clock.Now)
            {
                // A schedule is a FUTURE event. Compare the FULL date+time (never only the
                // date or only the hour). Do not silently change the user's selection — keep
                // the dialog open and show a clear, professional message.
                ShowValidation("Schedule time must be in the future.");
                return;
            }

            HideValidation();

            GeneratedTask = new TaskItem
            {
                Id = Guid.NewGuid().ToString(),
                Title = txtInput.Text.Trim(),
                ScheduledDate = dtpScheduleDate.Value.Date.ToString("yyyy-MM-dd"),
                Day = dtpScheduleDate.Value.DayOfWeek.ToString(),
                Type = cmbType.SelectedItem?.ToString() ?? "Notification",
                RemindTime = $"{cmbHr.SelectedItem ?? "08"}:{cmbMin.SelectedItem ?? "00"} {cmbAmPm.SelectedItem ?? "PM"}",
                Completed = false
            };

            DialogResult = DialogResult.OK;
            Close();
        }

        /// <summary>Builds the complete selected DateTime from the date picker + hour/minute/AM-PM
        /// combos, or null when any part is missing/unparseable.</summary>
        private DateTime? BuildScheduledDateTime()
        {
            if (!int.TryParse(cmbHr.SelectedItem?.ToString(), out int hour) ||
                !int.TryParse(cmbMin.SelectedItem?.ToString(), out int minute))
            {
                return null;
            }

            string? amPm = cmbAmPm.SelectedItem?.ToString();
            if (hour < 1 || hour > 12 || minute < 0 || minute > 59 || (amPm != "AM" && amPm != "PM"))
            {
                return null;
            }

            int hour24 = (hour % 12) + (amPm == "PM" ? 12 : 0);
            DateTime date = dtpScheduleDate.Value.Date;
            return new DateTime(date.Year, date.Month, date.Day, hour24, minute, 0);
        }

        private void ShowValidation(string message)
        {
            lblValidation.Text = message;
            lblValidation.Visible = true;
        }

        private void HideValidation()
        {
            lblValidation.Text = string.Empty;
            lblValidation.Visible = false;
        }
    }
}
