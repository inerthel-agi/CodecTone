using System.Globalization;

namespace AudioConverter.Core.Split;

/// <summary>
/// Parses a single-file cue sheet. Quoted and bare values, REM lines and INDEX 00 pregaps are handled;
/// sheets that reference several FILE entries are refused.
/// </summary>
public static class CueSheetParser
{
    private const int FramesPerSecond = 75;

    public static CueSheet Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var discTitle = "";
        var discPerformer = "";
        var drafts = new List<TrackDraft>();
        TrackDraft? current = null;
        var fileEntries = 0;

        foreach (var rawLine in text.TrimStart('﻿').Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;
            var space = line.IndexOfAny([' ', '\t']);
            var keyword = (space < 0 ? line : line[..space]).ToUpperInvariant();
            var value = space < 0 ? "" : line[(space + 1)..].Trim();
            switch (keyword)
            {
                case "FILE":
                    if (++fileEntries > 1)
                        throw new NotSupportedException("Cue sheets with several FILE entries are not supported. Split one audio file at a time.");
                    break;
                case "TITLE":
                    if (current is null) discTitle = ReadValue(value); else current.Title = ReadValue(value);
                    break;
                case "PERFORMER":
                    if (current is null) discPerformer = ReadValue(value); else current.Performer = ReadValue(value);
                    break;
                case "TRACK":
                    current = StartTrack(value);
                    drafts.Add(current);
                    break;
                case "INDEX":
                    if (current is not null) SetIndex(current, value);
                    break;
                // REM, FLAGS, ISRC and other metadata do not change where a track starts.
            }
        }

        if (drafts.Count == 0) throw new FormatException("The cue sheet has no tracks.");

        var tracks = new List<CueTrack>(drafts.Count);
        var previousStart = 0d;
        foreach (var draft in drafts)
        {
            if (draft.StartSeconds is not { } start) throw new FormatException($"Track {draft.Number} has no INDEX 01.");
            if (start < previousStart) throw new FormatException($"Track {draft.Number} starts before the track before it.");
            previousStart = start;
            tracks.Add(new CueTrack(draft.Number, draft.Title.Length > 0 ? draft.Title : $"Track {draft.Number:00}", draft.Performer, start));
        }
        return new CueSheet(discTitle, discPerformer, tracks);
    }

    private static string ReadValue(string value)
    {
        if (value.Length >= 2 && value[0] == '"')
        {
            var end = value.IndexOf('"', 1);
            return end > 0 ? value[1..end] : value[1..];
        }
        return value;
    }

    private static TrackDraft StartTrack(string value)
    {
        var fields = value.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 2 || !int.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            throw new FormatException($"Invalid TRACK line: {value}");
        if (!fields[1].Equals("AUDIO", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException($"Track {number} is not an audio track.");
        return new TrackDraft(number);
    }

    private static void SetIndex(TrackDraft track, string value)
    {
        var fields = value.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 2 || !int.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out var index))
            throw new FormatException($"Invalid INDEX line for track {track.Number}: {value}");
        // INDEX 00 is the pregap; the track itself starts at INDEX 01.
        if (index == 1) track.StartSeconds ??= ParseTimecode(fields[1], track.Number);
    }

    /// <summary>Parses mm:ss:ff, where ff counts frames at 75 per second.</summary>
    private static double ParseTimecode(string value, int trackNumber)
    {
        var parts = value.Split(':');
        if (parts.Length != 3
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var frames)
            || seconds >= 60
            || frames >= FramesPerSecond)
        {
            throw new FormatException($"Invalid INDEX time for track {trackNumber}: {value}");
        }
        return minutes * 60d + seconds + frames / (double)FramesPerSecond;
    }

    private sealed class TrackDraft(int number)
    {
        public int Number { get; } = number;
        public string Title { get; set; } = "";
        public string Performer { get; set; } = "";
        public double? StartSeconds { get; set; }
    }
}
