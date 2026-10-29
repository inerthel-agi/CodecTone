namespace AudioConverter.Core.Loudness;

public static class LoudnessPlanner
{
    public const double MinimumTargetLufs = -36;
    public const double MaximumTargetLufs = -6;
    public const double MinimumCeilingDbtp = -9;
    public const double MaximumCeilingDbtp = 0;

    // loudnorm reports -70 LUFS when no block passes its gate, so that floor counts as silence.
    private const double SilenceFloorLufs = -70;

    /// <summary>Rejects a target or ceiling outside the supported range before any measurement runs.</summary>
    public static void ValidateSettings(double targetLufs, double ceilingDbtp)
    {
        if (!double.IsFinite(targetLufs) || targetLufs < MinimumTargetLufs || targetLufs > MaximumTargetLufs)
            throw new ArgumentException($"Target loudness must be between {MinimumTargetLufs} and {MaximumTargetLufs} LUFS.");
        if (!double.IsFinite(ceilingDbtp) || ceilingDbtp < MinimumCeilingDbtp || ceilingDbtp > MaximumCeilingDbtp)
            throw new ArgumentException($"True-peak ceiling must be between {MinimumCeilingDbtp} and {MaximumCeilingDbtp} dBTP.");
    }

    public static LoudnessPlan Create(IReadOnlyList<LoudnessTrack> tracks, LoudnessMode mode, double targetLufs, double ceilingDbtp)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        if (tracks.Count == 0) throw new ArgumentException("At least one measured file is required.");
        ValidateSettings(targetLufs, ceilingDbtp);

        return mode switch
        {
            LoudnessMode.PerTrack => PlanPerTrack(tracks, targetLufs, ceilingDbtp),
            LoudnessMode.Album => PlanAlbum(tracks, targetLufs, ceilingDbtp),
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };
    }

    /// <summary>Integrated loudness of a group: 10·log10(Σ dᵢ·10^(Lᵢ/10) / Σ dᵢ). Silent files are left out.</summary>
    public static double CombineIntegrated(IEnumerable<LoudnessTrack> tracks)
    {
        var energy = 0d;
        var duration = 0d;
        foreach (var track in tracks)
        {
            if (IsSilent(track.IntegratedLufs) || track.DurationSeconds <= 0) continue;
            energy += track.DurationSeconds * Math.Pow(10, track.IntegratedLufs / 10);
            duration += track.DurationSeconds;
        }
        if (duration <= 0) throw new ArgumentException("Album loudness needs at least one audible file.");
        return 10 * Math.Log10(energy / duration);
    }

    /// <summary>True when a measurement has no audible signal, so a gain would only amplify nothing.</summary>
    public static bool IsSilent(double integratedLufs) => !double.IsFinite(integratedLufs) || integratedLufs <= SilenceFloorLufs;

    private static LoudnessPlan PlanPerTrack(IReadOnlyList<LoudnessTrack> tracks, double target, double ceiling)
    {
        var items = tracks.Select(track =>
        {
            if (IsSilent(track.IntegratedLufs))
                return new LoudnessPlanItem(track, 0, false, true, "silent, nothing to normalize");
            var gain = target - track.IntegratedLufs;
            return new LoudnessPlanItem(track, gain, track.TruePeakDbtp + gain > ceiling, false, null);
        }).ToArray();
        return new LoudnessPlan(LoudnessMode.PerTrack, target, ceiling, null, null, false, items);
    }

    private static LoudnessPlan PlanAlbum(IReadOnlyList<LoudnessTrack> tracks, double target, double ceiling)
    {
        var albumLufs = CombineIntegrated(tracks);
        var wantedGain = target - albumLufs;
        // One gain for every file: the loudest true peak decides how much room is left under the ceiling.
        var peakRoom = ceiling - tracks.Max(track => track.TruePeakDbtp);
        var gain = Math.Min(wantedGain, peakRoom);
        var capped = peakRoom < wantedGain;
        var items = tracks.Select(track => new LoudnessPlanItem(track, gain, capped, false, null)).ToArray();
        return new LoudnessPlan(LoudnessMode.Album, target, ceiling, albumLufs, gain, capped, items);
    }
}
