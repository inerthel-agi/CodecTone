using AudioConverter.Core.Validation;

namespace AudioConverter.Core.Split;

/// <summary>A cue track as written in the sheet: its INDEX 01 start, without an end.</summary>
public sealed record CueTrack(int Number, string Title, string Performer, double StartSeconds);

/// <summary>A parsed single-file cue sheet: disc-level title and performer, then its tracks.</summary>
public sealed record CueSheet(string Title, string Performer, IReadOnlyList<CueTrack> Tracks)
{
    /// <summary>Each track runs until the next track starts; the last one runs to the end of the source.</summary>
    public IReadOnlyList<AlbumTrack> ToAlbumTracks(double sourceDurationSeconds)
    {
        if (!double.IsFinite(sourceDurationSeconds) || sourceDurationSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(sourceDurationSeconds), "The source duration is unknown.");

        var tracks = new List<AlbumTrack>(Tracks.Count);
        for (var index = 0; index < Tracks.Count; index++)
        {
            var track = Tracks[index];
            if (track.StartSeconds >= sourceDurationSeconds)
                throw new InvalidDataException($"Track {track.Number} starts at {TimestampParser.Format(track.StartSeconds)}, after the end of the audio.");

            var end = index + 1 < Tracks.Count
                ? Math.Min(Tracks[index + 1].StartSeconds, sourceDurationSeconds)
                : sourceDurationSeconds;
            if (end <= track.StartSeconds)
                throw new InvalidDataException($"Track {track.Number} has no length: track {Tracks[index + 1].Number} starts at the same time.");

            tracks.Add(new AlbumTrack(track.Number, track.Title, track.Performer, track.StartSeconds, end));
        }
        return tracks;
    }
}
