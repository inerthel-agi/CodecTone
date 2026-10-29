using AudioConverter.Core.Models;
using AudioConverter.Infrastructure.Tags;

namespace AudioConverter.Infrastructure.Tests;

[TestClass]
public sealed class TagWriteCommandBuilderTests
{
    [TestMethod]
    public void Build_CopiesEveryStreamAndKeepsExistingMetadata()
    {
        var args = TagWriteCommandBuilder.Build("song.flac", "staged.flac", AudioFormat.Flac, []);

        AssertArgumentPair(args, "-map", "0");
        AssertArgumentPair(args, "-c", "copy");
        AssertArgumentPair(args, "-map_metadata", "0");
        Assert.AreEqual("staged.flac", args[^1]);
        CollectionAssert.DoesNotContain(args, "-id3v2_version");
    }

    [TestMethod]
    public void Build_Mp3WritesId3v23()
    {
        var args = TagWriteCommandBuilder.Build("song.mp3", "staged.mp3", AudioFormat.Mp3, []);

        AssertArgumentPair(args, "-id3v2_version", "3");
    }

    [TestMethod]
    public void Build_PassesEachChangeAsOneMetadataArgument()
    {
        var args = TagWriteCommandBuilder.Build(
            "song.m4a",
            "staged.m4a",
            AudioFormat.M4a,
            [new KeyValuePair<string, string>("artist", ""), new KeyValuePair<string, string>("title", "New Title")]);

        var list = args.ToList();
        var metadata = Enumerable.Range(0, list.Count).Where(index => list[index] == "-metadata").Select(index => list[index + 1]).ToArray();
        CollectionAssert.AreEqual(new[] { "artist=", "title=New Title" }, metadata);
    }

    private static void AssertArgumentPair(IReadOnlyList<string> args, string key, string value)
    {
        var index = args.ToList().IndexOf(key);
        Assert.IsTrue(index >= 0, $"Missing argument: {key}");
        Assert.AreEqual(value, args[index + 1]);
    }
}
