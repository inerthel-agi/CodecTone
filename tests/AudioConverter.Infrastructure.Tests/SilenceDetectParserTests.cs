using AudioConverter.Infrastructure.Split;

namespace AudioConverter.Infrastructure.Tests;

[TestClass]
public sealed class SilenceDetectParserTests
{
    [TestMethod]
    public void Parse_PairsStartAndEndLinesAndClosesTrailingSilenceAtTheEnd()
    {
        const string stderr = """
            Input #0, flac, from 'album.flac':
              Duration: 00:10:00.00, start: 0.000000, bitrate: 900 kb/s
            [silencedetect @ 0x55d5c8a0c600] silence_start: 0
            [silencedetect @ 0x55d5c8a0c600] silence_end: 2.14704 | silence_duration: 2.14704
            size=N/A time=00:01:40.00 bitrate=N/A speed=  60x
            [silencedetect @ 0x55d5c8a0c600] silence_start: 214.323
            [silencedetect @ 0x55d5c8a0c600] silence_end: 216.905 | silence_duration: 2.58196
            [silencedetect @ 0x55d5c8a0c600] silence_start: 580.5
            """;

        var silences = SilenceDetectParser.Parse(stderr, 600);

        Assert.AreEqual(3, silences.Count);
        Assert.AreEqual(0d, silences[0].StartSeconds, 1e-9);
        Assert.AreEqual(2.14704, silences[0].EndSeconds, 1e-9);
        Assert.AreEqual(214.323, silences[1].StartSeconds, 1e-9);
        Assert.AreEqual(216.905, silences[1].EndSeconds, 1e-9);
        Assert.AreEqual(580.5, silences[2].StartSeconds, 1e-9);
        Assert.AreEqual(600d, silences[2].EndSeconds, 1e-9);
    }

    [TestMethod]
    public void Parse_ReadsScientificNotationAndIgnoresOutputWithoutSilence()
    {
        const string stderr = "size=N/A time=00:00:03.00\n[silencedetect @ 0x1] silence_start: 1.5e-05\n[silencedetect @ 0x1] silence_end: 1.25 | silence_duration: 1.25\n";

        var silences = SilenceDetectParser.Parse(stderr, 3);

        Assert.AreEqual(1, silences.Count);
        Assert.AreEqual(1.5e-05, silences[0].StartSeconds, 1e-12);
        Assert.AreEqual(1.25, silences[0].EndSeconds, 1e-9);
        Assert.AreEqual(0, SilenceDetectParser.Parse("no silence lines here", 3).Count);
    }
}
