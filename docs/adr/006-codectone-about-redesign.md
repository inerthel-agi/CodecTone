# ADR 006: CodecTone identity and About workspace

## Status

Accepted on 2026-08-25.

## Context

The public name "Audio Converter" does not distinguish the application. The
application now converts, cuts, and compresses audio, and needs an About
workspace that explains its local FFmpeg pipeline and privacy model.

## Decision

- Use `CodecTone` as the public product and executable name.
- Keep the C# namespaces, solution name, application-data directory, and named
  single-instance primitives unchanged for compatibility.
- Add ABOUT after COMPRESS AUDIO and hide operation controls while it is active.
- Use a restrained editorial layout: strong typography, flat surfaces, rules,
  generous spacing, and the existing green accent only for hierarchy.
- Show the assembly version dynamically, supported tasks, local pipeline,
  privacy statement, runtime technologies, and Stealthy Labs attribution.
- Open `https://github.com/inerthel-agi` only from an explicit button through the
  Windows default browser. Do not embed remote content or a WebView.
- Show the MIT project license confirmed for public repository publication.

## Decision log

- `CodecTone` was selected over AudioKiln and LocalCodec.
- Internal identifiers remain stable to avoid settings and mono-instance
  migrations.
- ABOUT is informational and has no progress bar or primary operation button.
- The existing logo is retained as the core identity asset.

## Verification

- Validate the GitHub URI before shell execution.
- Confirm ABOUT in OLED and WHITE themes at the minimum window size.
- Confirm version metadata and all renamed lightweight, standalone, and CLI
  artifacts.
