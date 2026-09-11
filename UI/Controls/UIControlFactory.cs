using System;
using System.Drawing;
using System.Windows.Forms;
using Guna.UI2.WinForms;
using VibeAlarm.Models;
using VibeAlarm.Services;
using VibeAlarm.UI.Theming;

namespace VibeAlarm.UI.Controls
{
    /// <summary>
    /// The ONLY place forms go to create Guna control primitives. Guna supplies rendering;
    /// VibeAlarm's design system (an active <see cref="ThemePreset"/> + <see cref="DesignTokens"/>)
    /// supplies every visual decision. No form configures a Guna control's colors/spacing/radius
    /// directly — it calls this factory so all 5 presets stay consistent by construction.
    ///
    /// Sequence per factory method: create the Guna control → pull colors from the active preset
    /// → pull spacing/radius/typography from DesignTokens → apply consistent hover/pressed/disabled
    /// states → return. Business logic never lives here.
    /// </summary>
    public static partial class UIControlFactory
    {
        private static ThemePreset Active() => ThemeService.Shared.Current ?? ThemeService.Shared.Default;

        /// <summary>Primary action (e.g. "+ New Task", "Create Task"). Filled accent, white text,
        /// 8px radius (§14.5) — the one strong colored element on screen.</summary>
        public static Guna2Button CreatePrimaryButton(
            string text,
            ThemePreset? preset = null,
            bool enabled = true)
        {
            var p = preset ?? Active();
            Guna2Button btn = new Guna2Button
            {
                Text = text,
                Font = DesignTokens.Typography.Body(DesignTokens.Typography.FieldSize, FontStyle.Bold),
                FillColor = p.AccentColor,
                ForeColor = Color.White,
                BorderThickness = 0,
                BorderColor = p.AccentColor,
                BorderRadius = DesignTokens.Radius.Small,
                Animated = false,
                Cursor = Cursors.Hand,
                Enabled = enabled
            };
            btn.HoverState.FillColor = HoverAccent(p);
            btn.HoverState.ForeColor = Color.White;
            btn.PressedColor = Shade(p.AccentColor, 0.8f);
            ApplyDisabled(btn.DisabledState, p);
            return btn;
        }

        /// <summary>Secondary action (e.g. "Cancel", "Today", "Filter"). Notion ghost button:
        /// transparent at rest, no border, card-hover fill on hover (§14.5).</summary>
        public static Guna2Button CreateSecondaryButton(
            string text,
            ThemePreset? preset = null,
            bool enabled = true)
        {
            var p = preset ?? Active();
            Guna2Button btn = new Guna2Button
            {
                Text = text,
                Font = DesignTokens.Typography.Body(DesignTokens.Typography.FieldSize),
                FillColor = Color.Transparent,
                ForeColor = p.TextColor,
                BorderThickness = 0,
                BorderColor = Color.Transparent,
                BorderRadius = DesignTokens.Radius.Small,
                Animated = false,
                Cursor = Cursors.Hand,
                Enabled = enabled
            };
            btn.HoverState.FillColor = p.CardHoverBg;
            btn.HoverState.ForeColor = p.TextColor;
            btn.PressedColor = p.KeyStatePress(p.CardHoverBg);
            ApplyDisabled(btn.DisabledState, p);
            return btn;
        }

        /// <summary>Destructive action (e.g. "Delete all tasks"). Only this variant uses the preset's ErrorColor.</summary>
        public static Guna2Button CreateDangerButton(
            string text,
            ThemePreset? preset = null,
            bool enabled = true)
        {
            var p = preset ?? Active();
            Guna2Button btn = CreateSecondaryButton(text, p, enabled);
            btn.ForeColor = p.ErrorColor;
            btn.BorderColor = Mix(p.ErrorColor, p.BorderColor);
            btn.HoverState.FillColor = SoftTint(p.ErrorColor, p, 0.12f);
            btn.HoverState.BorderColor = p.ErrorColor;
            btn.HoverState.ForeColor = p.ErrorColor;
            btn.PressedColor = SoftTint(p.ErrorColor, p, 0.25f);
            return btn;
        }

        /// <summary>Text field. Normal: surface fill + subtle border. Focus: stronger monochrome border (never Guna's blue).</summary>
        public static Guna2TextBox CreateTextBox(
            string placeholder = "",
            string text = "",
            ThemePreset? preset = null,
            bool multiline = false)
        {
            var p = preset ?? Active();
            Guna2TextBox box = new Guna2TextBox
            {
                Text = text,
                PlaceholderText = placeholder,
                ForeColor = p.TextColor,
                FillColor = p.CardBgColor,
                PlaceholderForeColor = p.MutedTextColor,
                BorderColor = p.BorderColor,
                BorderThickness = 1,
                BorderRadius = DesignTokens.Radius.Small,
                Font = DesignTokens.Typography.Body(DesignTokens.Typography.FieldSize),
                Multiline = multiline,
                Cursor = Cursors.IBeam
            };
            box.FocusedState.BorderColor = p.TextColor;
            box.HoverState.BorderColor = Mix(p.TextColor, p.BorderColor);
            box.DisabledState.FillColor = p.KeyStateDisabled();
            box.DisabledState.ForeColor = p.IsLight ? Shade(p.MutedTextColor, 0.6f) : p.MutedTextColor;
            box.DisabledState.BorderColor = p.BorderColor;
            return box;
        }

