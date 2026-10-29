using AudioConverter.Core.Models;
using AudioConverter.Infrastructure.Normalize;
using AudioConverter.Infrastructure.Remix;

namespace AudioConverter.Infrastructure.Tests;

[TestClass]
public sealed class NormalizeCommandBuilderTests
{
    // Pass-one values: integrated, true peak, loudness range, gate threshold, and loudnorm's target_offset.
    private static readonly LoudnessMeasurements Measured = new(-20.1, -1.2, 6.5, -30.4, 0.04);

    [TestMethod]
    public void BuildMeasure_RunsLoudnormJsonWithTheTargetAndWritesNoAudio()
    {
        var args = NormalizeCommandBuilder.BuildMeasure("in.flac", -16, -1.5);

        AssertArgumentPair(args, "-af", "loudnorm=I=-16:TP=-1.5:LRA=11:print_format=json");
        AssertArgumentPair(args, "-f", "null");
        CollectionAssert.Contains(args.ToList(), "-i");
    }

    [TestMethod]
    public void BuildTrackPass_UsesMeasuredValuesAndTheFirstPassOffset()
    {
        var encoding = new NormalizeEncoding(AudioFormat.Flac, "flac", 16, 0, false, false);

        var args = NormalizeCommandBuilder.BuildTrackPass("in.flac", "out.flac", encoding, -14, -1, Measured);

        var filter = ArgumentAfter(args, "-af");
        StringAssert.Contains(filter, "loudnorm=I=-14:TP=-1:LRA=11:");
        StringAssert.Contains(filter, "measured_I=-20.1:measured_TP=-1.2:measured_LRA=6.5:measured_thresh=-30.4:");
        StringAssert.Contains(filter, "offset=0.04:linear=true");
        Assert.AreEqual("out.flac", args[^1]);
    }

    [TestMethod]
    public void BuildTrackPass_UsesZeroOffsetWhenFirstPassReportedNone()
    {
        var encoding = new NormalizeEncoding(AudioFormat.Flac, "flac", 16, 0, false, false);
        var infinite = new LoudnessMeasurements(-20.1, -1.2, 6.5, -30.4, double.PositiveInfinity);

        var filter = ArgumentAfter(NormalizeCommandBuilder.BuildTrackPass("in.flac", "out.flac", encoding, -14, -1, infinite), "-af");

        StringAssert.Contains(filter, "offset=0:linear=true");
    }

    [TestMethod]
    public void BuildTrackPass_Mp3KeepsSourceBitrateMetadataAndCoverArt()
    {
        var encoding = new NormalizeEncoding(AudioFormat.Mp3, "mp3", null, 256, true, true);

        var args = NormalizeCommandBuilder.BuildTrackPass("in.mp3", "out.mp3", encoding, -14, -1, Measured);

        AssertArgumentPair(args, "-c:a", "libmp3lame");
        AssertArgumentPair(args, "-b:a", "256k");
        AssertArgumentPair(args, "-map_metadata", "0");
        CollectionAssert.Contains(args.ToList(), "0:v:disp:attached_pic?");
        AssertArgumentPair(args, "-c:v", "mjpeg");
        AssertArgumentPair(args, "-disposition:v:0", "attached_pic");
    }

    [TestMethod]
    public void BuildAlbumPass_AppliesGainThenLimitsAtTheCeiling()
    {
        var encoding = new NormalizeEncoding(AudioFormat.Mp3, "mp3", null, 320, false, true);

        var args = NormalizeCommandBuilder.BuildAlbumPass("in.mp3", "out.mp3", encoding, -3.5, -1);

        // 10^(-1/20) is about 0.8913, the linear value of -1 dBTP.
        AssertArgumentPair(args, "-af", "volume=-3.5dB,alimiter=limit=0.8913:level=disabled");
    }

