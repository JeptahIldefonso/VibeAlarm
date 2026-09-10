using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
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
            // shared icon set, the search box, and the icon-bearing primary button.
            var appearance = new AppSettings();
            var cardPanel = UIControlFactory.CreateCardPanel(preset, appearance);
            var panelSurface = UIControlFactory.CreatePanelSurface(preset, appearance);
            var iconButton = UIControlFactory.CreateIconButton(IconKind.More, preset);
            var searchBox = UIControlFactory.CreateSearchBox("Search tasks...", preset);
            var primaryWithIcon = UIControlFactory.CreatePrimaryButton("New Task", IconKind.Plus, preset);

            // Nothing may fall back to Guna's default blue accent on any theme.
            Color gunaBlue = Color.FromArgb(94, 148, 255);
            Assert.NotEqual(gunaBlue, primary.FillColor);
            Assert.NotEqual(gunaBlue, secondary.BorderColor);
            Assert.NotEqual(gunaBlue, text.FocusedState.BorderColor);
            Assert.NotEqual(gunaBlue, dropdown.FocusedState.BorderColor);

            // Semantic status colors come from the preset.
            Assert.Equal(preset.ErrorColor, danger.ForeColor);
            Assert.Equal(preset.AccentColor, primary.FillColor);
            Assert.Equal(preset.AccentColor, primary.BorderColor);
            Assert.True(primary.ForeColor == Color.White, "Primary buttons use white text on the accent fill (§14.5).");
            Assert.Equal(preset.CardBgColor, text.FillColor);

            // Glass surfaces are computed by GlassSurface: the alpha reflects the settings'
            // transparency (default 10%) clamped to the readability floor, and the RGB
            // channels come from the preset unchanged.
            Assert.Equal(GlassSurface.AlphaFromPercent(appearance.CardTransparency, GlassSurface.CardMinAlpha), cardPanel.FillColor.A);
            Assert.Equal(preset.CardBgColor.R, cardPanel.FillColor.R);
            Assert.Equal(preset.CardBgColor.G, cardPanel.FillColor.G);
            Assert.Equal(preset.CardBgColor.B, cardPanel.FillColor.B);
            Assert.InRange(panelSurface.FillColor.A, GlassSurface.PanelMinAlpha, 255);
            Assert.NotNull(iconButton.Image);
            Assert.NotNull(searchBox.IconLeft);
            Assert.NotNull(primaryWithIcon.Image);
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

        private static int CountDescendants(Control root)
        {
            int count = root.Controls.Count;
            foreach (Control child in root.Controls)
            {
                count += CountDescendants(child);
            }
            return count;
        }
    }
}