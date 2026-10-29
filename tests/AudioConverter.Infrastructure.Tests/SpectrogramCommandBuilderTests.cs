using AudioConverter.Infrastructure.Inspection;

namespace AudioConverter.Infrastructure.Tests;

[TestClass]
public sealed class SpectrogramCommandBuilderTests
{
    [TestMethod]
    public void Build_AnalysisRenderHasNoLegendAndALinearAxis()
    {
        var args = SpectrogramCommandBuilder.Build("in.flac", "analysis.png", 1024, 512, legend: false);

        Assert.AreEqual(
            "[0:a:0]showspectrumpic=s=1024x512:legend=0:scale=log:color=intensity:fscale=lin[spectrum]",
            args[Array.IndexOf(args, "-filter_complex") + 1]);
        Assert.AreEqual("in.flac", args[Array.IndexOf(args, "-i") + 1]);
        CollectionAssert.AreEqual(new[] { "-map", "[spectrum]", "-frames:v", "1" }, args[^5..^1]);
        Assert.AreEqual("analysis.png", args[^1]);
    }

    [TestMethod]
    public void Build_SaveRenderUsesTheRequestedSizeAndLegend()
    {
        var args = SpectrogramCommandBuilder.Build("in.mp3", "out.png", 1600, 800, legend: true);

        StringAssert.Contains(args[Array.IndexOf(args, "-filter_complex") + 1], "showspectrumpic=s=1600x800:legend=1:");
    }

    [TestMethod]
    public void Build_RejectsSizesOutsideTheSupportedRange()
    {
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => SpectrogramCommandBuilder.Build("in.flac", "out.png", 32, 512, legend: false));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => SpectrogramCommandBuilder.Build("in.flac", "out.png", 1024, 9000, legend: false));
    }
}
