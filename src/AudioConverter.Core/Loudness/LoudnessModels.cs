namespace AudioConverter.Core.Loudness;

public enum LoudnessMode
{
    PerTrack,
    Album,
}

/// <summary>One measured file: integrated loudness in LUFS and true peak in dBTP.</summary>
public sealed record LoudnessTrack(
    string Path,
    string RelativePath,
    double DurationSeconds,
    double IntegratedLufs,
    double TruePeakDbtp);

/// <summary>Planned gain for one file. Skipped files are reported and never written.</summary>
public sealed record LoudnessPlanItem(
    LoudnessTrack Track,
    double GainDb,
    bool PeakLimited,
    bool Skipped,
    string? SkipReason);

/// <summary>AlbumLufs and AlbumGainDb are set in album mode only. GainCapped means the true-peak ceiling chose the gain.</summary>
public sealed record LoudnessPlan(
    LoudnessMode Mode,
    double TargetLufs,
    double CeilingDbtp,
    double? AlbumLufs,
    double? AlbumGainDb,
    bool GainCapped,
    IReadOnlyList<LoudnessPlanItem> Items);
