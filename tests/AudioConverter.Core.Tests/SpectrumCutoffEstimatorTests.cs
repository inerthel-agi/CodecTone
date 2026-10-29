using AudioConverter.Core.Inspection;

namespace AudioConverter.Core.Tests;

[TestClass]
public sealed class SpectrumCutoffEstimatorTests
{
    private const double Nyquist = 22_050;
    private const int Rows = 512;
    private const double BinHz = Nyquist / (Rows - 1);

    [TestMethod]
    public void FullBandSpectrum_ReportsNyquist()
    {
        var rows = Enumerable.Repeat(0.7, Rows).ToArray();

        Assert.AreEqual(Nyquist, SpectrumCutoffEstimator.Estimate(rows, Nyquist)!.Value, 1e-9);
    }

    [TestMethod]
    public void SixteenKilohertzShelf_FindsTheEdge()
    {
        var cutoff = SpectrumCutoffEstimator.Estimate(Shelf(16_000), Nyquist);

        Assert.IsNotNull(cutoff);
        Assert.AreEqual(16_000, cutoff.Value, 2 * BinHz);
    }

    [TestMethod]
    public void IsolatedBrightRowsAboveTheShelf_DoNotMoveTheCutoff()
    {
        var rows = Shelf(16_000);
        for (var row = 400; row < Rows; row += 37) rows[row] = 0.95;

        var cutoff = SpectrumCutoffEstimator.Estimate(rows, Nyquist);

        Assert.IsNotNull(cutoff);
        Assert.AreEqual(16_000, cutoff.Value, 2 * BinHz);
    }

    [TestMethod]
    public void Silence_HasNoCutoff()
    {
        Assert.IsNull(SpectrumCutoffEstimator.Estimate(new double[Rows], Nyquist));
    }

    [TestMethod]
    public void QuietPassband_BelowTheLevelFloor_HasNoCutoff()
    {
        var rows = Enumerable.Repeat(0.03, Rows).ToArray();

        Assert.IsNull(SpectrumCutoffEstimator.Estimate(rows, Nyquist));
    }

    [TestMethod]
    public void TooFewRows_HasNoCutoff()
    {
        Assert.IsNull(SpectrumCutoffEstimator.Estimate([0.8, 0.8, 0.1], Nyquist));
    }

    /// <summary>Brightness 0.8 up to the cutoff and 0.1 above it, indexed from 0 Hz.</summary>
    private static double[] Shelf(double cutoffHz) =>
        Enumerable.Range(0, Rows)
            .Select(row => row / (double)(Rows - 1) * Nyquist <= cutoffHz ? 0.8 : 0.1)
            .ToArray();
}
