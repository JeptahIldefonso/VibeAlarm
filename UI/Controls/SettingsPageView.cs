using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Guna.UI2.WinForms;
using VibeAlarm.Models;
using VibeAlarm.Services;
using VibeAlarm.UI.Theming;
using Appearance = VibeAlarm.UI.Theming.Appearance;

namespace VibeAlarm.UI.Controls
{
    /// <summary>
    /// The application settings page (§19–24 of the orientation redesign): a vertically
    /// scrolling list of section cards (Appearance, Interface, Notifications, Data, System,
    /// Background, About), each holding structured two-column rows — setting name + short
    /// description on the left (~60%), the control on the right (~40%). No fixed coordinates.
    ///
    /// Transparency sliders expose friendly 0–100% values (0% = opaque) with a live percentage
    /// label; the alpha math lives in <see cref="GlassSurface"/>. While a slider is being
    /// dragged, only <paramref name="onAppearancePreview"/> fires (cheap chrome refresh) so
    /// the control under the pointer is never rebuilt mid-drag; committing happens on release.
    ///
    /// All mutations go through the <paramref name="getTasks"/>/callback delegates so the page
    /// owns no business logic — MainForm keeps the master task list, persistence, and theme
    /// application.
    /// </summary>
    public sealed class SettingsPageView : UserControl
    {
        private const string AppVersion = "1.5.0";

        private readonly Func<IList<TaskItem>> getTasks;
        private readonly Action<string> onThemeModeChanged;
        private readonly Action onAppearanceCommitted;
        private readonly Action onAppearancePreview;
        private readonly Action onDataChanged;

        /// <summary>Working copy of the settings — every control mutates this, every commit
        /// point persists it, so a pending slider drag is never clobbered by another control's
        /// save.</summary>
        private readonly AppSettings editState;

        private ThemePreset Theme => ThemeService.Shared.Current ?? ThemeService.Shared.Default;

        private readonly List<Guna2Panel> sectionCards = new();
        private TableLayoutPanel scrollContent = null!;

        public SettingsPageView(
            Func<IList<TaskItem>> getTasks,
            Action<string> onThemeModeChanged,
            Action onAppearanceCommitted,
            Action onAppearancePreview,
            Action onDataChanged)
        {
            this.getTasks = getTasks;
            this.onThemeModeChanged = onThemeModeChanged;
            this.onAppearanceCommitted = onAppearanceCommitted;
            this.onAppearancePreview = onAppearancePreview;
            this.onDataChanged = onDataChanged;
            editState = SettingsService.Load();

            Build();
        }

        private void Build()
        {
            AutoScroll = true;
            BackColor = Color.Transparent;
            Padding = new Padding(0, DesignTokens.Spacing.Sm, 0, DesignTokens.Spacing.Lg);

            scrollContent = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 1,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            scrollContent.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            Controls.Add(scrollContent);

            AddSection("Appearance", "Choose how VibeAlarm looks.", rows =>
            {
                rows.Add(BuildThemeRow());
                rows.Add(BuildSliderRow("Glass transparency",
                    "How see-through the main glass surfaces appear. 0% is solid.",
                    s => s.GlassTransparency, (s, v) => s.GlassTransparency = v));
                rows.Add(BuildSliderRow("Panel opacity",
                    "Transparency of raised panels like the sidebar and status bar.",
                    s => s.PanelTransparency, (s, v) => s.PanelTransparency = v));
                rows.Add(BuildSliderRow("Card opacity",
                    "Transparency of task cards and content surfaces.",
                    s => s.CardTransparency, (s, v) => s.CardTransparency = v));
                rows.Add(BuildSliderRow("Border opacity",
                    "Transparency of the hairline borders around surfaces.",
                    s => s.BorderTransparency, (s, v) => s.BorderTransparency = v));
            });

            AddSection("Interface", "Layout density, rounding, and motion.", rows =>
            {
                rows.Add(BuildCornerRadiusRow());
                rows.Add(BuildDensityRow());
                rows.Add(BuildToggleRow("Animations",
                    "Smooth hover and press transitions on buttons.",
                    s => s.EnableAnimations, (s, v) => s.EnableAnimations = v));
            });

            AddSection("Notifications", "How alarms and reminders reach you.", rows =>
            {
                rows.Add(BuildToggleRow("Desktop notifications",
                    "Show a notification when a task is due, even when the window is hidden.",
                    s => s.EnableNotifications, (s, v) => s.EnableNotifications = v));
                rows.Add(BuildToggleRow("Alarm sound",
                    "Play the alarm sound when an alarm fires.",
                    s => s.EnableAlarmSound, (s, v) => s.EnableAlarmSound = v));
            });

            AddSection("Data", "Task records and application settings.", rows =>
            {
                rows.Add(BuildToggleRow("Confirm before deleting",
                    "Ask for confirmation before tasks are deleted or cleared.",
                    s => s.ConfirmBeforeDelete, (s, v) => s.ConfirmBeforeDelete = v));
                rows.Add(BuildClearTasksRow());
                rows.Add(BuildResetSettingsRow());
                rows.Add(BuildOpenDataFolderRow());
            });

            AddSection("System", "Startup and window behavior.", rows =>
            {
                rows.Add(BuildStartupRow());
                rows.Add(BuildTrayRow());
            });

            AddSection("Background", "Custom background image behind the interface.", rows =>
            {
                rows.Add(BuildBackgroundImageRow());
                rows.Add(BuildToggleRow("Monochrome filter",
                    "Desaturate the background image so content stays readable.",
                    s => s.ApplyMonochromeFilterToBackground, (s, v) => s.ApplyMonochromeFilterToBackground = v,
                    commit: true));
                rows.Add(BuildSliderRow("Background opacity",
                    "How strongly the background image shows through.",
                    s => (int)Math.Round(s.BackgroundOpacity * 100),
                    (s, v) => s.BackgroundOpacity = v / 100.0,
                    livePreview: false));
            });

            AddSection("About", string.Empty, rows =>
            {
                rows.Add(BuildAboutBlock());
            });
        }

