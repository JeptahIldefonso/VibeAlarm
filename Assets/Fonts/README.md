# Embedded typefaces

`FontRegistry` (`UI/Theming/FontRegistry.cs`) registers these with GDI+ at startup via
`PrivateFontCollection.AddMemoryFont`, because Windows ships neither family and WinForms has
no `@font-face` equivalent — an unregistered family silently substitutes Microsoft Sans Serif.

## Required files

Filenames are **load-bearing**: they become the embedded resource names that
`FontRegistry.EmbeddedFaces` looks up. Place them here exactly as named.

| File | Registers as | Role |
|---|---|---|
| `Inter-Regular.ttf` | `Inter` | body / UI text |
| `Inter-SemiBold.ttf` | `Inter SemiBold` | headers, nav, greetings |
| `JetBrainsMono-Regular.ttf` | `JetBrains Mono` | numbers, dates, times, statuses, all metadata |

Sources:

- Inter — <https://github.com/rsms/inter/releases> (`Inter-*.ttf` from the desktop archive)
- JetBrains Mono — <https://github.com/JetBrains/JetBrainsMono/releases>

## License

Both are **SIL Open Font License 1.1**, which permits bundling and redistribution inside an
application. Ship the license text alongside the fonts as `OFL-Inter.txt` and
`OFL-JetBrainsMono.txt` (copy `OFL.txt` from each upstream release).

`.csproj` embeds `Assets\Fonts\*.ttf` — no per-file edit is needed when adding these.

## Behaviour when absent

The build succeeds and the app runs: `FontRegistry` degrades per role to the closest
Windows-native face rather than throwing —

- display → `Bahnschrift`, then `Segoe UI`
- body → `Segoe UI Variable Text`, then `Segoe UI`
- mono → `Cascadia Mono`, then `Consolas`, then `Courier New`

Check which family actually won at runtime with `FontRegistry.ResolvedDisplayFamily` /
`ResolvedBodyFamily` / `ResolvedMonoFamily`, or `FontRegistry.HasEmbeddedFonts`.
