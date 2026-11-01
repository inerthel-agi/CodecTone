# Changelog

## 1.3.0 - 2026-11-01

### Added

- Added four tools. **Normalize** brings tracks or whole albums to a loudness target under a true-peak ceiling. **Split album** cuts one recording into tracks from a cue sheet or from silences. **Tags** edits tags for many files and renames files from their tags. **Inspect** shows a spectrogram, file details, and a lossy-source check.
- Added French. The interface follows the Windows display language, and the sidebar language button switches between System, English, and Français without a restart.
- Convert now keeps a queue of files with a status per file. Compress lists the files it will process and lets you switch files off.
- Remix can save, apply, and delete your own presets.
- Previews play through waveOut with the real position: pause, seek on the time ruler, and live volume. WPF `MediaPlayer` needed Windows Media Player, which N and LTSC editions lack.
- CodecTone remembers each tool's choices between launches, and the taskbar button shows operation progress.
- Added a GitHub Actions workflow that builds and tests on Windows, plus scripts that prepare code signing and a winget manifest (see `docs/distribution.md`). Nothing is signed or published by default.
- Added a Glass theme: translucent layers with bright top edges and larger corner radii over the Windows 11 acrylic backdrop. Without the backdrop (Windows 10, Windows 11 before 22H2, or transparency effects off), it falls back to an opaque tinted background. The theme button now cycles OLED, White, and Glass, and shows the current theme.

### Changed

- Replaced the header and wrapping tab row with a sidebar that has icons, the theme switch, and the local-only notice.
- Added page titles, drop zones for empty sources, and file cards for loaded sources. Convert and Compress no longer accept a typed source path.
- Added a window-wide drop overlay while dragging files.
- Restyled controls: switches for options, segmented format pickers, themed slider, text boxes and tooltips, and a visible keyboard focus ring.
- Switched interface labels from all caps to sentence case.
- The bottom bar shows progress only while busy, replaces the main action with Cancel, and offers Open folder after a successful operation.
- Moved Save report next to each report.
- Darkened the WHITE accent to `#007A5E` so accent text and white-on-accent text reach a 4.5:1 contrast ratio.
- Selected items, chips, info boxes and the scrollbar use neutral tones. The accent stays on small signals: switches, slider fills, the active-item bar, status text and icons.
- Added transitions: pages fade in, hover and selection states fade, buttons scale when pressed, switches slide, and the report disclosure chevron turns. They become instant when Windows animation effects are off.
- The waveform shows a time ruler, dims the area outside the selection, labels both handles, and shows a playhead during Cut and Remix previews. Arrow keys nudge the selected handle (Shift: 1 s), `[` and `]` pick a handle, Home and End jump to the bounds.
- Cut and Remix playback controls sit under the waveform. The bottom bar keeps only the status and the main action.
- Remix presets show as cards with a one-line description. Intensity and the Export/Metadata inspector use segmented controls. Each effect parameter has a slider bounded by the same limits the rack validator enforces.
- Reports show one row per file with a status icon and shortened paths. The full text stays under Raw report and in Save report.
- Split the main view model into one view model per tool on top of a shared `WorkspaceHost`.
- The About page lists all nine tools.
- The About page credits inerthel. The single-instance mutex and activation event names changed accordingly, so a 1.2.1 instance and this build can run side by side.

### Fixed

- The active tab and other accent-filled controls showed light text on the bright OLED accent (about 1.7:1 contrast).
- Sidebar buttons and remix rack buttons expose accessible names.

## 1.2.1 - 2026-09-28

### Fixed

- The About window's VIEW ON GITHUB button now opens github.com/inerthel-agi.
- README repository and clone links now point to github.com/inerthel-agi/CodecTone.

## 1.2.0 - 2026-09-05

### Added

- Added the Extract Artwork workspace for one file or a recursive folder, with one deduplicated front cover per album.
- Added original image extraction and optional PNG, JPEG, or WebP conversion with bounded dimensions.
- Added streamed SHA-256 collision detection, numbered output names, progress, cancellation, and batch reporting.
- Added editable Distortion and Bit Crusher modules. The fixed-power EARRAPE preset uses classic wide-band clipping without bit crushing, with per-activation confirmation, a persistent hearing warning, and a -0.1 dBFS final ceiling.
- Original/remix preview comparison with a separate preview volume applied on the next playback.
- Cover preview with source details and an output-folder shortcut.
- Text report export and a bounded local diagnostic log accessible from About.
- Desktop regression tests for shutdown, edited racks, and OLED/WHITE layouts.

### Fixed

- Wait for pending audio work and analysis before closing the window.
- Reject remix exports over their source and honor cancellation immediately before publishing outputs.
- Apply remix fades on the full timeline before extracting previews, including tempo changes.
- Preserve manual rack edits when an intensity change is declined.
- Validate integer distortion oversampling and preserve small clipping thresholds.
- Select album front covers across tracks, retry alternate candidates, and isolate artwork preparation failures.
- Aggregate waveform peaks at the source rate without downmix cancellation or low-pass loss.
- Keep partial batch reports after cancellation and report inaccessible or linked folders.

## 1.1.0 - 2026-08-25

### Added

- Added the single-track Remix Audio workspace with a movable 20-second preview, editable effect rack, metadata editor, and cover-art controls.
- Added 32 remix presets organized into Speed & Pitch, Bass & Punch, Atmosphere, Vocal & Clarity, Color & Texture, and Mastering categories.
- Added Light, Medium, and Strong preset intensity levels.
- Added local full-song analysis for loudness, dynamics, frequency balance, and stereo width, with an invalidating JSON cache and static fallback.
- Added Compressor, Stereo Width, High-Pass, Low-Pass, and Soft Limiter effects.
- Added cancellation controls for the GUI and Ctrl+C cancellation for the CLI.

### Changed

- Presets now adapt bounded parameters to the loaded song while keeping the generated rack editable.
- Waveform extraction now bounds decoded samples for long audio files.
- Recursive compression skips inaccessible folders and directory links instead of stopping the batch.
- Managed FFmpeg installations take precedence over PATH executables.

### Fixed

- Replaced metallic echo-based reverb with convolution reverb using a verified embedded impulse response.
- Prevented failed conversions from truncating or replacing an existing destination.
- Added ffprobe validation and atomic destination replacement for conversion, compression, and cutting.
- Fixed false successful results when a destination already existed.
- Stopped FFmpeg process trees when an operation is cancelled or the application closes.
- Prevented format, suffix, and output settings from changing during an active batch.
- Serialized concurrent settings and remix-analysis cache writes.
- Fixed adaptive analysis and loudness normalization for mono and silent audio.
- Preserved batch processing after individual corrupt-file failures without leaving partial outputs.