        /// <summary>Dropdown. NATIVE ComboBox, not Guna2ComboBox: Guna's dropdown popup is an
        /// internal ToolStripDropDownMenu sized to ALL items — v2.0.4.8 exposes no
        /// MaxDropDownItems/DropDownHeight/height cap of any name (assembly-metadata
        /// verified), so a 60-item minute list rendered as an unbounded strip and its custom
        /// item drawing truncated short strings to "…". The native combo caps the popup at
        /// <paramref name="maxVisibleItems"/> rows with real internal scrolling and renders
        /// item text itself; owner-draw keeps every surface on the preset's tokens.</summary>
        public static ComboBox CreateDropdown(
            string[] items,
            int selectedIndex = -1,
            ThemePreset? preset = null,
            bool enabled = true,
            int maxVisibleItems = 8)
        {
            var p = preset ?? Active();
            ComboBox combo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                BackColor = p.CardBgColor,
                ForeColor = p.TextColor,
                Font = DesignTokens.Typography.Body(DesignTokens.Typography.FieldSize),
                MaxDropDownItems = maxVisibleItems,
                DrawMode = DrawMode.OwnerDrawFixed,
                Cursor = Cursors.Hand,
                Enabled = enabled
            };

            // Owner-draw paints BOTH the collapsed field and every list row from the preset:
            // selected row uses the Selected/SelectedText tokens, the rest SurfaceElevated
            // with primary ink. No focus rectangle (the flat border already shows focus).
            combo.DrawItem += (s, e) =>
            {
                if (e.Index < 0)
                {
                    return;
                }

                bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
                using SolidBrush back = new SolidBrush(selected ? p.SelectedColor : p.SurfaceElevated);
                e.Graphics.FillRectangle(back, e.Bounds);
                string text = combo.Items[e.Index]?.ToString() ?? string.Empty;
                TextRenderer.DrawText(
                    e.Graphics,
                    text,
                    e.Font,
                    e.Bounds,
                    selected ? p.SelectedTextColor : p.TextColor,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.LeftAndRightPadding);
            };

            foreach (string it in items)
            {
                combo.Items.Add(it);
            }
            if (selectedIndex >= 0 && selectedIndex < items.Length)
            {
                combo.SelectedIndex = selectedIndex;
            }

            // Dropdown scrollbar/arrow chrome follows the theme (dark mode on Win10 1809+).
            NativeScrollbarTheme.TrackComboBox(combo);
            return combo;
        }

        /// <summary>On/off toggle. No default bright green: off = muted thumb on surface, on = light/selected monochrome thumb.</summary>
        public static Guna2ToggleSwitch CreateToggle(
            bool isChecked = false,
            ThemePreset? preset = null)
        {
            var p = preset ?? Active();
            Guna2ToggleSwitch toggle = new Guna2ToggleSwitch
            {
                Checked = isChecked,
                UseTransparentBackground = true
            };
            toggle.UncheckedState.FillColor = p.CardHoverBg;
            toggle.UncheckedState.InnerColor = p.MutedTextColor;
            toggle.UncheckedState.BorderThickness = 1;
            toggle.UncheckedState.BorderColor = p.BorderColor;
            toggle.CheckedState.FillColor = p.SelectedColor;
            toggle.CheckedState.InnerColor = p.SelectedTextColor;
            toggle.CheckedState.BorderThickness = 1;
            toggle.CheckedState.BorderColor = p.SelectedColor;
            return toggle;
        }

        /// <summary>Continuous-value slider (volume). Track = muted, thumb/active = monochrome text color.</summary>
        public static Guna2TrackBar CreateSlider(
            int minimum = 0,
            int maximum = 100,
            int value = 70,
            ThemePreset? preset = null)
        {
            var p = preset ?? Active();
            Guna2TrackBar slider = new Guna2TrackBar
            {
                Minimum = minimum,
                Maximum = maximum,
                Value = value,
                FillColor = p.BorderColor,
                ThumbColor = p.TextColor,
                Cursor = Cursors.Hand
            };
            slider.HoverState.FillColor = p.MutedTextColor;
            slider.HoverState.ThumbColor = p.TextColor;
            return slider;
        }

