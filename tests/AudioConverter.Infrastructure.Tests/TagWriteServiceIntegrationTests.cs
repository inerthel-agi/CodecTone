using System.Security.Cryptography;
using AudioConverter.Infrastructure.Ffmpeg;
using AudioConverter.Infrastructure.Tags;

namespace AudioConverter.Infrastructure.Tests;

[TestClass]
public sealed class TagWriteServiceIntegrationTests
{
    [TestMethod]
    [TestCategory("RequiresFfmpeg")]
    public async Task WriteAsync_UpdatesTagsAndKeepsCoverArt()
    {
        var (tools, root) = Prepare();
        try
        {
            var source = Path.Combine(root, "song.mp3");
            await CreateMp3WithCoverAsync(tools, source);
            var service = new TagWriteService(tools);

            await service.WriteAsync(source,
                [new KeyValuePair<string, string>("title", "Renamed"), new KeyValuePair<string, string>("artist", ""), new KeyValuePair<string, string>("date", "2024")],
                hasCoverArt: true,
                durationSeconds: 1);

            var info = await new FfprobeService(tools.FfprobePath).ProbeAsync(source);
            Assert.AreEqual("Renamed", info.Tags["title"]);
            Assert.AreEqual("2024", info.Tags["date"]);
            Assert.IsFalse(info.Tags.ContainsKey("artist"));
            Assert.IsTrue(info.HasCoverArt);
            Assert.AreEqual(0, Directory.GetFiles(root, "*.tmp*").Length);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    [TestCategory("RequiresFfmpeg")]
    public async Task WriteAsync_LostCoverArtLeavesOriginalUntouched()
    {
        var (tools, root) = Prepare();
        try
        {
            var source = Path.Combine(root, "tone.wav");
            await new FfmpegProcessRunner().RunAsync(tools.FfmpegPath,
                ["-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=440:duration=1", source], 1);
            var before = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(source)));

            // WAV has no attached picture, so claiming there was cover art must fail validation.
            await Assert.ThrowsExceptionAsync<InvalidDataException>(() => new TagWriteService(tools).WriteAsync(
                source, [new KeyValuePair<string, string>("title", "X")], hasCoverArt: true, durationSeconds: 1));

            Assert.AreEqual(before, Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(source))));
            Assert.AreEqual(0, Directory.GetFiles(root, "*.tmp*").Length);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static (FfmpegTools Tools, string Root) Prepare()
    {
        FfmpegTools tools;
        try { tools = FfmpegLocator.Find(); }
        catch (FfmpegDependencyException) { Assert.Inconclusive("FFmpeg is not available for integration testing."); return default; }
        var root = Path.Combine(Path.GetTempPath(), $"codectone-tags-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return (tools, root);
    }

    private static Task CreateMp3WithCoverAsync(FfmpegTools tools, string path) => new FfmpegProcessRunner().RunAsync(tools.FfmpegPath,
    [
        "-hide_banner", "-loglevel", "error", "-y",
        "-f", "lavfi", "-i", "sine=frequency=440:duration=1",
        "-f", "lavfi", "-i", "color=c=red:s=64x64:r=1:d=1",
        "-map", "0:a", "-map", "1:v",
        "-c:a", "libmp3lame", "-b:a", "96k", "-c:v", "mjpeg", "-disposition:v", "attached_pic",
        "-metadata", "title=Original", "-metadata", "artist=Band", "-id3v2_version", "3",
        path,
    ], 1);
}
