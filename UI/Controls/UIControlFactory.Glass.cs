using System;
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
    /// Glass-surface and icon extensions to the control factory. Every translucent ARGB surface
    /// color is computed here via <see cref="GlassSurface"/> (the single home of transparency
    /// math) so no form ever mixes its own alpha. Corner radii and density are locked design
    /// constants in <see cref="Appearance"/>.
    /// </summary>
    public static partial class UIControlFactory
    {
        /// <summary>Content card surface (task rows, headers, info blocks): glass card fill +
        /// glass hairline border, square corners per the locked card radius.</summary>
        public static Guna2Panel CreateCardPanel(ThemePreset? preset = null)
        {
            var p = preset ?? Active();
            Guna2Panel panel = new Guna2Panel
            {
                FillColor = GlassSurface.CardFill(p),
                BorderColor = GlassSurface.CardBorder(p),
                BorderThickness = 1,
                BorderRadius = Appearance.CardRadius
            };
            panel.ShadowDecoration.Enabled = false;
            return panel;
        }

        /// <summary>Raised panel surface (status bar, toolbars, hero areas): slightly more
        /// present than cards, still translucent per the locked panel transparency.</summary>
        public static Guna2Panel CreatePanelSurface(ThemePreset? preset = null)
        {
            var p = preset ?? Active();
            Guna2Panel panel = new Guna2Panel
            {
                FillColor = GlassSurface.PanelFill(p),
                BorderColor = GlassSurface.PanelBorder(p),
                BorderThickness = 1,
                BorderRadius = Appearance.CardRadius
            };
            panel.ShadowDecoration.Enabled = false;
            return panel;
        }

        /// <summary>Search field: rounded Guna2TextBox with a leading magnifier icon, subtle
        /// border, and a clear focus state. Placeholder is native — no manual focus hacks.</summary>
        public static Guna2TextBox CreateSearchBox(
            string placeholder = "Search tasks...",
            ThemePreset? preset = null)
        {
            var p = preset ?? Active();
            Guna2TextBox box = CreateTextBox(placeholder, preset: p);
            box.IconLeft = IconSet.Render(IconKind.Search, p.MutedTextColor, 16);
            box.IconLeftSize = new Size(20, 20);
            box.Margin = new Padding(0);
            return box;
        }

        /// <summary>Borderless square icon button rendered from the shared icon set (§7): the
        /// glyph ink is baked into the image, quiet fill on hover.</summary>
        public static Guna2Button CreateIconButton(
            IconKind kind,
            ThemePreset? preset = null,
            int iconSize = 18,
            int buttonSize = 36)
        {
            var p = preset ?? Active();
            Guna2Button btn = new Guna2Button
            {
                Text = string.Empty,
                Size = new Size(buttonSize, buttonSize),
                FillColor = Color.Transparent,
                ForeColor = p.MutedTextColor,
                BorderThickness = 0,
                BorderRadius = Math.Max(4, Appearance.ControlRadius),
                Animated = Appearance.Animations,
                Cursor = Cursors.Hand,
                Image = IconSet.Render(kind, p.MutedTextColor, iconSize),
                ImageAlign = HorizontalAlignment.Center,
                ImageSize = new Size(iconSize, iconSize),
                AccessibleName = kind.ToString()
            };
            btn.HoverState.FillColor = p.CardHoverBg;
            btn.HoverState.ForeColor = p.TextColor;
            btn.PressedColor = p.KeyStatePress(p.CardHoverBg);
            return btn;
        }

        /// <summary>Primary action with a leading icon (e.g. "+ New Task"): filled accent,
        /// white ink, icon + text together as the one strong element on screen.</summary>
        public static Guna2Button CreatePrimaryButton(
            string text,
            IconKind icon,
            ThemePreset? preset = null,
            bool enabled = true)
        {
            Guna2Button btn = CreatePrimaryButton(text, preset, enabled);
            var p = preset ?? Active();
            btn.Image = IconSet.Render(icon, Color.White, 16);
            btn.ImageSize = new Size(16, 16);
            btn.ImageAlign = HorizontalAlignment.Left;
            btn.ImageOffset = new Point(12, 0);
            btn.TextOffset = new Point(8, 0);
            btn.Padding = new Padding(12, 0, 16, 0);
            return btn;
        }
    }
}
