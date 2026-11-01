using AudioConverter.Core.Models;
using AudioConverter.Core.Remix;
using AudioConverter.Infrastructure.Ffmpeg;
using AudioConverter.Infrastructure.Remix;
using System.Globalization;
namespace AudioConverter.Infrastructure.Tests;
[TestClass]
public sealed class PreviewFadeIntegrationTests
{
    [TestMethod]
    [TestCategory("RequiresFfmpeg")]
    public async Task PreviewFades_MatchFullExportAtBeginningMiddleAndEnd()
    {
        FfmpegTools tools;
        try { tools = FfmpegLocator.Find(); }
        catch (FfmpegDependencyException) { Assert.Inconclusive("FFmpeg is not available for integration testing."); return; }
        var runner = new FfmpegProcessRunner();
        var root = Path.Combine(Path.GetTempPath(), $"fade-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "source.wav");
            var full = Path.Combine(root, "full.wav");
            await runner.RunAsync(tools.FfmpegPath, ["-v","error","-y","-f","lavfi","-i","sine=frequency=440:duration=6","-ac","2",source], 6);
            RemixEffect[] rack = [new TempoPitchEffect(2),new FadeInEffect(0.5),new FadeOutEffect(0.5)];
            await runner.RunAsync(tools.FfmpegPath, RemixCommandBuilder.BuildExport(source, full, rack,
                new ConversionOptions(AudioFormat.Wav),new RemixMetadata(),44100,6,false),3);
            foreach (var start in new[] { 0d, 2d, 5d })
            {
                var preview = Path.Combine(root,"preview.wav");
                var expected = Path.Combine(root,"expected.pcm");
                var actual = Path.Combine(root,"actual.pcm");
                await runner.RunAsync(tools.FfmpegPath,RemixCommandBuilder.BuildPreview(source,preview,rack,44100,6,start,1),0.5);
                var trim = $"atrim=start={(start/2).ToString(CultureInfo.InvariantCulture)}:end={((start+1)/2).ToString(CultureInfo.InvariantCulture)},asetpts=PTS-STARTPTS";
                await runner.RunAsync(tools.FfmpegPath,["-v","error","-y","-i",full,"-af",trim,"-f","s16le",expected],0.5);
                await runner.RunAsync(tools.FfmpegPath,["-v","error","-y","-i",preview,"-f","s16le",actual],0.5);
                CollectionAssert.AreEqual(await File.ReadAllBytesAsync(expected),await File.ReadAllBytesAsync(actual), $"Mismatch at source offset {start}");
            }
        }
        finally { Directory.Delete(root,true); }
    }
}
