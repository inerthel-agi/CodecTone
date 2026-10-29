using AudioConverter.Core.Split;

namespace AudioConverter.Core.Tests;

[TestClass]
public sealed class CueSheetParserTests
{
    private const string Sample = """
        REM GENRE "Rock"
        REM DATE 1999
        PERFORMER "The Band"
        TITLE "Live: The Album"
        FILE "album.flac" WAVE
          TRACK 01 AUDIO
            TITLE "Opening Song"
            PERFORMER "The Band"
            INDEX 00 00:00:00
            INDEX 01 00:02:10
          TRACK 02 AUDIO
            TITLE Second
            INDEX 01 04:30:40
        """;

    [TestMethod]
    public void Parse_ReadsDiscAndTrackValues_IgnoringRemAndPregap()
    {
        var sheet = CueSheetParser.Parse(Sample.Replace("\n", "\r\n"));

        Assert.AreEqual("Live: The Album", sheet.Title);
        Assert.AreEqual("The Band", sheet.Performer);
        Assert.AreEqual(2, sheet.Tracks.Count);
        Assert.AreEqual("Opening Song", sheet.Tracks[0].Title);
        Assert.AreEqual("The Band", sheet.Tracks[0].Performer);
        Assert.AreEqual(2 + 10d / 75, sheet.Tracks[0].StartSeconds, 1e-9);
        Assert.AreEqual("Second", sheet.Tracks[1].Title);
        Assert.AreEqual(270 + 40d / 75, sheet.Tracks[1].StartSeconds, 1e-9);
    }

    [TestMethod]
    public void Parse_AcceptsByteOrderMark()
    {
        var sheet = CueSheetParser.Parse("﻿PERFORMER \"Artist\"\nFILE \"a.wav\" WAVE\nTRACK 01 AUDIO\nINDEX 01 00:00:00\n");

        Assert.AreEqual("Artist", sheet.Performer);
    }

    [TestMethod]
    public void ToAlbumTracks_EndsEachTrackAtTheNextStartAndTheLastAtTheSourceEnd()
    {
        var tracks = CueSheetParser.Parse(Sample).ToAlbumTracks(600);

        Assert.AreEqual(2, tracks.Count);
        Assert.AreEqual(270 + 40d / 75, tracks[0].EndSeconds, 1e-9);
        Assert.AreEqual(270 + 40d / 75, tracks[1].StartSeconds, 1e-9);
        Assert.AreEqual(600d, tracks[1].EndSeconds, 1e-9);
    }

    [TestMethod]
    public void Parse_RejectsSheetsWithSeveralFileEntries()
    {
        const string text = "FILE \"a.wav\" WAVE\nTRACK 01 AUDIO\nINDEX 01 00:00:00\nFILE \"b.wav\" WAVE\nTRACK 02 AUDIO\nINDEX 01 00:10:00\n";

        var error = Assert.ThrowsException<NotSupportedException>(() => CueSheetParser.Parse(text));
        StringAssert.Contains(error.Message, "several FILE entries");
    }

    [TestMethod]
    public void Parse_RejectsTrackWithoutIndex01()
    {
        const string text = "FILE \"a.wav\" WAVE\nTRACK 01 AUDIO\nINDEX 00 00:00:00\n";

        Assert.ThrowsException<FormatException>(() => CueSheetParser.Parse(text));
    }

    [TestMethod]
    public void Parse_RejectsInvalidFrameCount()
    {
        const string text = "FILE \"a.wav\" WAVE\nTRACK 01 AUDIO\nINDEX 01 00:00:75\n";

        Assert.ThrowsException<FormatException>(() => CueSheetParser.Parse(text));
    }

    [TestMethod]
    public void ToAlbumTracks_RejectsTrackStartingAfterTheAudio()
    {
        var sheet = CueSheetParser.Parse("FILE \"a.wav\" WAVE\nTRACK 01 AUDIO\nINDEX 01 00:00:00\nTRACK 02 AUDIO\nINDEX 01 05:00:00\n");

        Assert.ThrowsException<InvalidDataException>(() => sheet.ToAlbumTracks(120));
    }
}
