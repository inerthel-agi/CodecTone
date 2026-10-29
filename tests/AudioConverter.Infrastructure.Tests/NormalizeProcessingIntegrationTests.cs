using AudioConverter.Core.Loudness;
using AudioConverter.Core.Models;
using AudioConverter.Infrastructure.Ffmpeg;
using AudioConverter.Infrastructure.Normalize;
using AudioConverter.Infrastructure.Remix;

namespace AudioConverter.Infrastructure.Tests;

[TestClass]
public sealed class NormalizeProcessingIntegrationTests
{
    [TestMethod]
    [TestCategory("RequiresFfmpeg")]
    public async Task TrackPass_ReachesTargetLoudnessAndKeepsTheCeiling()
    {
        var tools = FindTools();
        var root = NewRoot();
        try
        {
            var input = Path.Combine(root, "tone.wav");
            var output = Path.Combine(root, "out", "tone.wav");
            await GenerateAsync(tools, input, "sine=frequency=440:duration=8", "pcm_s16le");

            var service = new NormalizeProcessingService(tools);
            var probe = await new FfprobeService(tools.FfprobePath).ProbeAsync(input);
            var measured = await service.MeasureAsync(input, -14, -1);

            await service.ApplyTrackAsync(
                new NormalizeOutput(input, output, NormalizeEncoding.FromProbe(AudioFormat.Wav, probe, true), probe.DurationSeconds, true),
                -14,
                -1,
                measured);

            var result = await service.MeasureAsync(output, -14, -1);
            Assert.AreEqual(-14, result.InputIntegrated, 0.5, "Output loudness should reach the target.");
            Assert.IsTrue(result.InputTruePeak <= -0.9, $"True peak {result.InputTruePeak} should stay under the ceiling.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    [TestCategory("RequiresFfmpeg")]
    public async Task TrackPass_Mp3KeepsFormatAndSourceBitrate()
    {
        var tools = FindTools();
        var root = NewRoot();
        try
        {
            var input = Path.Combine(root, "tone.mp3");
            var output = Path.Combine(root, "out", "tone.mp3");
            await GenerateAsync(tools, input, "sine=frequency=440:duration=6", "libmp3lame", "-b:a", "128k");

            var service = new NormalizeProcessingService(tools);
            var probe = await new FfprobeService(tools.FfprobePath).ProbeAsync(input);
            var measured = await service.MeasureAsync(input, -14, -1);

            await service.ApplyTrackAsync(
                new NormalizeOutput(input, output, NormalizeEncoding.FromProbe(AudioFormat.Mp3, probe, true), probe.DurationSeconds, true),
                -14,
                -1,
                measured);

            var result = await new FfprobeService(tools.FfprobePath).ProbeAsync(output);
            Assert.AreEqual("mp3", result.CodecName);
            Assert.IsNotNull(result.AudioBitrateKbps);
            Assert.IsTrue(Math.Abs(result.AudioBitrateKbps!.Value - 128) <= 8, $"Bitrate {result.AudioBitrateKbps} should stay near the source 128 kbps.");
            Assert.AreEqual(-14, (await service.MeasureAsync(output, -14, -1)).InputIntegrated, 0.5);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    [TestCategory("RequiresFfmpeg")]
    public async Task AlbumPass_AppliesTheSameGainToEveryFile()
    {
        var tools = FindTools();
        var root = NewRoot();
        try
        {
            var loud = Path.Combine(root, "loud.wav");
            var quiet = Path.Combine(root, "quiet.wav");
            await GenerateAsync(tools, loud, "sine=frequency=440:duration=6", "pcm_s16le");
            await GenerateAsync(tools, quiet, "sine=frequency=330:duration=6", "pcm_s16le", "-af", "volume=0.3");

            var service = new NormalizeProcessingService(tools);
            var probeService = new FfprobeService(tools.FfprobePath);
            var inputs = new List<(string Path, ProbeInfo Probe, LoudnessMeasurements Loudness)>();
            foreach (var path in new[] { loud, quiet })
            {
                var probe = await probeService.ProbeAsync(path);
                inputs.Add((path, probe, await service.MeasureAsync(path, -14, -1)));
            }

            var plan = LoudnessPlanner.Create(
                inputs.Select(item => new LoudnessTrack(item.Path, Path.GetFileName(item.Path), item.Probe.DurationSeconds, item.Loudness.InputIntegrated, item.Loudness.InputTruePeak)).ToArray(),
                LoudnessMode.Album,
                -14,
                -1);
            var gain = plan.AlbumGainDb!.Value;
            Assert.IsFalse(plan.GainCapped, "Test tones have headroom, so the target should not be capped.");

            for (var index = 0; index < inputs.Count; index++)
            {
                var item = inputs[index];
                var output = Path.Combine(root, "out", Path.GetFileName(item.Path));
                await service.ApplyAlbumAsync(
                    new NormalizeOutput(item.Path, output, NormalizeEncoding.FromProbe(AudioFormat.Wav, item.Probe, true), item.Probe.DurationSeconds, true),
                    gain,
                    -1);

                var result = await service.MeasureAsync(output, -14, -1);
                Assert.AreEqual(gain, result.InputIntegrated - item.Loudness.InputIntegrated, 0.3, $"Gain applied to {Path.GetFileName(item.Path)}.");
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static FfmpegTools FindTools()
    {
        try { return FfmpegLocator.Find(); }
        catch (FfmpegDependencyException) { Assert.Inconclusive("FFmpeg is not available for integration testing."); throw; }
    }

    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"codectone-normalize-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "out"));
        return root;
    }

    private static async Task GenerateAsync(FfmpegTools tools, string output, string source, string codec, params string[] extra)
    {
        string[] arguments =
        [
            "-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", source,
            .. extra,
            "-c:a", codec,
            output,
        ];
        await new FfmpegProcessRunner().RunAsync(tools.FfmpegPath, arguments, 1);
    }
}
