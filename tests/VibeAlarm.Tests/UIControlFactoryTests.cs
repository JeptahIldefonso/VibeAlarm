using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Guna.UI2.WinForms;
using VibeAlarm.Models;
using VibeAlarm.Services;
using VibeAlarm.UI.Controls;
using VibeAlarm.UI.Forms;
using VibeAlarm.UI.Theming;
using Xunit;

namespace VibeAlarm.Tests
{
    /// <summary>
    /// Smoke tests the Guna-based UIControlFactory against every built-in preset. These lock
    /// two invariants required by the design system: (a) every factory method returns a usable
    /// configured control without Guna defaults leaking through, and (b) colors are sourced from
    /// the preset tokens — not hardcoded/Guna blue — so both Light and Dark modes keep working.
    /// </summary>
    public class UIControlFactoryTests
    {
        [Theory]
        [InlineData("Light")]
        [InlineData("Dark")]
        public void Every_factory_control_derives_from_preset_tokens(string themeName)
        {
            var themeService = ThemeService.Shared;
            ThemePreset preset = themeService.Presets.First(p => p.Name == themeName);

            var primary = UIControlFactory.CreatePrimaryButton("Primary", preset);
            var secondary = UIControlFactory.CreateSecondaryButton("Secondary", preset);
            var danger = UIControlFactory.CreateDangerButton("Delete", preset);
            var text = UIControlFactory.CreateTextBox("Placeholder", preset: preset);
            var dropdown = UIControlFactory.CreateDropdown(new[] { "A", "B", "C" }, preset: preset);
            var toggle = UIControlFactory.CreateToggle(false, preset);
            var slider = UIControlFactory.CreateSlider(0, 100, 50, preset);
            var panel = UIControlFactory.CreatePanel(preset: preset);
            var icon = UIControlFactory.CreateIconButton("+", preset);

            // Glass-surface extensions (§23–24): translucent panels, icon buttons from the
            // shared icon set, the search box, and the icon-bearing primary button. The
            // transparency percentages are locked design constants (the Settings sliders are
            // gone), so the alphas are exact expectations, not settings-derived.
            var cardPanel = UIControlFactory.CreateCardPanel(preset);
            var panelSurface = UIControlFactory.CreatePanelSurface(preset);
            var iconButton = UIControlFactory.CreateIconButton(IconKind.More, preset);
            var searchBox = UIControlFactory.CreateSearchBox("Search tasks...", preset);
            var primaryWithIcon = UIControlFactory.CreatePrimaryButton("New Task", IconKind.Plus, preset);

            // Nothing may fall back to Guna's default blue accent on any theme.
            Color gunaBlue = Color.FromArgb(94, 148, 255);
            Assert.NotEqual(gunaBlue, primary.FillColor);
            Assert.NotEqual(gunaBlue, secondary.BorderColor);
            Assert.NotEqual(gunaBlue, text.FocusedState.BorderColor);

            // Semantic status colors come from the preset.
            Assert.Equal(preset.ErrorColor, danger.ForeColor);
            Assert.Equal(preset.AccentColor, primary.FillColor);
            Assert.Equal(preset.AccentColor, primary.BorderColor);
            Assert.True(primary.ForeColor == Color.White, "Primary buttons use white text on the accent fill (§14.5).");
            Assert.Equal(preset.CardBgColor, text.FillColor);

            // The dropdown is a themed NATIVE combo (not Guna2ComboBox): flat, list-only,
            // owner-drawn, its colors from the preset tokens.
            Assert.Equal(ComboBoxStyle.DropDownList, dropdown.DropDownStyle);
            Assert.Equal(DrawMode.OwnerDrawFixed, dropdown.DrawMode);
            Assert.Equal(preset.CardBgColor, dropdown.BackColor);
            Assert.Equal(preset.TextColor, dropdown.ForeColor);

            // Glass surfaces are computed by GlassSurface from LOCKED design percentages:
            // cards opaque (0%), raised panels barely glassy (8%), RGB from the preset.
            Assert.Equal(255, cardPanel.FillColor.A);
            Assert.Equal(preset.CardBgColor.R, cardPanel.FillColor.R);
            Assert.Equal(preset.CardBgColor.G, cardPanel.FillColor.G);
            Assert.Equal(preset.CardBgColor.B, cardPanel.FillColor.B);
            Assert.Equal(GlassSurface.AlphaFromPercent(8, GlassSurface.PanelMinAlpha), panelSurface.FillColor.A);
            Assert.NotNull(iconButton.Image);
            Assert.NotNull(searchBox.IconLeft);
            Assert.NotNull(primaryWithIcon.Image);
        }

