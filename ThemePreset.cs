using System.Drawing;

namespace VibeAlarm;

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
}