using AudioConverter.Core.Split;

namespace AudioConverter.Core.Tests;

[TestClass]
public sealed class SilenceSplitPlannerTests
{
    [TestMethod]
    public void Plan_CutsInTheMiddleOfEachInnerSilence()
    {
        var tracks = SilenceSplitPlanner.Plan(
            [new SilenceInterval(100, 102), new SilenceInterval(200, 204)], 300, minimumTrackSeconds: 30);

        CollectionAssert.AreEqual(new[] { 0d, 101d, 202d }, tracks.Select(track => track.StartSeconds).ToArray());
        CollectionAssert.AreEqual(new[] { 101d, 202d, 300d }, tracks.Select(track => track.EndSeconds).ToArray());
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, tracks.Select(track => track.Number).ToArray());
    }

    [TestMethod]
    public void Plan_DropsLeadingAndTrailingSilence()
    {
        var tracks = SilenceSplitPlanner.Plan(
            [new SilenceInterval(0, 2), new SilenceInterval(100, 102), new SilenceInterval(298, 300)], 300);

        Assert.AreEqual(2, tracks.Count);
        Assert.AreEqual(1d, tracks[0].StartSeconds, 1e-9);
        Assert.AreEqual(101d, tracks[0].EndSeconds, 1e-9);
        Assert.AreEqual(101d, tracks[1].StartSeconds, 1e-9);
        Assert.AreEqual(299d, tracks[1].EndSeconds, 1e-9);
    }

    [TestMethod]
    public void Plan_JoinsPiecesShorterThanTheMinimumToTheirNeighbour()
    {
        // The first piece is 20.5 s, below the 30 s minimum, so it joins the next piece.
        var tracks = SilenceSplitPlanner.Plan(
            [new SilenceInterval(20, 21), new SilenceInterval(100, 101)], 300, minimumTrackSeconds: 30);

        Assert.AreEqual(2, tracks.Count);
        Assert.AreEqual(0d, tracks[0].StartSeconds, 1e-9);
        Assert.AreEqual(100.5, tracks[0].EndSeconds, 1e-9);
        Assert.AreEqual(199.5, tracks[1].DurationSeconds, 1e-9);
    }

    [TestMethod]
    public void Plan_WithoutSilenceReturnsOneTrack()
    {
        var tracks = SilenceSplitPlanner.Plan([], 240);

        Assert.AreEqual(1, tracks.Count);
        Assert.AreEqual(240d, tracks[0].DurationSeconds, 1e-9);
    }

    [TestMethod]
    public void Plan_FileThatIsAllSilenceHasNoTracks()
    {
        Assert.AreEqual(0, SilenceSplitPlanner.Plan([new SilenceInterval(0, 60)], 60).Count);
    }
}
