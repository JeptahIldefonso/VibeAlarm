using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using Svg;

namespace VibeAlarm.UI.Theming
{
    /// <summary>
    /// The application's single icon language (§7 of the orientation redesign). An icon is
    /// rendered from an embedded VECTOR when one is registered for the kind (Assets/Icons/*.svg,
    /// rasterized by SVG.NET in the requested ink at the requested size), otherwise from a Segoe
    /// Fluent Icons glyph drawn as text; when no symbol font is installed (pre-Windows 10
    /// systems), the renderer degrades to a neutral geometric marker so navigation and actions
    /// stay legible without mixing emoji, ASCII art, or random fonts. Icons support text — they
    /// never replace it.
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
        Snooze,        // snooze (moon/clock)

        // Vector-backed additions (SVG is the primary rendering; the glyph entries below are
        // their degraded fallbacks and must stay, since every kind needs a glyph+fallback for
        // the no-SVG path).
        TaskComplete,    // clipboard with check — completed-task state (distinct from Check)
        TaskCheckbox,    // rounded checkbox — per-row task list checkbox
        TaskPending,     // clock with orbiting arrows — incomplete/open task state
        SoundWave,       // audio waveform — Ambient navigation (background sound)
        AdminSettings,   // person + gear — advanced/admin settings surface
        UserPreferences  // person with skill badges — Settings navigation
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

            // Degraded fallbacks for the vector-backed kinds (the SVG is the primary rendering;
            // these approximations only appear if the SVG path fails or the vector is absent).
            [IconKind.TaskComplete] = "E73E",     // CheckMark (approximates clipboard-check)
            [IconKind.TaskCheckbox] = "E73A",     // Checkbox
            [IconKind.TaskPending] = "E823",      // Recent/clock face (approximates pending)
            [IconKind.SoundWave] = "E767",        // Volume (approximates waveform)
            [IconKind.AdminSettings] = "E713",    // Settings gear (approximates person+gear)
            [IconKind.UserPreferences] = "E77B",  // Contact (person)
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