        // ---- Section scaffolding ----

        private delegate void SectionBuilder(List<Control> rows);

        private void AddSection(string title, string description, SectionBuilder buildRows)
        {
            var rows = new List<Control>();
            buildRows(rows);

            var card = new Guna2Panel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                Padding = new Padding(DesignTokens.Spacing.Lg, 18, DesignTokens.Spacing.Lg, 18),
                Margin = new Padding(0, 0, 0, DesignTokens.Spacing.Md),
                FillColor = GlassSurface.SectionFill(Theme, editState),
                BorderColor = GlassSurface.PanelBorder(Theme, editState),
                BorderThickness = 1,
                BorderRadius = Appearance.CardRadius,
                BackColor = Color.Transparent
            };
            sectionCards.Add(card);

            var inner = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 1,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            inner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            var heading = new Label
            {
                Text = title,
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                Font = DesignTokens.Typography.Body(11.5F, FontStyle.Bold),
                ForeColor = Theme.TextColor,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 0, 0, 2),
                Padding = new Padding(0)
            };
            inner.Controls.Add(heading, 0, 0);

            if (!string.IsNullOrEmpty(description))
            {
                var sub = new Label
                {
                    Text = description,
                    Dock = DockStyle.Fill,
                    AutoEllipsis = true,
                    Font = DesignTokens.Typography.Body(9.5F),
                    ForeColor = Theme.MutedTextColor,
                    BackColor = Color.Transparent,
                    Margin = new Padding(0, 0, 0, DesignTokens.Spacing.Md),
                    Padding = new Padding(0)
                };
                inner.Controls.Add(sub, 0, 1);
            }

            foreach (Control row in rows)
            {
                inner.Controls.Add(row, 0, inner.Controls.Count);
            }

