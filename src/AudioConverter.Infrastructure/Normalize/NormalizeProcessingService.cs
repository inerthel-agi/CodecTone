using System.Diagnostics;
using AudioConverter.Infrastructure.Ffmpeg;
using AudioConverter.Infrastructure.Remix;

namespace AudioConverter.Infrastructure.Normalize;

/// <summary>One output file. Duration drives progress and the probe check on the staged file.</summary>
public sealed record NormalizeOutput(string Input, string Output, NormalizeEncoding Encoding, double DurationSeconds, bool Overwrite);

/// <summary>Measures files with loudnorm and writes normalized copies through a staged, probe-checked output.</summary>
public sealed class NormalizeProcessingService(FfmpegTools tools)
{
    private readonly FfmpegProcessRunner runner = new();
    private readonly FfprobeService probe = new(tools.FfprobePath);

    public async Task<LoudnessMeasurements> MeasureAsync(
        string inputPath,
        double targetLufs,
        double ceilingDbtp,
        CancellationToken cancellationToken = default)
    {
        var stderr = await CaptureAsync(NormalizeCommandBuilder.BuildMeasure(inputPath, targetLufs, ceilingDbtp), cancellationToken);
        return LoudnessParser.Parse(stderr);
    }

    /// <summary>Per-track output. <paramref name="measured"/> must come from <see cref="MeasureAsync"/> with the same target and ceiling.</summary>
    public Task ApplyTrackAsync(
        NormalizeOutput job,
        double targetLufs,
        double ceilingDbtp,
        LoudnessMeasurements measured,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default) =>
        RunTransactionalAsync(
            job,
            progress,
            (staged, token) => runner.RunAsync(
                tools.FfmpegPath,
                NormalizeCommandBuilder.BuildTrackPass(job.Input, staged, job.Encoding, targetLufs, ceilingDbtp, measured),
                job.DurationSeconds,
                progress,
                token),
            cancellationToken);

    public Task ApplyAlbumAsync(
        NormalizeOutput job,
        double gainDb,
        double ceilingDbtp,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default) =>
        RunTransactionalAsync(
            job,
            progress,
            (staged, token) => runner.RunAsync(
                tools.FfmpegPath,
                NormalizeCommandBuilder.BuildAlbumPass(job.Input, staged, job.Encoding, gainDb, ceilingDbtp),
                job.DurationSeconds,
                progress,
                token),
            cancellationToken);

    private async Task RunTransactionalAsync(
        NormalizeOutput job,
        IProgress<double>? progress,
        Func<string, CancellationToken, Task> produce,
        CancellationToken cancellationToken)
    {
        await TransactionalAudioOutput.RunAsync(
            job.Input,
            job.Output,
            job.Overwrite,
            produce,
            async (staged, token) =>
            {
                var info = await probe.ProbeAsync(staged, token);
                var minimumDuration = job.DurationSeconds <= 0
                    ? 0.001
                    : Math.Max(0.001, job.DurationSeconds - Math.Max(0.05, job.DurationSeconds * 0.02));
                if (info.DurationSeconds < minimumDuration)
                    throw new InvalidDataException("Encoded output did not contain valid audio.");
            },
            cancellationToken);
        progress?.Report(1);
    }

    private async Task<string> CaptureAsync(string[] arguments, CancellationToken cancellationToken)
    {
        var info = FfmpegProcessRunner.CreateStartInfo(tools.FfmpegPath, arguments, redirectOutput: true);
        using var process = new Process { StartInfo = info };
        try
        {
            if (!process.Start()) throw new FfmpegExecutionException("FFmpeg loudness analysis could not start.");
        }
        catch (Exception error) when (error is not FfmpegExecutionException)
        {
            throw new FfmpegExecutionException($"FFmpeg loudness analysis could not start: {error.Message}");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await FfmpegProcessRunner.WaitForExitOrKillAsync(process, cancellationToken);
        _ = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0) throw new FfmpegExecutionException($"FFmpeg loudness analysis failed: {stderr.Trim()}");
        return stderr;
    }
}
