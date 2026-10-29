using AudioConverter.Core.Models;
using AudioConverter.Core.Split;

namespace AudioConverter.Core.Tests;

[TestClass]
public sealed class TrackFileNamerTests
{
    [TestMethod]
    public void Build_PadsTheTrackNumberAndRemovesForbiddenCharacters()
    {
        var track = new AlbumTrack(3, "Ca/rol: *Song?", "", 0, 10);

        Assert.AreEqual("03 - Carol Song.flac", TrackFileNamer.Build(TrackFileNamer.DefaultPattern, track, AudioFormat.Flac));
    }

    [TestMethod]
    public void Build_SupportsWiderPaddingAndPlainNumbers()
    {
        var track = new AlbumTrack(12, "Title", "", 0, 10);

        Assert.AreEqual("012 Title.mp3", TrackFileNamer.Build("{track:000} {title}", track, AudioFormat.Mp3));
        Assert.AreEqual("12.wav", TrackFileNamer.Build("{track}", track, AudioFormat.Wav));
    }

    [TestMethod]
    public void Build_DoesNotExpandTokensInsideTitles()
    {
        // The colon is an invalid file-name character and is removed; the braces must not be expanded again.
        var track = new AlbumTrack(1, "{track:00}", "", 0, 10);

        Assert.AreEqual("01 - {track00}.ogg", TrackFileNamer.Build(TrackFileNamer.DefaultPattern, track, AudioFormat.Ogg));
    }

    [TestMethod]
    public void Build_RejectsPatternThatGivesAnEmptyName()
    {
        Assert.ThrowsException<ArgumentException>(() => TrackFileNamer.Build("///", new AlbumTrack(1, "x", "", 0, 1), AudioFormat.Flac));
    }
}
