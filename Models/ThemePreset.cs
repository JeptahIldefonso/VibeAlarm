using System.Drawing;

namespace VibeAlarm.Models;

public sealed class ThemePreset
{
    public string Name { get; set; } = string.Empty;
    public Color PrimaryBg { get; set; }
    public Color SidebarBg { get; set; }
    public Color SecondaryBg { get; set; }
    public Color CardBgColor { get; set; }
    public Color CardHoverBg { get; set; }
    public Color AccentColor { get; set; }
    public Color TextColor { get; set; }
    public Color MutedTextColor { get; set; }
    public Color BorderColor { get; set; }
    public bool IsLight { get; set; }

    // Elevated/state surfaces (all derived per-preset, stay strictly monochrome).
    public Color SurfaceElevated { get; set; }
    public Color PressedColor { get; set; }
    public Color SelectedColor { get; set; }
    public Color SelectedTextColor { get; set; }

    // Semantic status colors — the ONLY sanctioned non-grayscale accents, reserved for
    // genuine destructive/validation/affirmative actions, never decorative.
    public Color SuccessColor { get; set; }
    public Color WarningColor { get; set; }
    public Color ErrorColor { get; set; }
}