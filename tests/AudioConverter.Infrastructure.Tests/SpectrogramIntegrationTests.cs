using System.Buffers.Binary;
using AudioConverter.Infrastructure.Ffmpeg;
using AudioConverter.Infrastructure.Inspection;

namespace AudioConverter.Infrastructure.Tests;

[TestClass]
public sealed class SpectrogramIntegrationTests
{
    [TestMethod]
    [TestCategory("RequiresFfmpeg")]
    public async Task RenderAsync_WritesPngAtTheRequestedSize()
    {
        FfmpegTools tools;
        try { tools = FfmpegLocator.Find(); }
        catch (FfmpegDependencyException) { Assert.Inconclusive("FFmpeg is not available for integration testing."); return; }

        var root = Path.Combine(Path.GetTempPath(), $"codectone-spectrum-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(root);
            var input = Path.Combine(root, "tone.wav");
            await new FfmpegProcessRunner().RunAsync(
                tools.FfmpegPath,
                ["-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=440:duration=1", "-c:a", "pcm_s16le", input],
                1);
            var output = Path.Combine(root, "analysis", "spectrum.png");

            await new SpectrogramService(tools).RenderAsync(input, output, 1024, 512, legend: false, durationSeconds: 1);

            var png = await File.ReadAllBytesAsync(output);
            CollectionAssert.AreEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, png[..8]);
            Assert.AreEqual(1024, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)));
            Assert.AreEqual(512, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
