using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using VibeAlarm.Models;
using VibeAlarm.Services;
using VibeAlarm.UI.Theming;

namespace VibeAlarm.UI.Forms
{
    /// <summary>
    /// Notion-style quick switcher / command palette (Ctrl+K). Type to filter navigation
    /// targets, tasks, and actions; Enter runs the highlighted row; Esc dismisses.
    /// </summary>
    public sealed class CommandPaletteDialog : Form
    {
        public enum PaletteAction
        {
            Navigate,
            NewTask,
            Search
        }

        public sealed class PaletteEntry
        {
            public string Title { get; init; } = string.Empty;
            public string Subtitle { get; init; } = string.Empty;
            public PaletteAction Action { get; init; }
            public object? Payload { get; init; } // view key, or TaskItem, or null
        }

        public PaletteEntry? Result { get; private set; }

        private readonly IReadOnlyList<PaletteEntry> allEntries;
        private TextBox txtInput = null!;
        private ListBox lstResults = null!;

        public CommandPaletteDialog(IReadOnlyList<TaskItem> tasks)
        {
            allEntries = BuildEntries(tasks);
            InitializeCanvas();
        }

        private static IReadOnlyList<PaletteEntry> BuildEntries(IReadOnlyList<TaskItem> tasks)
        {
            var list = new List<PaletteEntry>();
            list.Add(new PaletteEntry { Title = "New Task...", Subtitle = "Ctrl+N", Action = PaletteAction.NewTask });
            list.Add(new PaletteEntry { Title = "Search tasks", Subtitle = "Set focus to search", Action = PaletteAction.Search });

            foreach (string view in new[] { "Dashboard", "Tasks", "Calendar", "Ambient", "Settings" })
            {
                list.Add(new PaletteEntry { Title = $"Go to {view}", Subtitle = view, Action = PaletteAction.Navigate, Payload = view });
            }

            foreach (TaskItem t in tasks.OrderBy(t => t.Title, StringComparer.OrdinalIgnoreCase).Take(40))
            {
                list.Add(new PaletteEntry
                {
                    Title = t.Title,
                    Subtitle = AlarmEngine.GetScheduledTimeLabel(t),
                    Action = PaletteAction.Navigate,
                    Payload = t
                });
            }
            return list;
        }

        private void InitializeCanvas()
        {
            // Same source the control factory reads — the palette must match the running theme.
            ThemePreset preset = ThemeService.Shared.Current ?? ThemeService.Shared.Default;

            Text = "Quick Switch";
            ClientSize = new Size(520, 280);
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = preset.SurfaceElevated;
            Font = VibeAlarmPalette.Body(10F);
            KeyPreview = true;

            Label lblAppLogo = new Label
            {
                Text = "Quick Switch",
                Location = new Point(24, 18),
                Size = new Size(200, 24),
                Font = VibeAlarmPalette.Display(15F, FontStyle.Bold),
                ForeColor = preset.TextColor,
                BackColor = Color.Transparent
            };
            Controls.Add(lblAppLogo);

            txtInput = new TextBox
            {
                Location = new Point(24, 52),
                Size = new Size(472, 32),
                Font = VibeAlarmPalette.Body(12F),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = preset.CardBgColor,
                ForeColor = preset.TextColor,
                Text = ""
            };
            txtInput.TextChanged += (_, _) => RebuildResults();
            Controls.Add(txtInput);

            lstResults = new ListBox
            {
                Location = new Point(24, 94),
                Size = new Size(472, 168),
                Font = VibeAlarmPalette.Body(10F),
                BackColor = preset.CardBgColor,
                ForeColor = preset.TextColor,
                BorderStyle = BorderStyle.None,
                IntegralHeight = false
            };
            lstResults.SelectedIndexChanged += (_, _) => { };
            lstResults.DoubleClick += (_, _) => AcceptSelection();
            Controls.Add(lstResults);
            // The list's native scrollbar follows the active theme like every other scroller.
            NativeScrollbarTheme.Track(lstResults);

            KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Escape)
                {
                    Close();
                }
                else if (e.KeyCode == Keys.Enter)
                {
                    AcceptSelection();
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
                else if (e.KeyCode == Keys.Down)
                {
                    if (lstResults.SelectedIndex < lstResults.Items.Count - 1)
                        lstResults.SelectedIndex++;
                    e.Handled = true;
                }
                else if (e.KeyCode == Keys.Up)
                {
                    if (lstResults.SelectedIndex > 0)
                        lstResults.SelectedIndex--;
                    e.Handled = true;
                }
            };

            RebuildResults();
            Shown += (_, _) => { txtInput.Focus(); };
        }

        private void RebuildResults()
        {
            string q = txtInput.Text.Trim();
            var matches = string.IsNullOrWhiteSpace(q)
                ? allEntries
                : allEntries.Where(e =>
                    e.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    e.Subtitle.Contains(q, StringComparison.OrdinalIgnoreCase));

            lstResults.Items.Clear();
            foreach (var e in matches)
            {
                lstResults.Items.Add(e);
            }
            if (lstResults.Items.Count > 0)
            {
                lstResults.SelectedIndex = 0;
            }
        }

        private void AcceptSelection()
        {
            if (lstResults.SelectedItem is PaletteEntry selected)
            {
                Result = selected;
                DialogResult = DialogResult.OK;
                Close();
            }
        }
    }
}