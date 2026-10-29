using AudioConverter.Core.Inspection;

namespace AudioConverter.Core.Tests;

[TestClass]
public sealed class SourceVerdictTests
{
    [TestMethod]
    public void LosslessWithCutoffBelowTwentyKilohertz_FlagsLossySource()
    {
        var verdict = SourceVerdict.Create("flac", 16_000, 22_050);

        Assert.AreEqual(SourceVerdictKind.LikelyLossySource, verdict.Kind);
        Assert.AreEqual("Likely made from a lossy source: energy stops near 16 kHz.", verdict.Message);
    }

    [TestMethod]
    public void LosslessAtNyquist_IsFullBand()
    {
        var verdict = SourceVerdict.Create("pcm_s24le", 22_050, 22_050);

        Assert.AreEqual(SourceVerdictKind.FullBand, verdict.Kind);
        Assert.AreEqual("Full-band audio up to 22.05 kHz.", verdict.Message);
    }

    [TestMethod]
    public void LosslessAboveTwentyKilohertz_IsNotFlagged()
    {
        var verdict = SourceVerdict.Create("alac", 20_500, 22_050);

        Assert.AreEqual(SourceVerdictKind.FullBand, verdict.Kind);
    }

    [TestMethod]
    public void LosslessWithLowerNyquist_UsesShareOfNyquist()
    {
        // 19 kHz is under 20 kHz but still 79% of a 48 kHz file's Nyquist.
        var verdict = SourceVerdict.Create("FLAC", 19_000, 24_000);

        Assert.AreEqual(SourceVerdictKind.LikelyLossySource, verdict.Kind);
    }

    [TestMethod]
    public void LossyCodec_ReportsItsEnergyLimit()
    {
        var verdict = SourceVerdict.Create("mp3", 16_000, 22_050);

        Assert.AreEqual(SourceVerdictKind.LossyFile, verdict.Kind);
        Assert.AreEqual("Lossy file with energy up to 16 kHz.", verdict.Message);
    }

    [TestMethod]
    public void MissingCutoff_SaysNoClearCutoffWasFound()
    {
        var verdict = SourceVerdict.Create("flac", null, 22_050);

        Assert.AreEqual(SourceVerdictKind.NoCutoff, verdict.Kind);
        Assert.AreEqual("No clear cutoff found.", verdict.Message);
    }

    [TestMethod]
    public void IsLossless_RecognisesLosslessCodecsOnly()
    {
        Assert.IsTrue(SourceVerdict.IsLossless("flac"));
        Assert.IsTrue(SourceVerdict.IsLossless("ALAC"));
        Assert.IsTrue(SourceVerdict.IsLossless("pcm_s16le"));
        Assert.IsFalse(SourceVerdict.IsLossless("mp3"));
        Assert.IsFalse(SourceVerdict.IsLossless("opus"));
    }
}
