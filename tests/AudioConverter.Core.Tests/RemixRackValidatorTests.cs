using AudioConverter.Core.Remix;

namespace AudioConverter.Core.Tests;

[TestClass]
public sealed class RemixRackValidatorTests
{
    [TestMethod]
    public void Bounds_DriveValidationAndCapFadesBySource()
    {
        Assert.AreEqual((1d, 16d), RemixRackValidator.Bounds(RemixEffectKind.Distortion, 2, 120));
        Assert.AreEqual((0d, 12d), RemixRackValidator.Bounds(RemixEffectKind.FadeIn, 0, 12));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => RemixRackValidator.Bounds(RemixEffectKind.Volume, 1, 120));

        var error = Assert.ThrowsException<ArgumentException>(() => RemixRackValidator.Validate([new BassEffect(19, 90)], 120));
        Assert.AreEqual("Bass gain must be between -12 and 18.", error.Message);
    }

    [TestMethod]
    public void Validate_RejectsDuplicateTempoEffect()
    {
        RemixEffect[] rack = [new TempoPitchEffect(0.9), new TempoPitchEffect(1.1)];

        var error = Assert.ThrowsException<ArgumentException>(() => RemixRackValidator.Validate(rack, 120));

        StringAssert.Contains(error.Message, "Tempo/Pitch");
    }

    [TestMethod]
    public void Validate_RejectsGainEffectAfterNormalize()
    {
        RemixEffect[] rack = [new LoudnessNormalizeEffect(-14), new VolumeEffect(2)];

        Assert.ThrowsException<ArgumentException>(() => RemixRackValidator.Validate(rack, 120));
    }

    [TestMethod]
    public void Validate_RejectsEffectAfterFade()
    {
        RemixEffect[] rack = [new FadeOutEffect(3), new BassEffect(4, 90)];

        Assert.ThrowsException<ArgumentException>(() => RemixRackValidator.Validate(rack, 120));
    }

    [TestMethod]
    public void Validate_RejectsOutOfRangeRate()
    {
        RemixEffect[] rack = [new TempoPitchEffect(2.1)];

        Assert.ThrowsException<ArgumentException>(() => RemixRackValidator.Validate(rack, 120));
    }

    [TestMethod]
    public void CalculateDuration_UsesCoupledTempoRate()
    {
        RemixEffect[] rack = [new TempoPitchEffect(0.8)];

        Assert.AreEqual(150, RemixRackValidator.CalculateOutputDuration(120, rack), 0.001);
    }

    [TestMethod]
    public void Validate_RejectsUnsafeMasteringValues()
    {
        Assert.ThrowsException<ArgumentException>(() => RemixRackValidator.Validate([new CompressorEffect(-18, 12, 2)], 120));
        Assert.ThrowsException<ArgumentException>(() => RemixRackValidator.Validate([new StereoWidthEffect(2.5)], 120));
        Assert.ThrowsException<ArgumentException>(() => RemixRackValidator.Validate([new HighPassEffect(10)], 120));
        Assert.ThrowsException<ArgumentException>(() => RemixRackValidator.Validate([new LowPassEffect(500)], 120));
        Assert.ThrowsException<ArgumentException>(() => RemixRackValidator.Validate([new SoftLimiterEffect(0)], 120));
    }

    [TestMethod]
    public void Validate_RejectsUnsafeExtremeEffectValues()
    {
        Assert.ThrowsException<ArgumentException>(() => RemixRackValidator.Validate([new DistortionEffect(40, 0.03, 4)], 120));
        Assert.ThrowsException<ArgumentException>(() => RemixRackValidator.Validate([new BitCrusherEffect(0, 16, 1)], 120));
        Assert.ThrowsException<ArgumentException>(() => RemixRackValidator.Validate([new BitCrusherEffect(3, 300, 1)], 120));
    }
}
