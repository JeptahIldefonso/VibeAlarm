using System.Drawing;
using VibeAlarm.UI.Theming;
using Xunit;

namespace VibeAlarm.Tests
{
    /// <summary>
    /// Accent catalog integrity: the eight named accents of the locked color system with
    /// their exact Base/Hover/OnAccent triples, Matrix Green as the default, legacy
    /// key mapping (so an existing settings.json lands on the same look), and
    /// lookup/resolve semantics (unknown keys fall back, never throw). The OnAccent
    /// values are the spec's fixed pairings — deliberately NOT a computed WCAG rule.
    /// </summary>
    public class AccentCatalogTests
    {
        [Fact]
        public void Catalog_offers_the_eight_named_accents_with_their_exact_triples()
        {
            Assert.Equal(8, AccentCatalog.Options.Count);
            Assert.Contains(AccentCatalog.Options, o =>
                o.Key == "Obsidian Core"
                && o.Base == Color.FromArgb(0x8E, 0x8E, 0x93)
                && o.Hover == Color.FromArgb(0xA0, 0xA0, 0xA6)
                && o.OnAccent == Color.FromArgb(0x00, 0x00, 0x00));
            Assert.Contains(AccentCatalog.Options, o =>
                o.Key == "Neo Blue"
                && o.Base == Color.FromArgb(0x4C, 0x8D, 0xFF)
                && o.Hover == Color.FromArgb(0x6F, 0xA1, 0xFF)
                && o.OnAccent == Color.FromArgb(0xFF, 0xFF, 0xFF));
            Assert.Contains(AccentCatalog.Options, o =>
                o.Key == "Violet System"
                && o.Base == Color.FromArgb(0x7C, 0x5C, 0xFC)
                && o.Hover == Color.FromArgb(0x94, 0x78, 0xFD)
                && o.OnAccent == Color.FromArgb(0xFF, 0xFF, 0xFF));
            Assert.Contains(AccentCatalog.Options, o =>
                o.Key == "Matrix Green"
                && o.Base == Color.FromArgb(0x22, 0xC5, 0x5E)
                && o.Hover == Color.FromArgb(0x34, 0xD3, 0x74)
                && o.OnAccent == Color.FromArgb(0x00, 0x00, 0x00));
            Assert.Contains(AccentCatalog.Options, o =>
                o.Key == "Sunset Red"
                && o.Base == Color.FromArgb(0xE5, 0x48, 0x4D)
                && o.Hover == Color.FromArgb(0xF1, 0x60, 0x65)
                && o.OnAccent == Color.FromArgb(0xFF, 0xFF, 0xFF));
            Assert.Contains(AccentCatalog.Options, o =>
                o.Key == "Solar Amber"
                && o.Base == Color.FromArgb(0xF0, 0xA0, 0x20)
                && o.Hover == Color.FromArgb(0xF5, 0xB3, 0x47)
                && o.OnAccent == Color.FromArgb(0x00, 0x00, 0x00));
            Assert.Contains(AccentCatalog.Options, o =>
                o.Key == "Pearl White"
                && o.Base == Color.FromArgb(0xE8, 0xE9, 0xEC)
                && o.Hover == Color.FromArgb(0xF2, 0xF3, 0xF5)
                && o.OnAccent == Color.FromArgb(0x00, 0x00, 0x00));
            Assert.Contains(AccentCatalog.Options, o =>
                o.Key == "Platinum Silver"
                && o.Base == Color.FromArgb(0xC4, 0xC8, 0xCC)
                && o.Hover == Color.FromArgb(0xD4, 0xD7, 0xDA)
                && o.OnAccent == Color.FromArgb(0x00, 0x00, 0x00));
        }

        [Fact]
        public void Default_is_Matrix_Green()
        {
            Assert.Equal("Matrix Green", AccentCatalog.DefaultKey);
            Assert.Equal(Color.FromArgb(0x22, 0xC5, 0x5E), AccentCatalog.Default.Base);
        }

        [Theory]
        [InlineData("Fluent", "Neo Blue")]
        [InlineData("Coral", "Sunset Red")]
        [InlineData("Forest", "Matrix Green")]
        [InlineData("Spotify Green", "Matrix Green")] // the retired default still lands on the green
        [InlineData("Indigo", "Violet System")]
        [InlineData("Amber", "Solar Amber")]
        public void Legacy_swatch_keys_map_onto_the_corresponding_accent(string legacyKey, string expectedKey)
        {
            // The first circular-swatch set persisted short keys; renaming the catalog must
            // not shift an existing settings.json.
            AccentOption? found = AccentCatalog.Find(legacyKey);
            Assert.NotNull(found);
            Assert.Equal(expectedKey, found!.Key);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("Ocean")] // unknown: a hand-edited settings.json value must never crash the UI
        public void Resolve_falls_back_to_the_default_instead_of_throwing(string? key)
        {
            Assert.Same(AccentCatalog.Default, AccentCatalog.Resolve(key));
        }

        [Fact]
        public void Find_is_case_insensitive_and_returns_null_for_unknown()
        {
            Assert.Same(AccentCatalog.Default, AccentCatalog.Find("matrix green"));
            Assert.Null(AccentCatalog.Find("Ocean"));
            Assert.Null(AccentCatalog.Find(null));
        }
    }
}
