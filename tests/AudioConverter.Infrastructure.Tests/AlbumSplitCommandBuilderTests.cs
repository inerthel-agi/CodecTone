using AudioConverter.Core.Models;
using AudioConverter.Infrastructure.Split;

namespace AudioConverter.Infrastructure.Tests;

[TestClass]
public sealed class AlbumSplitCommandBuilderTests
{
    [TestMethod]
    public void BuildSilenceDetect_UsesThresholdAndDurationAndWritesNothing()
    {
        var args = AlbumSplitCommandBuilder.BuildSilenceDetect("in.flac", -45, 1.5);

        var filter = args[Array.IndexOf(args, "-af") + 1];
        Assert.AreEqual("silencedetect=noise=-45dB:d=1.5", filter);
        Assert.AreEqual("-", args[^1]);
        CollectionAssert.Contains(args, "null");
    }

    [TestMethod]
    public void BuildTrack_CutsTheRangeWritesTagsAndDropsChapters()
    {
        var tags = new AlbumTrackTags("Second; \"Song\"", "The Band", "Live", "The Band", 2, 12);
        var args = AlbumSplitCommandBuilder.BuildTrack(
            "in.flac", "out.flac", new ConversionOptions(AudioFormat.Flac), TrimSelection.Create(10, 20), tags, false);

        Assert.AreEqual("atrim=start=10.000:end=20.000,asetpts=PTS-STARTPTS", args[Array.IndexOf(args, "-af") + 1]);
        CollectionAssert.Contains(args, "title=Second; \"Song\"");
        CollectionAssert.Contains(args, "album_artist=The Band");
        CollectionAssert.Contains(args, "track=2/12");
        Assert.AreEqual("-map_chapters", args[Array.IndexOf(args, "-map_chapters")]);
        Assert.AreEqual("-1", args[Array.IndexOf(args, "-map_chapters") + 1]);
        Assert.AreEqual("out.flac", args[^1]);
        Assert.IsTrue(Array.IndexOf(args, "-metadata") < args.Length - 1, "Metadata must come before the output path.");
    }
}
