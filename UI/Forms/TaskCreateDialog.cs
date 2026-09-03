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
            ClientSize = new Size(560, 360);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = VibeAlarmPalette.Background;
            Font = VibeAlarmPalette.Body(10F);

            Label lblHead = new Label { Text = "Create Task", Location = new Point(28, 24), Size = new Size(220, 28), Font = VibeAlarmPalette.Display(16F, FontStyle.Bold), ForeColor = VibeAlarmPalette.TextPrimary, BackColor = Color.Transparent };
            Label lblSub = new Label { Text = "Add a reminder, alarm, or important task.", Location = new Point(28, 54), Size = new Size(360, 20), Font = VibeAlarmPalette.Body(9F), ForeColor = VibeAlarmPalette.TextSecondary, BackColor = Color.Transparent };

            Label lblTask = new Label { Text = "Task Name", Location = new Point(28, 94), Size = new Size(160, 18), Font = VibeAlarmPalette.Mono(8.5F, FontStyle.Bold), ForeColor = VibeAlarmPalette.TextPrimary, BackColor = Color.Transparent };
            txtInput = UIControlFactory.CreateTextBox(placeholder: PlaceholderTaskName);
            txtInput.Location = new Point(28, 118);
            txtInput.Size = new Size(504, 42);

            Label lblSchedule = new Label { Text = "Schedule", Location = new Point(28, 174), Size = new Size(160, 18), Font = VibeAlarmPalette.Mono(8.5F, FontStyle.Bold), ForeColor = VibeAlarmPalette.TextPrimary, BackColor = Color.Transparent };
            Label lblDate = new Label { Text = "Date", Location = new Point(28, 198), Size = new Size(80, 18), Font = VibeAlarmPalette.Mono(8F), ForeColor = VibeAlarmPalette.TextSecondary, BackColor = Color.Transparent };
            dtpScheduleDate = new DateTimePicker
            {
                Location = new Point(28, 220),
                Size = new Size(154, 32),
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
                Font = VibeAlarmPalette.Body(9.5F)
            };

            Label lblHour = new Label { Text = "Hour", Location = new Point(206, 198), Size = new Size(60, 18), Font = VibeAlarmPalette.Mono(8F), ForeColor = VibeAlarmPalette.TextSecondary, BackColor = Color.Transparent };
            cmbHr = CreateDropdown(Enumerable.Range(1, 12).Select(h => h.ToString("D2")).ToArray(), 7);
            cmbHr.Location = new Point(206, 220);
            cmbHr.Size = new Size(70, 40);
            Label lblMinute = new Label { Text = "Minute", Location = new Point(292, 198), Size = new Size(70, 18), Font = VibeAlarmPalette.Mono(8F), ForeColor = VibeAlarmPalette.TextSecondary, BackColor = Color.Transparent };
            cmbMin = CreateDropdown(Enumerable.Range(0, 60).Select(m => m.ToString("D2")).ToArray(), 0);
            cmbMin.Location = new Point(292, 220);
            cmbMin.Size = new Size(78, 40);
            Label lblAmPm = new Label { Text = "AM/PM", Location = new Point(386, 198), Size = new Size(80, 18), Font = VibeAlarmPalette.Mono(8F), ForeColor = VibeAlarmPalette.TextSecondary, BackColor = Color.Transparent };
            cmbAmPm = CreateDropdown(new[] { "AM", "PM" }, 0);
            cmbAmPm.Location = new Point(386, 220);
            cmbAmPm.Size = new Size(78, 40);

            Label lblType = new Label { Text = "Type", Location = new Point(28, 272), Size = new Size(130, 18), Font = VibeAlarmPalette.Mono(8.5F, FontStyle.Bold), ForeColor = VibeAlarmPalette.TextPrimary, BackColor = Color.Transparent };
            cmbType = CreateDropdown(new[] { "Notification", "Alarm", "Important" }, 0);
            cmbType.Location = new Point(28, 294);
            cmbType.Size = new Size(210, 42);

            // Inline, monochrome validation feedback (no animation, no colorful material UI).
            lblValidation = new Label
            {
                Text = string.Empty,
                Location = new Point(254, 294),
                Size = new Size(200, 42),
                Font = VibeAlarmPalette.Mono(8F),
                ForeColor = VibeAlarmPalette.Error,
                BackColor = Color.Transparent,
                Visible = false
            };

            btnSave = UIControlFactory.CreatePrimaryButton("Create Task");
            btnSave.Location = new Point(392, 294);
            btnSave.Size = new Size(140, 40);
            btnSave.Click += OnSaveSubmitted;

            btnCancel = UIControlFactory.CreateSecondaryButton("Cancel");
            btnCancel.Location = new Point(254, 294);
            btnCancel.Size = new Size(118, 40);
            btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

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

            Controls.AddRange(new Control[] { lblHead, lblSub, lblTask, txtInput, lblSchedule, lblDate, dtpScheduleDate, lblHour, cmbHr, lblMinute, cmbMin, lblAmPm, cmbAmPm, lblType, cmbType, lblValidation, btnSave, btnCancel });
        }

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
