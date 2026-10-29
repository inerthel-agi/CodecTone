using System.Globalization;

namespace AudioConverter.Core.Inspection;

/// <summary>A frequency label. Fraction 0 is the bottom edge (0 Hz) and 1 the top edge (Nyquist).</summary>
public sealed record AxisTick(double Fraction, string Label);

public static class SpectrogramAxis
{
    /// <summary>Labels every <paramref name="stepHz"/> from 0 Hz, skipping any that crowd the Nyquist label, and always ends with Nyquist.</summary>
    public static IReadOnlyList<AxisTick> Create(double nyquistHz, double stepHz = 5_000)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(nyquistHz, 0d);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(stepHz, 0d);

        var ticks = new List<AxisTick>();
        for (var hz = 0d; hz < nyquistHz - 1_000; hz += stepHz)
            ticks.Add(new AxisTick(hz / nyquistHz, Label(hz)));
        ticks.Add(new AxisTick(1, Label(nyquistHz)));
        return ticks;
    }

    private static string Label(double hz) =>
        hz == 0 ? "0 Hz" : (hz / 1000d).ToString("0.##", CultureInfo.InvariantCulture) + " kHz";
}
