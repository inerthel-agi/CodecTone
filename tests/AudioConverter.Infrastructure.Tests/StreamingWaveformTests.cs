using AudioConverter.Infrastructure.Audio;
using AudioConverter.Infrastructure.Ffmpeg;
namespace AudioConverter.Infrastructure.Tests;
[TestClass]
public sealed class StreamingWaveformTests
{
    [TestMethod]
    public async Task Stream_HandlesOddReadsAndNegativeFullScale()
    {
        using var stream = new OddStream([0,128,0,0,255,127,0,0]);
        var peaks = await WaveformAggregator.AggregateAsync(stream, 2, 4);
        Assert.AreEqual(1, peaks[0]);
        Assert.AreEqual(32767d / 32768, peaks[1]);
    }

    [TestMethod]
    [TestCategory("RequiresFfmpeg")]
    public async Task Waveform_PreservesHighFrequencyAndAntiphaseChannels()
    {
        FfmpegTools tools;
        try { tools = FfmpegLocator.Find(); }
        catch (FfmpegDependencyException) { Assert.Inconclusive("FFmpeg is not available for integration testing."); return; }
        var path = Path.Combine(Path.GetTempPath(), $"waveform-{Guid.NewGuid():N}.wav");
        try
        {
            await new FfmpegProcessRunner().RunAsync(tools.FfmpegPath,
                ["-v","error","-y","-f","lavfi","-i","aevalsrc=0.8*sin(2*PI*12000*t)|-0.8*sin(2*PI*12000*t):s=44100:d=1",path], 1);
            var peaks = await new AudioProcessingService(tools).ExtractWaveformAsync(path, 100, 1);
            Assert.IsTrue(peaks.All(p => p > 0.7), "High-frequency or opposite-phase peaks were lost.");
        }
        finally { File.Delete(path); }
    }

    private sealed class OddStream(byte[] data) : MemoryStream(data)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) =>
            base.ReadAsync(buffer[..Math.Min(3, buffer.Length)], token);
    }
}
