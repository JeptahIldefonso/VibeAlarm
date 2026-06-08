using System.Drawing;

namespace VibeAlarm;

public sealed class ThemePreset
{
    public string Name { get; set; } = string.Empty;
    public Color BgColor { get; set; }
    public Color AccentColor { get; set; }
    public Color CardBgColor { get; set; }
    public bool IsLight { get; set; }
}