        /// <summary>Rounded surface container. Shadow off; used only where rendering earns a Guna panel over a plain Panel.</summary>
        public static Guna2Panel CreatePanel(
            Color? fill = null,
            bool border = false,
            int radius = DesignTokens.Radius.Small,
            ThemePreset? preset = null)
        {
            var p = preset ?? Active();
            Guna2Panel panel = new Guna2Panel
            {
                FillColor = fill ?? p.CardBgColor,
                BorderRadius = radius
            };
            panel.ShadowDecoration.Enabled = false; // no guna default drop shadows
            if (border)
            {
                panel.BorderColor = p.BorderColor;
                panel.BorderThickness = 1;
            }
            return panel;
        }

        /// <summary>Small icon/text action (sidebar nav, hover-reveal row icon). Borderless, non-focus-enclosing.</summary>
        public static Guna2Button CreateIconButton(
            string glyph,
            ThemePreset? preset = null)
        {
            var p = preset ?? Active();
            Guna2Button btn = new Guna2Button
            {
                Text = glyph,
                Font = DesignTokens.Typography.Mono(DesignTokens.Typography.NumericSize, FontStyle.Bold),
                FillColor = Color.Transparent,
                ForeColor = p.MutedTextColor,
                BorderThickness = 0,
                BorderRadius = DesignTokens.Radius.Small,
                Animated = false,
                Cursor = Cursors.Hand
            };
            btn.HoverState.FillColor = p.CardHoverBg;
            btn.HoverState.ForeColor = p.TextColor;
            btn.PressedColor = p.KeyStatePress(p.CardHoverBg);
            return btn;
        }

        /// <summary>Applies theme-consistent disabled visual state to buttons.</summary>
        private static void ApplyDisabled(Guna.UI2.WinForms.Suite.ButtonState disabled, ThemePreset p)
        {
            disabled.FillColor = p.KeyStateDisabled();
            disabled.ForeColor = p.IsLight ? Shade(p.MutedTextColor, 0.6f) : p.MutedTextColor;
            disabled.BorderColor = p.BorderColor;
        }

        // ---- monochrome blend/lift/shift helpers ----

        private static Color DarkInk(ThemePreset p) => p.IsLight ? p.SelectedTextColor : p.SelectedTextColor;

        private static Color Mix(Color a, Color b) =>
            Color.FromArgb((a.R + b.R) / 2, (a.G + b.G) / 2, (a.B + b.B) / 2);

        private static Color SoftTint(Color source, ThemePreset p, float amount)
        {
            Color baseColor = p.CardBgColor;
            int r = (int)(source.R * amount + baseColor.R * (1 - amount));
            int g = (int)(source.G * amount + baseColor.G * (1 - amount));
            int b = (int)(source.B * amount + baseColor.B * (1 - amount));
            return Color.FromArgb(Clamp255(r), Clamp255(g), Clamp255(b));
        }

        private static Color Shade(Color color, float factor)
        {
            int r = Clamp255((int)(color.R * factor));
            int g = Clamp255((int)(color.G * factor));
            int b = Clamp255((int)(color.B * factor));
            return Color.FromArgb(r, g, b);
        }

        private static int Clamp255(int v) => Math.Max(0, Math.Min(255, v));

        /// <summary>Accent hover: one tonal step off the accent fill — lighter in dark, darker in light.</summary>
        private static Color HoverAccent(ThemePreset p) =>
            p.IsLight
                ? Color.FromArgb(
                    Clamp255(p.AccentColor.R + 24),
                    Clamp255(p.AccentColor.G + 24),
                    Clamp255(p.AccentColor.B + 24))
                : Color.FromArgb(
                    Clamp255(p.AccentColor.R + 40),
                    Clamp255(p.AccentColor.G + 40),
                    Clamp255(p.AccentColor.B + 40));

        private static Color KeyStateLift(this ThemePreset p, Color baseColor, bool lift)
        {
            if (p.IsLight)
            {
                // light theme: hover = slightly darker than the base fill
                return Shade(baseColor, 0.9f);
            }
            // dark theme: "lift" toward white = lighter
            return Color.FromArgb(
                Clamp255(baseColor.R + 30),
                Clamp255(baseColor.G + 30),
                Clamp255(baseColor.B + 30));
        }

        private static Color KeyStatePress(this ThemePreset p, Color baseColor)
        {
            if (p.IsLight)
            {
                return Shade(baseColor, 0.76f);
            }
            return Color.FromArgb(
                Clamp255(baseColor.R - 24),
                Clamp255(baseColor.G - 24),
                Clamp255(baseColor.B - 24));
        }

        private static Color KeyStateDisabled(this ThemePreset p)
            => p.IsLight ? Color.FromArgb(226, 225, 221) : Color.FromArgb(46, 46, 44);
    }
}