            card.Controls.Add(inner);
            scrollContent.Controls.Add(card);
            scrollContent.SetRow(card, scrollContent.Controls.Count - 1);
        }

        /// <summary>Structured settings row: name + description left (~60%), control right
        /// (~40%), vertically centered (§21).</summary>
        private Control CreateSettingsRow(string name, string description, Control? right)
        {
            var row = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                BackColor = Color.Transparent,
                Margin = new Padding(0, Appearance.SettingsRowPadding, 0, Appearance.SettingsRowPadding)
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40F));

            var left = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                ColumnCount = 1,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            left.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            left.Controls.Add(new Label
            {
                Text = name,
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                Font = DesignTokens.Typography.Body(10.5F, FontStyle.Bold),
                ForeColor = Theme.TextColor,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 0, 0, 2),
                Padding = new Padding(0)
            }, 0, 0);
            left.Controls.Add(new Label
            {
                Text = description,
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                Font = DesignTokens.Typography.Body(9F),
                ForeColor = Theme.MutedTextColor,
                BackColor = Color.Transparent,
                Margin = new Padding(0),
                Padding = new Padding(0)
            }, 0, 1);

            row.Controls.Add(left, 0, 0);
            if (right != null)
            {
                // Anchor exactly Right → hugs the column's right edge and centers vertically.
                right.Anchor = AnchorStyles.Right;
                right.Margin = new Padding(DesignTokens.Spacing.Sm, DesignTokens.Spacing.Sm, 0, DesignTokens.Spacing.Sm);
                row.Controls.Add(right, 1, 0);
            }
            return row;
        }

        // ---- Row factories ----

        private Control BuildThemeRow()
        {
            string[] modes = { "Light", "Dark", "System" };
            int index = Array.FindIndex(modes, m => string.Equals(m, editState.Theme, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                index = 0;
            }

            Guna2ComboBox dropdown = UIControlFactory.CreateDropdown(modes, index, preset: Theme);
            dropdown.Width = 140;
            dropdown.SelectedIndexChanged += (s, e) =>
            {
                editState.Theme = dropdown.SelectedIndex >= 0 ? modes[dropdown.SelectedIndex] : "Light";
                SettingsService.Save(editState);
                // The whole page re-renders on theme change — defer past the event handler.
                BeginInvoke(onThemeModeChanged, editState.Theme);
            };
            return CreateSettingsRow("Theme", "Light, dark, or follow your Windows setting.", dropdown);
        }

        /// <summary>Slider row with a live percentage label. Continuous drags update the label
        /// and preview only; the value persists and the app re-renders when the drag ends.</summary>
        private Control BuildSliderRow(
            string name,
            string description,
            Func<AppSettings, int> getValue,
            Action<AppSettings, int> setValue,
            bool livePreview = true,
            int minimum = 0,
            int maximum = 100)
        {
            int value = Math.Clamp(getValue(editState), minimum, maximum);
            setValue(editState, value); // normalize any out-of-range persisted value immediately

            Guna2TrackBar slider = UIControlFactory.CreateSlider(minimum, maximum, value, preset: Theme);
            slider.Width = 140;

            Label lblValue = new()
            {
                Text = $"{value}%",
                Width = 44,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = DesignTokens.Typography.Mono(10F, FontStyle.Bold),
                ForeColor = Theme.TextColor,
                BackColor = Color.Transparent,
                Margin = new Padding(DesignTokens.Spacing.Sm, 0, 0, 0),
                Padding = new Padding(0)
            };

            FlowLayoutPanel holder = new()
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            holder.Controls.Add(slider);
            holder.Controls.Add(lblValue);

            slider.ValueChanged += (s, e) =>
            {
                int v = Math.Clamp(slider.Value, minimum, maximum);
                setValue(editState, v);
                lblValue.Text = $"{v}%";
                if (livePreview)
                {
                    RefreshGlassSurfacesLocal();
                    onAppearancePreview();
                }
            };
            slider.MouseUp += (s, e) =>
            {
                SettingsService.Save(editState);
                // Committing re-renders the active view (including this page) — defer one
                // message-loop turn so the control isn't disposed inside its own handler.
                BeginInvoke(onAppearanceCommitted);
            };

            return CreateSettingsRow(name, description, holder);
        }

        private Control BuildCornerRadiusRow()
        {
            return BuildSliderRow("Corner radius",
                "How rounded cards and controls are.",
                s => s.CornerRadius,
                (s, v) => s.CornerRadius = v,
                minimum: 0, maximum: 20);
        }

        private Control BuildDensityRow()
        {
            string[] densities = { "Comfortable", "Compact" };
            int index = Array.FindIndex(densities, d => string.Equals(d, editState.Density, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                index = 0;
            }

            Guna2ComboBox dropdown = UIControlFactory.CreateDropdown(densities, index, preset: Theme);
            dropdown.Width = 140;
            dropdown.SelectedIndexChanged += (s, e) =>
            {
                editState.Density = dropdown.SelectedIndex >= 0 ? densities[dropdown.SelectedIndex] : "Comfortable";
                SettingsService.Save(editState);
                BeginInvoke(onAppearanceCommitted);
            };
            return CreateSettingsRow("Density", "Comfortable spacing, or a compact layout that fits more.", dropdown);
        }

        /// <summary>Toggle row. Toggles that only change persisted behavior save immediately;
        /// appearance-affecting ones additionally re-render (deferred).</summary>
        private Control BuildToggleRow(
            string name,
            string description,
            Func<AppSettings, bool> getValue,
            Action<AppSettings, bool> setValue,
            bool commit = false)
        {
            Guna2ToggleSwitch toggle = UIControlFactory.CreateToggle(getValue(editState), preset: Theme);
            toggle.CheckedChanged += (s, e) =>
            {
                setValue(editState, toggle.Checked);
                SettingsService.Save(editState);
                if (commit)
                {
                    BeginInvoke(onAppearanceCommitted);
                }
            };
            return CreateSettingsRow(name, description, toggle);
        }

        private Control BuildClearTasksRow()
        {
            Guna2Button btn = UIControlFactory.CreateDangerButton("Clear tasks", preset: Theme);
            SizeButton(btn, 110);
            btn.Click += (s, e) =>
            {
                if (editState.ConfirmBeforeDelete &&
                    MessageBox.Show("Delete all task records permanently?", "Confirm Reset",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Stop) != DialogResult.Yes)
                {
                    return;
                }
                getTasks().Clear();
                onDataChanged();
            };
            return CreateSettingsRow("Clear tasks", "Remove all saved tasks.", btn);
        }

        private Control BuildResetSettingsRow()
        {
            Guna2Button btn = UIControlFactory.CreateDangerButton("Reset settings", preset: Theme);
            SizeButton(btn, 110);
            btn.Click += (s, e) =>
            {
                if (MessageBox.Show("Reset all VibeAlarm settings to their defaults?", "Reset Settings",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                {
                    return;
                }

                string defaultTheme = new AppSettings().Theme;
                ResetEditState(new AppSettings());
                SettingsService.Save(editState);
                BeginInvoke(onThemeModeChanged, defaultTheme);
                BeginInvoke(onAppearanceCommitted);
            };
            return CreateSettingsRow("Reset application settings", "Restore every setting to its default value.", btn);
        }

        private Control BuildOpenDataFolderRow()
        {
            Guna2Button btn = UIControlFactory.CreateSecondaryButton("Open folder", preset: Theme);
            SizeButton(btn, 110);
            btn.Click += (s, e) =>
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
                    MessageBox.Show($"Could not open the data folder:\n{ex.Message}", "Data Folder",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };
            return CreateSettingsRow("Data folder", "Open the folder where tasks and settings are stored.", btn);
        }

        private Control BuildStartupRow()
        {
            Guna2ToggleSwitch toggle = UIControlFactory.CreateToggle(SettingsService.IsRunOnStartupEnabled(), preset: Theme);
            toggle.CheckedChanged += (s, e) =>
            {
                SettingsService.SetRunOnStartup(toggle.Checked);
            };
            return CreateSettingsRow("Launch on Windows startup", "Start VibeAlarm automatically when you sign in.", toggle);
        }

        private Control BuildTrayRow()
        {
            Guna2ToggleSwitch toggle = UIControlFactory.CreateToggle(editState.MinimizeToTray, preset: Theme);
            toggle.CheckedChanged += (s, e) =>
            {
                editState.MinimizeToTray = toggle.Checked;
                SettingsService.Save(editState);
            };
            return CreateSettingsRow("Minimize to tray",
                "Hide to the notification area instead of exiting — alarms keep firing while hidden.", toggle);
        }

        private Control BuildBackgroundImageRow()
        {
            Label lblStatus = new()
            {
                Text = string.IsNullOrEmpty(editState.BackgroundImagePath) ? "No custom background set." : "Custom background set.",
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                Font = DesignTokens.Typography.Body(9F),
                ForeColor = Theme.MutedTextColor,
                BackColor = Color.Transparent,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };

            Guna2Button btnChoose = UIControlFactory.CreatePrimaryButton("Choose image...", preset: Theme);
            SizeButton(btnChoose, 120);
            btnChoose.Click += (s, e) =>
            {
                using OpenFileDialog picker = new()
                {
                    Title = "Choose a background image",
                    Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif",
                    CheckFileExists = true,
                    Multiselect = false
                };
                if (picker.ShowDialog(FindForm()) != DialogResult.OK)
                {
                    return;
                }

                string? stored = BackgroundService.SetBackground(picker.FileName);
                if (stored == null)
                {
                    MessageBox.Show("The selected image could not be used.", "Background",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                editState.BackgroundImagePath = stored;
                SettingsService.Save(editState);
                lblStatus.Text = "Background applied.";
                BeginInvoke(onAppearanceCommitted); // reload the background image layer
            };

            Guna2Button btnRemove = UIControlFactory.CreateSecondaryButton("Remove", preset: Theme);
            SizeButton(btnRemove, 80);
            btnRemove.Click += (s, e) =>
            {
                BackgroundService.RemoveBackground();
                editState.BackgroundImagePath = string.Empty;
                SettingsService.Save(editState);
                lblStatus.Text = "Background removed.";
                BeginInvoke(onAppearanceCommitted);
            };

            FlowLayoutPanel actions = new()
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            actions.Controls.Add(btnChoose);
            actions.Controls.Add(btnRemove);

            // Status line sits directly beneath the action buttons, muted and right-aligned.
            lblStatus.TextAlign = ContentAlignment.MiddleRight;
            TableLayoutPanel right = new()
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            right.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            right.Controls.Add(actions, 0, 0);
            right.Controls.Add(lblStatus, 0, 1);
            return CreateSettingsRow("Custom background", "Show your own image behind the interface.", right);
        }

        private Control BuildAboutBlock()
        {
            var block = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 1,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            block.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            block.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            block.Controls.Add(new Label
            {
                Text = "VibeAlarm",
                Dock = DockStyle.Fill,
                Font = DesignTokens.Typography.Display(13F, FontStyle.Bold),
                ForeColor = Theme.TextColor,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 0, 0, 2),
                Padding = new Padding(0)
            }, 0, 0);
            block.Controls.Add(new Label
            {
                Text = $"Version {AppVersion}",
                Dock = DockStyle.Fill,
                Font = DesignTokens.Typography.Mono(9F),
                ForeColor = Theme.MutedTextColor,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 0, 0, DesignTokens.Spacing.Sm),
                Padding = new Padding(0)
            }, 0, 1);
            block.Controls.Add(new Label
            {
                Text = "A calm desktop companion for reminders, alarms, and focused work.",
                Dock = DockStyle.Fill,
                Font = DesignTokens.Typography.Body(9.5F),
                ForeColor = Theme.MutedTextColor,
                BackColor = Color.Transparent,
                Margin = new Padding(0),
                Padding = new Padding(0),
                MaximumSize = new Size(560, 0)
            }, 0, 2);
            return block;
        }

        // ---- helpers ----

        /// <summary>Live glass refresh for this page's own section cards during slider drags
        /// (the persistent chrome is refreshed by the host via the preview callback).</summary>
        private void RefreshGlassSurfacesLocal()
        {
            foreach (Guna2Panel card in sectionCards)
            {
                card.FillColor = GlassSurface.SectionFill(Theme, editState);
                card.BorderColor = GlassSurface.PanelBorder(Theme, editState);
                card.Invalidate();
            }
        }

        private void ResetEditState(AppSettings defaults)
        {
            // Keep identity-ish fields that a "reset" shouldn't erase (task data lives elsewhere).
            editState.SchemaVersion = defaults.SchemaVersion;
            editState.Theme = defaults.Theme;
            editState.GlassTransparency = defaults.GlassTransparency;
            editState.PanelTransparency = defaults.PanelTransparency;
            editState.CardTransparency = defaults.CardTransparency;
            editState.BorderTransparency = defaults.BorderTransparency;
            editState.CornerRadius = defaults.CornerRadius;
            editState.Density = defaults.Density;
            editState.EnableAnimations = defaults.EnableAnimations;
            editState.EnableNotifications = defaults.EnableNotifications;
            editState.EnableAlarmSound = defaults.EnableAlarmSound;
            editState.ConfirmBeforeDelete = defaults.ConfirmBeforeDelete;
            editState.MinimizeToTray = defaults.MinimizeToTray;
            editState.BackgroundOpacity = defaults.BackgroundOpacity;
            editState.ApplyMonochromeFilterToBackground = defaults.ApplyMonochromeFilterToBackground;
            editState.BackgroundImagePath = defaults.BackgroundImagePath;
        }

        private static void SizeButton(Control btn, int width)
        {
            btn.Width = width;
            btn.Height = 34;
        }
    }
}
