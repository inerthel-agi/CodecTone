using System.Globalization;

namespace AudioConverter.Core.Inspection;

public enum SourceVerdictKind { NoCutoff, LikelyLossySource, FullBand, LossyFile }

/// <summary>The one-line reading of a file's spectrum: whether a lossless file shows a lossy-source cutoff.</summary>
public sealed record SourceVerdict(SourceVerdictKind Kind, string Message)
{
    /// <summary>Lossless audio normally keeps energy above this frequency; a lower cutoff hints at a lossy origin.</summary>
    // ponytail: any lossless file whose energy stops below 20 kHz is flagged, band-limited recordings included; add a per-source override if that misfires.
    public const double LossyCeilingHz = 20_000;

    /// <summary>A cutoff at or above this share of Nyquist counts as full band.</summary>
    public const double FullBandShare = 0.92;

    private static readonly string[] LosslessCodecs = ["flac", "alac", "wavpack", "ape", "tta", "tak"];

    public static bool IsLossless(string codecName) =>
        codecName.StartsWith("pcm_", StringComparison.OrdinalIgnoreCase)
        || LosslessCodecs.Contains(codecName, StringComparer.OrdinalIgnoreCase);

    public static SourceVerdict Create(string codecName, double? cutoffHz, double nyquistHz)
    {
        if (cutoffHz is not { } cutoff) return new(SourceVerdictKind.NoCutoff, "No clear cutoff found.");

        var kHz = (cutoff / 1000d).ToString("0.##", CultureInfo.InvariantCulture);
        if (!IsLossless(codecName))
            return new(SourceVerdictKind.LossyFile, $"Lossy file with energy up to {kHz} kHz.");
        if (cutoff <= LossyCeilingHz && cutoff < nyquistHz * FullBandShare)
            return new(SourceVerdictKind.LikelyLossySource, $"Likely made from a lossy source: energy stops near {kHz} kHz.");
        return new(SourceVerdictKind.FullBand, $"Full-band audio up to {kHz} kHz.");
    }
}
