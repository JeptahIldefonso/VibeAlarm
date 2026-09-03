# Prompt: VibeAlarm — Consolidate to 2 Theme Modes (Part 3)

Paste this alongside Parts 1 and 2 (`vibealarm-guna-integration-prompt.md`, `vibealarm-editorial-technical-refinement-prompt.md`) in the same coding agent session. This one changes the theme *count and values*; Parts 1 and 2's architecture (`ThemeService`/`ThemePreset`, `DesignTokens`, `UIControlFactory`) stays intact.

## What this supersedes

Parts 1 and 2 were written to protect the existing 5-preset system (Editorial Light, Warm Paper, Graphite, Stone, Midnight) against being silently collapsed by a source document that didn't know it existed. **That protection no longer applies here** — this is an explicit product decision from the person who owns the app: reduce `ThemeService.Presets` from 5 down to exactly **2**. Implement it as a real removal, not an extra option bolted onto the existing 5.

## The 2 modes

### Mode 1 — "Light" (grounded in your own portfolio site, jeptahportfolio.vercel.app)

| Token | Value | Source |
|---|---|---|
| `PrimaryBg` | `#C4C4C4` | given directly |
| `CardBgColor`/`SecondaryBg` | `#C9C9C9` | slightly lifted from background — the reference site is almost entirely flat/whitespace-driven with border-only separation, not filled cards; keep VibeAlarm's cards close to the background tone and let borders do the separating work |
| `TextColor` (primary) | `#101010` | near-black, high-contrast, matches the heavy dark text on the reference site |
| `MutedTextColor` (secondary/labels) | `#6B6B6B` | matches the gray nav-label weight on the reference site (`CONNECT`, `GITHUB`, project numbers) |
| `BorderColor` | `#9A9A9A` | thin, visible-but-subtle divider lines, matching the rule under the name and around the pill-style buttons on the reference site |
| `AccentColor` | `#000000` | pure black for the active/selected state |
| `IsLight` | `true` | |

**On "text full block":** reading this as *bold weight, high-contrast typography* (heavy black text against the mid-gray background, the way the reference site's body copy and labels read), not as a request to reproduce that site's ornate swash-serif display font specifically — that's a much bigger, separate typography decision (a new custom display font, on top of the sans/mono pairing already scoped in Part 2). If you do want that exact display serif carried over for VibeAlarm's hero elements (the "VibeAlarm" logotype, the big greeting), say so explicitly and I'll scope the font-licensing/embedding work for it the way Part 2 scoped Inter/Space Grotesk/JetBrains Mono — don't have the implementer guess at this.

### Mode 2 — "Dark" (Facebook/Google-style dark mode)

Grounded in Facebook's own verified dark-mode palette (Meta's 2020 dark theme), read as neutral/near-grayscale — which is why it fits your strict-monochrome rule without modification, no blue accent pulled in:

| Token | Value |
|---|---|
| `PrimaryBg` | `#18191A` |
| `CardBgColor`/`SecondaryBg` | `#242526` |
| `CardHoverBg`/`BorderColor` | `#3A3B3C` |
| `TextColor` (primary) | `#E4E6EB` |
| `MutedTextColor` (secondary) | `#B0B3B8` |
| `AccentColor` | `#FFFFFF` (kept monochrome — no Facebook blue) |
| `IsLight` | `false` |

**Note this replaces Part 2's Midnight refinement values** (`#0A0A0A`/`#121212`/`#FAFAFA`/`#A1A1AA`/`#27272A`) — that was one direction (a near-black "editorial technical" dark theme), this is a different, warmer-gray reference (Facebook's actual production values, background not pure black already by design, per their stated rationale of avoiding harsh black-on-white-style contrast). Use this table as the current source of truth for the dark preset.

## Architecture changes

1. **`ThemeService.InitializePresets()`** — replace the 5-preset population with exactly these 2 (`Presets.Add(...)` twice, not five times). Delete Editorial Light, Warm Paper, Graphite, and Stone entirely — don't leave them as unused dead presets in the list.
2. **Naming** — label them plainly as **Light** and **Dark** in the UI (the standard, expected naming for a 2-mode picker — this is the "industry standard, easy to use" pattern from earlier). Internally, name/slug them however fits the codebase, but the user-facing labels should just be Light/Dark.
3. **Settings UI — replace the dropdown, don't shrink it.** A 5-item `Guna2ComboBox` reduced to 2 items is the wrong control for a binary choice. Replace it with a `Guna2ToggleSwitch` (the same component already used elsewhere for Notifications/Sound in Part 1) labeled `LIGHT` / `DARK` in the surrounding monospace metadata style from Part 2. This is a smaller, more honest control for a 2-state setting and matches the "ergonomic, easy to use" goal from the original plan.
4. **Settings migration** — some users' `settings.json` may still have a persisted theme name from the old 5-preset system (`"Graphite"`, `"Warm Paper"`, etc.). On load, if the persisted name doesn't match `"Light"` or `"Dark"`, don't crash and don't silently reset without reasoning — bucket it by the old preset's `IsLight` flag if you still have that information available, or default to Light if not. Confirm this path is actually exercised (test with an old-format `settings.json` on disk), not just written and assumed to work.
5. **Custom background feature (from Part 1 §4/the earlier plan)** — if a custom background image is active, confirm both new presets' text/border contrast values still hold up against a photo background at the currently-supported opacity range. Re-verify after this change, don't assume Part 1's contrast guidance still holds with new hex values.

## Report back with

- Confirm the preset list is exactly 2 entries (`Presets.Count == 2`), not 2 visible + 3 hidden.
- Confirm the Settings screen shows a toggle, not a dropdown.
- Confirm old `settings.json` files with a removed theme name load without error and land on a sensible mode.
- Confirm the custom-background contrast check was re-run against the new hex values, not carried over unchanged from Part 1.
