# Changelog

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
