# CodecTone

CodecTone converts, compresses, cuts, remixes, and extracts embedded album artwork locally on Windows with FFmpeg. It provides a WPF interface and a command-line executable. Media processing does not use cloud storage or telemetry.

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

The GUI contains five tool workspaces and an About page:

- **CONVERT** processes one file or a non-recursive folder batch.
- **CUT AUDIO** selects a waveform range, applies fades, previews it, and exports it.
- **COMPRESS AUDIO** uses quality profiles or a total-size budget and scans folders recursively.
- **EXTRACT ARTWORK** extracts one embedded front cover per album as the original image, PNG, JPEG, or WebP.
- **REMIX AUDIO** applies 33 categorized presets or an ordered effect rack to one song and edits its metadata. The EARRAPE preset displays an explicit hearing warning.
- **ABOUT** explains the local processing pipeline and links to the maintainer profile.

In Remix, use PREVIEW ORIGINAL and PREVIEW REMIX to compare the same source interval. Preview volume defaults to 25%, applies on the next playback, and does not affect exports.

In Extract Artwork, analyze a folder, select an album, then choose PREVIEW COVER. SAVE REPORT exports the current report to text. About provides OPEN LOCAL DIAGNOSTICS; the rotating log stays local and can include file paths.

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
- Preview supports Play and Stop, without pause or cursor scrubbing. Rendering late excerpts processes preceding audio to preserve effects and fades.
- Preview loudness normalization is single-pass; final export uses two-pass measurements and may sound different.
- Remix processes one song at a time and does not load VST plugins.
- Artwork extraction does not search online services or modify source audio files.
- Video conversion, web services, cloud storage, and telemetry are not implemented.

## License

CodecTone is licensed under the [MIT License](LICENSE).

The optional Gyan FFmpeg Essentials build is distributed under GPLv3. Its source URL, checksum, and license are recorded in `%LOCALAPPDATA%\AudioConverter\runtime\ffmpeg\SOURCE.txt` after installation.