    [TestMethod]
    public void BuildAlbumPass_DropsMetadataAndArtworkWhenNotKept()
    {
        var encoding = new NormalizeEncoding(AudioFormat.Mp3, "mp3", null, 320, true, false);

        var args = NormalizeCommandBuilder.BuildAlbumPass("in.mp3", "out.mp3", encoding, 3, -1);

        AssertArgumentPair(args, "-map_metadata", "-1");
        CollectionAssert.Contains(args.ToList(), "-vn");
        CollectionAssert.DoesNotContain(args.ToList(), "0:v:disp:attached_pic?");
    }

    [TestMethod]
    public void BuildAlbumPass_KeepsLosslessSourcesLossless()
    {
        var flac = NormalizeCommandBuilder.BuildAlbumPass("in.flac", "out.flac", new NormalizeEncoding(AudioFormat.Flac, "flac", 24, 900, true, true), 1, -1);
        AssertArgumentPair(flac, "-c:a", "flac");
        AssertArgumentPair(flac, "-bits_per_raw_sample", "24");
        CollectionAssert.DoesNotContain(flac.ToList(), "-b:a");

        var alac = NormalizeCommandBuilder.BuildAlbumPass("in.m4a", "out.m4a", new NormalizeEncoding(AudioFormat.M4a, "alac", 16, 320, false, true), 1, -1);
        AssertArgumentPair(alac, "-c:a", "alac");
        CollectionAssert.DoesNotContain(alac.ToList(), "-b:a");

        var oggFlac = NormalizeCommandBuilder.BuildAlbumPass("in.ogg", "out.ogg", new NormalizeEncoding(AudioFormat.Ogg, "flac", 16, 0, false, true), 1, -1);
        AssertArgumentPair(oggFlac, "-c:a", "flac");
    }

    [TestMethod]
    public void BuildAlbumPass_EncodesWavAtSourceBitDepth()
    {
        var args = NormalizeCommandBuilder.BuildAlbumPass("in.wav", "out.wav", new NormalizeEncoding(AudioFormat.Wav, "pcm_s24le", 24, 0, false, true), 1, -1);

        AssertArgumentPair(args, "-c:a", "pcm_s24le");
    }

    [TestMethod]
    public void IsLossy_FollowsTheCodecInsideTheContainer()
    {
        Assert.IsFalse(new NormalizeEncoding(AudioFormat.M4a, "alac", 16, 0, false, false).IsLossy);
        Assert.IsTrue(new NormalizeEncoding(AudioFormat.M4a, "aac", null, 256, false, false).IsLossy);
        Assert.IsFalse(new NormalizeEncoding(AudioFormat.Ogg, "flac", 16, 0, false, false).IsLossy);
        Assert.IsTrue(new NormalizeEncoding(AudioFormat.Ogg, "vorbis", null, 192, false, false).IsLossy);
        Assert.IsFalse(new NormalizeEncoding(AudioFormat.Wav, "pcm_s16le", 16, 0, false, false).IsLossy);
    }

    [TestMethod]
    public void FromProbe_UsesSourceBitrateWithinEncoderLimits()
    {
        Assert.AreEqual(320, NormalizeEncoding.FromProbe(AudioFormat.Mp3, Probe(null), true).BitrateKbps);
        Assert.AreEqual(192, NormalizeEncoding.FromProbe(AudioFormat.Mp3, Probe(192), true).BitrateKbps);
        Assert.AreEqual(320, NormalizeEncoding.FromProbe(AudioFormat.Mp3, Probe(900), true).BitrateKbps);
        Assert.AreEqual(32, NormalizeEncoding.FromProbe(AudioFormat.Mp3, Probe(16), true).BitrateKbps);
    }

    private static ProbeInfo Probe(int? bitrateKbps) =>
        new(180, "mp3", 44100, null, false, new Dictionary<string, string>(), AudioBitrateKbps: bitrateKbps);

    private static string ArgumentAfter(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        Assert.IsTrue(index >= 0, $"Missing {name}");
        return args[index + 1];
    }

    private static void AssertArgumentPair(string[] args, string name, string expected) =>
        Assert.AreEqual(expected, ArgumentAfter(args, name));
}
