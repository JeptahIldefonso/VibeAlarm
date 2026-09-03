using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace VibeAlarm.UI.Theming
{
    /// <summary>
    /// Loads the app's embedded OFL typefaces (Inter for display/body, JetBrains Mono for
    /// numerics/metadata) into a process-wide <see cref="PrivateFontCollection"/>.
    ///
    /// WinForms has no @font-face: a font family must be registered with GDI+ before
    /// <c>new Font(family, ...)</c> will resolve it, and Windows ships none of these faces.
    /// So the .ttf files are embedded resources, copied to unmanaged memory at startup via
    /// <c>AddMemoryFont</c>, and the collection (plus every allocation backing it) is held for
    /// the lifetime of the process — GDI+ reads from those buffers lazily during text rendering,
    /// so freeing them early produces garbled glyphs or an access violation, not a clean error.
    ///
    /// Every lookup degrades instead of throwing: a missing or corrupt face falls back to a
    /// Windows-native substitute of the same genre, so the app always renders readable text.
    /// </summary>
    public static class FontRegistry
    {
        // Held for the process lifetime — see class remarks. Never disposed before exit.
        private static readonly PrivateFontCollection Collection = new();
        private static readonly List<IntPtr> NativeBuffers = new();
        private static readonly Dictionary<string, FontFamily> Loaded =
            new(StringComparer.OrdinalIgnoreCase);

        private static bool initialized;
        private static readonly object InitLock = new();

        /// <summary>Embedded resource names, and the family name each one registers as.</summary>
        private static readonly (string Resource, string Family)[] EmbeddedFaces =
        {
            ("VibeAlarm.Assets.Fonts.Inter-Regular.ttf",         "Inter"),
            ("VibeAlarm.Assets.Fonts.Inter-SemiBold.ttf",        "Inter SemiBold"),
            ("VibeAlarm.Assets.Fonts.JetBrainsMono-Regular.ttf", "JetBrains Mono"),
        };

        /// <summary>Preferred family per role, then Windows-native fallbacks in descending order of fit.</summary>
        private static readonly string[] DisplayCandidates = { "Inter SemiBold", "Inter", "Bahnschrift", "Segoe UI" };
        private static readonly string[] BodyCandidates = { "Inter", "Segoe UI Variable Text", "Segoe UI" };
        private static readonly string[] MonoCandidates = { "JetBrains Mono", "Cascadia Mono", "Consolas", "Courier New" };

        /// <summary>True when at least one embedded face registered — useful for diagnostics/tests.</summary>
        public static bool HasEmbeddedFonts { get; private set; }

        /// <summary>
        /// Registers the embedded faces. Safe to call more than once and from any thread;
        /// call once at startup before the first form is constructed.
        /// </summary>
        public static void Initialize()
        {
            lock (InitLock)
            {
                if (initialized)
                {
                    return;
                }
                initialized = true;

                foreach ((string resource, string family) in EmbeddedFaces)
                {
                    TryLoadEmbedded(resource, family);
                }

                HasEmbeddedFonts = Loaded.Count > 0;
            }
        }

        private static void TryLoadEmbedded(string resourceName, string familyName)
        {
            try
            {
                using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
                if (stream == null)
                {
                    // Font not bundled in this build — the resolver falls back to a native face.
                    Debug.WriteLine($"Font resource not found (using fallback): {resourceName}");
                    return;
                }

                byte[] data = new byte[stream.Length];
                int read = 0;
                while (read < data.Length)
                {
                    int n = stream.Read(data, read, data.Length - read);
                    if (n <= 0)
                    {
                        break;
                    }
                    read += n;
                }

                IntPtr buffer = Marshal.AllocCoTaskMem(data.Length);
                Marshal.Copy(data, 0, buffer, data.Length);
                // Deliberately not freed: GDI+ reads this buffer for the process lifetime.
                NativeBuffers.Add(buffer);
                Collection.AddMemoryFont(buffer, data.Length);

                // AddMemoryFont reports failure by simply not adding a family, so confirm by name.
                foreach (FontFamily family in Collection.Families)
                {
                    if (!Loaded.ContainsKey(family.Name))
                    {
                        Loaded[family.Name] = family;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load embedded font {familyName}: {ex.Message}");
            }
        }

        /// <summary>
        /// Builds a font for the given role, walking the candidate list until one resolves.
        /// Also verifies the family supports the requested style, since a Regular-only embedded
        /// face throws on <c>FontStyle.Bold</c> rather than synthesizing one.
        /// </summary>
        private static Font Resolve(string[] candidates, float size, FontStyle style)
        {
            foreach (string name in candidates)
            {
                if (Loaded.TryGetValue(name, out FontFamily? family))
                {
                    try
                    {
                        FontStyle usable = family.IsStyleAvailable(style)
                            ? style
                            : family.IsStyleAvailable(FontStyle.Regular) ? FontStyle.Regular : style;
                        return new Font(family, size, usable);
                    }
                    catch (ArgumentException)
                    {
                        // Style genuinely unavailable — try the next candidate.
                    }
                }
            }

            // No embedded match: hand the name to GDI+, which substitutes if the family is absent.
            foreach (string name in candidates)
            {
                try
                {
                    Font candidate = new Font(name, size, style);
                    if (candidate.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    {
                        return candidate;
                    }
                    candidate.Dispose();
                }
                catch (ArgumentException)
                {
                }
            }

            return new Font(FontFamily.GenericSansSerif, size, style);
        }

        /// <summary>Geometric grotesk for headers, nav, and greetings.</summary>
        public static Font Display(float size, FontStyle style = FontStyle.Bold)
            => Resolve(DisplayCandidates, size, style);

        /// <summary>Body/UI text.</summary>
        public static Font Body(float size, FontStyle style = FontStyle.Regular)
            => Resolve(BodyCandidates, size, style);

        /// <summary>Monospace — numbers, dates, times, statuses, and all metadata labels.</summary>
        public static Font Mono(float size, FontStyle style = FontStyle.Regular)
            => Resolve(MonoCandidates, size, style);

        /// <summary>The family actually backing each role. Diagnostics only.</summary>
        public static string ResolvedDisplayFamily => Describe(DisplayCandidates);
        public static string ResolvedBodyFamily => Describe(BodyCandidates);
        public static string ResolvedMonoFamily => Describe(MonoCandidates);

        private static string Describe(string[] candidates)
        {
            using Font f = Resolve(candidates, 10F, FontStyle.Regular);
            return f.Name;
        }
    }
}
