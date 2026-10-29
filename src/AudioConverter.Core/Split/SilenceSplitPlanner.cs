namespace AudioConverter.Core.Split;

/// <summary>
/// Turns detected silences into tracks. Cuts sit in the middle of each silence, silence at the very start
/// and end of the file is dropped, and pieces shorter than the minimum are joined to a neighbour.
/// </summary>
public static class SilenceSplitPlanner
{
    public const double DefaultMinimumTrackSeconds = 30;

    // A silence within this distance of the file edge counts as leading or trailing silence.
    private const double EdgeToleranceSeconds = 0.05;

    public static IReadOnlyList<AlbumTrack> Plan(
        IReadOnlyList<SilenceInterval> silences,
        double sourceDurationSeconds,
        double minimumTrackSeconds = DefaultMinimumTrackSeconds)
    {
        ArgumentNullException.ThrowIfNull(silences);
        if (!double.IsFinite(sourceDurationSeconds) || sourceDurationSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(sourceDurationSeconds));
        if (!double.IsFinite(minimumTrackSeconds) || minimumTrackSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(minimumTrackSeconds));

        var start = 0d;
        var end = sourceDurationSeconds;
        var cuts = new List<double>();
        foreach (var silence in silences.Where(item => item.EndSeconds > item.StartSeconds).OrderBy(item => item.StartSeconds))
        {
            var middle = (silence.StartSeconds + silence.EndSeconds) / 2;
            var atStart = silence.StartSeconds <= EdgeToleranceSeconds;
            var atEnd = silence.EndSeconds >= sourceDurationSeconds - EdgeToleranceSeconds;
            if (atStart && atEnd) return [];
            if (atStart) start = middle;
            else if (atEnd) end = middle;
            else cuts.Add(middle);
        }

        var boundaries = new List<double> { start };
        boundaries.AddRange(cuts.Where(cut => cut > start && cut < end));
        boundaries.Add(end);

        var segments = new List<(double Start, double End)>();
        for (var index = 0; index + 1 < boundaries.Count; index++)
        {
            if (boundaries[index + 1] > boundaries[index]) segments.Add((boundaries[index], boundaries[index + 1]));
        }

        // A short piece joins the piece before it, or the one after it when it comes first.
        var merged = new List<(double Start, double End)>();
        foreach (var segment in segments)
        {
            if (merged.Count > 0 && (Length(merged[^1]) < minimumTrackSeconds || Length(segment) < minimumTrackSeconds))
                merged[^1] = (merged[^1].Start, segment.End);
            else
                merged.Add(segment);
        }

        return merged
            .Select((segment, index) => new AlbumTrack(index + 1, $"Track {index + 1:00}", "", segment.Start, segment.End))
            .ToArray();
    }

    private static double Length((double Start, double End) segment) => segment.End - segment.Start;
}
