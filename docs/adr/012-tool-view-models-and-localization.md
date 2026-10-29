# ADR 012: Tool view models, new tools and localization

## Status

Accepted on 2026-10-29.

## Context

`MainViewModel` held every tool's state, commands and reports, and grew with
each workspace. Four tools were planned on top of the five existing ones
(Normalize, Split album, Tags, Inspect), plus a Convert queue, saved remix
presets and remembered settings. The interface text was English only, written
inline in XAML and C#. WPF `MediaPlayer` failed on Windows editions without
Windows Media Player (N, LTSC), so previews could not pause or seek.

## Decision

- One view model per tool, derived from `WorkspaceBase`. Shared services,
  busy state, progress, status and preview playback live in `WorkspaceHost`.
  `MainViewModel` only owns navigation, theme and language.
- `WorkspaceBase` gives each tool a primary command and label for the bottom
  bar, a report, drop handling and `LoadPreferences`/`SavePreferences`.
  Preferences are stored as `Tool.Name` strings in `settings.json`.
- Previews play PCM WAV through the winmm `waveOut` API
  (`Services/AudioPreviewPlayer.cs`). It reports the real position, pauses,
  seeks by re-queueing from an offset and changes volume while playing.
- Text lives in `Localization/Strings.resx` (English, neutral) and
  `Strings.fr.resx`. XAML uses `{l:T Key}`, a binding to the `Localizer`
  indexer, so a language change updates the window without a restart. Code
  uses `L.T("Key")` and `L.F("Key", args)`.
- List values (enum names, option strings) keep their English value as the
  model value. `LocalizeItemConverter` shows them through `Item` keys:
  "High fidelity" reads `ItemHighFidelity`. It is bound with the language as a
  second value so lists follow language changes.
- On a language change, each view model raises all its properties and
  re-reads placeholder text it set itself (`WorkspaceBase.Refreshed`). Status
  and report text written during an operation keeps its language until the
  next operation.
- Report lines keep their English `OK`, `FAIL`, `SKIP`, `READY` and
  `CANCELLED` prefixes in every language, because the report list classifies
  lines by prefix. The French resources keep the same prefixes and the same
  `{n}` placeholders; a test enforces both.
- The language setting is `null` (follow the Windows display language), `en`
  or `fr`, stored as `Language` in `settings.json`.

## Consequences

- Adding a tool means one view model, one view and one `NavItem`; the shell
  does not change.
- `EveryUsedKey_ExistsInEnglishAndFrench` scans XAML and C# for keys and fails
  when one is missing in either language. The render test captures every page
  in French at 900×650 and at full height to catch text overflow.
- Messages produced by FFmpeg, ffprobe and the Core/Infrastructure layers
  (skip reasons, validation errors) stay in English. Remix preset names stay
  in English; their descriptions are translated through
  `PresetDescription<Preset>` keys that exist only in French.
- Language switching relies on a static event. Tests that create many
  `MainViewModel` instances keep old workspaces subscribed; the app creates
  one per run.