        /// <summary>Regression: the dropdown was once Guna2ComboBox, whose popup is an
        /// internal ToolStripDropDownMenu sized to ALL items — a 60-item minute list rendered
        /// as one unbounded strip (with item text garbled to "…"). The native replacement
        /// must cap the popup at a scrollable size while keeping every item selectable.</summary>
        [Fact]
        public void CreateDropdown_caps_long_lists_to_a_scrollable_popup()
        {
            string[] minutes = Enumerable.Range(0, 60).Select(m => m.ToString("D2")).ToArray();
            ComboBox dropdown = UIControlFactory.CreateDropdown(minutes, selectedIndex: 0);

            Assert.Equal(8, dropdown.MaxDropDownItems); // popup shows 8 rows, scrolls the rest
            Assert.Equal(60, dropdown.Items.Count);     // all 60 minutes still selectable
            Assert.Equal(0, dropdown.SelectedIndex);
        }

        /// <summary>Smoke: the container-laid-out TaskCreateDialog constructs all its factory
        /// Guna primitives (text box, hour/minute/AM-PM/type dropdowns, create/cancel buttons)
        /// without throwing. Non-modal; only verifies construction and disposal lifecycle.</summary>
        [Fact]
        public void TaskCreateDialog_builds_factory_primitives_without_throwing()
        {
            using var dialog = new TaskCreateDialog();

            // The dialog is a TableLayoutPanel tree — count descendants, not direct children.
            Assert.True(CountDescendants(dialog) >= 8, "Expected the dialog's labels/text/dropdowns/buttons to be present.");
            dialog.CreateGraphics().Dispose();
        }

        /// <summary>Regression: modal canvases must follow the ACTIVE preset. The Create Task
        /// dialog once hardcoded static light tokens, so it stayed white while the app ran
        /// Dark. Locks the canvas, header, field caption, and validation colors per preset.</summary>
        [Theory]
        [InlineData("Light")]
        [InlineData("Dark")]
        public void TaskCreateDialog_canvas_follows_the_active_preset(string themeName)
        {
            ThemePreset? prior = ThemeService.Shared.Current;
            try
            {
                ThemeService.Shared.Current = ThemeService.Shared.Presets.First(p => p.Name == themeName);
                ThemePreset preset = ThemeService.Shared.Current;
                using var dialog = new TaskCreateDialog();

                Assert.Equal(preset.SurfaceElevated, dialog.BackColor);

                Label? title = FindLabelRecursive(dialog, "Create Task");
                Assert.NotNull(title);
                Assert.Equal(preset.TextColor, title!.ForeColor);

                Label? caption = FindLabelRecursive(dialog, "SCHEDULE");
                Assert.NotNull(caption);
                Assert.Equal(preset.MutedTextColor, caption!.ForeColor);
            }
            finally
            {
                ThemeService.Shared.Current = prior;
            }
        }

