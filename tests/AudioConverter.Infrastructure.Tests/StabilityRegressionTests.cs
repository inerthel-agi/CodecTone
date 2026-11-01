using AudioConverter.Core.Remix;
using AudioConverter.Core.Artwork;
using AudioConverter.Core.Models;
using AudioConverter.Infrastructure.Artwork;
using AudioConverter.Infrastructure.Ffmpeg;
using AudioConverter.Infrastructure.Remix;

namespace AudioConverter.Infrastructure.Tests;

[TestClass]
public sealed class StabilityRegressionTests
{
    [TestMethod]
    public async Task CancellationAfterValidation_DoesNotPublish()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var destination = Path.Combine(root, "out.wav");
        await File.WriteAllTextAsync(destination, "original");
        using var cts = new CancellationTokenSource();
        try
        {
            await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => TransactionalAudioOutput.RunAsync(
                Path.Combine(root, "in.wav"), destination, true,
                (stage, token) => File.WriteAllTextAsync(stage, "new", token),
                (_, _) => { cts.Cancel(); return Task.CompletedTask; }, cts.Token));
            Assert.AreEqual("original", await File.ReadAllTextAsync(destination));
            Assert.AreEqual(1, Directory.GetFiles(root).Length);
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task Remix_RejectsSourceDestinationBeforeStartingFfmpeg()
    {
        var service = new RemixProcessingService(new FfmpegTools("missing-ffmpeg", "missing-ffprobe"));
        await Assert.ThrowsExceptionAsync<IOException>(() => service.ExportAsync("same.wav", "same.wav", [],
            new ConversionOptions(AudioFormat.Wav, Overwrite: true), new RemixMetadata(), 44100, 10, false));
    }

    [DataTestMethod]
    [DataRow(0d)]
    [DataRow(50d)]
    [DataRow(95d)]
    public void Preview_AppliesFadesOnFullTimelineBeforeSelectingExcerpt(double start)
    {
        var args = RemixCommandBuilder.BuildPreview("in.wav", "out.wav",
            [new TempoPitchEffect(0.5), new FadeInEffect(5), new FadeOutEffect(5)], 44100, 100, start, 5);
        var filter = args[Array.IndexOf(args, "-af") + 1];
        Assert.IsTrue(filter.IndexOf("afade=t=out", StringComparison.Ordinal) < filter.IndexOf("atrim", StringComparison.Ordinal));
        StringAssert.Contains(filter, "atrim=start=" + (start / 0.5).ToString(System.Globalization.CultureInfo.InvariantCulture));
        StringAssert.Contains(filter, "afade=t=out:st=195:d=5");
    }

    [TestMethod]
    public void Distortion_RejectsFractionalOversampling()
    {
        Assert.ThrowsException<ArgumentException>(() => RemixRackValidator.Validate([new DistortionEffect(1, 0.1, 1.5)], 10));
    }

    [TestMethod]
    public void Distortion_PreservesSmallThreshold()
    {
        StringAssert.Contains(RemixFilterBuilder.Build([new DistortionEffect(1, 0.0001, 4)], 44100, 10, true), "threshold=0.0001");
    }

    [TestMethod]
    public void PreviewVolume_IsAppliedAfterEffectsAndNeverToExport()
    {
        RemixEffect[] rack = [new VolumeEffect(2)];
        var preview = RemixCommandBuilder.BuildPreview("in.wav", "preview.wav", rack, 44100, 10, 0, 2, previewGain: 0.25);
        StringAssert.EndsWith(preview[Array.IndexOf(preview, "-af") + 1], "volume=0.25");
        var export = RemixCommandBuilder.BuildExport("in.wav","export.wav",rack,new ConversionOptions(AudioFormat.Wav),new RemixMetadata(),44100,10,false);
        Assert.IsFalse(string.Join(' ',export).Contains("volume=0.25",StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task LockedDestination_RemainsIntactAndStageIsRemoved()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var destination = Path.Combine(root, "out.wav");
        await File.WriteAllTextAsync(destination, "original");
        try
        {
            using (var locked = new FileStream(destination,FileMode.Open,FileAccess.Read,FileShare.None))
            {
                try
                {
                    await TransactionalAudioOutput.RunAsync(Path.Combine(root,"source.wav"),destination,true,
                        (stage,token)=>File.WriteAllTextAsync(stage,"replacement",token),(_,_)=>Task.CompletedTask);
                    Assert.Fail("A locked destination must not be replaced.");
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            }
            Assert.AreEqual("original",await File.ReadAllTextAsync(destination));
            Assert.AreEqual(1,Directory.GetFiles(root).Length);
        }
        finally { Directory.Delete(root,true); }
    }

    [TestMethod]
    [TestCategory("RequiresFfmpeg")]
    public async Task CancelledRealEncoder_DoesNotPublishOrLeaveStagedFile()
    {
        FfmpegTools tools;
        try { tools = FfmpegLocator.Find(); }
        catch (FfmpegDependencyException) { Assert.Inconclusive("FFmpeg is not available for integration testing."); return; }
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        try
        {
            try
            {
                await TransactionalAudioOutput.RunAsync(Path.Combine(root,"input.wav"), Path.Combine(root,"out.wav"), false,
                    (stage,token) => new FfmpegProcessRunner().RunAsync(tools.FfmpegPath,
                        ["-v","error","-y","-re","-f","lavfi","-i","sine=duration=30","-progress","pipe:1",stage],30,cancellationToken:token),
                    (_,_) => Task.CompletedTask,cts.Token);
                Assert.Fail("Cancellation must interrupt the encode.");
            }
            catch (OperationCanceledException) { }
            Assert.AreEqual(0,Directory.GetFiles(root).Length);
        }
        finally { Directory.Delete(root,true); }
    }

    [TestMethod]
    public async Task Artwork_UnsupportedCodecIsAnAlbumFailure()
    {
        var service = new ArtworkExtractionService(new FfmpegTools("unused", "unused"));
        var album = new ArtworkPlannedAlbum("in.flac", "Artist", "Album", "Artist - Album", new EmbeddedArtworkInfo(1,"unknown",100,100,true,true));
        var result = await service.ExtractAsync(album, Path.GetTempPath(), new ArtworkExtractionOptions(ArtworkOutputFormat.Original));
        Assert.IsNotNull(result.Error);
    }
}
