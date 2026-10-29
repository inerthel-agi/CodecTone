using AudioConverter.Core.Loudness;

namespace AudioConverter.Core.Tests;

[TestClass]
public sealed class LoudnessPlannerTests
{
    [TestMethod]
    public void CombineIntegrated_WeightsEnergyByDuration()
    {
        // (1 s at -10 LUFS + 3 s at -20 LUFS) gives 10·log10((0.1 + 3·0.01) / 4), about -14.881.
        var combined = LoudnessPlanner.CombineIntegrated([Track("short", -10, 1), Track("long", -20, 3)]);

        Assert.AreEqual(-14.8812, combined, 0.001);
    }

    [TestMethod]
    public void CombineIntegrated_LeavesSilentFilesOut()
    {
        var combined = LoudnessPlanner.CombineIntegrated([Track("song", -14, 2), Track("silence", double.NegativeInfinity, 5)]);

        Assert.AreEqual(-14, combined, 0.0001);
    }

    [TestMethod]
    public void PerTrack_ReachesTargetAndFlagsFilesThatHitTheCeiling()
    {
        var plan = LoudnessPlanner.Create([Track("quiet", -20, 10, -8), Track("hot", -20, 10, -3)], LoudnessMode.PerTrack, -14, -1);

        Assert.AreEqual(6, plan.Items[0].GainDb, 1e-9);
        Assert.IsFalse(plan.Items[0].PeakLimited);
        Assert.AreEqual(6, plan.Items[1].GainDb, 1e-9);
        Assert.IsTrue(plan.Items[1].PeakLimited);
        Assert.IsFalse(plan.GainCapped);
    }

    [TestMethod]
    public void PerTrack_SkipsSilentFilesWithoutGain()
    {
        var plan = LoudnessPlanner.Create(
            [Track("silence", double.NegativeInfinity, 10, double.NegativeInfinity), Track("song", -18, 10, -2)],
            LoudnessMode.PerTrack,
            -14,
            -1);

        Assert.IsTrue(plan.Items[0].Skipped);
        Assert.IsNotNull(plan.Items[0].SkipReason);
        Assert.IsFalse(plan.Items[1].Skipped);
        Assert.AreEqual(4, plan.Items[1].GainDb, 1e-9);
    }

    [TestMethod]
    public void Album_UsesOneGainAndCapsItAtTheLoudestPeak()
    {
        // Equal durations: album loudness is the energy mean of -20 and -22 LUFS, about -20.886.
        // Raw gain would be +6.886 dB, but the loudest true peak (-1.5 dBTP) leaves only +0.5 dB under a -1 dBTP ceiling.
        var plan = LoudnessPlanner.Create([Track("a", -20, 10, -1.5), Track("b", -22, 10, -6)], LoudnessMode.Album, -14, -1);

        Assert.AreEqual(-20.886, plan.AlbumLufs!.Value, 0.01);
        Assert.AreEqual(0.5, plan.AlbumGainDb!.Value, 1e-9);
        Assert.IsTrue(plan.GainCapped);
        Assert.IsTrue(plan.Items.All(item => Math.Abs(item.GainDb - 0.5) < 1e-9 && item.PeakLimited));
    }

    [TestMethod]
    public void Album_KeepsTheRawGainWhenEveryPeakHasRoom()
    {
        var plan = LoudnessPlanner.Create([Track("a", -20, 10, -8), Track("b", -20, 10, -8)], LoudnessMode.Album, -14, -1);

        Assert.AreEqual(6, plan.AlbumGainDb!.Value, 1e-9);
        Assert.IsFalse(plan.GainCapped);
        Assert.IsTrue(plan.Items.All(item => Math.Abs(item.GainDb - 6) < 1e-9 && !item.PeakLimited));
    }

    [TestMethod]
    public void Album_FailsWhenNoFileIsAudible()
    {
        Assert.ThrowsException<ArgumentException>(() => LoudnessPlanner.Create(
            [Track("silence", double.NegativeInfinity, 10, double.NegativeInfinity)],
            LoudnessMode.Album,
            -14,
            -1));
    }

    [TestMethod]
    public void Create_RejectsTargetAndCeilingOutsideTheSupportedRange()
    {
        var tracks = new[] { Track("song", -20, 10, -8) };

        Assert.ThrowsException<ArgumentException>(() => LoudnessPlanner.Create(tracks, LoudnessMode.PerTrack, -5, -1));
        Assert.ThrowsException<ArgumentException>(() => LoudnessPlanner.Create(tracks, LoudnessMode.PerTrack, -37, -1));
        Assert.ThrowsException<ArgumentException>(() => LoudnessPlanner.Create(tracks, LoudnessMode.PerTrack, -14, 0.5));
        Assert.ThrowsException<ArgumentException>(() => LoudnessPlanner.Create(tracks, LoudnessMode.PerTrack, -14, -9.5));
        Assert.AreEqual(-36d, LoudnessPlanner.Create(tracks, LoudnessMode.PerTrack, -36, -9).TargetLufs);
        Assert.AreEqual(-6d, LoudnessPlanner.Create(tracks, LoudnessMode.PerTrack, -6, 0).TargetLufs);
    }

    private static LoudnessTrack Track(string name, double lufs, double seconds, double truePeak = -3) =>
        new(name, name + ".flac", seconds, lufs, truePeak);
}
