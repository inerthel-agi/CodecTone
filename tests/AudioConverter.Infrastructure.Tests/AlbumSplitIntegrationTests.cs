using AudioConverter.Core.Models;
using AudioConverter.Core.Split;
using AudioConverter.Infrastructure.Ffmpeg;
using AudioConverter.Infrastructure.Split;

namespace AudioConverter.Infrastructure.Tests;

[TestClass]
public sealed class AlbumSplitIntegrationTests
{
    [TestMethod]
    [TestCategory("RequiresFfmpeg")]
    public async Task SplitsAtSilenceAndWritesTrackTags()
    {
        FfmpegTools tools;
        try { tools = FfmpegLocator.Find(); }
        catch (FfmpegDependencyException) { Assert.Inconclusive("FFmpeg is not available for integration testing."); return; }

        var root = Path.Combine(Path.GetTempPath(), $"codectone-split-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            // 3 s tone, 2 s digital silence, 3 s tone; the source carries an album tag that the split must not keep.
            var source = Path.Combine(root, "album.wav");
            await new FfmpegProcessRunner().RunAsync(
                tools.FfmpegPath,
                ["-hide_banner", "-loglevel", "error", "-y",
                 "-f", "lavfi", "-i", "sine=frequency=440:duration=3",
                 "-f", "lavfi", "-i", "anullsrc=r=44100:cl=mono:d=2",
                 "-f", "lavfi", "-i", "sine=frequency=660:duration=3",
                 "-filter_complex", "[0][1][2]concat=n=3:v=0:a=1",
                 "-metadata", "album=Source Album", "-c:a", "pcm_s16le", source],
                8);

            var service = new AlbumSplitService(tools);
            var silences = await service.DetectSilenceAsync(source, -45, 0.5, 8);
            Assert.AreEqual(1, silences.Count);
            Assert.AreEqual(3.0, silences[0].StartSeconds, 0.2);
            Assert.AreEqual(5.0, silences[0].EndSeconds, 0.2);

            var tracks = SilenceSplitPlanner.Plan(silences, 8, minimumTrackSeconds: 1);
            Assert.AreEqual(2, tracks.Count);

            var output = Path.Combine(root, "out");
            Directory.CreateDirectory(output);
            var options = new ConversionOptions(AudioFormat.Flac);
            for (var index = 0; index < tracks.Count; index++)
            {
                var tags = new AlbumTrackTags($"Song {index + 1}", "Band", "", "Band", index + 1, tracks.Count);
                await service.WriteTrackAsync(source, Path.Combine(output, $"{index + 1:00}.flac"), options, tracks[index], tags);
            }

            var probe = new FfprobeService(tools.FfprobePath);
            var first = await probe.ProbeAsync(Path.Combine(output, "01.flac"));
            Assert.AreEqual(4, first.DurationSeconds, 0.1);
            Assert.AreEqual("Song 1", first.Tags["title"]);
            Assert.AreEqual("Band", first.Tags["album_artist"]);
            Assert.AreEqual("1/2", first.Tags["track"]);
            Assert.IsFalse(first.Tags.TryGetValue("album", out var album) && album == "Source Album", "The source album tag leaked into the track.");

            // Without overwrite, an existing track is left untouched.
            var existing = Path.Combine(output, "01.flac");
            var before = await File.ReadAllBytesAsync(existing);
            var tag = new AlbumTrackTags("Again", "Band", "", "Band", 1, 2);
            await Assert.ThrowsExceptionAsync<IOException>(() => service.WriteTrackAsync(source, existing, options, tracks[0], tag));
            CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(existing));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
