using AudioConverter.Core.Inspection;

namespace AudioConverter.Core.Tests;

[TestClass]
public sealed class SpectrogramAxisTests
{
    [TestMethod]
    public void CdRate_LabelsEveryFiveKilohertzAndEndsAtNyquist()
    {
        var ticks = SpectrogramAxis.Create(22_050);

        CollectionAssert.AreEqual(
            new[] { "0 Hz", "5 kHz", "10 kHz", "15 kHz", "20 kHz", "22.05 kHz" },
            ticks.Select(tick => tick.Label).ToArray());
        Assert.AreEqual(0d, ticks[0].Fraction, 1e-9);
        Assert.AreEqual(5_000d / 22_050, ticks[1].Fraction, 1e-9);
        Assert.AreEqual(1d, ticks[^1].Fraction, 1e-9);
    }

    [TestMethod]
    public void LowSampleRate_OnlyLabelsZeroAndNyquist()
    {
        var ticks = SpectrogramAxis.Create(4_000);

        CollectionAssert.AreEqual(new[] { "0 Hz", "4 kHz" }, ticks.Select(tick => tick.Label).ToArray());
    }
}