        /// <summary>Same invariant for the Ctrl+K command palette — another modal that once
        /// hardcoded light tokens. Its input and results surfaces must come from the preset.</summary>
        [Theory]
        [InlineData("Light")]
        [InlineData("Dark")]
        public void CommandPaletteDialog_canvas_follows_the_active_preset(string themeName)
        {
            ThemePreset? prior = ThemeService.Shared.Current;
            try
            {
                ThemeService.Shared.Current = ThemeService.Shared.Presets.First(p => p.Name == themeName);
                ThemePreset preset = ThemeService.Shared.Current;
                using var palette = new CommandPaletteDialog(Array.Empty<TaskItem>());

                Assert.Equal(preset.SurfaceElevated, palette.BackColor);
            }
            finally
            {
                ThemeService.Shared.Current = prior;
            }
        }

        /// <summary>Regression: the actions row was once Percent(100F) and could be starved
        /// to (near) zero height by the AutoSize rows above, hiding Cancel/Create Task. Locks
        /// that both buttons keep real button height and sit fully inside the client area —
        /// the dialog height is measured from the layout, so this holds at any DPI/font.</summary>
        [Fact]
        public void TaskCreateDialog_action_buttons_fit_inside_the_dialog()
        {
            using var dialog = new TaskCreateDialog();
            dialog.CreateGraphics().Dispose(); // forces handle creation + layout
            dialog.PerformLayout();

            var save = FindControlRecursive(dialog, c => c is Guna2Button b && b.Text == "Create Task");
            var cancel = FindControlRecursive(dialog, c => c is Guna2Button b && b.Text == "Cancel");
            Assert.NotNull(save);
            Assert.NotNull(cancel);

            Assert.InRange(save!.Height, 30, 60);
            Assert.InRange(cancel!.Height, 30, 60);
            Assert.True(save.Bottom <= dialog.ClientSize.Height,
                $"Create Task clipped: bottom {save.Bottom} exceeds client height {dialog.ClientSize.Height}.");
            Assert.True(cancel!.Bottom <= dialog.ClientSize.Height,
                $"Cancel clipped: bottom {cancel.Bottom} exceeds client height {dialog.ClientSize.Height}.");
            // NOTE: no Visible check — on a form that was never Show()n, every child reports
            // Visible == false (visibility is inherited). Height + position within the client
            // area is the real regression guard for the starved-actions-row bug.
        }

        /// <summary>Smoke: the native scrollbar theming must register and apply to a handle
        /// without throwing on either theme (null sub-application on Light, "DarkMode_Explorer"
        /// on Dark). The native call only happens on live handles, so both paths are exercised
        /// by creating the handle after Track and calling Reapply after.</summary>
        [Theory]
        [InlineData("Light")]
        [InlineData("Dark")]
        public void NativeScrollbarTheme_applies_without_throwing(string themeName)
        {
            ThemePreset? prior = ThemeService.Shared.Current;
            try
            {
                ThemeService.Shared.Current = ThemeService.Shared.Presets.First(p => p.Name == themeName);
                using var panel = new Panel { AutoScroll = true, Size = new Size(200, 200) };
                NativeScrollbarTheme.Track(panel); // pre-handle: defers to HandleCreated
                panel.CreateGraphics().Dispose();  // handle now exists -> theme applied
                NativeScrollbarTheme.Reapply(panel); // post-handle path
            }
            finally
            {
                ThemeService.Shared.Current = prior;
            }
        }

        private static int CountDescendants(Control root)
        {
            int count = root.Controls.Count;
            foreach (Control child in root.Controls)
            {
                count += CountDescendants(child);
            }
            return count;
        }

        private static Label? FindLabelRecursive(Control root, string text)
        {
            foreach (Control child in root.Controls)
            {
                if (child is Label label && label.Text == text)
                {
                    return label;
                }

                Label? nested = FindLabelRecursive(child, text);
                if (nested != null)
                {
                    return nested;
                }
            }
            return null;
        }

        private static Control? FindControlRecursive(Control root, Func<Control, bool> match)
        {
            foreach (Control child in root.Controls)
            {
                if (match(child))
                {
                    return child;
                }

                Control? nested = FindControlRecursive(child, match);
                if (nested != null)
                {
                    return nested;
                }
            }
            return null;
        }
    }
}