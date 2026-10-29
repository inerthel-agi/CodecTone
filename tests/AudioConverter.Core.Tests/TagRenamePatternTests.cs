using AudioConverter.Core.Tags;

namespace AudioConverter.Core.Tests;

[TestClass]
public sealed class TagRenamePatternTests
{
    private static readonly AudioTags Song = new("Song: Live?", "Artist", "Album", "Album Artist", "3/12", "1", "2024", "Rock");
    private static readonly AudioTags Empty = new("", "", "", "", "", "", "", "");

    [TestMethod]
    public void FileName_DefaultPatternZeroPadsTrackAndKeepsExtension()
    {
        Assert.AreEqual("03 - Song Live.mp3", TagRenamePattern.FileName(TagRenamePattern.DefaultPattern, Song, "old.mp3"));
    }

    [TestMethod]
    public void FileName_TokensAreCaseInsensitiveAndPlainTrackIsNotPadded()
    {
        Assert.AreEqual("3 Artist - Album Artist.FLAC", TagRenamePattern.FileName("{TRACK} {Artist} - {albumartist}", Song, "old.FLAC"));
    }

    [TestMethod]
    public void FileName_StripsInvalidCharactersAndCollapsesWhitespace()
    {
        var tags = new AudioTags("AC/DC  -  Back\tIn Black", "", "", "", "", "", "", "");
        Assert.AreEqual("ACDC - Back In Black.ogg", TagRenamePattern.FileName("{title}", tags, "x.ogg"));
    }

    [TestMethod]
    public void Stem_FallsBackToOriginalNameWhenNothingUsableRemains()
    {
        Assert.AreEqual("Track 01", TagRenamePattern.Stem("{title}", Empty, "Track 01.mp3"));
        Assert.AreEqual("Track 01.mp3", TagRenamePattern.FileName("{title} ..", Empty, "Track 01.mp3"));
    }

    [TestMethod]
    public void Stem_TrimsDotsAndSpaces()
    {
        Assert.AreEqual("End", TagRenamePattern.Stem("{title}", new AudioTags("End. ", "", "", "", "", "", "", ""), "x.mp3"));
    }

    [TestMethod]
    public void TrackPadding_ReadsLeadingNumberAndLeavesOtherValuesAlone()
    {
        Assert.AreEqual("A3", TagRenamePattern.Stem("{track:00}", new AudioTags("", "", "", "", "A3", "", "", ""), "x.mp3"));
        Assert.AreEqual("007", TagRenamePattern.Stem("{track:000}", new AudioTags("", "", "", "", "7/12", "", "", ""), "x.mp3"));
    }

    [TestMethod]
    public void UnknownTokens_StayLiteral()
    {
        Assert.AreEqual("{composer} - X.mp3", TagRenamePattern.FileName("{composer} - {title}", new AudioTags("X", "", "", "", "", "", "", ""), "y.mp3"));
    }

    [TestMethod]
    public void PlanTargets_NumbersDuplicatesAndNamesTakenOnDisk()
    {
        var root = Path.Combine(Path.GetTempPath(), "codectone-tags");
        var sources = new[]
        {
            new TagRenameSource(Path.Combine(root, "a.mp3"), new AudioTags("X", "", "", "", "", "", "", "")),
            new TagRenameSource(Path.Combine(root, "b.mp3"), new AudioTags("X", "", "", "", "", "", "", "")),
            new TagRenameSource(Path.Combine(root, "c.mp3"), new AudioTags("Y", "", "", "", "", "", "", "")),
        };
        var onDisk = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.Combine(root, "Y.mp3") };

        var targets = TagRenamePattern.PlanTargets("{title}", sources, onDisk.Contains);

        CollectionAssert.AreEqual(
            new[] { Path.Combine(root, "X.mp3"), Path.Combine(root, "X (2).mp3"), Path.Combine(root, "Y (2).mp3") },
            targets.ToArray());
    }

    [TestMethod]
    public void PlanTargets_KeepsOwnNameAndAllowsCaseOnlyRename()
    {
        var root = Path.Combine(Path.GetTempPath(), "codectone-tags");
        var same = new TagRenameSource(Path.Combine(root, "x.mp3"), new AudioTags("x", "", "", "", "", "", "", ""));
        var caseOnly = new TagRenameSource(Path.Combine(root, "song.mp3"), new AudioTags("Song", "", "", "", "", "", "", ""));
        var onDisk = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { same.FilePath, caseOnly.FilePath };

        var targets = TagRenamePattern.PlanTargets("{title}", [same, caseOnly], onDisk.Contains);

        Assert.AreEqual(same.FilePath, targets[0]);
        Assert.AreEqual(Path.Combine(root, "Song.mp3"), targets[1]);
    }

    [TestMethod]
    public void PlanTargets_DoesNotShareNamesAcrossFolders()
    {
        var first = Path.Combine(Path.GetTempPath(), "codectone-a");
        var second = Path.Combine(Path.GetTempPath(), "codectone-b");
        var sources = new[]
        {
            new TagRenameSource(Path.Combine(first, "1.mp3"), new AudioTags("Same", "", "", "", "", "", "", "")),
            new TagRenameSource(Path.Combine(second, "2.mp3"), new AudioTags("Same", "", "", "", "", "", "", "")),
        };

        var targets = TagRenamePattern.PlanTargets("{title}", sources, _ => false);

        CollectionAssert.AreEqual(new[] { Path.Combine(first, "Same.mp3"), Path.Combine(second, "Same.mp3") }, targets.ToArray());
    }
}