            // Degraded fallbacks for the vector-backed kinds — same neutral geometric style.
            [IconKind.TaskComplete] = "☑",    // ballot box with check
            [IconKind.TaskCheckbox] = "☐",    // ballot box
            [IconKind.TaskPending] = "◵",     // circle, lower-left quadrant
            [IconKind.SoundWave] = "∿",       // sine wave
            [IconKind.AdminSettings] = "⚙",   // gear
            [IconKind.UserPreferences] = "◉", // fisheye
        };

        /// <summary>Embedded vector icon per kind (Assets/Icons/*.svg, resource names follow the
        /// FontRegistry convention). MEMBERSHIP IS THE SOURCE FLAG: kinds listed here render as
        /// vectors; kinds not listed use the font-glyph path — no kind is forced to have an SVG.
        /// Public so tests can verify the mapping and inject a bad resource name to exercise
        /// the fallback.</summary>
        public static readonly Dictionary<IconKind, string> SvgResources = new()
        {
            // Richer vector alternatives for existing kinds — every call site that renders
            // these (sidebar nav, buttons) upgrades automatically through Render.
            [IconKind.Dashboard] = "VibeAlarm.Assets.Icons.dashboard-monitor.svg",
            [IconKind.Tasks] = "VibeAlarm.Assets.Icons.task-checklist.svg",
            [IconKind.Calendar] = "VibeAlarm.Assets.Icons.calendar-days.svg",

            // New vector-only kinds (glyph entries above are their degraded fallbacks).
            [IconKind.TaskComplete] = "VibeAlarm.Assets.Icons.clipboard-check.svg",
            [IconKind.TaskCheckbox] = "VibeAlarm.Assets.Icons.checkbox.svg",
            [IconKind.TaskPending] = "VibeAlarm.Assets.Icons.pending.svg",
            [IconKind.SoundWave] = "VibeAlarm.Assets.Icons.waveform-path.svg",
            [IconKind.AdminSettings] = "VibeAlarm.Assets.Icons.admin-alt.svg",
            [IconKind.UserPreferences] = "VibeAlarm.Assets.Icons.user-skill-gear.svg",
        };

        /// <summary>Renders an icon to a bitmap of the given pixel size, in the given ink.
        /// The drawn color is baked in, so callers re-render when the theme/ink changes (same
        /// contract the old RenderNavGlyph followed).
        ///
        /// Vector-backed kinds rasterize their embedded SVG at the requested size in the
        /// requested ink — identical sizing/coloring to the glyph path, so callers cannot
        /// tell the two apart.
        ///
        /// OWNERSHIP: the returned bitmap is SHARED — results are cached by (kind, ink, size)
        /// and repeat requests return the same instance, so callers must NOT dispose it and
        /// must never mutate it (assigning it to Image/IconLeft properties is the intended
        /// use). The cache is released by <see cref="ClearCache"/> (called on theme change,
        /// when every ink color shifts and the old entries would otherwise go stale).</summary>
        public static Bitmap Render(IconKind kind, Color color, int size = 22)
        {
            (IconKind Kind, int Argb, int Size) key = (kind, color.ToArgb(), size);
            if (rasterCache.TryGetValue(key, out Bitmap? cached))
            {
                if (IsUsable(cached))
                {
                    return cached;
                }
                // A control disposed our shared bitmap (some WinForms controls dispose the
                // Image they were assigned) — drop the dead entry and re-render below.
                rasterCache.Remove(key);
            }

            Bitmap rendered = RenderUncached(kind, color, size);
            rasterCache[key] = rendered;
            return rendered;
        }

        /// <summary>Releases every cached raster. Call when the theme changes so the cache
        /// only ever holds one theme's worth of bitmaps.</summary>
        public static void ClearCache()
        {
            foreach (Bitmap bmp in rasterCache.Values)
            {
                if (IsUsable(bmp))
                {
                    bmp.Dispose();
                }
            }
            rasterCache.Clear();
        }

        /// <summary>Cached rasters keyed by (kind, ink, size). UI-thread only, like every
        /// other GDI+ consumer in the app.</summary>
        private static readonly Dictionary<(IconKind Kind, int Argb, int Size), Bitmap> rasterCache = new();

        /// <summary>A disposed GDI+ bitmap throws on property access; this cheap probe keeps
        /// the cache self-healing instead of blowing up a paint cycle.</summary>
        private static bool IsUsable(Bitmap bmp)
        {
            try
            {
                _ = bmp.Width;
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private static Bitmap RenderUncached(IconKind kind, Color color, int size)
        {
            if (SvgResources.TryGetValue(kind, out string? resourceName))
            {
                Bitmap? vector = TryRenderSvg(resourceName, color, size);
                if (vector != null)
                {
                    return vector;
                }
            }

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

        /// <summary>Rasterizes an embedded SVG in the requested ink. Degrades to null — never
        /// throws — on any failure (missing resource, parse error, rasterizer exception) so the
        /// caller falls back to the glyph path, mirroring FontRegistry's font-missing policy:
        /// a bad SVG must never crash rendering.</summary>
        private static Bitmap? TryRenderSvg(string resourceName, Color ink, int size)
        {
            try
            {
                using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
                if (stream == null)
                {
                    Debug.WriteLine($"Icon SVG resource not found (using glyph): {resourceName}");
                    return null;
                }

                // SVG.NET 3.x has no Stream overload — parse the manifest content from a string.
                string svg;
                using (StreamReader sr = new(stream))
                {
                    svg = sr.ReadToEnd();
                }
                SvgDocument doc = SvgDocument.FromSvg<SvgDocument>(svg);

                // The bundled files are normalized to carry no fill attributes by design —
                // apply the requested ink to every paintable element so vector icons follow
                // the theme exactly like glyphs do (no pre-baked PNG colors).
                foreach (SvgVisualElement element in doc.Descendants().OfType<SvgVisualElement>())
                {
                    element.Fill = new SvgColourServer(ink);
                }

                doc.Width = size;
                doc.Height = size;
                return doc.Draw();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Icon SVG render failed (using glyph): {resourceName}: {ex.Message}");
                return null;
            }
        }

        /// <summary>The raw glyph character (for owner-drawn items that paint their own text),
        /// already resolved to the symbol/fallback face.</summary>
        public static string Glyph(IconKind kind)
            => FontRegistry.HasSymbolFonts
                ? ((char)Convert.ToInt32(Glyphs[kind], 16)).ToString()
                : Fallbacks[kind];
    }
}
