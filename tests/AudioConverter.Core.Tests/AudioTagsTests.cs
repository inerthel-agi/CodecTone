using AudioConverter.Core.Tags;

namespace AudioConverter.Core.Tests;

[TestClass]
public sealed class AudioTagsTests
{
    [TestMethod]
    public void FromProbe_ReadsAliasesAndFallsBackToYear()
    {
        var probed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["TITLE"] = "Song",
            ["ALBUMARTIST"] = "Band",
            ["YEAR"] = "1999",
            ["track"] = "5",
        };

        var tags = AudioTags.FromProbe(probed);

        Assert.AreEqual("Song", tags.Title);
        Assert.AreEqual("Band", tags.AlbumArtist);
        Assert.AreEqual("1999", tags.Date);
        Assert.AreEqual("5", tags.Track);
        Assert.AreEqual("", tags.Genre);
    }

    [TestMethod]
    public void ChangesFrom_ReturnsOnlyEditedFieldsAndClearsAliasKeys()
    {
        var original = new AudioTags("Song", "Band", "", "Band", "", "", "", "");
        var edited = original with { Title = "New", AlbumArtist = "" };

        var changes = edited.ChangesFrom(original);

        CollectionAssert.AreEqual(
            new[]
            {
                new KeyValuePair<string, string>("title", "New"),
                new KeyValuePair<string, string>("albumartist", ""),
                new KeyValuePair<string, string>("album_artist", ""),
            },
            changes.ToArray());
    }

    [TestMethod]
    public void ChangesFrom_SetsValueAfterClearingAlias()
    {
        var original = new AudioTags("", "", "", "Old", "", "", "", "");
        var edited = original with { AlbumArtist = " New " };

        var changes = edited.ChangesFrom(original);

        CollectionAssert.AreEqual(
            new[]
            {
                new KeyValuePair<string, string>("albumartist", ""),
                new KeyValuePair<string, string>("album_artist", "New"),
            },
            changes.ToArray());
    }

    [TestMethod]
    public void ChangesFrom_IgnoresWhitespaceOnlyEdits()
    {
        var original = new AudioTags("Song", "", "", "", "", "", "", "");

        Assert.AreEqual(0, (original with { Title = "  Song " }).ChangesFrom(original).Count);
    }

    [TestMethod]
    public void ChangesFrom_DateClearsYearAliasBeforeWritingDate()
    {
        var original = new AudioTags("", "", "", "", "", "", "1999", "");

        var changes = (original with { Date = "2020" }).ChangesFrom(original);

        CollectionAssert.AreEqual(
            new[]
            {
                new KeyValuePair<string, string>("year", ""),
                new KeyValuePair<string, string>("date", "2020"),
            },
            changes.ToArray());
    }
}
