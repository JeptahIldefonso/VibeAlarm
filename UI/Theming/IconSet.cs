using System.Drawing;

namespace VibeAlarm.UI.Theming
{
    /// <summary>
    /// The application's single icon language (§7 of the orientation redesign). Every glyph is
    /// a Segoe Fluent Icons character rendered to a bitmap in the requested ink color; when no
    /// symbol font is installed (pre-Windows 10 systems), the renderer degrades to a neutral
    /// geometric marker so navigation and actions stay legible without mixing emoji, ASCII art,
    /// or random fonts. Icons support text — they never replace it.
    ///
    /// Glyph codes are Segoe Fluent Icons / MDL2 codepoints, chosen to render identically on
    /// both faces where possible.
    /// </summary>
    public enum IconKind
    {
        Dashboard,     // grid/home
        Tasks,         // checklist
        Calendar,      // calendar
        Settings,      // gear
        Plus,          // add
        Search,        // magnifier
        More,          // three horizontal dots
        Trash,         // delete
        Copy,          // duplicate
        Check,         // complete
        Bell,          // notification
        Clock,         // alarm
        Star,          // important
        ChevronLeft,   // previous
        ChevronRight,  // next
        Volume,        // sound
        Play,          // play
        Stop,          // stop
        Image,         // background picture
        Folder,        // data folder
        Refresh,       // reset
        Snooze         // snooze (moon/clock)
    }

    public static class IconSet
    {
        /// <summary>Fluent/MDL2 glyph per kind. Values are hex codepoints.</summary>
        private static readonly Dictionary<IconKind, string> Glyphs = new()
        {
            [IconKind.Dashboard] = "E80F", // Home
            [IconKind.Tasks] = "E9D5",     // Checklist
            [IconKind.Calendar] = "E787",  // Calendar
            [IconKind.Settings] = "E713",  // Settings gear
            [IconKind.Plus] = "E710",      // Add (plus)
            [IconKind.Search] = "E721",    // Search (magnifier)
            [IconKind.More] = "E712",      // More (ellipsis)
            [IconKind.Trash] = "E74D",     // Delete (trash)
            [IconKind.Copy] = "E8C8",      // Copy
            [IconKind.Check] = "E73E",     // CheckMark
            [IconKind.Bell] = "EA8F",      // Ringer (bell)
            [IconKind.Clock] = "E823",     // Recent (clock face)
            [IconKind.Star] = "E734",      // FavoriteStar
            [IconKind.ChevronLeft] = "E76B",
            [IconKind.ChevronRight] = "E76C",
            [IconKind.Volume] = "E767",    // Volume
            [IconKind.Play] = "E768",      // Play
            [IconKind.Stop] = "E71A",      // Stop
            [IconKind.Image] = "E8B9",     // Photo2 (image)
            [IconKind.Folder] = "E8B7",    // Folder
            [IconKind.Refresh] = "E72C",   // Refresh
            [IconKind.Snooze] = "E707",    // QuietHours (moon)
        };

        /// <summary>Neutral fallback shapes when no symbol font is available — never tofu, never emoji.</summary>
        private static readonly Dictionary<IconKind, string> Fallbacks = new()
        {
            [IconKind.Dashboard] = "▤", // square with grid fill
            [IconKind.Tasks] = "☰",     // trigram (lines)
            [IconKind.Calendar] = "▦",  // square with fill grid
            [IconKind.Settings] = "◎",  // bullseye
            [IconKind.Plus] = "+",
            [IconKind.Search] = "○",    // circle
            [IconKind.More] = "…",      // ellipsis
            [IconKind.Trash] = "✕",     // multiplication X
            [IconKind.Copy] = "⧉",      // two joined squares
            [IconKind.Check] = "✓",     // check
            [IconKind.Bell] = "△",      // triangle
            [IconKind.Clock] = "◴",     // circle with quadrant
            [IconKind.Star] = "★",      // star
            [IconKind.ChevronLeft] = "<",
            [IconKind.ChevronRight] = ">",
            [IconKind.Volume] = "◀",    // left triangle
            [IconKind.Play] = "▶",      // right triangle
            [IconKind.Stop] = "■",      // filled square
            [IconKind.Image] = "▫",     // small square
            [IconKind.Folder] = "▭",    // small rectangle
            [IconKind.Refresh] = "↺",   // anticlockwise arrow
            [IconKind.Snooze] = "☾",    // crescent moon
        };

        /// <summary>Renders an icon to a new bitmap of the given pixel size, in the given ink.
        /// The caller owns the bitmap. The drawn color is baked in, so callers re-render when
        /// the theme/ink changes (same contract the old RenderNavGlyph followed).</summary>
        public static Bitmap Render(IconKind kind, Color color, int size = 22)
        {
            Bitmap bmp = new(size, size);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                bool symbol = FontRegistry.HasSymbolFonts;
                string glyph = symbol
                    ? ((char)Convert.ToInt32(Glyphs[kind], 16)).ToString()
                    : Fallbacks[kind];
                using Font f = symbol
                    ? FontRegistry.Symbol(size * 0.62F)
                    : VibeAlarmPalette.Mono(size * 0.62F, FontStyle.Bold);
                using SolidBrush b = new(color);
                using StringFormat sf = new()
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                };
                g.DrawString(glyph, f, b, new RectangleF(0, 0, size, size), sf);
            }
            return bmp;
        }

        /// <summary>The raw glyph character (for owner-drawn items that paint their own text),
        /// already resolved to the symbol/fallback face.</summary>
        public static string Glyph(IconKind kind)
            => FontRegistry.HasSymbolFonts
                ? ((char)Convert.ToInt32(Glyphs[kind], 16)).ToString()
                : Fallbacks[kind];
    }
}
