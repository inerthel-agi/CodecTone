# CodecTone

CodecTone converts, compresses, normalizes, cuts, splits, remixes, tags, and inspects audio files locally on Windows with FFmpeg. It also extracts embedded album artwork. It provides a WPF interface and a command-line executable. Media processing does not use cloud storage or telemetry.

Repository: [github.com/inerthel-agi/CodecTone](https://github.com/inerthel-agi/CodecTone)

## Requirements

- Windows 10 version 1809 or newer, x64.
- .NET 8 Desktop Runtime for the lightweight GUI build.
- .NET SDK 8.0.420 to build or test the source.
- `ffmpeg` and `ffprobe` on `PATH`, or network access for the optional managed FFmpeg installation.

CodecTone can install FFmpeg 9.0.1 in `%LOCALAPPDATA%\AudioConverter\runtime\ffmpeg`. The installer verifies the pinned SHA-256 before extracting `ffmpeg.exe` and `ffprobe.exe`.

## Install

Clone and build the project:

```console
git clone https://github.com/inerthel-agi/CodecTone.git
cd CodecTone
build_executable.bat
```

The build produces:

```text
release\final\CodecTone.exe
release\final\CodecTone-Standalone.exe
release\cli\CodecTone.Cli.exe
```

`CodecTone.exe` requires the .NET 8 Desktop Runtime. `CodecTone-Standalone.exe` includes the runtime. Double-click `launch_audio_converter.bat` to select the appropriate GUI build automatically.

## Usage

The GUI has a sidebar with nine tool workspaces, an About page, a theme button, and a language button. The theme button cycles OLED, White, and Glass. Glass shows a live acrylic backdrop on Windows 11 22H2 or newer when **Transparency effects** is on; otherwise it uses an opaque tinted background. The language button cycles System, English, and Français. System follows the Windows display language: French when Windows is in French, English otherwise.

- **Convert** queues files and non-recursive folders, then converts them in order with a status per file.
- **Compress** uses quality profiles or a total-size budget and scans folders recursively. Switch a file off to leave it out.
- **Normalize** measures loudness and brings each track, or a whole album with one gain, to -14, -16, -23 LUFS or a custom target under a true-peak ceiling.
- **Cut** selects a waveform range, applies fades, previews it, and exports it.
- **Split album** cuts one long recording into tracks from a cue sheet or from detected silences, and names files with a pattern such as `{track:00} - {title}`.
- **Remix** applies 33 categorized presets or an ordered effect rack to one song and edits its metadata. **Save as preset** stores your own racks. The EARRAPE preset displays an explicit hearing warning.
- **Tags** edits tags for many files at once and can rename files from their tags.
- **Artwork** extracts one embedded front cover per album as the original image, PNG, JPEG, or WebP.
- **Inspect** shows a spectrogram and file details, and flags lossless files whose spectrum stops like a lossy encode.
- **About** explains the local processing pipeline and links to the maintainer profile.

CodecTone remembers the theme, the language, and each tool's choices in `%LOCALAPPDATA%\AudioConverter\settings.json`. Saved remix presets live in `remix-presets.json` in the same folder. The taskbar button shows the progress of the running operation.

Drop a file or folder anywhere in the window, or use **Choose file** and **Choose folder**. After an operation succeeds, **Open folder** in the bottom bar opens its output folder. While an operation runs, **Cancel** replaces the main action button.

In Cut and Remix, the playback controls sit under the waveform. **Pause** pauses and resumes; click the time ruler to seek. Click the waveform to focus it, then use the arrow keys to nudge the selected handle (Shift: 1 s); `[` and `]` pick the handle. In Remix, play **Original** and **Remix** to compare the same source interval. Preview volume defaults to 25%, applies while playing, and does not affect exports. Transitions follow the Windows **Animation effects** setting.

In Artwork, analyze a folder, select an album, then choose **Preview cover**. **Save report** exports the current report to text. About provides **Open local diagnostics**; the rotating log stays local and can include file paths.

Supported source formats are MP3, FLAC, WAV, OGG, AAC, and M4A. Compression destinations exclude WAV because it generally does not reduce file size.

Convert FLAC to MP3 at 320 kbit/s:

```console
release\cli\CodecTone.Cli.exe "music\track.flac" --format mp3 --bitrate 320k
```

Convert a folder to 24-bit FLAC:

```console
release\cli\CodecTone.Cli.exe "music\album" --format flac --sample-rate 48000 --bit-depth 24 --output-dir "exports" --suffix "_archive"
```

Cut a selection with one-second fades:

```console
release\cli\CodecTone.Cli.exe "music\track.flac" --format flac --start 14.06 --end 164.48 --fade-in 1 --fade-out 1 --suffix "_trimmed"
```

Compress a folder recursively with the high-fidelity profile:

```console
release\cli\CodecTone.Cli.exe "music\album" --format m4a --compress-profile high --recursive --output-dir "compressed"
```

Compress a folder toward a 500 MiB total budget:

```console
release\cli\CodecTone.Cli.exe "music\album" --format mp3 --target-total-mb 500 --recursive --optimize-artwork
```

Install the pinned managed FFmpeg build:

```console
release\cli\CodecTone.Cli.exe --install-ffmpeg
```

Run the full suite (FFmpeg integration tests encode synthetic fixtures):

```console
dotnet test AudioConverter.sln -c Release
```

## Configuration

| Option | Type | Default | Effect |
| --- | --- | --- | --- |
| `source` | file or folder | required | Selects a source file or folder. |
| `-f`, `--format` | `mp3`, `flac`, `wav`, `ogg`, `aac`, `m4a` | required | Selects the destination format. |
| `-o`, `--output-dir` | folder | operation-specific folder | Selects the destination folder. |
| `--suffix` | text | empty | Adds text before the destination extension. |
| `--bitrate` | rate | `192k` | Sets lossy conversion bitrate. |
| `--sample-rate` | integer Hz | source rate | Sets WAV or FLAC sample rate. |
| `--bit-depth` | `16`, `24`, `32` | encoder default | Sets WAV or FLAC bit depth. FLAC accepts 16 or 24. |
| `--start`, `--end` | seconds | full source | Selects a cut interval. |
| `--fade-in`, `--fade-out` | seconds | `0` | Applies fades inside the cut interval. |
| `--compress-profile` | `high`, `balanced`, `small` | disabled | Enables recursive profile-based compression. |
| `--target-total-mb` | number | disabled | Enables compression toward a total folder budget. |
| `--recursive` | flag | enabled in compression mode | Includes supported files in subfolders. |
| `--optimize-artwork` | flag | enabled in compression mode | Limits embedded cover art to 1200 pixels. |
| `--no-optimize-artwork` | flag | disabled | Preserves original cover-art dimensions. |
| `--overwrite` | flag | disabled | Replaces an existing destination file. |
| `--no-metadata` | flag | disabled | Removes metadata and cover-art mapping. |
| `--install-ffmpeg` | flag | disabled | Downloads and verifies the managed FFmpeg build. |

## Limitations

- Folder conversion does not scan nested folders. Compression does.
- Target-total-size mode is unavailable for lossless FLAC output.
- WAV, raw AAC, and OGG output do not receive embedded cover art.
- Metadata support depends on the source and destination containers.
- Rendering late preview excerpts processes preceding audio to preserve effects and fades.
- Preview loudness normalization is single-pass; final export uses two-pass measurements and may sound different.
- Remix processes one song at a time and does not load VST plugins.
- Artwork extraction does not search online services or modify source audio files.
- Inspect flags any lossless file whose energy stops below 20 kHz, so band-limited recordings can be reported as lossy sources.
- Tags renames files inside their current folder only.
- The French interface translates CodecTone's own text. Messages from FFmpeg, ffprobe, and the processing engine (skip reasons, validation errors) stay in English, as do remix preset names.
- Video conversion, web services, cloud storage, and telemetry are not implemented.

## License

CodecTone is licensed under the [MIT License](LICENSE).

The optional Gyan FFmpeg Essentials build is distributed under GPLv3. Its source URL, checksum, and license are recorded in `%LOCALAPPDATA%\AudioConverter\runtime\ffmpeg\SOURCE.txt` after installation.
