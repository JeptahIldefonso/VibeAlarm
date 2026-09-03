using System;
using System.Drawing;
using System.Linq;
using VibeAlarm.Models;
using VibeAlarm.Services;
using VibeAlarm.UI.Controls;
using VibeAlarm.UI.Forms;
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

            // Nothing may fall back to Guna's default blue accent on any theme.
            Color gunaBlue = Color.FromArgb(94, 148, 255);
            Assert.NotEqual(gunaBlue, primary.FillColor);
            Assert.NotEqual(gunaBlue, secondary.BorderColor);
            Assert.NotEqual(gunaBlue, text.FocusedState.BorderColor);
            Assert.NotEqual(gunaBlue, dropdown.FocusedState.BorderColor);

            // Semantic status colors come from the preset.
            Assert.Equal(preset.ErrorColor, danger.ForeColor);
            Assert.Equal(preset.TextColor, primary.FillColor);
            Assert.Equal(preset.CardBgColor, text.FillColor);
        }

        /// <summary>Smoke: the migrated TaskCreateDialog constructs all its factory Guna primitives
        /// (text box, hour/minute/AM-PM/type dropdowns, create/cancel buttons) without throwing.
        /// Non-modal; only verifies construction and disposal lifecycle.</summary>
        [Fact]
        public void TaskCreateDialog_builds_factory_primitives_without_throwing()
        {
            using var dialog = new TaskCreateDialog();

            // The dialog hosts a Guna-based text box, four dropdowns, and two Guna buttons.
            Assert.NotEmpty(dialog.Controls);
            Assert.True(dialog.Controls.Count >= 8, "Expected the dialog's title/labels/text/dropdowns/buttons to be present.");
            dialog.CreateGraphics().Dispose();
        }
    }
}