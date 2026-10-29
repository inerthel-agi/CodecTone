namespace AudioConverter.Core.Inspection;

/// <summary>
/// Finds the highest frequency where a spectrogram still carries energy. Rows run from 0 Hz (index 0)
/// to Nyquist (last index); each value is a 0..1 brightness that rises with level.
/// </summary>
public static class SpectrumCutoffEstimator
{
    /// <summary>A passband brightness below this means the file is effectively silent.</summary>
    public const double MinimumPassbandLevel = 0.05;

    /// <summary>A passband-to-floor gap below this means the spectrum is flat, so there is no cutoff inside the band.</summary>
    public const double MinimumContrast = 0.1;

    /// <summary>Energy counts as present once it rises this far (as a share of the contrast) above the noise floor.</summary>
    public const double PresenceShare = 0.25;

    /// <summary>
    /// Returns the cutoff in Hz, Nyquist when the spectrum is flat to the top, or null when there is no
    /// usable passband to measure against. Each row is median-smoothed first, so isolated bright rows are ignored.
    /// </summary>
    public static double? Estimate(IReadOnlyList<double> rows, double nyquistHz)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(nyquistHz, 0d);
        var count = rows.Count;
        if (count < 8) return null;

        var radius = Math.Max(1, count / 128);
        var smoothed = new double[count];
        var window = new double[2 * radius + 1];
        for (var row = 0; row < count; row++)
        {
            var size = 0;
            for (var near = Math.Max(0, row - radius); near <= Math.Min(count - 1, row + radius); near++)
                window[size++] = rows[near];
            Array.Sort(window, 0, size);
            smoothed[row] = size % 2 == 1 ? window[size / 2] : (window[size / 2 - 1] + window[size / 2]) / 2;
        }

        var passband = Median(smoothed[..Math.Max(1, count / 2)]);
        if (passband < MinimumPassbandLevel) return null;

        var floor = Percentile(smoothed, 0.02);
        var contrast = passband - floor;
        if (contrast < MinimumContrast) return nyquistHz;

        var presence = floor + PresenceShare * contrast;
        for (var row = count - 1; row >= 0; row--)
        {
            if (smoothed[row] >= presence) return row / (double)(count - 1) * nyquistHz;
        }
        return null;
    }

    private static double Median(double[] values)
    {
        var sorted = (double[])values.Clone();
        Array.Sort(sorted);
        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }

    private static double Percentile(double[] values, double share)
    {
        var sorted = (double[])values.Clone();
        Array.Sort(sorted);
        return sorted[(int)Math.Floor(share * (sorted.Length - 1))];
    }
}
